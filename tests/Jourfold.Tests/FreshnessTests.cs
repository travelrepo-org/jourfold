using System.Globalization;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Jourfold.Application;
using Jourfold.Mobile;
using NodaTime;
using TravelRepo.Git;
using Xunit;

namespace Jourfold.Tests;

/// <summary>
/// When weather and exchange rates were fetched, said next to them, and fetching them again on request: once at a
/// time, keeping what is there when it fails.
/// </summary>
public sealed class FreshnessTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "jourfold-freshness-" + Guid.NewGuid());
    public void Dispose() => TestFiles.Delete(root);

    private static readonly DateTimeZone Tokyo = DateTimeZoneProviders.Tzdb["Asia/Tokyo"];
    private const string Ecb = """
        <gesmes:Envelope xmlns:gesmes="http://www.gesmes.org/xml/2002-08-01" xmlns="http://www.ecb.int/vocabulary/2002-08-01/eurofxref">
          <Cube><Cube time='2027-05-14'><Cube currency='USD' rate='1.1'/><Cube currency='JPY' rate='160'/></Cube></Cube></gesmes:Envelope>
        """;

    [Fact]
    public void TheTimeIsShortOnTheSameDayAndHasTheDateBefore()
    {
        var culture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-GB");
        try
        {
            var now = new LocalDateTime(2027, 5, 16, 10, 40).InZoneStrictly(Tokyo).ToInstant();
            Assert.Equal("as of 10:20", Freshness.Text(now - Duration.FromMinutes(20), now, Tokyo, "as of {0}"));
            Assert.Equal("as of 15 May, 18:00", Freshness.Text(now - Duration.FromMinutes(16 * 60 + 40), now, Tokyo, "as of {0}"));
        }
        finally { CultureInfo.CurrentCulture = culture; }
    }

    [Fact]
    public void RatesAskedForAreFetchedAtMostOnceAMinute()
    {
        var schedule = new RateSchedule(); var now = new DateTime(2027, 5, 16, 1, 0, 0, DateTimeKind.Utc);
        Assert.True(schedule.BeginRefresh(now));
        Assert.False(schedule.BeginRefresh(now.AddSeconds(30)));
        Assert.True(schedule.BeginRefresh(now.AddMinutes(1)));
        // A fetch on request counts for the automatic schedule too, so a failed one is not retried at once.
        Assert.False(schedule.Begin(null, Path.Combine(root, "none.json"), nowUtc: now.AddMinutes(2)));
    }

    [Fact]
    public void TheRatesWereFetchedWhenTheirFileWasWritten()
    {
        Directory.CreateDirectory(root); var file = Path.Combine(root, "rates.json");
        Assert.Null(RateSchedule.Fetched(file));
        File.WriteAllText(file, "{}"); var at = new DateTime(2027, 5, 16, 1, 0, 0, DateTimeKind.Utc); File.SetLastWriteTimeUtc(file, at);
        Assert.Equal(new DateTimeOffset(at), RateSchedule.Fetched(file));
    }

    [AvaloniaFact]
    public async Task TheConverterSaysWhenTheRatesWereFetchedAndFetchesThemAgain()
    {
        var handler = new EcbAnswer();
        var session = new MobileSession(new MobileHost(root, new GitCliBackend(), new FakeClock(Instant.FromUtc(2027, 5, 16, 1, 0)), () => Tokyo, "en", new MemorySecrets(), handler));
        await session.OpenAsync(await session.CreateSampleAsync(new LocalDate(2027, 5, 14)));
        await session.TurnOnRatesAsync(); Assert.Equal(1, handler.Asked);
        // Fetched yesterday, so the time shows with its date.
        File.SetLastWriteTimeUtc(Path.Combine(root, "rates.json"), DateTime.UtcNow.AddDays(-1));
        var window = new Window { Width = 412, Height = 915, Content = new AppShell(session) }; window.Show();
        try
        {
            session.ShowTab(MobileTab.More); session.ShowPage("converter"); Probe.Layout();
            Button Fresh() => Probe.All<Button>(window).Single(b => AutomationProperties.GetName(b)?.StartsWith("Fetch the exchange rates again, fetched ", StringComparison.Ordinal) == true);
            Assert.Contains(", ", AutomationProperties.GetName(Fresh())!["Fetch the exchange rates again, fetched ".Length..]);

            Fresh().RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            await Probe.Until(() => handler.Asked == 2 && !session.FetchingRates);
            Assert.True(DateTime.UtcNow - session.RatesFetched!.Value.UtcDateTime < TimeSpan.FromMinutes(1));
            // Tapped again straight away: nothing is fetched.
            Assert.True(await session.RefreshRatesAsync()); Assert.Equal(2, handler.Asked);
        }
        finally { window.Close(); session.Dispose(); }
    }

    [AvaloniaFact]
    public async Task RatesThatCannotBeFetchedStayAndTheUserIsTold()
    {
        var handler = new EcbAnswer();
        var session = new MobileSession(new MobileHost(root, new GitCliBackend(), new FakeClock(Instant.FromUtc(2027, 5, 16, 1, 0)), () => Tokyo, "en", new MemorySecrets(), handler));
        await session.OpenAsync(await session.CreateSampleAsync(new LocalDate(2027, 5, 14)));
        await session.TurnOnRatesAsync();
        var window = new Window { Width = 412, Height = 915, Content = new AppShell(session) }; window.Show();
        try
        {
            session.ShowTab(MobileTab.More); session.ShowPage("converter"); Probe.Layout();
            handler.Offline = true;
            Probe.All<Button>(window).Single(b => AutomationProperties.GetName(b)?.StartsWith("Fetch the exchange rates again", StringComparison.Ordinal) == true)
                .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            await Probe.Until(() => Probe.All<TextBlock>(window).Any(t => t.Text == session.Strings["RatesRefreshFailed"]));
            Assert.Equal(new DateOnly(2027, 5, 14), session.Rates!.Date);
        }
        finally { window.Close(); session.Dispose(); }
    }

    [AvaloniaFact]
    public async Task TheDaySaysWhenItsWeatherWasFetchedAndFetchesItAgain()
    {
        var handler = new OpenMeteoAnswer(); var clock = new Ahead();
        var session = new MobileSession(new MobileHost(root, new GitCliBackend(), clock, () => Tokyo, "en", new MemorySecrets(), handler));
        await session.OpenAsync(await session.CreateSampleAsync(clock.GetCurrentInstant().InZone(Tokyo).Date.PlusDays(1)));
        await session.SetWeatherAsync(true);
        Assert.NotEmpty(session.Weather.Days);
        var window = new Window { Width = 412, Height = 915, Content = new AppShell(session) }; window.Show();
        try
        {
            session.ShowTab(MobileTab.Plan); session.ShowDate(session.Weather.Days.Keys.Min()); Probe.Layout();
            Button? Offer() => Probe.All<Button>(window).FirstOrDefault(b => AutomationProperties.GetName(b)?.StartsWith("Fetch the weather again, as of ", StringComparison.Ordinal) == true);
            Assert.NotNull(Offer());

            // Ten minutes later a tap fetches the forecasts again, before they expire.
            var asked = handler.Asked; clock.By = Duration.FromMinutes(10);
            Offer()!.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            await Probe.Until(() => handler.Asked > asked);
            await Probe.Until(() => Offer() is { IsEnabled: true });

            // Offline: the user is told, and the day keeps its weather.
            handler.Offline = true; clock.By = Duration.FromMinutes(20);
            Offer()!.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            await Probe.Until(() => Probe.All<TextBlock>(window).Any(t => t.Text == session.Strings["WeatherRefreshFailed"]));
            Assert.NotEmpty(session.Weather.Days); Assert.NotNull(Offer());
        }
        finally { window.Close(); session.Dispose(); }
    }

    /// <summary>The device's clock, ahead by <see cref="By"/>, so forecasts fetched a moment ago count as older.</summary>
    private sealed class Ahead : IClock { public Duration By; public Instant GetCurrentInstant() => SystemClock.Instance.GetCurrentInstant() + By; }

    /// <summary>Open-Meteo for any points: dry and mild for the next days, or no network.</summary>
    private sealed class OpenMeteoAnswer : HttpMessageHandler
    {
        public int Asked; public bool Offline;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Asked++;
            if (Offline) throw new HttpRequestException("offline");
            var points = (System.Web.HttpUtility.ParseQueryString(request.RequestUri!.Query)["latitude"] ?? "").Split(',').Length;
            var start = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 86400 * 86400 - 86400;
            System.Text.Json.Nodes.JsonArray Series(Func<int, double> value) => [.. Enumerable.Range(0, 17 * 24).Select(i => (System.Text.Json.Nodes.JsonNode?)value(i))];
            System.Text.Json.Nodes.JsonObject Point() => new()
            {
                ["hourly"] = new System.Text.Json.Nodes.JsonObject
                {
                    ["time"] = Series(i => start + i * 3600), ["weather_code"] = Series(_ => 1), ["temperature_2m"] = Series(i => 15 + i % 24 / 3),
                    ["precipitation_probability"] = Series(_ => 10), ["precipitation"] = Series(_ => 0), ["wind_speed_10m"] = Series(_ => 8), ["wind_gusts_10m"] = Series(_ => 15),
                },
            };
            System.Text.Json.Nodes.JsonNode body = points == 1 ? Point() : new System.Text.Json.Nodes.JsonArray([.. Enumerable.Range(0, points).Select(_ => (System.Text.Json.Nodes.JsonNode?)Point())]);
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent(body.ToJsonString()) });
        }
    }

    /// <summary>The European Central Bank's file, or no network.</summary>
    private sealed class EcbAnswer : HttpMessageHandler
    {
        public int Asked; public bool Offline;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Asked++;
            if (Offline) throw new HttpRequestException("offline");
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent(Ecb) });
        }
    }

    private sealed class FakeClock(Instant now) : IClock { public Instant GetCurrentInstant() => now; }

    private sealed class MemorySecrets : TravelRepo.Providers.ISecretStore
    {
        private readonly Dictionary<string, string> values = [];
        public Task<string?> ReadAsync(string key, CancellationToken ct = default) => Task.FromResult(values.GetValueOrDefault(key));
        public Task WriteAsync(string key, string value, CancellationToken ct = default) { values[key] = value; return Task.CompletedTask; }
        public Task DeleteAsync(string key, CancellationToken ct = default) { values.Remove(key); return Task.CompletedTask; }
    }
}
