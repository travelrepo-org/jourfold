using System.Text.Json;

namespace Jourfold.Mobile;

/// <summary>
/// This phone's settings, in a small file in the app's private folder. Never part of a trip; secrets such as the
/// GitHub token are kept by the platform's secure storage, not here.
/// </summary>
public sealed class MobileSettings
{
    private string path = "";
    /// <summary>The name versions made on this phone are saved under.</summary>
    public string? Name { get; set; }
    public string? Email { get; set; }
    /// <summary>The public client ID of the GitHub App used to sign in.</summary>
    public string? GitHubClientId { get; set; }
    /// <summary>When each trip folder last synced, by folder name.</summary>
    public Dictionary<string, DateTimeOffset> LastSync { get; set; } = [];

    public static MobileSettings Load(string path)
    {
        MobileSettings settings;
        try { settings = File.Exists(path) ? JsonSerializer.Deserialize<MobileSettings>(File.ReadAllText(path)) ?? new() : new(); }
        catch (JsonException) { settings = new(); }
        settings.path = path; return settings;
    }

    public void Save()
    {
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, path, overwrite: true);
    }
}
