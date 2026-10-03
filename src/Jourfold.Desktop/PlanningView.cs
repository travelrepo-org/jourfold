using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using NodaTime;
using NodaTime.Text;
using TravelRepo.Core;
namespace Jourfold.Desktop;

/// <summary>Accessible entity buttons positioned by domain time or local coordinates.</summary>
public sealed class PlanningView : UserControl
{
    public static readonly DataFormat<string> EntityFormat = DataFormat.CreateStringApplicationFormat("org.travelrepo.entity");
    private readonly MainViewModel vm;
    private readonly Canvas canvas = new();
    private readonly Canvas header = new() { Height = 32 };
    private readonly bool map;
    private double columnWidth = 190;
    private readonly double hourHeight;
    private Guid? dragId;
    private Point dragStart;
    private bool resize;
    private double dragGrabOffset;
    private int dragContinuationDays;
    private Point? createStart;
    private Point contextPoint;
    private readonly Border selection = new() { Background = new SolidColorBrush(Color.FromArgb(70, 8, 127, 140)), BorderBrush = new SolidColorBrush(Color.Parse("#087F8C")), BorderThickness = new Thickness(1), IsHitTestVisible = false };
    public PlanningView(MainViewModel model, bool isMap)
    {
        vm = model; map = isMap; hourHeight = 55 * vm.Zoom; var layout = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        var scroll = new ScrollViewer { Content = canvas, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto };
        Grid.SetRow(scroll, 1); layout.Children.Add(header); layout.Children.Add(scroll); Content = layout;
        canvas.Background = Brushes.Transparent; canvas.Width = 650; canvas.Height = map ? 600 : hourHeight * 24 + 40;
        DragDrop.SetAllowDrop(canvas, true); canvas.AddHandler(DragDrop.DragOverEvent, (_, e) => e.DragEffects = e.DataTransfer.Contains(EntityFormat) ? DragDropEffects.Move : DragDropEffects.None);
        canvas.AddHandler(DragDrop.DropEvent, async (_, e) => { if (Guid.TryParse(e.DataTransfer.TryGetValue(EntityFormat), out var id)) await vm.RunAsync(() => ScheduleAt(id, e.GetPosition(canvas))); });
        if (!map)
        {
            var add = new MenuItem { Header = vm.Strings["NewActivity"] };
            add.Click += async (_, _) => await CreateAtAsync(contextPoint, contextPoint + new Point(0, hourHeight));
            canvas.ContextMenu = new ContextMenu { ItemsSource = new[] { add } };
            canvas.PointerPressed += (_, e) =>
            {
                var point = e.GetPosition(canvas); contextPoint = point;
                if (!e.GetCurrentPoint(canvas).Properties.IsLeftButtonPressed || dragId is not null || point.X < 50) return;
                createStart = point; e.Pointer.Capture(canvas); e.Handled = true;
            };
        }
        canvas.PointerMoved += (_, e) =>
        {
            if (createStart is { } anchor)
            {
                var p = e.GetPosition(canvas); var top = Math.Clamp(Math.Min(anchor.Y, p.Y), 40, 40 + 24 * hourHeight);
                selection.Width = columnWidth - 10; selection.Height = Math.Max(8, Math.Min(Math.Abs(p.Y - anchor.Y), 24 * hourHeight));
                Canvas.SetLeft(selection, 55 + Math.Max(0, (int)((anchor.X - 50) / columnWidth)) * columnWidth); Canvas.SetTop(selection, top);
                if (!canvas.Children.Contains(selection)) canvas.Children.Add(selection); e.Handled = true;
            }
            if (dragId is not null) e.Handled = true;
        };
        canvas.PointerReleased += async (_, e) =>
        {
            if (createStart is { } anchor)
            {
                createStart = null; canvas.Children.Remove(selection); e.Pointer.Capture(null); var end = e.GetPosition(canvas);
                if (Math.Abs(end.Y - anchor.Y) >= 8) await CreateAtAsync(anchor, end);
                e.Handled = true; return;
            }
            if (dragId is not { } id) return; dragId = null; e.Pointer.Capture(null); var p = e.GetPosition(canvas);
            if (Math.Abs(p.X - dragStart.X) + Math.Abs(p.Y - dragStart.Y) < 5) return;
            await vm.RunAsync(async () =>
            {
                if (resize && vm.Workspace?.State.Trip.Find(id)?.Data["time"]?["start"] is { } start) { var zoned = ZonedTime.From(start); var minutes = Math.Max(15, Math.Round((p.Y - dragStart.Y) / hourHeight * 60 / 15) * 15 + DurationOf(id).TotalMinutes); await vm.Workspace.ScheduleAsync(id, zoned, Duration.FromMinutes(minutes)); }
                else await ScheduleAt(id, new Point(p.X, p.Y - dragGrabOffset), dragContinuationDays);
            });
        };
        SizeChanged += (_, e) => { if (e.NewSize.Width > 0) { canvas.Width = Math.Max(500, e.NewSize.Width - 20); Draw(); } }; Draw();
    }
    private void Text(string text, double x, double y, double size = 12) { var block = new TextBlock { Text = text, FontSize = size }; Canvas.SetLeft(block, x); Canvas.SetTop(block, y); if (y <= 10) header.Children.Add(block); else canvas.Children.Add(block); }
    private void Line(double x1, double y1, double x2, double y2)
    { canvas.Children.Add(new Avalonia.Controls.Shapes.Line { StartPoint = new Point(x1, y1), EndPoint = new Point(x2, y2), Stroke = new SolidColorBrush(Color.Parse("#74909E")), Opacity = .25, StrokeThickness = 1 }); }
    private void Draw()
    {
        canvas.Children.Clear(); header.Children.Clear(); if (vm.Workspace is null) return;
        if (map) { DrawMap(); return; }
        var people = vm.Workspace.State.Trip.Entities.Values.Where(e => e.Type == "person").ToArray();
        var columns = vm.Lanes ? Math.Max(1, people.Length + 1) : Math.Clamp((int)((canvas.Width - 50) / 190), 1, 7); columnWidth = (canvas.Width - 50) / columns;
        for (var d = 0; d < columns; d++) { Text(vm.Lanes ? d < people.Length ? people[d].Title : vm.Strings["Unassigned"] : vm.StartDate.PlusDays(d).ToString("ddd dd MMM", System.Globalization.CultureInfo.CurrentCulture), 55 + d * columnWidth, 8); Line(50 + d * columnWidth, 32, 50 + d * columnWidth, canvas.Height); }
        for (var hour = 0; hour <= 24; hour++) { Text(hour.ToString("00") + ":00", 0, 35 + hour * hourHeight); Line(50, 40 + hour * hourHeight, canvas.Width, 40 + hour * hourHeight); }
        foreach (var entity in vm.Workspace.State.Trip.Entities.Values.Where(e => e.Type == "schedule_item"))
        {
            var start = entity.Data["time"]?["start"] ?? entity.Data["time"]?["earliest"];
            LocalDateTime local;
            if (start is not null) { try { local = ZonedTime.From(start).ToInstant().InZone(DisplayZone).LocalDateTime; } catch (DomainException) { continue; } }
            else if (entity.Data["time"]?["date"] is { } date && LocalDatePattern.Iso.Parse(date.ToString()).TryGetValue(default, out var day)) local = day.At(new LocalTime(entity.Data["time"]?["day_part"]?.ToString() switch { "morning" => 9, "afternoon" => 14, "evening" => 18, "night" => 22, _ => 0 }, 0));
            else if (entity.Data["children"] is System.Text.Json.Nodes.JsonArray children && children.Count > 0 && TripQueries.Range(vm.Workspace.State.Trip, entity).Start is { } aggregateStart) local = aggregateStart.InZone(DateTimeZoneProviders.Tzdb[vm.Workspace.State.Trip.Manifest.Data["default_timezone"]?.ToString() ?? "Etc/UTC"]).LocalDateTime;
            else continue;
            var localEnd = entity.Data["time"]?["precision"]?.ToString() == "all_day" ? local.PlusDays(1) : local.PlusMinutes((long)DurationOf(entity.Id).TotalMinutes);
            if (start is not null) { try { localEnd = (ZonedTime.From(start).ToInstant() + DurationOf(entity.Id)).InZone(DisplayZone).LocalDateTime; } catch (DomainException) { } }
            for (var visibleDay = 0; visibleDay < (vm.Lanes ? 1 : columns); visibleDay++)
            {
                var segmentDate = vm.StartDate.PlusDays(visibleDay); if (segmentDate < local.Date || segmentDate > localEnd.Date) continue;
                var segmentStart = segmentDate == local.Date ? local : segmentDate.AtMidnight();
                var minutes = segmentStart.Hour * 60 + segmentStart.Minute;
                var endMinutes = segmentDate == localEnd.Date ? localEnd.Hour * 60 + localEnd.Minute : 1440;
                if (endMinutes <= minutes) continue;
                var dayIndex = visibleDay;
                var lanes = new List<int>();
                if (vm.Lanes)
                { var participants = TripQueries.Resolve(vm.Workspace.State.Trip, entity.Id, "participants").Value as System.Text.Json.Nodes.JsonArray; for (var i = 0; i < people.Length; i++) if (participants?.Any(p => p?.ToString() == people[i].Id.ToString()) == true) lanes.Add(i); if (lanes.Count == 0) lanes.Add(people.Length); }
                else lanes.Add(dayIndex);
                foreach (var lane in lanes)
                {
                    var height = Math.Max(44, (endMinutes - minutes) / 60d * hourHeight); var parent = TripQueries.Parent(vm.Workspace.State.Trip, entity.Id);
                    var button = new Button
                    {
                        Tag = entity.Id,
                        Width = columnWidth - (parent is null ? 10 : 24),
                        Height = Math.Min(height, hourHeight * 24 - minutes / 60d * hourHeight),
                        Padding = new Thickness(8, 4),
                        Background = new SolidColorBrush(Color.Parse(vm.Selected?.Id == entity.Id ? "#A9E4D7" : "#D7EEF0")),
                        Foreground = new SolidColorBrush(Color.Parse("#12394D")),
                        BorderBrush = new SolidColorBrush(Color.Parse("#168896")),
                        BorderThickness = new Thickness(vm.Selected?.Id == entity.Id ? 2 : 1),
                        Content = new StackPanel
                        {
                            Spacing = 3,
                            Children = {
                        new TextBlock { Text = (vm.Selected?.Id == entity.Id ? "✓ " : "") + entity.Title + (segmentDate != local.Date ? " ↪" : ""), TextWrapping = TextWrapping.Wrap, FontSize = 12, FontWeight = FontWeight.SemiBold },
                        new TextBlock { Text = segmentStart.ToString("HH:mm", null) + " – " + (endMinutes == 1440 ? "24:00" : new LocalTime(endMinutes / 60, endMinutes % 60).ToString("HH:mm", null)), FontSize = 11 },
                    }
                        }
                    };
                    if (start?["timezone"]?.ToString() is { } origin && origin != DisplayZone.Id) ((StackPanel)button.Content!).Children.Add(new TextBlock { Text = start["local"] + " " + origin, FontSize = 10, TextWrapping = TextWrapping.Wrap });
                    ToolTip.SetTip(button, entity.Title + " · " + local.ToString("g", System.Globalization.CultureInfo.CurrentCulture) + " · " + DisplayZone.Id);
                    var remove = new MenuItem { Header = vm.Strings["Delete"] };
                    remove.Click += (_, _) => { vm.Selected = vm.Workspace?.State.Trip.Find(entity.Id); vm.DeleteCommand.Execute(null); };
                    button.ContextMenu = new ContextMenu { ItemsSource = new[] { remove } };
                    AutomationProperties.SetName(button, entity.Title + " " + local); button.Click += (_, _) => vm.Selected = entity;
                    button.AddHandler(PointerPressedEvent, (_, e) => { if (!e.GetCurrentPoint(button).Properties.IsLeftButtonPressed) { e.Handled = true; return; } dragId = entity.Id; dragStart = e.GetPosition(canvas); dragGrabOffset = e.GetPosition(button).Y; dragContinuationDays = Period.Between(local.Date, segmentDate, PeriodUnits.Days).Days; resize = e.GetPosition(button).Y > button.Height - 12; e.Pointer.Capture(canvas); vm.Selected = entity; e.Handled = true; }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
                    Canvas.SetLeft(button, 55 + lane * columnWidth + (parent is null ? 0 : 14)); Canvas.SetTop(button, 40 + minutes / 60d * hourHeight); canvas.Children.Add(button);
                }
            }
        }
    }
    private Task CreateAtAsync(Point a, Point b)
    {
        var first = Math.Clamp((int)Math.Floor((Math.Min(a.Y, b.Y) - 40) / hourHeight * 4) * 15, 0, 1425);
        var last = Math.Clamp((int)Math.Ceiling((Math.Max(a.Y, b.Y) - 40) / hourHeight * 4) * 15, first + 15, 1440);
        var column = Math.Max(0, (int)((a.X - 50) / columnWidth));
        var day = vm.Lanes ? vm.StartDate : vm.StartDate.PlusDays(column);
        var start = ZonedTime.At(DisplayZone.AtLeniently(day.AtMidnight().PlusMinutes(first)).ToInstant(), DisplayZone.Id);
        var people = vm.Workspace!.State.Trip.Entities.Values.Where(e => e.Type == "person").ToArray();
        Guid? person = vm.Lanes && column < people.Length ? people[column].Id : null;
        return vm.AddScheduledAsync(start, Duration.FromMinutes(last - first), person);
    }
    private DateTimeZone DisplayZone => DateTimeZoneProviders.Tzdb.GetZoneOrNull(vm.Workspace?.State.Trip.Manifest.Data["default_timezone"]?.ToString() ?? "Etc/UTC") ?? DateTimeZone.Utc;
    private Duration DurationOf(Guid id)
    {
        var e = vm.Workspace!.State.Trip.Find(id)!; try { if (e.Data["time"]?["start"] is { } a && e.Data["time"]?["end"] is { } b) return ZonedTime.From(b).ToInstant() - ZonedTime.From(a).ToInstant(); } catch (DomainException) { }
        var range = TripQueries.Range(vm.Workspace.State.Trip, e); if (range.Start is { } first && range.End is { } last && last > first) return last - first;
        if (e.Data["duration"] is { } duration && PeriodPattern.NormalizingIso.Parse(duration.ToString()).TryGetValue(default!, out var period)) { try { return period.ToDuration(); } catch (InvalidOperationException) { } }
        return Duration.FromHours(1);
    }
    private async Task ScheduleAt(Guid id, Point p, int continuationDays = 0)
    {
        if (map || vm.Workspace is null) return; var entity = vm.Workspace.State.Trip.Find(id); if (entity?.Type is not ("schedule_item" or "note" or "document")) return;
        var day = vm.Lanes ? 0 : Math.Max(0, (int)((p.X - 50) / columnWidth)); var minute = (int)Math.Clamp(Math.Round((p.Y - 40) / hourHeight * 60 / 15) * 15, 0, 1425);
        var zone = DisplayZone.Id;
        var displayStart = new ZonedTime(vm.StartDate.PlusDays(day - continuationDays).At(new LocalTime(minute / 60, minute % 60)).ToString("yyyy-MM-dd'T'HH:mm:ss", null), zone);
        var start = ZonedTime.At(displayStart.ToInstant(), entity.Data["time"]?["start"]?["timezone"]?.ToString() ?? zone);
        var scheduled = await vm.Workspace.PlanMaterialAsync(id, start, DurationOf(id)); vm.Selected = vm.Workspace.State.Trip.Find(scheduled);
    }
    private static double Coordinate(Entity e, string key) => double.Parse(e.Data["location"]![key]!.ToString(), System.Globalization.CultureInfo.InvariantCulture);
    private void DrawMap()
    {
        Text(vm.Strings["LocalMap"], 10, 10, 15);
        var places = vm.Workspace!.State.Trip.Entities.Values.Where(e => e.Type == "place" && e.Data["location"]?["latitude"] is not null).ToArray();
        if (places.Length == 0) { Text(vm.Strings["NoPlaces"], 10, 70); return; }
        var minLat = places.Min(e => Coordinate(e, "latitude")) - .01; var maxLat = places.Max(e => Coordinate(e, "latitude")) + .01;
        var minLon = places.Min(e => Coordinate(e, "longitude")) - .01; var maxLon = places.Max(e => Coordinate(e, "longitude")) + .01;
        Point PointFor(Entity e) => new(20 + (Coordinate(e, "longitude") - minLon) / (maxLon - minLon) * (canvas.Width - 160), 65 + (maxLat - Coordinate(e, "latitude")) / (maxLat - minLat) * 430);
        for (var i = 0; i <= 5; i++) { Line(10, 60 + i * 90, canvas.Width, 60 + i * 90); Text((maxLat - (maxLat - minLat) * i / 5).ToString("F3", System.Globalization.CultureInfo.CurrentCulture), 10, 60 + i * 90); }
        foreach (var route in vm.Workspace.State.Trip.Entities.Values.Where(e => e.Type == "schedule_item"))
        {
            var transport = route.Data["components"]?["transport"]; if (Guid.TryParse(transport?["departure"]?["place"]?.ToString(), out var a) && Guid.TryParse(transport?["arrival"]?["place"]?.ToString(), out var b) && places.FirstOrDefault(p => p.Id == a) is { } from && places.FirstOrDefault(p => p.Id == b) is { } to)
            {
                var x = PointFor(from); var y = PointFor(to); var line = new Avalonia.Controls.Shapes.Line { StartPoint = new Point(x.X + 20, x.Y + 15), EndPoint = new Point(y.X + 20, y.Y + 15), Stroke = Brushes.Teal, StrokeThickness = vm.Selected?.Id == route.Id ? 5 : 2 }; canvas.Children.Add(line);
                var routeButton = new Button { Content = "↔ " + route.Title, MaxWidth = 190 }; AutomationProperties.SetName(routeButton, route.Title); routeButton.Click += (_, _) => { vm.Selected = route; Draw(); }; Canvas.SetLeft(routeButton, (x.X + y.X) / 2); Canvas.SetTop(routeButton, (x.Y + y.Y) / 2 + 30); canvas.Children.Add(routeButton);
            }
        }
        foreach (var place in places)
        {
            var selected = vm.Selected?.Id == place.Id || vm.Selected?.Data.ToJsonString().Contains(place.Id.ToString(), StringComparison.Ordinal) == true;
            var button = new Button { Content = "● " + place.Title, Background = new SolidColorBrush(Color.Parse(selected ? "#A9E4D7" : "#D7EEF0")), Foreground = new SolidColorBrush(Color.Parse("#13344C")), MaxWidth = 190 }; AutomationProperties.SetName(button, place.Title); button.Click += (_, _) => { vm.Selected = place; Draw(); }; var point = PointFor(place); Canvas.SetLeft(button, point.X); Canvas.SetTop(button, point.Y); canvas.Children.Add(button);
        }
    }
}
