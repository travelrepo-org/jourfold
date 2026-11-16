using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Jourfold.UI;
using TravelRepo.Core;

namespace Jourfold.Mobile.Views;

/// <summary>
/// The search over the trip: a field, what to show, and the results under the best match, days and kinds. Typing only
/// draws the results again, so the field keeps the focus and the keyboard. A tap opens a result's details over the
/// search, which stays as it was; holding a result jumps to where it is.
/// </summary>
public static class SearchView
{
    /// <summary>The pause after the last key before the results follow.</summary>
    public static readonly TimeSpan Pause = TimeSpan.FromMilliseconds(150);

    public static Control Build(MobileSession s, WidthClass width)
    {
        var str = s.Strings; var trip = s.Trip!;
        var back = Ui.Button(s.BackLabel, "chevron-left", "ghost").Also(b => { b.HorizontalAlignment = HorizontalAlignment.Left; b.Padding = new Thickness(8, 8, 12, 8); });
        back.Click += (_, _) => s.GoBack();
        var box = new TextBox
        {
            Text = s.Drafts.GetValueOrDefault(MobileSession.SearchDraft) ?? "",
            PlaceholderText = str["SearchField"],
            InnerLeftContent = Ui.Icon("search", 16, "Text.Subtle").Margin(12, 0, 0, 0),
            MinHeight = 48,
            VerticalContentAlignment = VerticalAlignment.Center,
            Name = MorePages.DraftPrefix + MobileSession.SearchDraft
        };
        AutomationProperties.SetName(box, str["SearchTrip"]);
        var clear = Ui.IconButton("x", str["ClearSearch"]);
        var options = new ContentControl();
        var results = new ContentControl();
        // Says how many were found, for a screen reader, without taking the focus from the field.
        var count = Ui.Text("", "caption").Margin(20, 0);
        AutomationProperties.SetLiveSetting(count, AutomationLiveSetting.Polite);

        void Show()
        {
            var query = box.Text?.Trim() ?? "";
            clear.IsVisible = query.Length > 0;
            options.Content = Options(s, Show);
            if (query.Length == 0)
            {
                count.Text = ""; count.IsVisible = false;
                results.Content = Ui.Card(Ui.V(8, Hint(str["SearchStart"], "search"), Hint(str["SearchHoldHint"], "info")), 16).Margin(16, 0);
                return;
            }
            var hits = SearchTexts.Find(str, trip, query, s.SearchNotes, s.SearchOnly);
            count.Text = hits.Count == 1 ? str["SearchCountOne"] : string.Format(CultureInfo.CurrentCulture, str["SearchCount"], hits.Count);
            count.IsVisible = hits.Count > 0;
            if (hits.Count == 0)
            {
                results.Content = Ui.Card(Ui.V(8, Ui.Text(string.Format(CultureInfo.CurrentCulture, str["SearchNothing"], query), "strong").Also(t => t.TextWrapping = TextWrapping.Wrap),
                    s.SearchNotes ? null : Ui.Text(str["SearchNothingNotes"], "caption").Also(t => t.TextWrapping = TextWrapping.Wrap)), 16).Margin(16, 0);
                AutomationProperties.SetLiveSetting((Control)results.Content, AutomationLiveSetting.Polite);
                return;
            }
            var list = Ui.V(16);
            foreach (var section in SearchTexts.Sections(str, trip, hits, s.SearchByDay, s.PhoneToday))
                list.Children.Add(section.Best ? Best(s, section) : Group(s, section));
            results.Content = list;
        }

        DispatcherTimer? pause = null;
        box.TextChanged += (_, _) =>
        {
            // Only typing searches; the field being built or drawn again changes nothing.
            if ((box.Text ?? "") == (s.Drafts.GetValueOrDefault(MobileSession.SearchDraft) ?? "")) return;
            s.Drafts[MobileSession.SearchDraft] = box.Text;
            pause?.Stop();
            pause = new DispatcherTimer { Interval = Pause };
            pause.Tick += (_, _) => { pause!.Stop(); Show(); };
            pause.Start();
        };
        clear.Click += (_, _) => { box.Text = ""; s.Drafts[MobileSession.SearchDraft] = ""; pause?.Stop(); Show(); box.Focus(); };
        Show();

        var header = Ui.V(4, back, Ui.Columns("*,Auto", box, clear.Margin(4, 0, 0, 0)).Margin(16, 0, 8, 8));
        var body = Ui.V(12, options, count, results);
        body.Margin = new Thickness(0, 4, 0, 32);
        var page = new Grid { RowDefinitions = new RowDefinitions("Auto,*"), Margin = new Thickness(0, 4, 0, 0) };
        page.Children.Add(header); page.Children.Add(Ui.Scroll(body).Row(1));
        return page;
    }

