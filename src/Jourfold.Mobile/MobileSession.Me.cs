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

    /// <summary>Whether to ask who the user is: the trip has people, none is the user, and nobody answered yet.</summary>
    public bool AskWhoAmI => Trip is { } trip && Me is null && MeKey is { } key && !Settings.Values.ContainsKey(key) && trip.Entities.Values.Any(e => e.Type == "person");

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
