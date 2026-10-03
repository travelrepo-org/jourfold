using System.Text.Json.Nodes;
using TravelRepo.Plugins;
namespace Jourfold.ExamplePlugin;

public sealed class WalkingNotes : ITravelPlugin
{
    public PluginDescription Describe() => new(1, "org.travelrepo.walking", [new("org.travelrepo.walking.note", "Walking note", new JsonObject { ["type"] = "object" }, [new("surface", "Surface", "text"), new("distance_m", "Distance (m)", "number")])], ["walking.duration"]);
    public Task<JsonNode?> InvokeAsync(string command, JsonNode? arguments, CancellationToken ct = default)
    {
        if (command != "walking.duration") throw new ArgumentException("Unknown command.");
        var distance = double.Parse(arguments?["distance_m"]?.ToString() ?? throw new ArgumentException("Distance required."), System.Globalization.CultureInfo.InvariantCulture);
        if (distance < 0) throw new ArgumentOutOfRangeException(nameof(arguments));
        return Task.FromResult<JsonNode?>(new JsonObject { ["duration_seconds"] = distance / 1.4 });
    }
}
