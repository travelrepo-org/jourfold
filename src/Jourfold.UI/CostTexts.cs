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

    /// <summary>
    /// What an expense in another currency comes to in the trip's, estimated with reference rates because no charged
    /// amount was entered: "≈ €60.85 at the rates of 8 Nov 2026". Null when it is in the trip's currency, when what the
    /// card charged is known (shown as such), or when there are no rates to estimate with.
    /// </summary>
    public static string? Estimate(Localization s, Entity expense, string tripCurrency, ExchangeRates? rates)
    {
        if (rates is null || TripSummaries.Money(expense.Data["amount"]) is not { } money) return null;
        var (amount, currency, how) = Expenses.Convert(expense, money.Amount, money.Currency, tripCurrency, rates);
        return how == Conversion.Reference ? string.Format(CultureInfo.CurrentCulture, s["EstimateAtRates"], Formats.Money(amount, currency), Formats.Date(NodaTime.LocalDate.FromDateOnly(rates.Date))) : null;
    }

    /// <summary>
    /// Whether the newest rates are of an earlier day than the one they were fetched on, as on a weekend or before the
    /// European Central Bank publishes the day's rates around 16:00 in Frankfurt. The converter then says why.
    /// </summary>
    public static bool RatesOfEarlierDay(ExchangeRates rates, DateTimeOffset fetched) =>
        NodaTime.Instant.FromDateTimeOffset(fetched).InZone(NodaTime.DateTimeZoneProviders.Tzdb["Europe/Berlin"]).Date > NodaTime.LocalDate.FromDateOnly(rates.Date);

    /// <summary>
    /// Whether a paid expense is in another currency than the trip's and has no charged amount, so what it comes to in
    /// the trip's currency is only estimated. Payments still to pay are not counted: nothing can be charged yet.
    /// </summary>
    public static bool NeedsCharge(Entity expense, string tripCurrency) =>
        expense.Type == "expense" && !Expenses.IsPlanned(expense) && Expenses.ChargeOf(expense) is null
        && TripSummaries.Money(expense.Data["amount"]) is { Amount: not 0 } money && money.Currency != tripCurrency;

    /// <summary>
    /// What an expense comes to in another currency because of what was paid: "charged EUR 46.36" for a card, "cash,
    /// counted as EUR 15.00" for cash bought before. Null when neither was entered.
    /// </summary>
    public static string? ChargedText(Localization s, Entity expense) => Expenses.ChargeOf(expense) is not { } charge ? null
        : string.Format(CultureInfo.CurrentCulture, s[charge.Source == Expenses.Cash ? "CashShort" : "ChargedShort"], Formats.Money(charge.Value, charge.Currency));

    /// <summary>
    /// How a cash rate is shown and typed: one unit of the stronger currency first, so the number is above one, as on
    /// a cash machine's receipt: "1 EUR = 160.5 JPY" for yen, "1 GBP = 1.17 EUR" for pounds.
    /// </summary>
    /// <param name="Cash">The currency the cash was in, the expense's.</param>
    /// <param name="Trip">The currency it is counted in.</param>
    /// <param name="TripFirst">Whether the trip's currency is the stronger one and comes first.</param>
    public sealed record CashRateForm(string Cash, string Trip, bool TripFirst)
    {
        /// <summary>
        /// The form for <paramref name="rate"/>, the units of the trip's currency one unit of cash cost. Without a rate
        /// the trip's currency comes first, as travellers count: how much of the local money one of theirs buys.
        /// </summary>
        public static CashRateForm For(string cash, string trip, decimal? rate) => new(cash, trip, rate is not { } r || r < 1);
        public string Left => TripFirst ? Trip : Cash;
        public string Right => TripFirst ? Cash : Trip;
        /// <summary>The number shown for a stored rate.</summary>
        public decimal Shown(decimal rate) => Math.Round(TripFirst ? 1 / rate : rate, 4, MidpointRounding.AwayFromZero);
        /// <summary>The stored rate for a typed number (see <see cref="ReadRate"/>), or null when it is none above zero.</summary>
        public decimal? Parse(string? text) => ReadRate(text) is { } value && value > 0 ? TripFirst ? 1 / value : value : null;

        /// <summary>"1 EUR = 160.5 JPY".</summary>
        public string Text(decimal rate) => "1 " + Left + " = " + Shown(rate).ToString("0.####", CultureInfo.CurrentCulture) + " " + Right;
    }

    /// <summary>A rate offered for cash, and where it comes from, so the form can say so.</summary>
    /// <param name="Rate">Units of the trip's currency one unit of the cash cost.</param>
    /// <param name="ReferenceDay">The day of the reference rates when it is one, null when it is the last cash payment's.</param>
    public sealed record CashRateOffer(decimal Rate, DateOnly? ReferenceDay)
    {
        /// <summary>"Rate of the last cash payment in NZD" or "Reference rate of 13 Nov 2026".</summary>
        public string Source(Localization s, string cash) => ReferenceDay is { } day
            ? string.Format(CultureInfo.CurrentCulture, s["RateOfReference"], Formats.Date(NodaTime.LocalDate.FromDateOnly(day)))
            : string.Format(CultureInfo.CurrentCulture, s["RateOfLastCash"], cash);
    }

    /// <summary>
    /// A typed rate, such as "160,5", "160.5", "1.8844" or "1,234.5". A rate has more decimals than an amount, so it is
    /// not read like one, where "1.8844" would be a thousand and more: with both a comma and a point, the last is the
    /// decimal separator; one of them once is the decimal separator unless exactly three digits follow, as in "1.884"
    /// or "25.000", which then reads as the app's language writes numbers; one of them several times groups thousands.
    /// </summary>
    public static decimal? ReadRate(string? text)
    {
        var typed = (text ?? "").Trim().Replace(" ", "", StringComparison.Ordinal).Replace("\u00A0", "", StringComparison.Ordinal);
        if (typed.Length == 0) return null;
        int commas = typed.Count(c => c == ','), points = typed.Count(c => c == '.');
        char? decimalMark;
        if (commas > 0 && points > 0) decimalMark = typed.LastIndexOf(',') > typed.LastIndexOf('.') ? ',' : '.';
        else if (commas + points == 0) decimalMark = null;
        else if (commas + points > 1) decimalMark = null;
        else
        {
            var mark = commas == 1 ? ',' : '.';
            var after = typed.Length - typed.IndexOf(mark) - 1;
            decimalMark = after != 3 ? mark : CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator == mark.ToString() ? mark : null;
        }
        var digits = new string(typed.Where(c => c != ',' && c != '.' || c == decimalMark).ToArray());
        if (decimalMark is { } d) digits = digits.Replace(d, '.');
        return decimal.TryParse(digits, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value) ? value : null;
    }

    /// <summary>
    /// The rate to offer for cash in <paramref name="cash"/>: the one the last cash payment in that currency cost,
    /// which usually comes from the same withdrawal, otherwise the reference rate. <paramref name="except"/> leaves
    /// one expense out, such as the one being edited.
    /// </summary>
    public static CashRateOffer? OfferCashRate(TripSnapshot trip, string cash, string tripCurrency, ExchangeRates? rates, Guid? except = null)
    {
        var others = except is { } id ? trip with { Entities = trip.Entities.Where(p => p.Key != id).ToDictionary(p => p.Key, p => p.Value) } : trip;
        if (Expenses.LastCashRate(others, cash, tripCurrency) is { } last) return new(last, null);
        return rates?.Convert(1m, cash, tripCurrency) is { } reference ? new(reference, rates.Date) : null;
    }

    /// <summary>The rate to offer for cash, as <see cref="OfferCashRate"/>, without where it comes from.</summary>
    public static decimal? SuggestedCashRate(TripSnapshot trip, string cash, string tripCurrency, ExchangeRates? rates) =>
        OfferCashRate(trip, cash, tripCurrency, rates)?.Rate;

    /// <summary>
    /// The rate a cash withdrawal cost: <paramref name="debited"/> from the account in the trip's currency, fees
    /// included, for <paramref name="received"/> in cash. Null unless both are above zero.
    /// </summary>
    public static decimal? WithdrawalRate(decimal? received, decimal? debited) => received > 0 && debited > 0 ? debited / received : null;

    /// <summary>
    /// How much more (above zero) or less a rate cost than the reference rate, in percent, such as 2.1 for a cash
    /// machine and bank that took about two percent in fees. Null without a reference.
    /// </summary>
    public static decimal? Markup(decimal rate, decimal? reference) => reference is > 0 ? Math.Round((rate / reference.Value - 1) * 100, 1, MidpointRounding.AwayFromZero) : null;

    /// <summary>"2.1 % more than the reference rate of 13 Nov", "as the reference rate", or null without one.</summary>
    public static string? MarkupText(Localization s, decimal rate, ExchangeRates? rates, string cash, string tripCurrency)
    {
        if (rates?.Convert(1m, cash, tripCurrency) is not { } reference || Markup(rate, reference) is not { } percent) return null;
        var day = Formats.Date(NodaTime.LocalDate.FromDateOnly(rates.Date));
        return Math.Abs(percent) < 0.1m ? string.Format(CultureInfo.CurrentCulture, s["MarkupNone"], day)
            : string.Format(CultureInfo.CurrentCulture, s[percent > 0 ? "MarkupMore" : "MarkupLess"], Math.Abs(percent).ToString("0.0", CultureInfo.CurrentCulture), day);
    }

    /// <summary>
    /// A card symbol next to the amount of a paid expense: green when what it comes to in the trip's currency is exact,
    /// because it was paid in that currency or what the card charged is known, yellow when it is only estimated. Its
    /// tooltip and its name for screen readers say which, so the colour is not the only sign. Null for payments still
    /// to pay and expenses without an amount.
    /// </summary>
    public static Avalonia.Controls.Control? ChargeMark(Localization s, Entity expense, string tripCurrency, double size = 14)
    {
        if (expense.Type != "expense" || Expenses.IsPlanned(expense) || TripSummaries.Money(expense.Data["amount"]) is not { Amount: not 0 } money) return null;
        var estimated = NeedsCharge(expense, tripCurrency);
        var charge = Expenses.ChargeOf(expense);
        var cash = charge?.Source == Expenses.Cash;
        var text = estimated ? string.Format(CultureInfo.CurrentCulture, s["NotChargedYet"], tripCurrency)
            : cash ? string.Format(CultureInfo.CurrentCulture, s["ExactCash"], Formats.Money(charge!.Value, charge.Currency), CashRateForm.For(money.Currency, charge.Currency, charge.Rate).Text(charge.Rate))
            : charge is not null ? string.Format(CultureInfo.CurrentCulture, s["ExactCharged"], Formats.Money(charge.Value, charge.Currency))
            : string.Format(CultureInfo.CurrentCulture, s["ExactInTripCurrency"], money.Currency);
        // Cash shows a bank note, so it is told apart from a card charge at a glance.
        var mark = Ui.Icon(cash ? "banknote" : "credit-card", size, estimated ? "Warning" : "Success");
        mark.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;
        Avalonia.Controls.ToolTip.SetTip(mark, text); Avalonia.Automation.AutomationProperties.SetName(mark, text);
        return mark;
    }

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
