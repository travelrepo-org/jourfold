using System.Globalization;
using NodaTime;
using TravelRepo.Core;
using TravelRepo.Providers.Weather;

namespace Jourfold.Desktop;

/// <summary>Icons and short texts for weather. The data and the rules come from TravelRepo.Providers.Weather.</summary>
public static class WeatherDisplay
{
    public static string Icon(WeatherCondition condition, bool night = false) => condition switch
    {
        WeatherCondition.Clear => night ? "moon" : "sun",
        WeatherCondition.PartlyCloudy => night ? "cloud-moon" : "cloud-sun",
        WeatherCondition.Cloudy => "cloud",
        WeatherCondition.Fog => "cloud-fog",
        WeatherCondition.Drizzle => "cloud-drizzle",
        WeatherCondition.Rain or WeatherCondition.HeavyRain => "cloud-rain",
        WeatherCondition.Snow => "cloud-snow",
        _ => "cloud-lightning"
    };

    /// <summary>An icon for climate averages: rain on half the days or more, some rain, or mostly dry.</summary>
    public static string Icon(ClimateDay climate) => climate.RainDayShare >= .5 ? "cloud-rain" : climate.RainDayShare >= .25 ? "cloud-sun-rain" : "sun";

    public static string Icon(WeatherOutlook outlook) => outlook.Climate is { } c ? Icon(c) : Icon(outlook.Day?.Condition ?? outlook.Hours.Select(h => h.Condition).DefaultIfEmpty(WeatherCondition.Cloudy).Max());

    public static string ConcernIcon(ConcernKind kind) => kind switch
    {
        ConcernKind.Thunderstorm => "cloud-lightning",
        ConcernKind.Snow => "snowflake",
        ConcernKind.Wind => "wind",
        ConcernKind.Rain => "umbrella",
        ConcernKind.Heat => "thermometer-sun",
        _ => "thermometer-snowflake"
    };

    public static string Temperature(double celsius, bool imperial) => Math.Round(imperial ? celsius * 1.8 + 32 : celsius).ToString("0", CultureInfo.CurrentCulture) + "°";
    public static string Wind(double kmh, bool imperial) => imperial ? Math.Round(kmh / 1.609).ToString("0", CultureInfo.CurrentCulture) + " mph" : Math.Round(kmh).ToString("0", CultureInfo.CurrentCulture) + " km/h";
    public static string Precipitation(double mm, bool imperial) => imperial ? (mm / 25.4).ToString("0.##", CultureInfo.CurrentCulture) + " in" : mm.ToString(mm < 10 ? "0.#" : "0", CultureInfo.CurrentCulture) + " mm";
    public static string Percent(int value) => value.ToString(CultureInfo.CurrentCulture) + " %";

    public static string Condition(WeatherCondition condition, Localization s) => s["weather." + condition];

    /// <summary>"18° / 9°" for a day or climate averages.</summary>
    public static string Range(double? max, double? min, bool imperial) => max is { } a && min is { } b ? Temperature(a, imperial) + " / " + Temperature(b, imperial) : max is { } only ? Temperature(only, imperial) : "";

    /// <summary>The temperatures of an outlook, as short as possible: one day's high and low, or the averages.</summary>
    public static string Temperatures(WeatherOutlook outlook, bool imperial) => outlook.Climate is { } c ? Range(c.MaxC, c.MinC, imperial)
        : outlook.Hours.Count > 0 && outlook.Kind == OutlookKind.Forecast && outlook.Hours.Select(h => h.TemperatureC).OfType<double>().ToArray() is { Length: > 0 } t ? (Math.Round(t.Min()) == Math.Round(t.Max()) ? Temperature(t.Max(), imperial) : Temperature(t.Min(), imperial) + "–" + Temperature(t.Max(), imperial))
        : Range(outlook.Day?.MaxC, outlook.Day?.MinC, imperial);

