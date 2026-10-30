using System.Globalization;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using NodaTime;
using NodaTime.Text;
using TravelRepo.Core;

namespace Jourfold.Mobile.Views;

/// <summary>
/// The pages under More: bookings, tasks, costs, travellers, sync and versions, and settings. Lists unfold in place
/// instead of opening new screens, so the overview stays in view.
/// </summary>
public static class MorePages
{
    public static Control Build(MobileSession s, WidthClass width) => s.Page switch
    {
        "bookings" => Bookings(s),
        "tasks" => Tasks(s),
        "places" => Places(s),
        "files" => Files(s),
        "costs" => Costs(s, width),
        "travellers" => Travellers(s),
        "sync" => Sync(s),
        "settings" => Settings(s),
        _ => MoreView.Build(s, width)
    };

    /// <summary>A page with a way back to More and a large title.</summary>
    private static Control Frame(MobileSession s, string title, params Control?[] body)
    {
        var back = Ui.Button(s.Strings["More"], "chevron-left", "ghost").Also(b => { b.HorizontalAlignment = HorizontalAlignment.Left; b.Padding = new Thickness(8, 8, 12, 8); });
        back.Click += (_, _) => s.ShowPage(null);
        var page = Ui.V(16, Ui.V(4, back, Ui.Text(title, "h1").Margin(16, 0)));
        foreach (var part in body) if (part is not null) page.Children.Add(part);
        page.Margin = new Thickness(0, 4, 0, 32);
        return Ui.Scroll(page);
    }

    private static Control Group(string title, IReadOnlyList<Control> rows)
    {
        var list = Ui.V(0);
        for (var i = 0; i < rows.Count; i++) { if (i > 0) list.Children.Add(Ui.Divider()); list.Children.Add(rows[i]); }
        return Ui.V(8, Ui.Text(title, "overline").Margin(20, 0), new Border { Child = list, ClipToBounds = true }.Classed("card").Margin(16, 0));
    }

    /// <summary>A row that unfolds its details below itself when tapped.</summary>
    private static Control Unfolding(MobileSession s, Guid id, Control summary, Func<Control> details)
    {
        var open = s.ExpandedGroups.Contains(id);
        var button = new Button { Content = summary }.Classed("row");
        button.Click += (_, _) => s.ToggleGroup(id);
        AutomationProperties.SetName(button, Ui.FindDescendant<TextBlock>(summary, _ => true)?.Text);
        return open ? Ui.V(0, button, details().Margin(16, 0, 16, 16)) : button;
    }

    /// <summary>
    /// A field that keeps what was typed when the page is drawn again, for example after tapping Sync now. The shell
    /// does not redraw while a field has the focus, so this is only about the text.
    /// </summary>
    public static TextBox Draft(MobileSession s, string key, TextBox box)
    {
        if (s.Drafts.TryGetValue(key, out var text)) box.Text = text;
        box.TextChanged += (_, _) => s.Drafts[key] = box.Text;
        return box;
    }

    private static Control Empty(string icon, string title, string body) => Ui.Card(Ui.EmptyState(icon, title, body), 0).Margin(16, 0);

    /// <summary>The trip's files, newest name order, each opening in the viewer, with what it belongs to.</summary>
    private static Control Files(MobileSession s)
    {
        var str = s.Strings; var trip = s.Trip!;
        var files = trip.Entities.Values.Where(e => e.Type == "document").OrderBy(e => e.Title, StringComparer.CurrentCulture).ToArray();
        if (files.Length == 0) return Frame(s, str["Files"], Empty("paperclip", str["NoFilesYet"], str["NoFilesYetBody"]));
        var rows = files.Select(file =>
        {
            var owners = string.Join(", ", TripSummaries.ReferencesTo(trip, file.Id).Where(e => e.Type is "schedule_item" or "booking").Select(e => e.Title));
            var media = file.Data["media_type"]?.ToString() ?? "";
            var icon = TravelRepo.Documents.Mail.IsMail(media) ? "mail" : media == "application/pdf" ? "file-text" : media.StartsWith("image/", StringComparison.Ordinal) ? "image" : "file";
            var row = new Button { Content = Ui.Columns("Auto,*,Auto", Ui.Icon(icon, 18, "Text.Muted").Margin(0, 0, 14, 0), Ui.V(1, Ui.Text(file.Title, "strong"), owners.Length > 0 ? Ui.Text(owners, "caption") : null), Ui.Icon("chevron-right", 18, "Text.Subtle")) }.Classed("row");
            AutomationProperties.SetName(row, file.Title);
            row.Click += (_, _) => s.ShowDocument(file);
            return (Control)row;
        }).ToList();
        return Frame(s, str["Files"], Group(string.Format(CultureInfo.CurrentCulture, str["CountFiles"], files.Length), rows));
    }

