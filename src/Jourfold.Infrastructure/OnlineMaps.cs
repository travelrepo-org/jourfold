using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Jourfold.Infrastructure;

/// <summary>
/// The servers behind online maps: a raster tile server and a Nominatim-compatible address search. Online maps are
/// off until the person enables them.
/// </summary>
/// <param name="TilesUrl">Tile URL template with <c>{z}</c>, <c>{x}</c> and <c>{y}</c>.</param>
/// <param name="Attribution">Credit shown on the map, required by the data licence.</param>
/// <param name="AttributionUrl">Where the credit links to.</param>
/// <param name="MaxZoom">Highest zoom level the tile server offers.</param>
/// <param name="SearchUrl">Nominatim <c>/search</c> endpoint.</param>
public sealed record MapProvider(string TilesUrl, string Attribution, string AttributionUrl, int MaxZoom, string SearchUrl)
{
    public static readonly MapProvider OpenStreetMap = new("https://tile.openstreetmap.org/{z}/{x}/{y}.png", "© OpenStreetMap contributors", "https://www.openstreetmap.org/copyright", 19, "https://nominatim.openstreetmap.org/search");

    public bool IsValid => Https(TilesUrl.Replace("{z}", "0").Replace("{x}", "0").Replace("{y}", "0")) && TilesUrl.Contains("{z}") && TilesUrl.Contains("{x}") && TilesUrl.Contains("{y}")
        && Https(SearchUrl) && Https(AttributionUrl) && !string.IsNullOrWhiteSpace(Attribution) && MaxZoom is >= 1 and <= 22;

    private static bool Https(string url) => Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps;
}

/// <summary>
/// Chooses the map servers. People can enter their own servers in Settings. Otherwise Jourfold uses the provider list
/// published in its repository (<c>config/map-providers.json</c>), fetched at most once a day while online maps are on, so
/// the servers can be switched without a software update, as the Nominatim usage policy requires. Without a list, the
/// OpenStreetMap servers are used.
/// </summary>
public static class MapProviders
{
    public const string TilesKey = "map.tiles.url", AttributionKey = "map.attribution", SearchKey = "map.search.url";
    private const string ListKey = "map.providers", CheckedKey = "map.providers.checked";
    private static readonly TimeSpan CheckInterval = TimeSpan.FromDays(1);

    /// <summary>The provider list in the repository that published this build, or <c>null</c> when the build names none.</summary>
    public static Uri? ListUrl =>
        Uri.TryCreate(typeof(MapProviders).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(a => a.Key == "RepositoryUrl")?.Value, UriKind.Absolute, out var repository)
        && repository.Host == "github.com" && repository.AbsolutePath.Trim('/').Split('/') is [var owner, var name]
            ? new Uri($"https://raw.githubusercontent.com/{owner}/{name}/main/config/map-providers.json") : null;

    /// <summary>Own servers from Settings, else the last downloaded list, else OpenStreetMap.</summary>
    public static MapProvider Current(LocalStore store)
    {
        var listed = Parse(store.Get(ListKey)) ?? MapProvider.OpenStreetMap;
        var own = listed with
        {
            TilesUrl = Value(store, TilesKey) ?? listed.TilesUrl,
            Attribution = Value(store, AttributionKey) ?? (Value(store, TilesKey) is null ? listed.Attribution : MapProvider.OpenStreetMap.Attribution),
            SearchUrl = Value(store, SearchKey) ?? listed.SearchUrl
        };
        return own.IsValid ? own : listed;
    }

    /// <summary>True when the person entered their own tile or search server.</summary>
    public static bool HasOwnServers(LocalStore store) => Value(store, TilesKey) is not null || Value(store, SearchKey) is not null;

    /// <summary>Downloads the provider list when the last check is older than a day. Failures keep the previous list.</summary>
    public static async Task RefreshAsync(LocalStore store, HttpClient http, CancellationToken ct = default)
    {
        if (ListUrl is not { } url) return;
        if (DateTimeOffset.TryParse(store.Get(CheckedKey), CultureInfo.InvariantCulture, DateTimeStyles.None, out var last) && DateTimeOffset.UtcNow - last < CheckInterval) return;
        store.Set(CheckedKey, DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url); OnlineIdentity.Apply(request);
            using var response = await http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode) return;
            var text = await response.Content.ReadAsStringAsync(ct);
            if (Parse(text) is not null) store.Set(ListKey, text);
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException) { }
    }

    /// <summary>Reads <c>{"tiles": {"url", "attribution", "attributionUrl", "maxZoom"}, "search": {"url"}}</c>; anything else is ignored.</summary>
    public static MapProvider? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            var root = JsonNode.Parse(json);
            var provider = new MapProvider(root?["tiles"]?["url"]?.GetValue<string>() ?? "", root?["tiles"]?["attribution"]?.GetValue<string>() ?? "", root?["tiles"]?["attributionUrl"]?.GetValue<string>() ?? "",
                root?["tiles"]?["maxZoom"]?.GetValue<int>() ?? 0, root?["search"]?["url"]?.GetValue<string>() ?? "");
            return provider.IsValid ? provider : null;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException) { return null; }
    }

    private static string? Value(LocalStore store, string key) => store.Get(key) is { Length: > 0 } value ? value.Trim() : null;
}

