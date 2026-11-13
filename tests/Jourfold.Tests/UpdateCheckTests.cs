using System.Net;
using System.Text;
using System.Text.Json;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Jourfold.Application;
using Jourfold.Desktop;
using Jourfold.Mobile;
using NodaTime;
using TravelRepo.Git;
using Xunit;

namespace Jourfold.Tests;

/// <summary>GitHub builds tell about a newer release; store builds never look. See <see cref="UpdateCheck"/>.</summary>
public sealed class UpdateCheckTests
{
    private sealed class Values : IValueStore
    {
        public Dictionary<string, string> All { get; } = [];
        public string? Get(string key) => All.GetValueOrDefault(key);
        public void Set(string key, string value) => All[key] = value;
    }

    private static readonly AppRelease Next = new("99.1.0", "Jourfold 99.1.0 “Quiet Quokka”", new Uri("https://github.com/travelrepo-org/jourfold/releases/tag/v99.1.0"));

    [Theory]
    [InlineData("0.12.0", "0.12.1", -1)]
    [InlineData("0.12.9", "0.13.0", -1)]
    [InlineData("0.9.0", "0.10.0", -1)]
    [InlineData("1.0.0", "0.99.99", 1)]
    [InlineData("0.13.0-preview.3", "0.13.0", -1)]
    [InlineData("0.13.0-preview.3", "0.13.0-preview.12", -1)]
    [InlineData("0.13.0-beta", "0.13.0-preview", -1)]
    [InlineData("0.13.0-preview", "0.13.0-preview.1", -1)]
    [InlineData("0.12.0+3f1c2ab", "0.12.0", 0)]
    [InlineData("v0.12.0", "0.12.0", 0)]
    [InlineData("not a version", "0.0.1", -1)]
    public void VersionsAreOrderedAsSemanticVersioningOrdersThem(string a, string b, int expected)
    {
        Assert.Equal(expected, UpdateCheck.Compare(a, b));
        Assert.Equal(-expected, UpdateCheck.Compare(b, a));
    }

    [Fact]
    public void OnlyAFullReleaseFromGitHubIsOffered()
    {
        AppRelease? Read(string json) { using var doc = JsonDocument.Parse(json); return UpdateCheck.Parse(doc.RootElement); }
        const string page = "\"html_url\":\"https://github.com/travelrepo-org/jourfold/releases/tag/v0.13.0\"";
        Assert.Equal(new AppRelease("0.13.0", "Jourfold 0.13.0 “Gallant Gnu”", new Uri("https://github.com/travelrepo-org/jourfold/releases/tag/v0.13.0")),
            Read("{\"tag_name\":\"v0.13.0\",\"name\":\"Jourfold 0.13.0 “Gallant Gnu”\"," + page + ",\"draft\":false,\"prerelease\":false}"));
        Assert.Equal("Jourfold 0.13.0", Read("{\"tag_name\":\"v0.13.0\",\"name\":\"\"," + page + "}")!.Title);
        Assert.Null(Read("{\"tag_name\":\"v0.13.0\"," + page + ",\"prerelease\":true}"));
        Assert.Null(Read("{\"tag_name\":\"v0.13.0\"," + page + ",\"draft\":true}"));
        Assert.Null(Read("{\"tag_name\":\"v0.13.0-android.2\"," + page + "}"));
        Assert.Null(Read("{\"tag_name\":\"nightly\"," + page + "}"));
        Assert.Null(Read("{\"tag_name\":\"v0.13.0\",\"html_url\":\"http://example.com/\"}"));
    }