    /// <summary>The trip's places by name, each with its address or position; a tap opens it to change, and a button adds one.</summary>
    private static Control Places(MobileSession s)
    {
        var str = s.Strings; var trip = s.Trip!;
        var places = trip.Entities.Values.Where(e => e.Type == "place").OrderBy(e => e.Title, StringComparer.CurrentCulture).ToArray();
        var add = Ui.Button(str["AddPlace"], "plus", "soft").Also(b => b.HorizontalAlignment = HorizontalAlignment.Stretch);
        add.Click += (_, _) => s.ShowPlace(null);
        if (places.Length == 0) return Frame(s, str["Places"], Empty("map-pin", str["NoPlacesYet"], str["NoPlacesYetBody"]), add.Margin(16, 0));
        var rows = places.Select(place =>
        {
            var position = TravelRepo.Core.Places.Location(place);
            var detail = TravelRepo.Core.Places.Address(place) ?? (position is { } at ? Jourfold.Application.PlaceEdits.Format(at.Latitude, at.Longitude) : str["NoPositionYet"]);
            var row = new Button
            {
                Content = Ui.Columns("Auto,*,Auto", Ui.Icon("map-pin", 18, position is null ? "Warning" : "Text.Muted").Margin(0, 0, 14, 0),
                    Ui.V(1, Ui.Text(place.Title, "strong"), Ui.Text(detail, "caption")), Ui.Icon("chevron-right", 18, "Text.Subtle"))
            }.Classed("row");
            AutomationProperties.SetName(row, place.Title + ", " + detail);
            row.Click += (_, _) => s.ShowPlace(place.Id);
            return (Control)row;
        }).ToList();
        var missing = places.Count(p => TravelRepo.Core.Places.Location(p) is null);
        return Frame(s, str["Places"], add.Margin(16, 0), Group(string.Format(CultureInfo.CurrentCulture, str["CountPlaces"], places.Length), rows),
            missing > 0 ? Ui.Text(str["PlacesWithoutPosition"], "caption").Margin(20, 0) : null);
    }

    private static string Day(LocalDate date, MobileSession s) => Lines.DayShort(date, s.Strings);

    /// <summary>The first plan item a booking or expense belongs to, with its start where it happens.</summary>
    private static (Entity Item, ZonedDateTime? Start)? FirstItem(TripSnapshot trip, Entity entity)
    {
        IEnumerable<Entity> Items(Entity e) => e.Type switch
        {
            "schedule_item" => [e],
            "booking" => (e.Data["items"] as JsonArray ?? []).Select(i => Guid.TryParse(i?.ToString(), out var id) ? trip.Find(id) : null).OfType<Entity>(),
            _ => (e.Data["related"] as JsonArray ?? []).Select(r => Guid.TryParse(r?["id"]?.ToString(), out var id) ? trip.Find(id) : null).OfType<Entity>().Where(r => r.Type is "schedule_item" or "booking").SelectMany(Items)
        };
        var first = Items(entity).Select(i => (Item: i, Span: ScheduleQueries.Span(trip, i))).OrderBy(x => x.Span.Start ?? Instant.MaxValue).FirstOrDefault();
        if (first.Item is null) return null;
        var zone = DateTimeZoneProviders.Tzdb.GetZoneOrNull(first.Span.StartZone ?? "") ?? ScheduleQueries.TripZone(trip);
        return (first.Item, first.Span.Start?.InZone(zone));
    }

