using YamlDotNet.Serialization;

namespace Jourfold.Desktop;

/// <summary>One note of a release in every language it was written in. English is always there.</summary>
public sealed record ReleaseEntry(string? Icon, IReadOnlyDictionary<string, string> Texts)
{
    /// <summary>The text in <paramref name="language"/>, or English for releases written before that language existed.</summary>
    public string Text(string language) => Texts.GetValueOrDefault(language) ?? Texts["en"];
}

public sealed record ReleaseNote(string Version, string? Date, IReadOnlyList<ReleaseEntry> Highlights, IReadOnlyList<ReleaseEntry> Changes, IReadOnlyList<ReleaseEntry> Fixes)
{
    public string CodeName => CodeNames.For(Version);
}

/// <summary>
/// The release notes bundled with the app, from <c>docs/releases/*.yaml</c> (the same files that make CHANGELOG.md
/// and the GitHub release text), and when to show "What's new": once after an update, never after a fresh install.
/// </summary>
public static class ReleaseNotes
{
    private static readonly Lazy<IReadOnlyList<ReleaseNote>> Bundled = new(Load);

    /// <summary>All releases, newest first.</summary>
    public static IReadOnlyList<ReleaseNote> All => Bundled.Value;

    private static IReadOnlyList<ReleaseNote> Load()
    {
        var assembly = typeof(ReleaseNotes).Assembly; var yaml = new DeserializerBuilder().Build(); var result = new List<ReleaseNote>();
        foreach (var name in assembly.GetManifestResourceNames().Where(n => n.StartsWith("Jourfold.Releases.", StringComparison.Ordinal)))
        {
            using var stream = assembly.GetManifestResourceStream(name)!; using var reader = new StreamReader(stream);
            if (yaml.Deserialize<Dictionary<string, object?>>(reader) is not { } data || data.GetValueOrDefault("version")?.ToString() is not { } version) continue;
            IReadOnlyList<ReleaseEntry> Entries(string key) => (data.GetValueOrDefault(key) as List<object?> ?? []).OfType<Dictionary<object, object?>>()
                .Select(e => new ReleaseEntry(e.GetValueOrDefault("icon")?.ToString(), e.Where(p => p.Key.ToString() != "icon" && p.Value is not null).ToDictionary(p => p.Key.ToString()!, p => string.Join(' ', p.Value!.ToString()!.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)))))
                .Where(e => e.Texts.ContainsKey("en")).ToArray();
            result.Add(new(version, data.GetValueOrDefault("date")?.ToString(), Entries("highlights"), Entries("changes"), Entries("fixes")));
        }
        return result.OrderByDescending(r => Key(r.Version)).ToArray();
    }

    /// <summary>Orders versions like 0.10.0 after 0.9.1; a pre-release comes before its final release.</summary>
    public static (int, int, int, int) Key(string version)
    {
        var core = version.Split('+')[0]; var parts = core.Split('-')[0].Split('.').Select(p => int.TryParse(p, out var n) ? n : 0).Concat([0, 0, 0]).ToArray();
        return (parts[0], parts[1], parts[2], core.Contains('-') ? 0 : 1);
    }

    /// <summary>
    /// The releases to present after an update from <paramref name="seen"/> to <paramref name="current"/>. A first start
    /// (nothing seen and nothing used before) shows nothing; a first start of a version that records this, on a
    /// computer where Jourfold was used before, shows the current release.
    /// </summary>
    public static IReadOnlyList<ReleaseNote> Since(string? seen, string current, bool usedBefore)
    {
        if (seen == current || seen is null && !usedBefore) return [];
        (int, int, int, int)? from = seen is null ? null : Key(seen); var to = Key(current);
        return All.Where(r => Key(r.Version).CompareTo(to) <= 0 && (from is null ? r.Version == current : Key(r.Version).CompareTo(from.Value) > 0) && r.Highlights.Count + r.Changes.Count > 0).ToArray();
    }
}
