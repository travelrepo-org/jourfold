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
        var mails = new List<ProblemMail>(); var attached = new List<string>(); var asked = new List<string>();
        var session = new MobileSession(new MobileHost(root, new GitCliBackend(), new FakeClock(Instant.FromUtc(2027, 5, 16, 1, 0)), () => tokyo, "en", new MemorySecrets(), Http: new GitHubContact(asked), AppVersion: "9.9.9", Distribution: "github",
            SendMail: mail => { mails.Add(mail); attached.AddRange(mail.Attachments.Select(File.ReadAllText)); return Task.FromResult(true); },
            SystemInfo: () => [("ReportInfoDevice", "Example Phone 1"), ("ReportInfoSystem", "Android 16 (API 36)"), ("ReportInfoScreen", "1080 × 2400 px, 420 dpi")]));
        var window = new Window { Width = 412, Height = 915, Content = new AppShell(session) }; window.Show();
        try
        {
            IEnumerable<string> Texts() { Probe.Layout(); return window.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text ?? ""); }
            session.ShowMenu(new ActionMenu("Test", null, [new MenuAction("Break", "x", _ => throw new InvalidOperationException("A bug in an action"))])); Probe.Layout();
            Probe.Click(window, "Break");
            await Probe.Until(() => session.ChangeProblem is not null);
            Assert.Contains("Something went wrong", Texts());
            Assert.Contains("Jourfold could not finish this.", Texts());
            var log = await File.ReadAllTextAsync(session.ErrorLog);
            Assert.Contains("Jourfold 9.9.9", log); Assert.Contains("A bug in an action", log);
            // Nothing leaves the device until the user asks: the report opens in their own mail app.
            Assert.DoesNotContain(asked, a => a.StartsWith("/users/", StringComparison.Ordinal));
            Probe.Click(window, "Send error by e-mail");
            await Probe.Until(() => mails.Count == 1);
            var mail = mails[0];
            Assert.Equal(("support@example.org", "Jourfold 9.9.9: problem report"), (mail.To, mail.Subject));
            Assert.StartsWith("What happened:", mail.Body);
            foreach (var line in new[] { "App: Jourfold 9.9.9 (github)", "Device: Example Phone 1", "System: Android 16 (API 36)", "Screen: 1080 × 2400 px, 420 dpi", "Device type: phone", "Language: en" })
                Assert.Contains(line, mail.Body);
            // The log goes as an attachment, not in the text.
            Assert.DoesNotContain("A bug in an action", mail.Body);
            Assert.Contains(attached, a => a.Contains("InvalidOperationException: A bug in an action", StringComparison.Ordinal));
            Assert.All(mail.Attachments, a => Assert.StartsWith(session.Host.SharedRoot, a));
            Assert.Equal(["/users/travelrepo-org"], asked.Where(a => a.StartsWith("/users/", StringComparison.Ordinal)));
            Assert.Null(session.ChangeProblem);
        }
        finally { window.Close(); session.Dispose(); }
    }

    [AvaloniaFact]
    public async Task AProblemThatSaysWhyHasNoMailAndAPhoneWithoutMailAppSaysSo()
    {
        var tokyo = DateTimeZoneProviders.Tzdb["Asia/Tokyo"];
        var session = new MobileSession(new MobileHost(root, new GitCliBackend(), new FakeClock(Instant.FromUtc(2027, 5, 16, 1, 0)), () => tokyo, "en", new MemorySecrets(), Http: new GitHubContact([]), SendMail: _ => Task.FromResult(false)));
        var window = new Window { Width = 412, Height = 915, Content = new AppShell(session) }; window.Show();
        try
        {
            session.ShowProblem(new TravelRepo.Core.DomainException("trip.locked", "The trip is locked."));
            Probe.Layout();
            Assert.False(session.ProblemCanBeMailed);
            session.DismissProblem();
            session.ShowProblem(new InvalidOperationException("A bug"));
            Assert.True(session.ProblemCanBeMailed);
            await session.SendProblemByMailAsync();
            Assert.Equal("No mail app was found on this device.", session.ChangeProblem);
        }
        finally { window.Close(); session.Dispose(); }
    }

    /// <summary>GitHub's answer for the account that publishes Jourfold, with a public contact address.</summary>
    private sealed class GitHubContact(List<string> asked) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            asked.Add(request.RequestUri!.AbsolutePath);
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent("""{"email":"support@example.org"}""") });
        }
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
