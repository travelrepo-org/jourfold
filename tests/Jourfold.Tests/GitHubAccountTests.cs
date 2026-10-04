using System.Net;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Jourfold.Desktop;
using Jourfold.Infrastructure;
using TravelRepo.Git;
using TravelRepo.Providers;
using TravelRepo.Providers.GitHub;
using Xunit;
namespace Jourfold.Tests;

public sealed class GitHubAccountTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "jourfold-github-" + Guid.NewGuid());
    public void Dispose() => TestFiles.Delete(root);

    private sealed class MemorySecrets : ISecretStore
    {
        public Dictionary<string, string> Values { get; } = [];
        public Task<string?> ReadAsync(string key, CancellationToken ct = default) => Task.FromResult(Values.GetValueOrDefault(key));
        public Task WriteAsync(string key, string value, CancellationToken ct = default) { Values[key] = value; return Task.CompletedTask; }
        public Task DeleteAsync(string key, CancellationToken ct = default) { Values.Remove(key); return Task.CompletedTask; }
    }
    private sealed class UserHandler : HttpMessageHandler
    {
        public bool Offline { get; set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (Offline) throw new HttpRequestException("offline");
            Assert.EndsWith("/user", request.RequestUri!.AbsolutePath);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"login\":\"alex\",\"name\":\"Alex Example\",\"html_url\":\"https://github.com/alex\"}") });
        }
    }

    [Fact]
    public async Task AccountIsRememberedForOfflineUseAndDisconnectForgetsIt()
    {
        var secrets = new MemorySecrets { Values = { ["github.user-token"] = "token" } }; var handler = new UserHandler();
        using var http = new HttpClient(handler); using var store = new LocalStore(Path.Combine(root, "state"));
        var interaction = new TestInteraction();
        using var vm = new MainViewModel(store, new GitCliBackend(), new OsSecretStore(), interaction, () => new GitHubProvider(http, secrets, "client"));
        await vm.RefreshGitHubAccountAsync();
        Assert.True(vm.GitHubConnected); Assert.Equal("alex", vm.GitHubLogin); Assert.Equal("Alex Example", vm.GitHubAccount!.Name);

        using var offline = new MainViewModel(store, new GitCliBackend(), new OsSecretStore(), interaction, () => new GitHubProvider(http, secrets, "client"));
        handler.Offline = true; await offline.RefreshGitHubAccountAsync();
        Assert.True(offline.GitHubConnected); Assert.Null(offline.GitHubAccount); Assert.Equal("alex", offline.GitHubLogin);

        await vm.DisconnectGitHubCommand.ExecuteAsync(null);
        Assert.False(vm.GitHubConnected); Assert.Null(vm.GitHubLogin); Assert.Empty(secrets.Values);
        Assert.Contains(vm.Strings["GitHubDisconnected"], interaction.Toasts);
    }

    [AvaloniaFact]
    public async Task SettingsShowTheAccountInsteadOfAConnectButton()
    {
        var w = new MainWindow(); w.Model.SetPreference("Language", "en"); w.Show(); Probe.Layout();
        var secrets = new MemorySecrets { Values = { ["github.user-token"] = "token" } };
        using var http = new HttpClient(new UserHandler()); using var store = new LocalStore(Path.Combine(root, "state"));
        using var vm = new MainViewModel(store, new GitCliBackend(), new OsSecretStore(), w.Model.Interaction, () => new GitHubProvider(http, secrets, "client"));
        try
        {
            var settings = ((Dialogs)w.Model.Interaction).SettingsAsync(vm); Probe.Layout();
            Probe.Click(Probe.Sheet(w), vm.Strings["Providers"]); Probe.Layout();
            static IEnumerable<string?> Texts(Control sheet) => Probe.All<TextBlock>(sheet).Select(t => t.Text);
            static bool Has(Control sheet, string label) => Probe.All<Button>(sheet).Any(b => Avalonia.Automation.AutomationProperties.GetName(b) == label);
            await Probe.Until(() => Texts(Probe.Sheet(w)).Contains("Signed in as @alex"));
            var sheet = Probe.Sheet(w);
            Assert.Contains(vm.Strings["Connected"], Texts(sheet)); Assert.Contains("Alex Example", Texts(sheet));
            Assert.False(Has(sheet, vm.Strings["ConnectGitHub"])); Assert.True(Has(sheet, vm.Strings["Disconnect"])); Assert.True(Has(sheet, vm.Strings["GitHubProfile"]));

            // Leaving the page and coming back must not offer to connect again.
            Probe.Click(sheet, vm.Strings["General"]); Probe.Layout(); Probe.Click(Probe.Sheet(w), vm.Strings["Providers"]); Probe.Layout();
            Assert.False(Has(Probe.Sheet(w), vm.Strings["ConnectGitHub"])); Assert.Contains("Signed in as @alex", Texts(Probe.Sheet(w)));
            Probe.Click(Probe.Sheet(w), vm.Strings["Close"]); await settings.WaitAsync(TimeSpan.FromSeconds(3));
        }
        finally { w.Close(); }
    }
}
