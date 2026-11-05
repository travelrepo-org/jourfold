using System.Text.Json.Nodes;
using TravelRepo.Core;

namespace Jourfold.Application;

/// <summary>
/// "Me" in a trip: which person the local user is. The answer is kept on the device per trip; without one, TravelRepo
/// suggests a person by GitHub login and Git e-mail. It decides what is "mine", such as which plans remind the user,
/// and is not a security feature.
/// </summary>
public static class TripMe
{
    /// <summary>The stored answer for "I am not part of this trip".</summary>
    public const string NotInTrip = "none";

    /// <summary>The person the user is, from their stored <paramref name="choice"/> or, without one, their identity.</summary>
    public static Entity? Resolve(TripSnapshot trip, string? choice, string? githubLogin, string? gitEmail)
    {
        if (choice == NotInTrip) return null;
        if (Guid.TryParse(choice, out var id) && trip.Find(id) is { Type: "person" } chosen) return chosen;
        if (TripPeople.Match(trip, githubLogin, gitEmail)?.Person is { } matched) return matched;
        // In the sample trip, whoever explores it plays its organizer.
        return SampleTrip.IsSample(trip) ? trip.Entities.Values.FirstOrDefault(e => e.Type == "person" && e.Data["roles"] is JsonArray roles && roles.Any(r => r?.ToString() == "organizer")) : null;
    }

    /// <summary>
    /// Whether a plan concerns <paramref name="me"/>: it names them, or it names nobody. Without a known "me", every plan
    /// does. A stay counts its guests too.
    /// </summary>
    public static bool Concerns(TripSnapshot trip, Entity item, Entity? me)
    {
        if (me is null) return true;
        var people = ScheduleQueries.Participants(trip, item).Select(p => p.Id)
            .Concat((item.Data["components"]?["accommodation"]?["guests"] as JsonArray ?? []).Select(g => Guid.TryParse(g?.ToString(), out var guest) ? guest : Guid.Empty))
            .Where(g => g != Guid.Empty).ToArray();
        return people.Length == 0 || people.Contains(me.Id);
    }
}
