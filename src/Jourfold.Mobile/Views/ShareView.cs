using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Jourfold.Application;
using NodaTime;
using TravelRepo.Core;

namespace Jourfold.Mobile.Views;

/// <summary>
/// What another app shared, and where it goes: files to an item of the plan or a booking, or only into the trip's
/// files; a place from a maps app to a new place or an item's place; other text to a note or an item's notes.
/// </summary>
public sealed class ShareView
{
    private readonly MobileSession s;
    private readonly SharedContent content;
    private readonly StackPanel body = Ui.V(16);
    private readonly TextBlock problem = Ui.Text("", "caption").Also(t => { t.IsVisible = false; t.Res(TextBlock.ForegroundProperty, "Danger"); t.TextWrapping = TextWrapping.Wrap; });
    private (double Latitude, double Longitude)? position;
    private bool choosing;

    public Control View { get; }
    public Control Header { get; }

    public ShareView(MobileSession session, SharedContent shared)
    {
        s = session; content = shared; position = shared.Position;
        var close = Ui.IconButton("x", s.Strings["Close"]); close.Click += (_, _) => s.DropIncoming();
        Header = Ui.Columns("*,Auto", Ui.V(2, Ui.Text(s.Strings["SharedWithJourfold"], "h3"), Ui.Text(s.Trip?.Manifest.Title ?? "", "caption")), close).Margin(16, 0, 8, 8);
        View = body.Margin(16, 0, 16, 24);
        Render();
    }

    private void Render()
    {
        var str = s.Strings; body.Children.Clear();
        body.Children.Add(Summary());
        if (content.Files.Count > 0)
        {
            body.Children.Add(Ui.Text(str["AttachTo"], "overline"));
            body.Children.Add(Chooser(includeBookings: true, async id => { await s.AddFilesAsync(content.Files, id); s.FinishSharing(id); }));
            var loose = Ui.Button(str["KeepWithTripFiles"], "folder", "soft").Also(b => b.HorizontalAlignment = HorizontalAlignment.Stretch);
            loose.Click += async (_, _) => await Run(async () => { await s.AddFilesAsync(content.Files, null); s.FinishSharing(null); s.ShowPage("files"); });
            body.Children.Add(loose);
        }
        else if (position is not null || content.ShortLink is not null)
        {
            var lines = content.Lines;
            (string?, string?, double?, double?) Draft() => (lines.FirstOrDefault(), lines.Count > 1 ? string.Join(", ", lines.Skip(1)) : null, position?.Latitude, position?.Longitude);
            if (position is null && content.ShortLink is { } link)
            {
                // A short link says nothing by itself; only the maps service knows where it leads.
                var resolve = Ui.Button(str["PositionFromLink"], "map-pin", "primary").Also(b => b.HorizontalAlignment = HorizontalAlignment.Stretch);
                resolve.Click += async (_, _) => await Run(async () =>
                {
                    resolve.IsEnabled = false;
                    position = await s.ResolveShortLinkAsync(link);
                    if (position is null) Show(str["NoPositionInLink"]); else Render();
                });
                body.Children.Add(Ui.V(6, resolve, Ui.Text(string.Format(CultureInfo.CurrentCulture, str["PositionFromLinkHint"], link.Host), "caption").Also(t => t.TextWrapping = TextWrapping.Wrap)));
            }
            var place = Ui.Button(str["NewPlace"], "map-pin", position is null ? "soft" : "primary").Also(b => b.HorizontalAlignment = HorizontalAlignment.Stretch);
            place.Click += (_, _) => { var draft = Draft(); s.FinishSharing(null); s.ShowPlace(null, draft: draft); };
            body.Children.Add(place);
            body.Children.Add(Ui.Text(str["PlaceOfItem"], "overline"));
            body.Children.Add(Chooser(includeBookings: false, id =>
            {
                var draft = Draft(); var item = s.Trip!.Find(id)!;
                var slot = PlaceEdits.Slots(item).Contains(PlaceSlot.To) ? PlaceSlot.To : PlaceSlot.Place;
                s.FinishSharing(null); s.ShowPlace(null, id, slot, draft);
                return Task.CompletedTask;
            }));
        }
        else if (content.Text is { } text)
        {
            var note = Ui.Button(str["SaveAsNote"], "sticky-note", "primary").Also(b => b.HorizontalAlignment = HorizontalAlignment.Stretch);
            note.Click += async (_, _) => await Run(async () =>
            {
                var title = content.Lines.FirstOrDefault() ?? text.Trim();
                await s.AddNoteAsync(title.Length > 60 ? title[..60].TrimEnd() + "…" : title, text); s.FinishSharing(null);
            });
            body.Children.Add(note);
            body.Children.Add(Ui.Text(str["AddToNotesOf"], "overline"));
            body.Children.Add(Chooser(includeBookings: true, async id => { await s.AppendNoteAsync(id, text); s.FinishSharing(id); }));
        }
        body.Children.Add(problem);
    }

