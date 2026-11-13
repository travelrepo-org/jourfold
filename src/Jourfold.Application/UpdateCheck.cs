using System.Globalization;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;

namespace Jourfold.Application;

/// <summary>A published Jourfold release: its version, its title such as <c>Jourfold 0.13.0 “Gallant Gnu”</c>, and its page.</summary>
public sealed record AppRelease(string Version, string Title, Uri Page);

/// <summary>
/// Where this build of Jourfold comes from, set when it is built with <c>-p:JourfoldDistribution=…</c> (or the
/// environment variable of that name): <c>github</c> for the packages, APK and IPA attached to GitHub releases, and a
/// name such as <c>fdroid</c>, <c>play</c> or <c>appstore</c> for builds that a store updates. Builds without it, such as
/// one built from source, say nothing.
/// </summary>
public static class AppDistribution
{
    public const string GitHub = "github";
    public static string? Name { get; } = typeof(AppDistribution).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(a => a.Key == "Distribution")?.Value is { Length: > 0 } name ? name : null;
    /// <summary>Whether this build looks for new versions itself. Only GitHub builds do; stores bring their own updates.</summary>
    public static bool ChecksForUpdates(string? distribution) => distribution == GitHub;
    /// <summary>The public repository the build comes from, whose releases are new versions.</summary>
    public static string? Repository { get; } = typeof(AppDistribution).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(a => a.Key == "RepositoryUrl")?.Value;
}

/// <summary>
/// Tells about a newer Jourfold on GitHub. On by default in GitHub builds and off in one tap, from Settings or from the
/// notice itself; store builds never look. At most once a day it asks GitHub for the latest full release, sending
/// nothing but the app's name and version; pre-releases are not offered. Offline or on any error nothing happens and
/// the next start asks again. Nothing is downloaded or installed: the notice links to the release page.
/// </summary>
public sealed class UpdateCheck
{
    public const string OnKey = "updates.check";
    public const string CheckedKey = "updates.checked";
    public const string LatestKey = "updates.latest";
    public const string TitleKey = "updates.title";
    public const string PageKey = "updates.page";
    public const string SkipKey = "updates.skip";
    private static readonly TimeSpan Interval = TimeSpan.FromDays(1);
    private readonly IValueStore store;
    private readonly Func<DateTimeOffset> now;
    private bool checking;

    /// <param name="current">The running version, such as <c>0.12.0</c> or <c>0.12.0-preview.3</c>.</param>
    /// <param name="distribution">Where this build comes from; see <see cref="AppDistribution"/>.</param>
    public UpdateCheck(IValueStore store, string current, string? distribution, Func<DateTimeOffset>? now = null)
    {
        this.store = store; Current = current.Split('+')[0]; Available = AppDistribution.ChecksForUpdates(distribution); this.now = now ?? (() => DateTimeOffset.UtcNow);
    }

    public string Current { get; }
    /// <summary>Whether this build can look for new versions at all; false for store builds, which then show no setting either.</summary>
    public bool Available { get; }
    /// <summary>Whether it looks: on unless turned off.</summary>
    public bool On => Available && store.Get(OnKey) != "off";

    public void Set(bool on) => store.Set(OnKey, on ? "on" : "off");

    /// <summary>The newer release to tell about, or null: none known, not newer, skipped, or looking is off.</summary>
    public AppRelease? Offer
    {
        get
        {
            if (!On || store.Get(LatestKey) is not { Length: > 0 } latest || Compare(latest, Current) <= 0 || store.Get(SkipKey) == latest) return null;
            if (!Uri.TryCreate(store.Get(PageKey), UriKind.Absolute, out var page) || page.Scheme != Uri.UriSchemeHttps) return null;
            return new AppRelease(latest, store.Get(TitleKey) is { Length: > 0 } title ? title : "Jourfold " + latest, page);
        }
    }

    /// <summary>Stops telling about this release; a later one is told about again.</summary>
    public void Skip(AppRelease release) => store.Set(SkipKey, release.Version);

    /// <summary>Whether a day has passed since the last answer from GitHub.</summary>
    public bool Due => On && !(DateTimeOffset.TryParse(store.Get(CheckedKey), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var last) && now() - last < Interval && last <= now());

