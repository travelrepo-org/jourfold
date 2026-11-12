using System.Globalization;
using Jourfold.UI;
using NodaTime;
using TravelRepo.Core;
using TravelRepo.Git;
using TravelRepo.Repository;

namespace Jourfold.Mobile;

/// <summary>
/// The device's calendar, where the trips on this device appear as calendars of their own (read-only where the system
/// allows it, see <see cref="IsReadOnly"/>). Android keeps them under an account "Jourfold" on the device; iOS in the
/// calendars on the device, or in iCloud when the device has none of its own (see <see cref="Online"/>).
/// </summary>
public interface ISystemCalendar
{
    /// <summary>Whether the system's "Sync now" for the calendar reaches Jourfold, as Android's account does.</summary>
    bool SyncsFromSettings => false;
    /// <summary>Whether the calendars are read-only in calendar apps. iOS has no read-only calendars of an app's own.</summary>
    bool IsReadOnly => false;
    /// <summary>
    /// Whether the calendars are kept in an online account, such as iCloud or Google, so the plan also reaches the
    /// user's other devices and the account's provider; null while that is not known, as before the calendar may be
    /// used. The settings say so, with a conditional sentence while it is not known.
    /// </summary>
    bool? Online => false;
    /// <summary>The name of the online account the calendars are kept in, such as "iCloud"; null when there is none.</summary>
    string? AccountName => null;

    /// <summary>Whether the app may write to the calendar. With <paramref name="ask"/>, the user is asked first if needed.</summary>
    Task<bool> AllowAsync(bool ask, CancellationToken ct = default);

    /// <summary>
    /// Makes the device's Jourfold calendars exactly <paramref name="calendars"/>: new ones are added, others removed,
    /// and only the entries that changed are written.
    /// </summary>
    Task PublishAsync(IReadOnlyList<SystemCalendar> calendars, CancellationToken ct = default);

    /// <summary>Removes the Jourfold calendars, and on Android the account that holds them.</summary>
    Task TurnOffAsync(CancellationToken ct = default);
}

/// <summary>
/// One calendar: a trip, or one kind of a trip's plans. <see cref="Key"/> stays the same while the trip is on the
/// device: its folder, and the kind when split by kind. <see cref="Color"/> is ARGB.
/// </summary>
public sealed record SystemCalendar(string Key, string Name, uint Color, IReadOnlyList<SystemCalendarEntry> Entries);

/// <summary>
/// An entry as the calendar shows it. Timed entries have <see cref="Start"/> and <see cref="End"/> (equal for a single
/// moment); all-day entries have <see cref="Date"/>. <see cref="ColorKey"/> names the kind, whose colour is <see cref="Color"/>
/// (0 when not known, such as in the background before the app has drawn).
/// </summary>
public sealed record SystemCalendarEntry(string Uid, string Title, Instant? Start, Instant? End, LocalDate? Date, string? Zone, string? Location, string? Description, bool Tentative, string ColorKey, uint Color)
{
    /// <summary>Changes whenever anything the calendar shows changes, so unchanged entries are not written again.</summary>
    public string Fingerprint => string.Join('\u001f', Title, Start?.ToUnixTimeMilliseconds(), End?.ToUnixTimeMilliseconds(), Date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), Zone, Location, Description, Tentative, ColorKey, Color);
}

/// <summary>
/// Turns the trips on the device into calendars (one per trip, or one per trip and kind), and refreshes them for the
/// system's "Sync now", which may only take newer versions that need no decision.
/// </summary>
public static class SystemCalendars
{
    /// <summary>The kinds a trip splits into, in the order the calendar app lists them.</summary>
    public static readonly IReadOnlyList<string> Groups = ["transport", "stays", "food", "plans"];

    /// <summary>Held while a trip is updated in the background, so the app does not open it halfway.</summary>
    internal static readonly SemaphoreSlim TripGate = new(1, 1);

    public static string Group(ScheduleKind kind) => kind switch
    {
        ScheduleKind.Transport => "transport",
        ScheduleKind.Accommodation => "stays",
        ScheduleKind.Food => "food",
        _ => "plans",
    };

