using Jourfold.Application;

namespace Jourfold.Desktop;

/// <summary>
/// New versions of Jourfold: GitHub builds look once a day when the app starts and tell about a newer release above
/// the trips. See <see cref="UpdateCheck"/>.
/// </summary>
public partial class MainViewModel
{
    private UpdateCheck? updates;

    /// <summary>Where this build comes from; tests and screenshots set <c>github</c> to see the notice.</summary>
    public string? Distribution { get; set { field = value; updates = null; } } = AppDistribution.Name;
    /// <summary>The latest release; tests and screenshots replace it with one that never goes online.</summary>
    public Func<CancellationToken, Task<AppRelease?>>? ReleaseSource { get; set; }

    public UpdateCheck Updates => updates ??= new UpdateCheck(Store, AppVersion.Informational, Distribution);
    public AppRelease? UpdateOffer => Updates.Offer;

    /// <summary>Looks for a new version when due, at start. Errors, such as being offline, pass silently.</summary>
    public async Task CheckForUpdatesAsync()
    {
        var source = ReleaseSource ?? (ct => AppDistribution.Repository is { } repo ? UpdateCheck.LatestOnGitHubAsync(weatherHttp, repo, OnlineIdentity.UserAgent, ct) : Task.FromResult<AppRelease?>(null));
        if (await Updates.CheckAsync(source)) ContentChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SkipUpdate(AppRelease release) { Updates.Skip(release); ContentChanged?.Invoke(this, EventArgs.Empty); }

    public void SetUpdateChecks(bool on)
    {
        Updates.Set(on); ContentChanged?.Invoke(this, EventArgs.Empty);
        if (on) _ = CheckForUpdatesAsync();
    }
}