    [Fact]
    public async Task GitHubIsAskedWithoutSigningInAndOnlyTheAppIsNamed()
    {
        HttpRequestMessage? seen = null;
        var http = new HttpClient(new Answer(request =>
        {
            seen = request;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"tag_name\":\"v0.13.0\",\"name\":\"Jourfold 0.13.0\",\"html_url\":\"https://github.com/travelrepo-org/jourfold/releases/tag/v0.13.0\"}", Encoding.UTF8, "application/json") };
        }));
        var release = await UpdateCheck.LatestOnGitHubAsync(http, "https://github.com/travelrepo-org/jourfold", "Jourfold/0.12.0 (+https://github.com/travelrepo-org/jourfold)");
        Assert.Equal("0.13.0", release!.Version);
        Assert.Equal("https://api.github.com/repos/travelrepo-org/jourfold/releases/latest", seen!.RequestUri!.AbsoluteUri);
        Assert.Null(seen.Headers.Authorization);
        Assert.Equal("Jourfold/0.12.0 (+https://github.com/travelrepo-org/jourfold)", seen.Headers.UserAgent.ToString());
        Assert.Null(seen.Content);

        var none = new HttpClient(new Answer(_ => new HttpResponseMessage(HttpStatusCode.NotFound)));
        Assert.Null(await UpdateCheck.LatestOnGitHubAsync(none, "https://github.com/travelrepo-org/jourfold", "Jourfold"));
        Assert.Null(await UpdateCheck.LatestOnGitHubAsync(http, "https://example.com/jourfold", "Jourfold"));
    }

    [Fact]
    public async Task AGitHubBuildLooksOnceADayAndOfflineItTriesAgainAtTheNextStart()
    {
        var store = new Values(); var now = new DateTimeOffset(2026, 11, 13, 9, 0, 0, TimeSpan.Zero); var asked = 0; var offline = true;
        var check = new UpdateCheck(store, "0.12.0+3f1c2ab", AppDistribution.GitHub, () => now);
        Task<AppRelease?> Source(CancellationToken _) { asked++; return offline ? throw new HttpRequestException("offline") : Task.FromResult<AppRelease?>(Next); }

        Assert.True(check.On);
        Assert.False(await check.CheckAsync(Source));
        Assert.Null(check.Offer); Assert.True(check.Due);
        offline = false;
        Assert.True(await check.CheckAsync(Source));
        Assert.Equal(Next, check.Offer);
        now = now.AddHours(20);
        Assert.False(await check.CheckAsync(Source));
        Assert.Equal(2, asked);
        // What was found stays known offline until the next answer.
        Assert.Equal(Next, new UpdateCheck(store, "0.12.0", AppDistribution.GitHub, () => now).Offer);
        now = now.AddHours(5); offline = true;
        Assert.False(await check.CheckAsync(Source));
        Assert.Equal(3, asked); Assert.Equal(Next, check.Offer);
        // Once it is installed there is nothing to tell.
        Assert.Null(new UpdateCheck(store, "99.1.0", AppDistribution.GitHub, () => now).Offer);
        Assert.Null(new UpdateCheck(store, "99.2.0-preview.1", AppDistribution.GitHub, () => now).Offer);
    }

    [Fact]
    public async Task ASkippedVersionStaysQuietButALaterOneIsToldAbout()
    {
        var store = new Values(); var now = DateTimeOffset.UnixEpoch; var latest = Next;
        var check = new UpdateCheck(store, "0.12.0", AppDistribution.GitHub, () => now);
        await check.CheckAsync(_ => Task.FromResult<AppRelease?>(latest));
        check.Skip(check.Offer!);
        Assert.Null(check.Offer);
        now = now.AddDays(1);
        Assert.False(await check.CheckAsync(_ => Task.FromResult<AppRelease?>(latest)));
        latest = Next with { Version = "99.2.0", Title = "Jourfold 99.2.0" };
        now = now.AddDays(1);
        Assert.True(await check.CheckAsync(_ => Task.FromResult<AppRelease?>(latest)));
        Assert.Equal("99.2.0", check.Offer!.Version);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("fdroid")]
    [InlineData("play")]
    [InlineData("appstore")]
    public async Task StoreAndSelfBuiltCopiesNeverLook(string? distribution)
    {
        var store = new Values(); var asked = 0;
        var check = new UpdateCheck(store, "0.12.0", distribution);
        Assert.False(check.Available); Assert.False(check.On); Assert.False(check.Due);
        Assert.False(await check.CheckAsync(_ => { asked++; return Task.FromResult<AppRelease?>(Next); }));
        Assert.Equal(0, asked);
    }

    [Fact]
    public async Task TurnedOffItNeverAsks()
    {
        var store = new Values(); var asked = 0;
        var check = new UpdateCheck(store, "0.12.0", AppDistribution.GitHub);
        await check.CheckAsync(_ => Task.FromResult<AppRelease?>(Next));
        check.Set(false);
        Assert.Null(check.Offer); Assert.False(check.Due);
        Assert.False(await check.CheckAsync(_ => { asked++; return Task.FromResult<AppRelease?>(Next); }));
        Assert.Equal(0, asked);
        check.Set(true);
        Assert.Equal(Next, check.Offer);
    }

    [AvaloniaFact]
    public async Task TheDesktopTellsAboutANewVersionAboveTheTripsAndCanStopLooking()
    {
        var w = new MainWindow(); var m = w.Model;
        foreach (var key in new[] { UpdateCheck.OnKey, UpdateCheck.CheckedKey, UpdateCheck.LatestKey, UpdateCheck.TitleKey, UpdateCheck.PageKey, UpdateCheck.SkipKey }) m.Store.Set(key, "");
        m.SetPreference("Language", "en");
        w.Show(); Dispatcher.UIThread.RunJobs();
        try
        {
            m.ReleaseSource = _ => Task.FromResult<AppRelease?>(Next);
            // A copy from a store or built from source never looks.
            m.Distribution = null;
            await m.CheckForUpdatesAsync(); Probe.Layout();
            Assert.DoesNotContain(Probe.All<TextBlock>(w), t => t.Text?.Contains("is available", StringComparison.Ordinal) == true);

            m.Distribution = AppDistribution.GitHub;
            await m.CheckForUpdatesAsync();
            await Probe.Until(() => Probe.All<TextBlock>(w).Any(t => t.Text == "Jourfold 99.1.0 “Quiet Quokka” is available"));
            Assert.Contains(Probe.All<TextBlock>(w), t => t.Text == "Installed: " + AppVersion.Version);
            Assert.Contains(Probe.All<Button>(w), b => b.Content is not null && Probe.All<TextBlock>(b).Any(t => t.Text == "View release"));

            Probe.Click(w, "Skip this version");
            await Probe.Until(() => !Probe.All<TextBlock>(w).Any(t => t.Text?.EndsWith("is available", StringComparison.Ordinal) == true));
            m.Store.Set(UpdateCheck.SkipKey, "");
            m.SetUpdateChecks(true); Probe.Layout();
            await Probe.Until(() => Probe.All<TextBlock>(w).Any(t => t.Text?.EndsWith("is available", StringComparison.Ordinal) == true));

            Probe.Click(w, "Stop checking for updates");
            await Probe.Until(() => !Probe.All<TextBlock>(w).Any(t => t.Text?.EndsWith("is available", StringComparison.Ordinal) == true));
            Assert.False(m.Updates.On);
        }
        finally
        {
            foreach (var key in new[] { UpdateCheck.OnKey, UpdateCheck.CheckedKey, UpdateCheck.LatestKey, UpdateCheck.TitleKey, UpdateCheck.PageKey, UpdateCheck.SkipKey }) m.Store.Set(key, "");
            w.Close();
        }
    }

    [AvaloniaFact]
    public async Task ThePhoneTellsAboutANewVersionAboveTheTripsAndSaysWhereToTurnItBackOn()
    {
        var root = Path.Combine(Path.GetTempPath(), "jourfold-updates-" + Guid.NewGuid()); Directory.CreateDirectory(root);
        var tokyo = DateTimeZoneProviders.Tzdb["Asia/Tokyo"];
        var session = new MobileSession(new MobileHost(root, new GitCliBackend(), SystemClock.Instance, () => tokyo, "en", new MemorySecrets(), AppVersion: "0.12.0-preview.3", Distribution: AppDistribution.GitHub))
        {
            ReleaseSource = _ => Task.FromResult<AppRelease?>(Next),
        };
        var window = new Window { Width = 412, Height = 915, Content = new AppShell(session) }; window.Show();
        try
        {
            IEnumerable<string> Texts() { Probe.Layout(); return window.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text ?? ""); }
            await Probe.Until(() => Texts().Contains("Jourfold 99.1.0 “Quiet Quokka” is available"));
            Assert.Contains("Installed: 0.12.0-preview.3", Texts());

            session.ShowPage("settings");
            await Probe.Until(() => Probe.All<ToggleSwitch>(window).Any(t => AutomationProperties.GetName(t) == "Check for new versions" && t.IsChecked == true));
            session.ShowPage(null);

            await Probe.Until(() => Texts().Any(t => t.EndsWith("is available", StringComparison.Ordinal)));
            Probe.Click(window, "Stop checking for updates");
            await Probe.Until(() => Texts().Contains("Jourfold no longer checks for new versions. You can turn it back on in Settings."));
            Assert.DoesNotContain(Texts(), t => t.EndsWith("is available", StringComparison.Ordinal));
            Probe.Click(window, "Close");
            await Probe.Until(() => !Texts().Any(t => t.StartsWith("Jourfold no longer checks", StringComparison.Ordinal)));

            session.ShowPage("settings");
            await Probe.Until(() => Probe.All<ToggleSwitch>(window).Any(t => AutomationProperties.GetName(t) == "Check for new versions" && t.IsChecked == false));
        }
        finally { window.Close(); session.Dispose(); }
    }

    [AvaloniaFact]
    public async Task AStoreBuildOnThePhoneShowsNeitherTheNoticeNorTheSetting()
    {
        var root = Path.Combine(Path.GetTempPath(), "jourfold-updates-" + Guid.NewGuid()); Directory.CreateDirectory(root);
        var tokyo = DateTimeZoneProviders.Tzdb["Asia/Tokyo"]; var asked = 0;
        var session = new MobileSession(new MobileHost(root, new GitCliBackend(), SystemClock.Instance, () => tokyo, "en", new MemorySecrets(), Distribution: "play"))
        {
            ReleaseSource = _ => { asked++; return Task.FromResult<AppRelease?>(Next); },
        };
        var window = new Window { Width = 412, Height = 915, Content = new AppShell(session) }; window.Show();
        try
        {
            await session.CheckForUpdatesAsync(); Probe.Layout();
            Assert.Equal(0, asked);
            session.ShowPage("settings"); Probe.Layout();
            await Probe.Until(() => Probe.All<TextBlock>(window).Any(t => t.Text == "About Jourfold"));
            Assert.DoesNotContain(Probe.All<ToggleSwitch>(window), t => AutomationProperties.GetName(t) == "Check for new versions");
        }
        finally { window.Close(); session.Dispose(); }
    }

    private sealed class Answer(Func<HttpRequestMessage, HttpResponseMessage> answer) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(answer(request));
    }
}