    private static Control Hint(string text, string icon) =>
        Ui.Columns("Auto,*", Ui.Icon(icon, 16, "Text.Subtle").Also(i => i.VerticalAlignment = VerticalAlignment.Top).Margin(0, 2, 10, 0), Ui.Text(text, "caption").Also(t => t.TextWrapping = TextWrapping.Wrap));

    /// <summary>What to show: all or one kind, grouped by day or by kind, and whether note texts are searched too.</summary>
    private static Control Options(MobileSession s, Action show)
    {
        var str = s.Strings;
        var kinds = Ui.Flow(8);
        Button Kind(string label, string? icon, SearchKind? kind)
        {
            var chip = Ui.Choice(label, icon, s.SearchOnly == kind, "chip").Also(c => c.MinHeight = 40);
            AutomationProperties.SetName(chip, string.Format(CultureInfo.CurrentCulture, str["SearchOnly"], label));
            chip.Click += (_, _) => { s.SearchOnly = kind; show(); };
            return chip;
        }
        kinds.Children.Add(Kind(str["All"], null, null));
        foreach (var kind in SearchTexts.Filters) kinds.Children.Add(Kind(SearchTexts.Kind(str, kind), SearchTexts.KindIcon(kind), kind));

        Button Grouping(string label, string icon, bool byDay)
        {
            var chip = Ui.Choice(str[label], icon, s.SearchByDay == byDay, "chip").Also(c => c.MinHeight = 40);
            chip.Click += (_, _) => { s.SearchByDay = byDay; show(); };
            return chip;
        }
        var grouping = Ui.H(8, Grouping("SearchByDay", "calendar-days", true), Grouping("SearchByKind", "layers", false));
        AutomationProperties.SetName(grouping, str["SearchGrouping"]);
        var notes = new CheckBox { IsChecked = s.SearchNotes, Content = str["SearchNotes"], MinHeight = 40 };
        AutomationProperties.SetName(notes, str["SearchNotes"]);
        notes.IsCheckedChanged += (_, _) => { s.SearchNotes = notes.IsChecked == true; show(); };
        return Ui.V(8, kinds, Ui.Flow(8, grouping, notes.Margin(4, 0, 0, 0))).Margin(16, 0);
    }

    /// <summary>The best match, larger, with its day and time on a line of their own.</summary>
    private static Control Best(MobileSession s, SearchSection section)
    {
        var str = s.Strings; var trip = s.Trip!; var hit = section.Hits[0];
        var (icon, key) = Visuals.For(trip, hit.Entity);
        var when = SearchTexts.When(str, hit);
        var where = SearchTexts.Where(str, hit);
        var content = Ui.Columns("Auto,*",
            Ui.Tile(icon, key, 44).Also(t => t.VerticalAlignment = VerticalAlignment.Top).Margin(0, 0, 14, 0),
            Ui.V(4, SearchTexts.Marked(hit.Entity.Title, hit.TitleRanges, "h3").Also(t => { t.TextWrapping = TextWrapping.Wrap; t.TextTrimming = TextTrimming.None; }),
                when is null ? Ui.Text(SearchTexts.One(str, hit.Kind), "caption") : Ui.H(6, Ui.Icon("calendar", 15, "Accent"), Ui.Text(when, "strong")),
                when is null ? null : Ui.Text(SearchTexts.One(str, hit.Kind), "caption"),
                where is { } found ? SearchTexts.Marked(found.Text, found.Ranges, "caption").Also(t => { t.TextWrapping = TextWrapping.Wrap; t.TextTrimming = TextTrimming.None; }) : null));
        var button = Result(s, hit, content);
        button.Padding = new Thickness(16, 14);
        return Ui.V(8, Ui.Text(section.Title, "overline").Margin(20, 0), new Border { Child = button, ClipToBounds = true }.Classed("card").Margin(16, 0));
    }

