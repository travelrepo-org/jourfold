using System.Text.Json;
using CoreGraphics;
using EventKit;
using Foundation;
using Jourfold.Mobile;
using NodaTime;

namespace Jourfold.iOS;

/// <summary>
/// The trips in the iOS calendar: a calendar per trip (or per trip and kind) in the calendars on the device, or in
/// iCloud when the device keeps none of its own. iOS cannot mark a calendar read-only, so each time the calendars are
/// written, entries that differ from the plan are written again, also after a change in the Calendar app. Each entry
/// carries its key as its URL, so it is found again; entries that match are left alone.
/// </summary>
public sealed class IosCalendar(string dataRoot) : ISystemCalendar
{
    private static readonly EKEventStore Store = new();
    private const string Scheme = "jourfold-entry:";

    private string StatePath => Path.Combine(dataRoot, "calendars.json");

    public async Task<bool> AllowAsync(bool ask, CancellationToken ct = default)
    {
        if (Allowed() || !ask || EKEventStore.GetAuthorizationStatus(EKEntityType.Event) != EKAuthorizationStatus.NotDetermined) return Allowed();
        if (OperatingSystem.IsIOSVersionAtLeast(17)) await Store.RequestFullAccessToEventsAsync();
        else await Store.RequestAccessAsync(EKEntityType.Event);
        return Allowed();
    }

    // From iOS 17 this is full access; write-only access cannot add calendars.
    private static bool Allowed() => EKEventStore.GetAuthorizationStatus(EKEntityType.Event) == EKAuthorizationStatus.Authorized;

    public Task PublishAsync(IReadOnlyList<SystemCalendar> calendars, CancellationToken ct = default) => Task.Run(() =>
    {
        var state = Load();
        foreach (var calendar in calendars)
        {
            ct.ThrowIfCancellationRequested();
            var kept = state.GetValueOrDefault(calendar.Key) ?? new Kept();
            if (kept.Id is not { } id || Store.GetCalendar(id) is not { } target)
            {
                target = EKCalendar.Create(EKEntityType.Event, Store);
                target.Source = Source();
                kept = new Kept();
            }
            target.Title = calendar.Name;
            if (calendar.Color != 0) target.CGColor = Color(calendar.Color);
            if (!Store.SaveCalendar(target, true, out var error)) throw new IOException("The calendar could not be added: " + error?.LocalizedDescription);
            kept.Id = target.CalendarIdentifier;
            Write(target, calendar.Entries, kept);
            state[calendar.Key] = kept;
        }
        // Calendars of trips that are gone, or of the other way of splitting, go with their entries.
        foreach (var key in state.Keys.Where(k => calendars.All(c => c.Key != k)).ToArray())
        {
            if (state[key].Id is { } id && Store.GetCalendar(id) is { } gone) Store.RemoveCalendar(gone, true, out _);
            state.Remove(key);
        }
        Save(state);
    }, ct);

    public Task TurnOffAsync(CancellationToken ct = default) => Task.Run(() =>
    {
        foreach (var kept in Load().Values)
            if (kept.Id is { } id && Store.GetCalendar(id) is { } calendar) Store.RemoveCalendar(calendar, true, out _);
        File.Delete(StatePath);
    }, ct);

    /// <summary>Adds, changes and removes the calendar's entries so they match; unchanged entries are left alone.</summary>
    private static void Write(EKCalendar calendar, IReadOnlyList<SystemCalendarEntry> entries, Kept kept)
    {
        // The calendar holds Jourfold's entries only. They are looked for between the earliest and latest of this time
        // and the last, which iOS allows up to four years apart.
        var times = entries.SelectMany(e => new[] { e.Start, e.End, e.Date?.AtMidnight().InUtc().ToInstant() }).OfType<Instant>().Select(t => t.ToUnixTimeMilliseconds())
            .Concat(kept.From is { } from ? [from, kept.To!.Value] : []).ToArray();
        var have = new Dictionary<string, List<EKEvent>>();
        if (times.Length > 0)
        {
            var first = Instant.FromUnixTimeMilliseconds(times.Min()) - Duration.FromDays(2);
            var last = Instant.FromUnixTimeMilliseconds(times.Max()) + Duration.FromDays(2);
            if (last - first > Duration.FromDays(4 * 365)) first = last - Duration.FromDays(4 * 365);
            foreach (var found in Store.EventsMatching(Store.PredicateForEvents(Date(first), Date(last), [calendar])) ?? [])
                if (found.Url?.AbsoluteString is { } url && url.StartsWith(Scheme, StringComparison.Ordinal))
                    (have.TryGetValue(url, out var list) ? list : have[url] = []).Add(found);
            kept.From = first.ToUnixTimeMilliseconds(); kept.To = last.ToUnixTimeMilliseconds();
        }
        foreach (var entry in entries)
        {
            var url = Scheme + Uri.EscapeDataString(entry.Uid);
            EKEvent target;
            if (have.Remove(url, out var events))
            {
                // Duplicates of one entry should not exist; if they do, one stays.
                foreach (var extra in events.Skip(1)) Store.RemoveEvent(extra, EKSpan.ThisEvent, false, out _);
                target = events[0];
                if (Matches(target, entry)) continue;
            }
            else target = EKEvent.FromStore(Store);
            target.Calendar = calendar; target.Url = new NSUrl(url);
            Fill(target, entry);
            if (!Store.SaveEvent(target, EKSpan.ThisEvent, false, out var error)) throw new IOException("An entry could not be written: " + error?.LocalizedDescription);
        }
        foreach (var left in have.Values.SelectMany(e => e)) Store.RemoveEvent(left, EKSpan.ThisEvent, false, out _);
        if (!Store.Commit(out var failed)) throw new IOException("The calendar could not be updated: " + failed?.LocalizedDescription);
    }

