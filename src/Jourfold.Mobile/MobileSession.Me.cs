using Jourfold.Application;
using TravelRepo.Core;

namespace Jourfold.Mobile;

public sealed partial class MobileSession
{
    private string? MeKey => Workspace is null ? null : MeSetting(Workspace.Repository.Root);

    /// <summary>Where the answer to "who am I in this trip" is kept, per trip folder.</summary>
    internal static string MeSetting(string tripFolder) => "me:" + Path.GetFileName(tripFolder);

    /// <summary>The person the user is in the open trip: chosen on this device, or found by GitHub login and e-mail.</summary>
    public Entity? Me => Trip is { } trip ? TripMe.Resolve(trip, MeKey is { } key ? Settings.Values.GetValueOrDefault(key) : null, GitHubLogin, Settings.Email) : null;

    /// <summary>Whether to ask who the user is: none of the trip's people is the user, and nobody answered yet.</summary>
    public bool AskWhoAmI => Trip is { } trip && Me is null && MeKey is { } key && !Settings.Values.ContainsKey(key) && !SampleTrip.IsSample(trip);

    /// <summary>Whether the trip asks "Who are you in this trip?" on its own as it opens: the first time on this device.</summary>
    public bool WelcomeDue => Trip is { } trip && MeKey is { } key && Workspace is { } workspace
        && TripWelcome.Due(trip, Me, Settings.Values.GetValueOrDefault(key), Settings.Values.ContainsKey(TripWelcome.AskedKey(workspace.Repository.Root)));

    /// <summary>Asks who the user is in the open trip. It counts as asked, so it does not come again on its own.</summary>
    public void ShowWelcome()
    {
        if (Workspace is not { } workspace) return;
        Settings.Values[TripWelcome.AskedKey(workspace.Repository.Root)] = "asked"; Settings.Save();
        Showing = Overlay.Welcome; OverlayItem = null; Changed?.Invoke();
    }

    /// <summary>The email to offer: the settings', the GitHub account's public one, or GitHub's private address.</summary>
    public string? SuggestedEmail => CommitIdentity.Suggest(Settings.Email, null, GitHubAccount);

    /// <summary>GitHub's private address for commits of the signed-in account, to choose instead of a public one.</summary>
    public string? PrivateGitHubEmail => GitHubAccount is { } account ? TravelRepo.Providers.GitHub.GitHubProvider.NoReplyEmail(account.Login, account.Id) : null;

    /// <summary>Whether to offer keeping the email for other trips too: there is none in the settings yet.</summary>
    public bool OfferRememberEmail => string.IsNullOrWhiteSpace(Settings.Email);

    private (string Login, long? Id, string? PublicEmail)? GitHubAccount =>
        Settings.GitHubLogin is { Length: > 0 } login ? (login, Settings.GitHubId, Settings.GitHubEmail) : null;

    /// <summary>
    /// The answer to "Who are you in this trip?": a person of the trip, or a new one named <paramref name="newName"/>,
    /// or nobody when the user is not part of the trip. The email and the GitHub login go on that person, so versions
    /// are saved under that email on every device; <paramref name="remember"/> keeps the email for other trips too.
    /// </summary>
    public Task AnswerWelcomeAsync(Guid? person, string? newName, string? email, bool remember, CancellationToken ct = default) => ChangeAsync(async () =>
    {
        if (Workspace is not { } workspace || MeKey is not { } key) return;
        email = CommitIdentity.LooksLikeEmail(email) ? email!.Trim() : null;
        var login = GitHubAccount?.Login;
        if (newName is { Length: > 0 })
        {
            var (added, edits) = TripWelcome.AddMe(workspace.State.Trip, newName, login, email);
            await workspace.ApplyAsync([.. edits], ct); NoteChange();
            person = added.Id;
        }
        else if (person is { } id && workspace.State.Trip.Find(id) is { Type: "person" } chosen)
        {
            var updated = TripWelcome.WithMe(chosen, login, Settings.Name ?? chosen.Title, email);
            if (updated.Data.ToJsonString() != chosen.Data.ToJsonString()) { await workspace.EditAsync(updated, ct); NoteChange(); }
        }
        Settings.Values[key] = person?.ToString() ?? TripMe.NotInTrip;
        if (remember && email is not null && OfferRememberEmail) Settings.Email = email;
        Settings.Save();
        await ApplyIdentityAsync(ct);
        CloseOverlay();
        UpdateCalendarSoon();
    }, ct);

    /// <summary>
    /// Remembers which person the user is, or that they are not part of the trip (null). The user's GitHub login and
    /// e-mail are saved on that person when it has no other, so their other devices recognise them.
    /// </summary>
    public Task SetMeAsync(Guid? person, CancellationToken ct = default) => ChangeAsync(async () =>
    {
        if (Workspace is not { } workspace || MeKey is not { } key) return;
        Settings.Values[key] = person?.ToString() ?? TripMe.NotInTrip; Settings.Save();
        if (person is { } id && workspace.State.Trip.Find(id) is { Type: "person" } chosen && (GitHubLogin is not null || Settings.Email is not null)
            && !TripPeople.HasOtherIdentity(chosen, GitHubLogin, Settings.Email))
        {
            var updated = TripPeople.WithIdentity(chosen, GitHubLogin, Settings.Name ?? chosen.Title, Settings.Email);
            if (updated.Data.ToJsonString() != chosen.Data.ToJsonString()) { await workspace.EditAsync(updated, ct); NoteChange(); }
        }
        // Versions are saved under the address the trip knows for that person, when it knows one.
        await ApplyIdentityAsync(ct);
        Changed?.Invoke();
        UpdateCalendarSoon();
    }, ct);
}
