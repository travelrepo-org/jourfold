using Android;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Jourfold.Mobile;
using Jourfold.UI;
using NodaTime;

namespace Jourfold.Android;

/// <summary>
/// Jourfold's notifications on Android: one channel per kind, so the user can silence each in the system settings; the
/// travel day as an ongoing notification with a countdown or progress, which Android 16 shows as a Live Update in the
/// status bar; and an alarm that wakes the app for the next notice, on time to the minute when the user allows it.
/// </summary>
public sealed class AndroidNotices(Context context, string language, string device) : ISystemNotices
{
    private const int RequestCode = 4713, LiveId = 1;
    private const string LiveChannel = "travel_live", TravelDay = "travel_day", Reminders = "reminders", Deadlines = "deadlines";
    public const string ExtraTrip = "dev.jourfold.app.trip", ExtraItem = "dev.jourfold.app.item", ExtraTicket = "dev.jourfold.app.ticket", ActionWake = "dev.jourfold.app.NOTICES";
    private static TaskCompletionSource<bool>? asking;

    private NotificationManager Manager => (NotificationManager)context.GetSystemService(Context.NotificationService)!;
    private AlarmManager Alarms => (AlarmManager)context.GetSystemService(Context.AlarmService)!;

    public async Task<bool> AllowAsync(bool ask, CancellationToken ct = default)
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(33)) return Manager.AreNotificationsEnabled();
        bool Granted() => context.CheckSelfPermission(Manifest.Permission.PostNotifications) == Permission.Granted;
        if (Granted() || !ask || MainActivity.Current is not { } activity) return Granted();
        asking = new TaskCompletionSource<bool>();
        activity.RequestPermissions([Manifest.Permission.PostNotifications], RequestCode);
        return await asking.Task && Granted();
    }

    /// <summary>The user's answer to the permission question.</summary>
    public static void Answered(int requestCode, Permission[] results)
    {
        if (requestCode == RequestCode) asking?.TrySetResult(results.Length > 0 && results[0] == Permission.Granted);
    }

    public bool OnTime => !OperatingSystem.IsAndroidVersionAtLeast(31) || Alarms.CanScheduleExactAlarms();

    public void AskOnTime()
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(31)) return;
        context.StartActivity(new Intent(global::Android.Provider.Settings.ActionRequestScheduleExactAlarm, global::Android.Net.Uri.Parse("package:" + context.PackageName)).AddFlags(ActivityFlags.NewTask));
    }

    public void OpenSettings() => context.StartActivity(new Intent(global::Android.Provider.Settings.ActionAppNotificationSettings)
        .PutExtra(global::Android.Provider.Settings.ExtraAppPackage, context.PackageName).AddFlags(ActivityFlags.NewTask));

    public void Show(TravelNotice notice, string tripFolder)
    {
        Channels();
        var channel = notice.Kind switch
        {
            NoticeKind.Departure or NoticeKind.Connection or NoticeKind.Arrival => TravelDay,
            NoticeKind.Deadline or NoticeKind.TaskDue or NoticeKind.Packing => Deadlines,
            _ => Reminders,
        };
        var code = Code(notice.Key);
        var builder = new Notification.Builder(context, channel).SetSmallIcon(Resource.Drawable.ic_notification)!
            .SetContentTitle(notice.Title)!.SetContentText(notice.Text)!.SetStyle(new Notification.BigTextStyle().BigText(notice.Text))!
            .SetAutoCancel(true)!.SetWhen(notice.At.ToUnixTimeMilliseconds())!.SetShowWhen(true)!
            .SetContentIntent(Open(tripFolder, notice.Item, ticket: false, code))!;
        if (notice.Ticket) builder.AddAction(new Notification.Action.Builder(null, Strings["ShowTicket"], Open(tripFolder, notice.Item, ticket: true, code + 1)).Build());
        Manager.Notify(code, builder.Build());
    }

    public void ShowLive(LiveNotice? live, string? tripFolder)
    {
        if (live is null || tripFolder is null) { Manager.Cancel(LiveId); return; }
        Channels();
        var builder = new Notification.Builder(context, LiveChannel).SetSmallIcon(Resource.Drawable.ic_notification)!
            .SetContentTitle(live.Title)!.SetContentText(live.Text)!.SetOngoing(true)!.SetOnlyAlertOnce(true)!.SetCategory(Notification.CategoryProgress)!
            .SetContentIntent(Open(tripFolder, live.Item, ticket: false, LiveId))!;
        if (live.Until is { } until)
        {
            // The system counts down to the departure or arrival itself, also between updates.
            builder.SetWhen(until.ToUnixTimeMilliseconds())!.SetShowWhen(true)!.SetUsesChronometer(true)!.SetChronometerCountDown(true);
            if (live.From is { } from && until > from)
            {
                var done = (int)Math.Clamp((NodaTime.SystemClock.Instance.GetCurrentInstant() - from).TotalMinutes / (until - from).TotalMinutes * 100, 0, 100);
                builder.SetProgress(100, done, false);
            }
        }
        if (live.Ticket) builder.AddAction(new Notification.Action.Builder(null, Strings["ShowTicket"], Open(tripFolder, live.Item, ticket: true, LiveId + 1)).Build());
        // Android 16 shows a promoted ongoing notification as a Live Update, with a chip in the status bar.
        var extras = new Bundle(); extras.PutBoolean("android.requestPromotedOngoing", true);
        if (live.Until is { } chip) extras.PutString("android.shortCriticalText", chip.InZone(DateTimeZoneProviders.Tzdb.GetSystemDefault()).ToString("HH:mm", null));
        builder.AddExtras(extras);
        Manager.Notify(LiveId, builder.Build());
    }

    public void WakeAt(Instant? at)
    {
        var wake = PendingIntent.GetBroadcast(context, 0, new Intent(context, typeof(NoticeReceiver)).SetAction(ActionWake), PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent)!;
        if (at is not { } when) { Alarms.Cancel(wake); return; }
        var ms = when.ToUnixTimeMilliseconds();
        if (OnTime) Alarms.SetExactAndAllowWhileIdle(AlarmType.RtcWakeup, ms, wake);
        else Alarms.SetAndAllowWhileIdle(AlarmType.RtcWakeup, ms, wake);
    }

    public void SetTicketShortcuts(IReadOnlyList<TicketShortcut> tickets)
    {
        if (context.GetSystemService(Java.Lang.Class.FromType(typeof(ShortcutManager))) is not ShortcutManager shortcuts) return;
        shortcuts.SetDynamicShortcuts(tickets.Select(t =>
        {
            var intent = new Intent(context, typeof(MainActivity)).SetAction(Intent.ActionView)!.PutExtra(ExtraTrip, t.TripFolder)!.PutExtra(ExtraItem, t.Item.ToString())!.PutExtra(ExtraTicket, true)!;
            return new ShortcutInfo.Builder(context, "ticket-" + t.Item).SetShortLabel(t.Label)!.SetLongLabel(t.Label)!
                .SetIcon(global::Android.Graphics.Drawables.Icon.CreateWithResource(context, Resource.Mipmap.ic_launcher))!.SetIntent(intent)!.Build()!;
        }).ToList());
    }

    private Localization Strings => new(language) { Device = device };

    private void Channels()
    {
        var s = Strings;
        NotificationChannel Channel(string id, string name, string hint, NotificationImportance importance) => new(id, s[name], importance) { Description = s[hint] };
        Manager.CreateNotificationChannels([
            // The travel day sounds once when it appears; its updates are silent (only alert once).
            Channel(LiveChannel, "ChannelTravelDay", "ChannelTravelDayHint", NotificationImportance.Default),
            Channel(TravelDay, "ChannelTravelDay", "ChannelTravelDayHint", NotificationImportance.High),
            Channel(Reminders, "ChannelReminders", "ChannelRemindersHint", NotificationImportance.Default),
            Channel(Deadlines, "ChannelDeadlines", "ChannelDeadlinesHint", NotificationImportance.Default)]);
    }

    private PendingIntent Open(string tripFolder, Guid? item, bool ticket, int code)
    {
        var intent = new Intent(context, typeof(MainActivity)).SetAction(Intent.ActionView)!.AddFlags(ActivityFlags.NewTask | ActivityFlags.SingleTop)!
            .PutExtra(ExtraTrip, tripFolder)!.PutExtra(ExtraTicket, ticket)!;
        if (item is { } id) intent.PutExtra(ExtraItem, id.ToString());
        return PendingIntent.GetActivity(context, code, intent, PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent)!;
    }

    /// <summary>A notification id that stays the same for a key across app starts (string hashes do not).</summary>
    private static int Code(string key)
    {
        var hash = 2166136261u;
        foreach (var c in key) hash = (hash ^ c) * 16777619u;
        return (int)(hash & 0x3FFFFFFF) + 16;
    }
}

/// <summary>
/// Runs Jourfold's notices when the alarm it set goes off, after a restart, when the time or time zone changes (such
/// as after landing), and after an update of the app.
/// </summary>
[BroadcastReceiver(Name = "dev.jourfold.app.NoticeReceiver", Exported = true)]
[IntentFilter([Intent.ActionBootCompleted, Intent.ActionTimezoneChanged, Intent.ActionTimeChanged, Intent.ActionMyPackageReplaced])]
public sealed class NoticeReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        if (MobileApp.Host is not { } host) return;
        var pending = GoAsync();
        var zoneChanged = intent?.Action == Intent.ActionTimezoneChanged;
        Task.Run(async () =>
        {
            // A notice that cannot be worked out now is tried again at the next wake-up; it must not stop the app.
            try { await NoticeRunner.RunAsync(host, zoneChanged); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or TravelRepo.Core.DomainException or System.Text.Json.JsonException) { }
            finally { pending?.Finish(); }
        });
    }
}
