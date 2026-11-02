using System.Text.RegularExpressions;
using TravelRepo.Core;
using TravelRepo.Repository;

namespace Jourfold.Application;

/// <summary>
/// One line typed into a packing list, as read: the title, the group, the quantity, who brings it, and the names
/// after "@" that match nobody or several people. Such a line is not added.
/// </summary>
public sealed record PackingLine(string Title, string? Group, string? Quantity, IReadOnlyList<Guid> People, bool Everyone, IReadOnlyList<string> Unknown, IReadOnlyList<string> Ambiguous)
{
    public bool CanAdd => Title.Length > 0 && Unknown.Count == 0 && Ambiguous.Count == 0;
}

/// <summary>A person or group offered while typing "@" or "#": what the chip says and what replaces the word.</summary>
public sealed record PackingSuggestion(string Label, string Insert, string Kind);

/// <summary>
/// The packing line's shorthand, the same on the desktop and the phone. In the line, "@name" is always a person (by
/// first name, or the full name with "_" for spaces), "@all" (or "@alle") everyone, "#group" a group ("_" for
/// spaces) and "2x" or "x2" the quantity; the other words are the title. Titles can still hold "@" and "#" when
/// they are edited on the item itself.
/// </summary>
public static partial class PackingInput
{
    private static readonly string[] EveryoneWords = ["all", "alle", "everyone"];

