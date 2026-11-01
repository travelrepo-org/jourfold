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
        "trip" => TripPage(s),
        "bookings" => Bookings(s),
        "tasks" => Tasks(s),
        "packing" => Packing(s),
        "converter" => Converter(s),
        "places" => Places(s),
        "files" => Files(s),
        "costs" => Costs(s, width),
        "travellers" => Travellers(s),
        "sync" => Sync(s),
        "merge" => Merge(s),
        "settings" => Settings(s),
        "about" => About(s),
        "license.jourfold" => Text(s, string.Format(CultureInfo.CurrentCulture, s.Strings["LicenseTitle"], "GPL-3.0-or-later"), "Jourfold.LICENSE"),
        "license.travelrepo" => Text(s, string.Format(CultureInfo.CurrentCulture, s.Strings["LicenseTitle"], "Apache-2.0"), "TravelRepo.LICENSE"),
        "notices" => Text(s, s.Strings["OpenSourceComponents"], "Jourfold.NOTICES"),
        _ => MoreView.Build(s, width)
    };

    /// <summary>A page with a way back to where it was opened from and a large title.</summary>
    private static Control Frame(MobileSession s, string title, params Control?[] body)
    {
        var back = Ui.Button(s.BackLabel, "chevron-left", "ghost").Also(b => { b.HorizontalAlignment = HorizontalAlignment.Left; b.Padding = new Thickness(8, 8, 12, 8); });
        back.Click += (_, _) => s.GoBack();
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
        var add = Ui.Button(str["AddBooking"], "plus", "soft").Also(b => b.HorizontalAlignment = HorizontalAlignment.Left).Margin(16, 0);
        ((Button)add).Click += (_, _) => s.ShowQuickAdd("booking");
        if (bookings.Length == 0) return Frame(s, str["Bookings"], Empty("ticket", str["NoBookingsTitle"], str["NoBookingsBody"]), add);
        var groups = bookings.GroupBy(x => x.Booking.Data["status"]?.ToString() ?? "planned")
            .OrderBy(g => g.Key switch { "confirmed" => 0, "reserved" => 1, "pending" => 2, "planned" => 2, "idea" => 3, _ => 4 });
        return Frame(s, str["Bookings"], groups.Select(g => Group(str.Optional(g.Key) is { Length: > 0 } label ? label : g.Key,
            g.Select(x => Booking(s, x.Booking, x.First)).ToArray())).Prepend(add).ToArray());
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
            var change = Ui.Button(str["Edit"], "pencil");
            change.Click += (_, _) => s.ShowEditor(booking.Id);
            var details = Ui.Button(str["ShowDetails"], "chevron-right", "ghost");
            details.Click += (_, _) => s.ShowDetails(booking.Id);
            body.Children.Add(Ui.Flow(8, change, details));
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

    /// <summary>
    /// The packing lists: what the user brings, to tick off; what others bring, to see; what nobody brings yet, to take
    /// on with a tap. A line at the end of each list adds an item, the user's own unless it says "@name" or "@all".
    /// </summary>
    private static Control Packing(MobileSession s)
    {
        var trip = s.Trip!; var str = s.Strings; var me = s.Me;
        var newList = Draft(s, "packing-list", new TextBox { PlaceholderText = str["NewPackingList"] }); AutomationProperties.SetName(newList, str["NewPackingList"]);
        var create = Ui.Button(str["NewPackingList"], "plus", "soft");
        create.Click += async (_, _) => { var title = newList.Text; s.Drafts.Remove("packing-list"); await s.AddPackingListAsync(title ?? ""); };
        var lists = PackingLists.All(trip);
        var parts = new List<Control?> { Ui.Text(str["PackingHint"], "muted").Also(t => t.TextWrapping = Avalonia.Media.TextWrapping.Wrap).Margin(20, 0) };
        if (me is null && trip.Entities.Values.Any(e => e.Type == "person"))
        {
            var who = Ui.Button(str["ChooseWhoIAm"], "user-check", "soft"); who.Click += (_, _) => s.OpenPage("travellers");
            parts.Add(Ui.Card(Ui.V(8, Ui.Text(str["PackingWhoAmI"], "caption").Also(t => t.TextWrapping = Avalonia.Media.TextWrapping.Wrap), who), 14).Margin(16, 0));
        }
        if (lists.Count == 0) parts.Add(Empty("backpack", str["NoPackingListsTitle"], str["NoPackingListsBody"]));
        foreach (var list in lists)
        {
            var rows = new List<Control>();
            Control Caption(PackingItem item) => Ui.Text(string.Join(" · ", new[] { item.Group, item.Quantity,
                item.Everyone ? str["EveryoneBringsOwn"] : item.Assignees.Count > 1 ? string.Join(", ", item.Assignees.Select(a => a.Title)) : null }.Where(t => !string.IsNullOrEmpty(t))), "caption");
            foreach (var item in list.Items.Where(i => me is not null && i.IsFor(me.Id)).OrderBy(i => i.Group, StringComparer.CurrentCulture))
            {
                var box = new CheckBox { IsChecked = item.IsPackedBy(me!.Id), Content = Ui.V(2, Ui.Text(item.Task.Title), Caption(item)), MinHeight = 52, Padding = new Thickness(12, 0, 0, 0), Margin = new Thickness(16, 2) };
                AutomationProperties.SetName(box, item.Task.Title);
                box.IsCheckedChanged += async (_, _) => await s.SetPackedAsync(item.Task.Id, box.IsChecked == true);
                rows.Add(box);
            }
            foreach (var item in list.Items.Where(i => me is null || !i.IsFor(me.Id)))
            {
                var who = item.Unassigned ? str["Unassigned"] : string.Join(", ", item.Assignees.Select(a => a.Title + (item.IsPackedBy(a.Id) ? " ✓" : "")));
                var row = Ui.Columns("Auto,*", Ui.Icon(item.Done ? "circle-check" : "circle", 18, item.Done ? "Success" : "Text.Muted").Margin(0, 0, 12, 0), Ui.V(2, Ui.Text(item.Task.Title), Ui.Text(who, "caption")))
                    .Also(g => { g.MinHeight = 48; g.Margin = new Thickness(18, 4); });
                rows.Add(row);
            }
            var add = Draft(s, "packing:" + list.Collection.Id, new TextBox { PlaceholderText = str["AddPackingItemPlaceholder"] }); AutomationProperties.SetName(add, str["AddPackingItem"]);
            var plus = Ui.IconButton("plus", str["AddPackingItem"]);
            async Task Add() { var text = add.Text; s.Drafts.Remove("packing:" + list.Collection.Id); if (!string.IsNullOrWhiteSpace(text)) await s.AddPackingItemAsync(list.Collection.Id, text); }
            plus.Click += async (_, _) => await Add();
            add.KeyDown += async (_, e) => { if (e.Key == Avalonia.Input.Key.Enter) { e.Handled = true; await Add(); } };
            rows.Add(Ui.Columns("*,Auto", add, plus).Margin(16, 6));
            parts.Add(Group(string.Format(CultureInfo.CurrentCulture, str["PackingListProgress"], list.Collection.Title, list.DoneCount, list.Items.Count), rows));
        }
        parts.Add(Ui.Columns("*,Auto", newList, create.Margin(8, 0, 0, 0)).Margin(16, 0));
        return Frame(s, str["Packing"], [.. parts]);
    }

    /// <summary>
    /// Converts an amount between currencies offline, with the reference rates fetched at the last sync. The trip's
    /// currencies come first; any of the central bank's can be typed. Fetching waits for the user's yes once.
    /// </summary>
    private static Control Converter(MobileSession s)
    {
        var str = s.Strings;
        if (!s.RatesOn)
        {
            var allow = Ui.Button(str["RatesAllow"], "globe", "primary").Also(b => b.HorizontalAlignment = HorizontalAlignment.Left);
            allow.Click += async (_, _) => await s.TurnOnRatesAsync();
            return Frame(s, str["Converter"], Ui.Card(Ui.V(10, Ui.Text(str["RatesTitle"], "strong"), Ui.Text(str["RatesBody"], "caption").Also(t => t.TextWrapping = Avalonia.Media.TextWrapping.Wrap), allow), 14).Margin(16, 0));
        }
        if (s.Rates is not { } rates) return Frame(s, str["Converter"], Ui.Text(s.FetchingRates ? str["RatesLoading"] : str["RatesNone"], "muted").Margin(20, 0));
        var currencies = s.TripCurrencies().Where(c => rates.Currencies.Contains(c)).ToArray();
        string Pick(string key, string fallback) => s.Drafts.GetValueOrDefault(key) is { Length: 3 } c && rates.Currencies.Contains(c) ? c : fallback;
        var from = Pick("converter.from", currencies.FirstOrDefault(c => c != "EUR") ?? "USD"); var to = Pick("converter.to", "EUR");
        var amount = Draft(s, "converter.amount", new TextBox { Text = s.Drafts.GetValueOrDefault("converter.amount") ?? "100", FontSize = 28 });
        AutomationProperties.SetName(amount, str["Amount"]);
        var result = Ui.Text("", "h1").Also(t => t.FontSize = 32);
        void Show()
        {
            var text = amount.Text?.Replace(',', '.') ?? "";
            result.Text = decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var value) && rates.Convert(value, from, to) is { } converted
                ? Formats.Money(Math.Round(converted, 2), to) : "–";
        }
        amount.TextChanged += (_, _) => Show();
        Control Chips(string key, string chosen)
        {
            var flow = Ui.Flow(6);
            foreach (var code in currencies.Append(chosen).Distinct())
            {
                var chip = Ui.Choice(code, null, code == chosen, "chip"); var c = code;
                chip.Click += (_, _) => { s.Drafts[key] = c; s.Redraw(); };
                flow.Children.Add(chip);
            }
            var other = new AutoCompleteBox { ItemsSource = rates.Currencies.ToArray(), PlaceholderText = str["OtherCurrency"], MinimumPrefixLength = 1, Width = 110, FilterMode = AutoCompleteFilterMode.StartsWith };
            other.SelectionChanged += (_, _) => { if (other.SelectedItem is string c && c.Length == 3) { s.Drafts[key] = c; s.Redraw(); } };
            flow.Children.Add(other);
            return flow;
        }
        var swap = Ui.Button(str["SwapCurrencies"], "refresh-cw", "ghost");
        swap.Click += (_, _) => { s.Drafts["converter.from"] = to; s.Drafts["converter.to"] = from; s.Redraw(); };
        Show();
        return Frame(s, str["Converter"],
            Ui.Card(Ui.V(12, Ui.Text(from, "overline"), amount, Chips("converter.from", from), swap.Also(b => b.HorizontalAlignment = HorizontalAlignment.Left),
                Ui.Text(to, "overline"), result, Chips("converter.to", to)), 16).Margin(16, 0),
            Ui.Text(string.Format(CultureInfo.CurrentCulture, str["RatesFrom"], rates.Date.ToString("d MMM yyyy", CultureInfo.GetCultureInfo(str.Language))), "caption").Also(t => t.TextWrapping = Avalonia.Media.TextWrapping.Wrap).Margin(20, 0));
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
        var me = s.Me;
        // A tap says who the user is; it decides which plans remind them.
        var rows = people.Select(p =>
        {
            var email = p.Data["email"]?.ToString() ?? (p.Data["identities"]?["git"] as JsonArray)?.FirstOrDefault()?["email"]?.ToString();
            var items = TripSummaries.ReferencesTo(trip, p.Id).Count(e => e.Type is "schedule_item" or "booking");
            var detail = string.Join(" · ", new[] { email, items > 0 ? string.Format(CultureInfo.CurrentCulture, str["CountPlans"], items) : null }.Where(t => !string.IsNullOrEmpty(t)));
            var isMe = me?.Id == p.Id;
            var row = new Button
            {
                Content = Ui.Columns("Auto,*,Auto", Ui.Avatar(p.Title, 40).Margin(0, 0, 14, 0), Ui.V(2, Ui.Text(p.Title, "strong"), detail.Length == 0 ? null : Ui.Text(detail, "caption")),
                    isMe ? new Border { Child = Ui.Text(str["MeBadge"], "small"), Padding = new Thickness(8, 2), CornerRadius = new CornerRadius(8) }.Res(Border.BackgroundProperty, "Accent.Soft") : null),
                MinHeight = 64,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
            }.Classed("row");
            AutomationProperties.SetName(row, p.Title + (isMe ? ", " + str["MeBadge"] : ""));
            row.Click += (_, _) => s.ShowMenu(new ActionMenu(p.Title, null, [isMe
                ? new MenuAction(str["NotMe"], "x", _ => s.SetMeAsync(null))
                : new MenuAction(str["ThisIsMe"], "user-check", _ => s.SetMeAsync(p.Id))]));
            return (Control)row;
        }).ToArray();
        var notInTrip = Ui.Button(str["NotPartOfTrip"], "x", "ghost").Also(b => b.HorizontalAlignment = HorizontalAlignment.Left);
        notInTrip.Click += async (_, _) => await s.SetMeAsync(null);
        return Frame(s, str["Travellers"],
            Ui.Text(me is null ? str["WhoAmIPhone"] : string.Format(CultureInfo.CurrentCulture, str["YouAreNow"], me.Title), "muted").Also(t => t.TextWrapping = Avalonia.Media.TextWrapping.Wrap).Margin(20, 0),
            Group(string.Format(CultureInfo.CurrentCulture, str["CountPeople"], people.Length), rows),
            me is null ? null : notInTrip.Margin(12, 0),
            AddPerson(s));
    }

    /// <summary>A name and a button that adds the person to the trip, to choose them for plans, costs and tasks.</summary>
    private static Control AddPerson(MobileSession s)
    {
        var str = s.Strings;
        var name = Draft(s, "person", new TextBox { PlaceholderText = str["NameOfPerson"] }); AutomationProperties.SetName(name, str["NameOfPerson"]);
        var add = Ui.Button(str["AddPerson"], "user-plus");
        var problem = Ui.Text("", "caption").Also(t => { t.IsVisible = false; t.Res(TextBlock.ForegroundProperty, "Danger"); });
        add.Click += async (_, _) =>
        {
            try { await s.AddPersonAsync(name.Text ?? ""); s.Drafts.Remove("person"); name.Text = ""; }
            catch (DomainException ex) { problem.Text = ex.Message; problem.IsVisible = true; }
        };
        return Ui.V(8, Ui.Text(str["AddPerson"], "overline").Margin(20, 0), Ui.Card(Ui.V(10, Ui.Columns("*,8,Auto", name, null, add.Col(2)), problem), 14).Margin(16, 0));
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

    /// <summary>
    /// The things both sides changed, each with this device's and GitHub's version to choose from, and "keep both" for
    /// an item. Everything else was combined already. Choosing all sends the combined trip.
    /// </summary>
    private static Control Merge(MobileSession s)
    {
        var str = s.Strings;
        if (s.PendingMerge is not { } pending || s.Trip is not { } trip) return Frame(s, str["MergeTitle"], Ui.Text(str["NothingToChoose"], "muted").Margin(20, 0));
        var answers = new string?[pending.Plan.Conflicts.Count];
        var apply = Ui.Button(str["MergeApply"], "check", "primary").Also(b => { b.HorizontalAlignment = HorizontalAlignment.Stretch; b.IsEnabled = false; });
        var cards = Ui.V(12);
        for (var i = 0; i < pending.Plan.Conflicts.Count; i++)
        {
            var conflict = pending.Plan.Conflicts[i]; var index = i;
            var name = trip.Find(conflict.Entity)?.Title ?? pending.Incoming.Find(conflict.Entity)?.Title ?? str["Files"];
            string Side(System.Text.Json.Nodes.JsonNode? value, TripSnapshot from) => conflict.Kind == TravelRepo.Merge.ConflictKind.Binary ? str["BinaryConflict"] : MergeText.Value(value, from, str);
            var choices = Ui.V(6);
            var buttons = new List<ChoiceButton>();
            foreach (var action in Jourfold.Application.MergeAnswers.Actions(conflict, pending.Incoming, canEdit: false))
            {
                var (label, value) = action switch
                {
                    Jourfold.Application.MergeAnswers.UseCurrent => (str["MergeThisDevice"], Side(conflict.Current, trip)),
                    Jourfold.Application.MergeAnswers.UseOther => (str["MergeOther"], Side(conflict.Incoming, pending.Incoming)),
                    _ => (str["MergeKeepBoth"], str["MergeKeepBothHint"]),
                };
                var choice = new ChoiceButton { Content = Ui.V(2, Ui.Text(label, "strong"), Ui.Text(value, "caption").Also(t => t.TextWrapping = Avalonia.Media.TextWrapping.Wrap)), HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left }.Classed("row");
                AutomationProperties.SetName(choice, label + ": " + value);
                var chosen = action;
                choice.Click += (_, _) =>
                {
                    answers[index] = chosen;
                    foreach (var b in buttons) b.IsSelected = b == choice;
                    apply.IsEnabled = answers.All(a => a is not null);
                };
                buttons.Add(choice); choices.Children.Add(choice);
            }
            cards.Children.Add(Ui.Card(Ui.V(10, Ui.V(2, Ui.Text(name, "strong"), Ui.Text(MergeText.Field(conflict.Path, str), "caption")), choices), 14));
        }
        apply.Click += async (_, _) => { apply.IsEnabled = false; await s.ResolveMergeAsync(answers.Select(a => a!).ToArray()); };
        return Frame(s, str["MergeTitle"],
            Ui.Text(str["MergeHint"], "muted").Also(t => t.TextWrapping = Avalonia.Media.TextWrapping.Wrap).Margin(20, 0),
            cards.Margin(16, 0), apply.Margin(16, 0),
            s.SyncProblem is { } problem && !s.Syncing ? Ui.Text(problem, "caption").Also(t => { t.TextWrapping = Avalonia.Media.TextWrapping.Wrap; t.Res(TextBlock.ForegroundProperty, "Danger"); }).Margin(20, 0) : null);
    }

    /// <summary>Reminders on the trip: on or off, on time to the minute, and the kinds in the system settings.</summary>
    private static Control? Notices(MobileSession s)
    {
        if (s.Host.Notices is not { } notices) return null;
        var str = s.Strings;
        var show = new ToggleSwitch { IsChecked = s.NoticesOn, OnContent = str["NoticesShow"], OffContent = str["NoticesShow"] }.Also(t => AutomationProperties.SetName(t, str["NoticesShow"]));
        show.IsCheckedChanged += async (_, _) => await s.SetNoticesAsync(show.IsChecked == true);
        Control Hint(string text) => Ui.Text(text, "caption").Also(t => t.TextWrapping = Avalonia.Media.TextWrapping.Wrap);
        var body = Ui.V(12, show, Hint(str["NoticesHint"]));
        if (s.NoticesOn)
        {
            if (!notices.OnTime)
            {
                var exact = Ui.Button(str["NoticesExact"], "clock", "soft").Also(b => b.HorizontalAlignment = HorizontalAlignment.Stretch);
                exact.Click += (_, _) => notices.AskOnTime();
                body.Children.Add(Ui.V(6, exact, Hint(str["NoticesExactHint"])));
            }
            var kinds = Ui.Button(str["NoticesKinds"], "settings", "ghost").Also(b => b.HorizontalAlignment = HorizontalAlignment.Left);
            kinds.Click += (_, _) => notices.OpenSettings();
            body.Children.Add(kinds);
        }
        if (s.NoticesProblem is { } problem) body.Children.Add(Ui.Text(problem, "caption").Also(t => { t.TextWrapping = Avalonia.Media.TextWrapping.Wrap; t.Res(TextBlock.ForegroundProperty, "Danger"); }));
        return Ui.V(8, Ui.Text(str["NoticesSection"], "overline").Margin(20, 0), Ui.Card(body, 14).Margin(16, 0));
    }

    /// <summary>The trips in the calendar app: on or off, one calendar per trip or per kind. Only where the platform has it.</summary>
    private static Control? Calendar(MobileSession s)
    {
        if (s.Host.Calendar is null) return null;
        var str = s.Strings;
        ToggleSwitch Switch(string label, bool on, bool enabled) => new ToggleSwitch { IsChecked = on, IsEnabled = enabled, OnContent = label, OffContent = label }.Also(t => AutomationProperties.SetName(t, label));
        var show = Switch(str["CalendarShow"], s.CalendarOn, true);
        var byKind = Switch(str["CalendarByKind"], s.CalendarByKind, s.CalendarOn);
        show.IsCheckedChanged += async (_, _) => await s.SetCalendarAsync(show.IsChecked == true, byKind.IsChecked == true);
        byKind.IsCheckedChanged += async (_, _) => await s.SetCalendarAsync(s.CalendarOn, byKind.IsChecked == true);
        Control Hint(string text) => Ui.Text(text, "caption").Also(t => t.TextWrapping = Avalonia.Media.TextWrapping.Wrap);
        return Ui.V(8, Ui.Text(str["CalendarSection"], "overline").Margin(20, 0),
            Ui.Card(Ui.V(12, show, Hint(str["CalendarHint"]), byKind, Hint(str["CalendarByKindHint"]),
                s.CalendarOn ? Hint(str["CalendarSyncHint"]) : null,
                s.CalendarProblem is { } problem ? Ui.Text(problem, "caption").Also(t => { t.TextWrapping = Avalonia.Media.TextWrapping.Wrap; t.Res(TextBlock.ForegroundProperty, "Danger"); }) : null), 14).Margin(16, 0));
    }

    /// <summary>
    /// The trip's title and dates. Without dates, the trip runs from its first to its last plan; dates of their own
    /// keep empty days at the start or end, such as a day to arrive.
    /// </summary>
    private static Control TripPage(MobileSession s)
    {
        var trip = s.Trip!; var str = s.Strings;
        var title = Draft(s, "trip.title", new TextBox { Text = trip.Manifest.Title }); AutomationProperties.SetName(title, str["Title"]);
        DatePicker Day(string key)
        {
            var picker = new DatePicker { HorizontalAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Center };
            if (trip.Manifest.Data["dates"]?[key]?.ToString() is { } text && LocalDatePattern.Iso.Parse(text) is { Success: true } day)
                picker.SelectedDate = new DateTimeOffset(day.Value.ToDateTimeUnspecified(), TimeSpan.Zero);
            AutomationProperties.SetName(picker, str[key == "start" ? "TripStart" : "TripEnd"]);
            return picker;
        }
        var start = Day("start"); var end = Day("end");
        Control Clearable(DatePicker picker)
        {
            var clear = Ui.IconButton("x", str["FromThePlans"]); clear.Click += (_, _) => picker.SelectedDate = null;
            return Ui.Columns("*,Auto", picker, clear);
        }
        var hint = Ui.Text(str["TripDatesHint"], "caption").Also(t => t.TextWrapping = Avalonia.Media.TextWrapping.Wrap);
        var problem = Ui.Text("", "caption").Also(t => { t.IsVisible = false; t.Res(TextBlock.ForegroundProperty, "Danger"); });
        var save = Ui.Button(str["Save"], "check", "primary").Also(b => b.HorizontalAlignment = HorizontalAlignment.Stretch);
        static LocalDate? Date(DatePicker p) => p.SelectedDate is { } d ? LocalDate.FromDateTime(d.Date) : null;
        save.Click += async (_, _) =>
        {
            problem.IsVisible = false;
            try { await s.SaveTripAsync(title.Text ?? "", Date(start), Date(end)); s.Drafts.Remove("trip.title"); s.GoBack(); }
            catch (DomainException ex) { problem.Text = ex.Message; problem.IsVisible = true; }
        };
        return Frame(s, str["TripDetails"],
            Ui.Card(Ui.V(12, Ui.Field(str["Title"], title), Ui.Field(str["TripStart"], Clearable(start)), Ui.Field(str["TripEnd"], Clearable(end)), hint, problem, save), 14).Margin(16, 0));
    }

    private static Control Settings(MobileSession s)
    {
        var str = s.Strings;
        var name = Draft(s, "name", new TextBox { Text = s.Settings.Name, PlaceholderText = str["YourName"] }); AutomationProperties.SetName(name, str["Name"]);
        var email = Draft(s, "email", new TextBox { Text = s.Settings.Email, PlaceholderText = "name@example.com" }); AutomationProperties.SetName(email, str["Email"]);
        var save = Ui.Button(str["Save"], "check", "primary").Also(b => b.HorizontalAlignment = HorizontalAlignment.Stretch);
        save.Click += async (_, _) => { s.Drafts.Remove("name"); s.Drafts.Remove("email"); await s.SaveSettingsAsync(name.Text, email.Text); };
        long size = 0;
        try { size = new DirectoryInfo(s.Host.DataRoot).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        Control Info(string icon, string title, string value) => Ui.Columns("Auto,*,Auto", Ui.Icon(icon, 20, "Text.Muted").Margin(0, 0, 16, 0), Ui.Text(title), Ui.Text(value, "caption"))
            .Also(g => { g.MinHeight = 52; g.Margin = new Thickness(16, 4); });
        var about = new Button { Content = Ui.Columns("Auto,*,Auto,Auto", Ui.Icon("info", 20, "Text.Muted").Margin(0, 0, 16, 0), Ui.Text(str["About"]), Ui.Text(AppVersion(s), "caption").Margin(0, 0, 8, 0), Ui.Icon("chevron-right", 18, "Text.Subtle")).Also(g => g.MinHeight = 44) }.Classed("row");
        AutomationProperties.SetName(about, str["About"]);
        about.Click += (_, _) => s.OpenPage("about");
        return Frame(s, str["Settings"],
            Ui.V(8, Ui.Text(str["YouOnThisPhone"], "overline").Margin(20, 0),
                Ui.Card(Ui.V(12, Ui.Text(str["YouOnThisPhoneHint"], "caption"), Ui.Field(str["Name"], name), Ui.Field(str["Email"], email), save), 14).Margin(16, 0)),
            Notices(s),
            Ui.V(8, Ui.Text(str["Weather"], "overline").Margin(20, 0), Ui.Card(new ToggleSwitch { IsChecked = s.WeatherOn, OnContent = str["WeatherTurnOn"], OffContent = str["WeatherTurnOn"] }
                .Also(t => { AutomationProperties.SetName(t, str["WeatherTurnOn"]); t.IsCheckedChanged += async (_, _) => await s.SetWeatherAsync(t.IsChecked == true); }), 14).Margin(16, 0)),
            Calendar(s),
            Group(str["App"], [
                Info("globe", str["Language"], CultureInfo.GetCultureInfo(str.Language).NativeName),
                Info("folder", str["StorageUsed"], Formats.Bytes(size)),
                about]));
    }

    /// <summary>The installed app's version, such as a preview's; the assembly's where the platform does not say.</summary>
    private static string AppVersion(MobileSession s) => s.Host.AppVersion ?? typeof(MorePages).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
        .OfType<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion.Split('+')[0] ?? "";

    /// <summary>
    /// About Jourfold, as on the desktop: versions, the licenses of Jourfold and TravelRepo, where the source is, and the
    /// open-source parts the app is built from.
    /// </summary>
    private static Control About(MobileSession s)
    {
        var str = s.Strings;
        var mark = new Image { Width = 56, Height = 56, VerticalAlignment = VerticalAlignment.Center };
        mark.Bind(Image.SourceProperty, mark.GetResourceObservable("Brand.Mark"));
        var header = Ui.Columns("Auto,*", mark, Ui.V(2, Ui.Text("Jourfold", "h2"), Ui.Text(string.Format(CultureInfo.CurrentCulture, str["AboutVersion"], AppVersion(s)), "muted").Copies(s), Ui.Text(str["WelcomeTitle"], "caption").Also(t => t.TextWrapping = Avalonia.Media.TextWrapping.Wrap))
            .Also(v => { v.Margin = new Thickness(16, 0, 0, 0); v.VerticalAlignment = VerticalAlignment.Center; })).Margin(20, 0);
        Button Link(string key, Uri uri)
        {
            var b = Ui.Button(str[key], "external-link", "ghost", "compact");
            b.Click += async (_, _) => { if (TopLevel.GetTopLevel(b)?.Launcher is { } launcher) await launcher.LaunchUriAsync(uri); };
            return b;
        }
        Button Read(string key, string page) { var b = Ui.Button(str[key], "file-text", "ghost", "compact"); b.Click += (_, _) => s.OpenPage(page); return b; }
        Control Wrap(string text, string style) => Ui.Text(text, style).Also(t => t.TextWrapping = Avalonia.Media.TextWrapping.Wrap);
        Control Card(string title, string? detail, string body, string? meta, params Control?[] actions) => Ui.Card(Ui.V(6,
            Ui.Text(title, "strong"), detail is null ? null : Ui.Text(detail, "caption"), Wrap(body, "muted"), meta is null ? null : Ui.Text(meta, "caption"),
            Ui.Flow(4, actions).Margin(-8, 2, 0, 0)), 14).Margin(16, 0);
        var jourfold = typeof(MorePages).Assembly;
        Uri? Meta(string key) => TravelRepoInfo.Metadata(jourfold, key);
        return Frame(s, str["About"], header,
            Card("Jourfold", null, str["AboutJourfoldLicense"], null, Meta("WebsiteUrl") is { } site ? Link("Website", site) : null, Read("ViewLicense", "license.jourfold"), Meta("RepositoryUrl") is { } source ? Link("SourceCode", source) : null),
            Card("TravelRepo", string.Format(CultureInfo.CurrentCulture, str["AboutTravelRepoVersion"], TravelRepoInfo.Version, TravelRepoInfo.FormatVersion), str["AboutTravelRepo"], str["AboutTravelRepoLicense"],
                Read("ViewLicense", "license.travelrepo"), TravelRepoInfo.RepositoryUrl is { } repo ? Link("SourceCode", repo) : null),
            Card(str["OpenSourceComponents"], null, str["OpenSourceComponentsHint"], null, Read("ViewNotices", "notices")),
            Wrap(System.Runtime.InteropServices.RuntimeInformation.OSDescription.Trim() + ", .NET " + Environment.Version, "caption").Margin(20, 0));
    }

    /// <summary>A license or notice text that comes with the app, to read and copy.</summary>
    private static Control Text(MobileSession s, string title, string resource)
    {
        using var stream = typeof(MorePages).Assembly.GetManifestResourceStream(resource);
        var text = stream is null ? "" : new StreamReader(stream).ReadToEnd();
        return Frame(s, title, new SelectableTextBlock { Text = text, TextWrapping = Avalonia.Media.TextWrapping.Wrap, FontFamily = Avalonia.Media.FontFamily.Parse("monospace"), FontSize = 12, Margin = new Thickness(20, 0) });
    }
}
