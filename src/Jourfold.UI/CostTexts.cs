using System.Globalization;
using System.Text.Json.Nodes;
using TravelRepo.Core;

namespace Jourfold.UI;

/// <summary>Words for costs shared by the computer, the phone and the tablet: categories and what an upgrade changes.</summary>
public static class CostTexts
{
    /// <summary>The icon of an expense category; categories of the travellers' own share a tag.</summary>
    public static string CategoryIcon(string category) => category switch
    {
        ExpenseCategories.Transport => "route",
        ExpenseCategories.Accommodation => "bed-double",
        ExpenseCategories.Food => "utensils",
        ExpenseCategories.Activity => "ferris-wheel",
        ExpenseCategories.Souvenirs => "shopping-bag",
        ExpenseCategories.Other => "circle-dot",
        _ => "tag"
    };

    /// <summary>The name of a category: standard ones translated, the travellers' own as they typed them.</summary>
    public static string Category(Localization s, string category) => ExpenseCategories.IsStandard(category) ? s["expcat." + category] : category;

    /// <summary>
    /// What a booking costs, for lists: its payments added up per currency they were paid in, or a price an older
    /// version stored when it has none. Null for a booking without either.
    /// </summary>
    public static string? BookingAmount(TripSnapshot trip, Entity booking)
    {
        var amounts = Expenses.ForBooking(trip, booking.Id).Select(e => TripSummaries.Money(e.Data["amount"])).OfType<Money>()
            .GroupBy(x => x.Currency).Select(g => Formats.Money(g.Sum(x => x.Amount), g.Key)).ToArray();
        return amounts.Length > 0 ? string.Join(" + ", amounts) : TripSummaries.Money(booking.Data["price"]) is { } legacy ? Formats.Money(legacy.Amount, legacy.Currency) : null;
    }

    /// <summary>
    /// What was paid in all, for a summary line such as More: in the trip's currency (≈ where reference rates were
    /// used), plus what could not be converted yet in its own currency. Null for a trip without paid expenses.
    /// </summary>
    public static string? PaidSummary(TripSnapshot trip, string currency, ExchangeRates? rates)
    {
        var expenses = trip.Entities.Values.Where(e => e.Type == "expense").ToArray();
        if (!expenses.Any(e => !Expenses.IsPlanned(e))) return null;
        var total = Expenses.Total(expenses, currency, rates);
        var parts = new[] { total.Total != 0 || total.NotConverted.Count == 0 ? (total.Estimated != 0 ? "≈ " : "") + Formats.Money(total.Total, currency) : null }
            .Concat(total.NotConverted.Select(m => Formats.Money(m.Amount, m.Currency))).OfType<string>();
        return string.Join(" + ", parts);
    }

