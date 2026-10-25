using System.Globalization;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using NodaTime;
using TravelRepo.Core;

namespace Jourfold.Mobile.Views;

/// <summary>
/// The plan day by day: a date strip that stays at the top while the day scrolls, the day's items as dense cards in
/// local time, groups that unfold inline, the item happening now or next with a little more context, the night, and
/// the ideas without a day at the end. A tap opens an item's details. Swipe sideways to change day.
/// </summary>
public static class PlanView
{
    // Kept between draws, which replace the page: how far it was scrolled, for which trip and day, and from which side
    // the next day slides in.
    private static double offset;
    private static (string? Trip, LocalDate Date) shown;
    private static int enter;

    public static Control Build(MobileSession s, WidthClass width)
    {
        var day = s.Agenda.Find(s.SelectedDate) ?? s.Agenda.Days.FirstOrDefault();
        var strip = new Border { Child = DateStrip(s), ZIndex = 1, RenderTransform = new TranslateTransform(), Padding = new Thickness(0, 4, 0, 6) }
            .Res(Border.BackgroundProperty, "Bg.App").Res(Border.BorderBrushProperty, "Line");
        var content = day is null ? Ui.Text(s.Strings["NothingPlanned"], "muted").Margin(16, 8) : Day(s, day);
        var page = Ui.V(6, Lines.TripHeader(s), strip, content);
        page.Margin = new Thickness(0, 0, 0, 24);
        var scroll = new ScrollViewer { Content = page, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        Sticky(s, scroll, strip, day?.Date);
        if (day is not null) Swipeable(s, scroll, strip, content);
        return scroll;
    }

    private static Control Day(MobileSession s, AgendaDay day)
    {
        var zone = ScheduleQueries.TripZone(s.Trip!);
        var body = Ui.V(12, Ui.V(2, Ui.Text(Lines.DayTitle(day.Date, s), "h2"),
            Ui.Text(string.Format(CultureInfo.CurrentCulture, s.Strings["DayN"], day.Number) + " · " + TimeText.LocalHeading(day, zone, s.Strings, s.Trip), "caption")).Margin(16, 6, 16, 0));

        // On the phone's today, the item happening now or next shows its context; every other card stays compact.
        var now = s.Now;
        var current = day.Date != s.PhoneToday ? null : day.Entries.Where(e => e.Role == AgendaRole.Item && !e.IsGroup && e.IsTimed && e.Status != "cancelled")
            .FirstOrDefault(e => e.Start <= now && (e.End ?? e.Start) > now || e.Start > now);
        var list = Ui.V(8);
        foreach (var entry in day.Entries)
        {
            var open = entry.IsGroup && s.ExpandedGroups.Contains(entry.Item.Id);
            var time = TimeText.Local(entry, s.PhoneZone, s.Strings, s.Trip);
            var extra = open ? Lines.Steps(s, entry, e => TimeText.Local(e, s.PhoneZone, s.Strings, s.Trip))
                : ReferenceEquals(entry, current) ? Context(s, entry)
                : entry.Role == AgendaRole.CheckIn ? Nights(s, day, entry) : null;
            var card = Lines.Card(s, entry, ReferenceEquals(entry, current) ? LineState.Expanded : LineState.Normal,
                entry.IsGroup ? () => s.ToggleGroup(entry.Item.Id) : () => s.ShowDetails(entry.Item.Id), extra, open, time);
            list.Children.Add(Ui.Columns("64,*", Lines.TimeColumn(time, LineState.Normal), card));
        }
        if (day.Entries.Count == 0) list.Children.Add(Ui.Text(s.Strings["NothingPlanned"], "muted").Margin(0, 8));
        // The night closes the day, unless the stay's check-in on this day already says where it is.
        foreach (var night in day.Nights.Where(n => !day.Entries.Any(e => e.Role == AgendaRole.CheckIn && n.Run.Stays.Any(x => x.Item.Id == e.Item.Id))).Take(1))
            list.Children.Add(Ui.Columns("64,*", Ui.Icon("moon", 18, "Kind.Accommodation.Fg").Also(i => i.HorizontalAlignment = HorizontalAlignment.Left).Margin(0, 0, 8, 0),
                Ui.Text(night.Run.Main.Item.Title + " · " + string.Format(CultureInfo.CurrentCulture, s.Strings["NightOf"], night.Number, night.Run.Nights), "caption")).Margin(0, 4));
        body.Children.Add(list.Margin(16, 0));
        if (s.Agenda.Unscheduled.Count > 0) body.Children.Add(Ideas(s).Margin(16, 8, 16, 0));
        return body;
    }

    /// <summary>How many nights a stay that starts today has.</summary>
    private static Control? Nights(MobileSession s, AgendaDay day, AgendaEntry checkIn)
    {
        if (day.Nights.FirstOrDefault(n => n.Run.Stays.Any(x => x.Item.Id == checkIn.Item.Id)) is not { } night) return null;
        return Ui.H(6, Ui.Icon("moon", 14, "Kind.Accommodation.Fg"), Ui.Text(night.Run.Nights == 1 ? s.Strings["OneNight"] : string.Format(CultureInfo.CurrentCulture, s.Strings["NightsCount"], night.Run.Nights), "caption"));
    }

    /// <summary>One chip per trip day; the phone's today has a dot under its number.</summary>
    private static Control DateStrip(MobileSession s)
    {
        var culture = CultureInfo.GetCultureInfo(s.Strings.Language);
        var strip = Ui.H(4);
        foreach (var day in s.Agenda.Days)
        {
            var label = day.Date.ToString("ddd", culture);
            var content = Ui.V(2, Ui.Text(label, "small").Also(t => t.HorizontalAlignment = HorizontalAlignment.Center), Ui.Text(day.Date.Day.ToString(CultureInfo.InvariantCulture), "strong").Also(t => t.HorizontalAlignment = HorizontalAlignment.Center),
                day.Date == s.PhoneToday ? new Border { Width = 5, Height = 5, CornerRadius = new CornerRadius(3), HorizontalAlignment = HorizontalAlignment.Center }.Res(Border.BackgroundProperty, "NowLine") : null);
            var chip = new ChoiceButton { IsSelected = day.Date == s.SelectedDate, Content = content }.Classed("date");
            Avalonia.Automation.AutomationProperties.SetName(chip, Lines.DayTitle(day.Date, s));
            chip.Click += (_, _) => { enter = day.Date > s.SelectedDate ? 1 : day.Date < s.SelectedDate ? -1 : 0; s.ShowDate(day.Date); };
            strip.Children.Add(chip);
        }
        var scroll = new ScrollViewer { Content = strip, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled, Padding = new Thickness(12, 0) };
        // Keep the selected day in view when the strip is wider than the screen.
        scroll.AttachedToVisualTree += (_, _) => Dispatcher.UIThread.Post(() => strip.Children.OfType<ChoiceButton>().FirstOrDefault(c => c.IsSelected)?.BringIntoView());
        return scroll;
    }

    /// <summary>
    /// Keeps the date strip at the top once the trip header has scrolled away, and the page where it was when it is
    /// drawn again. A new day starts at its top, with the strip still in place.
    /// </summary>
    private static void Sticky(MobileSession s, ScrollViewer scroll, Border strip, LocalDate? date)
    {
        var at = (s.Workspace?.Repository.Root, date ?? default);
        var restore = at.Root == shown.Trip ? offset : 0;
        var sameDay = at == shown;
        shown = at;
        var move = (TranslateTransform)strip.RenderTransform!;
        void Pin()
        {
            move.Y = Math.Max(0, scroll.Offset.Y - strip.Bounds.Top);
            strip.BorderThickness = new Thickness(0, 0, 0, move.Y > 0 ? 1 : 0);
        }
        scroll.ScrollChanged += (_, _) => { offset = scroll.Offset.Y; Pin(); };
        scroll.LayoutUpdated += (_, _) => Pin();
        scroll.AttachedToVisualTree += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            var target = sameDay ? restore : Math.Min(restore, strip.Bounds.Top);
            if (target > 0) scroll.Offset = new Vector(0, target);
            offset = scroll.Offset.Y;
        }, DispatcherPriority.Loaded);
    }

    /// <summary>The current item's extra lines: its notes in short and the way to get there.</summary>
    private static Control? Context(MobileSession s, AgendaEntry entry)
    {
        var note = entry.Item.Data["body"]?.ToString() ?? entry.Item.Data["description"]?.ToString();
        var navigate = Navigation.Button(s, entry.Item);
        if (note is not { Length: > 0 } && navigate is null) return null;
        return Ui.V(8,
            note is { Length: > 0 } ? Ui.Text(note.Length > 140 ? note[..140].TrimEnd() + "…" : note, "muted") : null,
            navigate?.Also(b => b.HorizontalAlignment = HorizontalAlignment.Left));
    }

    private static Control Ideas(MobileSession s)
    {
        var list = Ui.V(8, Ui.H(8, Ui.Icon("lightbulb", 18, "Text.Muted"), Ui.Text(s.Strings["Ideas"], "h3")));
        foreach (var idea in s.Agenda.Unscheduled)
        {
            var length = ScheduleQueries.PlannedDuration(idea.Item) is { } d ? Formats.Duration(d, s.Strings) : null;
            list.Children.Add(Lines.Card(s, idea, LineState.Normal, () => s.ShowDetails(idea.Item.Id), length is null ? null : Ui.Text(length, "caption")));
        }
        return list;
    }

    /// <summary>
    /// A sideways swipe over the day moves it with the finger and, once far or fast enough, slides it out while the
    /// next day slides in from the other side. Vertical scrolling is left alone, and the date strip scrolls by itself.
    /// </summary>
    private static void Swipeable(MobileSession s, ScrollViewer scroll, Control strip, Control content)
    {
        var move = new TranslateTransform();
        content.RenderTransform = move;
        var index = s.Agenda.Days.ToList().FindIndex(d => d.Date == s.SelectedDate);
        bool Can(int direction) => index + direction >= 0 && index + direction < s.Agenda.Days.Count;
        var slide = new DoubleTransition { Property = TranslateTransform.XProperty, Duration = TimeSpan.FromMilliseconds(160), Easing = new CubicEaseOut() };

        // The day that was just chosen comes in from the side it was swiped from.
        if (enter != 0)
        {
            move.X = enter * 360; content.Opacity = 0.4;
            var from = enter; enter = 0;
            content.AttachedToVisualTree += (_, _) => Dispatcher.UIThread.Post(() =>
            {
                move.X = from * Math.Max(1, scroll.Bounds.Width) * 0.6;
                move.Transitions = [slide]; content.Transitions = [new DoubleTransition { Property = Visual.OpacityProperty, Duration = TimeSpan.FromMilliseconds(160) }];
                move.X = 0; content.Opacity = 1;
            }, DispatcherPriority.Loaded);
        }

        Point? start = null; bool? sideways = null; ulong lastTime = 0; double lastX = 0, speed = 0;
        void Back() { move.Transitions = [slide]; move.X = 0; content.Opacity = 1; sideways = null; start = null; }
        scroll.AddHandler(InputElement.PointerPressedEvent, (_, e) =>
        {
            var inStrip = e.Source is Visual v && (v == strip || Avalonia.VisualTree.VisualExtensions.GetVisualAncestors(v).Contains(strip));
            start = inStrip ? null : e.GetPosition(scroll); sideways = null; speed = 0; lastTime = e.Timestamp; lastX = start?.X ?? 0;
        }, RoutingStrategies.Tunnel, handledEventsToo: true);
        scroll.AddHandler(InputElement.PointerMovedEvent, (_, e) =>
        {
            if (start is not { } from) return;
            var at = e.GetPosition(scroll); var delta = at - from;
            if (sideways is null)
            {
                if (Math.Abs(delta.X) > 14 && Math.Abs(delta.X) > 1.4 * Math.Abs(delta.Y)) { sideways = true; e.Pointer.Capture(scroll); move.Transitions = null; content.Transitions = null; }
                else if (Math.Abs(delta.Y) > 14) { sideways = false; start = null; return; }
                else return;
            }
            if (e.Timestamp > lastTime) { speed = (at.X - lastX) / (e.Timestamp - lastTime); lastTime = e.Timestamp; lastX = at.X; }
            // At the first and last day the page follows only a little, to show there is nothing further.
            var direction = delta.X < 0 ? 1 : -1;
            move.X = Can(direction) ? delta.X : delta.X * 0.2;
            content.Opacity = 1 - Math.Min(0.5, Math.Abs(move.X) / Math.Max(1, scroll.Bounds.Width));
            e.Handled = true;
        }, RoutingStrategies.Tunnel, handledEventsToo: true);
        scroll.AddHandler(InputElement.PointerReleasedEvent, (_, e) =>
        {
            if (sideways != true || start is not { } from) { start = null; return; }
            var dx = e.GetPosition(scroll).X - from.X; var direction = dx < 0 ? 1 : -1;
            var far = Math.Abs(dx) > scroll.Bounds.Width * 0.25 || Math.Abs(speed) > 0.5 && Math.Abs(dx) > 40 && Math.Sign(speed) == Math.Sign(dx);
            sideways = null; start = null;
            e.Handled = true; e.Pointer.Capture(null);
            if (!far || !Can(direction)) { Back(); return; }
            move.Transitions = [slide]; move.X = -direction * scroll.Bounds.Width; content.Opacity = 0.3;
            DispatcherTimer.RunOnce(() => { enter = direction; s.StepDay(direction); }, TimeSpan.FromMilliseconds(150));
        }, RoutingStrategies.Tunnel, handledEventsToo: true);
        scroll.AddHandler(InputElement.PointerCaptureLostEvent, (_, _) => { if (sideways == true) Back(); }, RoutingStrategies.Bubble, handledEventsToo: true);
    }
}
