using System.Text.Json.Nodes;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Jourfold.Desktop;
using Jourfold.Infrastructure;
using NodaTime;
using TravelRepo.Core;
using TravelRepo.Git;
using TravelRepo.Providers.Weather;
using TravelRepo.Repository;
using Xunit;
namespace Jourfold.Tests;

/// <summary>Weather in Jourfold: off until turned on, shown per day and item, warnings in Reminders, the outdoor choice.</summary>
public sealed class WeatherViewTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "jourfold-weather-" + Guid.NewGuid());
    public void Dispose() => TestFiles.Delete(root);
    private const string Zone = "Asia/Tokyo";
    private static readonly DateTimeZone Local = DateTimeZoneProviders.Tzdb[Zone];
    private static LocalDate Tomorrow => SystemClock.Instance.GetCurrentInstant().InZone(Local).Date.PlusDays(1);

    /// <summary>A service that never goes online: dry everywhere except heavy rain in the mountain village tomorrow from 10:00 to 13:00.</summary>
    private sealed class Rainy : IWeatherProvider
    {
        public int Calls;
        public string Id => "open-meteo"; public string Name => "Test weather"; public WeatherAttribution Attribution => new("Test weather data", "https://example.invalid/weather", "CC BY 4.0");
        public bool RequiresKey => false; public int HorizonDays => 16; public bool Covers(GeoPoint point) => true;
        public Task<IReadOnlyList<WeatherForecast?>> ForecastAsync(IReadOnlyList<GeoPoint> points, CancellationToken ct = default)
        {
            Calls++; var now = SystemClock.Instance.GetCurrentInstant(); var start = Local.AtStartOfDay(Tomorrow.PlusDays(-1)).ToInstant();
            var wetFrom = Local.AtStrictly(Tomorrow.At(new LocalTime(10, 0))).ToInstant(); var wetUntil = wetFrom + Duration.FromHours(3);
            return Task.FromResult<IReadOnlyList<WeatherForecast?>>(points.Select(p => (WeatherForecast?)new WeatherForecast(Id, p, now, now + Duration.FromHours(2), Enumerable.Range(0, 16 * 24).Select(i =>
            {
                var at = start + Duration.FromHours(i); var wet = p.Latitude > 36.5 && at >= wetFrom && at < wetUntil;
                return new WeatherHour(at, 1, wet ? 12 : 19, wet ? 90 : 5, wet ? 5 : 0, 10, 25, wet ? WeatherCondition.HeavyRain : WeatherCondition.Clear);
            }).ToArray())).ToArray());
        }
    }

    private WeatherService Service(Rainy provider) => new([provider], null, new WeatherCache(Path.Combine(root, "weather-cache")));

    private async Task<string> Trip()
    {
        var repository = new TravelRepository(Path.Combine(root, "trip")); var manifest = Entity.CreateTrip("Autumn in Japan", "en", Zone);
        manifest.Data["dates"] = new JsonObject { ["start"] = Tomorrow.ToString("yyyy-MM-dd", null), ["end"] = Tomorrow.PlusDays(2).ToString("yyyy-MM-dd", null) };
        await repository.InitializeAsync(manifest);
        await new GitRepository(repository, new GitCliBackend()).InitializeAsync("Test", "test@example.invalid");
        return repository.Root;
    }

    /// <summary>A hike and a museum visit in a mountain village tomorrow.</summary>
    private static async Task<(Guid Hike, Guid Museum)> Seed(MainViewModel m)
    {
        var village = Entity.Create("place", "Mountain village"); village.Data["location"] = new JsonObject { ["latitude"] = 36.738, ["longitude"] = 139.501 };
        JsonObject Time(int from, int to) => new() { ["precision"] = "exact", ["start"] = new ZonedTime(Tomorrow.At(new LocalTime(from, 0)).ToString("yyyy-MM-dd'T'HH:mm:ss", null), Zone).ToJson(), ["end"] = new ZonedTime(Tomorrow.At(new LocalTime(to, 0)).ToString("yyyy-MM-dd'T'HH:mm:ss", null), Zone).ToJson() };
        var hike = Entity.Create("schedule_item", "Ridge trail"); hike.Data["status"] = "planned"; hike.Data["category"] = "nature"; hike.Data["time"] = Time(9, 15); hike.Data["default_place"] = village.Id.ToString();
        var museum = Entity.Create("schedule_item", "Village museum"); museum.Data["status"] = "planned"; museum.Data["category"] = "culture"; museum.Data["time"] = Time(16, 17); museum.Data["default_place"] = village.Id.ToString();
        await m.Workspace!.ApplyAsync([new(village.Id, village), new(hike.Id, hike), new(museum.Id, museum)]);
        return (hike.Id, museum.Id);
    }

    [AvaloniaFact]
    public async Task WeatherIsOffUntilTurnedOnAndWarnsAboutOutdoorItems()
    {
        using var store = new LocalStore(Path.Combine(root, "local"));
        using var m = new MainViewModel(store, new GitCliBackend(), new OsSecretStore(), new TestInteraction());
        var provider = new Rainy(); m.WeatherFactory = () => Service(provider);
        await m.OpenAsync(await Trip()); var (hike, museum) = await Seed(m); await m.UpdateWeatherAsync(force: true);
        Assert.False(m.WeatherOn); Assert.Equal(0, provider.Calls); Assert.Empty(m.Weather.Items);

        await m.SetWeatherAsync(true);
        Assert.Equal(1, provider.Calls);
        var weather = m.Weather.Items[hike];
        Assert.Equal(Exposure.Outdoor, weather.Exposure.Exposure);
        Assert.Equal(ConcernKind.Rain, Assert.Single(weather.Concerns).Kind);
        Assert.Empty(m.Weather.Items[museum].Concerns);
        Assert.True(m.Weather.Days.ContainsKey(Tomorrow));
        var reminder = Assert.Single(m.Reminders, r => r.Kind == ReminderKind.WeatherWarning);
        Assert.Equal(hike, reminder.Entity); Assert.Contains("Heavy rain, 15 mm", m.ReminderText(reminder));

        // Marked as indoors, the hike no longer gets a warning; the choice is stored in the trip.
        await m.SetOutdoorAsync(hike, false);
        await m.UpdateWeatherAsync(); await m.UpdateRemindersAsync();
        Assert.Empty(m.Weather.Items[hike].Concerns); Assert.DoesNotContain(m.Reminders, r => r.Kind == ReminderKind.WeatherWarning);
        Assert.False(m.Workspace!.State.Trip.Find(hike)!.Data["outdoor"]!.GetValue<bool>());
        await m.SetOutdoorAsync(hike, null);
        Assert.Null(m.Workspace.State.Trip.Find(hike)!.Data["outdoor"]);

        // Assistants read the same cache, and only while weather is on.
        var forAssistants = MainViewModel.CachedWeatherForAssistants(store.Root);
        Assert.Equal(MainViewModel.WeatherCacheDirectory(store), forAssistants?.CacheDirectory);

        await m.SetWeatherAsync(false);
        Assert.Null(MainViewModel.CachedWeatherForAssistants(store.Root));
        Assert.Empty(m.Weather.Items); Assert.DoesNotContain(m.Reminders, r => r.Kind == ReminderKind.WeatherWarning);
        Assert.Equal(1, provider.Calls); // the cache was fresh the whole time
    }

    [AvaloniaFact]
    public async Task ThePlanAndTheInspectorShowTheWeather()
    {
        var w = new MainWindow(); w.Model.SetPreference("Language", "en"); w.Model.SetLanes(false); w.Show(); Probe.Layout();
        var provider = new Rainy(); w.Model.WeatherFactory = () => Service(provider);
        try
        {
            var m = w.Model; await m.OpenAsync(await Trip()); var (hike, _) = await Seed(m); m.InboxOpen = false;
            m.WeatherPromptDismissed = false; m.Navigate("Plan"); m.ShowDate(Tomorrow);
            m.Selected = m.Workspace!.State.Trip.Find(hike); Probe.Layout();
            // Before weather is on, the inspector offers it once.
            await Probe.Until(() => { Probe.ShowDetails(w); return Probe.All<Button>(w).Any(b => AutomationProperties.GetName(b) == "Show weather"); });
            Probe.Click(w, "Show weather"); await Probe.Until(() => m.Weather.Items.Count > 0); Probe.Layout();

            var timetable = Assert.IsType<TimetableView>(w.FindControl<ContentControl>("MainContent")!.Content);
            Assert.Contains(Probe.All<Control>(timetable), c => AutomationProperties.GetName(c) is { } name && name.StartsWith(Formats.LongDay(Tomorrow) + ", Heavy rain", StringComparison.Ordinal));
            var block = Probe.All<Button>(timetable).First(b => b.Tag is Guid id && id == hike);
            Assert.EndsWith("Heavy rain, 15 mm", AutomationProperties.GetName(block));

            var inspector = Probe.Named<StackPanel>(w, "Inspector");
            Assert.Contains(Probe.All<TextBlock>(inspector), t => t.Text == "Heavy rain, 15 mm");
            Assert.Contains(Probe.All<TextBlock>(inspector), t => t.Text == "Estimated: Outdoors (category Nature)");
            Probe.Click(inspector, "Indoors"); await Probe.Until(() => m.Workspace.State.Trip.Find(hike)!.Data["outdoor"] is not null); Probe.Layout();
            Assert.False(m.Workspace.State.Trip.Find(hike)!.Data["outdoor"]!.GetValue<bool>());
            await Probe.Until(() => m.Weather.Items[hike].Concerns.Count == 0);

            m.Navigate("List"); Probe.Layout();
            Assert.Contains(Probe.All<Control>(w), c => AutomationProperties.GetName(c) is { } name && name.StartsWith("Heavy rain · 19° / 12°", StringComparison.Ordinal));
        }
        finally { await w.Model.SetWeatherAsync(false); w.Close(); }
    }
}
