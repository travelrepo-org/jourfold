using System.Text.Json;
using TravelRepo.Plugins;
namespace Jourfold.Infrastructure;

/// <summary>A plugin package the user has loaded on this computer.</summary>
public sealed record KnownPlugin(string Directory, PluginManifest Manifest)
{
    /// <summary>The full license text shipped with the package, if it has one.</summary>
    public string? LicenseFile => Manifest.ResolveLicenseFile(Directory);
}

/// <summary>
/// Remembers the plugin folders the user has loaded so they can be used again and listed in the About window.
/// Only the folder paths are stored; manifests are read from disk each time, so removed packages disappear.
/// </summary>
public static class PluginCatalog
{
    private const string Key = "plugins.known";

    public static PluginManifest ReadManifest(string directory) => PluginManifest.Parse(File.ReadAllText(Path.Combine(directory, PluginManifest.FileName)));

    public static IReadOnlyList<KnownPlugin> Known(LocalStore store)
    {
        var plugins = new List<KnownPlugin>();
        foreach (var directory in Folders(store))
        {
            try { plugins.Add(new(directory, ReadManifest(directory))); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException) { }
        }
        return plugins;
    }

    public static void Remember(LocalStore store, string directory)
    {
        var full = Path.GetFullPath(directory);
        store.Set(Key, JsonSerializer.Serialize(Folders(store).Where(f => f != full).Prepend(full).ToArray()));
    }

    public static void Forget(LocalStore store, string directory) => store.Set(Key, JsonSerializer.Serialize(Folders(store).Where(f => f != Path.GetFullPath(directory)).ToArray()));

    private static string[] Folders(LocalStore store)
    {
        try { return JsonSerializer.Deserialize<string[]>(store.Get(Key) ?? "[]") ?? []; }
        catch (JsonException) { return []; }
    }
}
