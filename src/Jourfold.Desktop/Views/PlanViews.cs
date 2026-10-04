using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using NodaTime;
using TravelRepo.Core;

namespace Jourfold.Desktop;

/// <summary>Compact day-by-day agenda of the plan.</summary>
public static class AgendaView
{
    public static Control Build(MainWindow w)
    {
        var m = w.Model; var s = m.Strings; var trip = m.Trip!; var zone = ScheduleQueries.TripZone(trip);
        var items = trip.Entities.Values.Where(e => e.Type == "schedule_item" && e.Data["children"] is not JsonArray { Count: > 0 })
            .Select(e => (Item: e, Span: ScheduleQueries.Span(trip, e))).ToArray();
        var placed = items.Where(x => x.Span.IsPlaced).OrderBy(x => x.Span.Start).ToArray();
        var panel = Ui.V(22); panel.Margin = new Thickness(28, 20, 28, 40); panel.MaxWidth = 980;
        if (placed.Length == 0 && items.Length == 0)
        {
            var add = Ui.Button(s["AddActivity"], "plus", "primary"); add.Command = m.QuickAddCommand;
            return Ui.EmptyState("list", s["EmptyPlanTitle"], s["EmptyPlanBody"], add);
        }
        var today = SystemClock.Instance.GetCurrentInstant().InZone(zone).Date;
        foreach (var day in placed.GroupBy(x => x.Span.Start!.Value.InZone(zone).Date))
        {
            var dayIndex = trip.Manifest.Data["dates"]?["start"] is { } st && NodaTime.Text.LocalDatePattern.Iso.Parse(st.ToString()).TryGetValue(default, out var first) ? Period.Between(first, day.Key, PeriodUnits.Days).Days + 1 : (int?)null;
            var heading = Ui.H(10, Ui.Text(Formats.LongDay(day.Key), "h3"), dayIndex is > 0 ? Ui.Pill(string.Format(s["DayN"], dayIndex), null, "Bg.Subtle", "Text.Muted") : null, day.Key == today ? Ui.Pill(s["Today"], null, "Accent.Soft", "Accent.SoftText") : null);
            var rows = Ui.V(2);
            foreach (var (item, span) in day) rows.Children.Add(Row(w, item, span));
            panel.Children.Add(Ui.V(8, heading, Ui.Card(rows, 6)));
        }
        var unplaced = items.Where(x => !x.Span.IsPlaced).ToArray();
        if (unplaced.Length > 0)
        {
            var rows = Ui.V(2); foreach (var (item, span) in unplaced) rows.Children.Add(Row(w, item, span));
            panel.Children.Add(Ui.V(8, Ui.H(10, Ui.Text(s["NotScheduledYet"], "h3"), Ui.Pill(unplaced.Length.ToString(System.Globalization.CultureInfo.CurrentCulture), null, "Bg.Subtle", "Text.Muted")), Ui.Card(rows, 6)));
        }
        return new ScrollViewer { Content = new Border { Child = panel, HorizontalAlignment = HorizontalAlignment.Center } };
    }

    public static Button Row(MainWindow w, Entity item, ScheduleSpan span)
    {
        var m = w.Model; var s = m.Strings; var trip = m.Trip!; var zone = ScheduleQueries.TripZone(trip); var (icon, kind) = Visuals.For(trip, item);
        string time = span.Precision switch
        {
            TimePrecision.Unscheduled => ScheduleQueries.PlannedDuration(item) is { } d ? Formats.Duration(d, s) : "—",
            TimePrecision.AllDay => s["AllDay"],
            TimePrecision.DayPart => s[span.DayPart ?? "day_part"],
            _ => (span.Precision == TimePrecision.Approximate ? "~" : "") + Formats.Time(span.Start!.Value.InZone(zone).TimeOfDay)
        };
        var end = span.Precision is TimePrecision.Exact or TimePrecision.Approximate or TimePrecision.Window && span.End is { } e ? Formats.Time(e.InZone(zone).TimeOfDay) : "";
        var route = ScheduleQueries.Route(trip, item); var place = ScheduleQueries.PrimaryPlace(trip, item);
        var where = route.From is not null && route.To is not null ? route.From.Title + " → " + route.To.Title : place?.Title;
        var people = ScheduleQueries.Participants(trip, item).Select(p => p.Title).ToArray();
        var status = item.Data["status"]?.ToString() ?? "idea"; var (statusIcon, bg, fg) = Visuals.Status(status);
        var grid = Ui.Columns("74,Auto,*,Auto,Auto",
            Ui.V(0, Ui.Text(time, "strong"), end.Length > 0 ? Ui.Text(end, "caption") : null),
            Ui.Tile(icon, kind, 34).Margin(0, 0, 12, 0),
            Ui.V(1, Ui.Line(item.Title, "title"), where is null ? null : Ui.Line(where, "caption")),
            people.Length > 0 ? Ui.AvatarStack(people, 24).Margin(10, 0) : null,
            Ui.Pill(s[status], statusIcon, bg, fg));
        var button = new Button { Content = grid }.Classed("row").Named(item.Title);
        if (m.Selected?.Id == item.Id) button.Classes.Add("selected");
        button.Click += (_, _) => m.Selected = m.Workspace?.State.Trip.Find(item.Id);
        return button;
    }
}

