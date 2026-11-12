using System.Text.Json;
using BackgroundTasks;
using Foundation;
using Jourfold.Mobile;
using NodaTime;
using UIKit;
using UserNotifications;

namespace Jourfold.iOS;

/// <summary>
/// Jourfold's notifications on iOS. iOS does not wake an app at a set time, so the coming notices are handed to the
/// system ahead and it shows them by itself, also when Jourfold is not running. Jourfold works them out again whenever
/// it comes on screen and, when iOS allows, in the background. There is no ongoing notification for the travel day and
/// no kinds to choose from; a tap opens the plan, and "Show ticket" its ticket.
/// </summary>
public sealed class IosNotices(string dataRoot, string language, string device) : UNUserNotificationCenterDelegate, ISystemNotices
{
    /// <summary>The background refresh that works out the notices again; listed in Info.plist.</summary>
    public const string RefreshTask = "dev.jourfold.app.notices";
    private const string Prefix = "jourfold.", TicketCategory = "ticket", TicketAction = "ticket", TripKey = "trip", ItemKey = "item";
    /// <summary>iOS keeps at most 64 scheduled notifications per app; the soonest are handed over.</summary>
    private const int Most = 60;

    private string ScheduledPath => Path.Combine(dataRoot, "notices-scheduled.json");

    /// <summary>Answers taps on notifications and registers the background refresh. Called once, while the app launches.</summary>
    public void Start()
    {
        var center = UNUserNotificationCenter.Current;
        center.Delegate = this;
        var ticket = UNNotificationAction.FromIdentifier(TicketAction, new Jourfold.UI.Localization(language) { Device = device }["ShowTicket"], UNNotificationActionOptions.Foreground);
        center.SetNotificationCategories(new NSSet<UNNotificationCategory>(UNNotificationCategory.FromIdentifier(TicketCategory, [ticket], [], UNNotificationCategoryOptions.None)));
        BGTaskScheduler.Shared.Register(RefreshTask, null, Refresh);
    }

    /// <summary>Works out the notices; a notice that cannot be worked out now is tried again next time and must not stop the app.</summary>
    public static async Task RunAsync(MobileHost host, bool zoneChanged = false, CancellationToken ct = default)
    {
        try { await NoticeRunner.RunAsync(host, zoneChanged, ct); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException or TravelRepo.Core.DomainException or JsonException) { }
    }

    private static void Refresh(BGTask task)
    {
        var cancel = new CancellationTokenSource();
        task.ExpirationHandler = () => { try { cancel.Cancel(); } catch (ObjectDisposedException) { } };
        // The task is completed whatever happens: one left open gets the app ended and lowers its later background time.
        _ = Task.Run(async () =>
        {
            var done = false;
            try { if (MobileApp.Host is { } host) await RunAsync(host, ct: cancel.Token); done = !cancel.IsCancellationRequested; }
            finally { task.SetTaskCompleted(done); cancel.Dispose(); }
        });
    }

    public async Task<bool> AllowAsync(bool ask, CancellationToken ct = default)
    {
        var center = UNUserNotificationCenter.Current;
        var status = (await center.GetNotificationSettingsAsync()).AuthorizationStatus;
        if (status is UNAuthorizationStatus.Authorized or UNAuthorizationStatus.Provisional or UNAuthorizationStatus.Ephemeral) return true;
        if (!ask || status != UNAuthorizationStatus.NotDetermined) return false;
        return (await center.RequestAuthorizationAsync(UNAuthorizationOptions.Alert | UNAuthorizationOptions.Sound)).Item1;
    }

    /// <summary>Scheduled notifications come on time.</summary>
    public bool OnTime => true;
    public bool ShowsLive => false;
    public bool HasKinds => false;
    public void AskOnTime() { }

    public void OpenSettings()
    {
        var page = OperatingSystem.IsIOSVersionAtLeast(16) ? UIApplication.OpenNotificationSettingsUrl : UIApplication.OpenSettingsUrlString;
        UIApplication.SharedApplication.OpenUrl(new NSUrl(page), new UIApplicationOpenUrlOptions(), null);
    }

    public void Show(TravelNotice notice, string tripFolder)
    {
        // The system has shown it already at its time.
        if (Scheduled().TryGetValue(notice.Key, out var at) && Instant.FromUnixTimeMilliseconds(at) <= SystemClock.Instance.GetCurrentInstant()) return;
        // Shown now; it takes the place of the same notice scheduled for later, such as an arrival after a time zone change.
        UNUserNotificationCenter.Current.AddNotificationRequest(Request(notice, tripFolder, null), null);
    }

