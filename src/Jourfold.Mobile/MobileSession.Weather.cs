using Jourfold.Application;
using TravelRepo.Providers.Weather;

namespace Jourfold.Mobile;

public sealed partial class MobileSession
{
    private WeatherService? weatherService;
    private Task? weatherUpdate;

    /// <summary>Whether weather is fetched for the trip's places; off until the user says yes once, as on the desktop.</summary>
    public bool WeatherOn => Settings.Values.GetValueOrDefault("weather.online") == "on";
    /// <summary>The open trip's weather from the device's cache, then what was fetched; empty while weather is off.</summary>
    public TripWeather Weather { get; private set; } = TripWeather.Empty;

    private WeatherService Weathers
    {
        get
        {
            if (weatherService is not null) return weatherService;
            var providers = WeatherService.BuiltIn(MapsHttp, Host.Secrets, OnlineIdentity.UserAgent);
            return weatherService = new WeatherService(providers, (OpenMeteo)providers[0], new WeatherCache(Path.Combine(Host.DataRoot, "cache", "weather")), clock: Host.Clock);
        }
    }

    /// <summary>Turns weather on or off; on fetches at once.</summary>
    public async Task SetWeatherAsync(bool on, CancellationToken ct = default)
    {
        Settings.Values["weather.online"] = on ? "on" : "off"; Settings.Save();
        if (!on) { Weather = TripWeather.Empty; Changed?.Invoke(); return; }
        await UpdateWeatherAsync(ct);
    }

    /// <summary>
    /// Shows the cached weather at once and fetches what is missing or expired; the service knows how long a forecast
    /// stays fresh, so calling this often costs nothing. Without a network the cache is what there is.
    /// </summary>
    public async Task UpdateWeatherAsync(CancellationToken ct = default)
    {
        if (!WeatherOn || Trip is not { } trip) return;
        Weather = Weathers.Cached(trip); Changed?.Invoke();
        if (weatherUpdate is { IsCompleted: false }) return;
        async Task Fetch()
        {
            try { var fetched = await Weathers.UpdateAsync(trip, ct); if (Trip == trip) { Weather = fetched; Changed?.Invoke(); } }
            catch (Exception ex) when (ex is IOException or HttpRequestException or UnauthorizedAccessException or OperationCanceledException) { }
        }
        weatherUpdate = Fetch(); await weatherUpdate;
    }
}
