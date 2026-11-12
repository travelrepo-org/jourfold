using System.Buffers.Binary;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CommunityToolkit.Mvvm.Input;
using Jourfold.Infrastructure;
using TravelRepo.Core;

namespace Jourfold.Desktop;

/// <summary>The running Jourfold build. The version comes from Directory.Build.props.</summary>
public static class AppVersion
{
    private static readonly Assembly Assembly = typeof(AppVersion).Assembly;
    /// <summary>For example <c>0.2.0+3f1c2ab…</c> when built from a Git checkout.</summary>
    public static string Informational => Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
    public static string Version => Informational.Split('+')[0];
    /// <summary>The first seven characters of the source commit, if the build recorded one.</summary>
    public static string? Commit => Informational.Split('+') is [_, var commit, ..] && commit.Length >= 7 ? commit[..7] : null;
    public static Uri? RepositoryUrl => TravelRepoInfo.Metadata(Assembly, "RepositoryUrl");
    /// <summary>The product website, from <c>WebsiteUrl</c> in Directory.Build.props.</summary>
    public static Uri? WebsiteUrl => TravelRepoInfo.Metadata(Assembly, "WebsiteUrl");
    /// <summary>The code name of this feature release, for example <c>Curious Caribou</c>.</summary>
    public static string CodeName => CodeNames.For(Version);
    public static string Platform => RuntimeInformation.OSDescription.Trim() + " (" + RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant() + "), .NET " + Environment.Version;
}

/// <summary>
/// Release code names: an alliterative adjective and animal. The animal and the letter come from the major and minor
/// version, so a feature release and its patch releases share them. A feature release (x.y.0) and pre-releases take
/// one of the letter's first adjectives; each patch release after 0.11.1 takes another of the letter's adjectives,
/// in an order of its own per feature release, so 0.12.0, 0.12.1 and 0.12.2 have three different ones. Releases up
/// to 0.11.1 keep the names they were published with. The words are in CodeNames.json, and
/// <c>eng/version.py --codename</c> computes the same name for release titles.
/// </summary>
public static class CodeNames
{
    private sealed record Words(string[] Adjectives, string[] Animals, string[]? PatchAdjectives);
    private static readonly Lazy<SortedDictionary<string, Words>> Lists = new(() =>
    {
        using var stream = typeof(CodeNames).Assembly.GetManifestResourceStream("Jourfold.CodeNames")!;
        var words = JsonSerializer.Deserialize<Dictionary<string, Words>>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        return new(words, StringComparer.Ordinal);
    });

    public static string For(string version)
    {
        var parts = version.Split('+')[0].Split('-')[0].Split('.');
        var feature = string.Join('.', parts.Take(2));
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes("jourfold:" + feature));
        uint Pick(int offset, int count) => BinaryPrimitives.ReadUInt32BigEndian(hash.AsSpan(offset, 4)) % (uint)count;
        var letter = Lists.Value.Values.ElementAt((int)Pick(0, Lists.Value.Count));
        var adjective = letter.Adjectives[Pick(4, letter.Adjectives.Length)];
        var animal = letter.Animals[Pick(8, letter.Animals.Length)];
        static int Number(string[] p, int i) => p.Length > i && int.TryParse(p[i], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var n) ? n : 0;
        var (major, minor, patch) = (Number(parts, 0), Number(parts, 1), Number(parts, 2));
        if (patch > 0 && (major, minor, patch).CompareTo((0, 11, 1)) > 0)
        {
            // The letter's other adjectives in an order of their own for this feature release, one per patch.
            var others = letter.Adjectives.Concat(letter.PatchAdjectives ?? []).Where(a => a != adjective).Distinct()
                .OrderBy(a => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("jourfold:" + feature + ":" + a))), StringComparer.Ordinal).ToArray();
            adjective = others[(patch - 1) % others.Length];
        }
        return adjective + " " + animal;
    }
}

/// <summary>Everything the About window shows.</summary>
public sealed record AboutInfo(string Version, string CodeName, string? Commit, Uri? Source, string TravelRepoVersion, string FormatVersion, Uri? TravelRepoSource, string Platform, IReadOnlyList<KnownPlugin> Plugins, Uri? Website = null);

public partial class MainViewModel
{
    [RelayCommand] private Task About() => Interaction.AboutAsync(this);

    /// <summary>
    /// Shows what is new once after an update. The last version seen is remembered on this computer; a fresh install
    /// starts with the sample trip instead.
    /// </summary>
    public async Task ShowWhatsNewAsync()
    {
        var seen = Store.Get("whatsnew.seen"); var current = AppVersion.Version;
        var usedBefore = Store.Recent().Count > 0 || Store.Get("lastView") is not null;
        var releases = ReleaseNotes.Since(seen, current, usedBefore);
        Store.Set("whatsnew.seen", current);
        if (releases.Count > 0) await Interaction.ReleaseNotesAsync(releases, highlightsOnly: true);
    }

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private Task VersionHistory() => Interaction.ReleaseNotesAsync(ReleaseNotes.All, highlightsOnly: false);

    public AboutInfo AboutInfo() => new(AppVersion.Version, AppVersion.CodeName, AppVersion.Commit, AppVersion.RepositoryUrl, TravelRepoInfo.Version, TravelRepoInfo.FormatVersion, TravelRepoInfo.RepositoryUrl, AppVersion.Platform, PluginCatalog.Known(Store), AppVersion.WebsiteUrl);

    /// <summary>License and notice texts compiled into the application: <c>Jourfold.LICENSE</c>, <c>TravelRepo.LICENSE</c> and <c>Jourfold.NOTICES</c>.</summary>
    public static string BundledText(string name)
    {
        using var stream = typeof(MainViewModel).Assembly.GetManifestResourceStream(name) ?? throw new ArgumentException("Unknown bundled text " + name, nameof(name));
        using var reader = new StreamReader(stream); return reader.ReadToEnd();
    }

    /// <summary>A plain-text summary for bug reports, copied from the About window.</summary>
    public string AboutText()
    {
        var info = AboutInfo();
        var lines = new List<string> { "Jourfold " + info.Version + " " + string.Format(Strings["QuotedName"], info.CodeName) + (info.Commit is null ? "" : " (" + info.Commit + ")"), "TravelRepo " + info.TravelRepoVersion + ", " + string.Format(Strings["TripFormatVersion"], info.FormatVersion), info.Platform };
        lines.AddRange(info.Plugins.Select(p => Strings["Plugin"] + ": " + p.Manifest.DisplayName + " " + p.Manifest.Version + " (" + p.Manifest.Id + ")"));
        return string.Join('\n', lines);
    }
}
