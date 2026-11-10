using TravelRepo.Core;

namespace Jourfold.UI;

/// <summary>Words for costs shared by the computer, the phone and the tablet: categories and what an upgrade changes.</summary>
public static class CostTexts
{
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
