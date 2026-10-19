using Jourfold.Infrastructure;
using NodaTime;
using TravelRepo.Core;
using TravelRepo.Providers.Weather;

namespace Jourfold.Desktop;

/// <summary>
/// Weather for the open trip, off until the person turns it on. TravelRepo's <see cref="WeatherService"/> decides what
/// to fetch and when weather matters; this part keeps the setting, starts updates and turns concerns into reminders.
/// </summary>
public partial class MainViewModel
{
    /// <summary>How long to wait after a failed update before trying again on its own.</summary>
    public static readonly TimeSpan WeatherRetry = TimeSpan.FromMinutes(10);
    private readonly HttpClient weatherHttp = new() { Timeout = TimeSpan.FromSeconds(30) };
    private WeatherService? weatherService;
    private Task? weatherUpdate;
    private DateTime lastWeatherUpdate = DateTime.MinValue;

    /// <summary>Creates the weather service; tests and screenshots replace it with services that never go online.</summary>
    public Func<WeatherService>? WeatherFactory { get; set; }

    /// <summary>The trip's weather, empty while weather is off.</summary>
    public TripWeather Weather { get; private set; } = TripWeather.Empty;

    public bool WeatherOn => Store.Get("weather.online") == "true";
    public bool WeatherPromptDismissed { get => Store.Get("weather.prompt") == "dismissed"; set => Store.Set("weather.prompt", value ? "dismissed" : "shown"); }
    public string WeatherProvider => Store.Get("weather.provider") is { Length: > 0 } id ? id : OpenMeteo.ProviderId;
    public bool Imperial => Store.Get("units") == "Imperial";
    public WeatherService WeatherServices => weatherService ??= CreateWeatherService();

    private WeatherService CreateWeatherService()
    {
        if (WeatherFactory is { } factory) return factory();
        var providers = WeatherService.BuiltIn(weatherHttp, secrets, OnlineIdentity.UserAgent);
        return new WeatherService(providers, (OpenMeteo)providers[0], new WeatherCache(WeatherCacheDirectory(Store)));
    }

    public static string WeatherCacheDirectory(LocalStore store) => Path.Combine(store.Root, "cache", "weather");

    /// <summary>
    /// The weather cache for AI assistants (<c>--mcp</c>), or null while weather is off. Read anew on every call, so
    /// turning weather off in Jourfold applies at once. Assistants only ever read this cache; they cannot fetch.
    /// </summary>
    public static TravelRepo.Mcp.CachedWeatherSource? CachedWeatherForAssistants(string? storeRoot = null)
    {
        using var store = new LocalStore(storeRoot);
        return store.Get("weather.online") == "true" ? new(WeatherCacheDirectory(store), store.Get("weather.provider")) : null;
    }

    public async Task SetWeatherAsync(bool enabled)
    {
        Store.Set("weather.online", enabled ? "true" : "false"); OnPropertyChanged(nameof(WeatherOn));
        if (!enabled) { Weather = TripWeather.Empty; ContentChanged?.Invoke(this, EventArgs.Empty); await UpdateRemindersAsync(); return; }
        await UpdateWeatherAsync(force: true);
    }

    public async Task SetWeatherProviderAsync(string id)
    {
        Store.Set("weather.provider", id); OnPropertyChanged(nameof(WeatherProvider));
        if (WeatherOn) await UpdateWeatherAsync(force: true);
    }

    /// <summary>Stores or removes the personal key of a service in the operating system's secret store.</summary>
    public async Task SetWeatherKeyAsync(string secret, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) await secrets.DeleteAsync(secret); else await secrets.WriteAsync(secret, value.Trim());
        if (WeatherOn) await UpdateWeatherAsync(force: true);
    }

    public async Task<bool> HasWeatherKeyAsync(string secret) => !string.IsNullOrEmpty(await secrets.ReadAsync(secret));

    public void ClearWeatherCache() { WeatherServices.Cache.Clear(); Weather = TripWeather.Empty; ContentChanged?.Invoke(this, EventArgs.Empty); }

    /// <summary>
    /// Shows the cached weather at once, then fetches what is missing or expired; the service knows how long each
    /// forecast stays fresh, so frequent calls cost nothing. After a failure it waits <see cref="WeatherRetry"/>
    /// unless <paramref name="force"/> is set.
    /// </summary>
    public async Task UpdateWeatherAsync(bool force = false, bool fetch = true)
    {
        if (!WeatherOn || Workspace is null) { if (Weather != TripWeather.Empty) { Weather = TripWeather.Empty; ContentChanged?.Invoke(this, EventArgs.Empty); } return; }
        var service = WeatherServices; service.Preferred = WeatherProvider;
        var trip = Workspace.State.Trip;
        Show(service.Cached(trip) with { Problems = Weather.Problems });
        if (!fetch || weatherUpdate is { IsCompleted: false }) return;
        if (!force && Weather.Problems.Count > 0 && DateTime.UtcNow - lastWeatherUpdate < WeatherRetry) return;
        lastWeatherUpdate = DateTime.UtcNow;
        var update = UpdateCoreAsync(service, trip); weatherUpdate = update; await update;
    }

    private async Task UpdateCoreAsync(WeatherService service, TripSnapshot trip)
    {
        TripWeather result;
        try { result = await service.UpdateAsync(trip); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException) { return; }
        if (Workspace?.State.Trip is not { } current || !WeatherOn) return;
        // The trip may have changed meanwhile; the cache has everything that was fetched.
        Show(current == trip ? result : service.Cached(current) with { Problems = result.Problems });
        await UpdateRemindersAsync();
    }

    private void Show(TripWeather weather)
    {
        if (Same(weather, Weather)) return;
        Weather = weather; OnPropertyChanged(nameof(Weather)); ContentChanged?.Invoke(this, EventArgs.Empty);
    }

    private static bool Same(TripWeather a, TripWeather b) =>
        a.Items.Count == b.Items.Count && a.Days.Count == b.Days.Count && a.Problems.Count == b.Problems.Count
        && a.Items.All(p => b.Items.TryGetValue(p.Key, out var other) && p.Value.Exposure == other.Exposure && p.Value.Concerns.SequenceEqual(other.Concerns) && p.Value.Outlooks.Select(o => (o.Kind, o.Fetched, o.Provider)).SequenceEqual(other.Outlooks.Select(o => (o.Kind, o.Fetched, o.Provider))))
        && a.Days.All(p => b.Days.TryGetValue(p.Key, out var other) && p.Value.Place == other.Place && p.Value.Outlook.Fetched == other.Outlook.Fetched && p.Value.Outlook.Kind == other.Outlook.Kind);

    /// <summary>The weather reminders while weather is on.</summary>
    private IReadOnlyList<Reminder> WeatherReminders(Instant now) => WeatherOn && Workspace is not null ? WeatherAdvice.Reminders(Workspace.State.Trip, Weather, now) : [];

    /// <summary>Changes whether an item happens outdoors: true, false, or null to let Jourfold estimate it.</summary>
    public Task SetOutdoorAsync(Guid id, bool? outdoor) => UpdateAsync(id, x => { if (outdoor is { } value) x.Data[OutdoorEstimates.Field] = value; else x.Data.Remove(OutdoorEstimates.Field); });
}