/// <summary>The collapsible, pinnable Inbox: unscheduled ideas, notes and loose files. Items drag onto the timetable.</summary>
public static class InboxView
{
    private static string filter = "All";
    public static Control Build(MainWindow w)
    {
        var m = w.Model; var s = m.Strings; var trip = m.Trip!;
        var pin = Ui.IconButton(m.InboxPinned ? "pin-off" : "pin", m.InboxPinned ? s["Unpin"] : s["Pin"], "small"); pin.Click += (_, _) => m.InboxPinned = !m.InboxPinned;
        if (m.InboxPinned) pin.Res(Button.ForegroundProperty, "Accent");
        var close = Ui.IconButton("x", s["Close"], "small"); close.Click += (_, _) => { m.InboxOpen = false; m.InboxPinned = false; };
        var header = Ui.Columns("Auto,*,Auto,Auto", Ui.Icon("inbox", 17, "Accent"), Ui.H(8, Ui.Text(s["Inbox"], "h3"), Ui.Pill(m.InboxItems.Count.ToString(System.Globalization.CultureInfo.CurrentCulture), null, "Accent.Soft", "Accent.SoftText")).Margin(8, 0), pin, close);
        var chips = Ui.H(4);
        foreach (var (key, label) in new[] { ("All", s["All"]), ("Ideas", s["Ideas"]), ("Notes", s["Notes"]), ("Files", s["Files"]) })
        {
            var chip = Ui.Button(label, null, "chip"); if (filter == key) chip.Classes.Add("selected");
            chip.Click += (_, _) => { filter = key; w.RenderMain(); }; chips.Children.Add(chip);
        }
        var capture = new TextBox { Watermark = s["CaptureIdea"] }; AutomationProperties.SetName(capture, s["CaptureIdea"]);
        capture.KeyDown += async (_, e) => { if (e.Key == Key.Enter && !string.IsNullOrWhiteSpace(capture.Text)) { var text = capture.Text; capture.Text = ""; e.Handled = true; await m.CaptureIdeaAsync(text); } };
        var list = Ui.V(6);
        var items = m.InboxItems.Where(e => filter switch { "Ideas" => e.Type == "schedule_item", "Notes" => e.Type == "note", "Files" => e.Type == "document", _ => true }).ToArray();
        foreach (var item in items) list.Children.Add(Card(w, item));
        if (items.Length == 0) list.Children.Add(Ui.Text(s["InboxEmpty"], "caption").Margin(4, 12));
        var hint = Ui.H(6, Ui.Icon("grip-vertical", 13, "Text.Subtle"), Ui.Text(s["InboxDragHint"], "caption")).Margin(2, 0);
        var body = new DockPanel();
        var top = Ui.V(12, header, capture, chips); top.Margin = new Thickness(14, 14, 14, 10); DockPanel.SetDock(top, Dock.Top); body.Children.Add(top);
        DockPanel.SetDock(hint, Dock.Bottom); hint.Margin = new Thickness(16, 8, 16, 12); body.Children.Add(hint);
        body.Children.Add(new ScrollViewer { Content = list.Margin(12, 0, 12, 0) });
        return new Border { Width = 290, Child = body, BorderThickness = new Thickness(0, 0, 1, 0) }.Res(Border.BackgroundProperty, "Bg.Surface").Res(Border.BorderBrushProperty, "Line");
    }

    private static Control Card(MainWindow w, Entity item)
    {
        var m = w.Model; var s = m.Strings; var trip = m.Trip!; var (icon, kind) = Visuals.For(trip, item);
        var subtitle = item.Type switch
        {
            "schedule_item" => ScheduleQueries.PlannedDuration(item) is { } d ? s["Idea"] + " · " + Formats.Duration(d, s) : s["Idea"],
            "note" => s["note"],
            "document" => item.Data["media_type"]?.ToString() == "application/pdf" ? "PDF" : s["document"],
            _ => s[item.Type]
        };
        var content = Ui.Columns("Auto,*", Ui.Tile(icon, kind, 30), Ui.V(1, Ui.Line(item.Title, "title"), Ui.Line(subtitle, "caption")).Margin(10, 0, 0, 0));
        var button = new Button { Content = content, Padding = new Thickness(10, 8), HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch }.Classed("card").Named(item.Title + ", " + subtitle);
        button.CornerRadius = new CornerRadius(10);
        if (m.Selected?.Id == item.Id) button.Classes.Add("selected");
        button.Click += (_, _) => m.Selected = m.Workspace?.State.Trip.Find(item.Id);
        PointerPressedEventArgs? press = null; Point start = default;
        button.AddHandler(InputElement.PointerPressedEvent, (_, e) => { if (e.GetCurrentPoint(button).Properties.IsLeftButtonPressed) { press = e; start = e.GetPosition(button); } }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        button.AddHandler(InputElement.PointerReleasedEvent, (_, _) => press = null, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        button.PointerMoved += async (_, e) =>
        {
            if (press is not { } pressed || !e.GetCurrentPoint(button).Properties.IsLeftButtonPressed) return;
            var p = e.GetPosition(button); if (Math.Abs(p.X - start.X) + Math.Abs(p.Y - start.Y) < 6) return;
            press = null; var data = new DataTransfer(); data.Add(DataTransferItem.Create(TimetableView.EntityFormat, item.Id.ToString()));
            await DragDrop.DoDragDropAsync(pressed, data, DragDropEffects.Move);
        };
        var menu = new ContextMenu();
        var schedule = new MenuItem { Header = s["Schedule"], Icon = Ui.Icon("calendar-plus", 15) }; schedule.Click += (_, _) => { m.Selected = m.Workspace?.State.Trip.Find(item.Id); _ = m.ScheduleSelectedAsync(); }; menu.Items.Add(schedule);
        var delete = new MenuItem { Header = s["Delete"], Icon = Ui.Icon("trash-2", 15) }; delete.Click += (_, _) => _ = m.DeleteAsync(item.Id); menu.Items.Add(delete);
        button.ContextMenu = menu;
        return button;
    }
}
