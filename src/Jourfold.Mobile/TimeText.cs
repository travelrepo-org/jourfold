using System.Globalization;
using NodaTime;
using TravelRepo.Core;
using TravelRepo.Repository;

namespace Jourfold.Mobile;

/// <summary>
/// A time as shown on a line. <see cref="Start"/> and <see cref="End"/> are in the line's main zone; <see cref="Zone"/>
/// names the place of that zone when a journey starts in another one; <see cref="Arrival"/> is the arrival of a
/// journey across zones in its own local time, with its place; <see cref="Other"/> is the start in the other zone that
/// matters (the phone's or the item's), with its place, quieter.
/// </summary>
public sealed record ClockText(string Start, string? End = null, string? Zone = null, string? Arrival = null, string? Other = null)
{
    /// <summary>One line, for example "11:00–13:00", "13:20 Berlin" or "All day".</summary>
    public string Primary => Start + (End is null ? "" : "–" + End) + (Zone is null ? "" : " " + Zone);
}

/// <summary>
/// How times read on the phone (specs/mobile/DECISIONS.md, 1 and 6). Today follows the phone's clock, with the item's
/// own local time added when its zone differs. Plan, details and bookings show the item's local time, with the phone's
/// time added when the phone is in another zone. Zones are named by their place, such as "Berlin", not by
/// abbreviations. A journey across zones always names both places.
/// </summary>
public static class TimeText
{
    private static string Clock(ZonedDateTime t) => t.TimeOfDay.ToString("HH:mm", CultureInfo.InvariantCulture);
    private static DateTimeZone Zone(string? id, DateTimeZone fallback) => id is null ? fallback : DateTimeZoneProviders.Tzdb.GetZoneOrNull(id) ?? fallback;
    private static bool Same(DateTimeZone a, DateTimeZone b, Instant at) => a.Id == b.Id || a.GetUtcOffset(at) == b.GetUtcOffset(at);

    /// <summary>The place a zone is named after, such as "Berlin" or "New York".</summary>
    public static string Place(DateTimeZone zone) => zone.Id is "UTC" or "Etc/UTC" ? "UTC" : Formats.ZoneName(zone.Id);

    /// <summary>
    /// What to call an item's zone: the town of the place where it starts or ends, read from its address, otherwise the
    /// place the zone is named after. Seattle is in the zone "America/Los_Angeles", but a flight there should say Seattle.
    /// </summary>
    private static string Place(DateTimeZone zone, TripSnapshot? trip, Entity item, bool end)
    {
        if (trip is null) return Place(zone);
        var (from, to) = ScheduleQueries.Route(trip, item);
        return TripSummaries.Town((end ? to : from) ?? ScheduleQueries.PrimaryPlace(trip, item)) ?? Place(zone);
    }

    /// <summary>The time of a line in the item's local time, for Plan and details.</summary>
    /// <param name="trip">The trip, to name zones by the item's places; without it they are named by the zone.</param>
    public static ClockText Local(AgendaEntry entry, DateTimeZone phone, Localization s, TripSnapshot? trip = null) => Format(entry, phone, s, phoneFirst: false, trip);

    /// <summary>The time of a line in the phone's time, for Today.</summary>
    public static ClockText OnPhone(AgendaEntry entry, DateTimeZone phone, Localization s, TripSnapshot? trip = null) => Format(entry, phone, s, phoneFirst: true, trip);

    private static ClockText Format(AgendaEntry entry, DateTimeZone phone, Localization s, bool phoneFirst, TripSnapshot? trip)
    {
        if (entry.Precision == TimePrecision.AllDay) return new(s["AllDay"]);
        if (entry.Precision == TimePrecision.DayPart && entry.Item.Data["time"]?["day_part"]?.ToString() is { } part) return new(s.Optional(part));
        if (entry.Start is not { } start) return new("");
        var startZone = Zone(entry.Zone, phone); var endZone = Zone(entry.EndZone ?? entry.Zone, startZone);
        var approximate = entry.Precision == TimePrecision.Approximate ? "~" : "";
        if (entry.Role == AgendaRole.Continued) return Continued(entry, endZone, phone, s, phoneFirst, trip);
        var end = entry.Role == AgendaRole.Item && entry.End is { } e && e > start ? entry.End : null;

        // A journey across zones: both ends in their own local time, each named, whatever the phone's zone.
        if (end is { } arrive && !Same(startZone, endZone, start))
            return new(approximate + Clock(start.InZone(startZone)), Zone: Place(startZone, trip, entry.Item, end: false),
                Arrival: Clock(arrive.InZone(endZone)) + " " + Place(endZone, trip, entry.Item, end: true) + (arrive.InZone(endZone).Date != start.InZone(startZone).Date ? " " + s["NextDayShort"] : ""));

        if (Same(startZone, phone, start)) return new(approximate + Clock(start.InZone(startZone)), end is { } same ? Clock(same.InZone(startZone)) : null);
        var mainZone = phoneFirst ? phone : startZone; var otherZone = phoneFirst ? startZone : phone;
        // On Today the other time is the item's, named by its place; in Plan it is the phone's, named by its zone.
        var otherPlace = phoneFirst ? Place(startZone, trip, entry.Item, end: false) : Place(phone);
        return new(approximate + Clock(start.InZone(mainZone)), end is { } local ? Clock(local.InZone(mainZone)) : null, Other: OtherTime(start, otherZone, otherPlace, mainZone, s, approximate));
    }

