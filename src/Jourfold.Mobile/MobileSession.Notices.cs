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
