using System.Text.Json;
using Jourfold.Application;
using Jourfold.UI;
using NodaTime;
using TravelRepo.Core;
using TravelRepo.Repository;

namespace Jourfold.Mobile;

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
            if (!On(settings) || !await notices.AllowAsync(ask: false, ct)) { notices.ShowLive(null, null); notices.WakeAt(null); notices.SetTicketShortcuts([]); return; }
            var now = host.Clock.GetCurrentInstant(); var phone = host.PhoneZone(); var strings = new Localization(host.Language) { Device = host.Device };
            var shownPath = Path.Combine(host.DataRoot, "notices-shown.json");
            var shown = Read(shownPath).Where(p => Instant.FromUnixTimeMilliseconds(p.Value) > now - Duration.FromDays(4)).ToDictionary();
            // Weather warnings come from the forecasts kept on the device; nothing is fetched here.
            var weather = settings.Values.GetValueOrDefault("weather.online") == "on"
                ? new TravelRepo.Providers.Weather.WeatherService([], null, new TravelRepo.Providers.Weather.WeatherCache(Path.Combine(host.DataRoot, "cache", "weather")), clock: host.Clock) : null;
            Instant? next = null; (LiveNotice Live, string Folder)? live = null; var shortcuts = new List<(Instant At, TicketShortcut Shortcut)>();
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
                    var covered = current is not null && notice.Kind is NoticeKind.Departure or NoticeKind.Connection && notice.Item == current.Item;
                    if (due && !shown.ContainsKey(notice.Key)) { if (!covered) notices.Show(notice, folder); shown[notice.Key] = now.ToUnixTimeMilliseconds(); }
                    else if (notice.At > now && (next is null || notice.At < next)) next = notice.At;
                }
            }
            notices.ShowLive(live?.Live, live?.Folder);
            notices.SetTicketShortcuts(shortcuts.OrderBy(t => t.At).Take(3).Select(t => t.Shortcut).ToArray());
            // While travelling the progress is redrawn every few minutes; otherwise the plan is read again twice a day.
            var wake = new[] { next, live?.Live.Changes, now + (live is null ? Duration.FromHours(12) : Duration.FromMinutes(5)) }.OfType<Instant>().Where(t => t > now).Min();
            notices.WakeAt(wake);
            File.WriteAllText(shownPath, JsonSerializer.Serialize(shown));
        }
        finally { Running.Release(); }
    }

    private static Dictionary<string, long> Read(string path)
    {
        try { return File.Exists(path) ? JsonSerializer.Deserialize<Dictionary<string, long>>(File.ReadAllText(path)) ?? [] : []; }
        catch (JsonException) { return []; }
    }
}