    [GeneratedRegex(@"^(?:(\d{1,4})[x×]|[x×](\d{1,4}))$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex QuantityWord();

    public static PackingLine Read(TripSnapshot trip, string text)
    {
        var people = new List<Guid>(); var unknown = new List<string>(); var ambiguous = new List<string>();
        var everyone = false; string? group = null; string? quantity = null; var words = new List<string>();
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (word.Length > 1 && word[0] == '#') { group = word[1..].Replace('_', ' '); continue; }
            if (word.Length > 1 && word[0] == '@')
            {
                var name = word[1..];
                if (EveryoneWords.Contains(name.ToLowerInvariant())) { everyone = true; continue; }
                var matches = Matches(trip, name);
                if (matches.Count == 1) { if (!people.Contains(matches[0].Id)) people.Add(matches[0].Id); }
                else (matches.Count == 0 ? unknown : ambiguous).Add(name.Replace('_', ' '));
                continue;
            }
            if (quantity is null && QuantityWord().Match(word) is { Success: true } m) { quantity = m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value; continue; }
            words.Add(word);
        }
        return new(string.Join(' ', words).Trim(), group, quantity, people, everyone, unknown, ambiguous);
    }

    /// <summary>
    /// The edits that add the line to a list. <paramref name="addPeople"/> first adds the names nobody has as new
    /// travellers. Without "@", the item is <paramref name="owner"/>'s when given, otherwise nobody's yet.
    /// </summary>
    public static (PackingLine Line, EntityEdit[] Edits) Add(TripSnapshot trip, Guid list, string text, Guid? owner, bool addPeople = false)
    {
        if (trip.Find(list) is not { } collection || !PackingLists.IsList(collection)) throw new DomainException("packing.list", "This is not a packing list.");
        var edits = new List<EntityEdit>();
        var line = Read(trip, text);
        if (addPeople && line.Unknown.Count > 0)
        {
            foreach (var name in line.Unknown.Distinct(StringComparer.CurrentCultureIgnoreCase))
            {
                var (person, made) = QuickAdd.Build(new QuickAddDraft("person", name), trip);
                trip = made.Aggregate(trip, (t, e) => e.Value is null ? t : e.Id == t.Manifest.Id ? t with { Manifest = e.Value } : t with { Entities = t.Entities.Values.Where(x => x.Id != e.Id).Append(e.Value).ToDictionary(x => x.Id) });
                edits.RemoveAll(e => e.Id == trip.Manifest.Id); edits.AddRange(made);
            }
            line = Read(trip, text);
        }
        if (!line.CanAdd) return (line, []);
        var people = line.People.Count == 0 && !line.Everyone && owner is { } me ? [me] : line.People;
        var (item, updated) = PackingLists.AddItem(collection, line.Title, line.Group, line.Quantity, people, line.Everyone);
        edits.Add(new(item.Id, item)); edits.Add(new(updated.Id, updated));
        return (line, [.. edits]);
    }

    /// <summary>How "@" names a person in the line: the first name when nobody else has it, otherwise the full name.</summary>
    public static string Mention(TripSnapshot trip, Entity person)
    {
        var first = person.Title.Split(' ')[0];
        return Persons(trip).Count(p => p.Title.Split(' ')[0].Equals(first, StringComparison.CurrentCultureIgnoreCase)) == 1 ? first : person.Title.Replace(' ', '_');
    }

    /// <summary>
    /// People or groups for the word at <paramref name="caret"/> when it starts with "@" or "#", best first:
    /// everyone (named <paramref name="everyone"/>), then people, or the groups already used.
    /// </summary>
    public static IReadOnlyList<PackingSuggestion> Suggest(TripSnapshot trip, string text, int caret, string everyone)
    {
        var (start, length) = WordAt(text, caret);
        if (length == 0) return [];
        var word = text.Substring(start, length);
        var prefix = word[1..].Replace('_', ' ');
        bool Fits(string candidate) => candidate.Split(' ').Prepend(candidate).Any(part => part.StartsWith(prefix, StringComparison.CurrentCultureIgnoreCase));
        if (word[0] == '@')
        {
            var found = new List<PackingSuggestion>();
            if (prefix.Length == 0 || everyone.StartsWith(prefix, StringComparison.CurrentCultureIgnoreCase) || EveryoneWords.Any(w => w.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
                found.Add(new(everyone, "@" + EveryoneWords[0], "everyone"));
            found.AddRange(Persons(trip).Where(p => Fits(p.Title)).OrderBy(p => p.Title, StringComparer.CurrentCulture).Select(p => new PackingSuggestion(p.Title, "@" + Mention(trip, p), "person")));
            return found.Where(f => !word.Equals(f.Insert, StringComparison.CurrentCultureIgnoreCase)).Take(8).ToArray();
        }
        if (word[0] == '#')
            return PackingLists.All(trip).SelectMany(l => l.Items).Select(i => i.Group).OfType<string>().Distinct(StringComparer.CurrentCultureIgnoreCase)
                .Where(Fits).Order(StringComparer.CurrentCulture).Select(g => new PackingSuggestion(g, "#" + g.Replace(' ', '_'), "group"))
                .Where(f => !word.Equals(f.Insert, StringComparison.CurrentCultureIgnoreCase)).Take(8).ToArray();
        return [];
    }

    /// <summary>Replaces the word at the caret with the suggestion and a space; returns the text and the new caret.</summary>
    public static (string Text, int Caret) Accept(string text, int caret, PackingSuggestion suggestion)
    {
        var (start, length) = WordAt(text, caret);
        var end = start + length;
        var after = end < text.Length && text[end] == ' ' ? text[end..] : " " + text[end..];
        var result = text[..start] + suggestion.Insert + after;
        return (result, start + suggestion.Insert.Length + 1);
    }

    /// <summary>Whether the caret is in a word that starts with "@" or "#".</summary>
    public static bool InShorthand(string text, int caret) => WordAt(text, caret).Length > 0;

    /// <summary>The "@" or "#" word that ends at the caret; empty when the caret is not in such a word.</summary>
    private static (int Start, int Length) WordAt(string text, int caret)
    {
        caret = Math.Clamp(caret, 0, text.Length);
        var start = caret; while (start > 0 && text[start - 1] != ' ') start--;
        var end = caret; while (end < text.Length && text[end] != ' ') end++;
        return end > start && text[start] is '@' or '#' ? (start, end - start) : (start, 0);
    }

    private static IReadOnlyList<Entity> Matches(TripSnapshot trip, string name)
    {
        var wanted = name.Replace('_', ' ');
        var persons = Persons(trip);
        var full = persons.Where(p => p.Title.Equals(wanted, StringComparison.CurrentCultureIgnoreCase)).ToArray();
        return full.Length > 0 ? full : persons.Where(p => p.Title.Split(' ')[0].Equals(wanted, StringComparison.CurrentCultureIgnoreCase)).ToArray();
    }

    private static IEnumerable<Entity> Persons(TripSnapshot trip) => trip.Entities.Values.Where(e => e.Type == "person");
}