    private static string Names(TripSnapshot trip, JsonNode? ids) =>
        string.Join(", ", (ids as JsonArray ?? []).Select(n => Guid.TryParse(n?.ToString(), out var id) ? trip.Find(id)?.Title : null).OfType<string>());

    // Bookings

    private static Control Bookings(MobileSession s)
    {
        var trip = s.Trip!; var str = s.Strings;
        var bookings = trip.Entities.Values.Where(e => e.Type == "booking")
            .Select(b => (Booking: b, First: FirstItem(trip, b)))
            .OrderBy(x => x.First?.Start?.ToInstant() ?? Instant.MaxValue).ThenBy(x => x.Booking.Title, StringComparer.CurrentCulture).ToArray();
        if (bookings.Length == 0) return Frame(s, str["Bookings"], Empty("ticket", str["NoBookingsTitle"], str["NoBookingsBody"]));
        var groups = bookings.GroupBy(x => x.Booking.Data["status"]?.ToString() ?? "planned")
            .OrderBy(g => g.Key switch { "confirmed" => 0, "reserved" => 1, "planned" => 2, "idea" => 3, _ => 4 });
        return Frame(s, str["Bookings"], groups.Select(g => Group(str.Optional(g.Key) is { Length: > 0 } label ? label : g.Key,
            g.Select(x => Booking(s, x.Booking, x.First)).ToArray())).ToArray());
    }

    private static Control Booking(MobileSession s, Entity booking, (Entity Item, ZonedDateTime? Start)? first)
    {
        var trip = s.Trip!; var str = s.Strings;
        var (icon, kind) = first is { } f ? Visuals.For(trip, f.Item) : ("ticket", "Transport");
        var when = first?.Start is { } start ? Day(start.Date, s) : str["NoDateYet"];
        var where = first is { } g ? Lines.Where(trip, g.Item) : "";
        var reference = booking.Data["reference"]?.ToString();
        var price = TripSummaries.Money(booking.Data["price"]) is { } money ? Formats.Money(money.Amount, money.Currency) : null;
        var summary = Ui.Columns("Auto,*,Auto",
            Ui.Tile(icon, kind, 36).Also(t => t.VerticalAlignment = VerticalAlignment.Top).Margin(0, 2, 12, 0),
            Ui.V(2, Ui.Text(booking.Title, "strong"), Ui.Text(string.Join(" · ", new[] { when, where }.Where(t => t.Length > 0)), "caption"),
                reference is null ? null : Ui.Text(string.Format(CultureInfo.CurrentCulture, str["ReferenceShort"], reference), "caption")),
            Ui.V(6, price is null ? null : Ui.Text(price, "strong").Also(t => t.HorizontalAlignment = HorizontalAlignment.Right),
                Lines.StatusChip(booking.Data["status"]?.ToString(), s)?.Also(c => c.HorizontalAlignment = HorizontalAlignment.Right)).Margin(8, 0, 0, 0));
        return Unfolding(s, booking.Id, summary, () =>
        {
            var body = Ui.V(12);
            if (reference is not null) body.Children.Add(DetailView.Copyable(s, "ticket", reference, DetailView.Provider(booking)));
            if (first is { } item && Navigation.Button(s, item.Item) is { } navigate) body.Children.Add(navigate.Also(b => b.HorizontalAlignment = HorizontalAlignment.Stretch));
            if (Names(trip, booking.Data["travelers"]) is { Length: > 0 } people) body.Children.Add(DetailView.Row("users", people, null, s));
            foreach (var linked in (booking.Data["items"] as JsonArray ?? []).Select(i => Guid.TryParse(i?.ToString(), out var id) ? trip.Find(id) : null).OfType<Entity>())
            {
                var open = Ui.Button(linked.Title, "calendar", "ghost").Also(b => b.HorizontalAlignment = HorizontalAlignment.Stretch);
                open.Click += (_, _) => s.ShowDetails(linked.Id);
                body.Children.Add(open);
            }
            if (booking.Data["notes"]?.ToString() is { Length: > 0 } notes) body.Children.Add(DetailView.Row("sticky-note", notes, null, s));
            var details = Ui.Button(str["ShowDetails"], "chevron-right");
            details.Click += (_, _) => s.ShowDetails(booking.Id);
            body.Children.Add(details);
            return body;
        });
    }

