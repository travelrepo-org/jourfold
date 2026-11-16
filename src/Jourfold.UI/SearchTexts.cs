using System.Globalization;
using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using NodaTime;
using TravelRepo.Core;

namespace Jourfold.UI;

/// <summary>A group of search results under one heading: the best match, a day, or a kind.</summary>
/// <param name="Title">The heading as shown.</param>
/// <param name="Day">The day, for a group of one day.</param>
/// <param name="Best">Whether this is the best match, shown larger.</param>
public sealed record SearchSection(string Title, IReadOnlyList<SearchHit> Hits, LocalDate? Day = null, bool Best = false);

/// <summary>
/// How search results read on the computer, phones and tablets. Finding and ordering is TravelRepo's
/// <see cref="TripSearch"/>; this only names kinds and fields, groups the hits and marks the words found.
/// </summary>
public static class SearchTexts
{
    private static readonly ConditionalWeakTable<Localization, SearchLabels> Cached = new();

    /// <summary>The kinds the filter offers, in its order. Notes and files are found too and come under "All".</summary>
    public static readonly IReadOnlyList<SearchKind> Filters = [SearchKind.Plan, SearchKind.Booking, SearchKind.Cost, SearchKind.Place, SearchKind.Task, SearchKind.Packing];

    /// <summary>The order of the kinds where results have no day.</summary>
    private static readonly SearchKind[] KindOrder = [SearchKind.Plan, SearchKind.Booking, SearchKind.Cost, SearchKind.Task, SearchKind.Place, SearchKind.Packing, SearchKind.Note, SearchKind.File];

    /// <summary>
    /// The names of categories in the app's language, so "Essen" finds food. One instance per language, which lets
    /// TravelRepo keep its index while the trip does not change.
    /// </summary>
    public static SearchLabels Labels(Localization s) => Cached.GetValue(s, strings => new SearchLabels(
        Enum.GetValues<ScheduleKind>().ToDictionary(k => k, k => strings.Optional("kind." + k)),
        ExpenseCategories.Standard.ToDictionary(c => c, c => strings["expcat." + c])));

    /// <summary>Searches the trip as the app does: with the app's category names.</summary>
    public static IReadOnlyList<SearchHit> Find(Localization s, TripSnapshot trip, string? query, bool notes, SearchKind? only = null) =>
        TripSearch.For(trip, Labels(s)).Find(query, new SearchOptions { Notes = notes, Kinds = only is { } kind ? new HashSet<SearchKind> { kind } : null });

    /// <summary>The name of a kind for a heading or a filter, such as "Bookings".</summary>
    public static string Kind(Localization s, SearchKind kind) => s["search.kind." + kind];

    /// <summary>The name of one thing of a kind, such as "Booking".</summary>
    public static string One(Localization s, SearchKind kind) => s["search.one." + kind];

    public static string KindIcon(SearchKind kind) => kind switch
    {
        SearchKind.Plan => "calendar",
        SearchKind.Booking => "ticket",
        SearchKind.Cost => "wallet",
        SearchKind.Place => "map-pin",
        SearchKind.Task => "square-check",
        SearchKind.Packing => "backpack",
        SearchKind.Note => "sticky-note",
        _ => "paperclip"
    };

    /// <summary>
    /// Where a hit was found besides its title, such as "Place: Sensō-ji" or "Booking no.: GR-1187", with the words
    /// found marked; null when the title matched.
    /// </summary>
    public static (string Text, IReadOnlyList<TextRange> Ranges)? Where(Localization s, SearchHit hit)
    {
        // A title that matched says enough; the row then names the kind instead.
        if (hit.TitleRanges.Count > 0 || hit.Elsewhere is not { } match) return null;
        var label = s["search.field." + match.Field] + ": ";
        return (label + match.Text, match.Ranges.Select(r => new TextRange(r.Start + label.Length, r.Length)).ToArray());
    }

    /// <summary>The day of a hit and, for a plan with a time, its local time: "Fri 14 May · 09:30".</summary>
    public static string? When(Localization s, SearchHit hit)
    {
        if (hit.Day is not { } day) return null;
        var culture = CultureInfo.GetCultureInfo(s.Language);
        var text = day.ToString(s["DayShortFormat"], culture);
        return hit.Start is { } start && hit.Zone is { } zone && DateTimeZoneProviders.Tzdb.GetZoneOrNull(zone) is { } tz ? text + " · " + Formats.Time(start.InZone(tz).TimeOfDay) : text;
    }

    /// <summary>What a screen reader says for a result: what it is, its title, where it was found and its day.</summary>
    public static string Spoken(Localization s, SearchHit hit) =>
        string.Join(", ", new[] { One(s, hit.Kind) + ": " + hit.Entity.Title, Where(s, hit)?.Text, When(s, hit) }.OfType<string>());

    /// <summary>
    /// The results under their headings. The best match comes first, alone. By day: the days in trip order, during the
    /// trip today first, then the coming days, then the past ones; then what has no day, by kind. By kind: every kind
    /// with its hits. Within a group the best come first.
    /// </summary>
    /// <param name="today">The day it is, to put it first during the trip.</param>
    public static IReadOnlyList<SearchSection> Sections(Localization s, TripSnapshot trip, IReadOnlyList<SearchHit> hits, bool byDay, LocalDate? today)
    {
        var sections = new List<SearchSection>();
        if (hits.Count == 0) return sections;
        sections.Add(new(s["SearchBest"], [hits[0]], Best: true));
        var rest = hits.Skip(1).ToArray();
        var culture = CultureInfo.GetCultureInfo(s.Language);
        IEnumerable<SearchSection> ByKind(IEnumerable<SearchHit> some) => some.GroupBy(h => h.Kind).OrderBy(g => Array.IndexOf(KindOrder, g.Key)).Select(g => new SearchSection(Kind(s, g.Key), g.ToArray()));
        if (!byDay) { sections.AddRange(ByKind(rest)); return sections; }
        foreach (var group in TripSearch.ByDay(trip, rest, today))
        {
            if (group.Day is { } day) sections.Add(new((day == today ? s["SearchToday"] + " · " : "") + day.ToString(s["DayTitleFormat"], culture), group.Hits, day));
            else sections.AddRange(ByKind(group.Hits));
        }
        return sections;
    }

    /// <summary>A text with the words found in it in the accent colour and semibold, so they stand out without colour alone.</summary>
    public static TextBlock Marked(string text, IReadOnlyList<TextRange> ranges, params string[] classes)
    {
        var block = new TextBlock { TextTrimming = TextTrimming.CharacterEllipsis };
        foreach (var name in classes) block.Classes.Add(name);
        var at = 0;
        foreach (var range in ranges.OrderBy(r => r.Start))
        {
            if (range.Start < at || range.Start + range.Length > text.Length) continue;
            if (range.Start > at) block.Inlines!.Add(new Run(text[at..range.Start]));
            var found = new Run(text.Substring(range.Start, range.Length)) { FontWeight = FontWeight.Bold, TextDecorations = TextDecorations.Underline };
            found[!TextElement.ForegroundProperty] = new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("Accent");
            block.Inlines!.Add(found);
            at = range.Start + range.Length;
        }
        if (at < text.Length) block.Inlines!.Add(new Run(text[at..]));
        return block;
    }
}
