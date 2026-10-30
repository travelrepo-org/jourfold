using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using NodaTime;
using TravelRepo.Core;

namespace Jourfold.Mobile.Views;

/// <summary>
/// Several days side by side on a time grid, for tablets and wide windows (design spec: multi-day timetable). It reads
/// like the desktop's timetable, a little calmer and with larger targets for touch: all-day plans in a strip above the
/// grid, stays as bars over their nights in a band below it, check-in and check-out as thin markers that take no room
/// (a line at a fixed hour, a soft band for the check-in hours), plans as tinted blocks with their kind's icon, time and
/// title, dashed when only an idea or a part of the day. As many days as fit, never a fixed number; each day keeps its
/// own local time. A tap opens the details in their column; a long press opens the item's menu.
/// </summary>
public static class TimetableView
{
    private const double HourHeight = 52, Gutter = 56, MinColumn = 180, Top = 10;

    /// <summary>How many days fit into <paramref name="width"/> pixels, from three to seven.</summary>
    public static int Columns(double width) => Math.Clamp((int)((width - Gutter - 32) / MinColumn), 3, 7);

    public static Control Build(MobileSession s, double width)
    {
        var days = s.Agenda.Days;
        if (days.Count == 0) return Ui.Text(s.Strings["NothingPlanned"], "muted").Margin(16, 8);
        var count = Math.Min(Columns(width), days.Count);
        var first = Math.Clamp(days.ToList().FindIndex(d => d.Date == s.SelectedDate), 0, Math.Max(0, days.Count - count));
        var shown = days.Skip(first).Take(count).ToArray();
        var columns = shown.Select(day => (Day: day, Zone: Zone(s, day), Lines: InGrid(day))).ToArray();

        // The hours from the earliest start to the latest end of the days shown, at least twelve.
        var starts = columns.SelectMany(c => c.Lines.Where(l => l.Role != AgendaRole.Continued).Select(l => l.Start!.Value.InZone(c.Zone).TimeOfDay.Hour)).DefaultIfEmpty(8).ToArray();
        var ends = columns.SelectMany(c => c.Lines.Select(l => End(l, c.Day, c.Zone))).DefaultIfEmpty(20).ToArray();
        var from = Math.Max(0, Math.Min(starts.Min(), 8)); var to = Math.Min(24, Math.Max(ends.Max(), from + 12));

        var spec = Gutter.ToString(CultureInfo.InvariantCulture) + "," + string.Join(',', Enumerable.Repeat("*", count));
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions(spec) };
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions(spec), Height = Top + (to - from) * HourHeight + 16 };
        var axis = new Canvas();
        for (var hour = from; hour <= to; hour++)
        {
            var y = Top + (hour - from) * HourHeight;
            axis.Children.Add(Ui.Text(hour.ToString("00", CultureInfo.InvariantCulture) + ":00", "small", "muted").Also(t => { Canvas.SetTop(t, y - 8); Canvas.SetLeft(t, 8); }));
            grid.Children.Add(new Border { Height = 1, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, y, 0, 0) }.Res(Border.BackgroundProperty, "Line").Also(b => Grid.SetColumnSpan(b, count + 1)));
        }
        grid.Children.Add(axis);
        var tripZone = ScheduleQueries.TripZone(s.Trip!);
        var culture = CultureInfo.GetCultureInfo(s.Strings.Language);
        for (var i = 0; i < columns.Length; i++)
        {
            var (day, zone, lines) = columns[i];
            var today = day.Date == s.PhoneToday;
            // As on the desktop: the weekday small above the date, today in the accent colour, the zone only when it is not the trip's.
            var weekday = Ui.Text(day.Date.ToString("ddd", culture).TrimEnd('.').ToUpper(culture), "small", "muted");
            var date = Ui.Text(day.Date.ToString("d MMM", culture), "strong");
            if (today) { weekday.Res(TextBlock.ForegroundProperty, "Accent"); date.Res(TextBlock.ForegroundProperty, "Accent"); }
            header.Children.Add(Ui.V(0, weekday, date, zone.Id == tripZone.Id ? null : Ui.Text(TimeText.Place(zone), "small", "muted")).Margin(10, 0, 4, 6).Col(i + 1));
            var lane = new Canvas { ClipToBounds = true };
            grid.Children.Add(new Border { BorderThickness = new Thickness(1, 0, 0, 0), Child = lane }.Res(Border.BorderBrushProperty, "Line").Col(i + 1));
            Markers(s, lane, day, zone, from);
            Place(s, lane, day, zone, lines, from);
            if (today) lane.Children.Add(Now(s, zone, from));
        }

        var earlier = Ui.IconButton("chevron-left", s.Strings["EarlierDays"]); earlier.IsEnabled = first > 0;
        earlier.Click += (_, _) => s.ShowDate(days[Math.Max(0, first - count)].Date);
        var later = Ui.IconButton("chevron-right", s.Strings["LaterDays"]); later.IsEnabled = first + count < days.Count;
        later.Click += (_, _) => s.ShowDate(days[Math.Min(days.Count - count, first + count)].Date);
        var range = Ui.Text(Formats.DateRange(shown[0].Date, shown[^1].Date), "strong").Also(t => t.VerticalAlignment = VerticalAlignment.Center);
        var scroll = new ScrollViewer { Content = grid, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        var layout = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,Auto,*,Auto") };
        layout.Children.Add(Ui.H(4, earlier, later, range.Margin(8, 0, 0, 0)).Margin(12, 0, 12, 8));
        layout.Children.Add(header.Row(1));
        layout.Children.Add(Band(s.Strings["AllDay"], AllDay(s, shown), count, top: true).Row(2));
        layout.Children.Add(scroll.Row(3));
        layout.Children.Add(Band(s.Strings["NightsBand"], Nights(s, shown), count, top: false).Row(4));
        return layout;
    }

    /// <summary>The zone where the day's own first line starts; a journey continuing from the day before does not count.</summary>
    private static DateTimeZone Zone(MobileSession s, AgendaDay day) =>
        InGrid(day).Where(e => e.Role != AgendaRole.Continued && e.Zone is not null).Select(e => DateTimeZoneProviders.Tzdb.GetZoneOrNull(e.Zone!)).FirstOrDefault(z => z is not null) ?? ScheduleQueries.TripZone(s.Trip!);

    /// <summary>
    /// The lines in the grid: plans with a clock time or a part of the day, steps of groups on their own, and the rest of
    /// a journey that began the day before. Check-in and check-out are markers, and stays sit in the band below.
    /// </summary>
    private static AgendaEntry[] InGrid(AgendaDay day) =>
        day.Entries.Where(e => e.Role is AgendaRole.Item or AgendaRole.Continued).SelectMany(e => e.IsGroup ? e.Steps : [e])
            .Where(e => e.Start is not null && (e.IsTimed || e.Precision == TimePrecision.DayPart) && !Stays.IsStay(e.Item)).ToArray();

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
            .Where(x => x.End > x.Start).OrderBy(x => x.Start).ToArray();
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
            var top = Top + (start - dayStart).TotalHours * HourHeight - from * HourHeight;
            var height = Math.Max(30, (end - start).TotalHours * HourHeight - 3);
            var block = Block(s, line, zone, height, continued: line.Role == AgendaRole.Continued);
            Canvas.SetTop(block, top);
            lane.Children.Add(block);
            // The width is set once the column is measured.
            lane.SizeChanged += (_, e) => { var w = (e.NewSize.Width - 6) / lanesHere; block.Width = Math.Max(20, w - 3); Canvas.SetLeft(block, 3 + laneIndex * w); };
        }
    }

    /// <summary>
    /// A plan as the desktop draws it: its kind's colour, a bar on the left, the icon and time in the kind's colour above
    /// the title, the place when there is room; dashed when it is only an idea, not fixed to the minute or a part of the day.
    /// </summary>
    private static Button Block(MobileSession s, AgendaEntry line, DateTimeZone zone, double height, bool continued = false, bool strip = false)
    {
        var trip = s.Trip!; var item = line.Item; var key = "Kind." + Visuals.KindKey(line.Kind);
        var icon = Visuals.KindIcon(line.Kind, ScheduleCategories.TransportType(item));
        var status = line.Status;
        var timeText = line.Precision switch
        {
            TimePrecision.AllDay => s.Strings["AllDay"],
            TimePrecision.DayPart => s.Strings.Optional(item.Data["time"]?["day_part"]?.ToString() ?? "day_part"),
            _ when line.Start is { } a => (line.Precision == TimePrecision.Approximate ? "~" : "") + Clock(a.InZone(zone).TimeOfDay)
                + (line.End is { } b ? " – " + Clock(b.InZone(zone).TimeOfDay) : ""),
            _ => ""
        };
        var compact = height < 44 || strip;
        var title = Ui.Text((continued ? "↳ " : "") + item.Title, "strong").Also(t =>
        {
            t.FontSize = 12.5; t.TextWrapping = compact ? TextWrapping.NoWrap : TextWrapping.Wrap; t.TextTrimming = TextTrimming.CharacterEllipsis; t.MaxHeight = Math.Max(16, height - 24);
            if (status == "cancelled") t.TextDecorations = TextDecorations.Strikethrough;
        });
        var time = Ui.Text(timeText, "small").Also(t => { t.FontWeight = FontWeight.SemiBold; t.TextTrimming = TextTrimming.CharacterEllipsis; t.Res(TextBlock.ForegroundProperty, key + ".Fg"); });
        var done = status is "confirmed" or "completed" ? Ui.Icon("circle-check", 12, "Success") : null;
        Control body = compact
            ? Ui.Columns("Auto,*,Auto", Ui.Icon(icon, 13, key + ".Fg"), title.Margin(5, 0), done)
            : Ui.V(2, Ui.Columns("Auto,*,Auto", Ui.Icon(icon, 13, key + ".Fg"), time.Margin(5, 0, 2, 0), done), title,
                height >= 70 && Lines.Where(trip, item) is { Length: > 0 } where ? Ui.Line(where, "caption") : null);
        var dashed = line.IsIdea || line.Precision is TimePrecision.DayPart or TimePrecision.Approximate or TimePrecision.Window;
        var border = new Border { Height = height, CornerRadius = new CornerRadius(strip ? 6 : 8), Padding = new Thickness(9, compact ? 3 : 6, 6, 4), BorderThickness = new Thickness(1), Child = body, ClipToBounds = true }
            .Res(Border.BackgroundProperty, key + ".Bg");
        if (dashed) border.BorderBrush = Brushes.Transparent; else border.Res(Border.BorderBrushProperty, key + ".Bar");
        var accent = new Border { Width = 3, CornerRadius = new CornerRadius(2), Margin = new Thickness(0, 4), HorizontalAlignment = HorizontalAlignment.Left, IsHitTestVisible = false }.Res(Border.BackgroundProperty, key + ".Bar");
        var layered = new Panel { Children = { border, accent } };
        if (dashed) layered.Children.Add(new Rectangle { StrokeThickness = 1.2, StrokeDashArray = [4, 3], RadiusX = 8, RadiusY = 8, Height = height, IsHitTestVisible = false }.Res(Shape.StrokeProperty, key + ".Bar"));
        if (status == "cancelled") layered.Opacity = 0.55;
        var button = new Button { Content = layered, Padding = new Thickness(0), MinHeight = 0, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch }.Classed("card");
        AutomationProperties.SetName(button, item.Title + (timeText.Length > 0 ? ", " + timeText : ""));
        Menus.OnHold(button, () => s.ShowMenu(Menus.Item(s, item)));
        button.Click += (_, _) => s.ShowDetails(item.Id);
        return button;
    }

    /// <summary>
    /// Check-in and check-out as on the desktop, taking no room: a dashed line with a tag at a fixed hour, a soft band
    /// for the check-in hours when the arrival has no fixed hour, and the latest check-out when the departure has none.
    /// They take no taps, so plans under them stay reachable.
    /// </summary>
    private static void Markers(MobileSession s, Canvas lane, AgendaDay day, DateTimeZone zone, int from)
    {
        var str = s.Strings;
        double Y(LocalTime time) => Top + (time.TickOfDay / (double)NodaConstants.TicksPerHour - from) * HourHeight;
        void Tag(string text, double y)
        {
            var tag = new Border { Padding = new Thickness(5, 1), CornerRadius = new CornerRadius(4), IsHitTestVisible = false, Child = Ui.H(3, Ui.Icon("bed-double", 11, "Kind.Accommodation.Fg"), Ui.Text(text, "small").Also(t => t.TextWrapping = TextWrapping.NoWrap)) }
                .Res(Border.BackgroundProperty, "Kind.Accommodation.Bg");
            Canvas.SetTop(tag, y - 9); tag.ZIndex = 1; lane.Children.Add(tag);
            lane.SizeChanged += (_, e) => { tag.Measure(Size.Infinity); Canvas.SetLeft(tag, Math.Max(2, e.NewSize.Width - tag.DesiredSize.Width - 4)); };
        }
        void Line(double y)
        {
            var line = new Line { StartPoint = new Point(2, y), EndPoint = new Point(2, y), StrokeThickness = 1.5, StrokeDashArray = [5, 3], IsHitTestVisible = false, Opacity = 0.85 }.Res(Shape.StrokeProperty, "Kind.Accommodation.Bar");
            lane.Children.Add(line); lane.SizeChanged += (_, e) => line.EndPoint = new Point(e.NewSize.Width - 2, y);
        }
        foreach (var stay in Stays.All(s.Trip!).Where(x => x.FirstNight is not null))
        {
            var stayZone = DateTimeZoneProviders.Tzdb.GetZoneOrNull(stay.Zone) ?? zone;
            ZonedDateTime Here(LocalDate date, LocalTime time) => stayZone.AtLeniently(date.At(time)).ToInstant().InZone(zone);
            // The accommodation's check-in hours on the arrival day.
            if (stay.CheckInWindow is { From: { } open } window && Here(stay.FirstNight!.Value, open) is var start && start.Date == day.Date)
            {
                var end = window.Until is { } until ? Here(stay.FirstNight!.Value, until).TimeOfDay : new LocalTime(23, 59);
                var band = new Border { Height = Math.Max(4, Y(end) - Y(start.TimeOfDay)), IsHitTestVisible = false, Opacity = 0.45, CornerRadius = new CornerRadius(4) }.Res(Border.BackgroundProperty, "Kind.Accommodation.Bg");
                Canvas.SetTop(band, Y(start.TimeOfDay)); Canvas.SetLeft(band, 2); lane.Children.Add(band);
                lane.SizeChanged += (_, e) => band.Width = Math.Max(0, e.NewSize.Width - 4);
                if (stay.CheckIn is null || stay.CheckInApproximate)
                    Tag(window.Until is { } u ? string.Format(CultureInfo.CurrentCulture, str["CheckInBetween"], Clock(open), Clock(u)) : string.Format(CultureInfo.CurrentCulture, str["CheckInFromTime"], Clock(open)), Y(start.TimeOfDay));
            }
            // A fixed hour gets a line; one that is not fixed gets none.
            foreach (var (moment, approximate, key) in new[] { (stay.CheckIn, stay.CheckInApproximate, "CheckInAt"), (stay.CheckOut, stay.CheckOutApproximate, "CheckOutAt") })
            {
                if (moment is null || approximate) continue;
                Instant instant; try { instant = moment.ToInstant(); } catch (DomainException) { continue; }
                var local = instant.InZone(zone);
                if (local.Date != day.Date) continue;
                Line(Y(local.TimeOfDay)); Tag(string.Format(CultureInfo.CurrentCulture, str[key], Clock(local.TimeOfDay)), Y(local.TimeOfDay));
            }
            // Without a fixed departure hour, the latest check-out is the useful time.
            if ((stay.CheckOut is null || stay.CheckOutApproximate) && stay.CheckOutWindow?.Until is { } latest && stay.DepartureDay is { } departure && Here(departure, latest) is var leave && leave.Date == day.Date)
            {
                Line(Y(leave.TimeOfDay)); Tag(string.Format(CultureInfo.CurrentCulture, str["CheckOutByTime"], Clock(latest)), Y(leave.TimeOfDay));
            }
        }
    }

    /// <summary>All-day plans as bars over the days they cover.</summary>
    private static IReadOnlyList<(Control Bar, int From, int To)> AllDay(MobileSession s, AgendaDay[] shown)
    {
        var zone = ScheduleQueries.TripZone(s.Trip!);
        return shown.SelectMany((d, i) => d.Entries.Where(e => e.Role is AgendaRole.Item or AgendaRole.Continued && e.Precision == TimePrecision.AllDay && !Stays.IsStay(e.Item)).Select(e => (Entry: e, Column: i)))
            .GroupBy(x => x.Entry.Item.Id)
            .Select(g => ((Control)Block(s, g.First().Entry, zone, 28, strip: true), g.Min(x => x.Column), g.Max(x => x.Column))).ToArray();
    }

    /// <summary>Stays as bars over the nights they cover, with how many nights; a tap opens the stay.</summary>
    private static IReadOnlyList<(Control Bar, int From, int To)> Nights(MobileSession s, AgendaDay[] shown)
    {
        var str = s.Strings; var bars = new List<(Control, int, int)>();
        foreach (var stay in Stays.All(s.Trip!).Where(x => x.FirstNight is not null && x.Nights > 0))
        {
            var columns = Enumerable.Range(0, shown.Length).Where(i => stay.Covers(shown[i].Date)).ToArray();
            if (columns.Length == 0) continue;
            var item = stay.Item; var status = item.Data["status"]?.ToString();
            var count = stay.Nights == 1 ? str["OneNight"] : string.Format(CultureInfo.CurrentCulture, str["NightsCount"], stay.Nights);
            var title = Ui.Text(item.Title, "strong").Also(t => { t.FontSize = 12; t.TextTrimming = TextTrimming.CharacterEllipsis; t.TextWrapping = TextWrapping.NoWrap; if (status == "cancelled") t.TextDecorations = TextDecorations.Strikethrough; });
            var body = Ui.Columns("Auto,*,Auto,Auto", Ui.Icon("bed-double", 13, "Kind.Accommodation.Fg"), title.Margin(6, 0), Ui.Text(count, "small", "muted").Also(t => t.TextWrapping = TextWrapping.NoWrap),
                status is "confirmed" or "completed" ? Ui.Icon("circle-check", 12, "Success").Margin(6, 0, 0, 0) : null);
            var border = new Border { Height = 30, CornerRadius = new CornerRadius(6), Padding = new Thickness(8, 2, 6, 2), BorderThickness = new Thickness(1), Child = body, ClipToBounds = true }
                .Res(Border.BackgroundProperty, "Kind.Accommodation.Bg");
            if (status == "idea") border.BorderBrush = Brushes.Transparent; else border.Res(Border.BorderBrushProperty, "Kind.Accommodation.Bar");
            var button = new Button { Content = border, Padding = new Thickness(0), MinHeight = 0, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch }.Classed("card");
            AutomationProperties.SetName(button, item.Title + ", " + count);
            Menus.OnHold(button, () => s.ShowMenu(Menus.Item(s, item)));
            button.Click += (_, _) => s.ShowDetails(item.Id);
            if (status == "cancelled") button.Opacity = 0.55;
            bars.Add((button, columns.Min(), columns.Max()));
        }
        return bars;
    }

    /// <summary>A band above or below the grid with its name in the time column and bars over the days, in rows where they overlap.</summary>
    private static Control Band(string name, IReadOnlyList<(Control Bar, int From, int To)> bars, int count, bool top)
    {
        var band = new Grid { ColumnDefinitions = new ColumnDefinitions(Gutter.ToString(CultureInfo.InvariantCulture) + "," + string.Join(',', Enumerable.Repeat("*", count))) };
        if (bars.Count == 0) return band;
        var rows = new List<int>();
        foreach (var (bar, from, to) in bars.OrderBy(b => b.From))
        {
            var row = rows.FindIndex(end => end < from); if (row < 0) { rows.Add(to); row = rows.Count - 1; } else rows[row] = to;
            while (band.RowDefinitions.Count <= row) band.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            bar.Margin = new Thickness(4, 3, 4, 3); Grid.SetColumn(bar, from + 1); Grid.SetColumnSpan(bar, to - from + 1); Grid.SetRow(bar, row);
            band.Children.Add(bar);
        }
        band.Children.Add(Ui.Text(name, "small", "muted").Also(t => { t.TextAlignment = TextAlignment.Right; t.Margin = new Thickness(0, 9, 8, 0); Grid.SetRowSpan(t, band.RowDefinitions.Count); }));
        return new Border { Child = band, Padding = new Thickness(0, 4), BorderThickness = top ? new Thickness(0, 1, 0, 1) : new Thickness(0, 1, 0, 0) }.Res(Border.BorderBrushProperty, "Line").Res(Border.BackgroundProperty, "Bg.Surface");
    }

    /// <summary>A time as the rest of the phone app writes it: 24 hours, such as 08:30.</summary>
    private static string Clock(LocalTime time) => time.ToString("HH:mm", CultureInfo.InvariantCulture);

    private static Control Now(MobileSession s, DateTimeZone zone, int from)
    {
        var at = s.Now.InZone(zone).TimeOfDay;
        var line = new Border { Height = 2, Width = 4000, IsHitTestVisible = false }.Res(Border.BackgroundProperty, "NowLine");
        Canvas.SetTop(line, Top + (at.Hour + at.Minute / 60.0 - from) * HourHeight); line.ZIndex = 2;
        return line;
    }
}