/// <summary>Identifies Jourfold to map servers with its version and a contact address, as their usage policies ask.</summary>
public static class OnlineIdentity
{
    public static string UserAgent { get; } = Create();
    public static void Apply(HttpRequestMessage request) => request.Headers.UserAgent.ParseAdd(UserAgent);

    private static string Create()
    {
        var assembly = typeof(OnlineIdentity).Assembly;
        var version = (assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0").Split('+')[0];
        var contact = assembly.GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(a => a.Key == "RepositoryUrl")?.Value;
        return "Jourfold/" + version + (string.IsNullOrEmpty(contact) ? " (desktop trip planner)" : " (+" + contact + ")");
    }
}

/// <summary>
/// Raster tiles with a local disk cache, following the OpenStreetMap tile usage policy: an identifying User-Agent, at most
/// two parallel requests, only tiles that are on screen, caching as long as the server's headers allow (Cache-Control
/// max-age minus Age, or seven days when it sends none), conditional requests to renew expired tiles, stale tiles only
/// within the server's stale-if-error window, and no bulk download for offline use. The cache is capped; the oldest
/// tiles are removed first. The viewed map area is the only information sent.
/// </summary>
public sealed class MapTiles
{
    public const long MaxCacheBytes = 250L * 1024 * 1024;
    private static readonly SemaphoreSlim Connections = new(2, 2);
    private static readonly TimeSpan DefaultLifetime = TimeSpan.FromDays(7);
    private readonly HttpClient http;
    private readonly string cache;
    public MapProvider Provider { get; }

