using System.Globalization;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using NodaTime;
using TravelRepo.Core;

namespace Jourfold.Desktop;

/// <summary>
/// Schedule-first timetable: days (or people) as columns, time on the vertical axis. Every block is a
/// focusable button bound to exactly one canonical schedule item; long stays and all-day items sit in
/// a strip above the grid. All edits go through the view model's undoable commands.
/// </summary>
public sealed class TimetableView : UserControl
{
    public static readonly DataFormat<string> EntityFormat = DataFormat.CreateStringApplicationFormat("org.travelrepo.entity");
    private const double Gutter = 58, Top = 10, MinColumn = 168, Snap = 15;
    private readonly MainWindow window;
    private readonly MainViewModel vm;
    private readonly Canvas canvas = new() { Background = Brushes.Transparent };
    private readonly Grid header = new();
    private readonly Canvas strip = new();
    private readonly ScrollViewer scroll;
    private readonly double hourHeight;
    private double columnWidth = 200;
    private int columns = 1;
    private Entity[] people = [];
    private DateTimeZone zone = DateTimeZone.Utc;
    private TripSnapshot trip = null!;
    private double lastWidth;

    // Pointer interaction state
    private Guid? dragId; private Point dragStart; private bool resizing; private bool moved; private double grabOffset; private int dragDayOffset; private Control? dragControl;
    private Point? createStart; private Point contextPoint;
    private readonly Border selection = new() { IsHitTestVisible = false, CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1.5) };
    private readonly TextBlock dragLabel = new() { FontSize = 11, FontWeight = FontWeight.Bold, IsHitTestVisible = false };

    public TimetableView(MainWindow owner)
    {
        window = owner; vm = owner.Model; hourHeight = 54 * vm.Zoom;
        selection.Res(Border.BackgroundProperty, "Accent.Soft").Res(Border.BorderBrushProperty, "Accent");
        dragLabel.Res(TextBlock.ForegroundProperty, "Accent.SoftText");
        scroll = new ScrollViewer { Content = canvas, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        var layout = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*") };
        layout.Children.Add(new Border { Child = header, BorderThickness = new Thickness(0, 0, 0, 1) }.Res(Border.BorderBrushProperty, "Line").Res(Border.BackgroundProperty, "Bg.Surface"));
        layout.Children.Add(new Border { Child = strip, BorderThickness = new Thickness(0, 0, 0, 1) }.Res(Border.BorderBrushProperty, "Line").Res(Border.BackgroundProperty, "Bg.Surface").Row(1));
        layout.Children.Add(scroll.Row(2));
        Content = layout; this.Res(BackgroundProperty, "Bg.Grid");
        AutomationProperties.SetName(this, vm.Strings["Timetable"]);
        canvas.Height = Top + 24 * hourHeight + 16;
        DragDrop.SetAllowDrop(canvas, true);
        canvas.AddHandler(DragDrop.DragOverEvent, (_, e) => e.DragEffects = e.DataTransfer.Contains(EntityFormat) ? DragDropEffects.Move : DragDropEffects.None);
        canvas.AddHandler(DragDrop.DropEvent, async (_, e) => { if (Guid.TryParse(e.DataTransfer.TryGetValue(EntityFormat), out var id)) { e.Handled = true; await vm.RunEditAsync(() => MoveToAsync(id, e.GetPosition(canvas), 0, keepDuration: true)); } });
        var add = new MenuItem { Header = vm.Strings["NewActivityHere"], Icon = Ui.Icon("plus", 15) };
        add.Click += async (_, _) => await CreateAsync(contextPoint, contextPoint + new Point(0, hourHeight));
        canvas.ContextMenu = new ContextMenu { Items = { add } };
        canvas.PointerPressed += OnCanvasPressed; canvas.PointerMoved += OnPointerMoved; canvas.PointerReleased += OnPointerReleased;
        canvas.DoubleTapped += async (_, e) => { if (e.Source == canvas) { var p = e.GetPosition(canvas); await CreateAsync(p, p + new Point(0, hourHeight)); } };
        SizeChanged += (_, e) => { if (Math.Abs(e.NewSize.Width - lastWidth) > 4) { lastWidth = e.NewSize.Width; Draw(); } };
        AttachedToVisualTree += (_, _) => Dispatcher.UIThread.Post(ScrollToMorning, DispatcherPriority.Loaded);
    }

    private void ScrollToMorning()
    {
        if (scroll.Offset.Y > 0) return;
        if (trip is null) Draw();
        if (trip is null) return;
        // Start near the first activity that begins in the visible days; ignore overnight continuations.
        var visibleDays = Enumerable.Range(0, vm.Lanes ? 1 : columns).Select(DayOf).ToHashSet();
        var first = Placed().Where(p => !InStrip(p) && visibleDays.Contains(p.Start.InZone(zone).Date)).Select(p => p.Start.InZone(zone).TimeOfDay.Hour).DefaultIfEmpty(8).Min();
        scroll.Offset = new Vector(0, Math.Max(0, Math.Min(first, 8) - 0.5) * hourHeight);
    }

    private sealed record Placement(Entity Item, Instant Start, Instant End, ScheduleSpan Span, int Depth, bool Group);
    private IEnumerable<Placement> Placed()
    {
        var parents = trip.Entities.Values.Where(e => e.Data["children"] is JsonArray { Count: > 0 }).ToDictionary(e => e.Id);
        int Depth(Entity e) { var d = 0; var current = e; while (TripQueries.Parent(trip, current.Id) is { } p && d < 6) { d++; current = p; } return d; }
        foreach (var e in trip.Entities.Values.Where(e => e.Type == "schedule_item"))
        {
            var span = ScheduleQueries.Span(trip, e);
            if (span.Start is not { } start || span.End is not { } end) continue;
            if (end <= start) end = start + Duration.FromMinutes(30);
            yield return new(e, start, end, span, Depth(e), parents.ContainsKey(e.Id));
        }
    }
    private static bool InStrip(Placement p) => p.Span.Precision == TimePrecision.AllDay || (p.End - p.Start) >= Duration.FromHours(20) && !p.Group;

    public void Draw()
    {
        if (vm.Workspace is null) return;
        trip = vm.Workspace.State.Trip; zone = ScheduleQueries.TripZone(trip);
        people = trip.Entities.Values.Where(e => e.Type == "person").OrderBy(p => p.Title, StringComparer.CurrentCulture).ToArray();
        var width = Math.Max(400, Bounds.Width - 14);
        columns = vm.Lanes ? people.Length + 1 : Math.Clamp((int)((width - Gutter) / MinColumn), 1, 7);
        columnWidth = (width - Gutter) / columns;
        var visible = vm.Lanes ? 1 : columns;
        if (vm.VisibleDays != visible) { vm.VisibleDays = visible; Dispatcher.UIThread.Post(window.RenderToolbar); }
        canvas.Width = width; canvas.Children.Clear(); header.Children.Clear(); strip.Children.Clear();
        DrawHeader(); DrawGrid();
        var placed = Placed().ToArray();
        var visibleStart = zone.AtStartOfDay(vm.StartDate).ToInstant(); var visibleEnd = zone.AtStartOfDay(vm.StartDate.PlusDays(vm.Lanes ? 1 : columns)).ToInstant();
        DrawStrip(placed.Where(InStrip).Where(p => p.Start < visibleEnd && p.End > visibleStart).ToArray());
        var grid = placed.Where(p => !InStrip(p)).ToArray();
        for (var column = 0; column < columns; column++) DrawColumn(column, grid);
        DrawNowLine();
        if (!placed.Any(p => p.Start < visibleEnd && p.End > visibleStart)) DrawEmptyHint();
    }

    private LocalDate DayOf(int column) => vm.Lanes ? vm.StartDate : vm.StartDate.PlusDays(column);

    private void DrawHeader()
    {
        header.ColumnDefinitions = new ColumnDefinitions(Gutter.ToString(CultureInfo.InvariantCulture) + "," + string.Join(",", Enumerable.Repeat("*", columns)));
        header.Height = vm.Lanes ? 56 : 52; header.Width = canvas.Width;
        header.HorizontalAlignment = HorizontalAlignment.Left;
        var today = SystemClock.Instance.GetCurrentInstant().InZone(zone).Date;
        for (var c = 0; c < columns; c++)
        {
            Control cell;
            if (vm.Lanes)
            {
                var name = c < people.Length ? people[c].Title : vm.Strings["Unassigned"];
                cell = Ui.H(8, c < people.Length ? Ui.Avatar(name, 26) : Ui.Icon("users", 18, "Text.Subtle"), Ui.Line(name, "title"));
            }
            else
            {
                var day = DayOf(c); var isToday = day == today;
                var number = Ui.Text(Formats.DayNumber(day), "title");
                Control date = isToday ? new Border { Child = number.Res(TextBlock.ForegroundProperty, "Text.OnAccent"), Padding = new Thickness(8, 1), CornerRadius = new CornerRadius(10) }.Res(Border.BackgroundProperty, "Accent") : number;
                cell = Ui.V(1, Ui.Text(Formats.DayHeader(day).ToUpper(CultureInfo.CurrentCulture), "overline").Also(t => { if (isToday) t.Res(TextBlock.ForegroundProperty, "Accent"); }), date);
                AutomationProperties.SetName(cell, Formats.LongDay(day));
            }
            cell.Margin = new Thickness(10, 0); cell.VerticalAlignment = VerticalAlignment.Center;
            header.Children.Add(new Border { Child = cell, BorderThickness = new Thickness(1, 0, 0, 0) }.Res(Border.BorderBrushProperty, "Line.Soft").Col(c + 1));
        }
        if (vm.Lanes) header.Children.Add(Ui.Text(Formats.ShortDate(vm.StartDate), "caption").Margin(8, 0).Also(t => t.VerticalAlignment = VerticalAlignment.Center));
    }

    private void DrawGrid()
    {
        for (var hour = 0; hour <= 24; hour++)
        {
            var y = Top + hour * hourHeight;
            canvas.Children.Add(new Line { StartPoint = new Point(Gutter - 6, y), EndPoint = new Point(canvas.Width, y), StrokeThickness = 1, IsHitTestVisible = false }.Res(Shape.StrokeProperty, "Line"));
            if (hour < 24 && hourHeight >= 40) canvas.Children.Add(new Line { StartPoint = new Point(Gutter, y + hourHeight / 2), EndPoint = new Point(canvas.Width, y + hourHeight / 2), StrokeThickness = 1, StrokeDashArray = [2, 4], IsHitTestVisible = false }.Res(Shape.StrokeProperty, "Line.Soft"));
            if (hour is > 0 and < 24) Place(Ui.Text(Formats.Time(new LocalTime(hour, 0)), "caption").Also(t => { t.FontSize = 11; t.Width = Gutter - 12; t.TextAlignment = TextAlignment.Right; t.IsHitTestVisible = false; }), 0, y - 8);
        }
        for (var c = 0; c <= columns; c++)
        {
            var x = Gutter + c * columnWidth;
            canvas.Children.Add(new Line { StartPoint = new Point(x, 0), EndPoint = new Point(x, canvas.Height), StrokeThickness = 1, IsHitTestVisible = false }.Res(Shape.StrokeProperty, "Line.Soft"));
            if (!vm.Lanes && c < columns && DayOf(c).DayOfWeek is IsoDayOfWeek.Saturday or IsoDayOfWeek.Sunday)
                Place(new Border { Width = columnWidth, Height = canvas.Height, IsHitTestVisible = false, Opacity = 0.5 }.Res(Border.BackgroundProperty, "Bg.Subtle"), x, 0, -1);
        }
    }

    private void DrawStrip(Placement[] items)
    {
        strip.Width = canvas.Width;
        var label = Ui.Text(vm.Strings["AllDay"], "caption").Also(t => { t.FontSize = 11; t.Width = Gutter - 12; t.TextAlignment = TextAlignment.Right; });
        if (items.Length == 0) { strip.Height = 0; return; }
        var rows = Enumerable.Range(0, 4).Select(_ => new List<(int From, int To)>()).ToList();
        var placed = 0;
        foreach (var item in items.OrderBy(i => i.Start).ThenByDescending(i => i.End - i.Start))
        {
            int Index(Instant instant, bool end) { var day = instant.InZone(zone).LocalDateTime; var date = end && day.TimeOfDay == LocalTime.Midnight ? day.Date.PlusDays(-1) : day.Date; return Period.Between(vm.StartDate, date, PeriodUnits.Days).Days; }
            var first = Math.Max(0, vm.Lanes ? 0 : Index(item.Start, false)); var last = Math.Min(columns - 1, vm.Lanes ? columns - 1 : Index(item.End, true));
            if (vm.Lanes) { var lanes = Lanes(item.Item); first = lanes.Min(); last = lanes.Max(); }
            if (last < first) continue;
            var row = rows.FindIndex(r => r.All(x => x.To < first || x.From > last)); if (row < 0) continue;
            rows[row].Add((first, last)); placed = Math.Max(placed, row + 1);
            var bar = Block(item, columnWidth * (last - first + 1) - 8, 24, compact: true, strip: true);
            Canvas.SetLeft(bar, Gutter + first * columnWidth + 4); Canvas.SetTop(bar, 6 + row * 28); strip.Children.Add(bar);
        }
        strip.Height = 10 + placed * 28;
        Canvas.SetLeft(label, 0); Canvas.SetTop(label, 10); strip.Children.Add(label);
    }

    private IReadOnlyList<int> Lanes(Entity item)
    {
        var assigned = ScheduleQueries.Participants(trip, item).Select(p => Array.FindIndex(people, x => x.Id == p.Id)).Where(i => i >= 0).ToArray();
        return assigned.Length == 0 ? [people.Length] : assigned;
    }

    private void DrawColumn(int column, Placement[] items)
    {
        var dayStart = zone.AtStartOfDay(vm.Lanes ? vm.StartDate : DayOf(column)).ToInstant();
        var dayEnd = zone.AtStartOfDay((vm.Lanes ? vm.StartDate : DayOf(column)).PlusDays(1)).ToInstant();
        var segments = items.Where(p => p.Start < dayEnd && p.End > dayStart && (!vm.Lanes || Lanes(p.Item).Contains(column)))
            .Select(p => (P: p, From: p.Start < dayStart ? dayStart : p.Start, To: p.End > dayEnd ? dayEnd : p.End)).OrderBy(s => s.P.Group ? 0 : 1).ThenBy(s => s.From).ThenByDescending(s => s.To - s.From).ToArray();
        double Y(Instant i) => Top + (i - dayStart).TotalHours * hourHeight;
        // Groups first, underneath their children.
        foreach (var g in segments.Where(s => s.P.Group))
        {
            // The group frame starts a little above its first step so its title stays readable.
            var box = new Border { Width = columnWidth - 6, Height = Math.Max(24, Y(g.To) - Y(g.From)) + 22, CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1.5), Opacity = 0.9 }
                .Res(Border.BorderBrushProperty, "Kind." + Visuals.For(trip, g.P.Item).KindKey + ".Bar").Res(Border.BackgroundProperty, "Bg.Surface");
            box.Child = Ui.H(5, Ui.Icon("layers", 12, "Kind." + Visuals.For(trip, g.P.Item).KindKey + ".Fg"), Ui.Line(g.P.Item.Title, "caption").Also(t => { t.FontWeight = FontWeight.SemiBold; t.Res(TextBlock.ForegroundProperty, "Kind." + Visuals.For(trip, g.P.Item).KindKey + ".Fg"); })).Also(h => { h.VerticalAlignment = VerticalAlignment.Top; h.Margin = new Thickness(8, 3); });
            var button = Wrap(g.P.Item, box, g.P.Span); Place(button, Gutter + column * columnWidth + 3, Y(g.From) - 22, 0);
        }
        // Pack overlapping leaf blocks side by side.
        var leaves = segments.Where(s => !s.P.Group).ToArray();
        var lanes = new List<Instant>(); var assignment = new int[leaves.Length]; var clusterEnd = Instant.MinValue; var clusterStart = 0;
        var widths = new int[leaves.Length];
        void CloseCluster(int end) { var count = Math.Max(1, lanes.Count); for (var k = clusterStart; k < end; k++) widths[k] = count; lanes.Clear(); }
        for (var i = 0; i < leaves.Length; i++)
        {
            var visualEnd = leaves[i].From + Duration.FromMinutes(Math.Max(30, (leaves[i].To - leaves[i].From).TotalMinutes));
            if (leaves[i].From >= clusterEnd && i > clusterStart) { CloseCluster(i); clusterStart = i; }
            var lane = lanes.FindIndex(e => e <= leaves[i].From); if (lane < 0) { lanes.Add(visualEnd); lane = lanes.Count - 1; } else lanes[lane] = visualEnd;
            assignment[i] = lane; if (visualEnd > clusterEnd) clusterEnd = visualEnd;
        }
        CloseCluster(leaves.Length);
        for (var i = 0; i < leaves.Length; i++)
        {
            var (p, from, to) = leaves[i];
            var indent = Math.Min(p.Depth, 2) * 10; var available = columnWidth - 8 - indent; var w = available / widths[i];
            var height = Math.Max(22, Y(to) - Y(from) - 2);
            var block = Block(p, w - 2, height, continued: from > p.Start, continues: to < p.End);
            Place(block, Gutter + column * columnWidth + 4 + indent + assignment[i] * w, Y(from) + 1, 2);
        }
    }

    private Control Block(Placement p, double width, double height, bool compact = false, bool strip = false, bool continued = false, bool continues = false)
    {
        var s = vm.Strings; var item = p.Item; var (icon, kind) = Visuals.For(trip, item);
        var status = item.Data["status"]?.ToString();
        var selected = vm.Selected?.Id == item.Id;
        var start = p.Start.InZone(zone); var end = p.End.InZone(zone);
        var approximate = p.Span.Precision is TimePrecision.Approximate or TimePrecision.Window;
        string timeText = p.Span.Precision switch
        {
            TimePrecision.DayPart => s[p.Span.DayPart ?? "day_part"],
            TimePrecision.AllDay => s["AllDay"],
            TimePrecision.Window => string.Format(s["BetweenTimes"], Formats.Time(start.TimeOfDay), Formats.Time(end.TimeOfDay)),
            _ => (approximate ? "~" : "") + Formats.Time(start.TimeOfDay) + " – " + Formats.Time(end.TimeOfDay)
        };
        if (strip && p.Span.Precision != TimePrecision.AllDay) timeText = Formats.ShortDate(start.Date) + " – " + Formats.ShortDate(end.Date);
        var title = new TextBlock { Text = (continued ? "↳ " : "") + item.Title, FontWeight = FontWeight.SemiBold, FontSize = 12.5, TextWrapping = compact || height < 40 ? TextWrapping.NoWrap : TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis, MaxHeight = Math.Max(16, height - 26) }
            .Res(TextBlock.ForegroundProperty, "Text");
        if (status == "cancelled") title.TextDecorations = TextDecorations.Strikethrough;
        var time = new TextBlock { Text = timeText, FontSize = 11, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis }.Res(TextBlock.ForegroundProperty, "Kind." + kind + ".Fg");
        var warn = vm.Diagnostics.Any(d => d.Path.StartsWith(item.Id.ToString(), StringComparison.Ordinal) && d.Severity >= Severity.Warning);
        var badges = Ui.H(3, warn ? Ui.Icon("triangle-alert", 12, "Warning") : null, status is "confirmed" or "completed" ? Ui.Icon("circle-check", 12, "Success") : null, item.Data["content"] is JsonArray { Count: > 0 } ? Ui.Icon("paperclip", 11, "Text.Subtle") : null);
        Control body;
        if (compact || height < 38)
            body = Ui.Columns("Auto,*,Auto", Ui.Icon(icon, 13, "Kind." + kind + ".Fg"), Ui.H(6, title, strip ? time : null).Margin(5, 0), badges);
        else
        {
            var stack = Ui.V(2, Ui.Columns("Auto,*,Auto", Ui.Icon(icon, 13, "Kind." + kind + ".Fg"), time.Margin(5, 0, 2, 0), badges), title);
            var route = ScheduleQueries.Route(trip, item); var place = ScheduleQueries.PrimaryPlace(trip, item);
            var where = route.From is not null && route.To is not null ? route.From.Title + " → " + route.To.Title : place?.Title;
            if (where is not null && height >= 62) stack.Children.Add(Ui.Line(where, "caption").Also(t => t.FontSize = 11));
            if (p.Span.StartZone is { } z && z != zone.Id && height >= 62) stack.Children.Add(Ui.Line(string.Format(s["LocalTimeIn"], Formats.Time(p.Start.InZone(DateTimeZoneProviders.Tzdb[z]).TimeOfDay), Formats.ZoneName(z)), "caption").Also(t => t.FontSize = 11));
            var participants = ScheduleQueries.Participants(trip, item);
            if (participants.Count > 0 && height >= 84 && width >= 120 && !vm.Lanes) stack.Children.Add(Ui.AvatarStack(participants.Select(x => x.Title), 18, 3).Margin(0, 3, 0, 0));
            body = stack;
        }
        var dashed = status is "idea" || approximate || p.Span.Precision == TimePrecision.DayPart;
        var border = new Border
        {
            Width = Math.Max(20, width), Height = height, CornerRadius = new CornerRadius(strip ? 6 : 8), Padding = new Thickness(compact || height < 38 ? 7 : 9, compact || height < 38 ? 2 : 6, 6, 4),
            BorderThickness = new Thickness(selected ? 2 : 1, selected ? 2 : 1, selected ? 2 : 1, selected ? 2 : 1), Child = body, ClipToBounds = true,
            BoxShadow = selected ? BoxShadows.Parse("0 4 14 0 #330F2A3D") : default
        }.Res(Border.BackgroundProperty, "Kind." + kind + ".Bg");
        if (dashed) border.BorderBrush = Brushes.Transparent; else border.Res(Border.BorderBrushProperty, selected ? "Accent" : "Kind." + kind + ".Bar");
        var accent = new Border { Width = 3, CornerRadius = new CornerRadius(2), Margin = new Thickness(0, 4), HorizontalAlignment = HorizontalAlignment.Left, IsHitTestVisible = false }.Res(Border.BackgroundProperty, "Kind." + kind + ".Bar");
        var layered = new Panel { Children = { border, accent } };
        if (dashed)
            layered.Children.Add(new Rectangle { StrokeThickness = 1.2, StrokeDashArray = [4, 3], RadiusX = 8, RadiusY = 8, Width = border.Width, Height = height, IsHitTestVisible = false }.Res(Shape.StrokeProperty, selected ? "Accent" : "Kind." + kind + ".Bar"));
        if (status == "cancelled") layered.Opacity = 0.55;
        if (continues && !compact) layered.Children.Add(Ui.Icon("chevron-down", 12, "Text.Subtle").Also(i => { i.HorizontalAlignment = HorizontalAlignment.Center; i.VerticalAlignment = VerticalAlignment.Bottom; }));
        return Wrap(item, layered, p.Span, resizable: !strip && !compact && p.Span.Precision is TimePrecision.Exact or TimePrecision.Approximate && !p.Group);
    }

    private Button Wrap(Entity item, Control visual, ScheduleSpan span, bool resizable = false)
    {
        var s = vm.Strings;
        var button = new Button { Content = visual, Padding = new Thickness(0), Background = Brushes.Transparent, BorderThickness = new Thickness(0), MinHeight = 0, CornerRadius = new CornerRadius(8), Tag = item.Id, Cursor = new Cursor(StandardCursorType.Hand) }.Classed("block");
        var when = span.Start is { } a && span.End is { } b ? Formats.LongDay(a.InZone(zone).Date) + ", " + Formats.Time(a.InZone(zone).TimeOfDay) + " – " + Formats.Time(b.InZone(zone).TimeOfDay) : "";
        AutomationProperties.SetName(button, item.Title + ", " + when + ", " + s.Optional(item.Data["status"]?.ToString() ?? ""));
        ToolTip.SetTip(button, item.Title + "\n" + when);
        button.Click += (_, _) => { if (!moved) vm.Selected = vm.Workspace?.State.Trip.Find(item.Id); };
        button.DoubleTapped += (_, _) => _ = vm.ScheduleSelectedAsync();
        button.KeyDown += async (_, e) =>
        {
            if (e.KeyModifiers == KeyModifiers.Alt && e.Key is Key.Up or Key.Down) { e.Handled = true; await vm.RunEditAsync(() => ShiftAsync(item.Id, Duration.FromMinutes(e.Key == Key.Up ? -15 : 15))); }
            if (e.Key == Key.Enter) { vm.Selected = vm.Workspace?.State.Trip.Find(item.Id); e.Handled = true; }
        };
        var menu = new ContextMenu();
        MenuItem Item(string text, string icon, Action action) { var mi = new MenuItem { Header = text, Icon = Ui.Icon(icon, 15) }; mi.Click += (_, _) => action(); return mi; }
        menu.Items.Add(Item(s["EditTime"], "clock", () => { vm.Selected = vm.Workspace?.State.Trip.Find(item.Id); _ = vm.ScheduleSelectedAsync(); }));
        menu.Items.Add(Item(s["Duplicate"], "copy", () => _ = vm.DuplicateAsync(item.Id)));
        menu.Items.Add(Item(s["MoveToInbox"], "inbox", () => _ = vm.UpdateAsync(item.Id, e => { e.Data["time"] = null; if (e.Data["status"]?.ToString() == "planned") e.Data["status"] = "idea"; })));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item(s["Delete"], "trash-2", () => _ = vm.DeleteAsync(item.Id)));
        button.ContextMenu = menu;
        button.AddHandler(PointerPressedEvent, (_, e) =>
        {
            var point = e.GetCurrentPoint(button);
            if (!point.Properties.IsLeftButtonPressed || e.ClickCount > 1) return;
            dragId = item.Id; dragStart = e.GetPosition(canvas); moved = false; dragControl = button.Parent is Canvas ? button : null;
            grabOffset = e.GetPosition(button).Y; resizing = resizable && e.GetPosition(button).Y > button.Bounds.Height - 8;
            var segmentLeft = Canvas.GetLeft(button); dragDayOffset = vm.Lanes ? 0 : (int)((segmentLeft - Gutter) / columnWidth) - DayIndex(span.Start);
            e.Pointer.Capture(canvas);
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        if (resizable) button.PointerMoved += (_, e) => { if (dragId is null) button.Cursor = new Cursor(e.GetPosition(button).Y > button.Bounds.Height - 8 ? StandardCursorType.SizeNorthSouth : StandardCursorType.Hand); };
        return button;
    }

    private int DayIndex(Instant? instant) => instant is { } i ? Period.Between(vm.StartDate, i.InZone(zone).Date, PeriodUnits.Days).Days : 0;

    private void Place(Control control, double x, double y, int z = 1) { Canvas.SetLeft(control, x); Canvas.SetTop(control, y); control.ZIndex = z; canvas.Children.Add(control); }

    private void DrawNowLine()
    {
        var now = SystemClock.Instance.GetCurrentInstant().InZone(zone);
        var column = vm.Lanes ? (now.Date == vm.StartDate ? 0 : -1) : Period.Between(vm.StartDate, now.Date, PeriodUnits.Days).Days;
        if (column < 0 || column >= columns) return;
        var y = Top + now.TimeOfDay.TickOfDay / (double)NodaConstants.TicksPerHour * hourHeight;
        var x = vm.Lanes ? Gutter : Gutter + column * columnWidth; var w = vm.Lanes ? columnWidth * columns : columnWidth;
        Place(new Line { StartPoint = new Point(x, y), EndPoint = new Point(x + w, y), StrokeThickness = 2, IsHitTestVisible = false }.Res(Shape.StrokeProperty, "NowLine"), 0, 0, 5);
        Place(new Ellipse { Width = 9, Height = 9, IsHitTestVisible = false }.Res(Shape.FillProperty, "NowLine"), x - 4, y - 4, 5);
    }

    private void DrawEmptyHint()
    {
        var s = vm.Strings;
        var card = Ui.Card(Ui.Columns("Auto,*", Ui.Tile("calendar-plus", "Activity", 36), Ui.V(2, Ui.Text(s["EmptyDaysTitle"], "title"), Ui.Text(s["EmptyDaysBody"], "caption")).Margin(12, 0, 0, 0)), 14);
        card.Width = Math.Min(460, canvas.Width - Gutter - 40); card.IsHitTestVisible = false;
        Place(card, Gutter + (canvas.Width - Gutter - card.Width) / 2, Top + 9 * hourHeight, 6);
    }

    // Pointer interaction ----------------------------------------------------------------------------------

    private void OnCanvasPressed(object? sender, PointerPressedEventArgs e)
    {
        var point = e.GetPosition(canvas); contextPoint = point;
        if (!e.GetCurrentPoint(canvas).Properties.IsLeftButtonPressed || dragId is not null || point.X < Gutter || e.ClickCount > 1) return;
        createStart = point; e.Pointer.Capture(canvas);
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        var p = e.GetPosition(canvas);
        if (createStart is { } anchor)
        {
            var (from, to) = SnapRange(anchor.Y, p.Y);
            var column = Math.Clamp((int)((anchor.X - Gutter) / columnWidth), 0, columns - 1);
            selection.Width = columnWidth - 8; selection.Height = Math.Max(10, (to - from) / 60d * hourHeight);
            if (!canvas.Children.Contains(selection)) { Place(selection, 0, 0, 8); Place(dragLabel, 0, 0, 9); }
            Canvas.SetLeft(selection, Gutter + column * columnWidth + 4); Canvas.SetTop(selection, Top + from / 60d * hourHeight);
            dragLabel.Text = Formats.Time(new LocalTime(from / 60, from % 60)) + " – " + (to >= 1440 ? "24:00" : Formats.Time(new LocalTime(to / 60, to % 60)));
            Canvas.SetLeft(dragLabel, Gutter + column * columnWidth + 12); Canvas.SetTop(dragLabel, Top + from / 60d * hourHeight + 6);
            e.Handled = true;
        }
        if (dragId is not null && dragControl is not null)
        {
            if (!moved && Math.Abs(p.X - dragStart.X) + Math.Abs(p.Y - dragStart.Y) < 5) return;
            moved = true; e.Handled = true;
            if (resizing) { dragControl.Height = Math.Max(22, dragControl.Height + 0); var delta = Math.Round((p.Y - dragStart.Y) / hourHeight * 60 / Snap) * Snap; dragControl.RenderTransform = null; ShowGhostLabel(p, ((int)delta >= 0 ? "+" : "") + (int)delta + " min"); }
            else
            {
                var dx = vm.Lanes ? 0 : Math.Round((p.X - dragStart.X) / columnWidth) * columnWidth;
                var dy = Math.Round((p.Y - dragStart.Y) / hourHeight * 60 / Snap) * Snap / 60 * hourHeight;
                dragControl.RenderTransform = new TranslateTransform(dx, dy); dragControl.Opacity = 0.85; dragControl.ZIndex = 10;
                var minute = MinuteAt(p.Y - grabOffset); ShowGhostLabel(new Point(Canvas.GetLeft(dragControl) + dx, Canvas.GetTop(dragControl) + dy - 18), Formats.Time(new LocalTime(minute / 60, minute % 60)));
            }
        }
    }
    private void ShowGhostLabel(Point at, string text)
    {
        dragLabel.Text = text; if (!canvas.Children.Contains(dragLabel)) Place(dragLabel, 0, 0, 11);
        Canvas.SetLeft(dragLabel, at.X + 8); Canvas.SetTop(dragLabel, Math.Max(0, at.Y));
    }

    private async void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        var p = e.GetPosition(canvas);
        if (createStart is { } anchor)
        {
            createStart = null; canvas.Children.Remove(selection); canvas.Children.Remove(dragLabel); e.Pointer.Capture(null);
            if (Math.Abs(p.Y - anchor.Y) >= 8) await CreateAsync(anchor, p);
            e.Handled = true; return;
        }
        if (dragId is not { } id) return;
        dragId = null; e.Pointer.Capture(null); canvas.Children.Remove(dragLabel);
        if (!moved) { dragControl = null; return; }
        e.Handled = true; dragControl = null;
        await vm.RunEditAsync(async () =>
        {
            if (resizing) await ResizeAsync(id, Duration.FromMinutes(Math.Round((p.Y - dragStart.Y) / hourHeight * 60 / Snap) * Snap));
            else await MoveToAsync(id, new Point(p.X, p.Y - grabOffset), dragDayOffset, keepDuration: true, deltaX: p.X - dragStart.X);
        });
        Dispatcher.UIThread.Post(() => moved = false, DispatcherPriority.Background);
    }

    private int MinuteAt(double y) => (int)Math.Clamp(Math.Round((y - Top) / hourHeight * 60 / Snap) * Snap, 0, 1440 - Snap);
    private (int From, int To) SnapRange(double a, double b)
    {
        var from = (int)Math.Clamp(Math.Floor((Math.Min(a, b) - Top) / hourHeight * 60 / Snap) * Snap, 0, 1440 - Snap);
        var to = (int)Math.Clamp(Math.Ceiling((Math.Max(a, b) - Top) / hourHeight * 60 / Snap) * Snap, from + Snap, 1440);
        return (from, to);
    }

    private Task CreateAsync(Point a, Point b)
    {
        var (from, to) = SnapRange(a.Y, b.Y);
        var column = Math.Clamp((int)((a.X - Gutter) / columnWidth), 0, columns - 1);
        var day = DayOf(column);
        var start = ZonedTime.At(zone.AtLeniently(day.AtMidnight().PlusMinutes(from)).ToInstant(), zone.Id);
        Guid? person = vm.Lanes && column < people.Length ? people[column].Id : null;
        return vm.AddScheduledAsync(start, Duration.FromMinutes(to - from), person);
    }

    /// <summary>Move an item (or schedule Inbox material) so it starts at the pointer, keeping its duration and own timezones.</summary>
    private async Task MoveToAsync(Guid id, Point p, int continuationDays, bool keepDuration, double deltaX = 0)
    {
        if (vm.Workspace is null) return; var entity = vm.Workspace.State.Trip.Find(id); if (entity?.Type is not ("schedule_item" or "note" or "document")) return;
        var span = ScheduleQueries.Span(vm.Workspace.State.Trip, entity);
        var column = vm.Lanes ? 0 : Math.Clamp((int)((p.X - Gutter) / columnWidth), 0, columns - 1);
        var minute = MinuteAt(p.Y);
        var day = vm.Lanes ? vm.StartDate : DayOf(column).PlusDays(-continuationDays);
        var startInstant = zone.AtLeniently(day.At(new LocalTime(minute / 60, minute % 60))).ToInstant();
        var duration = span.Length ?? ScheduleQueries.PlannedDuration(entity) ?? Duration.FromHours(1);
        if (entity.Type == "schedule_item" && span.Precision is TimePrecision.Exact or TimePrecision.Approximate && entity.Data["time"] is JsonObject time)
        {
            var copy = entity.Copy(); var t = (JsonObject)copy.Data["time"]!;
            var startZone = t["start"]?["timezone"]?.ToString() ?? zone.Id; var endZone = t["end"]?["timezone"]?.ToString() ?? startZone;
            t["start"] = ZonedTime.At(startInstant, startZone).ToJson(); t["end"] = ZonedTime.At(startInstant + duration, endZone).ToJson();
            if (copy.Data["components"]?["accommodation"] is JsonObject stay) { stay["check_in"] = t["start"]!.DeepClone(); stay["check_out"] = t["end"]!.DeepClone(); }
            await vm.Workspace.EditAsync(copy); return;
        }
        var source = entity.Data["time"]?["start"]?["timezone"]?.ToString() ?? zone.Id;
        var start = ZonedTime.At(startInstant, source);
        var scheduled = await vm.Workspace.PlanMaterialAsync(id, new JsonObject { ["precision"] = "exact", ["start"] = start.ToJson(), ["end"] = ZonedTime.At(startInstant + duration, source).ToJson() });
        var result = vm.Workspace.State.Trip.Find(scheduled)!;
        if (result.Data["status"]?.ToString() == "idea") { var planned = result.Copy(); planned.Data["status"] = "planned"; await vm.Workspace.EditAsync(planned); }
        vm.Selected = vm.Workspace.State.Trip.Find(scheduled);
    }

    private async Task ResizeAsync(Guid id, Duration delta)
    {
        if (vm.Workspace?.State.Trip.Find(id) is not { } entity || entity.Data["time"] is not JsonObject time || time["start"] is not JsonObject startNode) return;
        var span = ScheduleQueries.Span(vm.Workspace.State.Trip, entity); if (span.Start is not { } a || span.End is not { } b) return;
        var end = b + delta; if (end - a < Duration.FromMinutes(Snap)) end = a + Duration.FromMinutes(Snap);
        var copy = entity.Copy(); var t = (JsonObject)copy.Data["time"]!;
        t["end"] = ZonedTime.At(end, t["end"]?["timezone"]?.ToString() ?? startNode["timezone"]!.ToString()).ToJson();
        await vm.Workspace.EditAsync(copy);
    }

    private async Task ShiftAsync(Guid id, Duration delta)
    {
        if (vm.Workspace?.State.Trip.Find(id) is not { } entity || entity.Data["time"] is not JsonObject) return;
        var copy = entity.Copy(); var t = (JsonObject)copy.Data["time"]!;
        foreach (var key in new[] { "start", "end", "earliest", "latest" })
            if (t[key] is JsonObject node) { var z = ZonedTime.From(node); t[key] = ZonedTime.At(z.ToInstant() + delta, z.Timezone).ToJson(); }
        await vm.Workspace.EditAsync(copy);
        Dispatcher.UIThread.Post(() => window.FindDescendant<Button>(b => b.Tag is Guid g && g == id)?.Focus(), DispatcherPriority.Background);
    }
}