    // Tasks

    private static Control Tasks(MobileSession s)
    {
        var trip = s.Trip!; var str = s.Strings; var today = s.PhoneToday;
        var tasks = trip.Entities.Values.Where(e => e.Type == "task" && !PackingLists.IsItem(e)).ToArray();
        if (tasks.Length == 0) return Frame(s, str["Tasks"], Empty("square-check", str["NoTasksTitle"], str["NoTasksBody"]));
        static bool Done(Entity t) => t.Data["status"]?.ToString() is "completed" or "cancelled" or "done";
        LocalDate? Due(Entity t) => t.Data["due"]?["date"] is { } d && LocalDatePattern.Iso.Parse(d.ToString()) is { Success: true } r ? r.Value : null;
        var overdue = tasks.Where(t => !Done(t) && Due(t) < today).ToArray();
        var open = tasks.Where(t => !Done(t)).Except(overdue).ToArray();
        var done = tasks.Where(Done).ToArray();
        Control? Section(string title, Entity[] items) => items.Length == 0 ? null
            : Group(title, items.OrderBy(t => Due(t) ?? LocalDate.MaxIsoValue).ThenBy(t => t.Title, StringComparer.CurrentCulture).Select(t => Task(s, t, Due(t), Due(t) < today && !Done(t))).ToArray());
        return Frame(s, str["Tasks"], Section(str["Overdue"], overdue), Section(str["OpenTasks"], open), Section(str["DoneTasks"], done));
    }

    private static Control Task(MobileSession s, Entity task, LocalDate? due, bool late)
    {
        var trip = s.Trip!;
        var done = task.Data["status"]?.ToString() is "completed" or "done";
        var related = (task.Data["related"] as JsonArray ?? []).Select(r => Guid.TryParse(r?["id"]?.ToString(), out var id) ? trip.Find(id)?.Title : null).OfType<string>().FirstOrDefault();
        var detail = string.Join(" · ", new[] { due is { } d ? Day(d, s) : null, Names(trip, task.Data["assigned_to"]), related }.Where(t => !string.IsNullOrEmpty(t)));
        var name = Ui.Text(task.Title).Also(t => { if (done) { t.Classes.Add("muted"); t.TextDecorations = Avalonia.Media.TextDecorations.Strikethrough; } });
        var caption = detail.Length == 0 ? null : Ui.Text(detail, "caption").Also(t => { if (late) t.Res(TextBlock.ForegroundProperty, "Danger"); });
        var box = new CheckBox { IsChecked = done, Content = Ui.V(2, name, caption), MinHeight = 56, Padding = new Thickness(12, 0, 0, 0), Margin = new Thickness(16, 4) };
        AutomationProperties.SetName(box, task.Title);
        box.IsCheckedChanged += async (_, _) => await s.SetTaskDoneAsync(task.Id, box.IsChecked == true);
        return box;
    }

    // Costs

    private static Control Costs(MobileSession s, WidthClass width)
    {
        var trip = s.Trip!; var str = s.Strings;
        var totals = TripSummaries.Costs(trip);
        var expenses = trip.Entities.Values.Where(e => e.Type == "expense").Select(e => (Expense: e, First: FirstItem(trip, e))).ToArray();
        if (totals.Count == 0) return Frame(s, str["Costs"], Empty("wallet", str["NoCostsTitle"], str["NoCostsBody"]));
        var columns = totals.Count > 1 ? 2 : 1;
        var tiles = new Grid { ColumnDefinitions = new ColumnDefinitions(columns == 2 ? "*,12,*" : "*"), Margin = new Thickness(16, 0) };
        for (var i = 0; i < totals.Count; i++)
        {
            if (i % columns == 0) tiles.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            tiles.Children.Add(Total(s, totals[i]).Col(i % columns * 2).Row(i / columns).Also(c => c.Margin = new Thickness(0, 0, 0, 12)));
        }
        var dated = expenses.Where(x => x.First?.Start is not null).OrderBy(x => x.First!.Value.Start!.Value.ToInstant()).ToArray();
        var undated = expenses.Except(dated).OrderBy(x => x.Expense.Title, StringComparer.CurrentCulture).ToArray();
        return Frame(s, str["Costs"], tiles,
            dated.Length == 0 ? null : Group(str["ByDate"], dated.Select(x => Expense(s, x.Expense, x.First!.Value.Start!.Value.Date)).ToArray()),
            undated.Length == 0 ? null : Group(str["WithoutDay"], undated.Select(x => Expense(s, x.Expense, null)).ToArray()),
            Ui.Columns("Auto,*", Ui.Icon("info", 13, "Text.Subtle").Also(i => i.VerticalAlignment = VerticalAlignment.Top).Margin(0, 2, 6, 0), Ui.Text(str["NoAutoConversion"], "caption")).Margin(20, 0));
    }