    private static Control Group(MobileSession s, SearchSection section)
    {
        var list = Ui.V(0);
        for (var i = 0; i < section.Hits.Count; i++)
        {
            if (i > 0) list.Children.Add(Ui.Divider());
            list.Children.Add(Row(s, section.Hits[i], withDay: section.Day is null));
        }
        return Ui.V(8, Ui.Text(section.Title, "overline").Margin(20, 0), new Border { Child = list, ClipToBounds = true }.Classed("card").Margin(16, 0));
    }

    /// <summary>A result: the kind's icon, the title with the words found marked, where it was found, and the day or time.</summary>
    private static Control Row(MobileSession s, SearchHit hit, bool withDay)
    {
        var str = s.Strings; var trip = s.Trip!;
        var (icon, key) = Visuals.For(trip, hit.Entity);
        var where = SearchTexts.Where(str, hit);
        // Under a day's heading the time is enough; elsewhere the day says when.
        var when = withDay ? SearchTexts.When(str, hit)
            : hit.Start is { } start && hit.Zone is { } zone && NodaTime.DateTimeZoneProviders.Tzdb.GetZoneOrNull(zone) is { } tz ? Formats.Time(start.InZone(tz).TimeOfDay) : null;
        var content = Ui.Columns("Auto,*,Auto",
            Ui.Tile(icon, key, 32).Also(t => t.VerticalAlignment = VerticalAlignment.Top).Margin(0, 0, 12, 0),
            Ui.V(2, SearchTexts.Marked(hit.Entity.Title, hit.TitleRanges, "strong").Also(t => { t.TextWrapping = TextWrapping.Wrap; t.TextTrimming = TextTrimming.None; }),
                where is { } found ? SearchTexts.Marked(found.Text, found.Ranges, "caption").Also(t => { t.TextWrapping = TextWrapping.Wrap; t.TextTrimming = TextTrimming.None; })
                    : Ui.Text(SearchTexts.One(str, hit.Kind), "caption")),
            when is null ? null : Ui.Text(when, "caption").Also(t => t.VerticalAlignment = VerticalAlignment.Top).Margin(8, 2, 0, 0));
        return Result(s, hit, content);
    }

    private static Button Result(MobileSession s, SearchHit hit, Control content)
    {
        var button = new Button { Content = content, Tag = "search:" + hit.Entity.Id }.Classed("row");
        AutomationProperties.SetName(button, SearchTexts.Spoken(s.Strings, hit));
        // Holding jumps to where the result is; the hold is set up before the click, which then does not count as one.
        if (s.ShowInPlaceKey(hit.Entity) is { } show)
        {
            Menus.OnHold(button, () => s.ShowInPlace(hit.Entity.Id));
            AutomationProperties.SetHelpText(button, s.Strings[show]);
        }
        button.Click += (_, _) => Open(s, hit.Entity);
        return button;
    }

    /// <summary>A result's details over the search: a sheet for most, the place's form, a packing item's page, a file's viewer.</summary>
    public static void Open(MobileSession s, Entity entity)
    {
        switch (entity.Type)
        {
            case "place": s.ShowPlace(entity.Id); break;
            case "document": s.ShowDocument(entity); break;
            case "collection": s.ShowPacking(entity.Id); break;
            case "task" when PackingLists.IsItem(entity): s.ShowPacking(entity.Id); break;
            default: s.ShowDetails(entity.Id); break;
        }
    }

    /// <summary>The way from a result's details to where it is, shown in the details while they are open over the search.</summary>
    public static Control? ShowButton(MobileSession s, Entity item)
    {
        if (s.Page != "search" || s.ShowInPlaceKey(item) is not { } key) return null;
        var show = Ui.Button(s.Strings[key], "arrow-up-right", "soft").Also(b => { b.MinHeight = 44; b.HorizontalAlignment = HorizontalAlignment.Left; b.Tag = "search.show"; });
        show.Click += (_, _) => s.ShowInPlace(item.Id);
        return show;
    }
}
