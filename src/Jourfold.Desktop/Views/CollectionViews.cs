using System.Globalization;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using NodaTime;
using NodaTime.Text;
using TravelRepo.Core;

namespace Jourfold.Desktop;

/// <summary>Bookings, costs, tasks, files, people, places and collections.</summary>
public static class CollectionViews
{
    public static Control Build(MainWindow w) => w.Model.View switch
    {
        "Bookings" => Bookings(w),
        "Costs" => Costs(w),
        "Tasks" => Tasks(w),
        "Files" => Files(w),
        "People" => People(w),
        "Places" => Places(w),
        "Collections" => Collections(w),
        _ => Ui.Text(w.Model.View)
    };

    private static Control Page(MainWindow w, string title, string subtitle, Control body, params Control[] actions)
    {
        var header = Ui.Columns("*,Auto", Ui.V(4, Ui.Text(title, "h1"), Ui.Text(subtitle, "muted")), Ui.H(8, actions).Also(h => h.VerticalAlignment = VerticalAlignment.Bottom));
        var panel = Ui.V(20, header, body); panel.Margin = new Thickness(32, 24, 32, 40); panel.MaxWidth = 1100;
        return new ScrollViewer { Content = new Border { Child = panel, HorizontalAlignment = HorizontalAlignment.Stretch } };
    }
    private static Button AddButton(MainWindow w, string label, string kind, bool primary = true)
    {
        var button = Ui.Button(label, "plus", primary ? "primary" : ""); button.Click += (_, _) => _ = w.Model.AddAsync(kind); return button;
    }
    private static Button Row(MainWindow w, Entity e, Control content)
    {
        var button = new Button { Content = content, Tag = e.Id }.Classed("row").Named(e.Title);
        if (w.Model.Selected?.Id == e.Id) button.Classes.Add("selected");
        button.Click += (_, _) => w.Model.Selected = w.Model.Workspace?.State.Trip.Find(e.Id);
        var menu = new ContextMenu(); var delete = new MenuItem { Header = w.Model.Strings["Delete"], Icon = Ui.Icon("trash-2", 15) }; delete.Click += (_, _) => _ = w.Model.DeleteAsync(e.Id); menu.Items.Add(delete);
        button.ContextMenu = menu; return button;
    }
    private static string Names(TripSnapshot trip, JsonNode? ids) => string.Join(", ", (ids as JsonArray ?? []).Select(n => Guid.TryParse(n?.ToString(), out var id) ? trip.Find(id)?.Title : null).OfType<string>());
    private static IEnumerable<string> PeopleNames(TripSnapshot trip, JsonNode? ids) => (ids as JsonArray ?? []).Select(n => Guid.TryParse(n?.ToString(), out var id) ? trip.Find(id)?.Title : null).OfType<string>();

