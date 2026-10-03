using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Jourfold.Infrastructure;

/// <summary>
/// OpenStreetMap raster tiles with a local disk cache. Only used after the user enables online maps.
/// Follows the OSM tile usage policy: identifying User-Agent, at most two parallel requests, long caching.
/// The viewed map area is the only information sent; trip data never leaves the device.
/// </summary>
public sealed class MapTiles(string stateRoot, HttpClient http)
{
    public const string Attribution = "© OpenStreetMap contributors";
    public const string AttributionUrl = "https://www.openstreetmap.org/copyright";
    private static readonly SemaphoreSlim Connections = new(2, 2);
    private static readonly TimeSpan Fresh = TimeSpan.FromDays(14);
    private readonly string cache = Path.Combine(stateRoot, "cache", "tiles");

    public static void Identify(HttpClient client)
    {
        if (client.DefaultRequestHeaders.UserAgent.Count > 0) return;
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Jourfold", typeof(MapTiles).Assembly.GetName().Version?.ToString(3) ?? "0"));
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("(desktop trip planner)"));
    }

    public async Task<byte[]?> GetAsync(int zoom, int x, int y, CancellationToken ct = default)
    {
        var max = 1 << zoom; if (zoom < 0 || zoom > 19 || y < 0 || y >= max) return null;
        x = ((x % max) + max) % max;
        var file = Path.Combine(cache, zoom.ToString(CultureInfo.InvariantCulture), x.ToString(CultureInfo.InvariantCulture), y.ToString(CultureInfo.InvariantCulture) + ".png");
        if (File.Exists(file) && DateTime.UtcNow - File.GetLastWriteTimeUtc(file) < Fresh) return await File.ReadAllBytesAsync(file, ct);
        await Connections.WaitAsync(ct);
        try
        {
            Identify(http);
            using var response = await http.GetAsync($"https://tile.openstreetmap.org/{zoom}/{x}/{y}.png", ct);
            if (!response.IsSuccessStatusCode) return File.Exists(file) ? await File.ReadAllBytesAsync(file, ct) : null;
            var bytes = await response.Content.ReadAsByteArrayAsync(ct);
            Directory.CreateDirectory(Path.GetDirectoryName(file)!); await File.WriteAllBytesAsync(file, bytes, ct);
            return bytes;
        }
        catch (HttpRequestException) { return File.Exists(file) ? await File.ReadAllBytesAsync(file, ct) : null; }
        finally { Connections.Release(); }
    }

    public static void ClearCache(string stateRoot)
    {
        var path = Path.Combine(stateRoot, "cache", "tiles");
        if (Directory.Exists(path)) Directory.Delete(path, true);
    }
}

public sealed record PlaceResult(string Name, string Address, double Latitude, double Longitude, string? OsmId);

/// <summary>
/// Nominatim place search, opt-in like map tiles. Requests are made only when the user submits a search,
/// limited to one per second as the Nominatim usage policy requires.
/// </summary>
public sealed class PlaceSearch(HttpClient http)
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static DateTime last = DateTime.MinValue;

    public async Task<IReadOnlyList<PlaceResult>> SearchAsync(string query, string language, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query)) return [];
        await Gate.WaitAsync(ct);
        try
        {
            var wait = TimeSpan.FromSeconds(1) - (DateTime.UtcNow - last); if (wait > TimeSpan.Zero) await Task.Delay(wait, ct);
            MapTiles.Identify(http);
            var url = "https://nominatim.openstreetmap.org/search?format=jsonv2&limit=6&addressdetails=0&q=" + Uri.EscapeDataString(query.Trim()) + "&accept-language=" + Uri.EscapeDataString(language);
            using var response = await http.GetAsync(url, ct); last = DateTime.UtcNow;
            response.EnsureSuccessStatusCode();
            using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            var results = new List<PlaceResult>();
            foreach (var item in document.RootElement.EnumerateArray())
            {
                var display = item.GetProperty("display_name").GetString() ?? "";
                var name = item.TryGetProperty("name", out var n) && n.GetString() is { Length: > 0 } named ? named : display.Split(',')[0].Trim();
                if (!double.TryParse(item.GetProperty("lat").GetString(), CultureInfo.InvariantCulture, out var lat) || !double.TryParse(item.GetProperty("lon").GetString(), CultureInfo.InvariantCulture, out var lon)) continue;
                var osm = item.TryGetProperty("osm_type", out var type) && item.TryGetProperty("osm_id", out var id) ? type.GetString() + "/" + id.GetRawText() : null;
                results.Add(new(name, display, Math.Round(lat, 6), Math.Round(lon, 6), osm));
            }
            return results;
        }
        finally { Gate.Release(); }
    }
}
