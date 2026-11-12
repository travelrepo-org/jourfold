using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Jourfold.Desktop;
using Jourfold.Infrastructure;
using TravelRepo.Core;
using Xunit;
namespace Jourfold.Tests;

public sealed class AboutTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "jourfold-about-" + Guid.NewGuid());
    public void Dispose() => TestFiles.Delete(root);

    private string Plugin(string id, params (string Key, string Value)[] fields)
    {
        var directory = Path.Combine(root, id); Directory.CreateDirectory(Path.Combine(directory, "LICENSES"));
        var manifest = new JsonObject { ["id"] = id, ["version"] = "2.0.0", ["minimumApiVersion"] = 1, ["entryAssembly"] = "P.dll", ["capabilities"] = new JsonArray(), ["permissions"] = new JsonArray() };
        foreach (var (key, value) in fields) manifest[key] = value;
        File.WriteAllText(Path.Combine(directory, "jourfold.plugin.json"), manifest.ToJsonString());
        File.WriteAllText(Path.Combine(directory, "LICENSES", "MIT.txt"), "MIT License text for " + id);
        return directory;
    }

    [Fact]
    public void UnexpectedErrorsAreLoggedWithVersionAndStack()
    {
        ErrorReporting.UseLog(root);
        try { throw new InvalidOperationException("Quick Add broke"); }
        catch (InvalidOperationException ex) { ErrorReporting.Handle(ex); }
        var log = File.ReadAllText(Path.Combine(root, "logs", "errors.log"));
        Assert.Contains("Quick Add broke", log); Assert.Contains("Jourfold " + AppVersion.Version, log); Assert.Contains(nameof(UnexpectedErrorsAreLoggedWithVersionAndStack), log);
    }

    [Fact]
    public void VersionsComeFromTheBuild()
    {
        Assert.Matches(@"^\d+\.\d+\.\d+", AppVersion.Version);
        Assert.DoesNotContain('+', AppVersion.Version);
        Assert.True(AppVersion.Commit is null || AppVersion.Commit.Length == 7);
        Assert.Contains(".NET", AppVersion.Platform);
        Assert.Contains("GNU GENERAL PUBLIC LICENSE", MainViewModel.BundledText("Jourfold.LICENSE"));
        Assert.Contains("Apache License", MainViewModel.BundledText("TravelRepo.LICENSE"));
        Assert.Contains("Avalonia", MainViewModel.BundledText("Jourfold.NOTICES"));
    }

    [Theory]
    [InlineData("0.1.0", "Nomadic Nightingale")]
    [InlineData("0.2.0", "Uplifted Umbrellabird")]
    [InlineData("0.2.5", "Uplifted Umbrellabird")]
    [InlineData("0.3.0-beta.1+abc", "Zesty Zebra")]
    [InlineData("1.0.0", "Untamed Urial")]
    [InlineData("0.11.0", "Frosty Ferret")]
    // Releases up to 0.11.1 keep the names they were published with.
    [InlineData("0.11.1", "Frosty Ferret")]
    [InlineData("0.7.1", "Keen Kudu")]
    public void CodeNamesAreStablePerFeatureRelease(string version, string expected) => Assert.Equal(expected, CodeNames.For(version));

    [Fact]
    public void EachPatchReleaseHasItsOwnAdjective()
    {
        foreach (var feature in new[] { "0.11", "0.12", "1.3" })
        {
            var names = Enumerable.Range(1, 8).Select(patch => CodeNames.For(feature + "." + patch)).ToList();
            var baseName = CodeNames.For(feature + ".0"); var animal = baseName.Split(' ')[1];
            // The animal stays; the adjective starts with its letter and differs from the feature release's.
            Assert.All(names.Skip(feature == "0.11" ? 1 : 0), n => { Assert.EndsWith(" " + animal, n); Assert.NotEqual(baseName, n); Assert.Equal(animal[0], n[0]); });
            Assert.Equal(names.Skip(feature == "0.11" ? 1 : 0).Count(), names.Skip(feature == "0.11" ? 1 : 0).Distinct().Count());
            // Pre-releases are named like their release.
            Assert.Equal(CodeNames.For(feature + ".2"), CodeNames.For(feature + ".2-beta.1+abc"));
        }
    }

    [Fact]
    public void ReleaseScriptComputesTheSameCodeNames()
    {
        var repository = new DirectoryInfo(AppContext.BaseDirectory);
        while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "eng", "version.py"))) repository = repository.Parent;
        Assert.NotNull(repository);
        foreach (var version in new[] { "0.1.0", "0.4.2", "0.11.1", "0.11.2", "0.12.3", "1.7.0-rc.1", "1.7.4-rc.1", "12.3.0", "12.3.17", AppVersion.Version })
        {
            var info = new System.Diagnostics.ProcessStartInfo(OperatingSystem.IsWindows() ? "python" : "python3") { RedirectStandardOutput = true, WorkingDirectory = repository.FullName };
            info.ArgumentList.Add("eng/version.py"); info.ArgumentList.Add("--codename"); info.ArgumentList.Add(version);
            using var process = System.Diagnostics.Process.Start(info)!; var output = process.StandardOutput.ReadToEnd().Trim(); process.WaitForExit();
            Assert.Equal(CodeNames.For(version), output);
        }
        // The website the installer gets is the one the About window links to.
        var website = new System.Diagnostics.ProcessStartInfo(OperatingSystem.IsWindows() ? "python" : "python3") { RedirectStandardOutput = true, WorkingDirectory = repository.FullName };
        website.ArgumentList.Add("eng/version.py"); website.ArgumentList.Add("--website");
        using (var process = System.Diagnostics.Process.Start(website)!) { var output = process.StandardOutput.ReadToEnd().Trim(); process.WaitForExit(); Assert.Equal(AppVersion.WebsiteUrl?.AbsoluteUri.TrimEnd('/'), output); }
        Assert.Equal("https://jourfold.dev/", AppVersion.WebsiteUrl?.AbsoluteUri);
    }

    [Fact]
    public void CatalogRemembersPluginsAndSkipsMissingOnes()
    {
        using var store = new LocalStore(Path.Combine(root, "state"));
        var first = Plugin("org.example.first", ("name", "First"), ("license", "MIT"), ("homepage", "https://example.org/first"));
        var second = Plugin("org.example.second");
        PluginCatalog.Remember(store, first); PluginCatalog.Remember(store, second); PluginCatalog.Remember(store, first);
        Assert.Equal(["org.example.first", "org.example.second"], PluginCatalog.Known(store).Select(p => p.Manifest.Id));
        var known = PluginCatalog.Known(store)[0];
        Assert.Equal("First", known.Manifest.DisplayName);
        Assert.Equal(Path.Combine(first, "LICENSES", "MIT.txt"), known.LicenseFile);
        TestFiles.Delete(second);
        Assert.Single(PluginCatalog.Known(store));
        PluginCatalog.Forget(store, first);
        Assert.Empty(PluginCatalog.Known(store));
    }

    [Fact]
    public async Task PaletteOpensAbout()
    {
        var interaction = new TestInteraction(); using var store = new LocalStore(Path.Combine(root, "state"));
        using var model = new MainViewModel(store, new TravelRepo.Git.GitCliBackend(), new OsSecretStore(), interaction);
        var about = model.PaletteSearch("About").Single(c => c.Id == "command:About");
        await model.ExecutePaletteAsync(about);
        Assert.Equal(1, interaction.AboutShown);
        Assert.Contains("TravelRepo " + TravelRepoInfo.Version, model.AboutText());
    }

    [AvaloniaFact]
    public async Task AboutShowsVersionsLicensesAndPlugins()
    {
        var w = new MainWindow(); w.Model.SetPreference("Language", "en"); w.Show(); Probe.Layout();
        try
        {
            var walks = Plugin("org.example.walks", ("name", "Walks"), ("description", "Adds walks."), ("authors", "Example"), ("license", "MIT"), ("homepage", "https://example.org/walks"));
            PluginCatalog.Remember(w.Model.Store, walks); Probe.Layout();
            var task = w.Model.AboutCommand.ExecuteAsync(null); Probe.Layout();
            var sheet = Probe.Sheet(w);
            var texts = Probe.All<TextBlock>(sheet).Select(t => t.Text).ToList();
            Assert.Contains("Jourfold", texts); Assert.Contains("TravelRepo", texts); Assert.Contains("Walks", texts);
            Assert.Contains(texts, t => t?.Contains("Version " + AppVersion.Version) == true);
            Assert.Contains(texts, t => t?.Contains("Adds walks.") == true);
            Assert.Contains(texts, t => t?.Contains("MIT") == true);
            Assert.Equal(3, Probe.All<Button>(sheet).Count(b => Avalonia.Automation.AutomationProperties.GetName(b) == "Read license"));
            // Jourfold's own website and the plugin's.
            var websites = Probe.All<Button>(sheet).Where(b => Avalonia.Automation.AutomationProperties.GetName(b) == "Website").ToArray();
            Assert.Equal(2, websites.Length); Assert.Equal("https://jourfold.dev/", ToolTip.GetTip(websites[0]));

            Probe.Click(sheet, "Read license"); Probe.Layout();
            var license = Probe.Sheet(w); Assert.NotSame(sheet, license);
            Assert.Contains(Probe.All<SelectableTextBlock>(license), t => t.Text?.Contains("GNU GENERAL PUBLIC LICENSE") == true);
            Probe.Click(license, "Close"); Probe.Layout();
            Probe.Click(Probe.Sheet(w), "Close");
            await task.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.Empty(w.FindControl<Grid>("DialogLayer")!.Children);
            PluginCatalog.Forget(w.Model.Store, walks);
        }
        finally { w.Close(); }
    }
}
