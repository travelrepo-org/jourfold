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
    /// <summary>The code name of this feature release, for example <c>Curious Caribou</c>.</summary>
    public static string CodeName => CodeNames.For(Version);
    public static string Platform => RuntimeInformation.OSDescription.Trim() + " (" + RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant() + "), .NET " + Environment.Version;
}

/// <summary>
/// Release code names: an alliterative adjective and animal, derived from the major and minor version, so patch
/// releases and pre-releases share the name of their feature release. The words are in CodeNames.json, and
/// <c>eng/version.py --codename</c> computes the same name for release titles.
/// </summary>
public static class CodeNames
{
    private sealed record Words(string[] Adjectives, string[] Animals);
    private static readonly Lazy<SortedDictionary<string, Words>> Lists = new(() =>
    {
        using var stream = typeof(CodeNames).Assembly.GetManifestResourceStream("Jourfold.CodeNames")!;
        var words = JsonSerializer.Deserialize<Dictionary<string, Words>>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        return new(words, StringComparer.Ordinal);
    });

    public static string For(string version)
    {
        var parts = version.Split('+')[0].Split('-')[0].Split('.');
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes("jourfold:" + string.Join('.', parts.Take(2))));
        uint Pick(int offset, int count) => BinaryPrimitives.ReadUInt32BigEndian(hash.AsSpan(offset, 4)) % (uint)count;
        var letter = Lists.Value.Values.ElementAt((int)Pick(0, Lists.Value.Count));
        return letter.Adjectives[Pick(4, letter.Adjectives.Length)] + " " + letter.Animals[Pick(8, letter.Animals.Length)];
    }
}

/// <summary>Everything the About window shows.</summary>
public sealed record AboutInfo(string Version, string CodeName, string? Commit, Uri? Source, string TravelRepoVersion, string FormatVersion, Uri? TravelRepoSource, string Platform, IReadOnlyList<KnownPlugin> Plugins);

public partial class MainViewModel
{
    [RelayCommand] private Task About() => Interaction.AboutAsync(this);

    public AboutInfo AboutInfo() => new(AppVersion.Version, AppVersion.CodeName, AppVersion.Commit, AppVersion.RepositoryUrl, TravelRepoInfo.Version, TravelRepoInfo.FormatVersion, TravelRepoInfo.RepositoryUrl, AppVersion.Platform, PluginCatalog.Known(Store));

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