    public MapTiles(string stateRoot, HttpClient http, MapProvider provider)
    {
        this.http = http; Provider = provider;
        // Each server gets its own folder, so switching servers never mixes their tiles.
        cache = Path.Combine(CacheRoot(stateRoot), Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(provider.TilesUrl)))[..12].ToLowerInvariant());
    }

    public static string CacheRoot(string stateRoot) => Path.Combine(stateRoot, "cache", "tiles");

    /// <param name="StaleIfError">How long after <paramref name="Expires"/> the server allows showing the tile when it cannot be reached.</param>
    private sealed record Meta(DateTimeOffset Expires, string? ETag, DateTimeOffset? LastModified, TimeSpan StaleIfError = default);

    /// <summary>
    /// Returns the tile, or <c>null</c> when there is none. <paramref name="stillWanted"/> is asked just before a download;
    /// when it returns false (the person has moved on), nothing is requested and <c>Skipped</c> is true.
    /// </summary>
    public async Task<(byte[]? Bytes, bool Skipped)> GetAsync(int zoom, int x, int y, Func<bool>? stillWanted = null, CancellationToken ct = default)
    {
        var max = 1 << Math.Clamp(zoom, 0, 30); if (zoom < 0 || zoom > Provider.MaxZoom || y < 0 || y >= max) return (null, false);
        x = ((x % max) + max) % max;
        var file = Path.Combine(cache, zoom.ToString(CultureInfo.InvariantCulture), x.ToString(CultureInfo.InvariantCulture), y.ToString(CultureInfo.InvariantCulture) + ".png");
        var meta = ReadMeta(file);
        if (meta is not null && File.Exists(file) && meta.Expires > DateTimeOffset.UtcNow) return (await File.ReadAllBytesAsync(file, ct), false);
        await Connections.WaitAsync(ct);
        try
        {
            if (stillWanted?.Invoke() == false) return (null, true);
            using var request = new HttpRequestMessage(HttpMethod.Get, Provider.TilesUrl.Replace("{z}", zoom.ToString(CultureInfo.InvariantCulture)).Replace("{x}", x.ToString(CultureInfo.InvariantCulture)).Replace("{y}", y.ToString(CultureInfo.InvariantCulture)));
            OnlineIdentity.Apply(request);
            if (File.Exists(file) && meta is not null)
            {
                if (meta.ETag is { } etag && EntityTagHeaderValue.TryParse(etag, out var tag)) request.Headers.IfNoneMatch.Add(tag);
                if (meta.LastModified is { } modified) request.Headers.IfModifiedSince = modified;
            }
            using var response = await http.SendAsync(request, ct);
            if (response.StatusCode == HttpStatusCode.NotModified && File.Exists(file))
            {
                WriteMeta(file, Renewed(response, meta));
                return (await File.ReadAllBytesAsync(file, ct), false);
            }
            if (!response.IsSuccessStatusCode) return (await StaleAsync(file, meta, ct), false);
            var bytes = await response.Content.ReadAsByteArrayAsync(ct);
            Directory.CreateDirectory(Path.GetDirectoryName(file)!); await File.WriteAllBytesAsync(file, bytes, ct);
            WriteMeta(file, Renewed(response, null));
            return (bytes, false);
        }
        catch (HttpRequestException) { return (await StaleAsync(file, meta, ct), false); }
        finally { Connections.Release(); }
    }

    // When the server cannot be reached, a tile already on this computer is shown only as long as the server's
    // stale-if-error allows; nothing is downloaded for offline use.
    private static async Task<byte[]?> StaleAsync(string file, Meta? meta, CancellationToken ct) =>
        meta is not null && File.Exists(file) && DateTimeOffset.UtcNow <= meta.Expires + meta.StaleIfError ? await File.ReadAllBytesAsync(file, ct) : null;

    private static Meta Renewed(HttpResponseMessage response, Meta? previous)
    {
        var now = DateTimeOffset.UtcNow; var control = response.Headers.CacheControl;
        // Freshness is max-age minus the time the response already spent in caches on the way (Age).
        var expires = control?.MaxAge is { } maxAge ? now + (maxAge - (response.Headers.Age ?? TimeSpan.Zero))
            : response.Content.Headers.Expires is { } at && at > now ? at
            : now + DefaultLifetime;
        if (control is { NoCache: true }) expires = now; // ask again (conditionally) next time
        var staleIfError = control?.Extensions.FirstOrDefault(e => e.Name == "stale-if-error")?.Value is { } seconds && int.TryParse(seconds, CultureInfo.InvariantCulture, out var s) ? TimeSpan.FromSeconds(s) : TimeSpan.Zero;
        return new(expires, response.Headers.ETag?.ToString() ?? previous?.ETag, response.Content.Headers.LastModified ?? previous?.LastModified, staleIfError);
    }

    private static Meta? ReadMeta(string file)
    {
        try { return File.Exists(file + ".json") ? JsonSerializer.Deserialize<Meta>(File.ReadAllText(file + ".json")) : null; }
        catch (Exception ex) when (ex is IOException or JsonException) { return null; }
    }

    private static void WriteMeta(string file, Meta meta)
    {
        try { File.WriteAllText(file + ".json", JsonSerializer.Serialize(meta)); }
        catch (IOException) { }
    }

    /// <summary>Bytes the tile cache uses on this computer.</summary>
    public static long CacheSize(string stateRoot) => Directory.Exists(CacheRoot(stateRoot)) ? new DirectoryInfo(CacheRoot(stateRoot)).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length) : 0;

    /// <summary>Keeps the cache below <paramref name="maxBytes"/> by deleting the tiles downloaded longest ago.</summary>
    public static void Prune(string stateRoot, long maxBytes = MaxCacheBytes)
    {
        var root = CacheRoot(stateRoot); if (!Directory.Exists(root)) return;
        var tiles = new DirectoryInfo(root).EnumerateFiles("*.png", SearchOption.AllDirectories).OrderBy(f => f.LastWriteTimeUtc).ToList();
        long Size(FileInfo tile) => tile.Length + (File.Exists(tile.FullName + ".json") ? new FileInfo(tile.FullName + ".json").Length : 0);
        var total = tiles.Sum(Size);
        foreach (var tile in tiles)
        {
            if (total <= maxBytes * 8 / 10) break;
            try { total -= Size(tile); tile.Delete(); File.Delete(tile.FullName + ".json"); }
            catch (IOException) { }
        }
    }

    public static void ClearCache(string stateRoot)
    {
        var path = CacheRoot(stateRoot);
        if (Directory.Exists(path)) Directory.Delete(path, true);
    }
}

public sealed record PlaceResult(string Name, string Address, double Latitude, double Longitude, string? OsmId);

/// <summary>
/// Nominatim place search, opt-in like map tiles. Requests are made only when the person starts a search (no search as
/// you type), at most one per second, and repeated searches in a session are answered from memory.
/// </summary>
public sealed class PlaceSearch(HttpClient http, string searchUrl)
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static DateTime last = DateTime.MinValue;
    private static readonly Dictionary<string, IReadOnlyList<PlaceResult>> Recent = new();

    public async Task<IReadOnlyList<PlaceResult>> SearchAsync(string query, string language, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query)) return [];
        var key = searchUrl + "|" + language + "|" + query.Trim().ToLowerInvariant();
        await Gate.WaitAsync(ct);
        try
        {
            if (Recent.TryGetValue(key, out var known)) return known;
            var wait = TimeSpan.FromSeconds(1) - (DateTime.UtcNow - last); if (wait > TimeSpan.Zero) await Task.Delay(wait, ct);
            var url = searchUrl + (searchUrl.Contains('?') ? "&" : "?") + "format=jsonv2&limit=6&addressdetails=0&q=" + Uri.EscapeDataString(query.Trim()) + "&accept-language=" + Uri.EscapeDataString(language);
            using var request = new HttpRequestMessage(HttpMethod.Get, url); OnlineIdentity.Apply(request);
            using var response = await http.SendAsync(request, ct); last = DateTime.UtcNow;
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
            if (Recent.Count >= 100) Recent.Clear();
            Recent[key] = results;
            return results;
        }
        finally { Gate.Release(); }
    }
}