    /// <summary>The files with their size, or the text as it came.</summary>
    private Control Summary()
    {
        var rows = Ui.V(8);
        foreach (var file in content.Files)
        {
            var icon = TravelRepo.Documents.Mail.IsMail(file.MediaType) ? "mail" : file.MediaType == "application/pdf" ? "file-text" : file.MediaType.StartsWith("image/", StringComparison.Ordinal) ? "image" : "file";
            rows.Children.Add(Ui.Columns("Auto,*", Ui.Icon(icon, 18, "Text.Muted").Margin(0, 0, 12, 0), Ui.V(1, Ui.Text(file.Name, "strong"), Ui.Text(Size(file.Bytes.Length), "caption"))));
        }
        if (content.Files.Count == 0 && content.Text is { } text)
        {
            var shown = string.Join("\n", text.Trim().Split('\n').Take(6));
            rows.Children.Add(Ui.Text(shown.Length > 400 ? shown[..400] + "…" : shown).Also(t => t.TextWrapping = TextWrapping.Wrap));
            if (position is { } at) rows.Children.Add(Ui.Columns("Auto,*", Ui.Icon("map-pin", 16, "Accent").Margin(0, 0, 8, 0), Ui.Text(PlaceEdits.Format(at.Latitude, at.Longitude), "caption")));
        }
        return Ui.Card(rows, 14);
    }

    private static string Size(long bytes) => bytes < 1024 * 1024 ? (bytes / 1024.0).ToString("0", CultureInfo.CurrentCulture) + " KB" : (bytes / 1024.0 / 1024.0).ToString("0.0", CultureInfo.CurrentCulture) + " MB";

