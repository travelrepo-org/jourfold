using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Automation;
using Avalonia.Layout;
using Jourfold.Application;
using TravelRepo.Core;
using TravelRepo.Repository;

namespace Jourfold.Mobile.Views;

/// <summary>
/// One item's details, top to bottom in the order the sheet reveals them: first what is needed at a glance (time,
/// place, booking reference, Navigate), then the operational context (notes, journey details, open tasks), then
/// everything else (people, booking, cost, files, status and editing).
/// </summary>
public static class DetailView
{
    public static Control Header(MobileSession s, Entity item, Action close)
    {
        var trip = s.Trip!; var kind = ScheduleCategories.Classify(trip, item); var key = Visuals.KindKey(kind);
        var label = s.Strings.Optional("kind." + kind);
        var shut = Ui.IconButton("x", s.Strings["Close"]); shut.Click += (_, _) => close();
        return Ui.Columns("Auto,*,Auto",
            Ui.Tile(Visuals.KindIcon(kind, ScheduleCategories.TransportType(item)), key, 40).Margin(0, 0, 12, 0),
            Ui.V(2, Ui.Text(item.Title, "h3").Copies(s), Ui.Text(label, "caption")),
            shut).Margin(16, 2, 8, 10);
    }

    public static Control Body(MobileSession s, Entity item)
    {
        var trip = s.Trip!; var str = s.Strings;
        var entry = s.Entry(item.Id);
        var page = Ui.V(14);

        // Glance
        if (entry is not null && TimeText.Local(entry, s.PhoneZone, str, s.Trip) is { Start.Length: > 0 } time)
        {
            var day = entry.Day is { } d ? Lines.DayShort(d, str) + " · " : "";
            var travel = Lines.TravelTime(entry, str) is { } length ? string.Format(CultureInfo.CurrentCulture, str["TravelTime"], length) : null;
            var other = string.Join(" · ", new[] { travel, time.Other }.OfType<string>());
            page.Children.Add(Row("clock", day + time.Primary + (time.Arrival is { } arrival ? " → " + arrival : ""), other.Length > 0 ? other : null, s));
        }
        if (item.Type == "schedule_item") foreach (var slot in PlaceEdits.Slots(item)) page.Children.Add(PlaceRow(s, item, slot));
        else if (Lines.Where(trip, item) is { Length: > 0 } where)
            page.Children.Add(Row("map-pin", where, (ScheduleQueries.PrimaryPlace(trip, item) ?? ScheduleQueries.Route(trip, item).To)?.Data["address"]?["formatted"]?.ToString(), s));
        var booking = Booking(trip, item);
        if (booking?.Data["reference"]?.ToString() is { Length: > 0 } reference) page.Children.Add(Copyable(s, "ticket", reference, booking.Title));
        if (Navigation.Button(s, item) is { } navigate) page.Children.Add(navigate.Also(b => b.HorizontalAlignment = HorizontalAlignment.Stretch));

        // Operational
        var transport = item.Data["components"]?["transport"];
        var service = string.Join(" ", new[] { transport?["carrier"]?.ToString(), transport?["flight_number"]?.ToString() }.Where(t => !string.IsNullOrWhiteSpace(t)));
        if (service.Length > 0 || transport?["seat"] is not null)
            page.Children.Add(Row(Visuals.TransportIcon(ScheduleCategories.TransportType(item)), service.Length > 0 ? service : str["Journey"], transport?["seat"]?.ToString() is { } seat ? string.Format(CultureInfo.CurrentCulture, str["SeatsAre"], seat) : null,
                // The flight or train number alone is what goes into a tracking app.
                s, transport?["flight_number"]?.ToString() is { Length: > 0 } number ? number : service.Length > 0 ? service : "", transport?["seat"]?.ToString()));
        var notes = ExportText.Notes(trip, item).Concat(new[] { item.Data["body"]?.ToString(), item.Data["description"]?.ToString() }.OfType<string>()).Where(n => n.Length > 0).Distinct().ToArray();
        if (notes.Length > 0) page.Children.Add(Section(str["Notes"], Ui.V(6, notes.Select(n => (Control)Ui.Text(n).Copies(s)).ToArray())));
        var tasks = Linked(trip, item, "task").Where(t => t.Data["status"]?.ToString() is not ("done" or "completed" or "cancelled")).ToArray();
        if (tasks.Length > 0) page.Children.Add(Section(str["OpenTasks"], Ui.V(4, tasks.Select(t => (Control)TaskRow(s, t)).ToArray())));

        // Expanded
        var people = ScheduleQueries.Participants(trip, item).Select(p => p.Title).Order(StringComparer.CurrentCulture).ToArray();
        if (people.Length > 0) page.Children.Add(Row("users", Lines.Names(people, s), null, s));
        if (booking is not null)
        {
            var price = TripSummaries.Money(booking.Data["price"]) is { } money ? Formats.Money(money.Amount, money.Currency) : null;
            page.Children.Add(Section(str["Booking"], Ui.Card(Ui.V(4, Ui.Text(booking.Title, "strong").Copies(s),
                Ui.Text(string.Join(" · ", new[] { Provider(booking), booking.Data["reference"]?.ToString(), str.Optional(booking.Data["status"]?.ToString() ?? ""), price }.Where(t => !string.IsNullOrWhiteSpace(t))), "caption")
                    .Copies(s, booking.Data["reference"]?.ToString() is { Length: > 0 } code ? code : null)), 12)));
        }
        var costs = Linked(trip, item, "expense").Concat(booking is null ? [] : Linked(trip, booking, "expense")).DistinctBy(e => e.Id).ToArray();
        foreach (var cost in costs)
            if (TripSummaries.Money(cost.Data["amount"]) is { } money) page.Children.Add(Row("wallet", Formats.Money(money.Amount, money.Currency), cost.Title, s));
        var files = TripReferences.From(item).Concat(booking is null ? [] : TripReferences.From(booking)).Select(r => trip.Find(r.Target)).OfType<Entity>().Where(e => e.Type == "document").DistinctBy(e => e.Id).ToArray();
        if (files.Length > 0) page.Children.Add(Section(str["Files"], Ui.V(0, files.Select(f => FileRow(s, f)).ToArray())));
        if (item.Type == "schedule_item") page.Children.Add(Section(str["Status"], StatusChooser(s, item)));
        var edit = Ui.Button(str["Edit"], "pencil"); edit.Click += (_, _) => s.ShowEditor(item.Id);
        var delete = Ui.Button(str["Delete"], "trash-2", "ghost").Also(b => b.Res(Button.ForegroundProperty, "Danger"));
        delete.Click += (_, _) => s.Confirm(string.Format(CultureInfo.CurrentCulture, str["DeleteItemQuestion"], item.Title), str["Delete"], () => s.DeleteAsync(item.Id));
        page.Children.Add(Ui.Columns("*,12,*", edit.Also(b => b.HorizontalAlignment = HorizontalAlignment.Stretch), null, delete.Also(b => b.HorizontalAlignment = HorizontalAlignment.Stretch).Col(2)));
        page.Margin = new Thickness(16, 0, 16, 32);
        return page;
    }

