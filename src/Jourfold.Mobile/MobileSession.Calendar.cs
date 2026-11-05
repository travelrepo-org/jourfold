using Avalonia.Media;
using Avalonia.Styling;
using Jourfold.UI;
using TravelRepo.Core;

namespace Jourfold.Mobile;

public sealed partial class MobileSession
{
    private CancellationTokenSource? calendarWait;

    /// <summary>Whether the trips appear in the device's calendar app; off until the user turns it on.</summary>
    public bool CalendarOn => SystemCalendars.On(Settings);
    /// <summary>Whether each trip is split into one calendar per kind.</summary>
    public bool CalendarByKind => SystemCalendars.ByKind(Settings);
    /// <summary>Why the calendar is not up to date, such as a missing permission; null when it is.</summary>
    public string? CalendarProblem { get; private set; }

    /// <summary>
    /// Turns the trips in the calendar app on or off, or changes how they are split. Turning on asks for the
    /// permission; turning off removes the calendars.
    /// </summary>
    public async Task SetCalendarAsync(bool on, bool byKind, CancellationToken ct = default)
    {
        if (Host.Calendar is not { } calendar) return;
        CalendarProblem = null;
        if (on && !await calendar.AllowAsync(ask: true, ct)) { CalendarProblem = Strings["CalendarNotAllowed"]; Changed?.Invoke(); return; }
        Settings.Values["calendar"] = on ? byKind ? "kinds" : "trips" : "off"; Settings.Save();
        try
        {
            if (on) await UpdateCalendarAsync(ct); else await calendar.TurnOffAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { CalendarProblem = Strings["CalendarFailed"]; }
        Changed?.Invoke();
    }

    /// <summary>Writes the trips on this device to their calendars, the open trip as it is now.</summary>
    public async Task UpdateCalendarAsync(CancellationToken ct = default)
    {
        if (Host.Calendar is not { } calendar || !CalendarOn) return;
        if (!await calendar.AllowAsync(ask: false, ct)) { CalendarProblem = Strings["CalendarNotAllowed"]; return; }
        var open = Workspace;
        var trips = Trips.Select(card => (Path.GetFileName(card.Path), open is not null && Path.GetFullPath(open.Repository.Root) == Path.GetFullPath(card.Path) ? open.State.Trip : card.Trip)).ToArray();
        await calendar.PublishAsync(SystemCalendars.Build(trips, Strings, CalendarByKind, KindColor), ct);
        CalendarProblem = null;
    }

    /// <summary>
    /// Updates the calendars and the notifications once the trips have been quiet for a moment, so typing does not
    /// write each letter.
    /// </summary>
    private void UpdateCalendarSoon()
    {
        if ((Host.Calendar is null || !CalendarOn) && (Host.Notices is null || !NoticesOn)) return;
        calendarWait?.Cancel(); var wait = calendarWait = new CancellationTokenSource();
        _ = Later();
        async Task Later()
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(2), wait.Token);
                await UpdateCalendarAsync(wait.Token);
                if (NoticesOn) await NoticeRunner.RunAsync(Host, ct: wait.Token);
            }
            catch (OperationCanceledException) { }
            catch (Exception) { CalendarProblem = Strings["CalendarFailed"]; }
        }
    }

    /// <summary>
    /// The kind's colour as the light theme draws its bar; 0 when the app's resources cannot be read, such as in the
    /// background, where the calendar keeps the colours it has.
    /// </summary>
    public static uint KindColor(ScheduleKind kind)
    {
        try
        {
            return Avalonia.Application.Current?.TryGetResource("Kind." + Visuals.KindKey(kind) + ".Bar", ThemeVariant.Light, out var value) == true && value is ISolidColorBrush brush ? brush.Color.ToUInt32() : 0;
        }
        catch (InvalidOperationException) { return 0; }
    }
}
