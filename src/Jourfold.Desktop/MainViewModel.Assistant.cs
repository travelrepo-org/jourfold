using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.Input;

namespace Jourfold.Desktop;

public partial class MainViewModel
{
    [RelayCommand] private Task ConnectAssistant() => Workspace is null ? Task.CompletedTask : Interaction.ConnectAssistantAsync(this);

    /// <summary>This Jourfold executable, which serves a trip to AI assistants when started with <c>--mcp</c>.</summary>
    public static string AssistantCommand => Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "Jourfold.Desktop.exe" : "Jourfold.Desktop");

    /// <summary>The MCP client configuration for the open trip, in the "mcpServers" format most assistants use.</summary>
    public string AssistantConfiguration(bool readOnly) => Workspace is null ? "" : AssistantConfiguration(AssistantCommand, Workspace.Repository.Root, Workspace.State.Trip.Manifest.Title, readOnly);

    public static string AssistantConfiguration(string command, string trip, string title, bool readOnly)
    {
        var args = new JsonArray("--mcp", trip); if (readOnly) args.Add("--read-only");
        var server = new JsonObject { ["command"] = command, ["args"] = args };
        var root = new JsonObject { ["mcpServers"] = new JsonObject { [AssistantServerName(title)] = server } };
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
    }

    /// <summary>A short ASCII server name such as "jourfold-spring-in-japan", which every MCP client accepts.</summary>
    public static string AssistantServerName(string title)
    {
        var slug = new StringBuilder();
        foreach (var c in title.Normalize(NormalizationForm.FormD).ToLowerInvariant())
            if (c is >= 'a' and <= 'z' or >= '0' and <= '9') slug.Append(c);
            else if (char.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark && slug.Length > 0 && slug[^1] != '-') slug.Append('-');
        var name = slug.ToString().Trim('-');
        return "jourfold-" + (name.Length == 0 ? "trip" : name.Length > 40 ? name[..40].TrimEnd('-') : name);
    }

    /// <summary>Saves the agent guide (SKILL.md) for assistants that edit the trip files directly.</summary>
    public async Task SaveAgentGuideAsync()
    {
        var path = await Interaction.FileAsync(true, ".md", "SKILL.md"); if (path is null) return;
        await File.WriteAllTextAsync(path, TravelRepo.Mcp.TripServer.Guide); Interaction.Toast(Strings["AgentGuideSaved"]);
    }
}