    /// <summary>
    /// Whether an expense fits what was typed into the search of the costs: every word must be found, ignoring case and
    /// accents, in its title, category, who paid or shares it, what it belongs to (and that plan's or booking's place),
    /// or its amount. "alex" finds both a payment at "Alex's Pizza" and every payment Alex paid or shares.
    /// </summary>
    public static bool Matches(Localization s, TripSnapshot trip, Entity expense, string? query)
    {
        var words = (query ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (words.Length == 0) return true;
        var texts = new List<string?> { expense.Title, expense.Data["notes"]?.ToString(), expense.Data["body"]?.ToString() };
        var category = ExpenseCategories.Of(trip, expense);
        texts.Add(category); texts.Add(Category(s, category));
        var split = Expenses.Split(trip, expense);
        texts.AddRange(split.Payers.Concat(split.People).Distinct().Select(p => trip.Find(p)?.Title));
        foreach (var id in (expense.Data["related"] as JsonArray ?? []).Select(r => Guid.TryParse(r?["id"]?.ToString(), out var x) ? x : (Guid?)null).OfType<Guid>())
        {
            if (trip.Find(id) is not { } related) continue;
            texts.Add(related.Title); texts.Add(related.Data["provider"]?.ToString());
            var plans = related.Type == "booking" ? TripCommands.BookedItems(trip, id).Select(trip.Find).OfType<Entity>() : related.Type == "schedule_item" ? [related] : [];
            foreach (var plan in plans) { texts.Add(plan.Title); texts.Add(ScheduleQueries.PrimaryPlace(trip, plan)?.Title); }
        }
        if (TripSummaries.Money(expense.Data["amount"]) is { } money) { texts.Add(Formats.Money(money.Amount, money.Currency)); texts.Add(money.Value); }
        var compare = CultureInfo.CurrentCulture.CompareInfo;
        var found = texts.OfType<string>().Where(t => t.Length > 0).ToArray();
        return words.All(word => found.Any(t => compare.IndexOf(t, word, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0));
    }

    /// <summary>
    /// Where each person stands, one entry per person with all their currencies, such as "gets EUR 119.56 · owes
    /// JPY 52,200", those owed money first. ≈ marks amounts estimated with reference rates.
    /// </summary>
    /// <remarks><c>Direction</c> is 1 when the person only gets money, -1 when they only owe, 0 for both in different currencies.</remarks>
    public static IReadOnlyList<(Guid Person, int Direction, string Text)> Balances(Localization s, IEnumerable<ExpenseBalance> balances) =>
        balances.GroupBy(b => b.Person).Select(g => (Person: g.Key, Direction: g.All(b => b.Net > 0) ? 1 : g.All(b => b.Net < 0) ? -1 : 0,
                Text: string.Join(" · ", g.OrderByDescending(b => b.Net).Select(b => string.Format(CultureInfo.CurrentCulture, s[b.Net > 0 ? "BalanceOwed" : "BalanceOwes"], (b.Estimated ? "≈ " : "") + Formats.Money(Math.Abs(b.Net), b.Currency))))))
            .OrderByDescending(x => x.Direction).ToArray();

    /// <summary>
    /// What upgrading the trip's costs changes, one line per kind of change, such as "Hotel: the price is covered by its
    /// payments and removed". Lines that only report something, such as a price that stays, come last.
    /// </summary>
    public static IReadOnlyList<string> Upgrade(Localization s, CostUpgradePlan plan)
    {
        string M(Money? m) => m is null ? "" : Formats.Money(m.Amount, m.Currency);
        var lines = new List<string>();
        foreach (var group in plan.Changes.GroupBy(c => c.Kind).OrderBy(g => g.Key == CostChangeKind.PriceKept ? 1 : 0).ThenBy(g => g.Key))
        {
            var items = group.ToArray();
            switch (group.Key)
            {
                case CostChangeKind.TripCurrency: lines.Add(string.Format(s["UpgradeCurrency"], items[0].Title)); break;
                case CostChangeKind.SharesNamed: lines.Add(string.Format(s[items.Length == 1 ? "UpgradeSharesOne" : "UpgradeShares"], items.Length)); break;
                case CostChangeKind.PriceCovered: lines.Add(string.Format(s[items.Length == 1 ? "UpgradeCoveredOne" : "UpgradeCovered"], items.Length)); break;
                case CostChangeKind.BudgetScope: lines.Add(string.Format(s[items.Length == 1 ? "UpgradeScopeOne" : "UpgradeScope"], items.Length)); break;
                default:
                    foreach (var c in items)
                        lines.Add(c.Kind switch
                        {
                            CostChangeKind.PriceToPlanned => string.Format(s["UpgradeToPlanned"], c.Title, M(c.Amount)),
                            CostChangeKind.PriceRestPlanned => string.Format(s["UpgradeRest"], c.Title, M(c.Other), M(c.Amount)),
                            CostChangeKind.PriceExceeded => string.Format(s["UpgradeExceeded"], c.Title, M(c.Amount), M(c.Other)),
                            _ => string.Format(s["UpgradeKept"], c.Title, M(c.Amount))
                        });
                    break;
            }
        }
        return lines;
    }
}