    /// <summary>
    /// "Berlin: 01:30", and the weekday when the other zone is on another calendar day, so 01:30 is not read as the
    /// same morning: "Berlin: 01:30 (Wed)".
    /// </summary>
    private static string OtherTime(Instant at, DateTimeZone zone, string place, DateTimeZone main, Localization s, string approximate = "")
    {
        var there = at.InZone(zone);
        var day = there.Date != at.InZone(main).Date ? " (" + there.Date.ToString("ddd", CultureInfo.GetCultureInfo(s.Language)) + ")" : "";
        return string.Format(CultureInfo.CurrentCulture, s["TimeIn"], place, approximate + Clock(there) + day);
    }

    /// <summary>
    /// A line that began on an earlier day: on the day it ends, the time it ends (the card says "Arrival" or "Ends");
    /// on the days between, it runs all day.
    /// </summary>
    private static ClockText Continued(AgendaEntry entry, DateTimeZone endZone, DateTimeZone phone, Localization s, bool phoneFirst, TripSnapshot? trip)
    {
        if (entry.End is not { } end || !EndsOnItsDay(entry)) return new(s["AllDay"]);
        var zone = phoneFirst ? phone : endZone;
        return new(Clock(end.InZone(zone)), Other: Same(endZone, phone, end) ? null
            : OtherTime(end, phoneFirst ? endZone : phone, phoneFirst ? Place(endZone, trip, entry.Item, end: true) : Place(phone), zone, s));
    }

    /// <summary>Whether a continued line ends on its own day rather than running on through it.</summary>
    public static bool EndsOnItsDay(AgendaEntry entry)
    {
        if (entry.End is not { } end || entry.Day is not { } day) return true;
        var local = end.InZone(Zone(entry.EndZone ?? entry.Zone, DateTimeZone.Utc));
        return (local.TimeOfDay == LocalTime.Midnight ? local.Date.PlusDays(-1) : local.Date) <= day;
    }

    /// <summary>"Times as on your phone (Berlin)" for Today.</summary>
    public static string PhoneHeading(DateTimeZone phone, Localization s) => string.Format(CultureInfo.CurrentCulture, s["TimesOnPhone"], Place(phone));

    /// <summary>
    /// "Local time Kyoto" for a day in Plan; on a day across zones every place in order, such as
    /// "Local time Berlin → Tokyo".
    /// </summary>
    public static string LocalHeading(AgendaDay day, DateTimeZone fallback, Localization s, TripSnapshot? trip = null)
    {
        // Only lines with a clock time say where the day is, in the order they happen.
        var places = day.Entries.Where(e => e.IsTimed).OrderBy(e => e.Role == AgendaRole.Continued ? e.End : e.Start)
            .SelectMany(e => new[]
            {
                e.Role == AgendaRole.Continued || e.Zone is null ? null : Place(Zone(e.Zone, fallback), trip, e.Item, end: false),
                (e.EndZone ?? (e.Role == AgendaRole.Continued ? e.Zone : null)) is { } to ? Place(Zone(to, fallback), trip, e.Item, end: true) : null
            })
            .OfType<string>().Distinct().ToList();
        if (places.Count == 0) places.Add(Place(day.Nights.FirstOrDefault() is { } night ? Zone(night.Run.Main.Zone, fallback) : fallback));
        return string.Format(CultureInfo.CurrentCulture, s["TimesLocal"], string.Join(" → ", places));
    }

    /// <summary>"in 20 min", "in 1 h 15 min" or "now".</summary>
    public static string Until(Instant now, Instant at, Localization s)
    {
        var minutes = (int)Math.Ceiling((at - now).TotalMinutes);
        if (minutes <= 0) return s["StartsNow"];
        return string.Format(CultureInfo.CurrentCulture, s["StartsIn"], minutes < 60 ? minutes + " min" : minutes / 60 + " h" + (minutes % 60 > 0 ? " " + minutes % 60 + " min" : ""));
    }
}