    private static Control Bookings(MainWindow w)
    {
        var m = w.Model; var s = m.Strings; var trip = m.Trip!;
        var bookings = trip.Entities.Values.Where(e => e.Type == "booking").OrderBy(e => (e.Data["items"] as JsonArray ?? []).Select(i => Guid.TryParse(i?.ToString(), out var id) && trip.Find(id) is { } item ? ScheduleQueries.Span(trip, item).Start : null).Where(x => x is not null).Min() ?? Instant.MaxValue).ToArray();
        if (bookings.Length == 0) return Page(w, s["Bookings"], s["BookingsHint"], Ui.Card(Ui.EmptyState("ticket", s["NoBookingsTitle"], s["NoBookingsBody"], AddButton(w, s["AddBooking"], "booking")), 0));
        var list = Ui.V(2);
        foreach (var b in bookings)
        {
            var items = (b.Data["items"] as JsonArray ?? []).Select(i => Guid.TryParse(i?.ToString(), out var id) ? trip.Find(id) : null).OfType<Entity>().ToArray();
            var (icon, kind) = items.Length > 0 ? Visuals.For(trip, items[0]) : ("ticket", "Transport");
            var when = items.Select(i => ScheduleQueries.Span(trip, i).Start).Where(x => x is not null).Min() is { } first ? Formats.Date(first.InZone(ScheduleQueries.TripZone(trip)).Date) : s["NoDateYet"];
            var status = b.Data["status"]?.ToString() ?? "idea"; var (si, sb, sf) = Visuals.Status(status);
            var reference = b.Data["reference"]?.ToString();
            var price = TripSummaries.Money(b.Data["price"]) is { } money ? Formats.Money(money.Amount, money.Currency) : "";
            var copy = reference is null ? null : Ui.IconButton("copy", s["CopyReference"], "small").Also(c => c.Click += async (_, _) => { await m.Interaction.CopyAsync(reference); m.Interaction.Toast(s["Copied"]); });
            var refPill = reference is null ? null : Ui.H(2, new Border { Child = Ui.Text(reference).Also(t => { t.FontFamily = FontFamily.Parse("monospace"); t.FontSize = 12; }), Padding = new Thickness(8, 2), CornerRadius = new CornerRadius(6) }.Res(Border.BackgroundProperty, "Bg.Subtle"), copy);
            var travelers = PeopleNames(trip, b.Data["travelers"]).ToArray();
            var grid = Ui.Columns("Auto,*,Auto,Auto,120,Auto",
                Ui.Tile(icon, kind, 38).Margin(0, 0, 14, 0),
                Ui.V(2, Ui.Line(b.Title, "title"), Ui.Line(string.Join(" · ", new[] { b.Data["provider"]?["name"]?.ToString(), when, items.Length > 1 ? string.Format(s["ItemsCount"], items.Length) : items.FirstOrDefault()?.Title }.Where(x => !string.IsNullOrEmpty(x))), "caption")),
                refPill?.Margin(12, 0), travelers.Length > 0 ? Ui.AvatarStack(travelers, 24).Margin(8, 0) : null,
                Ui.Text(price, "strong").Also(t => { t.HorizontalAlignment = HorizontalAlignment.Right; t.TextAlignment = TextAlignment.Right; }),
                Ui.Pill(s[status], si, sb, sf).Margin(14, 0, 0, 0));
            list.Children.Add(Row(w, b, grid));
        }
        var confirmed = bookings.Count(b => b.Data["status"]?.ToString() == "confirmed");
        return Page(w, s["Bookings"], string.Format(s["BookingsSummary"], bookings.Length, confirmed), Ui.Card(list, 6), AddButton(w, s["AddBooking"], "booking"));
    }

