using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Jourfold.Application;
using Jourfold.Desktop;
using Jourfold.Infrastructure;
using Jourfold.Mobile;
using NodaTime;
using TravelRepo.Core;
using TravelRepo.Git;
using TravelRepo.Repository;
using Xunit;
namespace Jourfold.Tests;

/// <summary>"Who are you in this trip?", asked the first time a trip opens on a device, with the email versions use.</summary>
public sealed class TripWelcomeTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "jourfold-welcome-" + Guid.NewGuid());
    public void Dispose() => TestFiles.Delete(root);
    private static readonly DateTimeZone Tokyo = DateTimeZoneProviders.Tzdb["Asia/Tokyo"];

    /// <summary>A shared trip as it comes from someone else: Alex and Sam, nobody with an email yet.</summary>
    private async Task<string> Trip(string folder, params string[] people)
    {
        var repository = new TravelRepository(Path.Combine(root, folder)); await repository.InitializeAsync(Entity.CreateTrip("Lake weekend", "en", "Europe/Berlin"));
        var state = await repository.ReadAsync();
        var manifest = state.Trip.Manifest.Copy(); var participants = new JsonArray(); manifest.Data["participants"] = participants;
        var edits = new List<EntityEdit>();
        foreach (var name in people) { var p = Entity.Create("person", name); participants.Add(p.Id.ToString()); edits.Add(new(p.Id, p)); }
        edits.Add(new(manifest.Id, manifest));
        await repository.ApplyAsync(state, edits);
        await new GitRepository(repository, new GitCliBackend()).InitializeAsync("Someone else", "someone@example.invalid");
        return repository.Root;
    }

    private MobileSession Phone(string settings)
    {
        var folder = Path.Combine(root, "phone"); Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "settings.json"), settings);
        return new MobileSession(new MobileHost(folder, new GitCliBackend(), SystemClock.Instance, () => Tokyo, "en", new MemorySecrets()));
    }

    private static async Task<string> GitEmail(string path) => (await new GitCliBackend().ExecuteAsync(path, ["config", "--local", "user.email"])).Output.Trim();

    [AvaloniaFact]
    public async Task APhoneAsksOnceAndSavesTheEmailOnThePersonAndForOtherTrips()
    {
        var path = await Trip("lake", "Alex", "Sam");
        var session = Phone("""{ "GitHubLogin": "sam", "GitHubId": 42, "Name": "Sam" }""");
        var window = new Window { Width = 412, Height = 915, Content = new AppShell(session) }; window.Show();
        try
        {
            await session.LoadTripsAsync();
            await session.OpenAsync(path);
            Assert.Equal(Overlay.Welcome, session.Showing);
            Probe.Layout();
            Assert.Contains("Who are you in this trip?", window.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text));
            // Without an email of one's own, GitHub's private address is offered, and keeping it for other trips is.
            Assert.Equal("42+sam@users.noreply.github.com", session.SuggestedEmail);
            Assert.True(session.OfferRememberEmail);

            var sam = session.Trip!.Entities.Values.Single(e => e.Title == "Sam");
            await session.AnswerWelcomeAsync(sam.Id, null, "sam@example.invalid", remember: true);
            Assert.Equal(Overlay.None, session.Showing);
            Assert.Equal(sam.Id, session.Me?.Id);
            Assert.Equal(["sam@example.invalid"], TripPeople.Emails(session.Trip!.Find(sam.Id)!));
            Assert.Equal(["sam"], TripPeople.GitHubLogins(session.Trip!.Find(sam.Id)!));
            Assert.Equal("sam@example.invalid", session.Settings.Email);
            Assert.Equal("sam@example.invalid", await GitEmail(path));

            // It asked once: opening the trip again does not ask.
            session.Close(); await session.OpenAsync(path);
            Assert.Equal(Overlay.None, session.Showing);
        }
        finally { window.Close(); session.Dispose(); }
    }

    [AvaloniaFact]
    public async Task OnAPhoneSomeoneNotInTheTripAddsThemselvesOrSaysLater()
    {
        var empty = await Trip("empty");
        var session = Phone("""{ "Name": "Kim", "Email": "kim@example.invalid" }""");
        try
        {
            await session.OpenAsync(empty);
            // A trip without people asks too, to add oneself.
            Assert.Equal(Overlay.Welcome, session.Showing);
            Assert.False(session.OfferRememberEmail);
            await session.AnswerWelcomeAsync(null, "Kim", "kim@trip.invalid", remember: true);
            var kim = Assert.Single(session.Trip!.Entities.Values, e => e.Type == "person");
            Assert.Equal("Kim", kim.Title); Assert.Equal(kim.Id, session.Me?.Id);
            Assert.Contains(kim.Id.ToString(), session.Trip!.Manifest.Data["participants"]!.AsArray().Select(p => p?.ToString()));
            Assert.Equal(["kim@trip.invalid"], TripPeople.Emails(kim));
            // The settings already had an email, so they keep it.
            Assert.Equal("kim@example.invalid", session.Settings.Email);
            // The trip's address for the person wins over the settings'.
            Assert.Equal("kim@trip.invalid", await GitEmail(empty));

            // "Not now" closes it; the card on Today stays.
            var other = await Trip("other", "Alex");
            await session.OpenAsync(other);
            Assert.Equal(Overlay.Welcome, session.Showing);
            session.CloseOverlay();
            Assert.Equal(Overlay.None, session.Showing); Assert.True(session.AskWhoAmI);

            // The sample trip never asks.
            await session.OpenAsync(await session.CreateSampleAsync(new LocalDate(2027, 5, 14)));
            Assert.Equal(Overlay.None, session.Showing);
        }
        finally { session.Dispose(); }
    }

    [AvaloniaFact]
    public async Task TheComputerAsksOnceAndSavesTheEmailOnThePerson()
    {
        var path = await Trip("lake", "Alex", "Sam");
        using var store = new LocalStore(Path.Combine(root, "local")); var interaction = new TestInteraction();
        // The name made into a placeholder address when a trip was created does not count as an email.
        store.Set("identity.name", "Sam"); store.Set("identity.email", "sam@localhost");
        using var m = new MainViewModel(store, new GitCliBackend(), new OsSecretStore(), interaction);
        WhoAmIRequest? asked = null;
        interaction.WhoAmI = request =>
        {
            asked = request;
            var sam = request.People.Single(p => p.Title == "Sam");
            Assert.True(request.NeedsEmail(sam)); Assert.True(request.OfferRememberEmail);
            Assert.NotEqual("sam@localhost", request.SuggestedEmail);
            return new WhoAmIAnswer(sam.Id.ToString(), false, "sam@example.invalid", RememberEmail: true);
        };
        MainViewModel.WelcomeOnOpen = true;
        try
        {
            await m.OpenAsync(path);
            await Probe.Until(() => m.Me is { } me && TripPeople.Emails(me).Count > 0);
            Assert.Equal("Sam", m.Me!.Title);
            Assert.Equal(["sam@example.invalid"], TripPeople.Emails(m.Me));
            Assert.Equal("sam@example.invalid", store.Get("identity.email"));
            for (var tries = 0; tries < 100 && await GitEmail(path) != "sam@example.invalid"; tries++) { await Task.Delay(20); Probe.Layout(); }
            Assert.Equal("sam@example.invalid", await GitEmail(path));

            // Asked once on this computer.
            m.CloseTrip(); store.Set("me:" + path, ""); interaction.WhoAmIRequests.Clear();
            await m.OpenAsync(path); Probe.Layout();
            Assert.Empty(interaction.WhoAmIRequests);
        }
        finally { MainViewModel.WelcomeOnOpen = false; }
        Assert.NotNull(asked);
    }
}
