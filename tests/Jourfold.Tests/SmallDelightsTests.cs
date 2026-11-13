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

/// <summary>The small things: the countdown, a word on the first and last day, the trip in numbers, cost per day, the code name.</summary>
public sealed class SmallDelightsTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "jourfold-delights-" + Guid.NewGuid());
    public void Dispose() => TestFiles.Delete(root);
    private static readonly DateTimeZone Tokyo = DateTimeZoneProviders.Tzdb["Asia/Tokyo"];

    /// <summary>The sample trip from 14 to 21 May 2027, opened on a phone whose clock says <paramref name="day"/> at noon in Tokyo.</summary>
    private async Task<MobileSession> Phone(LocalDate day)
    {
        var folder = Path.Combine(root, day.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)); Directory.CreateDirectory(folder);
        // With rates, so the yen in the sample trip can be counted in euros.
        new ExchangeRates(new DateOnly(2027, 5, 14), new Dictionary<string, decimal> { ["JPY"] = 160m }).Save(Path.Combine(folder, "rates.json"));
        var session = new MobileSession(new MobileHost(folder, new GitCliBackend(), new FakeClock(day.At(new LocalTime(12, 0)).InZoneLeniently(Tokyo).ToInstant()), () => Tokyo, "en", new MemorySecrets()));
        session.Settings.Values["rates.online"] = "on";
        await session.OpenAsync(await session.CreateSampleAsync(new LocalDate(2027, 5, 14)));
        return session;
    }

    private static string[] Texts(Control page) => [.. page.GetLogicalDescendants().OfType<TextBlock>().Select(t => t.Text ?? "")];

    [AvaloniaFact]
    public async Task TheCountdownSaysTomorrowAndTheFirstAndLastDayGetAWord()
    {
        using (var before = await Phone(new LocalDate(2027, 5, 13))) Assert.Contains("The trip starts tomorrow. Time to pack!", Texts(TodayView.Build(before, WidthClass.Phone)));
        using (var week = await Phone(new LocalDate(2027, 5, 7))) Assert.Contains("The trip starts in 7 days.", Texts(TodayView.Build(week, WidthClass.Phone)));
        using (var first = await Phone(new LocalDate(2027, 5, 14))) Assert.Contains("Today it begins. Have a wonderful trip!", Texts(TodayView.Build(first, WidthClass.Phone)));
        using (var middle = await Phone(new LocalDate(2027, 5, 17)))
        {
            var texts = Texts(TodayView.Build(middle, WidthClass.Phone));
            Assert.DoesNotContain("Today it begins. Have a wonderful trip!", texts); Assert.DoesNotContain("The last day. Make the most of it!", texts);
        }
        using var last = await Phone(new LocalDate(2027, 5, 21));
        Assert.Contains("The last day. Make the most of it!", Texts(TodayView.Build(last, WidthClass.Phone)));
    }

    [AvaloniaFact]
    public async Task AfterTheTripTodayShowsItInNumbersAndOffersToPayBack()
    {
        using var session = await Phone(new LocalDate(2027, 5, 25));
        var trip = session.Trip!;
        var texts = Texts(TodayView.Build(session, WidthClass.Phone));
        Assert.Contains("This trip is over.", texts);
        Assert.Contains("The trip in numbers", texts);
        var plans = trip.Entities.Values.Count(e => e.Type == "schedule_item" && e.Data["status"]?.ToString() is not ("cancelled" or "idea"));
        Assert.Contains($"{session.Agenda.Days.Count} days · {plans} plans · {trip.Entities.Values.Count(e => e.Type == "place")} places", texts);
        Assert.Contains(texts, t => t.StartsWith("It cost ", StringComparison.Ordinal));
        // The sample trip still has debts open, so paying back is offered.
        Assert.NotEmpty(Expenses.Open(trip));
        Assert.Contains("Pay back…", texts);
    }

    [AvaloniaFact]
    public async Task TheCostsSayWhatTheTripComesToPerPersonAndDay()
    {
        using var session = await Phone(new LocalDate(2027, 5, 16));
        var trip = session.Trip!; var home = session.TripCurrencyCode;
        var expenses = trip.Entities.Values.Where(e => e.Type == "expense" && !Expenses.IsIdea(trip, e)).ToArray();
        var paid = Expenses.Total(expenses, home, session.EstimateRates); var planned = Expenses.Total(expenses, home, session.EstimateRates, planned: true);
        // Two travellers and eight days, paid and still to pay together; some of it is estimated.
        Assert.Equal(2, TripPeople.Travelers(trip).Count);
        var expected = "≈ " + Formats.Money(Math.Round((paid.Total + planned.Total) / 2 / 8, 2, MidpointRounding.AwayFromZero), home) + " per person and day";
        Assert.Equal(expected, CostTexts.PerPersonAndDay(session.Strings, trip, home, paid, planned));
        session.ShowTab(MobileTab.More); session.OpenPage("costs");
        Assert.Contains(expected, Texts(MorePages.Build(session, WidthClass.Phone)));
        // Without rates the yen cannot be counted, and a smaller number would mislead, so there is none.
        Assert.Null(CostTexts.PerPersonAndDay(session.Strings, trip, home, Expenses.Total(expenses, home), Expenses.Total(expenses, home, planned: true)));
        // Without dates there is nothing to divide by.
        var undated = trip with { Manifest = trip.Manifest.Copy().Also(m => m.Data.Remove("dates")), Entities = trip.Entities.Where(p => p.Value.Type != "schedule_item").ToDictionary() };
        Assert.Null(CostTexts.PerPersonAndDay(session.Strings, undated, home, paid, planned));
    }

    [AvaloniaFact]
    public async Task ThePhoneSaysTheCodeNameOfItsVersion()
    {
        using var session = await Phone(new LocalDate(2027, 5, 16));
        session.ShowTab(MobileTab.More); session.OpenPage("settings");
        var version = typeof(MorePages).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .OfType<System.Reflection.AssemblyInformationalVersionAttribute>().First().InformationalVersion.Split('+')[0];
        Assert.Contains(version + " · " + CodeNames.For(version), Texts(MorePages.Build(session, WidthClass.Phone)));
        session.OpenPage("about");
        Assert.Contains(Texts(MorePages.Build(session, WidthClass.Phone)), t => t.Contains("“" + CodeNames.For(version) + "”", StringComparison.Ordinal));
    }
}
