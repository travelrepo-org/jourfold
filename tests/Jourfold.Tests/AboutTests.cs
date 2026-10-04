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
    public void CodeNamesAreStablePerFeatureRelease(string version, string expected) => Assert.Equal(expected, CodeNames.For(version));

    [Fact]
    public void ReleaseScriptComputesTheSameCodeNames()
    {
        var repository = new DirectoryInfo(AppContext.BaseDirectory);
        while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "eng", "version.py"))) repository = repository.Parent;
        Assert.NotNull(repository);
        foreach (var version in new[] { "0.1.0", "0.4.2", "1.7.0-rc.1", "12.3.0", AppVersion.Version })
        {
            var info = new System.Diagnostics.ProcessStartInfo(OperatingSystem.IsWindows() ? "python" : "python3") { RedirectStandardOutput = true, WorkingDirectory = repository.FullName };
            info.ArgumentList.Add("eng/version.py"); info.ArgumentList.Add("--codename"); info.ArgumentList.Add(version);
            using var process = System.Diagnostics.Process.Start(info)!; var output = process.StandardOutput.ReadToEnd().Trim(); process.WaitForExit();
            Assert.Equal(CodeNames.For(version), output);
        }
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
            Assert.Single(Probe.All<Button>(sheet), b => Avalonia.Automation.AutomationProperties.GetName(b) == "Website");

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
