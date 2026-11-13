using Jourfold.Application;

namespace Jourfold.Mobile;

/// <summary>
/// New versions of Jourfold: the APK and IPA from GitHub look once a day when the app starts and tell about a newer
/// release above the trips; store builds never look. See <see cref="UpdateCheck"/>.
/// </summary>
public sealed partial class MobileSession
{
    private UpdateCheck? updates;

    /// <summary>The latest release; tests replace it with one that never goes online.</summary>
    public Func<CancellationToken, Task<AppRelease?>>? ReleaseSource { get; set; }

    /// <summary>The installed version, such as a preview's; the assembly's where the platform does not say.</summary>
    public string AppVersion => Host.AppVersion ?? (typeof(MobileSession).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
        .OfType<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion ?? "").Split('+')[0];

    public UpdateCheck Updates => updates ??= new UpdateCheck(Settings, AppVersion, Host.Distribution ?? AppDistribution.Name);
    public AppRelease? UpdateOffer => Updates.Offer;
    /// <summary>Set right after the user stopped looking from the notice, which then says where to turn it back on.</summary>
    public bool UpdatesJustStopped { get; private set; }

    /// <summary>Looks for a new version when due, at start. Errors, such as being offline, pass silently.</summary>
    public async Task CheckForUpdatesAsync(CancellationToken ct = default)
    {
        var source = ReleaseSource ?? (c => AppDistribution.Repository is { } repo ? UpdateCheck.LatestOnGitHubAsync(MapsHttp, repo, OnlineIdentity.UserAgent, c) : Task.FromResult<AppRelease?>(null));
        if (await Updates.CheckAsync(source, ct)) Changed?.Invoke();
    }

    public void SkipUpdate(AppRelease release) { Updates.Skip(release); Changed?.Invoke(); }

    public void SetUpdateChecks(bool on, bool fromNotice = false)
    {
        Updates.Set(on); UpdatesJustStopped = !on && fromNotice; Changed?.Invoke();
        if (on) _ = CheckForUpdatesAsync();
    }

    public void DismissUpdatesStopped() { UpdatesJustStopped = false; Changed?.Invoke(); }
}
