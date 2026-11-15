using TravelRepo.Core;

namespace Jourfold.UI;

/// <summary>How a person's circle looks: the name, the letters in it and its colour.</summary>
public sealed record PersonLook(string Name, string Initials, string Color);

/// <summary>
/// The circles of a trip's people, the same on the computer, phones and tablets. Each person keeps a colour of their
/// own within the trip: colours are handed out in the order people joined the trip (their IDs are made in time order),
/// so a new person never changes anyone else's, renaming keeps it, and two people with the same name still differ. Up
/// to <see cref="Palette"/>'s length people get different colours. Letters are the initials; where two people would
/// share them, a later letter of the name tells them apart, and the same name twice is numbered.
/// </summary>
public static class PersonLooks
{
    /// <summary>
    /// Fifteen colours that white letters read well on (a contrast of at least 4.5 to 1), in an order where neighbours
    /// differ most, so a small group gets clearly different ones. None is yellow, amber or orange: that colour belongs
    /// to warnings alone (see the Warning tokens).
    /// </summary>
    public static readonly IReadOnlyList<string> Palette =
    [
        "#1D4ED8", "#B91C1C", "#15803D", "#7E22CE", "#0F766E", "#BE185D", "#4338CA", "#4D7C0F",
        "#0E7490", "#A21CAF", "#047857", "#475569", "#0369A1", "#5B21B6", "#7F1D1D",
    ];

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<TripSnapshot, IReadOnlyDictionary<Guid, PersonLook>> Cache = new();

    /// <summary>The look of every person of <paramref name="trip"/>.</summary>
    public static IReadOnlyDictionary<Guid, PersonLook> For(TripSnapshot trip) => Cache.GetValue(trip, Make);

    /// <summary>The look of one person of the trip; one not in it looks as its name alone would.</summary>
    public static PersonLook Of(TripSnapshot trip, Guid person) => For(trip).TryGetValue(person, out var look) ? look : ForName(trip.Find(person)?.Title ?? "?");

    public static PersonLook Of(TripSnapshot trip, Entity person) => Of(trip, person.Id);

    /// <summary>
    /// The look of someone who is no person of a trip, such as the author of a version: the colour comes from the
    /// letters of the name, the same everywhere.
    /// </summary>
    public static PersonLook ForName(string name)
    {
        var hash = 0; foreach (var ch in name) hash = unchecked(hash * 31 + ch);
        return new(name, Initials(name), Palette[Math.Abs(hash % Palette.Count)]);
    }

    /// <summary>"Lena Weber" is LW, "Max" is MA.</summary>
    public static string Initials(string name)
    {
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) return "?";
        return parts.Length == 1 ? parts[0][..Math.Min(2, parts[0].Length)].ToUpperInvariant() : (parts[0][..1] + parts[^1][..1]).ToUpperInvariant();
    }

    private static IReadOnlyDictionary<Guid, PersonLook> Make(TripSnapshot trip)
    {
        var people = trip.Entities.Values.Where(e => e.Type == "person").OrderBy(e => e.Id).ToArray();
        var letters = people.ToDictionary(p => p.Id, p => Initials(p.Title));
        // Shared initials: the first keeps them; the next takes the first letter and a later one of its name that is
        // still free ("Max" MA, "Mara" MR), and the same name again is numbered ("Max" MA, "Max" M2).
        foreach (var group in people.GroupBy(p => letters[p.Id]).Where(g => g.Count() > 1))
        {
            var used = letters.Where(l => group.All(p => p.Id != l.Key)).Select(l => l.Value).ToHashSet();
            var seen = new List<string>();
            foreach (var person in group)
            {
                var plain = person.Title.Replace(" ", "", StringComparison.Ordinal).ToUpperInvariant() is { Length: > 0 } letters0 ? letters0 : "?";
                var number = seen.Count(n => string.Equals(n, person.Title, StringComparison.CurrentCultureIgnoreCase)) + 1;
                var choice = seen.Count == 0 ? letters[person.Id]
                    : number > 1 ? plain[..1] + number
                    : Enumerable.Range(1, Math.Max(0, plain.Length - 1)).Select(j => plain[..1] + plain[j]).FirstOrDefault(c => !used.Contains(c)) ?? plain[..1] + (seen.Count + 1);
                letters[person.Id] = choice; used.Add(choice); seen.Add(person.Title);
            }
        }
        return people.Select((p, i) => (p, i)).ToDictionary(x => x.p.Id, x => new PersonLook(x.p.Title, letters[x.p.Id], Palette[x.i % Palette.Count]));
    }
}
