using System.Text.Json.Nodes;
using Avalonia.Headless.XUnit;
using Jourfold.Desktop;
using Jourfold.Infrastructure;
using TravelRepo.Core;
using TravelRepo.Git;
using TravelRepo.Repository;
using Xunit;
namespace Jourfold.Tests;

/// <summary>"Me" per trip: remembered on this computer, suggested from identities, asked when unknown.</summary>
public sealed class MeTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "jourfold-me-" + Guid.NewGuid());
    public void Dispose() => TestFiles.Delete(root);

    private async Task<string> Trip()
    {
        var repository = new TravelRepository(Path.Combine(root, "trip")); await repository.InitializeAsync(Entity.CreateTrip("Lake weekend", "en", "Europe/Berlin"));
        await new GitRepository(repository, new GitCliBackend()).InitializeAsync("Test", "test@example.invalid");
        return repository.Root;
    }

    [AvaloniaFact]
    public async Task SomeoneWhoJoinsLaterClaimsThePersonCreatedForThem()
    {
        using var store = new LocalStore(Path.Combine(root, "local")); var interaction = new TestInteraction();
        store.Set("identity.name", "Fred"); store.Set("identity.email", "fred@example.org");
        using var m = new MainViewModel(store, new GitCliBackend(), new OsSecretStore(), interaction);
        await m.OpenAsync(await Trip());
        var alex = Entity.Create("person", "Alex"); alex.Data["identities"] = new JsonObject { ["git"] = new JsonArray(new JsonObject { ["name"] = "Alex", ["email"] = "alex@example.org" }) };
        var fred = Entity.Create("person", "Fred");
        await m.Workspace!.ApplyAsync([new(alex.Id, alex), new(fred.Id, fred)]);
        await m.UpdateRemindersAsync();

        // Nobody matches, so the bell asks once.
        Assert.Null(m.Me); Assert.True(m.WhoAmIOpen);
        var reminder = Assert.Single(m.Reminders, r => r.Kind == ReminderKind.WhoAmI);

        // Choosing Fred and saving the identity: remembered here, and Fred now carries the e-mail for other devices.
        interaction.WhoAmI = request =>
        {
            Assert.False(request.HasOtherIdentity(request.People.Single(p => p.Title == "Fred")));
            Assert.True(request.HasOtherIdentity(request.People.Single(p => p.Title == "Alex")));
            return new WhoAmIAnswer(fred.Id.ToString(), true);
        };
        m.ShowReminder(reminder); await Probe.Until(() => TripPeople.Emails(m.Workspace.State.Trip.Find(fred.Id)!).Count > 0);
        Assert.Equal(fred.Id, m.Me!.Id); Assert.False(m.WhoAmIOpen);
        Assert.Equal(["fred@example.org"], TripPeople.Emails(m.Workspace.State.Trip.Find(fred.Id)!));
        await m.UpdateRemindersAsync(); Assert.DoesNotContain(m.Reminders, r => r.Kind == ReminderKind.WhoAmI);

        // Without the remembered choice, as on another computer, the saved e-mail recognises Fred.
        store.Set("me:" + m.Workspace.Repository.Root, "");
        Assert.Equal(fred.Id, m.Me?.Id);

        // "I'm not part of this trip" is remembered too, and nothing is asked any more.
        interaction.WhoAmI = _ => new WhoAmIAnswer(WhoAmIAnswer.NotInTrip, false);
        await m.ChooseMeCommand.ExecuteAsync(null);
        Assert.Null(m.Me); Assert.False(m.WhoAmIOpen);
        Assert.Equal("Lake weekend", m.TripTitle); // the choice did not change the trip
    }
}
