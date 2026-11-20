using System.Text.Json;
using Jourfold.Application;
using Jourfold.UI;
using NodaTime;
using TravelRepo.Core;
using TravelRepo.Repository;

namespace Jourfold.Mobile;

/// <summary>How the system treats the travel day's ongoing notification beyond listing it.</summary>
public enum LiveSupport
{
    /// <summary>The system has nothing more than the notification itself, as Android before 16.</summary>
    None,
    /// <summary>The system could show it in the status bar, but the user turned that off for the app.</summary>
    Off,
    /// <summary>The system may show it as a Live Update, with the time in the status bar.</summary>
    On
}

/// <summary>
/// The keys of the texts about the travel day's system surface, whole so they can be searched for: its name, what the
/// system allows, the button to allow it, and what showing the example came to.
/// </summary>
public sealed record LiveTexts(string Title, string On, string Off, string None, string Allow, string Promoted, string Plain)
{
    /// <summary>Android 16's Live Updates in the status bar.</summary>
    public static readonly LiveTexts Updates = new("LiveUpdates", "LiveUpdatesOn", "LiveUpdatesOff", "LiveUpdatesNone", "LiveUpdatesAllow", "LiveUpdatesPromoted", "LiveUpdatesPlain");

    /// <summary>iOS's Live Activities on the Lock Screen and in the Dynamic Island.</summary>
    public static readonly LiveTexts Activities = new("LiveActivities", "LiveActivitiesOn", "LiveActivitiesOff", "LiveActivitiesNone", "LiveActivitiesAllow", "LiveActivitiesPromoted", "LiveActivitiesPlain");

    public IEnumerable<string> All => [Title, On, Off, None, Allow, Promoted, Plain];
}

/// <summary>
/// The device's notifications, as the platform shows them. Jourfold works out what to show and when to wake up again;
/// the platform shows it and wakes the app at that time, also when it is not running.
/// </summary>
public interface ISystemNotices
{
    /// <summary>Whether the app may show notifications; with <paramref name="ask"/> the user is asked first if needed.</summary>
    Task<bool> AllowAsync(bool ask, CancellationToken ct = default);
    /// <summary>Whether reminders come on time to the minute; without the permission the system may delay them a little.</summary>
    bool OnTime { get; }
    /// <summary>Opens the system page where the user allows reminders on time.</summary>
    void AskOnTime();
    /// <summary>Opens the system page where the user chooses which kinds of notifications to get.</summary>
    void OpenSettings();
    void Show(TravelNotice notice, string tripFolder);
    /// <summary>Shows, updates or (with null) removes the travel day's ongoing notification.</summary>
    void ShowLive(LiveNotice? live, string? tripFolder);
    /// <summary>Wakes the app at <paramref name="at"/> to run <see cref="NoticeRunner.RunAsync"/>; null cancels.</summary>
    void WakeAt(Instant? at);
    /// <summary>Shortcuts on the app's icon to the tickets of journeys that are close, replacing the ones before.</summary>
    void SetTicketShortcuts(IReadOnlyList<TicketShortcut> tickets);
    /// <summary>
    /// Whether the travel day appears as an ongoing notification (<see cref="ShowLive"/>). Where it does, departures it
    /// already counts down to are not announced again.
    /// </summary>
    bool ShowsLive => true;
    /// <summary>Whether the system may show the travel day in the status bar as well (Android 16's Live Updates).</summary>
    LiveSupport Live => LiveSupport.None;
    /// <summary>
    /// The keys the settings word the travel day's system surface with: Android's Live Updates in the status bar or
    /// iOS's Live Activities on the Lock Screen.
    /// </summary>
    LiveTexts LiveTexts => LiveTexts.Updates;
    /// <summary>Opens the system page where the user allows Live Updates for the app.</summary>
    void OpenLiveSettings() { }
    /// <summary>
    /// Whether the system shows the travel day's notification as a Live Update right now; null when it cannot say or
    /// nothing is shown.
    /// </summary>
    bool? LivePromoted => null;
    /// <summary>
    /// Whether the phone's maker shows Live Updates only for apps it approved, as Samsung's One UI does, unless the user
    /// lets all apps through in the developer options.
    /// </summary>
    bool LiveByMakerOnly => false;
    /// <summary>Whether the system settings let the user choose kinds of notifications, as Android's channels do.</summary>
    bool HasKinds => true;
    /// <summary>
    /// The notices still to come, soonest first, replacing the ones handed over before. Platforms that cannot wake the
    /// app on time, such as iOS, schedule them to show by themselves; <see cref="Show"/> then leaves out a notice the
    /// system has already shown.
    /// </summary>
    void Schedule(IReadOnlyList<(TravelNotice Notice, string TripFolder)> coming) { }
}

