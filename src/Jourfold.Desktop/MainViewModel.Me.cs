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
    private const string NotInTrip = Jourfold.Application.TripMe.NotInTrip;
    private string? MeKey => Workspace is null ? null : "me:" + Workspace.Repository.Root;
    private string? MeChoice => MeKey is { } key ? Store.Get(key) : null;

    /// <summary>The trip-local person that represents the local user, if known.</summary>
    public Entity? Me
    {
        get
        {
            if (Trip is not { } trip) return null;
            return Jourfold.Application.TripMe.Resolve(trip, MeChoice, GitHubLogin, Store.Get("identity.email"));
        }
    }

    /// <summary>Whether to ask who the local user is: the trip has people, none matches, and nobody answered yet.</summary>
    public bool WhoAmIOpen => Trip is { } trip && Me is null && MeChoice is null && trip.Entities.Values.Any(e => e.Type == "person");

    private Reminder? WhoAmIReminder() => WhoAmIOpen ? new("whoami:" + Workspace!.Repository.Root, ReminderKind.WhoAmI, null, Strings["WhoAmITitle"], null, false, []) : null;

    /// <summary>The email set under Settings → You, unless it is the placeholder made from a name.</summary>
    private string? OwnEmail => Store.Get("identity.email") is { Length: > 0 } email && !Jourfold.Application.CommitIdentity.IsPlaceholder(email) ? email : null;

    private (string Login, long? Id, string? PublicEmail)? GitHubIdentity => GitHubLogin is { } login ? (login, GitHubAccount?.Id, GitHubAccount?.PublicEmail) : null;

    /// <summary>Whether the trip asks "Who are you in this trip?" on its own as it opens: the first time on this computer.</summary>
    public bool WelcomeDue => Trip is { } trip && Workspace is { } workspace
        && Jourfold.Application.TripWelcome.Due(trip, Me, MeChoice, Store.Get(Jourfold.Application.TripWelcome.AskedKey(workspace.Repository.Root)) is not null);

    /// <summary>
    /// Whether a trip asks who the user is on its own as it opens. Headless tests turn it off: nobody answers the
    /// dialog there, and every later action would wait for it.
    /// </summary>
    public static bool WelcomeOnOpen { get; set; } = true;

    /// <summary>Asks who the user is once, as a trip opens on this computer for the first time.</summary>
    private async Task WelcomeAsync()
    {
        if (!WelcomeOnOpen || !WelcomeDue) return;
        Store.Set(Jourfold.Application.TripWelcome.AskedKey(Workspace!.Repository.Root), "asked");
        await ChooseMeCommand.ExecuteAsync(null);
    }

    /// <summary>
    /// Asks which person of the trip the local user is, optionally starting with <paramref name="suggested"/>. When the
    /// trip has no email for that person, or the user adds themselves, the answer brings one, and versions are saved
    /// under it on every device (<see cref="Jourfold.Application.CommitIdentity"/>).
    /// </summary>
    [RelayCommand]
    private Task ChooseMe(Entity? suggested) => RunAsync(async () =>
    {
        if (Workspace is null) return;
        var trip = Workspace.State.Trip; var login = GitHubLogin; var email = OwnEmail;
        var people = trip.Entities.Values.Where(e => e.Type == "person").OrderBy(e => e.Title, StringComparer.CurrentCulture).ToArray();
        var gitConfig = await GitConfigAsync();
        var account = GitHubIdentity;
        var answer = await Interaction.WhoAmIAsync(new(people, suggested?.Id ?? Me?.Id, p => TripPeople.HasOtherIdentity(p, login, email), login is not null || email is not null)
        {
            NeedsEmail = Jourfold.Application.TripWelcome.NeedsEmail,
            SuggestedEmail = Jourfold.Application.CommitIdentity.Suggest(email, gitConfig?.Email, account),
            PrivateGitHubEmail = account is { } a ? TravelRepo.Providers.GitHub.GitHubProvider.NoReplyEmail(a.Login, a.Id) : null,
            OfferRememberEmail = email is null,
            Name = Store.Get("identity.name") ?? gitConfig?.Name ?? GitHubAccount?.Name ?? login
        });
        if (answer is null || Workspace is null) return;
        var given = Jourfold.Application.CommitIdentity.LooksLikeEmail(answer.Email) ? answer.Email!.Trim() : null;
        if (answer.RememberEmail && given is not null && OwnEmail is null) Store.Set("identity.email", given);
        if (answer.Choice == WhoAmIAnswer.AddMe)
        {
            if (answer.Name is not { Length: > 0 } name) { await AddIdentityCommand.ExecuteAsync(null); return; }
            var (added, edits) = Jourfold.Application.TripWelcome.AddMe(Workspace.State.Trip, name, login, given);
            await Workspace.ApplyAsync([.. edits]); Selected = Workspace.State.Trip.Find(added.Id);
            if (Store.Get("identity.name") is null) Store.Set("identity.name", name.Trim());
            RememberMe(added);
            Interaction.Toast(string.Format(Strings["YouAreNow"], added.Title));
            return;
        }
        if (answer.Choice == WhoAmIAnswer.NotInTrip) { Store.Set(MeKey!, NotInTrip); AfterMeChanged(); return; }
        if (!Guid.TryParse(answer.Choice, out var id) || Workspace.State.Trip.Find(id) is not { Type: "person" } person) return;
        Store.Set(MeKey!, person.Id.ToString());
        if (given is not null && Jourfold.Application.TripWelcome.NeedsEmail(person))
        {
            var updated = Jourfold.Application.TripWelcome.WithMe(person, login, Store.Get("identity.name") ?? person.Title, given);
            if (updated.Data.ToJsonString() != person.Data.ToJsonString()) await Workspace.EditAsync(updated);
        }
        else if (answer.SaveIdentity && (login is not null || email is not null))
        {
            var updated = TripPeople.WithIdentity(person, login, Store.Get("identity.name") ?? person.Title, email);
            if (updated.Data.ToJsonString() != person.Data.ToJsonString()) await Workspace.EditAsync(updated);
        }
        AfterMeChanged();
        Interaction.Toast(string.Format(Strings["YouAreNow"], person.Title));
    });

    private void AfterMeChanged() { OnPropertyChanged(nameof(Me)); _ = UpdateRemindersAsync(); _ = ApplyIdentityAsync(); ContentChanged?.Invoke(this, EventArgs.Empty); }

    /// <summary>The name and email in the user's own Git settings, read and never changed.</summary>
    private async Task<(string? Name, string? Email)?> GitConfigAsync()
    {
        if (Workspace is not { } workspace) return null;
        async Task<string?> Read(string key)
        {
            try
            {
                var result = await backend.ExecuteAsync(workspace.Repository.Root, ["config", "--global", "--get", key]);
                return result.ExitCode == 0 && result.Output.Trim() is { Length: > 0 } value ? value : null;
            }
            catch (Exception ex) when (ex is DomainException or IOException or InvalidOperationException) { return null; }
        }
        var (name, email) = (await Read("user.name"), await Read("user.email"));
        return name is null && email is null ? null : (name, email);
    }

    /// <summary>
    /// Saves the open trip's next versions under <see cref="Jourfold.Application.CommitIdentity.Resolve"/>: the trip's
    /// person, Settings → You, the user's Git settings, then the GitHub account. Only the trip's own Git settings change.
    /// </summary>
    public async Task ApplyIdentityAsync()
    {
        if (Workspace is not { } workspace) return;
        try
        {
            var identity = Jourfold.Application.CommitIdentity.Resolve(Me, Store.Get("identity.name"), OwnEmail, GitHubIdentity, GitHubAccount?.Name, await GitConfigAsync());
            if (identity is ({ } name, { } email) && Workspace == workspace) await workspace.Git.SetIdentityAsync(name, email);
        }
        catch (Exception ex) when (ex is DomainException or IOException) { }
    }

    /// <summary>Remembers that the local user is this person, after they added themselves to the trip.</summary>
    private void RememberMe(Entity person) { if (MeKey is { } key) Store.Set(key, person.Id.ToString()); AfterMeChanged(); }
}