    /// <summary>Asks for the latest release when due. True when it found one to tell about.</summary>
    /// <param name="source">Where the latest release comes from; GitHub in the app, a stand-in in tests.</param>
    public async Task<bool> CheckAsync(Func<CancellationToken, Task<AppRelease?>> source, CancellationToken ct = default)
    {
        if (!Due || checking) return false;
        checking = true;
        try
        {
            var latest = await source(ct);
            store.Set(CheckedKey, now().ToString("O", CultureInfo.InvariantCulture));
            if (latest is null) return false;
            store.Set(LatestKey, latest.Version); store.Set(TitleKey, latest.Title); store.Set(PageKey, latest.Page.ToString());
            return Offer is not null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or JsonException or FormatException or InvalidOperationException) { return false; }
        finally { checking = false; }
    }

    /// <summary>
    /// The latest full release of the repository on GitHub, without signing in. Drafts and pre-releases are not
    /// "latest" on GitHub, so they never show up here. Null when the repository has no release or is not on GitHub.
    /// </summary>
    public static async Task<AppRelease?> LatestOnGitHubAsync(HttpClient http, string repository, string userAgent, CancellationToken ct = default)
    {
        if (!Uri.TryCreate(repository, UriKind.Absolute, out var repo) || repo.Host != "github.com" || repo.AbsolutePath.Trim('/').Split('/') is not [var owner, var name]) return null;
        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(name)}/releases/latest");
        request.Headers.UserAgent.ParseAdd(userAgent);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(15));
        using var response = await http.SendAsync(request, timeout.Token);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(timeout.Token), cancellationToken: timeout.Token);
        return Parse(json.RootElement);
    }

    /// <summary>Reads a release from GitHub's answer; null for a draft, a pre-release or a tag that is no version.</summary>
    public static AppRelease? Parse(JsonElement release)
    {
        bool Flag(string key) => release.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.True;
        string? Text(string key) => release.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        if (Flag("draft") || Flag("prerelease")) return null;
        var version = Text("tag_name")?.TrimStart('v', 'V');
        if (version is null || Parts(version) is null || version.Contains('-')) return null;
        if (!Uri.TryCreate(Text("html_url"), UriKind.Absolute, out var page) || page.Scheme != Uri.UriSchemeHttps) return null;
        return new AppRelease(version, Text("name") is { Length: > 0 } title ? title.Trim() : "Jourfold " + version, page);
    }

    /// <summary>
    /// Compares two versions the way Semantic Versioning orders them: numbers first, and a pre-release such as
    /// <c>0.13.0-preview.2</c> before its release. Build metadata after <c>+</c> does not count; something that is no
    /// version counts as lowest.
    /// </summary>
    public static int Compare(string a, string b)
    {
        var (x, y) = (Parts(a), Parts(b));
        if (x is null || y is null) return (x is null ? 0 : 1) - (y is null ? 0 : 1);
        for (var i = 0; i < 3; i++) if (x.Value.Numbers[i] != y.Value.Numbers[i]) return x.Value.Numbers[i].CompareTo(y.Value.Numbers[i]);
        var (p, q) = (x.Value.Pre, y.Value.Pre);
        // A release comes after its pre-releases.
        if (p.Length == 0 || q.Length == 0) return p.Length == q.Length ? 0 : p.Length == 0 ? 1 : -1;
        for (var i = 0; i < Math.Min(p.Length, q.Length); i++)
        {
            var (m, n) = (long.TryParse(p[i], NumberStyles.None, CultureInfo.InvariantCulture, out var pm), long.TryParse(q[i], NumberStyles.None, CultureInfo.InvariantCulture, out var qn));
            var c = m && n ? pm.CompareTo(qn) : m ? -1 : n ? 1 : string.CompareOrdinal(p[i], q[i]);
            if (c != 0) return Math.Sign(c);
        }
        return p.Length.CompareTo(q.Length);
    }

    private static (long[] Numbers, string[] Pre)? Parts(string version)
    {
        var core = version.Trim().TrimStart('v', 'V').Split('+')[0];
        var dash = core.IndexOf('-');
        var pre = dash < 0 ? [] : core[(dash + 1)..].Split('.');
        var numbers = (dash < 0 ? core : core[..dash]).Split('.');
        if (numbers.Length != 3 || pre.Any(p => p.Length == 0)) return null;
        var parsed = new long[3];
        for (var i = 0; i < 3; i++) if (!long.TryParse(numbers[i], NumberStyles.None, CultureInfo.InvariantCulture, out parsed[i])) return null;
        return (parsed, pre);
    }
}