/// <summary>
/// Shows the notifications that are due across the trips on the device, each once, keeps the travel day's
/// notification up to date, and asks to be woken for the next one. It runs when the app starts, after the trips
/// change, when the time zone changes, after a restart and when it woke itself.
/// </summary>
/// <summary>A shortcut to a journey's ticket, such as "Ticket: Flight to Tokyo".</summary>
public sealed record TicketShortcut(string Label, string TripFolder, Guid Item);

public static class NoticeRunner
{
    private static readonly SemaphoreSlim Running = new(1, 1);

    public static bool On(MobileSettings settings) => settings.Values.GetValueOrDefault("notices") == "on";

    /// <param name="zoneChanged">The phone's time zone just changed, as after a long flight: an arrival up to three hours
    /// ahead of plan is shown now.</param>
    public static async Task RunAsync(MobileHost host, bool zoneChanged = false, CancellationToken ct = default)
    {
        if (host.Notices is not { } notices) return;
        await Running.WaitAsync(ct);
        try
        {
            var settings = MobileSettings.Load(Path.Combine(host.DataRoot, "settings.json"));
            if (!On(settings) || !await notices.AllowAsync(ask: false, ct)) { notices.ShowLive(null, null); notices.WakeAt(null); notices.SetTicketShortcuts([]); notices.Schedule([]); return; }
            var now = host.Clock.GetCurrentInstant(); var phone = host.PhoneZone(); var strings = new Localization(host.Language) { Device = host.Device };
            var shownPath = Path.Combine(host.DataRoot, "notices-shown.json");
            var shown = Read(shownPath).Where(p => Instant.FromUnixTimeMilliseconds(p.Value) > now - Duration.FromDays(4)).ToDictionary();
            // Weather warnings come from the forecasts kept on the device; nothing is fetched here.
            var weather = settings.Values.GetValueOrDefault("weather.online") == "on"
                ? TravelRepo.Providers.Weather.WeatherService.CacheOnly(new TravelRepo.Providers.Weather.WeatherCache(Path.Combine(host.DataRoot, "cache", "weather")), clock: host.Clock) : null;
            Instant? next = null; (LiveNotice Live, string Folder)? live = null; var shortcuts = new List<(Instant At, TicketShortcut Shortcut)>(); var coming = new List<(TravelNotice, string)>();
            foreach (var folder in Directory.GetDirectories(host.TripsRoot).Order(StringComparer.Ordinal).Where(f => File.Exists(Path.Combine(f, "travel.yaml"))))
            {
                TripSnapshot trip;
                try { trip = (await new TravelRepository(folder, Path.Combine(host.RecoveryRoot, Path.GetFileName(folder))).ReadAsync(ct)).Trip; }
                catch (DomainException) { continue; }
                var me = TripMe.Resolve(trip, settings.Values.GetValueOrDefault(MobileSession.MeSetting(folder)), settings.GitHubLogin, settings.Email);
                var current = TravelNotices.Live(trip, me, now, phone, strings);
                // Tickets of journeys from an hour ago to the next day sit on the app's icon.
                foreach (var journey in trip.Entities.Values.Where(e => e.Type == "schedule_item" && e.Data["components"]?["transport"] is not null && TripMe.Concerns(trip, e, me)))
                    if (ScheduleQueries.Span(trip, journey) is { IsPlaced: true, Start: { } start, End: { } end } && end > now - Duration.FromHours(1) && start < now + Duration.FromDays(1) && Tickets.For(trip, journey).Count > 0)
                        shortcuts.Add((start, new(string.Format(System.Globalization.CultureInfo.CurrentCulture, strings["TicketFor"], journey.Title), folder, journey.Id)));
                if (live is null && current is not null) live = (current, folder);
                foreach (var notice in TravelNotices.Plan(trip, me, now, phone, strings, weather?.Cached(trip)))
                {
                    var due = notice.At <= now || zoneChanged && notice.Kind == NoticeKind.Arrival && notice.At - now <= Duration.FromHours(3);
                    // The travel day's notification already says when a journey leaves; it announces itself once instead.
                    var covered = notices.ShowsLive && current is not null && notice.Kind is NoticeKind.Departure or NoticeKind.Connection && notice.Item == current.Item;
                    if (due && !shown.ContainsKey(notice.Key)) { if (!covered) notices.Show(notice, folder); shown[notice.Key] = now.ToUnixTimeMilliseconds(); }
                    else if (notice.At > now)
                    {
                        if (next is null || notice.At < next) next = notice.At;
                        if (!shown.ContainsKey(notice.Key)) coming.Add((notice, folder));
                    }
                }
            }
            // The example from the settings takes the travel day's place for its minute and moves on like a journey.
            var example = Example(settings, now, phone, strings);
            if (example is { } shownExample) live = shownExample;
            // Swiped away, the travel day stays away until its next part: the departure, the arrival or a change.
            var dismissed = live is { } today && File.Exists(DismissedPath(host)) && File.ReadAllText(DismissedPath(host)).Trim() == today.Live.Key;
            notices.ShowLive(dismissed ? null : live?.Live, dismissed ? null : live?.Folder);
            notices.Schedule(coming.OrderBy(c => c.Item1.At).ToArray());
            notices.SetTicketShortcuts(shortcuts.OrderBy(t => t.At).Take(3).Select(t => t.Shortcut).ToArray());
            // While travelling the notification is redrawn as the progress and the minutes left move on, the example every
            // few seconds; swiped away, it waits for its next part; otherwise the plan is read again twice a day.
            var redraw = dismissed ? now + Duration.FromHours(12) : example is not null ? now + ExampleStep : live is { } shownLive ? shownLive.Live.Redraw(now) : now + Duration.FromHours(12);
            var wake = new[] { next, live?.Live.Changes, redraw }.OfType<Instant>().Where(t => t > now).Min();
            notices.WakeAt(wake);
            File.WriteAllText(shownPath, JsonSerializer.Serialize(shown));
        }
        finally { Running.Release(); }
    }