    private static Control Costs(MainWindow w)
    {
        var m = w.Model; var s = m.Strings; var trip = m.Trip!;
        var totals = TripSummaries.Costs(trip);
        var expenses = trip.Entities.Values.Where(e => e.Type == "expense").OrderBy(e => e.Title, StringComparer.CurrentCulture).ToArray();
        var budgets = trip.Entities.Values.Where(e => e.Type == "budget").ToArray();
        var actions = new Control[] { AddButton(w, s["AddBudget"], "budget", false), AddButton(w, s["AddExpense"], "expense") };
        if (expenses.Length == 0 && budgets.Length == 0) return Page(w, s["Costs"], s["CostsHint"], Ui.Card(Ui.EmptyState("wallet", s["NoCostsTitle"], s["NoCostsBody"], actions), 0));
        var tiles = new WrapPanel { ItemSpacing = 14, LineSpacing = 14 };
        foreach (var total in totals)
        {
            var spent = total.Paid + total.Estimated;
            var body = Ui.V(8, Ui.Columns("*,Auto", Ui.Text(total.Currency, "overline"), total.Budget > 0 ? Ui.Text(string.Format(s["OfBudget"], Formats.Money(total.Budget, total.Currency)), "caption") : null),
                Ui.Text(Formats.Money(spent, total.Currency), "h1"),
                Ui.V(4, Ui.H(5, Ui.Icon("circle-check", 13, "Success"), Ui.Text(string.Format(s["PaidAmount"], Formats.Money(total.Paid, total.Currency)), "caption")), total.Estimated > 0 ? Ui.H(5, Ui.Icon("clock", 13, "Warning"), Ui.Text(string.Format(s["EstimatedAmount"], Formats.Money(total.Estimated, total.Currency)), "caption")) : null));
            if (total.Budget > 0)
            {
                var ratio = (double)Math.Min(1.2m, spent / total.Budget);
                var track = new Border { Height = 8, CornerRadius = new CornerRadius(4) }.Res(Border.BackgroundProperty, "Bg.Muted");
                var bar = new Border { Height = 8, CornerRadius = new CornerRadius(4), HorizontalAlignment = HorizontalAlignment.Left, Width = 260 * Math.Min(1, ratio) }.Res(Border.BackgroundProperty, ratio > 1 ? "Danger" : ratio > 0.85 ? "Warning" : "Accent");
                body.Children.Add(new Panel { Children = { track, bar }, Width = 260, HorizontalAlignment = HorizontalAlignment.Left });
                body.Children.Add(Ui.Text(spent <= total.Budget ? string.Format(s["BudgetLeft"], Formats.Money(total.Budget - spent, total.Currency)) : string.Format(s["BudgetOver"], Formats.Money(spent - total.Budget, total.Currency)), "caption"));
            }
            tiles.Children.Add(Ui.Card(body, 18).Also(c => c.Width = 300));
        }
        var list = Ui.V(2);
        foreach (var e in expenses.Concat(budgets))
        {
            var money = TripSummaries.Money(e.Data["amount"]);
            var estimated = e.Data["estimated"] is JsonValue v && v.TryGetValue<bool>(out var est) && est;
            var payers = PeopleNames(trip, e.Data["paid_by"]).ToArray();
            var related = (e.Data["related"] as JsonArray ?? []).Select(r => Guid.TryParse(r?["id"]?.ToString(), out var id) ? trip.Find(id)?.Title : null).OfType<string>().FirstOrDefault();
            var grid = Ui.Columns("Auto,*,Auto,Auto,140",
                Ui.Tile(e.Type == "budget" ? "piggy-bank" : "receipt", e.Type == "budget" ? "Nature" : "Food", 34).Margin(0, 0, 12, 0),
                Ui.V(2, Ui.Line(e.Title, "title"), Ui.Line(e.Type == "budget" ? s["budget"] : related ?? (payers.Length > 0 ? string.Format(s["PaidBy"], string.Join(", ", payers)) : ""), "caption")),
                estimated ? Ui.Pill(s["Estimated"], "clock", "Warning.Soft", "Warning").Margin(8, 0) : null,
                payers.Length > 0 ? Ui.AvatarStack(payers, 22).Margin(8, 0) : null,
                Ui.Text(money is null ? "" : Formats.Money(money.Amount, money.Currency), "strong").Also(t => { t.HorizontalAlignment = HorizontalAlignment.Right; }));
            list.Children.Add(Row(w, e, grid));
        }
        var note = Ui.H(6, Ui.Icon("info", 13, "Text.Subtle"), Ui.Text(s["NoAutoConversion"], "caption"));
        return Page(w, s["Costs"], s["CostsHint"], Ui.V(20, tiles, Ui.Card(list, 6), note), actions);
    }

    private static Control Tasks(MainWindow w)
    {
        var m = w.Model; var s = m.Strings; var trip = m.Trip!;
        var tasks = trip.Entities.Values.Where(e => e.Type == "task").ToArray();
        var input = new TextBox { Watermark = s["AddTaskPlaceholder"] }; Avalonia.Automation.AutomationProperties.SetName(input, s["AddTaskPlaceholder"]);
        input.KeyDown += async (_, e) =>
        {
            if (e.Key != Key.Enter || string.IsNullOrWhiteSpace(input.Text)) return;
            var title = input.Text.Trim(); input.Text = ""; e.Handled = true;
            await m.RunEditAsync(async () => { var task = Entity.Create("task", title); if (m.Me is { } me) task.Data["assigned_to"] = new JsonArray(me.Id.ToString()); await m.Workspace!.EditAsync(task); });
        };
        var today = SystemClock.Instance.GetCurrentInstant().InZone(ScheduleQueries.TripZone(trip)).Date;
        LocalDate? Due(Entity e) => e.Data["due"]?["date"] is { } d && LocalDatePattern.Iso.Parse(d.ToString()).TryGetValue(default, out var day) ? day : null;
        Control Group(string title, IEnumerable<Entity> items)
        {
            var list = Ui.V(2);
            foreach (var t in items.OrderBy(x => Due(x) ?? LocalDate.MaxIsoValue).ThenBy(x => x.Title, StringComparer.CurrentCulture))
            {
                var done = t.Data["status"]?.ToString() is "completed" or "cancelled";
                var check = new CheckBox { IsChecked = done, MinWidth = 0, Padding = new Thickness(0) }; Avalonia.Automation.AutomationProperties.SetName(check, string.Format(s["MarkDone"], t.Title));
                check.IsCheckedChanged += (_, _) => _ = m.ToggleTaskAsync(t.Id);
                var due = Due(t); var overdue = !done && due is { } d && d < today;
                var dueText = due is { } dd ? Ui.Pill(Formats.ShortDate(dd), "calendar", overdue ? "Danger.Soft" : "Bg.Subtle", overdue ? "Danger" : "Text.Muted") : null;
                var assignees = PeopleNames(trip, t.Data["assigned_to"]).ToArray();
                var related = (t.Data["related"] as JsonArray ?? []).Select(r => Guid.TryParse(r?["id"]?.ToString(), out var id) ? trip.Find(id)?.Title : null).OfType<string>().FirstOrDefault();
                var name = Ui.Line(t.Title, "title"); if (done) { name.TextDecorations = TextDecorations.Strikethrough; name.Classes.Add("muted"); }
                var grid = Ui.Columns("Auto,*,Auto,Auto", check, Ui.V(1, name, related is null ? null : Ui.Line(related, "caption")).Margin(8, 0), dueText?.Margin(8, 0), assignees.Length > 0 ? Ui.AvatarStack(assignees, 22) : null);
                list.Children.Add(Row(w, t, grid));
            }
            return Ui.V(8, Ui.Text(title, "h3"), Ui.Card(list.Children.Count > 0 ? list : Ui.Text(s["NothingHere"], "caption").Margin(12), 6));
        }
        var open = tasks.Where(t => t.Data["status"]?.ToString() is not ("completed" or "cancelled")).ToArray();
        var done = tasks.Except(open).ToArray();
        var body = Ui.V(20, input, Group(string.Format(s["ToDoCount"], open.Length), open));
        if (done.Length > 0) body.Children.Add(Group(string.Format(s["DoneCount"], done.Length), done));
        return Page(w, s["Tasks"], s["TasksHint"], body, AddButton(w, s["AddTask"], "task"));
    }

