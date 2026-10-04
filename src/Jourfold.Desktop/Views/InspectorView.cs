using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using NodaTime;
using NodaTime.Text;
using TravelRepo.Core;

namespace Jourfold.Desktop;

/// <summary>The detail inspector. Fields autosave as undoable edits; complex values open focused editors.</summary>
public static class InspectorView
{
    private static string tab = "Details";

    public static Control Build(MainWindow w, Entity e)
    {
        var m = w.Model; var s = m.Strings; var trip = m.Trip!;
        var (icon, kind) = Visuals.For(trip, e);
        var titlePath = e.Type == "person" ? "display_name" : e.Type is "place" or "document" ? "name" : "title";
        var titleBox = (TextBox)Editors.Text(w, e.Id, titlePath, s["Title"], e.Type == "comment" ? s["comment"] : e.Title);
        titleBox.Classes.Add("inline"); titleBox.Classes.Add("heading"); titleBox.TextWrapping = TextWrapping.Wrap; titleBox.IsReadOnly = e.Type == "comment";
        var close = Ui.IconButton("x", s["CloseDetails"], "small"); close.Click += (_, _) => m.Selected = null;
        var more = Ui.IconButton("ellipsis", s["More"], "small"); more.Flyout = ItemMenu(w, e);
        var typeLabel = e.Type == "schedule_item" ? s["kind." + ScheduleCategories.Classify(trip, e)] : s[e.Type];
        var subtitle = Subtitle(m, e);
        var header = Ui.V(10, Ui.Columns("Auto,*,Auto,Auto", Ui.Tile(icon, kind, 40), Ui.V(0, Ui.Text(typeLabel.ToUpper(CultureInfo.CurrentCulture), "overline"), subtitle is null ? null : Ui.Line(subtitle, "caption")).Margin(12, 0, 4, 0), e.Type == "trip" ? null : more, close), titleBox.Margin(-6, 0, 0, 0));
        var tabs = Tabs(w, e);
        var top = Ui.V(12, header, tabs); top.Margin = new Thickness(20, 18, 16, 8);
        var body = new StackPanel { Name = "Inspector", Spacing = 18, Margin = new Thickness(20, 8, 20, 28) };
        var current = tabs is null ? "Details" : tab;
        if (current == "Notes" && SupportsNotes(e)) NotesTab(w, e, body);
        else if (current == "Comments" && e.Type != "comment") CommentsTab(w, e, body);
        else DetailsTab(w, e, body);
        var dock = new DockPanel(); DockPanel.SetDock(top, Dock.Top); dock.Children.Add(top);
        var scroll = new ScrollViewer { Content = body, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        dock.Children.Add(scroll);
        AutomationProperties.SetName(dock, s["Details"] + ": " + e.Title);
        return dock;
    }

    private static bool SupportsNotes(Entity e) => e.Type is "schedule_item" or "note" or "place" or "booking";

    private static Control? Tabs(MainWindow w, Entity e)
    {
        if (e.Type is "trip" or "comment" or "document") return null;
        var s = w.Model.Strings; var trip = w.Model.Trip!;
        var comments = trip.Entities.Values.Count(c => c.Type == "comment" && c.Data["target"]?["id"]?.ToString() == e.Id.ToString());
        var notes = (e.Data["content"] as JsonArray)?.Count ?? 0;
        var items = new List<(string, string, string?)> { ("Details", s["Details"], null) };
        if (SupportsNotes(e)) items.Add(("Notes", notes > 0 ? s["Notes"] + " · " + notes : s["Notes"], null));
        items.Add(("Comments", comments > 0 ? s["Comments"] + " · " + comments : s["Comments"], null));
        if (!items.Any(i => i.Item1 == tab)) tab = "Details";
        return ViewToolbar.Segmented(items, tab, key => { tab = key; w.RenderInspector(); });
    }

    private static string? Subtitle(MainViewModel m, Entity e)
    {
        var trip = m.Trip!; var s = m.Strings;
        if (e.Type == "schedule_item")
        {
            var span = ScheduleQueries.Span(trip, e); var zone = ScheduleQueries.TripZone(trip);
            if (!span.IsPlaced) return s["NotScheduledYet"];
            var a = span.Start!.Value.InZone(zone); var b = span.End!.Value.InZone(zone);
            return span.Precision switch
            {
                TimePrecision.AllDay => Formats.LongDay(a.Date),
                TimePrecision.DayPart => Formats.LongDay(a.Date) + " · " + s[span.DayPart ?? "day_part"],
                _ => Formats.LongDay(a.Date) + " · " + Formats.Time(a.TimeOfDay) + " – " + (b.Date != a.Date ? Formats.ShortDate(b.Date) + " " : "") + Formats.Time(b.TimeOfDay)
            };
        }
        if (e.Type == "trip") return Formats.DateRange(TripQueries.EffectiveDates(trip).Start, TripQueries.EffectiveDates(trip).End);
        if (e.Type == "place") return e.Data["address"]?["formatted"]?.ToString();
        return null;
    }

    private static MenuFlyout ItemMenu(MainWindow w, Entity e)
    {
        var m = w.Model; var s = m.Strings; var menu = new MenuFlyout();
        void Item(string text, string icon, Action action) { var i = new MenuItem { Header = text, Icon = Ui.Icon(icon, 15) }; i.Click += (_, _) => action(); menu.Items.Add(i); }
        if (e.Type == "schedule_item") Item(s["EditTime"], "clock", () => _ = m.ScheduleSelectedAsync());
        if (e.Type is not ("person" or "comment")) Item(s["Duplicate"], "copy", () => _ = m.DuplicateAsync(e.Id));
        if (e.Type == "schedule_item" && e.Data["time"] is not null) Item(s["MoveToInbox"], "inbox", () => _ = m.UpdateAsync(e.Id, x => { x.Data["time"] = null; }));
        Item(s["AddComment"], "message-square", () => { tab = "Comments"; w.RenderInspector(); });
        menu.Items.Add(new Separator());
        Item(s["Delete"], "trash-2", () => _ = m.DeleteAsync(e.Id));
        return menu;
    }

    private static void Add(StackPanel body, string title, Control? content, Control? action = null) { if (content is not null) body.Children.Add(Ui.Section(title, content, action)); }
    private static Control Field(string label, Control editor) => Ui.Field(label, editor);

    private static void DetailsTab(MainWindow w, Entity e, StackPanel body)
    {
        var m = w.Model; var s = m.Strings; var trip = m.Trip!;
        foreach (var d in m.Diagnostics.Where(d => d.Path.StartsWith(e.Id.ToString(), StringComparison.Ordinal) || e.Type == "trip" && d.Path is "travel.yaml" or "cover" or "dates"))
            body.Children.Add(new Border { Padding = new Thickness(12, 10), CornerRadius = new CornerRadius(10), Child = Ui.Columns("Auto,*", Ui.Icon(d.Severity == Severity.Error ? "circle-help" : "triangle-alert", 16, d.Severity == Severity.Error ? "Danger" : "Warning"), Ui.Text(m.Advanced ? d.Message : s.Diagnostic(d.Code)).Margin(10, 0, 0, 0)) }.Res(Border.BackgroundProperty, d.Severity == Severity.Error ? "Danger.Soft" : "Warning.Soft"));
        switch (e.Type)
        {
            case "schedule_item": ScheduleDetails(w, e, body); break;
            case "trip": TripDetails(w, e, body); break;
            case "document": DocumentDetails(w, e, body); break;
            case "note": body.Children.Add(Field(s["Body"], Editors.Text(w, e.Id, "body", s["Body"], e.Data["body"]?.ToString(), multiline: true, watermark: s["NoteWatermark"]))); if (e.Data["body"]?.ToString() is { Length: > 0 } text) body.Children.Add(Ui.Section(s["Preview"], Ui.Card(new MarkdownView(text), 14))); break;
            case "place": PlaceDetails(w, e, body); break;
            case "booking": BookingDetails(w, e, body); break;
            case "task":
                body.Children.Add(Field(s["Status"], Editors.StatusChips(w, e.Id, e.Data["status"]?.ToString(), ["open", "in_progress", "completed", "cancelled"])));
                body.Children.Add(Field(s["Due"], Editors.Date(w, e.Id, "due/date", s["Due"], e.Data["due"]?["date"]?.ToString())));
                body.Children.Add(Field(s["Assignees"], Editors.References(w, e.Id, "assigned_to", s["Assignees"], "person")));
                body.Children.Add(Field(s["Related"], Related(w, e)));
                break;
            case "expense" or "budget":
                body.Children.Add(Field(s["Amount"], Editors.Money(w, e.Id, "amount", s["Amount"], e.Data["amount"])));
                if (e.Type == "expense")
                {
                    body.Children.Add(Editors.Toggle(w, e.Id, "estimated", s["EstimatedToggle"], e.Data["estimated"] is JsonValue v && v.TryGetValue<bool>(out var est) && est));
                    body.Children.Add(Field(s["Payer"], Editors.References(w, e.Id, "paid_by", s["Payer"], "person")));
                    body.Children.Add(Field(s["Related"], Related(w, e)));
                    if (e.Data["converted"] is JsonObject converted) body.Children.Add(Field(s["Converted"], Ui.Text(converted["value"] + " " + converted["currency"] + " · " + string.Format(s["RateOn"], converted["rate"], converted["rate_date"]), "muted")));
                }
                else body.Children.Add(Field(s["BudgetScope"], Editors.Choice(w, e.Id, "scope", s["BudgetScope"], e.Data["scope"]?.ToString(), ["trip", "category", "participant", "collection"])));
                break;
            case "person": PersonDetails(w, e, body); break;
            case "collection": body.Children.Add(Field(s["Items"], Editors.References(w, e.Id, "items", s["Items"], null))); break;
            case "comment":
                body.Children.Add(new MarkdownView(e.Data["body"]?.ToString() ?? ""));
                body.Children.Add(Field(s["Body"], Editors.Text(w, e.Id, "body", s["Body"], e.Data["body"]?.ToString(), multiline: true)));
                if (Guid.TryParse(e.Data["target"]?["id"]?.ToString(), out var target) && trip.Find(target) is { } t) { var open = Ui.Button(t.Title, "arrow-up-right"); open.Click += (_, _) => m.Selected = t; body.Children.Add(Field(s["Target"], open)); }
                body.Children.Add(Field(s["Author"], Editors.Reference(w, e.Id, "author", s["Author"], "person", allowCreate: false)));
                break;
            default:
                foreach (var field in EditorModel.Fields(e).Skip(1)) body.Children.Add(Field(s[field.Label], Generic(w, e, field)));
                break;
        }
        if (e.Type != "trip" && e.Type != "comment")
        {
            var used = TripSummaries.ReferencesTo(trip, e.Id).Where(x => x.Type != "comment" && x.Type != "trip").Take(12).ToArray();
            if (used.Length > 0 && e.Type is not ("schedule_item")) Add(body, s["UsedInSection"], Ui.V(2, used.Select(u => (Control)Link(w, u)).ToArray()));
        }
        if (m.Advanced) body.Children.Add(new Expander { Header = Ui.Text(s["RawData"], "caption"), Content = new SelectableTextBlock { Text = e.Data.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }), FontFamily = FontFamily.Parse("monospace"), FontSize = 11, TextWrapping = TextWrapping.Wrap }, HorizontalAlignment = HorizontalAlignment.Stretch });
    }

    public static Button Link(MainWindow w, Entity target)
    {
        var trip = w.Model.Trip!; var (icon, kind) = Visuals.For(trip, target);
        var button = new Button { Content = Ui.Columns("Auto,*,Auto", Ui.Icon(icon, 14, "Kind." + kind + ".Fg"), Ui.Line(target.Title).Margin(8, 0), Ui.Icon("chevron-right", 13, "Text.Subtle")) }.Classed("row").Named(target.Title);
        button.Padding = new Thickness(8, 6); button.Click += (_, _) => w.Model.Selected = target; return button;
    }

    private static Control Related(MainWindow w, Entity e) => Editors.References(w, e.Id, "related", w.Model.Strings["Related"], null,
        read: x => new JsonArray((x.Data["related"] as JsonArray ?? []).Select(r => (JsonNode?)JsonValue.Create(r?["id"]?.ToString())).ToArray()),
        write: (x, ids) => x.Data["related"] = new JsonArray(ids.Select(id => Guid.TryParse(id?.ToString(), out var g) && w.Model.Trip!.Find(g) is { } t ? (JsonNode?)new JsonObject { ["type"] = t.Type, ["id"] = g.ToString() } : null).Where(n => n is not null).ToArray()));

    private static void ScheduleDetails(MainWindow w, Entity e, StackPanel body)
    {
        var m = w.Model; var s = m.Strings; var trip = m.Trip!; var span = ScheduleQueries.Span(trip, e);
        // When
        var whenText = Subtitle(m, e) ?? s["NotScheduledYet"];
        var precision = span.Precision is TimePrecision.Exact or TimePrecision.Unscheduled ? null : s[span.Precision switch { TimePrecision.Approximate => "approximate", TimePrecision.Window => "window", TimePrecision.DayPart => "day_part", _ => "all_day" }];
        var zoneNote = span.StartZone is { } z && z != ScheduleQueries.TripZone(trip).Id && span.Start is { } st ? string.Format(s["LocalTimeIn"], Formats.Time(st.InZone(DateTimeZoneProviders.Tzdb[z]).TimeOfDay), Formats.ZoneName(z)) : null;
        var endNote = span.EndZone is { } ez && ez != span.StartZone && span.End is { } en ? string.Format(s["ArrivesLocal"], Formats.Time(en.InZone(DateTimeZoneProviders.Tzdb[ez]).TimeOfDay), Formats.ZoneName(ez)) : null;
        var when = Editors.ValueButton(whenText, span.IsPlaced ? "clock" : "calendar-plus", s["Schedule"], () => m.ScheduleSelectedAsync(), "time");
        var whenBlock = Ui.V(6, when, precision is null && zoneNote is null && endNote is null ? null : Ui.H(10, precision is null ? null : Ui.Pill(precision, "timer", "Bg.Subtle", "Text.Muted"), Ui.Text(string.Join(" · ", new[] { zoneNote, endNote }.Where(x => x is not null)), "caption")));
        if (span.Derived) whenBlock.Children.Add(Ui.Text(s["DerivedFromItems"], "caption"));
        if (!span.IsPlaced) whenBlock.Children.Add(Field(s["DurationMinutes"], Editors.Number(w, e.Id, "duration", s["DurationMinutes"], ScheduleQueries.PlannedDuration(e)?.TotalMinutes, s["MinutesShort"], v => JsonValue.Create(PeriodPattern.NormalizingIso.Format(Period.FromMinutes((long)v)))!, 15)));
        Add(body, s["When"], whenBlock);
        Add(body, s["Status"], Editors.StatusChips(w, e.Id, e.Data["status"]?.ToString(), ScheduleCategories.Statuses));

        var transport = e.Data["components"]?["transport"]; var stay = e.Data["components"]?["accommodation"];
        if (transport is JsonObject)
        {
            var modes = new WrapPanel { ItemSpacing = 6, LineSpacing = 6 };
            foreach (var mode in ScheduleCategories.TransportTypes)
            {
                var chip = Ui.Button(s[mode], Visuals.TransportIcon(mode), "chip"); if (transport["type"]?.ToString() == mode) chip.Classes.Add("selected");
                chip.Click += (_, _) => _ = m.UpdateAsync(e.Id, x => x.Data["components"]!["transport"]!["type"] = mode); modes.Children.Add(chip);
            }
            var route = Ui.V(10, modes,
                Ui.Columns("*,Auto,*", Field(s["From"], Editors.Reference(w, e.Id, "components/transport/departure/place", s["From"], "place")), Ui.Icon("arrow-right", 16, "Text.Subtle").Margin(8, 22, 8, 0), Field(s["To"], Editors.Reference(w, e.Id, "components/transport/arrival/place", s["To"], "place"))),
                Ui.Columns("*,*", Field(s["Carrier"], Editors.Text(w, e.Id, "components/transport/carrier", s["Carrier"], transport["carrier"]?.ToString())), Field(s["Number"], Editors.Text(w, e.Id, "components/transport/flight_number", s["Number"], transport["flight_number"]?.ToString())).Margin(10, 0, 0, 0)),
                Ui.Columns("*,*,*", Field(s["Seat"], Editors.Text(w, e.Id, "components/transport/seat", s["Seat"], transport["seat"]?.ToString())), Field(s["Terminal"], Editors.Text(w, e.Id, "components/transport/departure/terminal", s["Terminal"], transport["departure"]?["terminal"]?.ToString())).Margin(8, 0), Field(s["Gate"], Editors.Text(w, e.Id, "components/transport/departure/gate", s["Gate"], transport["departure"]?["gate"]?.ToString()))),
                Field(s["Distance"] + " (" + (m.Store.Get("units") == "Imperial" ? "mi" : "km") + ")", Editors.Number(w, e.Id, "components/transport/distance_m", s["Distance"], transport["distance_m"] is JsonValue dist && double.TryParse(dist.ToString(), CultureInfo.InvariantCulture, out var meters) ? Math.Round(meters / (m.Store.Get("units") == "Imperial" ? 1609.344 : 1000), 2) : null, null, v => JsonValue.Create(Math.Round(v * (m.Store.Get("units") == "Imperial" ? 1609.344 : 1000)))!)));
            Add(body, s["Journey"], route);
        }
        else if (stay is JsonObject)
        {
            string Zoned(string key) => stay[key] is JsonObject z && LocalDateTimePattern.ExtendedIso.Parse(z["local"]?.ToString() ?? "").TryGetValue(default, out var local) ? Formats.LongDay(local.Date) + " · " + Formats.Time(local.TimeOfDay) : s["Choose"];
            var fields = EditorModel.Fields(e);
            var stayPanel = Ui.V(10,
                Field(s["Place"], Editors.Reference(w, e.Id, "components/accommodation/place", s["Place"], "place")),
                Ui.Columns("*,*", Field(s["CheckIn"], Editors.ValueButton(Zoned("check_in"), "log-out", s["CheckIn"], () => m.RunEditAsync(() => EditorModel.ComplexAsync(m, e.Id, fields.Single(f => f.Path.EndsWith("check_in", StringComparison.Ordinal)))))), Field(s["CheckOut"], Editors.ValueButton(Zoned("check_out"), "log-out", s["CheckOut"], () => m.RunEditAsync(() => EditorModel.ComplexAsync(m, e.Id, fields.Single(f => f.Path.EndsWith("check_out", StringComparison.Ordinal)))))).Margin(10, 0, 0, 0)),
                Field(s["Guests"], Editors.References(w, e.Id, "components/accommodation/guests", s["Guests"], "person")),
                Field(s["Rooms"], ListText(w, e, "components/accommodation/rooms", s["Rooms"])));
            Add(body, s["Stay"], stayPanel);
        }
        else
        {
            var categories = new WrapPanel { ItemSpacing = 6, LineSpacing = 6 };
            var inherited = TripQueries.Resolve(trip, e.Id, "category");
            var currentCategory = inherited.Value?.ToString();
            foreach (var category in ScheduleCategories.Standard.Where(c => c is not ("transport" or "accommodation" or "other")))
            {
                var chip = Ui.Button(s["category." + category], Visuals.CategoryIcon(category), "chip"); if ((currentCategory ?? "activity") == category) chip.Classes.Add("selected");
                chip.Click += (_, _) => _ = m.UpdateAsync(e.Id, x => { if (category == "activity") x.Data.Remove("category"); else x.Data["category"] = category; });
                categories.Children.Add(chip);
            }
            var categoryBlock = Ui.V(6, categories, inherited.Inherited ? Ui.Text(string.Format(s["InheritedFrom"], trip.Find(inherited.Source)?.Title), "caption") : null);
            Add(body, s["Category"], categoryBlock);
            Add(body, s["Where"], Editors.Reference(w, e.Id, "default_place", s["DefaultPlace"], "place"));
        }

        // Who
        var participants = TripQueries.Resolve(trip, e.Id, "participants");
        var who = Ui.V(6, Editors.References(w, e.Id, "participants/values", s["Participants"], "person",
            read: x => TripQueries.Resolve(trip, x.Id, "participants").Value as JsonArray,
            write: (x, ids) => x.Data["participants"] = new JsonObject { ["inherit"] = false, ["values"] = ids.DeepClone() }));
        if (participants.Inherited) who.Children.Add(Ui.Text(string.Format(s["InheritedFrom"], trip.Find(participants.Source)?.Title), "caption"));
        else if (TripQueries.Parent(trip, e.Id) is not null)
        {
            var reset = Ui.Button(s["UseParentPeople"], "rotate-ccw", "link"); reset.Click += (_, _) => _ = m.UpdateAsync(e.Id, x => x.Data["participants"] = new JsonObject { ["inherit"] = true, ["values"] = new JsonArray() }); who.Children.Add(reset);
        }
        Add(body, s["Who"], who);

        // Booking
        var booking = Guid.TryParse(e.Data["components"]?["booking"]?["ref"]?.ToString(), out var bookingId) ? trip.Find(bookingId) : null;
        Add(body, s["Booking"], Ui.V(6, Editors.Reference(w, e.Id, "components/booking/ref", s["Booking"], "booking"),
            booking is null ? null : Ui.H(8, booking.Data["reference"]?.ToString() is { } reference ? Ui.Pill(reference, "ticket", "Bg.Subtle", "Text") : null, Ui.Pill(s[booking.Data["status"]?.ToString() ?? "pending"], Visuals.Status(booking.Data["status"]?.ToString()).Icon, Visuals.Status(booking.Data["status"]?.ToString()).Background, Visuals.Status(booking.Data["status"]?.ToString()).Foreground))));

        // Nesting
        var parent = TripQueries.Parent(trip, e.Id);
        var children = (e.Data["children"] as JsonArray ?? []).Select(c => Guid.TryParse(c?.ToString(), out var g) ? trip.Find(g) : null).OfType<Entity>().ToArray();
        var nest = Ui.V(4);
        if (parent is not null) nest.Children.Add(Ui.H(6, Ui.Text(s["PartOf"], "caption"), Link(w, parent)));
        foreach (var child in children) nest.Children.Add(Link(w, child));
        var addChild = Ui.Button(s["AddSubItem"], "plus", "link"); addChild.Click += async (_, _) =>
        {
            var title = await m.Interaction.PromptAsync(s["AddSubItem"]); if (string.IsNullOrWhiteSpace(title)) return;
            await m.RunEditAsync(async () => { var child = Entity.Create("schedule_item", title.Trim()); child.Data["participants"] = new JsonObject { ["inherit"] = true, ["values"] = new JsonArray() }; var copy = m.Workspace!.State.Trip.Find(e.Id)!.Copy(); var list = copy.Data["children"] as JsonArray ?? new JsonArray(); copy.Data["children"] = list; list.Add(child.Id.ToString()); await m.Workspace.ApplyAsync([new(child.Id, child), new(copy.Id, copy)]); m.Selected = m.Workspace.State.Trip.Find(child.Id); });
        };
        nest.Children.Add(addChild);
        if (span.Derived && span.Length is { } total) nest.Children.Add(Ui.Text(string.Format(s["TotalDuration"], Formats.Duration(total, s)), "caption"));
        Add(body, s["NestedItems"], nest);

        Add(body, s["Tags"], ListText(w, e, "tags", s["Tags"]));
        Add(body, s["Constraints"], Constraints(w, e));
    }

    private static Control ListText(MainWindow w, Entity e, string path, string label)
    {
        var current = EditorModel.Get(e, path) as JsonArray;
        var box = new TextBox { Text = current is null ? "" : string.Join(", ", current.Select(n => n?.ToString())), Watermark = w.Model.Strings["CommaSeparated"], Tag = path }; AutomationProperties.SetName(box, label);
        var original = box.Text;
        async Task Commit() { if (box.Text == original) return; original = box.Text; var values = (box.Text ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries); await w.Model.UpdateAsync(e.Id, x => EditorModel.Set(x, path, values.Length == 0 ? null : new JsonArray(values.Select(v => (JsonNode?)JsonValue.Create(v)).ToArray()))); }
        box.LostFocus += async (_, _) => await Commit(); box.KeyDown += async (_, k) => { if (k.Key == Key.Enter) { k.Handled = true; await Commit(); } };
        return box;
    }

    private static Control Constraints(MainWindow w, Entity e)
    {
        var m = w.Model; var s = m.Strings; var trip = m.Trip!; var panel = Ui.V(6);
        var list = e.Data["constraints"] as JsonArray ?? [];
        for (var i = 0; i < list.Count; i++)
        {
            var c = list[i]; var index = i; var type = c?["type"]?.ToString() ?? "";
            var other = Guid.TryParse(c?["item"]?.ToString(), out var oid) ? trip.Find(oid)?.Title : null;
            var offset = c?["offset"]?.ToString() is { } o && PeriodPattern.NormalizingIso.Parse(o).TryGetValue(Period.Zero, out var p) ? Formats.Duration(p.ToDuration(), s) : "";
            var text = type switch { "after" => string.Format(s["ConstraintAfter"], offset, other), "arrive_before" => string.Format(s["ConstraintBefore"], offset, other), "earliest_start" => string.Format(s["ConstraintEarliest"], c?["local_time"]), _ => type };
            var remove = Ui.IconButton("x", s["Remove"], "small"); remove.Click += (_, _) => _ = m.UpdateAsync(e.Id, x => { var l = (JsonArray)x.Data["constraints"]!; l.RemoveAt(index); if (l.Count == 0) x.Data.Remove("constraints"); });
            panel.Children.Add(Ui.Columns("Auto,*,Auto", Ui.Icon("link", 14, "Text.Subtle"), Ui.Text(text).Margin(8, 0), remove));
        }
        var add = Ui.Button(s["AddConstraint"], "plus", "link"); add.Click += async (_, _) => await m.RunEditAsync(async () =>
        {
            var kind = await m.Interaction.ChooseAsync(s["Constraints"], new[] { "after", "earliest_start", "arrive_before" }.Select(k => new Choice(k, s[k], "link", s[k + "Hint"])).ToArray()); if (kind is null) return;
            var constraint = new JsonObject { ["type"] = kind };
            if (kind == "earliest_start") { var values = await m.Interaction.FormAsync(s["earliest_start"], [new("time", s["Start"], "time", "10:00:00", Required: true)]); if (values is null) return; constraint["local_time"] = values["time"][..5]; }
            else
            {
                var item = await m.Interaction.ChooseAsync(s["Items"], trip.Entities.Values.Where(x => x.Type == "schedule_item" && x.Id != e.Id).OrderBy(x => x.Title).Select(x => new Choice(x.Id.ToString(), x.Title, Visuals.For(trip, x).Icon)).ToArray()); if (item is null) return;
                var values = await m.Interaction.FormAsync(s["Offset"], [new("duration", s["DurationMinutes"], "duration", "PT30M", Required: true)]); if (values is null) return; constraint["item"] = item; constraint["offset"] = values["duration"];
            }
            var updated = m.Workspace!.State.Trip.Find(e.Id)!.Copy(); var l = updated.Data["constraints"] as JsonArray ?? []; updated.Data["constraints"] = l; l.Add(constraint); await m.Workspace.EditAsync(updated);
        });
        panel.Children.Add(add);
        return panel;
    }

    private static void TripDetails(MainWindow w, Entity e, StackPanel body)
    {
        var m = w.Model; var s = m.Strings; var trip = m.Trip!;
        body.Children.Add(Ui.Card(SidebarView.Cover(m, trip, 340, 150, 10), 0).Also(c => c.ClipToBounds = true));
        body.Children.Add(Ui.Columns("*,*", Field(s["Start"], Editors.Date(w, e.Id, "dates/start", s["Start"], e.Data["dates"]?["start"]?.ToString())), Field(s["End"], Editors.Date(w, e.Id, "dates/end", s["End"], e.Data["dates"]?["end"]?.ToString())).Margin(10, 0, 0, 0)));
        body.Children.Add(Ui.Text(s["DatesHint"], "caption"));
        body.Children.Add(Field(s["Timezone"], Editors.Search(w, e.Id, "default_timezone", s["Timezone"], e.Data["default_timezone"]?.ToString(), FieldOptions.Timezones)));
        body.Children.Add(Field(s["Language"], Editors.Search(w, e.Id, "language", s["Language"], e.Data["language"]?.ToString(), FieldOptions.Languages)));
        body.Children.Add(Field(s["Participants"], Editors.References(w, e.Id, "participants", s["Participants"], "person")));
        body.Children.Add(Field(s["Cover"], Editors.Reference(w, e.Id, "cover/document", s["Cover"], "document", allowCreate: false)));
        var stats = new WrapPanel { ItemSpacing = 8, LineSpacing = 8 };
        foreach (var (type, icon, label) in new[] { ("schedule_item", "calendar", "StatActivities"), ("place", "map-pin", "StatPlaces"), ("booking", "ticket", "StatBookings"), ("task", "square-check", "StatTasks"), ("document", "file", "StatFiles") })
            stats.Children.Add(Ui.Pill(string.Format(s[label], trip.Entities.Values.Count(x => x.Type == type)), icon, "Bg.Subtle", "Text.Muted"));
        Add(body, s["Overview"], stats);
        if (m.Advanced) Add(body, s["Repository"], Ui.V(4, new SelectableTextBlock { Text = m.Workspace!.Repository.Root, TextWrapping = TextWrapping.Wrap, FontSize = 12 }.Classed("muted"), Ui.Text(m.CurrentBranch, "caption")));
    }

    private static void DocumentDetails(MainWindow w, Entity e, StackPanel body)
    {
        var m = w.Model; var s = m.Strings; var trip = m.Trip!;
        string? path = null; try { path = m.Workspace!.Repository.DocumentPath(e); } catch (DomainException) { }
        var media = e.Data["media_type"]?.ToString() ?? "";
        if (media.StartsWith("image/", StringComparison.Ordinal) && path is not null && File.Exists(path))
        {
            try { using var stream = File.OpenRead(path); body.Children.Add(new Border { CornerRadius = new CornerRadius(10), ClipToBounds = true, Child = new Image { Source = Bitmap.DecodeToWidth(stream, 720), Stretch = Stretch.Uniform, MaxHeight = 260 } }); }
            catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException) { body.Children.Add(Ui.Text(s["PreviewUnavailable"], "caption")); }
        }
        var open = Ui.Button(s["OpenFile"], "external-link", "primary"); open.Click += (_, _) => OpenDocument(m, e);
        var replace = Ui.Button(s["Replace"], "upload"); replace.Click += async (_, _) => await m.RunAsync(async () => { var file = await m.Interaction.FileAsync(); if (file is null) return; var large = new FileInfo(file).Length > 25 * 1024 * 1024; if (large && !await m.Interaction.ConfirmAsync(s["LargeFile"])) return; await m.Workspace!.ReplaceDocumentAsync(e.Id, file, media, large); });
        var actions = Ui.Flow(8, open, replace);
        if (media.StartsWith("image/", StringComparison.Ordinal)) { var cover = Ui.Button(s["UseAsCover"], "image"); cover.Click += (_, _) => _ = m.UpdateAsync(trip.Manifest.Id, x => x.Data["cover"] = new JsonObject { ["document"] = e.Id.ToString() }); actions.Children.Add(cover); }
        body.Children.Add(actions);
        var size = path is not null && File.Exists(path) ? Formats.Bytes(new FileInfo(path).Length) : "";
        body.Children.Add(Ui.Text(string.Join(" · ", new[] { media, size }.Where(x => x.Length > 0)), "caption"));
        body.Children.Add(Field(s["Caption"], Editors.Text(w, e.Id, "caption", s["Caption"], e.Data["caption"]?.ToString())));
        body.Children.Add(Field(s["Tags"], ListText(w, e, "tags", s["Tags"])));
        var duplicates = trip.Entities.Values.Count(o => o.Type == "document" && o.Id != e.Id && o.Data["blob"]?["hash"]?.ToString() == e.Data["blob"]?["hash"]?.ToString());
        if (duplicates > 0) body.Children.Add(Ui.Text(string.Format(s["SameFileElsewhere"], duplicates), "caption"));
        if (m.Advanced) body.Children.Add(new SelectableTextBlock { Text = "sha256 " + e.Data["blob"]?["hash"], FontFamily = FontFamily.Parse("monospace"), FontSize = 11, TextWrapping = TextWrapping.Wrap }.Classed("muted"));
    }

    /// <summary>Open a document with the system viewer through a temporary copy outside the repository.</summary>
    public static void OpenDocument(MainViewModel m, Entity e)
    {
        try
        {
            var path = m.Workspace!.Repository.DocumentPath(e);
            var target = Path.Combine(m.Store.Root, "previews", e.Id + Path.GetExtension(e.Title)); Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(path, target, true); m.Interaction.Open(target);
        }
        catch (Exception ex) when (ex is DomainException or IOException) { m.Interaction.Toast(m.Strings.Error(ex)); }
    }

    private static void PlaceDetails(MainWindow w, Entity e, StackPanel body)
    {
        var m = w.Model; var s = m.Strings;
        body.Children.Add(Field(s["Address"], Editors.Text(w, e.Id, "address/formatted", s["Address"], e.Data["address"]?["formatted"]?.ToString(), multiline: false)));
        var coords = MapView.Coordinates(e);
        var location = Editors.Coordinates(w, e.Id, coords);
        var find = Ui.Button(s["FindOnMap"], "search", "soft"); find.Flyout = Editors.Picker(w, "place", [], false, async picked =>
        {
            if (picked is null || picked.Id == e.Id) return;
            var created = m.Trip!.Find(picked.Id);
            if (created?.Data["location"] is JsonObject loc)
            {
                await m.UpdateAsync(e.Id, x => { x.Data["location"] = loc.DeepClone(); if (created.Data["address"] is JsonObject a) x.Data["address"] = a.DeepClone(); if (created.Data["external_ids"] is JsonObject ids) x.Data["external_ids"] = ids.DeepClone(); });
                if (TripSummaries.ReferencesTo(m.Trip!, picked.Id).Count == 0 && picked.Id != e.Id) await m.RunEditAsync(() => m.Workspace!.ApplyAsync([new(picked.Id, null)]));
                m.Selected = m.Trip!.Find(e.Id);
            }
        }, allowCreate: false, allowClear: false);
        var show = Ui.Button(s["ShowOnMap"], "map"); show.IsEnabled = coords is not null; show.Click += (_, _) => m.Navigate("Map");
        Add(body, s["Location"], Ui.V(10, location, Ui.Flow(8, find, show), m.OnlineMaps ? null : Ui.Text(s["EnableOnlineSearchHint"], "caption")));
        body.Children.Add(Field(s["Timezone"], Editors.Search(w, e.Id, "timezone", s["Timezone"], e.Data["timezone"]?.ToString(), FieldOptions.Timezones)));
    }

    private static void BookingDetails(MainWindow w, Entity e, StackPanel body)
    {
        var m = w.Model; var s = m.Strings;
        body.Children.Add(Field(s["Status"], Editors.StatusChips(w, e.Id, e.Data["status"]?.ToString(), ["idea", "pending", "reserved", "confirmed", "cancelled", "completed"])));
        body.Children.Add(Ui.Columns("*,*", Field(s["Provider"], Editors.Text(w, e.Id, "provider/name", s["Provider"], e.Data["provider"]?["name"]?.ToString())), Field(s["Reference"], Editors.Text(w, e.Id, "reference", s["Reference"], e.Data["reference"]?.ToString())).Margin(10, 0, 0, 0)));
        body.Children.Add(Field(s["Amount"], Editors.Money(w, e.Id, "price", s["Amount"], e.Data["price"])));
        body.Children.Add(Field(s["Travelers"], Editors.References(w, e.Id, "travelers", s["Travelers"], "person")));
        body.Children.Add(Field(s["BookedItems"], Editors.References(w, e.Id, "items", s["BookedItems"], "schedule_item")));
        body.Children.Add(Field(s["Documents"], Editors.References(w, e.Id, "documents", s["Documents"], "document")));
    }

    private static void PersonDetails(MainWindow w, Entity e, StackPanel body)
    {
        var m = w.Model; var s = m.Strings;
        var me = m.Me?.Id == e.Id;
        body.Children.Add(Ui.Columns("Auto,*", Ui.Avatar(e.Title, 64), Ui.V(4, me ? Ui.Pill(s["You"], null, "Accent.Soft", "Accent.SoftText") : null, Ui.Text(s["PersonLocalHint"], "caption")).Margin(14, 0, 0, 0)));
        body.Children.Add(Field(s["Roles"], ListText(w, e, "roles", s["Roles"])));
        var fields = EditorModel.Fields(e);
        string Identities(string key, string inner) => string.Join(", ", (EditorModel.Get(e, "identities/" + key) as JsonArray ?? []).Select(i => i?[inner]?.ToString()).Where(x => x is not null));
        body.Children.Add(Field(s["GitIdentities"], Editors.ValueButton(Identities("git", "email") is { Length: > 0 } git ? git : s["AddEmail"], "mail", s["GitIdentities"], () => m.RunEditAsync(() => EditorModel.ComplexAsync(m, e.Id, fields.Single(f => f.Kind == "identities"))))));
        body.Children.Add(Field("GitHub", Editors.ValueButton(Identities("github", "username") is { Length: > 0 } gh ? "@" + gh : s["AddGitHubUser"], "cloud", "GitHub", () => m.RunEditAsync(() => EditorModel.ComplexAsync(m, e.Id, fields.Single(f => f.Kind == "github"))))));
        body.Children.Add(Field(s["Avatar"], Editors.Reference(w, e.Id, "avatar/document", s["Avatar"], "document", allowCreate: false)));
        if (!me && m.Me is null) { var mine = Ui.Button(s["ThisIsMe"], "user", "soft"); mine.Click += async (_, _) => { m.Store.Set("identity.name", e.Title); if (Identities("git", "email") is { Length: > 0 } email) m.Store.Set("identity.email", email.Split(',')[0].Trim()); await m.AddIdentityCommand.ExecuteAsync(null); }; body.Children.Add(mine); }
    }

    private static Control Generic(MainWindow w, Entity e, EditorField field)
    {
        var m = w.Model; var value = EditorModel.Get(e, field.Path);
        return field.Kind switch
        {
            "choice" => Editors.Choice(w, e.Id, field.Path, m.Strings[field.Label], value?.ToString(), field.Choices!),
            "bool" => Editors.Toggle(w, e.Id, field.Path, m.Strings[field.Label], value?.ToString() == "true"),
            "multiline" => Editors.Text(w, e.Id, field.Path, m.Strings[field.Label], value?.ToString(), true),
            "ref" => Editors.Reference(w, e.Id, field.Path, m.Strings[field.Label], field.ReferenceType),
            "refs" => Editors.References(w, e.Id, field.Path, m.Strings[field.Label], field.ReferenceType),
            "money" => Editors.Money(w, e.Id, field.Path, m.Strings[field.Label], value),
            "date" => Editors.Date(w, e.Id, field.Path, m.Strings[field.Label], value?.ToString()),
            "timezone" => Editors.Search(w, e.Id, field.Path, m.Strings[field.Label], value?.ToString(), FieldOptions.Timezones),
            "list" => ListText(w, e, field.Path, m.Strings[field.Label]),
            "readonly" => Ui.Text(value?.ToString(), "muted"),
            "text" => Editors.Text(w, e.Id, field.Path, m.Strings[field.Label], value?.ToString()),
            _ => Editors.ValueButton(value is null ? m.Strings["Choose"] : m.DescribeValue(value, m.Trip!), "pencil", m.Strings[field.Label], () => m.RunEditAsync(() => EditorModel.ComplexAsync(m, e.Id, field)), field.Path)
        };
    }

    // Notes ---------------------------------------------------------------------------------------------

    private static void NotesTab(MainWindow w, Entity e, StackPanel body)
    {
        var m = w.Model; var s = m.Strings; var trip = m.Trip!;
        var entries = e.Data["content"] as JsonArray ?? [];
        for (var i = 0; i < entries.Count; i++)
        {
            if (entries[i] is not JsonObject entry) continue; var index = i;
            var remove = Ui.IconButton("trash-2", s["RemoveContent"], "small"); remove.Click += (_, _) => _ = m.UpdateAsync(e.Id, x => { ((JsonArray)x.Data["content"]!).RemoveAt(index); });
            Control content;
            var type = entry["type"]?.ToString();
            if (type == "markdown" && entry["file"] is { } file && trip.Resources.TryGetValue(file.ToString(), out var bytes))
            {
                var text = Encoding.UTF8.GetString(bytes);
                var view = new MarkdownView(text);
                var edit = Ui.IconButton("pencil", s["Edit"], "small");
                var host = new ContentControl { Content = view };
                edit.Click += (_, _) =>
                {
                    var box = new TextBox { Text = text, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 140 }; AutomationProperties.SetName(box, s["Body"]);
                    var save = Ui.Button(s["Save"], null, "primary", "compact"); var cancel = Ui.Button(s["Cancel"], null, "ghost", "compact");
                    save.Click += async (_, _) => await m.RunEditAsync(() => m.Workspace!.ApplyAsync([], resources: [new(file.ToString(), Encoding.UTF8.GetBytes(box.Text ?? ""))]));
                    cancel.Click += (_, _) => host.Content = view;
                    host.Content = Ui.V(8, box, Ui.H(6, save, cancel)); box.Focus();
                };
                content = Ui.V(6, Ui.Columns("*,Auto,Auto", Ui.H(6, Ui.Icon("sticky-note", 14, "Text.Subtle"), Ui.Text(s["Note"], "caption")), edit, remove), host);
            }
            else if (type == "link" && entry["url"]?.ToString() is { } url)
            {
                var open = Ui.Button(url, "external-link", "link"); open.Click += (_, _) => { if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http") m.Interaction.Open(url); };
                content = Ui.Columns("*,Auto", open, remove);
            }
            else if (Guid.TryParse((entry["document"] ?? entry["entity"])?.ToString(), out var refId) && trip.Find(refId) is { } linked)
            {
                Control preview = Link(w, linked);
                if (type == "image")
                {
                    try { using var stream = File.OpenRead(m.Workspace!.Repository.DocumentPath(linked)); preview = Ui.V(4, new Border { CornerRadius = new CornerRadius(8), ClipToBounds = true, Child = new Image { Source = Bitmap.DecodeToWidth(stream, 640), MaxHeight = 200, Stretch = Stretch.Uniform } }, Link(w, linked)); }
                    catch (Exception ex) when (ex is DomainException or IOException or ArgumentException or NotSupportedException) { }
                }
                content = Ui.Columns("*,Auto", preview, remove);
            }
            else content = Ui.Columns("*,Auto", Ui.Text(entry.ToJsonString(), "caption"), remove);
            body.Children.Add(Ui.Card(content, 12));
        }
        if (entries.Count == 0) body.Children.Add(Ui.Text(s["NoNotesYet"], "muted"));
        var noteBox = new TextBox { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 90, Watermark = s["WriteNote"] }; AutomationProperties.SetName(noteBox, s["WriteNote"]);
        var addNote = Ui.Button(s["AddNote"], "plus", "primary", "compact");
        addNote.Click += async (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(noteBox.Text)) return; var text = noteBox.Text;
            await m.RunEditAsync(async () =>
            {
                var updated = m.Workspace!.State.Trip.Find(e.Id)!.Copy(); var content = updated.Data["content"] as JsonArray ?? new JsonArray(); updated.Data["content"] = content;
                var file = "documents/" + Guid.CreateVersion7() + ".md"; content.Add(new JsonObject { ["type"] = "markdown", ["file"] = file });
                await m.Workspace.ApplyAsync([new(updated.Id, updated)], resources: [new(file, Encoding.UTF8.GetBytes(text))]);
            });
        };
        var addLink = Ui.Button(s["AddLink"], "link", "compact"); addLink.Click += async (_, _) =>
        {
            var values = await m.Interaction.FormAsync(s["AddLink"], [new("url", s["Link"], Required: true, Hint: "https://")]); if (values is null) return;
            var url = values["url"].Trim(); if (!url.Contains("://", StringComparison.Ordinal)) url = "https://" + url;
            await m.UpdateAsync(e.Id, x => { var content = x.Data["content"] as JsonArray ?? new JsonArray(); x.Data["content"] = content; content.Add(new JsonObject { ["type"] = "link", ["url"] = url }); });
        };
        var attach = Ui.Button(s["AttachFile"], "paperclip", "compact"); attach.Click += async (_, _) => { var file = await m.Interaction.FileAsync(); if (file is not null) await m.AddFilesAsync([file], e.Id); };
        var reference = Ui.Button(s["Reference"], "arrow-up-right", "compact");
        reference.Flyout = Editors.Picker(w, null, [], false, picked => picked is null ? Task.CompletedTask : m.UpdateAsync(e.Id, x => { var content = x.Data["content"] as JsonArray ?? new JsonArray(); x.Data["content"] = content; content.Add(picked.Type == "document" ? new JsonObject { ["type"] = (picked.Data["media_type"]?.ToString() ?? "").StartsWith("image/", StringComparison.Ordinal) ? "image" : "pdf", ["document"] = picked.Id.ToString() } : new JsonObject { ["type"] = "reference", ["entity"] = picked.Id.ToString() }); }), false, false);
        body.Children.Add(Ui.V(8, noteBox, Ui.Flow(6, addNote, addLink, attach, reference)));
        body.Children.Add(Ui.Text(s["MarkdownHint"], "caption"));
    }

    // Comments ------------------------------------------------------------------------------------------

    private static void CommentsTab(MainWindow w, Entity e, StackPanel body)
    {
        var m = w.Model; var s = m.Strings; var trip = m.Trip!;
        var comments = trip.Entities.Values.Where(c => c.Type == "comment" && c.Data["target"]?["id"]?.ToString() == e.Id.ToString()).OrderBy(c => c.Data["created_at"]?.ToString()).ToArray();
        foreach (var comment in comments)
        {
            var author = Guid.TryParse(comment.Data["author"]?.ToString(), out var authorId) ? trip.Find(authorId)?.Title ?? s["Someone"] : s["Someone"];
            var when = DateTimeOffset.TryParse(comment.Data["created_at"]?.ToString(), CultureInfo.InvariantCulture, out var at) ? Formats.Relative(at, s) : "";
            var remove = Ui.IconButton("trash-2", s["Delete"], "small"); remove.Click += (_, _) => _ = m.DeleteAsync(comment.Id);
            body.Children.Add(Ui.Columns("Auto,*", Ui.Avatar(author, 30).Also(a => a.VerticalAlignment = VerticalAlignment.Top), Ui.V(4, Ui.Columns("*,Auto", Ui.H(8, Ui.Text(author, "title"), Ui.Text(when, "caption")), remove), new MarkdownView(comment.Data["body"]?.ToString() ?? "")).Margin(10, 0, 0, 0)));
        }
        if (comments.Length == 0) body.Children.Add(Ui.Text(s["NoCommentsYet"], "muted"));
        var box = new TextBox { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 70, Watermark = s["WriteComment"] }; AutomationProperties.SetName(box, s["WriteComment"]);
        var send = Ui.Button(s["Comment"], "message-square", "primary", "compact");
        send.Click += async (_, _) => { if (!string.IsNullOrWhiteSpace(box.Text)) { var text = box.Text; box.Text = ""; await m.AddCommentAsync(m.Trip!.Find(e.Id), text); } };
        box.KeyDown += async (_, k) => { if (k.Key == Key.Enter && k.KeyModifiers == KeyModifiers.Control && !string.IsNullOrWhiteSpace(box.Text)) { k.Handled = true; var text = box.Text; box.Text = ""; await m.AddCommentAsync(m.Trip!.Find(e.Id), text); } };
        var who = m.Me?.Title;
        body.Children.Add(Ui.V(8, box, Ui.Columns("*,Auto", Ui.Text(who is null ? s["CommentAsHint"] : string.Format(s["CommentingAs"], who), "caption"), send)));
    }
}
