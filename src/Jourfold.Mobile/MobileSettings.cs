using System.Text.Json;

namespace Jourfold.Mobile;

/// <summary>
/// This phone's settings, in a small file in the app's private folder. Never part of a trip; secrets such as the
/// GitHub token are kept by the platform's secure storage, not here.
/// </summary>
public sealed class MobileSettings : Jourfold.Application.IValueStore
{
    private string path = "";
    /// <summary>The name versions made on this phone are saved under.</summary>
    public string? Name { get; set; }
    public string? Email { get; set; }
    /// <summary>The public client ID of the GitHub App used to sign in.</summary>
    public string? GitHubClientId { get; set; }
    /// <summary>The GitHub account of the last sign-in, shown while offline. The token itself is not kept here.</summary>
    public string? GitHubLogin { get; set; }
    /// <summary>GitHub's number for that account, for its private address for commits.</summary>
    public long? GitHubId { get; set; }
    /// <summary>The email that account shows publicly, if any.</summary>
    public string? GitHubEmail { get; set; }
    /// <summary>When each trip folder last synced, by folder name.</summary>
    public Dictionary<string, DateTimeOffset> LastSync { get; set; } = [];
    /// <summary>The second time zone shown next to the times of each trip, by trip folder name; chosen by the user.</summary>
    public Dictionary<string, string> SecondZones { get; set; } = [];
    /// <summary>Whether the map shows street tiles and places can be searched online. Off until the user turns it on.</summary>
    public bool OnlineMaps { get; set; }
    /// <summary>Small values kept for shared code, such as the downloaded list of map servers.</summary>
    public Dictionary<string, string> Values { get; set; } = [];

    string? Jourfold.Application.IValueStore.Get(string key) => Values.GetValueOrDefault(key);
    void Jourfold.Application.IValueStore.Set(string key, string value) { Values[key] = value; Save(); }

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