    /// <summary>
    /// The calendars for <paramref name="trips"/>, keyed by their folder name. Cancelled plans are left out; ideas
    /// and plans not yet booked are tentative. Stays appear as check-in and check-out.
    /// </summary>
    public static IReadOnlyList<SystemCalendar> Build(IEnumerable<(string Key, TripSnapshot Trip)> trips, Localization strings, bool byKind, Func<ScheduleKind, uint>? kindColor = null)
    {
        kindColor ??= _ => 0;
        var calendars = new List<SystemCalendar>();
        foreach (var (key, trip) in trips)
        {
            var entries = TripCalendar.Entries(trip, staysAsCheckInOut: true).Where(e => e.Status != "cancelled")
                .Select(e => (Group: Group(e.Kind), Entry: new SystemCalendarEntry(e.Uid, Title(e, strings), e.Start, e.End, e.Date, e.Zone, e.Location, e.Description,
                    e.Status is "idea" or "planned", e.Kind.ToString(), kindColor(e.Kind))))
                .ToArray();
            var title = trip.Manifest.Title;
            if (!byKind) { calendars.Add(new(key, title, Tint(title), entries.Select(x => x.Entry).ToArray())); continue; }
            foreach (var group in Groups)
            {
                var kind = group switch { "transport" => ScheduleKind.Transport, "stays" => ScheduleKind.Accommodation, "food" => ScheduleKind.Food, _ => ScheduleKind.Activity };
                calendars.Add(new(key + "/" + group, title + " · " + strings[group switch { "transport" => "CalendarTransport", "stays" => "CalendarStays", "food" => "CalendarFood", _ => "CalendarPlans" }], kindColor(kind), entries.Where(x => x.Group == group).Select(x => x.Entry).ToArray()));
            }
        }
        return calendars;
    }

    private static string Title(CalendarEntry entry, Localization strings) => entry.Role switch
    {
        CalendarEntryRole.CheckIn => string.Format(CultureInfo.CurrentCulture, strings["CalendarCheckIn"], entry.Item.Title),
        CalendarEntryRole.CheckOut => string.Format(CultureInfo.CurrentCulture, strings["CalendarCheckOut"], entry.Item.Title),
        _ => entry.Item.Title,
    };

    /// <summary>The trip's own colour, the one its generated cover starts with.</summary>
    private static uint Tint(string title) => uint.Parse(Covers.Tint(title).TrimStart('#'), NumberStyles.HexNumber, CultureInfo.InvariantCulture) | 0xFF000000;

    /// <summary>
    /// For the system's "Sync now", while nobody may be watching: takes each trip's newer versions from GitHub only when
    /// that is a plain fast-forward, then writes the calendars. Never sends; a trip with changes of its own, one where
    /// both sides changed, and the trip open in the app are left exactly as they are.
    /// </summary>
    /// <param name="host">Where the trips are and how Git runs.</param>
    /// <param name="open">The folder of the trip open in the app, if any.</param>
    /// <param name="kindColor">The kinds' colours, 0 where unknown.</param>
    /// <param name="ct">Cancels between trips.</param>
    /// <returns>How many trips took newer versions.</returns>
    public static async Task<int> RefreshAsync(MobileHost host, string? open, Func<ScheduleKind, uint>? kindColor = null, CancellationToken ct = default)
    {
        if (host.Calendar is not { } calendar) return 0;
        var settings = MobileSettings.Load(Path.Combine(host.DataRoot, "settings.json"));
        if (!On(settings) || !await calendar.AllowAsync(ask: false, ct)) return 0;
        var updated = 0; var trips = new List<(string, TripSnapshot)>();
        foreach (var folder in Directory.GetDirectories(host.TripsRoot).Order(StringComparer.Ordinal).Where(f => File.Exists(Path.Combine(f, "travel.yaml"))))
        {
            await TripGate.WaitAsync(ct);
            try
            {
                var repository = new TravelRepository(folder, Path.Combine(host.RecoveryRoot, Path.GetFileName(folder)));
                var isOpen = open is not null && Path.GetFullPath(open) == Path.GetFullPath(folder);
                if (!isOpen && settings.GitHubLogin is not null)
                {
                    try
                    {
                        var git = new GitRepository(repository, host.Git);
                        if ((await git.RemotesAsync(ct)).ContainsKey("origin") && await git.PullAsync("origin", ct) == SyncState.Synced) updated++;
                    }
                    catch (Exception ex) when (ex is DomainException or HttpRequestException or IOException) { }
                }
                trips.Add((Path.GetFileName(folder), (await repository.ReadAsync(ct)).Trip));
            }
            catch (DomainException) { }
            finally { TripGate.Release(); }
        }
        await calendar.PublishAsync(Build(trips, new Localization(host.Language), ByKind(settings), kindColor), ct);
        return updated;
    }

    internal static bool On(MobileSettings settings) => settings.Values.GetValueOrDefault("calendar") is "trips" or "kinds";
    internal static bool ByKind(MobileSettings settings) => settings.Values.GetValueOrDefault("calendar") == "kinds";
}
