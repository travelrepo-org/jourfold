using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.Input;
using TravelRepo.Core;

namespace Jourfold.Desktop;

/// <summary>
/// "Me" in the open trip: which person the local user is. It is remembered on this computer only, per trip folder,
/// and otherwise suggested by TravelRepo's <see cref="TripPeople.Match"/> from the GitHub login and Git e-mail. It
/// drives "mine" views and is not a security feature: anyone who can edit the trip can edit everything.
/// </summary>
public partial class MainViewModel
{
    private const string NotInTrip = "none";
    private string? MeKey => Workspace is null ? null : "me:" + Workspace.Repository.Root;
    private string? MeChoice => MeKey is { } key ? Store.Get(key) : null;

    /// <summary>The trip-local person that represents the local user, if known.</summary>
    public Entity? Me
    {
        get
        {
            if (Trip is not { } trip) return null;
            var choice = MeChoice;
            if (choice == NotInTrip) return null;
            if (Guid.TryParse(choice, out var id) && trip.Find(id) is { Type: "person" } chosen) return chosen;
            if (TripPeople.Match(trip, GitHubLogin, Store.Get("identity.email"))?.Person is { } matched) return matched;
            // In the sample trip, whoever explores it plays its organizer.
            return Jourfold.Application.SampleTrip.IsSample(trip) ? trip.Entities.Values.FirstOrDefault(e => e.Type == "person" && e.Data["roles"] is JsonArray roles && roles.Any(r => r?.ToString() == "organizer")) : null;
        }
    }

    /// <summary>Whether to ask who the local user is: the trip has people, none matches, and nobody answered yet.</summary>
    public bool WhoAmIOpen => Trip is { } trip && Me is null && MeChoice is null && trip.Entities.Values.Any(e => e.Type == "person");

    private Reminder? WhoAmIReminder() => WhoAmIOpen ? new("whoami:" + Workspace!.Repository.Root, ReminderKind.WhoAmI, null, Strings["WhoAmITitle"], null, false, []) : null;

    /// <summary>Asks which person of the trip the local user is, optionally starting with <paramref name="suggested"/>.</summary>
    [RelayCommand]
    private Task ChooseMe(Entity? suggested) => RunAsync(async () =>
    {
        if (Workspace is null) return;
        var trip = Workspace.State.Trip; var login = GitHubLogin; var email = Store.Get("identity.email");
        var people = trip.Entities.Values.Where(e => e.Type == "person").OrderBy(e => e.Title, StringComparer.CurrentCulture).ToArray();
        var answer = await Interaction.WhoAmIAsync(new(people, suggested?.Id ?? Me?.Id, p => TripPeople.HasOtherIdentity(p, login, email), login is not null || email is not null));
        if (answer is null) return;
        if (answer.Choice == WhoAmIAnswer.AddMe) { await AddIdentityCommand.ExecuteAsync(null); return; }
        if (answer.Choice == WhoAmIAnswer.NotInTrip) { Store.Set(MeKey!, NotInTrip); AfterMeChanged(); return; }
        if (!Guid.TryParse(answer.Choice, out var id) || Workspace.State.Trip.Find(id) is not { Type: "person" } person) return;
        Store.Set(MeKey!, person.Id.ToString());
        if (answer.SaveIdentity && (login is not null || email is not null))
        {
            var updated = TripPeople.WithIdentity(person, login, Store.Get("identity.name") ?? person.Title, email);
            if (updated.Data.ToJsonString() != person.Data.ToJsonString()) await Workspace.EditAsync(updated);
        }
        AfterMeChanged();
        Interaction.Toast(string.Format(Strings["YouAreNow"], person.Title));
    });

    private void AfterMeChanged() { OnPropertyChanged(nameof(Me)); _ = UpdateRemindersAsync(); ContentChanged?.Invoke(this, EventArgs.Empty); }

    /// <summary>Remembers that the local user is this person, after they added themselves to the trip.</summary>
    private void RememberMe(Entity person) { if (MeKey is { } key) Store.Set(key, person.Id.ToString()); AfterMeChanged(); }
}

/// <summary>What the "Who are you in this trip?" question shows.</summary>
/// <param name="People">The trip's people.</param>
/// <param name="Suggested">The person to preselect.</param>
/// <param name="HasOtherIdentity">Whether a person already carries someone else's e-mail or GitHub login.</param>
/// <param name="CanSaveIdentity">Whether there is an e-mail or GitHub login to save on the chosen person.</param>
public sealed record WhoAmIRequest(IReadOnlyList<Entity> People, Guid? Suggested, Func<Entity, bool> HasOtherIdentity, bool CanSaveIdentity);

/// <summary>The answer: a person ID, <see cref="AddMe"/> or <see cref="NotInTrip"/>, and whether to save the identity on the person.</summary>
public sealed record WhoAmIAnswer(string Choice, bool SaveIdentity)
{
    public const string AddMe = "add", NotInTrip = "none";
}