    /// <summary>The provider's name; the field may be a plain name or an object with one.</summary>
    public static string? Provider(Entity booking) => booking.Data["provider"] switch
    {
        System.Text.Json.Nodes.JsonObject provider => provider["name"]?.ToString(),
        { } value => value.ToString(),
        _ => null
    };

    /// <summary>The booking an item points to, if any.</summary>
    public static Entity? Booking(TripSnapshot trip, Entity item)
    {
        if (item.Type == "booking") return item;
        var reference = item.Data["components"]?["booking"]?["ref"] ?? item.Data["components"]?["accommodation"]?["booking"];
        return Guid.TryParse(reference?.ToString(), out var id) ? trip.Find(id) : null;
    }

    /// <summary>Entities of a type that point to the item, such as its tasks and expenses.</summary>
    public static IEnumerable<Entity> Linked(TripSnapshot trip, Entity item, string type) =>
        TripSummaries.ReferencesTo(trip, item.Id).Where(e => e.Type == type).OrderBy(e => e.Title, StringComparer.CurrentCulture);

    /// <summary>
    /// A line of the details with an icon and an optional second line. With a session, a long press copies the line:
    /// <paramref name="copy"/> and <paramref name="copyDetail"/> when given, otherwise the text as shown.
    /// </summary>
    public static Control Row(string icon, string text, string? detail, MobileSession? s = null, string? copy = null, string? copyDetail = null)
    {
        var main = Ui.Text(text); var second = detail is null ? null : Ui.Text(detail, "caption");
        if (s is not null) { main.Copies(s, copy); second?.Copies(s, copyDetail); }
        return Ui.Columns("Auto,*", Ui.Icon(icon, 18, "Text.Muted").Also(i => i.VerticalAlignment = VerticalAlignment.Top).Margin(0, 2, 12, 0), Ui.V(1, main, second));
    }

