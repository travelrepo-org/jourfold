using Jourfold.Application;
namespace Jourfold.Mobile;

public sealed partial class MobileSession
{
    /// <summary>Whether Jourfold reminds the user on the trip; off until turned on.</summary>
    public bool NoticesOn => NoticeRunner.On(Settings);
    /// <summary>Why notifications cannot be shown, such as a missing permission; null when they can.</summary>
    public string? NoticesProblem { get; private set; }

    /// <summary>Turns notifications on (asking for the permission) or off, and works out what is due.</summary>
    public async Task SetNoticesAsync(bool on, CancellationToken ct = default)
    {
        if (Host.Notices is not { } notices) return;
        NoticesProblem = null;
        if (on && !await notices.AllowAsync(ask: true, ct)) { NoticesProblem = Strings["NoticesNotAllowed"]; Changed?.Invoke(); return; }
        Settings.Values["notices"] = on ? "on" : "off"; Settings.Save();
        await NoticeRunner.RunAsync(Host, ct: ct);
        Changed?.Invoke();
    }

    /// <summary>What trying the travel day's notification came to, shown under the button; null before it was tried.</summary>
    public string? LiveExampleResult { get; private set; }

    /// <summary>How long the example stays.</summary>
    public static readonly TimeSpan LiveExampleTime = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Shows the travel day's notification with an example journey that arrives in 45 minutes, so the user sees on
    /// their own device what a travel day looks like, and whether the system makes it a Live Update. After a minute
    /// the notices are worked out again, which removes the example or puts a real journey back.
    /// </summary>
    public async Task ShowLiveExampleAsync(CancellationToken ct = default)
    {
        if (Host.Notices is not { } notices || !NoticesOn) return;
        var now = Now; var arrives = now + NodaTime.Duration.FromMinutes(45);
        var title = string.Format(System.Globalization.CultureInfo.CurrentCulture, Strings["NoticeArrives"], Strings["LiveExampleJourney"], arrives.InZone(PhoneZone).ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture));
        notices.ShowLive(new LiveNotice(title, Strings["LiveExampleText"], now - NodaTime.Duration.FromMinutes(45), arrives, arrives, Guid.Empty, false), Workspace?.Repository.Root ?? Host.DataRoot);
        // The system decides a moment after the notification is posted.
        await Task.Delay(TimeSpan.FromMilliseconds(600), ct);
        LiveExampleResult = Strings[notices.LivePromoted switch { true => "LiveExamplePromoted", false when notices.Live != LiveSupport.None => "LiveExamplePlain", _ => "LiveExampleShown" }];
        Changed?.Invoke();
        _ = Task.Delay(LiveExampleTime, CancellationToken.None).ContinueWith(async _ =>
        {
            try { await NoticeRunner.RunAsync(Host); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or TravelRepo.Core.DomainException or System.Text.Json.JsonException) { }
        }, TaskScheduler.Default);
    }

    /// <summary>
    /// Opens what a notification is about: the trip, then the plan's details, or its first ticket.
    /// </summary>
    public async Task OpenNoticeAsync(string tripFolder, Guid? item, bool ticket, CancellationToken ct = default)
    {
        if (!Directory.Exists(tripFolder)) return;
        if (Workspace is null || Path.GetFullPath(Workspace.Repository.Root) != Path.GetFullPath(tripFolder)) await OpenAsync(tripFolder, ct);
        if (item is not { } id || Trip?.Find(id) is not { } entity) return;
        ShowTab(MobileTab.Today);
        ShowDetails(id);
        if (ticket && Tickets.For(Trip!, entity).FirstOrDefault() is { } first) ShowDocument(first);
    }
}