    /// <summary>
    /// The plan's items by day, and the bookings, to choose where something goes. A long trip makes a long list, so
    /// a search narrows it, and during the trip it starts at today unless the whole trip is chosen.
    /// </summary>
    private Control Chooser(bool includeBookings, Func<Guid, Task> chosen)
    {
        var str = s.Strings;
        var list = Ui.V(4);
        // Each heading with its rows, and what a search finds a row by.
        var groups = new List<(Control Heading, LocalDate? Date, List<(Button Row, string Text)> Rows)>();
        Button Row(Entity item, string detail, string icon)
        {
            var row = new Button { Content = Ui.Columns("Auto,*", Ui.Icon(icon, 18, "Text.Muted").Margin(0, 0, 12, 0), Ui.V(1, Ui.Text(item.Title, "strong"), detail.Length > 0 ? Ui.Text(detail, "caption") : null)) }.Classed("row");
            AutomationProperties.SetName(row, item.Title);
            row.Click += async (_, _) => { if (choosing) return; choosing = true; try { await Run(() => chosen(item.Id)); } finally { choosing = false; } };
            return row;
        }
        foreach (var day in s.Agenda.Days.Where(d => d.Entries.Count > 0))
        {
            var title = Lines.DayTitle(day.Date, s);
            var rows = new List<(Button, string)>();
            foreach (var entry in day.Entries.Where(e => e.Role is AgendaRole.Item or AgendaRole.CheckIn).SelectMany(e => e.IsGroup ? [e, .. e.Steps] : new[] { e }))
            {
                var detail = TimeText.Local(entry, s.PhoneZone, s.Strings, s.Trip).Primary;
                // Also by its places, so "Kyoto" finds the temple in Kyoto.
                var places = PlaceEdits.Slots(entry.Item).Select(slot => PlaceEdits.Current(s.Trip!, entry.Item, slot)).OfType<Entity>().SelectMany(p => new[] { p.Title, p.Data["address"]?["formatted"]?.ToString() });
                rows.Add((Row(entry.Item, detail, Visuals.KindIcon(entry.Kind)), string.Join(' ', [entry.Item.Title, detail, title, entry.Item.Data["notes"]?.ToString(), .. places])));
            }
            groups.Add((Ui.Text(title, "caption").Margin(4, 8, 0, 0), day.Date, rows));
        }
        if (includeBookings)
        {
            var bookings = s.Trip!.Entities.Values.Where(e => e.Type == "booking").OrderBy(e => e.Title, StringComparer.CurrentCulture).ToArray();
            if (bookings.Length > 0)
                groups.Add((Ui.Text(str["Bookings"], "caption").Margin(4, 8, 0, 0), null, [.. bookings.Select(b => (Row(b, b.Data["reference"]?.ToString() ?? "", "ticket"), string.Join(' ', b.Title, b.Data["reference"]?.ToString(), b.Data["provider"]?["name"]?.ToString(), str["Bookings"])))]));
        }
        foreach (var (heading, _, rows) in groups) { list.Children.Add(heading); foreach (var (row, _) in rows) list.Children.Add(row); }
        var nothing = Ui.Text(str["NothingFound"], "muted").Margin(4, 8);
        list.Children.Add(nothing);

        // During the trip, the days before today are out of the way.
        var today = s.PhoneToday;
        var during = groups.Any(g => g.Date < today) && groups.Any(g => g.Date >= today);
        var whole = !during || s.Drafts.GetValueOrDefault(WholeTripKey) is not null;
        var search = MorePages.Draft(s, SearchKey, new TextBox { PlaceholderText = includeBookings ? str["ShareSearchWithBookings"] : str["ShareSearch"], InnerLeftContent = Ui.Icon("search", 16, "Text.Muted").Margin(10, 0, 0, 0) });
        AutomationProperties.SetName(search, str["Search"]);
        var fromToday = Ui.Choice(str["ShareFromToday"], "calendar", !whole, "chip"); var all = Ui.Choice(str["ShareWholeTrip"], "calendar-days", whole, "chip");
        foreach (var chip in new[] { fromToday, all }) chip.Focusable = false;

        void Filter()
        {
            var words = (search.Text ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var compare = CultureInfo.CurrentCulture.CompareInfo;
            bool Finds(string text) => words.All(w => compare.IndexOf(text, w, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0);
            var shown = 0;
            foreach (var (heading, date, rows) in groups)
            {
                // A search looks through the whole trip.
                var inTime = words.Length > 0 || whole || date is null || date >= today;
                var any = false;
                foreach (var (row, text) in rows) { row.IsVisible = inTime && Finds(text); any |= row.IsVisible; }
                heading.IsVisible = any; if (any) shown++;
            }
            nothing.IsVisible = shown == 0;
            fromToday.IsSelected = !whole; all.IsSelected = whole;
        }
        search.TextChanged += (_, _) => Filter();
        fromToday.Click += (_, _) => { whole = false; s.Drafts.Remove(WholeTripKey); Filter(); };
        all.Click += (_, _) => { whole = true; s.Drafts[WholeTripKey] = "1"; Filter(); };
        Filter();
        var chips = during ? new WrapPanel { ItemSpacing = 6, LineSpacing = 6, Children = { fromToday, all } } : null;
        return Ui.V(8, search, chips, Ui.Card(list, 8));
    }

    /// <summary>The search and the "whole trip" choice, kept while the sheet is drawn again and forgotten with the share.</summary>
    internal const string SearchKey = "share.search", WholeTripKey = "share.whole";

    private async Task Run(Func<Task> action)
    {
        problem.IsVisible = false;
        try { await action(); }
        catch (DomainException ex) { Show(ex.Code == "asset.large" ? ex.Message : s.Strings.Diagnostic(ex.Code)); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { Show(s.Strings["NetworkError"]); }
    }

    private void Show(string text) { problem.Text = text; problem.IsVisible = true; }
}
