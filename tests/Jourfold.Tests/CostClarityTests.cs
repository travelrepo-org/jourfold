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
    public async Task TheConverterConvertsIntoTheTripsCurrencyAndOffersNoCurrencyForTheRatesAlone()
    {
        var session = await Phone();
        try
        {
            await session.SetTripCurrencyAsync("USD"); session.CloseOverlay();
            // The trip's currency first, then its costs' currencies and the phone's region's; the euro only as one of them.
            var expected = TripSummaries.Costs(session.Trip!).Select(c => c.Currency).Prepend("USD").Append(System.Globalization.RegionInfo.CurrentRegion.ISOCurrencySymbol).Distinct();
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
