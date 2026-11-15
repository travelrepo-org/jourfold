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
/// Several days side by side on a time grid, for tablets and wide windows (design spec: multi-day timetable); on the phone
/// the same grid shows one day when the user chooses it. It reads like the desktop's timetable, a little calmer and with
/// larger targets for touch: all-day plans in a strip above the grid, stays as bars over their nights in a band below it,
/// check-in and check-out as thin markers behind the plans that take no room (a line at a fixed hour, a soft band for
/// the check-in hours), plans as tinted blocks with their kind's icon, time and title, dashed when only an idea or a
/// part of the day. As many days as fit, never a fixed number, with the chosen day in the middle; each day keeps its
/// own local time. A tap opens the details in their column; a long press opens the item's menu. The days glide
/// sideways with the finger and come to rest with a day in the middle; the arrows glide by the days shown.
/// </summary>
public static class TimetableView
{
    private const double HourHeight = 52, Gutter = 56, MinColumn = 180, Top = 10;
    // The days the grid showed last and how far it was scrolled, so drawing them again keeps the place.
    private static (string? Trip, LocalDate First, int Count) placeOf;
    private static double kept;

    /// <summary>How many days fit into <paramref name="width"/> pixels, from three to seven, an odd number so one is in the middle.</summary>
    public static int Columns(double width) { var n = Math.Clamp((int)((width - Gutter - 32) / MinColumn), 3, 7); return n % 2 == 0 ? n - 1 : n; }

    /// <summary>How many days the strip holds at most; a longer trip shows this many around the chosen day.</summary>
    internal const int MostDays = 61;

    /// <summary>
    /// The days on the strip: all of them, or for a long trip <see cref="MostDays"/> around <paramref name="chosen"/>,
    /// with the index of the first.
    /// </summary>
    internal static (int First, AgendaDay[] Days) Window(IReadOnlyList<AgendaDay> days, int chosen)
    {
        if (days.Count <= MostDays) return (0, days.ToArray());
        var first = Math.Clamp(chosen - MostDays / 2, 0, days.Count - MostDays);
        return (first, days.Skip(first).Take(MostDays).ToArray());
    }

    /// <summary>
    /// The strip of days for <paramref name="days"/> (see <see cref="Window"/>) on <paramref name="area"/>: a day in the
    /// middle at first, gliding with the finger, and the chosen day following the one that comes to rest in the middle.
    /// Near the end of a long trip's window the plan is drawn again around the new day.
    /// </summary>
    /// <param name="width">The width of the whole timetable, the time column included.</param>
    /// <param name="share">How much of the days' width one day takes: one of the columns on the tablet, most of it on the phone.</param>
    internal static Controls.DayPan Pan(MobileSession s, Control area, double width, double share, (int First, AgendaDay[] Days) window, Action<int>? settled = null)
    {
        var lanes = Math.Max(1, width - Gutter);
        var pan = new Controls.DayPan(lanes * share, window.Days.Length, () => area.Bounds.Width > Gutter ? area.Bounds.Width - Gutter : lanes);
        var chosen = Math.Max(0, Array.FindIndex(window.Days, d => d.Date == s.SelectedDate));
        pan.Jump(pan.OffsetOf(chosen));
        // The first layout gives the real width; until the user moves the days, the chosen day stays in the middle.
        var moved = false;
        area.SizeChanged += (_, _) => { if (!moved) pan.Jump(pan.OffsetOf(chosen)); };
        pan.Settled += day =>
        {
            moved = true;
            var date = window.Days[day].Date;
            var all = s.Agenda.Days.Count;
            var nearEdge = window.Days.Length < all && (day < 5 && window.First > 0 || day > window.Days.Length - 6 && window.First + window.Days.Length < all);
            if (nearEdge) s.ShowDate(date); else s.FollowDate(date);
            settled?.Invoke(day);
        };
        pan.Follow(area);
        return pan;
    }