    private static string DismissedPath(MobileHost host) => Path.Combine(host.DataRoot, "live-dismissed.txt");

    /// <summary>
    /// Remembers that the user swiped the travel day's notification away, for the part it showed (<see cref="LiveNotice.Key"/>).
    /// A file of its own, so the settings the app keeps in memory do not write over it.
    /// </summary>
    public static void Dismiss(MobileHost host, string key)
    {
        try { File.WriteAllText(DismissedPath(host), key); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>The setting that holds when the example was started and from which trip, as "milliseconds|folder".</summary>
    public const string ExampleSetting = "live.example";

    /// <summary>How long the example stays: a short journey that arrives when it goes away.</summary>
    public static readonly Duration ExampleTime = Duration.FromMinutes(1);

    /// <summary>How often the example is drawn again, so its progress and the minutes left visibly move on.</summary>
    public static readonly Duration ExampleStep = Duration.FromSeconds(10);

    /// <summary>
    /// The example journey while it runs: a train that left a minute before it was started and arrives a minute after,
    /// when the system takes it away. Null before and after.
    /// </summary>
    public static (LiveNotice Live, string Folder)? Example(MobileSettings settings, Instant now, DateTimeZone phone, Localization s)
    {
        var value = settings.Values.GetValueOrDefault(ExampleSetting);
        if (value?.Split('|', 2) is not [var ms, var folder] || !long.TryParse(ms, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var started)) return null;
        var from = Instant.FromUnixTimeMilliseconds(started); var until = from + ExampleTime;
        if (now < from || now >= until) return null;
        var title = string.Format(System.Globalization.CultureInfo.CurrentCulture, s["NoticeArrives"], s["LiveExampleJourney"], until.InZone(phone).ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture));
        var live = new LiveNotice(title, s["LiveExampleText"], from - ExampleTime, until, until, Guid.Empty, false, (until - now).ToTimeSpan())
        { Chip = LiveNotice.ChipFor(until, now, phone, s), Icon = "train-front" };
        return (live, folder);
    }

    private static Dictionary<string, long> Read(string path)
    {
        try { return File.Exists(path) ? JsonSerializer.Deserialize<Dictionary<string, long>>(File.ReadAllText(path)) ?? [] : []; }
        catch (JsonException) { return []; }
    }
}
