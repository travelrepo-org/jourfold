using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using NodaTime;
using NodaTime.Text;
using TravelRepo.Core;

namespace Jourfold.Desktop;

/// <summary>
/// Reminders from TravelRepo's <see cref="TripReminders"/>, with what the person did about them: dismissed or
/// postponed. That is remembered on this computer only, so it never changes the trip for the other travellers.
/// Urgent reminders also become a notification of the operating system once, while Jourfold runs.
/// </summary>
public partial class MainViewModel
{
    [ObservableProperty] private IReadOnlyList<Reminder> reminders = [];
    public int UrgentReminders => Reminders.Count(r => r.Urgent);
    partial void OnRemindersChanged(IReadOnlyList<Reminder> value) => OnPropertyChanged(nameof(UrgentReminders));

    /// <summary>Called with each urgent reminder that has not been shown as a desktop notification yet.</summary>
    public Func<string, string, Task>? DesktopNotifier { get; set; }
    public bool DesktopNotificationsEnabled => Store.Get("notifications.desktop") != "off";

    /// <summary>The current moment; tests replace it.</summary>
    public Func<Instant> Clock { get; set; } = () => SystemClock.Instance.GetCurrentInstant();

    private string ReminderStoreKey(string kind) => "reminders." + kind + ":" + Workspace?.Repository.Root;
    private HashSet<string> Keys(string kind) => (Store.Get(ReminderStoreKey(kind)) ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
    private void SaveKeys(string kind, IEnumerable<string> keys) => Store.Set(ReminderStoreKey(kind), string.Join('\n', keys));
    private Dictionary<string, string> Snoozed() { try { return JsonSerializer.Deserialize<Dictionary<string, string>>(Store.Get(ReminderStoreKey("snoozed")) ?? "{}") ?? []; } catch (JsonException) { return []; } }

    /// <summary>Recomputes the reminders and sends desktop notifications for new urgent ones.</summary>
    public async Task UpdateRemindersAsync()
    {
        if (Workspace is null) { Reminders = []; return; }
        var now = Clock();
        var dismissed = Keys("dismissed"); var snoozed = Snoozed();
        bool Postponed(Reminder r) => snoozed.TryGetValue(r.Key, out var until) && InstantPattern.ExtendedIso.Parse(until).TryGetValue(default, out var moment) && moment > now;
        IReadOnlyList<Reminder> all = TripReminders.For(Workspace.State.Trip, now);
        if (GoneVariantsReminder() is { } gone) all = [.. all, gone];
        var current = all.Where(r => !dismissed.Contains(r.Key) && !Postponed(r)).ToArray();
        if (!current.Select(r => r.Key).SequenceEqual(Reminders.Select(r => r.Key)) || current.Count(r => r.Urgent) != UrgentReminders) Reminders = current;
        if (!DesktopNotificationsEnabled || DesktopNotifier is null) return;
        var notified = Keys("notified");
        foreach (var reminder in current.Where(r => r.Urgent && !notified.Contains(r.Key)))
        {
            notified.Add(reminder.Key);
            await DesktopNotifier(ReminderTitle(reminder), ReminderText(reminder));
        }
        // Keep the list short: only keys of reminders that still exist, including postponed ones, so they are not notified again.
        SaveKeys("notified", notified.Where(k => all.Any(r => r.Key == k)));
    }

    public Task DismissReminderAsync(Reminder reminder) { var keys = Keys("dismissed"); keys.Add(reminder.Key); SaveKeys("dismissed", keys); return UpdateRemindersAsync(); }

    /// <summary>Hides a reminder for a day.</summary>
    public Task SnoozeReminderAsync(Reminder reminder)
    {
        var snoozed = Snoozed(); snoozed[reminder.Key] = InstantPattern.ExtendedIso.Format(Clock() + Duration.FromDays(1));
        Store.Set(ReminderStoreKey("snoozed"), JsonSerializer.Serialize(snoozed)); return UpdateRemindersAsync();
    }

    /// <summary>Opens what a reminder is about.</summary>
    public void ShowReminder(Reminder reminder)
    {
        if (Workspace is null) return;
        if (reminder.Kind == ReminderKind.VariantsDeletedOnline) { _ = CleanUpVariantsCommand.ExecuteAsync(null); return; }
        var trip = Workspace.State.Trip; var entity = reminder.Entity is { } id ? trip.Find(id) : null;
        switch (reminder.Kind)
        {
            case ReminderKind.TaskDue: View = "Tasks"; break;
            case ReminderKind.UnconfirmedBooking: View = "Bookings"; break;
            case ReminderKind.CancellationDeadline when entity?.Type == "booking": View = "Bookings"; break;
            default:
                View = "Plan";
                if (reminder.Dates.FirstOrDefault() is { } day && day != default) ShowDate(day);
                else if (entity is not null && Stays.Resolve(trip, entity)?.FirstNight is { } night) ShowDate(night);
                break;
        }
        Selected = entity;
    }

    public string ReminderTitle(Reminder r) => r.Kind == ReminderKind.NightsWithoutStay ? Strings["ReminderNightsTitle"] : r.Title;

    /// <summary>One sentence that says what the reminder is about, in the trip's timezone.</summary>
    public string ReminderText(Reminder r)
    {
        var s = Strings; var zone = Workspace is { } w ? ScheduleQueries.TripZone(w.State.Trip) : DateTimeZone.Utc;
        string When(Instant moment) { var local = moment.InZone(zone); return Formats.LongDay(local.Date) + " · " + Formats.Time(local.TimeOfDay); }
        return r.Kind switch
        {
            ReminderKind.CancellationDeadline => r.Moment is { } m && m < Clock() ? string.Format(s["ReminderCancellationPassed"], When(m)) : string.Format(s["ReminderCancellation"], r.Moment is { } d ? When(d) : ""),
            ReminderKind.TaskDue => r.Dates.FirstOrDefault() is var due && due < Clock().InZone(zone).Date ? string.Format(s["ReminderTaskOverdue"], Formats.LongDay(due)) : string.Format(s["ReminderTaskDue"], Formats.LongDay(due)),
            ReminderKind.LateArrival => string.Format(s["ReminderLateArrival"], r.Moment is { } a ? Formats.Time(a.InZone(zone).TimeOfDay) : "", Workspace?.State.Trip.Find(r.Entity ?? default) is { } stay && Stays.Resolve(Workspace.State.Trip, stay)?.CheckInWindow?.Until is { } until ? Formats.Time(until) : ""),
            ReminderKind.NightsWithoutStay => string.Format(r.Dates.Count == 1 ? s["ReminderOneNight"] : s["ReminderNights"], r.Dates.Count, string.Join(", ", r.Dates.Select(Formats.ShortDate))),
            ReminderKind.VariantsDeletedOnline => string.Format(GoneVariants.Count == 1 ? s["VariantsDeletedOnlineOne"] : s["VariantsDeletedOnlineMany"], GoneVariants.Count, string.Join(", ", GoneVariants.Select(v => v.Title))),
            ReminderKind.UnconfirmedBooking => string.Format(s["ReminderUnconfirmed"], r.Moment is { } b ? Formats.LongDay(b.InZone(zone).Date) : ""),
            _ => ""
        };
    }

    public void SetDesktopNotifications(bool enabled) { Store.Set("notifications.desktop", enabled ? "on" : "off"); OnPropertyChanged(nameof(DesktopNotificationsEnabled)); }
}
