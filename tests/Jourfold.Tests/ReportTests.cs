using System.Net;
using Avalonia.Headless.XUnit;
using Jourfold.Desktop;
using Jourfold.Infrastructure;
using TravelRepo.Git;
using Xunit;
namespace Jourfold.Tests;

/// <summary>Problem reports: reviewed first, then copied or opened in the person's own mail program.</summary>
public sealed class ReportTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "jourfold-report-" + Guid.NewGuid());
    public void Dispose() => TestFiles.Delete(root);

    private sealed class GitHub(string? email) : HttpMessageHandler
    {
        public List<string> Urls { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Urls.Add(request.RequestUri!.AbsoluteUri);
            var body = "{\"login\":\"travelrepo-org\",\"type\":\"Organization\",\"email\":" + (email is null ? "null" : "\"" + email + "\"") + "}";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
        }
    }

    [Fact]
    public async Task TheContactAddressIsLookedUpOnceADay()
    {
        Assert.Equal("travelrepo-org", SupportContact.Owner("https://github.com/travelrepo-org/jourfold"));
        Assert.Equal("travelrepo-org", SupportContact.Owner());
        Assert.Null(SupportContact.Owner("https://gitlab.example/someone/jourfold"));

        using var store = new LocalStore(Path.Combine(root, "local"));
        var github = new GitHub("support@example.org"); using var http = new HttpClient(github);
        Assert.Equal("support@example.org", await SupportContact.EmailAsync(store, http, "travelrepo-org"));
        Assert.Equal("support@example.org", await SupportContact.EmailAsync(store, http, "travelrepo-org"));
        Assert.Equal(["https://api.github.com/users/travelrepo-org"], github.Urls.ToArray());

        using var empty = new LocalStore(Path.Combine(root, "empty"));
        Assert.Null(await SupportContact.EmailAsync(empty, new HttpClient(new GitHub(null)), "travelrepo-org"));
        Assert.Null(await SupportContact.EmailAsync(empty, new HttpClient(new GitHub("not an address")), "travelrepo-org"));
    }

    [Fact]
    public void LongReportsGoThroughTheClipboard()
    {
        var (link, complete) = SupportContact.MailTo("support@example.org", "Jourfold 1.0: problem report", "What happened:\nIt broke & stopped", "paste here");
        Assert.True(complete);
        Assert.Equal("mailto:support@example.org?subject=Jourfold%201.0%3A%20problem%20report&body=What%20happened%3A%0D%0AIt%20broke%20%26%20stopped", link);
        (link, complete) = SupportContact.MailTo("support@example.org", "Report", new string('x', 5000), "paste here");
        Assert.False(complete); Assert.EndsWith("body=paste%20here", link);
    }

    [AvaloniaFact]
    public async Task AReportIsReviewedAndOpenedInTheMailProgram()
    {
        using var store = new LocalStore(Path.Combine(root, "local"));
        var interaction = new TestInteraction();
        using var m = new MainViewModel(store, new GitCliBackend(), new OsSecretStore(), interaction) { SupportHttp = new HttpClient(new GitHub("support@example.org")) };
        string? reviewed = null;
        interaction.Report = text => { reviewed = text; return new ReportReview("It crashed when I saved.", true); };
        await m.ReportAsync("Sync failed: remote rejected");
        Assert.Contains("Sync failed: remote rejected", reviewed); Assert.Contains("Jourfold " + AppVersion.Informational, reviewed);
        var link = Assert.Single(interaction.Opened);
        Assert.StartsWith("mailto:support@example.org?subject=Jourfold%20" + AppVersion.Version, link);
        Assert.Contains("It%20crashed%20when%20I%20saved.", link);
        Assert.Empty(interaction.Copied);

        // Copying sends nothing and opens nothing.
        interaction.Report = text => new ReportReview(text, false);
        await m.ReportAsync(); Assert.Single(interaction.Opened); Assert.Single(interaction.Copied);

        // Without a public contact address the report is copied instead.
        using var other = new LocalStore(Path.Combine(root, "other"));
        using var m2 = new MainViewModel(other, new GitCliBackend(), new OsSecretStore(), interaction) { SupportHttp = new HttpClient(new GitHub(null)) };
        interaction.Report = text => new ReportReview("text", true);
        await m2.ReportAsync(); Assert.Single(interaction.Opened); Assert.Equal(2, interaction.Copied.Count);
        Assert.Contains(m2.Strings["ReportNoContact"], interaction.Toasts);
    }
}