    /// <param name="leading">Shown at the start of the top row, such as the trip; the switch to the list goes in <paramref name="trailing"/>.</param>
    public static Control Build(MobileSession s, double width, Control? leading = null, Control? trailing = null)
    {
        var all = s.Agenda.Days;
        if (all.Count == 0) return Ui.Text(s.Strings["NothingPlanned"], "muted").Margin(16, 8);
        var visible = Math.Min(Columns(width), all.Count);
        var window = Window(all, Math.Max(0, all.ToList().FindIndex(d => d.Date == s.SelectedDate)));
        // The days glide sideways inside a frame that stays put, with a day in the middle once they come to rest.
        var area = new Border { ClipToBounds = true, Background = Brushes.Transparent };
        var range = Ui.Text("", "strong").Also(t => t.VerticalAlignment = VerticalAlignment.Center);
        var earlier = Ui.IconButton("chevron-left", s.Strings["EarlierDays"]);
        var later = Ui.IconButton("chevron-right", s.Strings["LaterDays"]);
        Controls.DayPan? pan = null;
        void Show()
        {
            var (first, last) = pan!.Visible;
            range.Text = Formats.DateRange(window.Days[first].Date, window.Days[last].Date);
            earlier.IsEnabled = window.First + first > 0; later.IsEnabled = window.First + last < all.Count - 1;
        }
        pan = Pan(s, area, width, 1.0 / visible, window, _ => Show());
        var (header, allDay, scroll, nights) = Parts(s, window.Days, pan);
        // The arrows glide by the days shown, as a swipe does.
        earlier.Click += (_, _) => pan.GlideTo(pan.Middle - visible);
        later.Click += (_, _) => pan.GlideTo(pan.Middle + visible);
        area.SizeChanged += (_, _) => Show();
        Show();
        var layout = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        // One row for the trip, the days shown and the switch, so the grid gets the height.
        layout.Children.Add(Ui.Columns("Auto,*,Auto", leading, Ui.H(4, earlier, later, range.Margin(8, 0, 0, 0)).Also(h => h.VerticalAlignment = VerticalAlignment.Center).Margin(leading is null ? 12 : 16, 0, 12, 0).Col(1), trailing?.Also(t => t.VerticalAlignment = VerticalAlignment.Center).Col(2)).Margin(0, 0, 12, 6));
        area.Child = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto"), Children = { header, allDay.Row(1), scroll.Row(2), nights.Row(3) } };
        layout.Children.Add(area.Row(1));
        return layout;
    }

    /// <summary>
    /// The timetable of <paramref name="shown"/>: the days' heads, the all-day strip, the time grid that scrolls, and the
    /// nights band. The phone shows one day this way, the tablet several. A single day has its date in the date strip
    /// above, so its head only names the zone when it is not the trip's, and the grid opens at its first plan, or at the
    /// hour before now on today.
    /// </summary>
    /// <param name="pan">
    /// The strip the days glide on, when there are more of them than fit: each part's days move with it while the time
    /// column stays. Without it the days share the width.
    /// </param>
    internal static (Control Header, Control AllDay, ScrollViewer Grid, Control Nights) Parts(MobileSession s, AgendaDay[] shown, Controls.DayPan? pan = null)
    {
        var count = shown.Length;
        // The days' columns: as wide as each part's room allows, or the pan's width each.
        var spec = pan is null ? string.Join(',', Enumerable.Repeat("*", count)) : string.Join(',', Enumerable.Repeat(pan.DayWidth.ToString(CultureInfo.InvariantCulture), count));
        Control Days(Control days) => pan is null ? days : pan.Layer(days);
        Grid Frame(Control days) => new Grid { ColumnDefinitions = new ColumnDefinitions(Gutter.ToString(CultureInfo.InvariantCulture) + ",*"), Children = { Days(days).Col(1) } };
        var columns = shown.Select(day => (Day: day, Zone: Zone(s, day), Lines: InGrid(day))).ToArray();

        // The whole day, midnight to midnight, so a night flight or an early start is always reachable; the grid opens
        // at the first plan instead (see below).
        const int from = 0, to = 24;

        var heads = new Grid { ColumnDefinitions = new ColumnDefinitions(spec) };
        var header = Frame(heads);
        var height = Top + (to - from) * HourHeight + 16;
        var lanes = new Grid { ColumnDefinitions = new ColumnDefinitions(spec), Height = height };
        var grid = Frame(lanes); grid.Height = height;
        var axis = new Canvas();
        for (var hour = from; hour <= to; hour++)
        {
            var y = Top + (hour - from) * HourHeight;
            axis.Children.Add(Ui.Text(hour.ToString("00", CultureInfo.InvariantCulture) + ":00", "small", "muted").Also(t => { Canvas.SetTop(t, y - 8); Canvas.SetLeft(t, 8); }));
            grid.Children.Insert(0, new Border { Height = 1, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, y, 0, 0), IsHitTestVisible = false }.Res(Border.BackgroundProperty, "Line").Also(b => Grid.SetColumnSpan(b, 2)));
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
            var date = Ui.Text(day.Date.ToString(Formats.DayPattern("d MMM", culture), culture), "strong");
            if (today) { weekday.Res(TextBlock.ForegroundProperty, "Accent"); date.Res(TextBlock.ForegroundProperty, "Accent"); }
            var place = zone.Id == tripZone.Id ? null : Ui.Text(TimeText.Place(zone, s.Trip, lines), "small", "muted");
            if ((count == 1 ? (Control?)place?.Margin(10, 0, 4, 4) : Ui.V(0, weekday, date, place).Margin(10, 0, 4, 6)) is { } head) heads.Children.Add(head.Col(i));
            var lane = new Canvas { ClipToBounds = true };
            lanes.Children.Add(new Border { BorderThickness = new Thickness(1, 0, 0, 0), Child = lane }.Res(Border.BorderBrushProperty, "Line").Col(i));
            Markers(s, lane, day, zone, from);
            Place(s, lane, day, zone, lines, from);
            if (today) lane.Children.Add(Now(s, zone, from));
        }

