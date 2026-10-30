using TravelRepo.Core;

namespace Jourfold.Application;

/// <summary>One line typed into a packing list, as the desktop and the phone read it.</summary>
public static class PackingInput
{
    /// <summary>
    /// "@name" assigns a person (by full or first name), "@all" (or "@alle") everyone, and "#group" sets the group,
    /// for example "Grill @Sam #Cooking"; the other words are the title.
    /// </summary>
    public static (string Title, string? Group, IReadOnlyList<Guid> People, bool Everyone) Parse(TripSnapshot trip, string text)
    {
        var people = new List<Guid>(); var everyone = false; string? group = null; var words = new List<string>();
        var persons = trip.Entities.Values.Where(e => e.Type == "person").ToArray();
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (word.Length > 1 && word[0] == '#') { group = word[1..].Replace('_', ' '); continue; }
            if (word.Length > 1 && word[0] == '@')
            {
                var name = word[1..];
                if (name.ToLowerInvariant() is "all" or "alle" or "everyone") { everyone = true; continue; }
                var matches = persons.Where(p => p.Title.Equals(name, StringComparison.CurrentCultureIgnoreCase) || p.Title.Split(' ')[0].Equals(name, StringComparison.CurrentCultureIgnoreCase)).ToArray();
                if (matches.Length == 1) { people.Add(matches[0].Id); continue; }
            }
            words.Add(word);
        }
        return (string.Join(' ', words).Trim(), group, people, everyone);
    }
}
