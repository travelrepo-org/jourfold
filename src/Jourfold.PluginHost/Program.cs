using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using System.Text.Json.Nodes;
using TravelRepo.Plugins;

if (args.Length != 1) { Console.Error.WriteLine("Usage: Jourfold.PluginHost <plugin-directory>"); return 2; }
var root = Path.GetFullPath(args[0]);
var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true };
var manifest = JsonSerializer.Deserialize<PluginManifest>(await File.ReadAllTextAsync(Path.Combine(root, "jourfold.plugin.json")), options)!;
if (manifest.MinimumApiVersion > 1) { Console.Error.WriteLine("Unsupported plugin API version."); return 2; }
var entry = Path.GetFullPath(Path.Combine(root, manifest.EntryAssembly));
if (!entry.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal)) throw new InvalidDataException("Entry assembly must be in the plugin package.");
var context = new PluginContext(entry); var assembly = context.LoadFromAssemblyPath(entry);
var type = assembly.GetTypes().Single(t => typeof(ITravelPlugin).IsAssignableFrom(t) && !t.IsAbstract);
var plugin = (ITravelPlugin)Activator.CreateInstance(type)!;
while (await Console.In.ReadLineAsync() is { } line)
{
    int? id = null;
    try
    {
        var request = JsonSerializer.Deserialize<RpcRequest>(line, options)!; id = request.Id;
        if (request.Jsonrpc != "2.0") throw new InvalidDataException("Unsupported RPC version.");
        JsonNode? result = request.Method == "describe" ? JsonSerializer.SerializeToNode(plugin.Describe(), options) : await plugin.InvokeAsync(request.Method, request.Params);
        await Console.Out.WriteLineAsync(JsonSerializer.Serialize(new { jsonrpc = "2.0", id, result }, options));
    }
    catch (Exception) { await Console.Out.WriteLineAsync(JsonSerializer.Serialize(new { jsonrpc = "2.0", id, error = new RpcError(-32603, "Plugin request failed.") }, options)); }
}
return 0;
sealed class PluginContext(string entry) : AssemblyLoadContext(isCollectible: true)
{
    private readonly AssemblyDependencyResolver resolver = new(entry);
    protected override Assembly? Load(AssemblyName name)
    {
        if (name.Name == typeof(ITravelPlugin).Assembly.GetName().Name) return null;
        var path = resolver.ResolveAssemblyToPath(name); return path is null ? null : LoadFromAssemblyPath(path);
    }
}
