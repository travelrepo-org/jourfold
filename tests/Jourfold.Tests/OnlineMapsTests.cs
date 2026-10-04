using System.Net;
using System.Net.Http.Headers;
using Jourfold.Infrastructure;
using Xunit;
namespace Jourfold.Tests;

public sealed class OnlineMapsTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "jourfold-maps-" + Guid.NewGuid());
    public void Dispose() => TestFiles.Delete(root);

    private sealed class Server(Func<HttpRequestMessage, HttpResponseMessage> answer) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) { Requests.Add(request); return Task.FromResult(answer(request)); }
    }
    private static HttpResponseMessage Tile(TimeSpan maxAge, string etag = "\"v1\"")
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3]) };
        response.Headers.CacheControl = new CacheControlHeaderValue { MaxAge = maxAge }; response.Headers.ETag = new EntityTagHeaderValue(etag);
        return response;
    }

    [Fact]
    public async Task TilesFollowCacheHeadersAndRenewWithConditionalRequests()
    {
        var server = new Server(r => r.Headers.IfNoneMatch.Count > 0 ? new HttpResponseMessage(HttpStatusCode.NotModified) : Tile(TimeSpan.Zero));
        var tiles = new MapTiles(root, new HttpClient(server), MapProvider.OpenStreetMap);
        Assert.Equal([1, 2, 3], (await tiles.GetAsync(3, 4, 5)).Bytes);
        var first = Assert.Single(server.Requests);
        Assert.Equal("https://tile.openstreetmap.org/3/4/5.png", first.RequestUri!.AbsoluteUri);
        Assert.StartsWith("Jourfold/", first.Headers.UserAgent.ToString()); Assert.Contains("+https://github.com/travelrepo-org/jourfold", first.Headers.UserAgent.ToString());

        // max-age=0: the next view asks "changed since?" and keeps the stored image on 304.
        Assert.Equal([1, 2, 3], (await tiles.GetAsync(3, 4, 5)).Bytes);
        Assert.Equal(2, server.Requests.Count); Assert.Equal("\"v1\"", server.Requests[1].Headers.IfNoneMatch.Single().Tag);
    }

    [Fact]
    public async Task FreshnessCountsTheAgeHeaderAndStaleTilesFollowStaleIfError()
    {
        var reachable = true;
        var server = new Server(_ =>
        {
            if (!reachable) throw new HttpRequestException("offline");
            var response = Tile(TimeSpan.FromSeconds(518701)); response.Headers.Age = TimeSpan.FromSeconds(518701);
            response.Headers.CacheControl!.Extensions.Add(new NameValueHeaderValue("stale-if-error", "604800")); return response;
        });
        var tiles = new MapTiles(root, new HttpClient(server), MapProvider.OpenStreetMap);
        await tiles.GetAsync(6, 1, 1);
        await tiles.GetAsync(6, 1, 1); Assert.Equal(2, server.Requests.Count); // already as old as max-age: renewed on the next view
        reachable = false;
        Assert.Equal([1, 2, 3], (await tiles.GetAsync(6, 1, 1)).Bytes); // within stale-if-error

        var strict = new Server(_ => reachable ? Tile(TimeSpan.Zero) : throw new HttpRequestException("offline"));
        var other = new MapTiles(Path.Combine(root, "other"), new HttpClient(strict), MapProvider.OpenStreetMap);
        reachable = true; await other.GetAsync(6, 2, 2); reachable = false;
        Assert.Null((await other.GetAsync(6, 2, 2)).Bytes); // no stale-if-error: nothing is shown offline
    }

    [Fact]
    public async Task FreshTilesComeFromDiskAndHiddenTilesAreNotRequested()
    {
        var server = new Server(_ => Tile(TimeSpan.FromDays(7)));
        var tiles = new MapTiles(root, new HttpClient(server), MapProvider.OpenStreetMap);
        await tiles.GetAsync(5, 1, 1); await tiles.GetAsync(5, 1, 1);
        Assert.Single(server.Requests);
        var (bytes, skipped) = await tiles.GetAsync(5, 2, 2, stillWanted: () => false);
        Assert.Null(bytes); Assert.True(skipped); Assert.Single(server.Requests);
        Assert.Equal((null, false), await tiles.GetAsync(20, 1, 1)); // beyond the server's zoom levels
    }

    [Fact]
    public async Task CacheStaysWithinItsLimitRemovingTheOldestFirst()
    {
        var tiles = new MapTiles(root, new HttpClient(new Server(_ => Tile(TimeSpan.FromDays(7)))), MapProvider.OpenStreetMap);
        for (var x = 0; x < 6; x++) await tiles.GetAsync(4, x, 0);
        var files = Directory.GetFiles(MapTiles.CacheRoot(root), "*.png", SearchOption.AllDirectories).OrderBy(f => f).ToArray();
        for (var i = 0; i < files.Length; i++) File.SetLastWriteTimeUtc(files[i], DateTime.UtcNow.AddDays(-10 + i));
        var limit = MapTiles.CacheSize(root) / 2;
        MapTiles.Prune(root, limit);
        Assert.True(MapTiles.CacheSize(root) <= limit);
        Assert.False(File.Exists(files[0])); Assert.True(File.Exists(files[^1]));
    }

    [Fact]
    public async Task ProvidersComeFromOwnSettingsThenThePublishedListThenOpenStreetMap()
    {
        using var store = new LocalStore(Path.Combine(root, "state"));
        Assert.Equal(MapProvider.OpenStreetMap, MapProviders.Current(store));
        Assert.Equal(new Uri("https://raw.githubusercontent.com/travelrepo-org/jourfold/main/config/map-providers.json"), MapProviders.ListUrl);

        var list = """{"tiles":{"url":"https://tiles.example.org/{z}/{x}/{y}.png","attribution":"© OpenStreetMap contributors, Example","attributionUrl":"https://example.org/credits","maxZoom":18},"search":{"url":"https://search.example.org/search"}}""";
        var server = new Server(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(list) });
        await MapProviders.RefreshAsync(store, new HttpClient(server));
        Assert.Equal("https://tiles.example.org/{z}/{x}/{y}.png", MapProviders.Current(store).TilesUrl);
        await MapProviders.RefreshAsync(store, new HttpClient(server)); Assert.Single(server.Requests); // at most once a day

        store.Set(MapProviders.SearchKey, "https://nominatim.example.com/search");
        Assert.Equal("https://nominatim.example.com/search", MapProviders.Current(store).SearchUrl); Assert.True(MapProviders.HasOwnServers(store));
        store.Set(MapProviders.TilesKey, "http://insecure.example.org/{z}/{x}/{y}.png");
        Assert.Equal("https://tiles.example.org/{z}/{x}/{y}.png", MapProviders.Current(store).TilesUrl); // invalid own servers are ignored

        Assert.Null(MapProviders.Parse("""{"tiles":{"url":"https://evil.example.org/tile.png"}}"""));
        Assert.Equal(MapProvider.OpenStreetMap, MapProviders.Parse(File.ReadAllText(Path.Combine(RepositoryRoot(), "config", "map-providers.json"))));
    }

    [Fact]
    public async Task RepeatedSearchesAreAnsweredFromMemory()
    {
        var server = new Server(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""[{"display_name":"Auckland, New Zealand","name":"Auckland","lat":"-36.85","lon":"174.76","osm_type":"relation","osm_id":2094141}]""") });
        var search = new PlaceSearch(new HttpClient(server), "https://search.example.org/search?countrycodes=nz");
        var query = "Auckland " + Guid.NewGuid().ToString("N")[..6];
        var found = await search.SearchAsync(query, "en"); await search.SearchAsync(query, "en");
        Assert.Equal("Auckland", Assert.Single(found).Name); Assert.Single(server.Requests);
        Assert.StartsWith("https://search.example.org/search?countrycodes=nz&format=jsonv2", server.Requests[0].RequestUri!.AbsoluteUri);
        Assert.StartsWith("Jourfold/", server.Requests[0].Headers.UserAgent.ToString());
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "config"))) directory = directory.Parent;
        return directory!.FullName;
    }
}