    /// <summary>One currency: what is spent so far, what is still estimated, and how much of the budget is left.</summary>
    private static Control Total(MobileSession s, CurrencyTotal total)
    {
        var str = s.Strings;
        var body = Ui.V(6, Ui.Text(total.Currency, "overline"), Ui.Text(Formats.Money(total.Total, total.Currency), "h2"));
        if (total.Budget > 0)
        {
            var ratio = (double)Math.Min(1m, total.Total / total.Budget);
            // Two star columns split the track, so the bar fits any card width.
            var bar = new Grid { ColumnDefinitions = new ColumnDefinitions($"{ratio.ToString(CultureInfo.InvariantCulture)}*,{(1 - ratio).ToString(CultureInfo.InvariantCulture)}*"), Height = 6 };
            bar.Children.Add(new Border { CornerRadius = new CornerRadius(3) }.Res(Border.BackgroundProperty, "Bg.Muted").Col(0, 2));
            bar.Children.Add(new Border { CornerRadius = new CornerRadius(3) }.Res(Border.BackgroundProperty, total.Total > total.Budget ? "Danger" : ratio > 0.85 ? "Warning" : "Accent"));
            AutomationProperties.SetName(bar, string.Format(CultureInfo.CurrentCulture, str["OfBudget"], Formats.Money(total.Budget, total.Currency)));
            body.Children.Add(bar);
            body.Children.Add(Ui.Text(total.Total <= total.Budget ? string.Format(CultureInfo.CurrentCulture, str["BudgetLeft"], Formats.Money(total.Budget - total.Total, total.Currency))
                : string.Format(CultureInfo.CurrentCulture, str["BudgetOver"], Formats.Money(total.Total - total.Budget, total.Currency)), "caption"));
        }
        if (total.Estimated > 0) body.Children.Add(Ui.Text(string.Format(CultureInfo.CurrentCulture, str["EstimatedAmount"], Formats.Money(total.Estimated, total.Currency)), "caption"));
        return Ui.Card(body, 14);
    }