    private static Control Files(MainWindow w)
    {
        var m = w.Model; var s = m.Strings; var trip = m.Trip!;
        var add = Ui.Button(s["AddFile"], "upload", "primary"); add.Command = m.AddFileCommand;
        var docs = trip.Entities.Values.Where(e => e.Type is "document").OrderBy(e => e.Title, StringComparer.CurrentCulture).ToArray();
        var notes = trip.Entities.Values.Where(e => e.Type is "note").ToArray();
        var newNote = AddButton(w, s["AddNote"], "note", false);
        var drop = new Border { Padding = new Thickness(20), CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1.5), Child = Ui.H(12, Ui.Icon("upload", 20, "Accent"), Ui.V(2, Ui.Text(s["DropFilesTitle"], "title"), Ui.Text(s["DropFilesBody"], "caption"))) }
            .Res(Border.BorderBrushProperty, "Line.Strong").Res(Border.BackgroundProperty, "Bg.Surface");
        var grid = new WrapPanel { ItemSpacing = 14, LineSpacing = 14 };
        foreach (var doc in docs) grid.Children.Add(FileTile(w, doc));
        foreach (var note in notes) grid.Children.Add(NoteTile(w, note));
        var body = Ui.V(18, drop, grid);
        return Page(w, s["Files"], s["FilesHint"], body, newNote, add);
    }

    private static Control FileTile(MainWindow w, Entity doc)
    {
        var m = w.Model; var s = m.Strings; var trip = m.Trip!;
        var media = doc.Data["media_type"]?.ToString() ?? ""; Control preview;
        string? path = null; try { path = m.Workspace!.Repository.DocumentPath(doc); } catch (DomainException) { }
        if (media.StartsWith("image/", StringComparison.Ordinal) && path is not null && File.Exists(path))
        {
            try { using var stream = File.OpenRead(path); preview = new Image { Source = Bitmap.DecodeToWidth(stream, 360), Stretch = Stretch.UniformToFill }; }
            catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException) { preview = Ui.Icon("image", 36, "Text.Subtle"); }
        }
        else preview = Ui.Icon(media == "application/pdf" ? "file-text" : "file", 36, "Text.Subtle").Also(i => i.HorizontalAlignment = HorizontalAlignment.Center);
        var size = path is not null && File.Exists(path) ? Formats.Bytes(new FileInfo(path).Length) : "";
        var references = TripSummaries.ReferencesTo(trip, doc.Id).Count;
        var thumb = new Border { Height = 120, Child = preview, ClipToBounds = true, CornerRadius = new CornerRadius(11, 11, 0, 0) }.Res(Border.BackgroundProperty, "Bg.Subtle");
        var info = Ui.V(2, Ui.Line(doc.Title, "title"), Ui.Line(string.Join(" · ", new[] { media == "application/pdf" ? "PDF" : media.Split('/').LastOrDefault()?.ToUpperInvariant(), size, references > 0 ? string.Format(s["UsedIn"], references) : null }.Where(x => !string.IsNullOrEmpty(x))), "caption")).Margin(12, 10);
        var button = new Button { Width = 200, Content = Ui.V(0, thumb, info) }.Classed("card").Named(doc.Title);
        if (m.Selected?.Id == doc.Id) button.Classes.Add("selected");
        button.Click += (_, _) => m.Selected = m.Workspace?.State.Trip.Find(doc.Id);
        button.DoubleTapped += (_, _) => InspectorView.OpenDocument(m, doc);
        return button;
    }
    private static Control NoteTile(MainWindow w, Entity note)
    {
        var m = w.Model;
        var body = note.Data["body"]?.ToString() ?? "";
        var preview = new Border { Height = 120, Padding = new Thickness(12), Child = Ui.Text(body.Length > 160 ? body[..160] + "…" : body, "caption"), CornerRadius = new CornerRadius(11, 11, 0, 0) }.Res(Border.BackgroundProperty, "Kind.Nightlife.Bg");
        var info = Ui.H(6, Ui.Icon("sticky-note", 14, "Kind.Nightlife.Fg"), Ui.Line(note.Title, "title")).Margin(12, 10);
        var button = new Button { Width = 200, Content = Ui.V(0, preview, info) }.Classed("card").Named(note.Title);
        if (m.Selected?.Id == note.Id) button.Classes.Add("selected");
        button.Click += (_, _) => m.Selected = m.Workspace?.State.Trip.Find(note.Id);
        return button;
    }

    private static Control People(MainWindow w)
    {
        var m = w.Model; var s = m.Strings; var trip = m.Trip!;
        var me = m.Me;
        var addMe = Ui.Button(s["AddMyself"], "user-plus", ""); addMe.Command = m.AddIdentityCommand; addMe.IsVisible = me is null;
        var people = trip.Entities.Values.Where(e => e.Type == "person").OrderBy(p => p.Title, StringComparer.CurrentCulture).ToArray();
        if (people.Length == 0) return Page(w, s["People"], s["PeopleHint"], Ui.Card(Ui.EmptyState("users", s["NoPeopleTitle"], s["NoPeopleBody"], addMe.Also(b => b.Classes.Add("primary")), AddButton(w, s["AddPerson"], "person", false)), 0));
        var grid = new WrapPanel { ItemSpacing = 14, LineSpacing = 14 };
        foreach (var person in people)
        {
            var roles = (person.Data["roles"] as JsonArray ?? []).Select(r => s.Optional(r?.ToString() ?? "")).ToArray();
            var items = trip.Entities.Values.Count(e => e.Type == "schedule_item" && ScheduleQueries.Participants(trip, e).Any(p => p.Id == person.Id));
            var github = (person.Data["identities"]?["github"] as JsonArray ?? []).Select(g => g?["username"]?.ToString()).FirstOrDefault(x => x is not null);
            var body = Ui.V(10, Ui.Columns("Auto,*", Ui.Avatar(person.Title, 52), Ui.V(3, Ui.H(6, Ui.Line(person.Title, "h3"), person.Id == me?.Id ? Ui.Pill(s["You"], null, "Accent.Soft", "Accent.SoftText") : null), Ui.Line(roles.Length > 0 ? string.Join(", ", roles) : s["traveler"], "caption")).Margin(14, 0, 0, 0)),
                Ui.H(14, Ui.H(5, Ui.Icon("calendar", 13, "Text.Subtle"), Ui.Text(string.Format(s["InItems"], items), "caption")), github is null ? null : Ui.H(5, Ui.Icon("cloud", 13, "Text.Subtle"), Ui.Text("@" + github, "caption"))));
            var button = new Button { Width = 300, Content = body.Margin(18) }.Classed("card").Named(person.Title);
            if (m.Selected?.Id == person.Id) button.Classes.Add("selected");
            button.Click += (_, _) => m.Selected = m.Workspace?.State.Trip.Find(person.Id);
            grid.Children.Add(button);
        }
        return Page(w, s["People"], s["PeopleHint"], grid, addMe, AddButton(w, s["AddPerson"], "person"));
    }

    private static Control Places(MainWindow w)
    {
        var m = w.Model; var s = m.Strings; var trip = m.Trip!;
        var places = trip.Entities.Values.Where(e => e.Type == "place").OrderBy(p => p.Title, StringComparer.CurrentCulture).ToArray();
        if (places.Length == 0) return Page(w, s["Places"], s["PlacesHint"], Ui.Card(Ui.EmptyState("map-pin", s["NoPlacesTitle"], s["NoPlacesBody"], AddButton(w, s["AddPlace"], "place")), 0));
        var list = Ui.V(2);
        foreach (var place in places)
        {
            var coords = MapView.Coordinates(place);
            var uses = TripSummaries.ReferencesTo(trip, place.Id).Count(e => e.Type == "schedule_item");
            var map = Ui.IconButton("map", s["ShowOnMap"], "small"); map.IsEnabled = coords is not null; map.Click += (_, _) => { m.Selected = m.Workspace?.State.Trip.Find(place.Id); m.Navigate("Map"); };
            var grid = Ui.Columns("Auto,*,Auto,Auto,Auto",
                Ui.Tile("map-pin", coords is null ? "Other" : "Sightseeing", 34).Margin(0, 0, 12, 0),
                Ui.V(2, Ui.Line(place.Title, "title"), Ui.Line(place.Data["address"]?["formatted"]?.ToString() ?? (coords is null ? s["NoLocationYet"] : coords.Value.Lat.ToString("0.0000", CultureInfo.CurrentCulture) + ", " + coords.Value.Lon.ToString("0.0000", CultureInfo.CurrentCulture)), "caption")),
                uses > 0 ? Ui.Pill(string.Format(s["InItems"], uses), "calendar", "Bg.Subtle", "Text.Muted").Margin(8, 0) : null,
                coords is null ? Ui.Pill(s["NoLocation"], "triangle-alert", "Warning.Soft", "Warning").Margin(8, 0) : null,
                map);
            list.Children.Add(Row(w, place, grid));
        }
        return Page(w, s["Places"], string.Format(s["PlacesSummary"], places.Length), Ui.Card(list, 6), AddButton(w, s["AddPlace"], "place"));
    }

    private static Control Collections(MainWindow w)
    {
        var m = w.Model; var s = m.Strings; var trip = m.Trip!;
        var collections = trip.Entities.Values.Where(e => e.Type == "collection").OrderBy(c => c.Title, StringComparer.CurrentCulture).ToArray();
        if (collections.Length == 0) return Page(w, s["Collections"], s["CollectionsHint"], Ui.Card(Ui.EmptyState("layers", s["NoCollectionsTitle"], s["NoCollectionsBody"], AddButton(w, s["AddCollection"], "collection")), 0));
        var grid = new WrapPanel { ItemSpacing = 14, LineSpacing = 14 };
        foreach (var c in collections)
        {
            var items = (c.Data["items"] as JsonArray ?? []).Select(i => Guid.TryParse(i?.ToString(), out var id) ? trip.Find(id) : null).OfType<Entity>().ToArray();
            var list = Ui.V(6); foreach (var item in items.Take(4)) { var (icon, kind) = Visuals.For(trip, item); list.Children.Add(Ui.H(8, Ui.Icon(icon, 13, "Kind." + kind + ".Fg"), Ui.Line(item.Title, "caption"))); }
            if (items.Length > 4) list.Children.Add(Ui.Text(string.Format(s["AndMore"], items.Length - 4), "caption"));
            var body = Ui.V(10, Ui.Columns("Auto,*", Ui.Tile("layers", "Culture", 34), Ui.V(1, Ui.Line(c.Title, "h3"), Ui.Text(string.Format(s["ItemsCount"], items.Length), "caption")).Margin(12, 0, 0, 0)), list);
            var button = new Button { Width = 300, MinHeight = 180, Content = body.Margin(18), VerticalContentAlignment = VerticalAlignment.Top }.Classed("card").Named(c.Title);
            if (m.Selected?.Id == c.Id) button.Classes.Add("selected");
            button.Click += (_, _) => m.Selected = m.Workspace?.State.Trip.Find(c.Id);
            grid.Children.Add(button);
        }
        return Page(w, s["Collections"], s["CollectionsHint"], grid, AddButton(w, s["AddCollection"], "collection"));
    }
}
