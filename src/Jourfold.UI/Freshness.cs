using System.Globalization;
using NodaTime;
using TravelRepo.Providers.Weather;

namespace Jourfold.UI;

/// <summary>
/// When weather or exchange rates on this device were fetched, said quietly next to them, on the desktop and the phone
/// alike: "as of 14:20" on the same day, "as of 3 Jun, 14:20" before. The time is the device's, as the fetch happened here.
/// </summary>
public static class Freshness
{
    /// <param name="format">A text with <c>{0}</c> for the time, such as <c>WeatherAsOf</c> ("as of {0}").</param>
    public static string Text(Instant fetched, Instant now, DateTimeZone zone, string format)
    {
        var at = fetched.InZone(zone);
        var time = at.Date == now.InZone(zone).Date ? Formats.Time(at.TimeOfDay) : Formats.ShortDate(at.Date) + ", " + Formats.Time(at.TimeOfDay);
        return string.Format(CultureInfo.CurrentCulture, format, time);
    }

    /// <summary>
    /// When the weather on show was fetched: the oldest of its forecasts, so the text never claims newer weather than
    /// there is. Null when nothing shown was fetched, such as climate averages alone.
    /// </summary>
    public static Instant? Fetched(TripWeather weather)
    {
        var times = weather.Days.Values.Select(d => d.Outlook.Fetched)
            .Concat(weather.Items.Values.SelectMany(i => i.Outlooks.Select(o => o.Fetched))).OfType<Instant>().ToArray();
        return times.Length == 0 ? null : times.Min();
    }
}