    /// <summary>Whether the entry in the calendar shows what the plan says.</summary>
    private static bool Matches(EKEvent target, SystemCalendarEntry entry)
    {
        bool Same(string? a, string? b) => (a ?? "") == (b ?? "");
        bool At(NSDate? date, Instant instant) => date is not null && Math.Abs(date.SecondsSince1970 - instant.ToUnixTimeSeconds()) < 1;
        var times = entry.Date is { } day
            ? target.AllDay && At(target.StartDate, AllDay(day))
            : !target.AllDay && At(target.StartDate, entry.Start!.Value) && At(target.EndDate, entry.End!.Value);
        return times && Same(target.Title, entry.Title) && Same(target.Notes, entry.Description) && Same(target.Location, entry.Location)
            && target.Availability == (entry.Tentative ? EKEventAvailability.Tentative : EKEventAvailability.Busy);
    }

    /// <summary>An all-day entry starts and ends on its day in the device's time zone, as the Calendar app writes them.</summary>
    private static Instant AllDay(LocalDate date) => date.AtStartOfDayInZone(DateTimeZoneProviders.Tzdb.GetSystemDefault()).ToInstant();

    private static void Fill(EKEvent target, SystemCalendarEntry entry)
    {
        target.Title = entry.Title; target.Notes = entry.Description; target.Location = entry.Location;
        target.Availability = entry.Tentative ? EKEventAvailability.Tentative : EKEventAvailability.Busy;
        if (entry.Date is { } date)
        {
            var day = Date(AllDay(date));
            target.TimeZone = null; target.AllDay = true; target.StartDate = day; target.EndDate = day;
        }
        else
        {
            target.AllDay = false;
            target.TimeZone = entry.Zone is { } zone ? NSTimeZone.FromName(zone) : null;
            target.StartDate = Date(entry.Start!.Value); target.EndDate = Date(entry.End!.Value);
        }
    }

    /// <summary>The device's own calendars, or iCloud's when the device keeps none (as when iCloud calendars are on).</summary>
    private static EKSource Source() =>
        Store.Sources.FirstOrDefault(s => s.SourceType == EKSourceType.Local)
        ?? Store.Sources.FirstOrDefault(s => s.SourceType == EKSourceType.CalDav && s.Title == "iCloud")
        ?? Store.DefaultCalendarForNewEvents?.Source
        ?? throw new IOException("This device has no calendar to add trips to.");

    private static NSDate Date(Instant instant) => NSDate.FromTimeIntervalSince1970(instant.ToUnixTimeMilliseconds() / 1000.0);

    private static CGColor Color(uint argb) => new((nfloat)((argb >> 16 & 0xFF) / 255.0), (nfloat)((argb >> 8 & 0xFF) / 255.0), (nfloat)((argb & 0xFF) / 255.0), 1);

    private Dictionary<string, Kept> Load()
    {
        try { return File.Exists(StatePath) ? JsonSerializer.Deserialize<Dictionary<string, Kept>>(File.ReadAllText(StatePath)) ?? [] : []; }
        catch (JsonException) { return []; }
    }

    private void Save(Dictionary<string, Kept> state) => File.WriteAllText(StatePath, JsonSerializer.Serialize(state));

    /// <summary>A calendar Jourfold made: its identifier and the time its entries were last looked for in.</summary>
    private sealed class Kept
    {
        public string? Id { get; set; }
        public long? From { get; set; }
        public long? To { get; set; }
    }
}