    private static Control Expense(MobileSession s, Entity expense, LocalDate? day)
    {
        var trip = s.Trip!; var str = s.Strings;
        var money = TripSummaries.Money(expense.Data["amount"]);
        var estimated = expense.Data["estimated"] is JsonValue flag && flag.TryGetValue<bool>(out var e) && e;
        var related = (expense.Data["related"] as JsonArray ?? []).Select(r => Guid.TryParse(r?["id"]?.ToString(), out var id) ? trip.Find(id) : null).OfType<Entity>().FirstOrDefault();
        var culture = CultureInfo.GetCultureInfo(str.Language);
        var date = day is { } d ? Ui.V(0, Ui.Text(d.Day.ToString(culture), "strong").Also(t => t.HorizontalAlignment = HorizontalAlignment.Center), Ui.Text(d.ToString("ddd", culture), "caption").Also(t => t.HorizontalAlignment = HorizontalAlignment.Center))
            : (Control)Ui.Icon("receipt", 20, "Text.Muted").Also(i => i.HorizontalAlignment = HorizontalAlignment.Center);
        var summary = Ui.Columns("40,*,Auto",
            date.Also(c => c.VerticalAlignment = VerticalAlignment.Center),
            Ui.V(2, Ui.Text(expense.Title, "strong"), related is null ? null : Ui.Text(related.Title, "caption")).Margin(12, 0, 8, 0),
            Ui.V(4, money is null ? null : Ui.Text(Formats.Money(money.Amount, money.Currency), "strong").Also(t => t.HorizontalAlignment = HorizontalAlignment.Right),
                estimated ? Ui.Pill(str["Estimated"], "clock", "Warning.Soft", "Warning").Also(p => p.HorizontalAlignment = HorizontalAlignment.Right) : null));
        return Unfolding(s, expense.Id, summary, () =>
        {
            var body = Ui.V(10);
            if (Names(trip, expense.Data["paid_by"]) is { Length: > 0 } payers) body.Children.Add(DetailView.Row("user", string.Format(CultureInfo.CurrentCulture, str["PaidBy"], payers), null));
            if (expense.Data["body"]?.ToString() is { Length: > 0 } notes) body.Children.Add(DetailView.Row("sticky-note", notes, null, s));
            var change = Ui.Button(str["Edit"], "pencil");
            change.Click += (_, _) => s.ShowEditor(expense.Id);
            var details = Ui.Button(str["ShowDetails"], "chevron-right", "ghost");
            details.Click += (_, _) => s.ShowDetails(related?.Id ?? expense.Id);
            body.Children.Add(Ui.H(8, change, details));
            return body;
        });
    }

    // Travellers

    private static Control Travellers(MobileSession s)
    {
        var trip = s.Trip!; var str = s.Strings;
        var people = trip.Entities.Values.Where(e => e.Type == "person").OrderBy(p => p.Title, StringComparer.CurrentCulture).ToArray();
        if (people.Length == 0) return Frame(s, str["Travellers"], Empty("users", str["NoTravellersTitle"], str["NoTravellersBody"]));
        return Frame(s, str["Travellers"], Group(string.Format(CultureInfo.CurrentCulture, str["CountPeople"], people.Length), people.Select(p =>
        {
            var email = p.Data["email"]?.ToString() ?? (p.Data["identities"]?["git"] as JsonArray)?.FirstOrDefault()?["email"]?.ToString();
            var items = TripSummaries.ReferencesTo(trip, p.Id).Count(e => e.Type is "schedule_item" or "booking");
            var detail = string.Join(" · ", new[] { email, items > 0 ? string.Format(CultureInfo.CurrentCulture, str["CountPlans"], items) : null }.Where(t => !string.IsNullOrEmpty(t)));
            return (Control)Ui.Columns("Auto,*", Ui.Avatar(p.Title, 40).Margin(0, 0, 14, 0), Ui.V(2, Ui.Text(p.Title, "strong"), detail.Length == 0 ? null : Ui.Text(detail, "caption")))
                .Also(g => { g.MinHeight = 64; g.Margin = new Thickness(16, 8); });
        }).ToArray()));
    }

    // Sync and versions

