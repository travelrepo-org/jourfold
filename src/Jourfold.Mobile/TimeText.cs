using System.Globalization;
using NodaTime;
using TravelRepo.Core;
using TravelRepo.Repository;

namespace Jourfold.Mobile;

/// <summary>
/// A time as shown on a line. <see cref="Start"/> and <see cref="End"/> are in the line's main zone; <see cref="Zone"/>
/// names that zone when a journey starts in another one; <see cref="Arrival"/> is the arrival of a journey across
/// zones in its own local time; <see cref="Other"/> is the start in the other zone that matters (the phone's or the
/// item's), quieter.
/// </summary>
public sealed record ClockText(string Start, string? End = null, string? Zone = null, string? Arrival = null, string? Other = null)
{
    /// <summary>One line, for example "11:00–13:00", "13:20 CEST" or "All day".</summary>
    public string Primary => Start + (End is null ? "" : "–" + End) + (Zone is null ? "" : " " + Zone);
}

/// <summary>
/// How times read on the phone (specs/mobile/DECISIONS.md, 1). Today follows the phone's clock, with the item's own
/// local time added when its zone differs. Plan, details and bookings show the item's local time, with the phone's
/// time added when the phone is in another zone. A journey across zones always names both zones.
/// </summary>
public static class TimeText
{
    private static string Clock(ZonedDateTime t) => t.TimeOfDay.ToString("HH:mm", CultureInfo.InvariantCulture);
    private static DateTimeZone Zone(string? id, DateTimeZone fallback) => id is null ? fallback : DateTimeZoneProviders.Tzdb.GetZoneOrNull(id) ?? fallback;
    private static string Short(DateTimeZone zone, Instant at) => ExportTimes.Abbreviation(zone, at);
    private static bool Same(DateTimeZone a, DateTimeZone b, Instant at) => a.Id == b.Id || a.GetUtcOffset(at) == b.GetUtcOffset(at);

    /// <summary>The time of a line in the item's local time, for Plan and details.</summary>
    public static ClockText Local(AgendaEntry entry, DateTimeZone phone, Localization s) => Format(entry, phone, s, phoneFirst: false);

    /// <summary>The time of a line in the phone's time, for Today.</summary>
    public static ClockText OnPhone(AgendaEntry entry, DateTimeZone phone, Localization s) => Format(entry, phone, s, phoneFirst: true);

    private static ClockText Format(AgendaEntry entry, DateTimeZone phone, Localization s, bool phoneFirst)
    {
        if (entry.Precision == TimePrecision.AllDay) return new(s["AllDay"]);
        if (entry.Precision == TimePrecision.DayPart && entry.Item.Data["time"]?["day_part"]?.ToString() is { } part) return new(s.Optional(part));
        if (entry.Start is not { } start) return new("");
        var startZone = Zone(entry.Zone, phone); var endZone = Zone(entry.EndZone ?? entry.Zone, startZone);
        var approximate = entry.Precision == TimePrecision.Approximate ? "~" : "";
        var end = entry.Role == AgendaRole.Item && entry.End is { } e && e > start ? entry.End : null;

        // A journey across zones: both ends in their own local time, each named, whatever the phone's zone.
        if (end is { } arrive && !Same(startZone, endZone, start))
            return new(approximate + Clock(start.InZone(startZone)), Zone: Short(startZone, start),
                Arrival: Clock(arrive.InZone(endZone)) + " " + Short(endZone, arrive) + (arrive.InZone(endZone).Date != start.InZone(startZone).Date ? " " + s["NextDayShort"] : ""));

        if (Same(startZone, phone, start)) return new(approximate + Clock(start.InZone(startZone)), end is { } same ? Clock(same.InZone(startZone)) : null);
        var mainZone = phoneFirst ? phone : startZone; var otherZone = phoneFirst ? startZone : phone;
        var other = approximate + Clock(start.InZone(otherZone)) + " " + Short(otherZone, start);
        // When the other zone is on another calendar day, say which day, so 01:30 is not read as the same morning.
        if (start.InZone(otherZone).Date != start.InZone(mainZone).Date)
            other = start.InZone(otherZone).Date.ToString("ddd", CultureInfo.GetCultureInfo(s.Language)) + " " + other;
        return new(approximate + Clock(start.InZone(mainZone)), end is { } local ? Clock(local.InZone(mainZone)) : null, Other: other);
    }

    /// <summary>"in 20 min", "in 1 h 15 min" or "now".</summary>
    public static string Until(Instant now, Instant at, Localization s)
    {
        var minutes = (int)Math.Ceiling((at - now).TotalMinutes);
        if (minutes <= 0) return s["StartsNow"];
        return string.Format(CultureInfo.CurrentCulture, s["StartsIn"], minutes < 60 ? minutes + " min" : minutes / 60 + " h" + (minutes % 60 > 0 ? " " + minutes % 60 + " min" : ""));
    }
}