    /// <summary>One concern as a short phrase, such as "Heavy rain, 14 mm" or "Gusts up to 80 km/h".</summary>
    public static string Concern(WeatherConcern concern, Localization s, bool imperial) => concern.Kind switch
    {
        ConcernKind.Rain => string.Format(CultureInfo.CurrentCulture, s[concern.Severe ? "ConcernHeavyRain" : "ConcernRain"], Precipitation(concern.Value, imperial)),
        ConcernKind.Wind => string.Format(CultureInfo.CurrentCulture, s[concern.Severe ? "ConcernStorm" : "ConcernWind"], Wind(concern.Value, imperial)),
        ConcernKind.Heat => string.Format(CultureInfo.CurrentCulture, s["ConcernHeat"], Temperature(concern.Value, imperial)),
        ConcernKind.Cold => string.Format(CultureInfo.CurrentCulture, s["ConcernCold"], Temperature(concern.Value, imperial)),
        ConcernKind.Snow => s["ConcernSnow"],
        _ => s["ConcernThunderstorm"]
    };

    public static string Concerns(IEnumerable<WeatherConcern> concerns, Localization s, bool imperial) => string.Join(" · ", concerns.Select(c => Concern(c, s, imperial)));

    /// <summary>What an outlook says in one line, for tooltips and screen readers.</summary>
    public static string Summary(WeatherOutlook outlook, Localization s, bool imperial)
    {
        if (outlook.Climate is { } c)
            return string.Format(CultureInfo.CurrentCulture, s["ClimateSummary"], Range(c.MaxC, c.MinC, imperial), (int)Math.Round(c.RainDayShare * 10));
        var parts = new List<string>();
        var condition = outlook.Kind == OutlookKind.Forecast && outlook.Hours.Count > 0 ? WorstOf(outlook.Hours) : outlook.Day?.Condition;
        if (condition is { } value) parts.Add(Condition(value, s));
        if (Temperatures(outlook, imperial) is { Length: > 0 } temperatures) parts.Add(temperatures);
        var chance = outlook.Kind == OutlookKind.Forecast && outlook.Hours.Count > 0 ? outlook.Hours.Max(h => h.PrecipitationChance) : outlook.Day?.PrecipitationChance;
        if (chance is >= 20) parts.Add(string.Format(CultureInfo.CurrentCulture, s["RainChance"], Percent(chance.Value)));
        return (outlook.Kind == OutlookKind.Trend ? s["TrendPrefix"] + " " : "") + string.Join(" · ", parts);
    }

    /// <summary>The most severe condition that lasts an hour or more; otherwise the most common one.</summary>
    public static WeatherCondition WorstOf(IReadOnlyList<WeatherHour> hours) =>
        hours.Where(h => h.Condition >= WeatherCondition.Drizzle).Select(h => (WeatherCondition?)h.Condition).Max()
        ?? hours.GroupBy(h => h.Condition).OrderByDescending(g => g.Count()).First().Key;

    /// <summary>The label of an exposure, for the outdoor control.</summary>
    public static string Exposure(Exposure exposure, Localization s) => s["exposure." + exposure];

    /// <summary>Why Jourfold thinks so: "Category Nature", "Title: Wanderung", "Journey by car".</summary>
    public static string Reason(ExposureEstimate estimate, Entity item, Localization s) => estimate.Source switch
    {
        ExposureSource.Transport => string.Format(CultureInfo.CurrentCulture, s["ExposureByTransport"], s[ScheduleCategories.TransportType(item) ?? "other"]),
        ExposureSource.Accommodation => s["ExposureByStay"],
        ExposureSource.Title => string.Format(CultureInfo.CurrentCulture, s["ExposureByWord"], estimate.Word),
        ExposureSource.Inherited => s["ExposureInherited"],
        _ => string.Format(CultureInfo.CurrentCulture, s["ExposureByCategory"], s["category." + (ScheduleCategories.Standard.Contains(estimate.Category) ? estimate.Category : "activity")])
    };

    /// <summary>"Sunrise 06:12 · Sunset 20:41" in the given zone.</summary>
    public static string? SunTimes(Instant? sunrise, Instant? sunset, DateTimeZone zone, Localization s) =>
        sunrise is { } rise && sunset is { } set ? string.Format(CultureInfo.CurrentCulture, s["SunTimes"], Formats.Time(rise.InZone(zone).TimeOfDay), Formats.Time(set.InZone(zone).TimeOfDay)) : null;

    /// <summary>The name of the service an outlook came from.</summary>
    public static string Source(WeatherOutlook outlook, IEnumerable<IWeatherProvider> providers, Localization s) =>
        outlook.Kind == OutlookKind.Climate ? s["ClimateSource"] : providers.FirstOrDefault(p => p.Id == outlook.Provider)?.Name ?? outlook.Provider;
}