    private static Control Sync(MobileSession s)
    {
        var trip = s.Trip!; var str = s.Strings;
        var state = s.HasChanges
            ? Ui.Columns("Auto,*", Ui.Icon("pencil", 22, "Text.Muted").Margin(0, 0, 14, 0), Ui.V(2, Ui.Text(str["ChangesNotInVersion"], "strong"), Ui.Text(str["ChangesBecomeVersion"], "caption")))
            : Ui.Columns("Auto,*", Ui.Icon("circle-check", 22, "Success").Margin(0, 0, 14, 0), Ui.V(2, Ui.Text(str["AllSavedOnPhone"], "strong"),
                s.LastVersion is { } last ? Ui.Text(string.Format(CultureInfo.CurrentCulture, str["LastVersionAt"], Formats.Relative(last, str)), "caption") : null));
        var message = Draft(s, "version", new TextBox { PlaceholderText = str["VersionMessage"] });
        AutomationProperties.SetName(message, str["VersionMessage"]);
        var create = Ui.Button(str["CreateVersion"], "bookmark", "primary").Also(b => b.HorizontalAlignment = HorizontalAlignment.Stretch);
        var problem = Ui.Text("", "caption").Also(t => { t.IsVisible = false; t.Res(TextBlock.ForegroundProperty, "Danger"); });
        create.Click += async (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(message.Text)) { problem.Text = str["EnterVersionMessage"]; problem.IsVisible = true; return; }
            create.IsEnabled = false;
            try { await s.CreateVersionAsync(message.Text); }
            catch (DomainException ex) { problem.Text = str.Diagnostic(ex.Code); problem.IsVisible = true; create.IsEnabled = true; }
        };
        var versions = s.History.Take(12).Select(v =>
        {
            var author = TripSummaries.PersonForGitEmail(trip, v.Email)?.Title ?? v.Author;
            var when = DateTimeOffset.TryParse(v.Timestamp, CultureInfo.InvariantCulture, DateTimeStyles.None, out var t) ? Formats.Relative(t, str) : "";
            var auto = v.Message.Contains("\nTravelRepo-Action: auto-version", StringComparison.Ordinal);
            return (Control)Ui.Columns("Auto,*", Ui.Icon(auto ? "clock" : "bookmark", 18, "Text.Muted").Also(i => i.VerticalAlignment = VerticalAlignment.Top).Margin(0, 2, 12, 0),
                Ui.V(2, Ui.Text(v.Message.Split('\n')[0]), Ui.Text(string.Join(" · ", new[] { when, author }.Where(x => x.Length > 0)), "caption"))).Also(g => g.Margin = new Thickness(16, 10));
        }).ToArray();
        return Frame(s, str["SyncAndVersions"],
            Ui.Card(state, 16).Margin(16, 0),
            GitHubView.TripState(s).Margin(16, 0),
            Ui.V(8, Ui.Text(str["CreateVersion"], "overline").Margin(20, 0), Ui.Card(Ui.V(10, Ui.Text(str["CreateVersionHint"], "caption"), message, problem, create), 14).Margin(16, 0)),
            versions.Length == 0 ? null : Group(str["RecentVersions"], versions),
            Ui.V(8, Ui.Text("GitHub", "overline").Margin(20, 0), GitHubView.Account(s).Margin(16, 0)));
    }

    // Settings

    private static Control Settings(MobileSession s)
    {
        var str = s.Strings;
        var name = Draft(s, "name", new TextBox { Text = s.Settings.Name, PlaceholderText = str["YourName"] }); AutomationProperties.SetName(name, str["Name"]);
        var email = Draft(s, "email", new TextBox { Text = s.Settings.Email, PlaceholderText = "name@example.com" }); AutomationProperties.SetName(email, str["Email"]);
        var save = Ui.Button(str["Save"], "check", "primary").Also(b => b.HorizontalAlignment = HorizontalAlignment.Stretch);
        save.Click += async (_, _) => { s.Drafts.Remove("name"); s.Drafts.Remove("email"); await s.SaveSettingsAsync(name.Text, email.Text); };
        var version = typeof(MorePages).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .OfType<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion.Split('+')[0] ?? "";
        long size = 0;
        try { size = new DirectoryInfo(s.Host.DataRoot).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        Control Info(string icon, string title, string value) => Ui.Columns("Auto,*,Auto", Ui.Icon(icon, 20, "Text.Muted").Margin(0, 0, 16, 0), Ui.Text(title), Ui.Text(value, "caption"))
            .Also(g => { g.MinHeight = 52; g.Margin = new Thickness(16, 4); });
        return Frame(s, str["Settings"],
            Ui.V(8, Ui.Text(str["YouOnThisPhone"], "overline").Margin(20, 0),
                Ui.Card(Ui.V(12, Ui.Text(str["YouOnThisPhoneHint"], "caption"), Ui.Field(str["Name"], name), Ui.Field(str["Email"], email), save), 14).Margin(16, 0)),
            Group(str["App"], [
                Info("globe", str["Language"], CultureInfo.GetCultureInfo(str.Language).NativeName),
                Info("folder", str["StorageUsed"], Formats.Bytes(size)),
                Info("info", str["About"], version)]));
    }
}