    /// <summary>
    /// A place of the item that opens the place, to change its name, address or position; for a journey its start and
    /// end. Without a place, a button adds one.
    /// </summary>
    private static Control PlaceRow(MobileSession s, Entity item, PlaceSlot slot)
    {
        var str = s.Strings; var place = PlaceEdits.Current(s.Trip!, item, slot);
        var label = slot switch { PlaceSlot.From => str["From"], PlaceSlot.To => str["To"], _ => null };
        if (place is null)
        {
            var add = Ui.Button(label is null ? str["PlaceMissing"] : label + ": " + str["PlaceMissing"], "map-pin", "ghost", "compact");
            add.Click += (_, _) => s.ShowPlace(null, item.Id, slot);
            return add.Also(b => b.HorizontalAlignment = HorizontalAlignment.Left);
        }
        var detail = Places.Address(place) ?? (Places.Location(place) is { } at ? PlaceEdits.Format(at.Latitude, at.Longitude) : str["NoPositionYet"]);
        var row = new Button
        {
            Content = Ui.Columns("Auto,*,Auto", Ui.Icon("map-pin", 18, "Text.Muted").Also(i => i.VerticalAlignment = VerticalAlignment.Top).Margin(0, 2, 12, 0),
                Ui.V(1, Ui.Text(label is null ? place.Title : label + ": " + place.Title), Ui.Text(detail, "caption")),
                Ui.Icon("pencil", 16, "Text.Subtle").Margin(8, 2, 0, 0).Also(i => i.VerticalAlignment = VerticalAlignment.Top))
        }.Classed("row");
        AutomationProperties.SetName(row, str["ChangePlace"] + ": " + place.Title);
        row.Click += (_, _) => s.ShowPlace(place.Id, item.Id, slot);
        return row;
    }

    /// <summary>A file of the item, such as the booking e-mail or a ticket, which opens in the viewer.</summary>
    private static Control FileRow(MobileSession s, Entity file)
    {
        var media = file.Data["media_type"]?.ToString() ?? "";
        var icon = TravelRepo.Documents.Mail.IsMail(media) ? "mail" : media == "application/pdf" ? "file-text" : media.StartsWith("image/", StringComparison.Ordinal) ? "image" : "file";
        var row = new Button { Content = Ui.Columns("Auto,*,Auto", Ui.Icon(icon, 18, "Text.Muted").Margin(0, 0, 12, 0), Ui.Text(file.Title), Ui.Icon("chevron-right", 18, "Text.Subtle")) }.Classed("row");
        Avalonia.Automation.AutomationProperties.SetName(row, file.Title);
        row.Click += (_, _) => s.ShowDocument(file);
        return row;
    }

    private static Control Section(string title, Control content) => Ui.V(6, Ui.Text(title, "overline"), content);

    /// <summary>A reference to read out at a counter, with a button that copies it.</summary>
    public static Control Copyable(MobileSession s, string icon, string value, string? detail)
    {
        var copy = Ui.Button(s.Strings["Copy"], "copy", "chip");
        copy.Click += async (_, _) => { if (TopLevel.GetTopLevel(copy)?.Clipboard is { } clipboard) await clipboard.SetTextAsync(value); copy.Content = Ui.H(6, Ui.Icon("check", 15), Ui.Text(s.Strings["Copied"])); };
        var row = Ui.Columns("Auto,*", Ui.Icon(icon, 18, "Text.Muted").Also(i => i.VerticalAlignment = VerticalAlignment.Top).Margin(0, 2, 12, 0), Ui.V(1, Ui.Text(value, "strong").Copies(s), detail is null ? null : Ui.Text(detail, "caption")));
        return Ui.Columns("*,Auto", row, copy);
    }

    /// <summary>A task with a box to tick it off.</summary>
    public static Control TaskRow(MobileSession s, Entity task)
    {
        var done = task.Data["status"]?.ToString() is "done" or "completed";
        var box = new CheckBox { IsChecked = done, Content = Ui.Text(task.Title).Also(t => { if (done) t.Classes.Add("muted"); }), MinHeight = 48 };
        box.IsCheckedChanged += async (_, _) => await s.SetTaskDoneAsync(task.Id, box.IsChecked == true);
        return box;
    }

    /// <summary>The six statuses as words with icons; the current one is pressed.</summary>
    private static Control StatusChooser(MobileSession s, Entity item)
    {
        var current = item.Data["status"]?.ToString() ?? "planned";
        var flow = Ui.Flow(8);
        foreach (var (status, icon) in new[] { ("idea", "lightbulb"), ("planned", "calendar"), ("reserved", "clock"), ("confirmed", "check"), ("completed", "check"), ("cancelled", "x") })
        {
            var chip = Ui.Choice(s.Strings[status], icon, status == current, "chip");
            chip.Click += async (_, _) => await s.SetStatusAsync(item.Id, status);
            flow.Children.Add(chip);
        }
        return flow;
    }
}
