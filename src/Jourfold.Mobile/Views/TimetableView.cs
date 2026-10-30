using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using NodaTime;
using TravelRepo.Core;

namespace Jourfold.Mobile.Views;

/// <summary>
/// Several days side by side on a time grid, for tablets and wide windows (design spec: multi-day timetable). As many
/// days as fit, never a fixed number; each day keeps its own local time, the hours run from the earliest to the latest
/// plan of the days shown, where everyone sleeps stands above the grid, and a tap opens the details in their column.
/// </summary>
public static class TimetableView
{
    private const double HourHeight = 52, AxisWidth = 52, MinColumn = 180;

    /// <summary>How many days fit into <paramref name="width"/> pixels, from three to seven.</summary>
    public static int Columns(double width) => Math.Clamp((int)((width - AxisWidth - 32) / MinColumn), 3, 7);

    public static Control Build(MobileSession s, double width)
    {
        var days = s.Agenda.Days;
        if (days.Count == 0) return Ui.Text(s.Strings["NothingPlanned"], "muted").Margin(16, 8);
        var count = Math.Min(Columns(width), days.Count);
        var first = Math.Clamp(days.ToList().FindIndex(d => d.Date == s.SelectedDate), 0, Math.Max(0, days.Count - count));
        var shown = days.Skip(first).Take(count).ToArray();
        var columns = shown.Select(day => (Day: day, Zone: Zone(s, day), Lines: Timed(day))).ToArray();

        // The hours from the earliest start to the latest end of the days shown, at least eight in the day.
        var starts = columns.SelectMany(c => c.Lines.Select(l => l.Start!.Value.InZone(c.Zone).TimeOfDay.Hour)).DefaultIfEmpty(8).ToArray();
        var ends = columns.SelectMany(c => c.Lines.Select(l => End(l, c.Day, c.Zone))).DefaultIfEmpty(20).ToArray();
        var from = Math.Max(0, Math.Min(starts.Min(), 8)); var to = Math.Min(24, Math.Max(ends.Max(), from + 12));

        var spec = AxisWidth.ToString(CultureInfo.InvariantCulture) + "," + string.Join(',', Enumerable.Repeat("*", count));
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions(spec) };
        var strip = new Grid { ColumnDefinitions = new ColumnDefinitions(spec) };
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions(spec), Height = (to - from) * HourHeight, Margin = new Thickness(0, 10, 0, 16) };
        var axis = new Canvas();
        for (var hour = from; hour <= to; hour++)
        {
            var y = (hour - from) * HourHeight;
            axis.Children.Add(Ui.Text(hour.ToString("00", CultureInfo.InvariantCulture) + ":00", "small", "muted").Also(t => { Canvas.SetTop(t, y - 8); Canvas.SetLeft(t, 8); }));
            grid.Children.Add(new Border { Height = 1, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, y, 0, 0) }.Res(Border.BackgroundProperty, "Line").Also(b => Grid.SetColumnSpan(b, count + 1)));
        }
        grid.Children.Add(axis);
        for (var i = 0; i < columns.Length; i++)
        {
            var (day, zone, lines) = columns[i];
            var title = Ui.V(1, Ui.Text(Lines.DayShort(day.Date, s.Strings), day.Date == s.PhoneToday ? ["strong"] : []), Ui.Text(string.Format(CultureInfo.CurrentCulture, s.Strings["DayN"], day.Number) + " · " + TimeText.Place(zone), "caption"));
            header.Children.Add(title.Margin(8, 0, 4, 6).Col(i + 1));
            var nights = Ui.V(4);
            foreach (var night in day.Nights)
                nights.Children.Add(new Border { Child = Ui.Line(night.Run.Main.Item.Title, "caption"), Padding = new Thickness(8, 3), CornerRadius = new CornerRadius(6) }.Res(Border.BackgroundProperty, "Kind.Accommodation.Bg"));
            foreach (var allDay in day.Entries.Where(e => e.Role == AgendaRole.Item && !e.IsTimed && e.Day == day.Date))
                nights.Children.Add(Block(s, allDay, allDay.Item.Title, 0, 26));
            strip.Children.Add(nights.Margin(4, 0, 4, 8).Col(i + 1));
            var lane = new Canvas { ClipToBounds = true };
            grid.Children.Add(new Border { BorderThickness = new Thickness(1, 0, 0, 0), Child = lane }.Res(Border.BorderBrushProperty, "Line").Col(i + 1));
            Place(s, lane, day, zone, lines, from);
            if (day.Date == s.PhoneToday) lane.Children.Add(Now(s, zone, from));
        }

        var earlier = Ui.IconButton("chevron-left", s.Strings["EarlierDays"]); earlier.IsEnabled = first > 0;
        earlier.Click += (_, _) => s.ShowDate(days[Math.Max(0, first - count)].Date);
        var later = Ui.IconButton("chevron-right", s.Strings["LaterDays"]); later.IsEnabled = first + count < days.Count;
        later.Click += (_, _) => s.ShowDate(days[Math.Min(days.Count - count, first + count)].Date);
        var range = Ui.Text(Formats.DateRange(shown[0].Date, shown[^1].Date), "strong").Also(t => t.VerticalAlignment = VerticalAlignment.Center);
        var scroll = new ScrollViewer { Content = grid, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        var layout = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,Auto,*") };
        layout.Children.Add(Ui.H(4, earlier, later, range.Margin(8, 0, 0, 0)).Margin(12, 0, 12, 8));
        layout.Children.Add(header.Row(1)); layout.Children.Add(strip.Row(2)); layout.Children.Add(scroll.Row(3));
        return layout;
    }

    /// <summary>The zone a day's times are read in: that of its first timed line, otherwise the trip's.</summary>
    private static DateTimeZone Zone(MobileSession s, AgendaDay day) =>
        // A journey that continues from the day before still reads in the zone it left from; the day's own lines decide.
        Timed(day).Where(e => e.Zone is not null).Select(e => DateTimeZoneProviders.Tzdb.GetZoneOrNull(e.EndZone ?? e.Zone!)).FirstOrDefault(z => z is not null) ?? ScheduleQueries.TripZone(s.Trip!);

    /// <summary>The lines of a day that have a clock time; groups show their steps, and stays only their check-in and check-out.</summary>
    private static AgendaEntry[] Timed(AgendaDay day) =>
        day.Entries.Where(e => e.Role != AgendaRole.Continued).SelectMany(e => e.IsGroup ? e.Steps : [e]).Where(e => e.IsTimed && e.Start is not null).ToArray();

    private static int End(AgendaEntry line, AgendaDay day, DateTimeZone zone)
    {
        var end = (line.End ?? line.Start!.Value + Duration.FromHours(1)).InZone(zone);
        return end.Date > day.Date ? 24 : end.TimeOfDay.Hour + (end.TimeOfDay.Minute > 0 ? 1 : 0);
    }

    /// <summary>The lines of one day as blocks; lines at the same time share the width.</summary>
    private static void Place(MobileSession s, Canvas lane, AgendaDay day, DateTimeZone zone, AgendaEntry[] lines, int from)
    {
        var dayStart = zone.AtStartOfDay(day.Date).ToInstant(); var dayEnd = zone.AtStartOfDay(day.Date.PlusDays(1)).ToInstant();
        var spans = lines.Select(l => (Line: l, Start: l.Start!.Value < dayStart ? dayStart : l.Start!.Value, End: Instant.Min(l.End is { } e && e > l.Start ? e : l.Start!.Value + Duration.FromMinutes(45), dayEnd)))
            .OrderBy(x => x.Start).ToArray();
        // Each line goes into the first lane free at its start; lines that overlap divide the width by their lanes.
        var lanes = new List<Instant>(); var placed = new List<(int Lane, int Cluster)>(); var clusters = new List<int>(); var clusterEnd = Instant.MinValue;
        foreach (var span in spans)
        {
            if (span.Start >= clusterEnd) { clusters.Add(0); lanes.Clear(); }
            var index = lanes.FindIndex(end => end <= span.Start);
            if (index < 0) { lanes.Add(span.End); index = lanes.Count - 1; } else lanes[index] = span.End;
            clusters[^1] = Math.Max(clusters[^1], lanes.Count);
            clusterEnd = Instant.Max(clusterEnd, span.End);
            placed.Add((index, clusters.Count - 1));
        }
        for (var i = 0; i < spans.Length; i++)
        {
            var (line, start, end) = spans[i]; var (laneIndex, cluster) = placed[i]; var lanesHere = clusters[cluster];
            var top = (start - dayStart).TotalHours * HourHeight - from * HourHeight;
            var height = Math.Max(26, (end - start).TotalHours * HourHeight - 2);
            var role = line.Role switch { AgendaRole.CheckIn => s.Strings["CheckIn"] + " · ", AgendaRole.CheckOut => s.Strings["CheckOut"] + " · ", _ => "" };
            var block = Block(s, line, line.Item.Title + " · " + role + start.InZone(zone).TimeOfDay.ToString("HH:mm", CultureInfo.InvariantCulture), top, height);
            lane.Children.Add(block);
            // The width is set once the column is measured.
            lane.SizeChanged += (_, e) => { var w = (e.NewSize.Width - 6) / lanesHere; block.Width = Math.Max(20, w - 3); Canvas.SetLeft(block, 3 + laneIndex * w); };
        }
    }

    /// <summary>A line as a tinted block in its kind's colour, opening the details.</summary>
    private static Control Block(MobileSession s, AgendaEntry line, string text, double top, double height)
    {
        var key = "Kind." + Visuals.KindKey(line.Kind);
        var block = new Border { Child = Ui.Text(text, "caption").Also(t => { t.TextWrapping = TextWrapping.Wrap; t.TextTrimming = TextTrimming.CharacterEllipsis; t.Res(TextBlock.ForegroundProperty, key + ".Fg"); }), Padding = new Thickness(6, 3), CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(3, 0, 0, 0), Height = height, ClipToBounds = true }
            .Res(Border.BackgroundProperty, key + ".Bg").Res(Border.BorderBrushProperty, key + ".Bar");
        if (line.IsIdea || line.Status == "cancelled") block.Opacity = 0.6;
        var button = new Button { Content = block, Padding = new Thickness(0), MinHeight = 0, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch }.Classed("card");
        AutomationProperties.SetName(button, text);
        Menus.OnHold(button, () => s.ShowMenu(Menus.Item(s, line.Item)));
        button.Click += (_, _) => s.ShowDetails(line.Item.Id);
        Canvas.SetTop(button, top);
        return button;
    }

    private static Control Now(MobileSession s, DateTimeZone zone, int from)
    {
        var at = s.Now.InZone(zone).TimeOfDay;
        var line = new Border { Height = 2, Width = 4000 }.Res(Border.BackgroundProperty, "NowLine");
        Canvas.SetTop(line, (at.Hour + at.Minute / 60.0 - from) * HourHeight); line.IsHitTestVisible = false;
        return line;
    }
}
