using System.Text.Json.Nodes;
using TravelRepo.Core;
using TravelRepo.Repository;

namespace Jourfold.Application;

/// <summary>
/// "Who are you in this trip?", asked the first time a trip opens on a device when the user is not known in it yet.
/// The answer decides what is "mine", and the email the trip has for that person is the one versions are saved under
/// on every device (<see cref="CommitIdentity"/>).
/// </summary>
public static class TripWelcome
{
    /// <summary>Where a device remembers that it asked, per trip folder, so the question comes once.</summary>
    public static string AskedKey(string tripFolder) => "welcome:" + Path.GetFileName(Path.TrimEndingDirectorySeparator(tripFolder));

    /// <summary>
    /// Whether to ask: not asked on this device before, nobody chose who the user is, and the user is not recognised
    /// by their GitHub login or email. The sample trip never asks; whoever explores it plays its organizer.
    /// </summary>
    public static bool Due(TripSnapshot trip, Entity? me, string? choice, bool asked) => !asked && choice is null && me is null && !SampleTrip.IsSample(trip);

    /// <summary>Whether the trip has no email for the person yet, so the question goes on to ask for one.</summary>
    public static bool NeedsEmail(Entity person) => TripPeople.Emails(person).Count == 0;

    /// <summary>
    /// The person with the user's GitHub login and email added, unless the person already carries someone else's; then
    /// it is left as it is, as when choosing "This is me".
    /// </summary>
    public static Entity WithMe(Entity person, string? gitHubLogin, string name, string? email) =>
        TripPeople.HasOtherIdentity(person, gitHubLogin, email) && !NeedsEmail(person) ? person : TripPeople.WithIdentity(person, gitHubLogin, name, email);

    /// <summary>The user as a new traveller, with their GitHub login and email, and the trip listing them.</summary>
    public static (Entity Person, IReadOnlyList<EntityEdit> Edits) AddMe(TripSnapshot trip, string name, string? gitHubLogin, string? email)
    {
        var person = TripPeople.WithIdentity(Entity.Create("person", name.Trim()), gitHubLogin, name.Trim(), email);
        person.Data["roles"] = new JsonArray("traveler");
        var manifest = trip.Manifest.Copy(); var participants = manifest.Data["participants"] as JsonArray ?? [];
        manifest.Data["participants"] = participants; participants.Add(person.Id.ToString());
        return (person, [new(person.Id, person), new(manifest.Id, manifest)]);
    }
}
