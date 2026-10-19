using Avalonia.Headless.XUnit;
using Jourfold.Desktop;
using Jourfold.Infrastructure;
using TravelRepo.Git;
using Xunit;
namespace Jourfold.Tests;

/// <summary>Release notes bundled from docs/releases, and "What's new" shown once after an update.</summary>
public sealed class ReleaseNotesTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "jourfold-releases-" + Guid.NewGuid());
    public void Dispose() => TestFiles.Delete(root);

    [Fact]
    public void TheCurrentVersionHasCompleteNotes()
    {
        var releases = ReleaseNotes.All;
        Assert.Equal(releases.OrderByDescending(r => ReleaseNotes.Key(r.Version)).Select(r => r.Version), releases.Select(r => r.Version));
        var current = Assert.Single(releases, r => r.Version == AppVersion.Version);
        Assert.NotEmpty(current.Highlights);
        Assert.All(current.Highlights.Concat(current.Changes).Concat(current.Fixes), e => { Assert.True(e.Texts.ContainsKey("en")); Assert.True(e.Texts.ContainsKey("de")); });
        // A language added later falls back to English.
        Assert.Equal(current.Highlights[0].Texts["en"], current.Highlights[0].Text("fr"));
    }

    [Fact]
    public void WhatsNewCoversTheReleasesSinceTheLastOneSeen()
    {
        var current = AppVersion.Version;
        Assert.Empty(ReleaseNotes.Since(null, current, usedBefore: false));
        Assert.Empty(ReleaseNotes.Since(current, current, usedBefore: true));
        Assert.Equal([current], ReleaseNotes.Since(null, current, usedBefore: true).Select(r => r.Version).ToArray());
        var since = ReleaseNotes.Since("0.1.1", current, usedBefore: true).Select(r => r.Version).ToArray();
        Assert.Contains(current, since); Assert.Contains("0.2.0", since); Assert.DoesNotContain("0.1.1", since);
        Assert.True(ReleaseNotes.Key("0.10.0").CompareTo(ReleaseNotes.Key("0.9.1")) > 0);
        Assert.True(ReleaseNotes.Key("1.0.0-beta.1").CompareTo(ReleaseNotes.Key("1.0.0")) < 0);
    }

    [AvaloniaFact]
    public async Task WhatsNewAppearsOnceAfterAnUpdate()
    {
        using var store = new LocalStore(Path.Combine(root, "local")); var interaction = new TestInteraction();
        using var m = new MainViewModel(store, new GitCliBackend(), new OsSecretStore(), interaction);
        store.Set("lastView", "Plan"); store.Set("whatsnew.seen", "0.3.1");
        await m.ShowWhatsNewAsync();
        var shown = Assert.Single(interaction.ReleaseNotesShown);
        Assert.True(shown.HighlightsOnly); Assert.Equal(AppVersion.Version, shown.Releases[0].Version);
        await m.ShowWhatsNewAsync();
        Assert.Single(interaction.ReleaseNotesShown);
        await m.VersionHistoryCommand.ExecuteAsync(null);
        Assert.False(interaction.ReleaseNotesShown[^1].HighlightsOnly); Assert.True(interaction.ReleaseNotesShown[^1].Releases.Count >= 5);
    }
}
