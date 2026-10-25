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
using Avalonia.Platform.Storage;
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
        return EntityMenu.Flyout(w, e, new EntityMenu.Action(w.Model.Strings["AddComment"], "message-square", () => { tab = "Comments"; w.RenderInspector(); }));
    }

    private static void Add(StackPanel body, string title, Control? content, Control? action = null) { if (content is not null) body.Children.Add(Ui.Section(title, content, action)); }
    private static Control Field(string label, Control editor) => Ui.Field(label, editor);

    /// <summary>
    /// The accommodation's rules and practical details: check-in and check-out hours, how to get in, whether the
    /// planned hours are fixed, free cancellation, breakfast, parking, arrival notes and the place's contact.
    /// </summary>
    private static Control StayDetails(MainWindow w, Entity e, Stay? stay)
    {
        var m = w.Model; var s = m.Strings; var trip = m.Trip!; var a = e.Data["components"]?["accommodation"];
        const string Root = "components/accommodation/";
        var hours = Ui.Columns("*,*",
            Field(s["CheckInHours"], Ui.H(6, Editors.TimeOfDay(w, e.Id, Root + "check_in_window/from", s["CheckInFrom"], a?["check_in_window"]?["from"]?.ToString()), Ui.Text("–", "muted").Also(t => t.VerticalAlignment = VerticalAlignment.Center), Editors.TimeOfDay(w, e.Id, Root + "check_in_window/until", s["CheckInUntil"], a?["check_in_window"]?["until"]?.ToString()))),
            Field(s["CheckOutBy"], Editors.TimeOfDay(w, e.Id, Root + "check_out_window/until", s["CheckOutBy"], a?["check_out_window"]?["until"]?.ToString())).Margin(10, 0, 0, 0));
        var panel = Ui.V(10, hours);
        if (stay is { CheckIn: { } ci, CheckInApproximate: false, CheckInWindow.Until: { } until } && NodaTime.Text.LocalDateTimePattern.ExtendedIso.Parse(ci.Local).TryGetValue(default, out var arrive) && arrive.TimeOfDay > until)
            panel.Children.Add(new Border { Padding = new Thickness(10, 8), CornerRadius = new CornerRadius(8), Child = Ui.Columns("Auto,*", Ui.Icon("clock", 14, "Warning"), Ui.Text(string.Format(s["LateArrivalHint"], Formats.Time(arrive.TimeOfDay), Formats.Time(until)), "caption").Margin(8, 0, 0, 0)) }.Res(Border.BackgroundProperty, "Warning.Soft"));
        panel.Children.Add(Field(s["Access"], Editors.Chips(w, e.Id, Root + "access", s["Access"], stay?.Access, [("reception", "building-2", s["AccessReception"]), ("self", "lock", s["AccessSelf"]), ("host", "user-round", s["AccessHost"])])));
        Control Open(string key, string label, bool value)
        {
            var toggle = new ToggleSwitch { IsChecked = value, OnContent = label, OffContent = label }; AutomationProperties.SetName(toggle, label);
            // The stay's general time follows: approximate when neither hour is fixed.
            toggle.IsCheckedChanged += (_, _) =>
            {
                if (m.Trip?.Find(e.Id)?.Copy() is not { } copy || copy.Data["components"]?["accommodation"]?[key] is not JsonObject moment) return;
                if (toggle.IsChecked == true) moment["approximate"] = true; else moment.Remove("approximate");
                _ = m.UpdateAsync(Stays.Align(m.Trip!, copy));
            };
            return toggle;
        }
        if (stay?.CheckIn is not null) panel.Children.Add(Ui.Flow(16, Open("check_in", s["ArrivalTimeOpen"], stay.CheckInApproximate), Open("check_out", s["DepartureTimeOpen"], stay.CheckOutApproximate)));
        var fields = EditorModel.Fields(e);
        var free = stay?.FreeCancellationUntil is { } until2 && NodaTime.Text.LocalDateTimePattern.ExtendedIso.Parse(until2.Local).TryGetValue(default, out var freeLocal) ? Formats.LongDay(freeLocal.Date) + " · " + Formats.Time(freeLocal.TimeOfDay) : s["Choose"];
        panel.Children.Add(Ui.Columns("*,*",
            Field(s["FreeCancellationUntil"], Editors.ValueButton(free, "receipt", s["FreeCancellationUntil"], () => m.RunEditAsync(() => EditorModel.ComplexAsync(m, e.Id, fields.Single(f => f.Path.EndsWith("cancellation/free_until", StringComparison.Ordinal)))))),
            Field(s["CancellationNote"], Editors.Text(w, e.Id, Root + "cancellation/note", s["CancellationNote"], a?["cancellation"]?["note"]?.ToString())).Margin(10, 0, 0, 0)));
        panel.Children.Add(Field(s["Breakfast"], Editors.Chips(w, e.Id, Root + "breakfast", s["Breakfast"], a?["breakfast"]?.ToString(), [("included", "coffee", s["BreakfastIncluded"]), ("available", "utensils", s["BreakfastAvailable"]), ("none", "x", s["BreakfastNone"])])));
        panel.Children.Add(Ui.Columns("Auto,*", Field(s["Parking"], Editors.Toggle(w, e.Id, Root + "parking/included", s["ParkingIncluded"], a?["parking"]?["included"] is JsonValue p && p.TryGetValue<bool>(out var included) && included)),
            Field(s["ParkingNote"], Editors.Text(w, e.Id, Root + "parking/note", s["ParkingNote"], a?["parking"]?["note"]?.ToString())).Margin(10, 0, 0, 0)));
        panel.Children.Add(Field(s["ArrivalNotes"], Ui.V(4, Editors.Text(w, e.Id, Root + "arrival_notes", s["ArrivalNotes"], a?["arrival_notes"]?.ToString(), multiline: true), Ui.Text(s["NoDoorCodes"], "caption"))));
        if (stay?.Place is { } placeId && trip.Find(placeId) is { } place && ContactLinks(m, place) is { } contact) panel.Children.Add(Field(s["Contact"], contact));
        return panel;
    }

    /// <summary>Phone, e-mail and website of a place as buttons that open the phone app, mail program or browser.</summary>
    private static Control? ContactLinks(MainViewModel m, Entity place)
    {
        var contact = place.Data["contact"]; var links = new WrapPanel { ItemSpacing = 6, LineSpacing = 6 };
        void Link(string? value, string icon, Func<string, string> target)
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            var button = Ui.Button(value, icon, "compact"); button.Click += (_, _) => m.Interaction.Open(target(value.Trim())); links.Children.Add(button);
        }
        Link(contact?["phone"]?.ToString(), "message-square", v => "tel:" + new string(v.Where(c => char.IsDigit(c) || c == '+').ToArray()));
        Link(contact?["email"]?.ToString(), "mail", v => "mailto:" + v);
        Link(contact?["website"]?.ToString(), "globe", v => v.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? v : "https://" + v);
        return links.Children.Count == 0 ? null : links;
    }

    /// <summary>The nights of a stay with a stepper, and a note with a fix when check-in, check-out and time disagree or one is missing.</summary>
    private static Control? StayNights(MainWindow w, Entity e, Stay? stay)
    {
        if (stay is null) return null;
        var m = w.Model; var s = m.Strings;
        if (stay.FirstNight is not { } first) return Ui.Text(s["StayUnscheduled"], "caption");
        var count = stay.Nights == 0 ? s["DayRoom"] : stay.Nights == 1 ? s["OneNight"] : string.Format(s["NightsCount"], stay.Nights);
        var fewer = Ui.IconButton("minus", s["FewerNights"], "small"); fewer.IsEnabled = stay.Nights > 1; fewer.Click += (_, _) => _ = m.SetStayNightsAsync(e.Id, stay.Nights - 1);
        var more = Ui.IconButton("plus", s["MoreNights"], "small"); more.Click += (_, _) => _ = m.SetStayNightsAsync(e.Id, Math.Max(1, stay.Nights) + 1);
        var summary = Ui.Columns("Auto,*,Auto,Auto", Ui.Icon("bed-double", 15, "Kind.Accommodation.Fg"), Ui.V(1, Ui.Text(count, "strong"), Ui.Text(Formats.DateRange(first, stay.DepartureDay), "caption")).Margin(8, 0), fewer, more);
        AutomationProperties.SetName(summary, count + ", " + Formats.DateRange(first, stay.DepartureDay));
        string? note = stay.Issues.HasFlag(StayIssues.TimeMismatch) ? s["StayTimeMismatch"] : stay.Issues.HasFlag(StayIssues.MissingCheckOut) ? s["StayMissingCheckOut"] : stay.Issues.HasFlag(StayIssues.MissingCheckIn) ? s["StayMissingCheckIn"] : null;
        if (note is null) return summary;
        var fix = Ui.Button(s["AlignStay"], "wrench", "compact"); fix.Click += (_, _) => _ = m.AlignStayAsync(e.Id);
        var hint = new Border { Padding = new Thickness(10, 8), CornerRadius = new CornerRadius(8), Child = Ui.V(6, Ui.Columns("Auto,*", Ui.Icon("triangle-alert", 14, "Warning"), Ui.Text(note, "caption").Margin(8, 0, 0, 0)), fix) }.Res(Border.BackgroundProperty, "Warning.Soft");
        return Ui.V(8, summary, hint);
    }

    private static void DetailsTab(MainWindow w, Entity e, StackPanel body)
    {
        var m = w.Model; var s = m.Strings; var trip = m.Trip!;
        // A stay explains a time that differs from check-in and check-out in its Nights section, with a fix.
        var explainedBelow = Stays.IsStay(e) ? new HashSet<string> { "stay.time_mismatch" } : [];
        foreach (var d in m.Diagnostics.Where(d => (d.Path.StartsWith(e.Id.ToString(), StringComparison.Ordinal) || e.Type == "trip" && d.Path is "travel.yaml" or "cover" or "dates") && !explainedBelow.Contains(d.Code)))
        {
            // Advanced mode adds the code and TravelRepo's own wording below the translated text.
            var text = Ui.V(2, Ui.Text(s.Diagnostic(d.Code)).Also(t => t.TextWrapping = TextWrapping.Wrap), m.Advanced ? new SelectableTextBlock { Text = d.Code + ": " + d.Message, TextWrapping = TextWrapping.Wrap, FontSize = 11 }.Classed("muted") : null);
            body.Children.Add(new Border { Padding = new Thickness(12, 10), CornerRadius = new CornerRadius(10), Child = Ui.Columns("Auto,*", Ui.Icon(d.Severity == Severity.Error ? "circle-help" : "triangle-alert", 16, d.Severity == Severity.Error ? "Danger" : "Warning"), text.Margin(10, 0, 0, 0)) }.Res(Border.BackgroundProperty, d.Severity == Severity.Error ? "Danger.Soft" : "Warning.Soft"));
        }
        switch (e.Type)
        {
            case "schedule_item": ScheduleDetails(w, e, body); break;
            case "trip": TripDetails(w, e, body); break;
            case "document": DocumentDetails(w, e, body); break;
            case "note": body.Children.Add(Field(s["Body"], Editors.Text(w, e.Id, "body", s["Body"], e.Data["body"]?.ToString(), multiline: true, watermark: s["NoteWatermark"]))); if (e.Data["body"]?.ToString() is { Length: > 0 } text) body.Children.Add(Ui.Section(s["Preview"], Ui.Card(new MarkdownView(text), 14))); break;
            case "place": PlaceDetails(w, e, body); break;
            case "booking": BookingDetails(w, e, body); break;
            case "task" when PackingLists.IsItem(e): PackingDetails(w, e, body); break;
            case "collection" when PackingLists.IsList(e):
                var packingList = PackingLists.Resolve(trip, e);
                body.Children.Add(Ui.Text(string.Format(s["PackingListProgress"], e.Title, packingList.DoneCount, packingList.Items.Count), "muted"));
                var openList = Ui.Button(s["OpenPackingList"], "backpack", "soft"); openList.Click += (_, _) => { m.ShowPackingList(e.Id); m.Navigate("Packing"); };
                body.Children.Add(openList.Also(b => b.HorizontalAlignment = HorizontalAlignment.Left));
                break;
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
                var chip = Ui.Choice(s[mode], Visuals.TransportIcon(mode), transport["type"]?.ToString() == mode, "chip");
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
            var resolved = Stays.Resolve(trip, e);
            var stayPanel = Ui.V(10,
                Field(s["Place"], Editors.Reference(w, e.Id, "components/accommodation/place", s["Place"], "place")),
                StayNights(w, e, resolved),
                Ui.Columns("*,*", Field(s["CheckIn"], Editors.ValueButton(Zoned("check_in"), "log-out", s["CheckIn"], () => m.RunEditAsync(() => EditorModel.ComplexAsync(m, e.Id, fields.Single(f => f.Path.EndsWith("check_in", StringComparison.Ordinal)))))), Field(s["CheckOut"], Editors.ValueButton(Zoned("check_out"), "log-out", s["CheckOut"], () => m.RunEditAsync(() => EditorModel.ComplexAsync(m, e.Id, fields.Single(f => f.Path.EndsWith("check_out", StringComparison.Ordinal)))))).Margin(10, 0, 0, 0)),
                Field(s["Guests"], Editors.References(w, e.Id, "components/accommodation/guests", s["Guests"], "person")),
                Field(s["Rooms"], ListText(w, e, "components/accommodation/rooms", s["Rooms"])));
            Add(body, s["Stay"], stayPanel);
            Add(body, s["StayDetails"], StayDetails(w, e, resolved));
        }
        else
        {
            var categories = new WrapPanel { ItemSpacing = 6, LineSpacing = 6 };
            var inherited = TripQueries.Resolve(trip, e.Id, "category");
            var currentCategory = inherited.Value?.ToString();
            foreach (var category in ScheduleCategories.Standard.Where(c => c is not ("transport" or "accommodation" or "other")))
            {
                var chip = Ui.Choice(s["category." + category], Visuals.CategoryIcon(category), (currentCategory ?? "activity") == category, "chip");
                chip.Click += (_, _) => _ = m.UpdateAsync(e.Id, x => { if (category == "activity") x.Data.Remove("category"); else x.Data["category"] = category; });
                categories.Children.Add(chip);
            }
            var categoryBlock = Ui.V(6, categories, inherited.Inherited ? Ui.Text(string.Format(s["InheritedFrom"], trip.Find(inherited.Source)?.Title), "caption") : null);
            Add(body, s["Category"], categoryBlock);
            Add(body, s["Where"], Editors.Reference(w, e.Id, "default_place", s["DefaultPlace"], "place"));
        }

        if (stay is not JsonObject) Add(body, s["Weather"], WeatherSection(w, e));

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
        Add(body, s["Confirmations"], Confirmations(w, e));

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

    /// <summary>
    /// Whether the item is outdoors (estimated unless stated), and its weather: hour by hour within a week, a trend
    /// up to about two weeks, climate averages beyond. Weather is off until the person turns it on.
    /// </summary>
    private static Control WeatherSection(MainWindow w, Entity e)
    {
        var m = w.Model; var s = m.Strings; var trip = m.Trip!; var zone = ScheduleQueries.TripZone(trip); var panel = Ui.V(12);
        var estimate = OutdoorEstimates.For(trip, e);
        bool? stated = e.Data[OutdoorEstimates.Field] is JsonValue v && v.TryGetValue<bool>(out var flag) ? flag : null;
        var chips = new WrapPanel { ItemSpacing = 6, LineSpacing = 6 };
        void Chip(string label, string icon, bool? value)
        {
            var chip = Ui.Choice(label, icon, stated == value, "chip");
            chip.Click += (_, _) => _ = m.SetOutdoorAsync(e.Id, value); chips.Children.Add(chip);
        }
        Chip(s["OutdoorAuto"], "sparkles", null); Chip(s["exposure.Outdoor"], "trees", true); Chip(s["exposure.Indoor"], "house", false);
        var reason = stated is not null ? null : estimate.Source == ExposureSource.Inherited ? string.Format(s["OutdoorInherited"], WeatherDisplay.Exposure(estimate.Exposure, s))
            : string.Format(s["OutdoorEstimated"], WeatherDisplay.Exposure(estimate.Exposure, s), WeatherDisplay.Reason(estimate, e, s));
        panel.Children.Add(Ui.V(6, chips, reason is null ? null : Ui.Text(reason, "caption").Also(t => t.TextWrapping = TextWrapping.Wrap)));

        if (!m.WeatherOn)
        {
            var enable = Ui.Button(s["WeatherTurnOn"], "cloud-sun", m.WeatherPromptDismissed ? "link" : "primary"); enable.Click += async (_, _) => await m.SetWeatherAsync(true);
            if (m.WeatherPromptDismissed) { panel.Children.Add(enable.Also(b => b.HorizontalAlignment = HorizontalAlignment.Left)); return panel; }
            var later = Ui.Button(s["NotNow"], null, "ghost"); later.Click += (_, _) => { m.WeatherPromptDismissed = true; m.RaiseContentChanged(); };
            panel.Children.Add(Ui.Card(Ui.V(10, Ui.Columns("Auto,*", Ui.Icon("cloud-sun", 18, "Accent"), Ui.Text(s["WeatherPrompt"], "muted").Also(t => t.TextWrapping = TextWrapping.Wrap).Margin(10, 0, 0, 0)), Ui.H(8, enable, later)), 14));
            return panel;
        }
        if (!m.Weather.Items.TryGetValue(e.Id, out var weather))
        {
            var span = ScheduleQueries.Span(trip, e);
            var hasPlace = e.Data["components"]?["transport"] is JsonObject ? ScheduleQueries.Route(trip, e) is var r && (TravelRepo.Providers.Weather.GeoPoint.Of(r.From) ?? TravelRepo.Providers.Weather.GeoPoint.Of(r.To)) is not null : TravelRepo.Providers.Weather.GeoPoint.Of(ScheduleQueries.PrimaryPlace(trip, e)) is not null;
            var hint = !hasPlace ? s["WeatherNeedsPlace"] : !span.IsPlaced ? s["WeatherNeedsTime"] : span.End < m.Clock() ? null : m.Weather.Problems.Count > 0 ? WeatherProblem(m) : s["WeatherLoading"];
            if (hint is not null) panel.Children.Add(Ui.Text(hint, "caption").Also(t => t.TextWrapping = TextWrapping.Wrap));
            return panel;
        }
        var route = ScheduleQueries.Route(trip, e);
        for (var index = 0; index < weather.Outlooks.Count; index++)
        {
            var outlook = weather.Outlooks[index]; var place = trip.Find(outlook.Place)?.Title ?? "";
            // A journey has the departure first, unless it has already left; then only the arrival is left.
            var departure = weather.Outlooks.Count == 2 ? index == 0 : outlook.Place == route.From?.Id && route.To?.Id != outlook.Place;
            if (e.Data["components"]?["transport"] is JsonObject) place = string.Format(s[departure ? "WeatherAtDeparture" : "WeatherAtArrival"], place);
            var summary = WeatherDisplay.Summary(outlook, s, m.Imperial);
            var block = Ui.V(8, Ui.Columns("Auto,*", Ui.Icon(WeatherDisplay.Icon(outlook), 22, "Text.Muted"), Ui.V(1, Ui.Line(place, "title"), Ui.Text(summary, "caption").Also(t => t.TextWrapping = TextWrapping.Wrap)).Margin(10, 0, 0, 0)));
            if (outlook.Kind == TravelRepo.Providers.Weather.OutlookKind.Forecast && outlook.Hours.Count > 0) block.Children.Add(HourlyStrip(m, outlook.Hours, zone));
            if (outlook.Kind == TravelRepo.Providers.Weather.OutlookKind.Trend) block.Children.Add(Ui.Text(s["TrendHint"], "caption").Also(t => t.TextWrapping = TextWrapping.Wrap));
            if (outlook.Climate is { } climate) block.Children.Add(Ui.Text(string.Format(s["ClimateDetail"], climate.Years), "caption").Also(t => t.TextWrapping = TextWrapping.Wrap));
            if (WeatherDisplay.SunTimes(outlook.Day?.Sunrise ?? outlook.Climate?.Sunrise, outlook.Day?.Sunset ?? outlook.Climate?.Sunset, zone, s) is { } sun)
                block.Children.Add(Ui.H(6, Ui.Icon("sunrise", 14, "Text.Subtle"), Ui.Text(sun, "caption")));
            AutomationProperties.SetName(block, place + ". " + summary);
            panel.Children.Add(block);
        }
        foreach (var concern in weather.Concerns)
            panel.Children.Add(new Border { Padding = new Thickness(10, 6), CornerRadius = new CornerRadius(8), Child = Ui.H(8, Ui.Icon(WeatherDisplay.ConcernIcon(concern.Kind), 15, concern.Severe ? "Danger" : "Warning"), Ui.Text(WeatherDisplay.Concern(concern, s, m.Imperial), "strong")) }
                .Res(Border.BackgroundProperty, concern.Severe ? "Danger.Soft" : "Warning.Soft"));
        if (m.Weather.Problems.Count > 0) panel.Children.Add(Ui.Text(WeatherProblem(m), "caption").Also(t => t.TextWrapping = TextWrapping.Wrap));
        var first = weather.Outlooks[0];
        var source = WeatherDisplay.Source(first, m.WeatherServices.Providers, s) + (first.Fetched is { } fetched ? " · " + string.Format(s["WeatherAsOf"], Formats.Time(fetched.InZone(DateTimeZoneProviders.Tzdb.GetSystemDefault()).TimeOfDay)) : "");
        var credits = Ui.Flow(4, m.Weather.Sources.Select(a => (Control)Ui.Button(a.Text + (a.License is null ? "" : " (" + a.License + ")"), "external-link", "ghost", "compact").Also(b => b.Click += (_, _) => m.Interaction.Open(a.Url))).ToArray());
        panel.Children.Add(Ui.V(2, Ui.Text(source, "caption"), credits));
        return panel;
    }

    private static string WeatherProblem(MainViewModel m) => m.Weather.Problems.Any(p => p.Code == "weather.key") ? m.Strings["WeatherKeyProblem"] : m.Strings["WeatherUnavailable"];

    /// <summary>The hours of an item side by side: time, sky, temperature, and the chance of rain where it matters.</summary>
    private static Control HourlyStrip(MainViewModel m, IReadOnlyList<TravelRepo.Providers.Weather.WeatherHour> hours, DateTimeZone zone)
    {
        var step = Math.Max(1, (int)Math.Ceiling(hours.Count / 8.0)); var shown = hours.Where((_, i) => i % step == 0).Take(8).ToArray();
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions(string.Join(",", shown.Select(_ => "*"))) };
        for (var i = 0; i < shown.Length; i++)
        {
            var hour = shown[i]; var local = hour.Time.InZone(zone);
            var night = local.Hour is < 6 or >= 21;
            var rain = hour.PrecipitationChance is >= 20 ? WeatherDisplay.Percent(hour.PrecipitationChance.Value) : hour.PrecipitationMm is >= 0.2 ? WeatherDisplay.Precipitation(hour.PrecipitationMm.Value, m.Imperial) : null;
            var cell = Ui.V(3, Ui.Text(Formats.Time(local.TimeOfDay), "caption").Also(t => { t.FontSize = 10.5; t.HorizontalAlignment = HorizontalAlignment.Center; }),
                Ui.Icon(WeatherDisplay.Icon(hour.Condition, night), 16, "Text.Muted").Also(c => c.HorizontalAlignment = HorizontalAlignment.Center),
                hour.TemperatureC is { } t ? Ui.Text(WeatherDisplay.Temperature(t, m.Imperial), "strong").Also(x => x.HorizontalAlignment = HorizontalAlignment.Center) : null,
                rain is null ? null : Ui.Text(rain, "caption").Also(x => { x.FontSize = 10.5; x.HorizontalAlignment = HorizontalAlignment.Center; }).Res(TextBlock.ForegroundProperty, "Info"));
            AutomationProperties.SetName(cell, Formats.Time(local.TimeOfDay) + ", " + WeatherDisplay.Condition(hour.Condition, m.Strings) + (hour.TemperatureC is { } c ? ", " + WeatherDisplay.Temperature(c, m.Imperial) : "") + (rain is null ? "" : ", " + rain));
            grid.Children.Add(cell.Col(i));
        }
        return new Border { Child = grid, Padding = new Thickness(4, 8), CornerRadius = new CornerRadius(8) }.Res(Border.BackgroundProperty, "Bg.Subtle");
    }

    /// <summary>A packing item: its group and quantity, who brings it, and who has packed it.</summary>
    private static void PackingDetails(MainWindow w, Entity e, StackPanel body)
    {
        var m = w.Model; var s = m.Strings; var item = PackingLists.Item(m.Trip!, e);
        body.Children.Add(Ui.Columns("*,*", Field(s["PackingGroup"], Editors.Text(w, e.Id, "components/packing/group", s["PackingGroup"], item.Group)), Field(s["Quantity"], Editors.Text(w, e.Id, "components/packing/quantity", s["Quantity"], item.Quantity)).Margin(10, 0, 0, 0)));
        var who = item.Everyone ? s["Everyone"] : item.Unassigned ? s["Unassigned"] : string.Join(", ", item.Assignees.Select(a => a.Title));
        body.Children.Add(Field(s["WhoBringsIt"], Editors.ValueButton(who, "users", s["WhoBringsIt"], () => m.AssignPackingItemAsync(e.Id))));
        if (item.Assignees.Count > 0)
        {
            var packed = Ui.V(4);
            foreach (var person in item.Assignees)
            {
                var check = new CheckBox { IsChecked = item.IsPackedBy(person.Id), Content = Ui.H(8, Ui.Avatar(person.Title, 22), Ui.Text(person.Title)) };
                AutomationProperties.SetName(check, string.Format(s["PackedBy"], person.Title));
                check.IsCheckedChanged += async (_, _) => await m.TogglePackedAsync(e.Id, person.Id);
                packed.Children.Add(check);
            }
            body.Children.Add(Field(item.Everyone ? s["EveryoneBringsOwn"] : s["SharedItem"], packed));
        }
        // The lists this item is on.
        var lists = PackingLists.All(m.Trip!).Where(l => l.Items.Any(i => i.Task.Id == e.Id)).ToArray();
        if (lists.Length > 0) body.Children.Add(Field(s["Packing"], Ui.V(4, lists.Select(l => (Control)Link(w, l.Collection)).ToArray())));
    }

    private static Control ListText(MainWindow w, Entity e, string path, string label)
    {
        var current = EditorModel.Get(e, path) as JsonArray;
        var box = new TextBox { Text = current is null ? "" : string.Join(", ", current.Select(n => n?.ToString())), PlaceholderText = w.Model.Strings["CommaSeparated"], Tag = path }; AutomationProperties.SetName(box, label);
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

    /// <summary>
    /// Show a document: PDFs, e-mails and images in Jourfold's document window, other files with the system viewer.
    /// </summary>
    public static void OpenDocument(MainViewModel m, Entity e)
    {
        if (DocumentPreviews.CanView(e)) m.Interaction.ViewDocument(e); else OpenExternally(m, e);
    }

    /// <summary>Open a document with the system viewer through a temporary copy outside the repository.</summary>
    public static void OpenExternally(MainViewModel m, Entity e)
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
        var contact = e.Data["contact"]; var ps = w.Model.Strings;
        body.Children.Add(Ui.Columns("*,*", Field(ps["Phone"], Editors.Text(w, e.Id, "contact/phone", ps["Phone"], contact?["phone"]?.ToString())), Field(ps["Email"], Editors.Text(w, e.Id, "contact/email", ps["Email"], contact?["email"]?.ToString())).Margin(10, 0, 0, 0)));
        body.Children.Add(Field(ps["Website"], Editors.Text(w, e.Id, "contact/website", ps["Website"], contact?["website"]?.ToString())));
        if (ContactLinks(w.Model, e) is { } links) body.Children.Add(links);
    }

    private static void BookingDetails(MainWindow w, Entity e, StackPanel body)
    {
        var m = w.Model; var s = m.Strings;
        body.Children.Add(Field(s["Status"], Editors.StatusChips(w, e.Id, e.Data["status"]?.ToString(), ["idea", "pending", "reserved", "confirmed", "cancelled", "completed"])));
        body.Children.Add(Ui.Columns("*,*", Field(s["Provider"], Editors.Text(w, e.Id, "provider/name", s["Provider"], e.Data["provider"]?["name"]?.ToString())), Field(s["Reference"], Editors.Text(w, e.Id, "reference", s["Reference"], e.Data["reference"]?.ToString())).Margin(10, 0, 0, 0)));
        body.Children.Add(Field(s["Amount"], Editors.Money(w, e.Id, "price", s["Amount"], e.Data["price"])));
        body.Children.Add(Field(s["Travelers"], Editors.References(w, e.Id, "travelers", s["Travelers"], "person")));
        body.Children.Add(Field(s["BookedItems"], Editors.References(w, e.Id, "items", s["BookedItems"], "schedule_item")));
        var fields = EditorModel.Fields(e);
        var free = Stays.Zoned(e.Data["cancellation"]?["free_until"]) is { } until && NodaTime.Text.LocalDateTimePattern.ExtendedIso.Parse(until.Local).TryGetValue(default, out var freeLocal) ? Formats.LongDay(freeLocal.Date) + " · " + Formats.Time(freeLocal.TimeOfDay) : s["Choose"];
        body.Children.Add(Ui.Columns("*,*",
            Field(s["FreeCancellationUntil"], Editors.ValueButton(free, "receipt", s["FreeCancellationUntil"], () => m.RunEditAsync(() => EditorModel.ComplexAsync(m, e.Id, fields.Single(f => f.Path == "cancellation/free_until"))))),
            Field(s["CancellationNote"], Editors.Text(w, e.Id, "cancellation/note", s["CancellationNote"], e.Data["cancellation"]?["note"]?.ToString())).Margin(10, 0, 0, 0)));
        Add(body, s["Confirmations"], Confirmations(w, e));
    }

    /// <summary>
    /// Confirmations and other files of a booking or schedule item: e-mails and PDFs shown as cards that open in the
    /// document window, a drop area for files dragged from a mail program, and help for getting an e-mail out of
    /// common mail programs.
    /// </summary>
    private static Control Confirmations(MainWindow w, Entity e)
    {
        var m = w.Model; var s = m.Strings; var trip = m.Trip!;
        var panel = Ui.V(8);
        foreach (var document in TripCommands.AttachedDocuments(trip, e))
        {
            Control preview = DocumentPreviews.PdfThumbnail(m, document, 72) is { } thumbnail
                ? new Border { Width = 36, Height = 46, ClipToBounds = true, CornerRadius = new CornerRadius(3), BorderThickness = new Thickness(1), Child = new Image { Source = thumbnail, Stretch = Stretch.UniformToFill } }.Res(Border.BorderBrushProperty, "Line")
                : Ui.Tile(DocumentPreviews.IsMail(document) ? "mail" : DocumentPreviews.IsImage(document) ? "image" : DocumentPreviews.IsPdf(document) ? "file-text" : "file", "Transport", 36);
            var title = DocumentPreviews.Title(m, document);
            var card = new Button { Content = Ui.Columns("Auto,*,Auto", preview, Ui.V(1, Ui.Line(title, "title"), Ui.Line(DocumentPreviews.Summary(m, document), "caption")).Margin(10, 0), Ui.Icon("eye", 14, "Text.Subtle")), Padding = new Thickness(8, 6), HorizontalContentAlignment = HorizontalAlignment.Stretch }.Classed("card").Named(title);
            card.Click += (_, _) => OpenDocument(m, document);
            card.ContextMenu = EntityMenu.Context(w, document);
            panel.Children.Add(card);
        }
        var drop = new Border { Padding = new Thickness(12, 10), CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1.2), Child = Ui.Columns("Auto,*", Ui.Icon("paperclip", 16, "Accent"), Ui.V(1, Ui.Text(s["DropConfirmationTitle"], "strong"), Ui.Text(s["DropConfirmationBody"], "caption")).Margin(10, 0, 0, 0)) }
            .Res(Border.BorderBrushProperty, "Line.Strong").Res(Border.BackgroundProperty, "Bg.Subtle");
        AutomationProperties.SetName(drop, s["DropConfirmationTitle"]);
        DragDrop.SetAllowDrop(drop, true);
        drop.AddHandler(DragDrop.DragOverEvent, (_, args) => { if (args.DataTransfer.Contains(DataFormat.File)) { args.DragEffects = DragDropEffects.Copy; drop.Res(Border.BorderBrushProperty, "Accent"); args.Handled = true; } });
        drop.AddHandler(DragDrop.DragLeaveEvent, (_, _) => drop.Res(Border.BorderBrushProperty, "Line.Strong"));
        drop.AddHandler(DragDrop.DropEvent, async (_, args) =>
        {
            var files = args.DataTransfer.TryGetFiles()?.Select(f => f.TryGetLocalPath()).OfType<string>().ToArray();
            if (files is not { Length: > 0 }) return;
            args.Handled = true; await m.AddFilesAsync(files, e.Id);
        });
        var choose = Ui.Button(s["ChooseFile"], "upload", "compact"); choose.Click += async (_, _) => { var file = await m.Interaction.FileAsync(); if (file is not null) await m.AddFilesAsync([file], e.Id); };
        var existing = Ui.Button(s["LinkExistingFile"], "link", "compact");
        existing.Click += async (_, _) =>
        {
            var attached = TripCommands.AttachedDocuments(m.Trip!, e).Select(d => d.Id).ToHashSet();
            var choices = m.Trip!.Entities.Values.Where(d => d.Type == "document" && !attached.Contains(d.Id)).OrderBy(d => DocumentPreviews.Title(m, d), StringComparer.CurrentCulture).Select(d => new Choice(d.Id.ToString(), DocumentPreviews.Title(m, d), DocumentPreviews.IsMail(d) ? "mail" : "file", DocumentPreviews.Summary(m, d))).ToArray();
            if (choices.Length == 0) { m.Interaction.Toast(s["NoOtherFiles"]); return; }
            if (await m.Interaction.ChooseAsync(s["LinkExistingFile"], choices) is { } id && m.Trip!.Find(Guid.Parse(id)) is { } document) await m.UpdateAsync(TripCommands.AttachDocument(m.Trip!.Find(e.Id)!, document));
        };
        var help = Ui.IconButton("circle-help", s["MailHelpTitle"], "small");
        help.Flyout = new Flyout { Content = new Border { MaxWidth = 380, Padding = new Thickness(4), Child = Ui.V(8, Ui.Text(s["MailHelpTitle"], "strong"), new SelectableTextBlock { Text = s["MailHelp"], TextWrapping = TextWrapping.Wrap, FontSize = 12.5 }) } };
        panel.Children.Add(drop);
        panel.Children.Add(Ui.Flow(6, choose, existing, help));
        return panel;
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
        // "Me" is remembered on this computer only; the question also offers to save the identity on the person.
        var choose = Ui.Button(me ? s["NotMe"] : s["ThisIsMe"], "user", me ? "link" : "soft"); choose.Click += async (_, _) => await m.ChooseMeCommand.ExecuteAsync(me ? null : e);
        body.Children.Add(choose.Also(b => b.HorizontalAlignment = HorizontalAlignment.Left));
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
        var noteBox = new TextBox { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 90, PlaceholderText = s["WriteNote"] }; AutomationProperties.SetName(noteBox, s["WriteNote"]);
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
        var box = new TextBox { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 70, PlaceholderText = s["WriteComment"] }; AutomationProperties.SetName(box, s["WriteComment"]);
        var send = Ui.Button(s["Comment"], "message-square", "primary", "compact");
        send.Click += async (_, _) => { if (!string.IsNullOrWhiteSpace(box.Text)) { var text = box.Text; box.Text = ""; await m.AddCommentAsync(m.Trip!.Find(e.Id), text); } };
        box.KeyDown += async (_, k) => { if (k.Key == Key.Enter && k.KeyModifiers == KeyModifiers.Control && !string.IsNullOrWhiteSpace(box.Text)) { k.Handled = true; var text = box.Text; box.Text = ""; await m.AddCommentAsync(m.Trip!.Find(e.Id), text); } };
        var who = m.Me?.Title;
        body.Children.Add(Ui.V(8, box, Ui.Columns("*,Auto", Ui.Text(who is null ? s["CommentAsHint"] : string.Format(s["CommentingAs"], who), "caption"), send)));
    }
}