        var scroll = new ScrollViewer { Content = grid, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        // It opens an hour before now on today, otherwise at the first plan of the days shown (a journey from the day
        // before counts from midnight), or at eight without any.
        // On the strip only the days in view at first count, not the whole trip's.
        var seen = pan is null ? columns : columns[pan.Visible.First..(pan.Visible.Last + 1)];
        var opens = seen.Any(c => c.Day.Date == s.PhoneToday) ? s.Now.InZone(seen.First(c => c.Day.Date == s.PhoneToday).Zone).TimeOfDay.Hour - 1
            : seen.SelectMany(c => c.Lines.Select(l => l.Role == AgendaRole.Continued ? 0 : l.Start!.Value.InZone(c.Zone).TimeOfDay.Hour)).DefaultIfEmpty(8).Min();
        var opening = Math.Max(0, (opens - from) * HourHeight);
        // Drawn again for the same days, such as after a tap on a plan, the grid stays where it was scrolled to.
        var at = (s.Workspace?.Repository.Root, pan is null ? shown[0].Date : default, pan is null ? count : -1);
        var target = at == placeOf ? kept : opening;
        placeOf = at; var placed = false;
        scroll.ScrollChanged += (_, _) => { if (placed) kept = scroll.Offset.Y; };
        Controls.ScrollPlace.Set(scroll, () => { if (target > 0) scroll.Offset = new Vector(0, target); kept = scroll.Offset.Y; placed = true; });
        return (header, Band(s.Strings["AllDay"], AllDay(s, shown), spec, Days, top: true), scroll, Band(s.Strings["NightsBand"], Nights(s, shown), spec, Days, top: false));
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
    /// They lie behind the plans and take no taps, so plans over them stay readable and reachable.
    /// </summary>
    private static void Markers(MobileSession s, Canvas lane, AgendaDay day, DateTimeZone zone, int from)
    {
        var str = s.Strings;
        double Y(LocalTime time) => Top + (time.TickOfDay / (double)NodaConstants.TicksPerHour - from) * HourHeight;
        void Tag(string text, double y)
        {
            var tag = new Border { Padding = new Thickness(5, 1), CornerRadius = new CornerRadius(4), IsHitTestVisible = false, Child = Ui.H(3, Ui.Icon("bed-double", 11, "Kind.Accommodation.Fg"), Ui.Text(text, "small").Also(t => t.TextWrapping = TextWrapping.NoWrap)) }
                .Res(Border.BackgroundProperty, "Kind.Accommodation.Bg");
            Canvas.SetTop(tag, y - 9); lane.Children.Add(tag);
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
    /// <param name="spec">The days' columns.</param>
    /// <param name="days">Places the days' part, such as on the strip that glides.</param>
    private static Control Band(string name, IReadOnlyList<(Control Bar, int From, int To)> bars, string spec, Func<Control, Control> days, bool top)
    {
        if (bars.Count == 0) return new Grid();
        var lanes = new Grid { ColumnDefinitions = new ColumnDefinitions(spec) };
        var rows = new List<int>();
        foreach (var (bar, from, to) in bars.OrderBy(b => b.From))
        {
            var row = rows.FindIndex(end => end < from); if (row < 0) { rows.Add(to); row = rows.Count - 1; } else rows[row] = to;
            while (lanes.RowDefinitions.Count <= row) lanes.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            bar.Margin = new Thickness(4, 3, 4, 3); Grid.SetColumn(bar, from); Grid.SetColumnSpan(bar, to - from + 1); Grid.SetRow(bar, row);
            lanes.Children.Add(bar);
        }
        var band = new Grid { ColumnDefinitions = new ColumnDefinitions(Gutter.ToString(CultureInfo.InvariantCulture) + ",*"), Children = { days(lanes).Col(1) } };
        // The name fits the time column in one line, also a long one such as "Ganztägig".
        band.Children.Add(Ui.Text(name, "small", "muted").Also(t => { t.FontSize = 10.5; t.TextWrapping = TextWrapping.NoWrap; t.TextAlignment = TextAlignment.Right; t.Margin = new Thickness(0, 9, 5, 0); }));
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
