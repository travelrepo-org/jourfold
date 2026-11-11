using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Jourfold.Mobile;
using NodaTime;
using TravelRepo.Git;
using Xunit;
namespace Jourfold.Tests;

/// <summary>A trip that TravelRepo will not change says so on the phone instead of ending the app.</summary>
public sealed class MobileProblemTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "jourfold-problem-" + Guid.NewGuid());
    public void Dispose() => TestFiles.Delete(root);

    [AvaloniaFact]
    public async Task ChoosingWhoIAmInATripWithErrorsShowsWhyInsteadOfCrashing()
    {
        var tokyo = DateTimeZoneProviders.Tzdb["Asia/Tokyo"];
        var now = new LocalDateTime(2027, 5, 16, 10, 40).InZoneStrictly(tokyo).ToInstant();
        var session = new MobileSession(new MobileHost(root, new GitCliBackend(), new FakeClock(now), () => tokyo, "en", new MemorySecrets()));
        var window = new Window { Width = 412, Height = 915, Content = new AppShell(session) }; window.Show();
        try
        {
            IEnumerable<string> Texts() { Probe.Layout(); return window.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text ?? ""); }
            var path = await session.CreateSampleAsync(new LocalDate(2027, 5, 14));
            await session.SaveSettingsAsync("Sam", "sam@example.invalid");
            // One plan of the trip can no longer be read, as after a broken copy.
            await File.WriteAllTextAsync(Directory.EnumerateFiles(Path.Combine(path, "schedule"), "*", SearchOption.AllDirectories).First(), "title: [unclosed\n");
            await session.OpenAsync(path);
            Assert.NotEmpty(session.TripErrors);
            Assert.Contains("This trip has errors", Texts());

            session.OpenPage("travellers"); Probe.Layout();
            var sam = session.Trip!.Entities.Values.First(e => e.Type == "person" && e.Title.StartsWith("Sam", StringComparison.Ordinal));
            Probe.Click(window, sam.Title); Probe.Layout();
            Probe.Click(window, "This is me");
            await Probe.Until(() => session.ChangeProblem is not null);
            Assert.Contains("Not saved", Texts());
            Assert.Contains(session.ChangeProblem, Texts());

            Probe.Click(window, "Close");
            Assert.Null(session.ChangeProblem); Assert.DoesNotContain("Not saved", Texts());
        }
        finally { window.Close(); session.Dispose(); }
    }

    [AvaloniaFact]
    public async Task AnUnexpectedErrorIsShownAndLoggedOnTheDeviceInsteadOfEndingTheApp()
    {
        var tokyo = DateTimeZoneProviders.Tzdb["Asia/Tokyo"];
        var session = new MobileSession(new MobileHost(root, new GitCliBackend(), new FakeClock(Instant.FromUtc(2027, 5, 16, 1, 0)), () => tokyo, "en", new MemorySecrets(), AppVersion: "9.9.9"));
        var window = new Window { Width = 412, Height = 915, Content = new AppShell(session) }; window.Show();
        try
        {
            IEnumerable<string> Texts() { Probe.Layout(); return window.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text ?? ""); }
            session.ShowMenu(new ActionMenu("Test", null, [new MenuAction("Break", "x", _ => throw new InvalidOperationException("A bug in an action"))])); Probe.Layout();
            Probe.Click(window, "Break");
            await Probe.Until(() => session.ChangeProblem is not null);
            Assert.Contains("Something went wrong", Texts());
            Assert.Contains("Jourfold could not finish this. The details are kept in a log on this device and are not sent anywhere.", Texts());
            var log = await File.ReadAllTextAsync(session.ErrorLog);
            Assert.Contains("Jourfold 9.9.9", log); Assert.Contains("A bug in an action", log);

            Probe.Click(window, "Close");
            Assert.Null(session.ChangeProblem); Assert.DoesNotContain("Something went wrong", Texts());
        }
        finally { window.Close(); session.Dispose(); }
    }

    private sealed class FakeClock(Instant now) : IClock { public Instant GetCurrentInstant() => now; }

    private sealed class MemorySecrets : TravelRepo.Providers.ISecretStore
    {
        private readonly Dictionary<string, string> values = [];
        public Task<string?> ReadAsync(string key, CancellationToken ct = default) => Task.FromResult(values.GetValueOrDefault(key));
        public Task WriteAsync(string key, string value, CancellationToken ct = default) { values[key] = value; return Task.CompletedTask; }
        public Task DeleteAsync(string key, CancellationToken ct = default) { values.Remove(key); return Task.CompletedTask; }
    }
}