    public void Schedule(IReadOnlyList<(TravelNotice Notice, string TripFolder)> coming)
    {
        var center = UNUserNotificationCenter.Current;
        center.RemoveAllPendingNotificationRequests();
        var now = SystemClock.Instance.GetCurrentInstant();
        var scheduled = Scheduled().Where(s => Instant.FromUnixTimeMilliseconds(s.Value) > now - Duration.FromDays(4) && Instant.FromUnixTimeMilliseconds(s.Value) <= now).ToDictionary();
        foreach (var (notice, folder) in coming.Take(Most))
        {
            center.AddNotificationRequest(Request(notice, folder, notice.At - now), null);
            scheduled[notice.Key] = notice.At.ToUnixTimeMilliseconds();
        }
        File.WriteAllText(ScheduledPath, JsonSerializer.Serialize(scheduled, IosJson.Default.DictionaryStringInt64));
    }

    /// <summary>iOS has no ongoing notification; Live Activities may take its place later.</summary>
    public void ShowLive(LiveNotice? live, string? tripFolder) { }

    /// <summary>Asks iOS to run the notices again in the background from <paramref name="at"/>; iOS decides when.</summary>
    public void WakeAt(Instant? at)
    {
        BGTaskScheduler.Shared.Cancel(RefreshTask);
        if (at is not { } when) return;
        // The simulator has no background refresh and refuses the request; the notices are scheduled either way.
        BGTaskScheduler.Shared.Submit(new BGAppRefreshTaskRequest(RefreshTask) { EarliestBeginDate = NSDate.FromTimeIntervalSince1970(when.ToUnixTimeMilliseconds() / 1000.0) }, out _);
    }

    /// <summary>
    /// Not on iOS yet: Avalonia's window scene does not pass on taps on the icon's quick actions, so a ticket shortcut
    /// could only open the app.
    /// </summary>
    public void SetTicketShortcuts(IReadOnlyList<TicketShortcut> tickets) { }

    // While Jourfold is open, its notices still appear as banners.
    public override void WillPresentNotification(UNUserNotificationCenter center, UNNotification notification, Action<UNNotificationPresentationOptions> completionHandler) =>
        completionHandler(UNNotificationPresentationOptions.Banner | UNNotificationPresentationOptions.List | UNNotificationPresentationOptions.Sound);

    /// <summary>A tap opens the trip and the plan's details; "Show ticket" its ticket.</summary>
    public override void DidReceiveNotificationResponse(UNUserNotificationCenter center, UNNotificationResponse response, Action completionHandler)
    {
        var info = response.Notification.Request.Content.UserInfo;
        if (info.ObjectForKey(new NSString(TripKey))?.ToString() is { } trip && MobileApp.Host is { } host)
        {
            // The app's folder moves with updates of the app; the trip is found by its folder's name.
            var folder = Path.Combine(host.TripsRoot, trip);
            var item = Guid.TryParse(info.ObjectForKey(new NSString(ItemKey))?.ToString(), out var id) ? id : (Guid?)null;
            var ticket = response.ActionIdentifier == TicketAction;
            AppDelegate.WhenReady(session => session.OpenNoticeAsync(folder, item, ticket));
        }
        completionHandler();
    }

    private static UNNotificationRequest Request(TravelNotice notice, string tripFolder, Duration? after)
    {
        var trip = Path.GetFileName(tripFolder);
        var content = new UNMutableNotificationContent { Title = notice.Title, Body = notice.Text, Sound = UNNotificationSound.Default, ThreadIdentifier = trip };
        content.UserInfo = notice.Item is { } item
            ? NSDictionary.FromObjectsAndKeys(new NSObject[] { new NSString(trip), new NSString(item.ToString()) }, new NSObject[] { new NSString(TripKey), new NSString(ItemKey) })
            : NSDictionary.FromObjectsAndKeys(new NSObject[] { new NSString(trip) }, new NSObject[] { new NSString(TripKey) });
        if (notice.Ticket) content.CategoryIdentifier = TicketCategory;
        var trigger = after is { } wait ? UNTimeIntervalNotificationTrigger.CreateTrigger(Math.Max(1, wait.TotalSeconds), false) : null;
        return UNNotificationRequest.FromIdentifier(Prefix + notice.Key, content, trigger);
    }

    /// <summary>The notices handed to the system, with the time each shows at.</summary>
    private Dictionary<string, long> Scheduled()
    {
        try { return File.Exists(ScheduledPath) ? JsonSerializer.Deserialize(File.ReadAllText(ScheduledPath), IosJson.Default.DictionaryStringInt64) ?? [] : []; }
        catch (JsonException) { return []; }
    }
}

/// <summary>The app's own state files, read and written without reflection, which trimming would break.</summary>
[System.Text.Json.Serialization.JsonSerializable(typeof(Dictionary<string, long>))]
[System.Text.Json.Serialization.JsonSerializable(typeof(Dictionary<string, IosCalendar.Kept>))]
internal sealed partial class IosJson : System.Text.Json.Serialization.JsonSerializerContext;