/// <summary>What the "Who are you in this trip?" question shows.</summary>
/// <param name="People">The trip's people.</param>
/// <param name="Suggested">The person to preselect.</param>
/// <param name="HasOtherIdentity">Whether a person already carries someone else's e-mail or GitHub login.</param>
/// <param name="CanSaveIdentity">Whether there is an e-mail or GitHub login to save on the chosen person.</param>
public sealed record WhoAmIRequest(IReadOnlyList<Entity> People, Guid? Suggested, Func<Entity, bool> HasOtherIdentity, bool CanSaveIdentity)
{
    /// <summary>Whether the trip has no email for a person yet, so choosing them asks for one.</summary>
    public Func<Entity, bool> NeedsEmail { get; init; } = _ => false;
    /// <summary>The email to fill in: Settings → You, the user's Git settings, or the GitHub account's.</summary>
    public string? SuggestedEmail { get; init; }
    /// <summary>GitHub's private address for commits of the signed-in account, to choose instead.</summary>
    public string? PrivateGitHubEmail { get; init; }
    /// <summary>Whether to offer keeping the email for other trips: Settings → You has none yet.</summary>
    public bool OfferRememberEmail { get; init; }
    /// <summary>The name to fill in when the user adds themselves.</summary>
    public string? Name { get; init; }
}

/// <summary>The answer: a person ID, <see cref="AddMe"/> or <see cref="NotInTrip"/>, and whether to save the identity on the person.</summary>
/// <param name="Email">The email given for the person, when the trip had none for them.</param>
/// <param name="RememberEmail">Whether to keep that email under Settings → You for other trips.</param>
/// <param name="Name">The name when the user adds themselves.</param>
public sealed record WhoAmIAnswer(string Choice, bool SaveIdentity, string? Email = null, bool RememberEmail = false, string? Name = null)
{
    public const string AddMe = "add", NotInTrip = "none";
}
