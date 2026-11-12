using System.Text.Json.Nodes;
using TravelRepo.Core;
using TravelRepo.Providers.GitHub;

namespace Jourfold.Application;

/// <summary>
/// The name and email the versions of a trip are saved under, Git's identity. Everyone who can open the trip sees
/// them, and other devices recognise the person by them.
/// </summary>
/// <remarks>
/// The email comes from, in this order:
/// <list type="number">
/// <item>the person the user is in the trip ("me"), so every device saves under the address the trip already knows;</item>
/// <item>the email set in the app's settings;</item>
/// <item>on a computer, the email in the user's own Git settings (<c>git config --global user.email</c>);</item>
/// <item>the email the GitHub account shows publicly;</item>
/// <item>GitHub's private address for commits, made from the account's number and login.</item>
/// </list>
/// Without any of them there is none, and saving a version fails until the user says who they are.
/// </remarks>
public static class CommitIdentity
{
    /// <param name="me">The person the user is in the trip, if known.</param>
    /// <param name="name">The name set in the app's settings.</param>
    /// <param name="email">The email set in the app's settings. The placeholder a computer makes from a name does not count.</param>
    /// <param name="gitHub">The signed-in GitHub account: its login, number and public email, as far as known.</param>
    /// <param name="gitHubName">The GitHub account's display name.</param>
    /// <param name="gitConfig">The name and email in the user's own Git settings, on a computer.</param>
    public static (string Name, string Email)? Resolve(Entity? me, string? name, string? email, (string Login, long? Id, string? PublicEmail)? gitHub, string? gitHubName = null, (string? Name, string? Email)? gitConfig = null)
    {
        name = Clean(name); email = Own(email);
        // A person may have several addresses, such as from two computers; the one set in the app wins among them.
        var mine = me is null ? [] : TripPeople.Emails(me);
        var fromTrip = mine.FirstOrDefault(e => string.Equals(e, email, StringComparison.OrdinalIgnoreCase)) ?? mine.FirstOrDefault();
        var chosen = fromTrip ?? Suggest(email, gitConfig?.Email, gitHub);
        if (chosen is null) return null;
        // The name stored with the trip's address, then the app's, the person's, Git's and the account's.
        var stored = fromTrip is null ? null : (me!.Data["identities"]?["git"] as JsonArray ?? [])
            .FirstOrDefault(n => string.Equals(n?["email"]?.ToString(), fromTrip, StringComparison.OrdinalIgnoreCase))?["name"]?.ToString();
        var who = Clean(stored) ?? name ?? Clean(me?.Title) ?? Clean(gitConfig?.Name) ?? Clean(gitHubName) ?? gitHub?.Login ?? chosen;
        return (who, chosen);
    }

    /// <summary>
    /// The email to offer when the trip does not know the user's yet, as in "Who are you in this trip?": the app's, the
    /// one in Git's settings, the GitHub account's public one, then GitHub's private address for commits.
    /// </summary>
    public static string? Suggest(string? email, string? gitConfigEmail, (string Login, long? Id, string? PublicEmail)? gitHub) =>
        Own(email) ?? Own(gitConfigEmail) ?? (gitHub is { } account ? Clean(account.PublicEmail) ?? GitHubProvider.NoReplyEmail(account.Login, account.Id) : null);

    /// <summary>
    /// Whether an email is a placeholder rather than an address: a computer makes <c>name@localhost</c> from the name
    /// when no email was given.
    /// </summary>
    public static bool IsPlaceholder(string? email) => email?.Trim().EndsWith("@localhost", StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>Whether text can be an email address: something, an at sign, a domain with a dot.</summary>
    public static bool LooksLikeEmail(string? text) => Clean(text) is { } t && t.IndexOf('@') is var at and > 0 && t.IndexOf('.', at) > at + 1 && !t.EndsWith('.') && !t.Any(char.IsWhiteSpace);

    private static string? Own(string? email) => IsPlaceholder(email) ? null : Clean(email);

    /// <summary>
    /// Whether Git refused a version because it knows no name and email, as the git program and libgit2 both say
    /// ("Author identity unknown").
    /// </summary>
    public static bool IsMissing(Exception exception) =>
        exception is TravelRepo.Git.GitCommandException git && git.Output.Contains("identity unknown", StringComparison.OrdinalIgnoreCase);

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
