using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using TravelRepo.Core;
using TravelRepo.Plugins;
namespace Jourfold.Infrastructure;

/// <summary>One external process per plugin. This provides crash isolation, not a security sandbox.</summary>
public sealed class PluginProcess : IDisposable
{
    private readonly Process process;
    private readonly SemaphoreSlim gate = new(1, 1);
    private int sequence;
    private readonly JsonSerializerOptions options = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true };
    public PluginManifest Manifest { get; }
    public PluginProcess(string hostExecutable, string directory, string? dotnetExecutable = null)
    {
        Manifest = JsonSerializer.Deserialize<PluginManifest>(File.ReadAllText(Path.Combine(directory, "jourfold.plugin.json")), options)!;
        if (Manifest.MinimumApiVersion > 1) throw new DomainException("plugin.version", "This plugin needs a newer API.");
        var info = new ProcessStartInfo(hostExecutable.EndsWith(".dll", StringComparison.Ordinal) ? dotnetExecutable ?? "dotnet" : hostExecutable) { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        if (hostExecutable.EndsWith(".dll", StringComparison.Ordinal)) info.ArgumentList.Add(hostExecutable); info.ArgumentList.Add(Path.GetFullPath(directory));
        process = Process.Start(info) ?? throw new IOException("Unable to start plugin host."); process.ErrorDataReceived += (_, _) => { }; process.BeginErrorReadLine();
    }
    public async Task<JsonNode?> InvokeAsync(string method, JsonNode? arguments = null, CancellationToken ct = default)
    {
        await gate.WaitAsync(ct);
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct); deadline.CancelAfter(TimeSpan.FromSeconds(30)); var id = ++sequence;
            await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new RpcRequest("2.0", id, method, arguments), options).AsMemory(), deadline.Token); await process.StandardInput.FlushAsync(deadline.Token);
            var line = await process.StandardOutput.ReadLineAsync(deadline.Token) ?? throw new DomainException("plugin.exited", "The plugin stopped unexpectedly."); var response = JsonNode.Parse(line)!;
            if (response["id"]?.GetValue<int>() != id || response["error"] is not null) throw new DomainException("plugin.response", "The plugin request failed."); return response["result"]?.DeepClone();
        }
        finally { gate.Release(); }
    }
    public async Task<PluginDescription> DescribeAsync(CancellationToken ct = default) => (await InvokeAsync("describe", ct: ct))!.Deserialize<PluginDescription>(options)!;
    public void Dispose() { try { if (!process.HasExited) process.Kill(true); } finally { process.Dispose(); gate.Dispose(); } }
}
