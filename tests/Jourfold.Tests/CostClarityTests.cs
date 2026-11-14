using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Jourfold.Mobile;
using Jourfold.Mobile.Views;
using Jourfold.UI;
using NodaTime;
using TravelRepo.Core;
using TravelRepo.Git;
using Xunit;
namespace Jourfold.Tests;

/// <summary>What costs come to in the trip's currency, the trip's currency on the phone, and the converter's words.</summary>
public sealed class CostClarityTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "jourfold-clarity-" + Guid.NewGuid());
    public void Dispose() => TestFiles.Delete(root);
    private static readonly DateTimeZone Tokyo = DateTimeZoneProviders.Tzdb["Asia/Tokyo"];
    private static readonly ExchangeRates Friday = new(new DateOnly(2027, 5, 14), new Dictionary<string, decimal> { ["JPY"] = 160m, ["USD"] = 1.1m, ["CHF"] = 0.95m });

    private async Task<MobileSession> Phone()
    {
        Directory.CreateDirectory(root); Friday.Save(Path.Combine(root, "rates.json"));
        var session = new MobileSession(new MobileHost(root, new GitCliBackend(), new FakeClock(Instant.FromUtc(2027, 5, 16, 1, 40)), () => Tokyo, "en", new MemorySecrets()));
        session.Settings.Values["rates.online"] = "on";
        await session.OpenAsync(await session.CreateSampleAsync(new LocalDate(2027, 5, 14)));
        return session;
    }

    private static string[] Texts(Control page) => [.. page.GetLogicalDescendants().OfType<TextBlock>().Select(t => t.Text ?? "")];

    [AvaloniaFact]
    public async Task AnAmountInAnotherCurrencyShowsWhatItComesToInTheTrips()
    {
        var session = await Phone();
        try
        {
            var trip = session.Trip!; var s = session.Strings;
            // The ryokan is paid at check-in in yen, nothing was charged yet: estimated with Friday's rates.
            var ryokan = trip.Entities.Values.First(e => e.Type == "expense" && Expenses.ChargeOf(e) is null && TripSummaries.Money(e.Data["amount"])?.Currency == "JPY");
            var yen = TripSummaries.Money(ryokan.Data["amount"])!.Amount;
            var expected = "≈ " + Formats.Money(Math.Round(yen / 160m, 2), "EUR") + " at the rates of " + Formats.Date(new LocalDate(2027, 5, 14));
            Assert.Equal(expected, CostTexts.Estimate(s, ryokan, "EUR", Friday));
            // What the card charged is exact and shown as such; in the trip's currency, or without rates, nothing.
            var charged = trip.Entities.Values.First(e => e.Type == "expense" && Expenses.ChargeOf(e) is not null);
            Assert.Null(CostTexts.Estimate(s, charged, "EUR", Friday));
            Assert.Null(CostTexts.Estimate(s, ryokan, "JPY", Friday));
            Assert.Null(CostTexts.Estimate(s, ryokan, "EUR", null));

            // The phone's details of the expense show it under the amount.
            session.ShowDetails(ryokan.Id);
            Assert.Contains(expected, Texts(DetailView.Body(session, session.Trip!.Find(ryokan.Id)!)));
        }
        finally { session.Dispose(); }
    }

    [AvaloniaFact]
    public async Task PaidPaymentsAreMarkedGreenWhenExactAndYellowWhenEstimated()
    {
        var session = await Phone();
        try
        {
            var trip = session.Trip!;
            Entity Expense(Func<Entity, bool> where) => trip.Entities.Values.First(e => e.Type == "expense" && where(e));
            string? Currency(Entity e) => TripSummaries.Money(e.Data["amount"])?.Currency;
            // Paid in yen and nothing charged: only estimated in euros.
            var uncharged = Expense(e => Currency(e) == "JPY" && !Expenses.IsPlanned(e) && Expenses.ChargeOf(e) is null);
            Assert.True(CostTexts.NeedsCharge(uncharged, "EUR"));
            // Charged, still to pay, or in the trip's currency: no mark.
            Assert.False(CostTexts.NeedsCharge(Expense(e => Expenses.ChargeOf(e) is not null), "EUR"));
            Assert.False(CostTexts.NeedsCharge(Expense(e => Currency(e) == "JPY" && Expenses.IsPlanned(e)), "EUR"));
            Assert.False(CostTexts.NeedsCharge(Expense(e => Currency(e) == "EUR"), "EUR"));
            Assert.False(CostTexts.NeedsCharge(uncharged, "JPY"));

            // The Costs page marks every paid payment: yellow while estimated, green once exact, with words for the
            // tooltip and screen readers; payments still to pay have no mark.
            session.ShowTab(MobileTab.More); session.OpenPage("costs");
            var names = MorePages.Build(session, WidthClass.Phone).GetLogicalDescendants().OfType<Jourfold.UI.Icon>()
                .Where(i => i.Kind == "credit-card").Select(Avalonia.Automation.AutomationProperties.GetName).ToArray();
            var paid = trip.Entities.Values.Where(e => e.Type == "expense" && !Expenses.IsPlanned(e)).ToArray();
            Assert.Equal(paid.Length, names.Length);
            Assert.Equal(paid.Count(e => CostTexts.NeedsCharge(e, "EUR")), names.Count(n => n!.StartsWith("No charged amount in EUR yet", StringComparison.Ordinal)));
            Assert.Contains(names, n => n!.StartsWith("Exact: the card charged EUR", StringComparison.Ordinal));
            Assert.Contains("Exact: paid in EUR, the trip currency.", names);
            Assert.Null(CostTexts.ChargeMark(session.Strings, Expense(e => Expenses.IsPlanned(e)), "EUR"));
        }
        finally { session.Dispose(); }
    }

    [AvaloniaFact]
    public async Task CashCountsAtTheRateItCostOnTheComputer()
    {
        Directory.CreateDirectory(root);
        using var store = new Jourfold.Infrastructure.LocalStore(Path.Combine(root, "local"));
        using var m = new Jourfold.Desktop.MainViewModel(store, new GitCliBackend(), new Jourfold.Infrastructure.OsSecretStore(), new TestInteraction());
        var path = Path.Combine(root, "computer");
        await Jourfold.Application.SampleTrip.CreateAsync(path, new GitCliBackend(), new LocalDate(2027, 5, 14), "Alex", "alex@example.invalid", Path.Combine(root, "recovery"));
        await m.OpenAsync(path);
        var tea = m.Trip!.Entities.Values.Single(e => e.Type == "expense" && e.Title == "Tea ceremony");
        // Without rates nothing can be offered, so the rate is asked for first.
        await m.PayWithCashAsync(tea.Id);
        Assert.Contains(tea.Id, m.CashPending); Assert.False(Expenses.IsCash(m.Trip!.Find(tea.Id)!));
        // Typed as on the cash machine's receipt: 1 EUR = 160.5 JPY.
        var form = CostTexts.CashRateForm.For("JPY", "EUR", 1m / 160m);
        Assert.Equal(("EUR", "JPY"), (form.Left, form.Right));
        Assert.False(await m.SetCashRateAsync(tea.Id, "none", form));
        Assert.True(await m.SetCashRateAsync(tea.Id, "160,5", form));
        var paid = m.Trip!.Find(tea.Id)!;
        Assert.True(Expenses.IsCash(paid)); Assert.Empty(m.CashPending);
        Assert.Equal(Math.Round(4400m / 160.5m, 2), Expenses.ChargeOf(paid)!.Value);
        Assert.Equal("1 EUR = 160.5 JPY", form.Text(Expenses.ChargeOf(paid)!.Rate));
        // Exact now: a green bank note, and the next cash in yen is offered the same rate.
        var mark = Assert.IsType<Jourfold.UI.Icon>(CostTexts.ChargeMark(m.Strings, paid, "EUR"));
        Assert.Equal("banknote", mark.Kind);
        Assert.StartsWith("Exact: paid in cash, counted as", Avalonia.Automation.AutomationProperties.GetName(mark));
        Assert.Equal(Expenses.ChargeOf(paid)!.Rate, CostTexts.SuggestedCashRate(m.Trip!, "JPY", "EUR", null));
        Assert.Equal("cash, counted as " + Formats.Money(Math.Round(4400m / 160.5m, 2), "EUR"), CostTexts.ChargedText(m.Strings, paid));
        // Paid by card after all: the cash rate goes.
        await m.PayWithCardAsync(tea.Id);
        Assert.False(Expenses.IsCash(m.Trip!.Find(tea.Id)!)); Assert.Null(Expenses.ChargeOf(m.Trip!.Find(tea.Id)!));
    }

    [AvaloniaFact]
    public async Task TheCalculatorWorksOutTheRateOfAWithdrawalAndItsFees()
    {
        var session = await Phone();
        try
        {
            var s = session.Strings;
            // 600 NZD for 318.40 EUR with fees; the reference rate says 1 EUR = 1.9 NZD.
            var rates = new ExchangeRates(new DateOnly(2027, 5, 14), new Dictionary<string, decimal> { ["NZD"] = 1.9m });
            var rate = CostTexts.WithdrawalRate(600m, 318.40m)!.Value;
            Assert.Equal("1 EUR = 1.8844 NZD", CostTexts.CashRateForm.For("NZD", "EUR", rate).Text(rate));
            Assert.Equal(0.8m, CostTexts.Markup(rate, 1m / 1.9m));
            Assert.Equal("0.8 % more than the reference rate of " + Formats.Date(new LocalDate(2027, 5, 14)), CostTexts.MarkupText(s, rate, rates, "NZD", "EUR"));
            Assert.Null(CostTexts.WithdrawalRate(600m, null)); Assert.Null(CostTexts.WithdrawalRate(0m, 10m));
            // A rate is read with its decimals, not like an amount where "1.884" could be a thousand and more.
            var nzd = CostTexts.CashRateForm.For("NZD", "EUR", rate);
            Assert.Equal(Math.Round(1m / 1.8844m, 10), Math.Round(nzd.Parse("1.8844")!.Value, 10));
            Assert.Null(nzd.Parse("none")); Assert.Null(nzd.Parse("0"));
            foreach (var (typed, read) in new[] { ("160,5", 160.5m), ("160.5", 160.5m), ("1.8844", 1.8844m), ("1,8844", 1.8844m), ("1,234.5", 1234.5m), ("1.234,5", 1234.5m), ("25,000", 25000m), ("1.884", 1.884m), ("160", 160m) })
                Assert.Equal(read, CostTexts.ReadRate(typed));

            // The calculator itself: typed amounts, the rate, the fees, and the rate handed over.
            decimal? used = null;
            var calculator = RateCalculator.Build(s, "NZD", "EUR", rates, r => used = r);
            var window = new Window { Content = calculator }; window.Show();
            try
            {
                Probe.All<TextBox>(calculator).Single(t => Avalonia.Automation.AutomationProperties.GetName(t) == "Cash received (NZD)").Text = "600";
                Probe.All<TextBox>(calculator).Single(t => Avalonia.Automation.AutomationProperties.GetName(t) == "Charged to the account, fees included (EUR)").Text = "318,40";
                Probe.Layout();
                Assert.Contains("1 EUR = 1.8844 NZD", Texts(calculator));
                Probe.Click(calculator, "Use this rate");
                Assert.Equal(rate, used);
            }
            finally { window.Close(); }

            // Offered: the last cash payment's rate, otherwise the reference rate, saying which.
            var trip = session.Trip!;
            var offer = CostTexts.OfferCashRate(trip, "JPY", "EUR", Friday)!;
            Assert.Equal(("Reference rate of " + Formats.Date(new LocalDate(2027, 5, 14))), offer.Source(s, "JPY"));
            var tea = trip.Entities.Values.Single(e => e.Type == "expense" && e.Title == "Tea ceremony");
            await session.SaveDetailsAsync([new TravelRepo.Repository.EntityEdit(tea.Id, Expenses.WithCash(tea, "EUR", 1m / 158m, new LocalDate(2027, 5, 15)))]);
            var last = CostTexts.OfferCashRate(session.Trip!, "JPY", "EUR", Friday)!;
            Assert.Equal(("Rate of the last cash payment in JPY", Math.Round(1m / 158m, 8)), (last.Source(s, "JPY"), Math.Round(last.Rate, 8)));
            // Editing that payment itself, its own rate is not offered back.
            Assert.NotNull(CostTexts.OfferCashRate(session.Trip!, "JPY", "EUR", Friday, tea.Id)!.ReferenceDay);
        }
        finally { session.Dispose(); }
    }

    [AvaloniaFact]
    public void ADraftPaidWithCashIsAddedAtItsRate()
    {
        var trip = new TripSnapshot(TripCurrency.With(Entity.CreateTrip("Wanaka", "en", "Pacific/Auckland"), "EUR"), new Dictionary<Guid, Entity>());
        var (main, _) = Jourfold.Application.QuickAdd.Build(new Jourfold.Application.QuickAddDraft("expense", "Market") { Amount = 60m, Currency = "NZD", ChargedCurrency = "EUR", CashRate = 318.40m / 600m, PaidOn = new LocalDate(2027, 5, 14) }, trip);
        Assert.True(Expenses.IsCash(main));
        Assert.Equal(31.84m, Expenses.ChargeOf(main)!.Value);
    }

    [Theory]
    [InlineData("Pacific/Auckland", "NZD")]
    [InlineData("Asia/Shanghai", "CNY")]
    [InlineData("Europe/Berlin", "EUR")]
    [InlineData("Asia/Tokyo", "JPY")]
    [InlineData("America/New_York", "USD")]
    [InlineData("Asia/Calcutta", "INR")]
    [InlineData("Etc/UTC", null)]
    public void TheLocalCurrencyComesFromThePhonesTimeZone(string zone, string? expected) =>
        Assert.Equal(expected, Jourfold.UI.CurrencyCodes.OfZone(DateTimeZoneProviders.Tzdb[zone]));

    [AvaloniaFact]
    public async Task TheCurrenciesKeepOneOrderTripLocalCostsRegionThenRecent()
    {
        // A trip from Germany to New Zealand with a layover in China, on a phone in Auckland.
        var folder = Path.Combine(root, "nz"); Directory.CreateDirectory(folder); Friday.Save(Path.Combine(folder, "rates.json"));
        var session = new MobileSession(new MobileHost(folder, new GitCliBackend(), new FakeClock(Instant.FromUtc(2027, 5, 16, 1, 40)), () => DateTimeZoneProviders.Tzdb["Pacific/Auckland"], "en", new MemorySecrets()));
        try
        {
            await session.OpenAsync(await session.CreateSampleAsync(new LocalDate(2027, 5, 14)));
            await session.SetTripCurrencyAsync("EUR"); session.CloseOverlay();
            var trip = session.Trip!;
            var edits = trip.Entities.Values.Where(e => e.Type == "expense").Select((e, i) =>
            {
                var copy = e.Copy(); copy.Data["amount"] = new System.Text.Json.Nodes.JsonObject { ["value"] = "10", ["currency"] = i % 2 == 0 ? "CNY" : "NZD" };
                return new TravelRepo.Repository.EntityEdit(copy.Id, copy);
            }).ToArray();
            await session.SaveDetailsAsync(edits);
            var region = System.Globalization.RegionInfo.CurrentRegion.ISOCurrencySymbol;
            Assert.Equal(new[] { "EUR", "NZD", "CNY", region }.Distinct(), session.TripCurrencies());
            // One chosen under Other comes after them, with an x.
            session.RememberCurrency("THB");
            Assert.Equal(["THB"], session.RecentShown);
            // A payment in baht moves it up among the trip's currencies, without an x.
            var market = session.Trip!.Entities.Values.First(e => e.Type == "expense").Copy();
            market.Data["amount"] = new System.Text.Json.Nodes.JsonObject { ["value"] = "300", ["currency"] = "THB" };
            await session.SaveDetailsAsync([new TravelRepo.Repository.EntityEdit(market.Id, market)]);
            Assert.Contains("THB", session.TripCurrencies()); Assert.Empty(session.RecentShown);
            // Four more chosen meanwhile do not push it out, since it was not shown.
            foreach (var code in new[] { "CHF", "GBP", "SEK", "DKK" }) session.RememberCurrency(code);
            Assert.Contains("THB", session.RecentCurrencies);
            // Without the payment it is back with its x.
            market.Data["amount"] = new System.Text.Json.Nodes.JsonObject { ["value"] = "10", ["currency"] = "NZD" };
            await session.SaveDetailsAsync([new TravelRepo.Repository.EntityEdit(market.Id, market)]);
            Assert.DoesNotContain("THB", session.TripCurrencies()); Assert.Contains("THB", session.RecentShown);
            // The x takes it off.
            session.ForgetConverterCurrency("THB");
            Assert.DoesNotContain("THB", session.RecentShown);
        }
        finally { session.Dispose(); }
    }

    [AvaloniaFact]
    public async Task TheConverterConvertsIntoTheTripsCurrencyAndOffersNoCurrencyForTheRatesAlone()
    {
        var session = await Phone();
        try
        {
            await session.SetTripCurrencyAsync("USD"); session.CloseOverlay();
            // The trip's currency first, then the local one where the phone is (yen in Tokyo), its costs' currencies and
            // the phone's region's; the euro only as one of them.
            var expected = TripSummaries.Costs(session.Trip!).Select(c => c.Currency).Prepend("JPY").Prepend("USD").Append(System.Globalization.RegionInfo.CurrentRegion.ISOCurrencySymbol).Distinct();
            Assert.Equal(expected, session.TripCurrencies());
            // Into the trip's currency, from the first other one.
            session.ShowTab(MobileTab.More); session.ShowPage("converter");
            var overlines = MorePages.Build(session, WidthClass.Phone).GetLogicalDescendants().OfType<TextBlock>().Where(t => t.Classes.Contains("overline")).Select(t => t.Text).ToArray();
            Assert.Equal(["USD"], overlines.Where(t => t == "USD"));
            Assert.Equal("USD", overlines[1]);
        }
        finally { session.Dispose(); }
    }

    [AvaloniaFact]
    public async Task ThePhoneSetsTheTripsCurrencyAndOffersToConvertTheBudgets()
    {
        var session = await Phone();
        try
        {
            session.OpenPage("trip");
            Assert.Contains("Trip currency", Texts(MorePages.Build(session, WidthClass.Phone)));
            await session.SetTripCurrencyAsync("JPY");
            Assert.Equal("JPY", TripCurrency.Stored(session.Trip!));
            // The budgets in euros stay until the travellers agree; the question lists them.
            var question = Assert.IsType<Question>(session.Asking);
            Assert.Equal("Keep them as they are", question.Decline);
            Assert.Contains("Trip budget", question.Detail);
            Assert.All(session.Trip!.Entities.Values.Where(e => e.Type == "budget"), b => Assert.Equal("EUR", TripSummaries.Money(b.Data["amount"])!.Currency));
            await session.AnswerAsync(true);
            Assert.All(session.Trip!.Entities.Values.Where(e => e.Type == "budget"), b => Assert.Equal("JPY", TripSummaries.Money(b.Data["amount"])!.Currency));
            Assert.Equal("560000", TripSummaries.Money(session.Trip!.Entities.Values.Single(e => e.Title == "Trip budget").Data["amount"])!.Amount.ToString("0", System.Globalization.CultureInfo.InvariantCulture));
            // More says which currency the trip counts in.
            session.ShowTab(MobileTab.More);
            Assert.Contains(Texts(MorePages.Build(session, WidthClass.Phone)), t => t.EndsWith(" · JPY", StringComparison.Ordinal));
        }
        finally { session.Dispose(); }
    }

    [AvaloniaFact]
    public async Task TheConverterKeepsOtherCurrenciesAndSaysWhyTheRatesAreFridays()
    {
        var session = await Phone();
        try
        {
            session.ShowTab(MobileTab.More); session.ShowPage("converter");
            // Chosen under Other, Swiss francs stay to tap after switching back.
            session.ChooseConverterCurrency("converter.from", "CHF");
            session.ChooseConverterCurrency("converter.from", "JPY");
            Assert.Equal(["CHF"], session.RecentCurrencies);
            // A long press takes it off again.
            session.ForgetConverterCurrency("CHF"); Assert.Empty(session.RecentCurrencies);
            session.ChooseConverterCurrency("converter.from", "CHF"); session.ChooseConverterCurrency("converter.from", "JPY");
            var page = MorePages.Build(session, WidthClass.Phone);
            Assert.Contains(page.GetLogicalDescendants().OfType<Avalonia.Controls.Primitives.ToggleButton>().Select(b => b.Content as string)
                .Concat(page.GetLogicalDescendants().OfType<Button>().Select(b => Avalonia.Automation.AutomationProperties.GetName(b))), n => n == "CHF");
            // Fetched on Sunday, the newest rates are Friday's, and the page says why.
            Assert.True(CostTexts.RatesOfEarlierDay(Friday, new DateTimeOffset(2027, 5, 16, 10, 0, 0, TimeSpan.Zero)));
            Assert.False(CostTexts.RatesOfEarlierDay(Friday, new DateTimeOffset(2027, 5, 14, 16, 30, 0, TimeSpan.Zero)));
            Assert.Contains("European Central Bank reference rates of Fri 14 May 2027. For orientation; card and cash rates differ.", Texts(page));
        }
        finally { session.Dispose(); }
    }
}
