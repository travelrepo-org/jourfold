using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using NodaTime;
using TravelRepo.Core;

namespace Jourfold.Mobile.Views;

/// <summary>
/// The plan day by day: a date strip, the day's items as dense cards in local time, groups that unfold inline, one
/// selected item with a little more context, the night, and the ideas without a day at the end. Swipe to change day.
/// </summary>
public static class PlanView
{
    public static Control Build(MobileSession s, WidthClass width)
    {
        var day = s.Agenda.Find(s.SelectedDate) ?? s.Agenda.Days.FirstOrDefault();
        var page = Ui.V(12, Lines.TripHeader(s), DateStrip(s));
        if (day is null) { page.Children.Add(Ui.Text(s.Strings["NoDatesYet"], "muted").Margin(16, 8)); return Ui.Scroll(page); }

        var zone = ScheduleQueries.TripZone(s.Trip!);
        page.Children.Add(Ui.V(2, Ui.Text(Lines.DayTitle(day.Date, s), "h2"),
            Ui.Text(string.Format(CultureInfo.CurrentCulture, s.Strings["DayN"], day.Number) + " · " + Formats.ZoneName(zone.Id), "caption")).Margin(16, 4, 16, 0));

        var list = Ui.V(8);
        foreach (var entry in day.Entries)
        {
            var selected = s.SelectedItem == entry.Item.Id;
            var open = entry.IsGroup && s.ExpandedGroups.Contains(entry.Item.Id);
            Control? extra = open ? Lines.Steps(s, entry, e => TimeText.Local(e, s.PhoneZone, s.Strings))
                : selected ? Context(s, entry) : null;
            var time = TimeText.Local(entry, s.PhoneZone, s.Strings);
            var card = Lines.Card(s, entry, selected ? LineState.Expanded : LineState.Normal,
                entry.IsGroup ? () => s.ToggleGroup(entry.Item.Id) : () => s.Select(entry.Item.Id), extra, open, time);
            // The phone's time appears with the selected item only; the list stays in local time.
            list.Children.Add(Ui.Columns("64,*", Lines.TimeColumn(time, LineState.Normal, showOther: false), card));
        }
        if (day.Entries.Count == 0) list.Children.Add(Ui.Text(s.Strings["NothingPlanned"], "muted").Margin(0, 8));
        if (day.Nights.FirstOrDefault() is { } night)
            list.Children.Add(Ui.Columns("64,*", Ui.Icon("moon", 18, "Kind.Accommodation.Fg").Also(i => i.HorizontalAlignment = HorizontalAlignment.Left).Margin(0, 0, 8, 0),
                Ui.Text(night.Run.Main.Item.Title + " · " + string.Format(CultureInfo.CurrentCulture, s.Strings["NightOf"], night.Number, night.Run.Nights), "caption")).Margin(0, 4));
        page.Children.Add(list.Margin(16, 0));
        if (s.Agenda.Unscheduled.Count > 0) page.Children.Add(Ideas(s).Margin(16, 8, 16, 0));
        page.Margin = new Thickness(0, 0, 0, 24);
        return Swipeable(s, Ui.Scroll(page));
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
            chip.Click += (_, _) => s.ShowDate(day.Date);
            strip.Children.Add(chip);
        }
        var scroll = new ScrollViewer { Content = strip, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled, Padding = new Thickness(12, 0) };
        // Keep the selected day in view when the strip is wider than the screen.
        scroll.AttachedToVisualTree += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(() => strip.Children.OfType<ChoiceButton>().FirstOrDefault(c => c.IsSelected)?.BringIntoView());
        return scroll;
    }

    /// <summary>The selected item's extra lines: its notes in short and the way to get there.</summary>
    private static Control? Context(MobileSession s, AgendaEntry entry)
    {
        var note = entry.Item.Data["body"]?.ToString() ?? entry.Item.Data["description"]?.ToString();
        var time = TimeText.Local(entry, s.PhoneZone, s.Strings);
        var navigate = Navigation.Button(s, entry.Item);
        if (note is null && time.Other is null && navigate is null) return null;
        return Ui.V(8,
            time.Other is { } other ? Ui.Text(string.Format(CultureInfo.CurrentCulture, s.Strings["OnYourPhone"], other), "caption") : null,
            note is { Length: > 0 } ? Ui.Text(note.Length > 140 ? note[..140].TrimEnd() + "…" : note, "muted") : null,
            navigate?.Also(b => b.HorizontalAlignment = HorizontalAlignment.Left));
    }

    private static Control Ideas(MobileSession s)
    {
        var list = Ui.V(8, Ui.H(8, Ui.Icon("lightbulb", 18, "Text.Muted"), Ui.Text(s.Strings["Ideas"], "h3")));
        foreach (var idea in s.Agenda.Unscheduled)
        {
            var length = ScheduleQueries.PlannedDuration(idea.Item) is { } d ? Formats.Duration(d, s.Strings) : null;
            list.Children.Add(Lines.Card(s, idea, LineState.Normal, () => s.Select(idea.Item.Id), length is null ? null : Ui.Text(length, "caption")));
        }
        return list;
    }

    /// <summary>A horizontal swipe changes the day; vertical scrolling is left alone.</summary>
    private static Control Swipeable(MobileSession s, Control content)
    {
        Point? start = null;
        content.AddHandler(InputElement.PointerPressedEvent, (_, e) => start = e.GetPosition(content), Avalonia.Interactivity.RoutingStrategies.Tunnel);
        content.AddHandler(InputElement.PointerReleasedEvent, (_, e) =>
        {
            if (start is not { } from) return;
            var delta = e.GetPosition(content) - from; start = null;
            if (Math.Abs(delta.X) > 80 && Math.Abs(delta.X) > 2 * Math.Abs(delta.Y)) { s.StepDay(delta.X < 0 ? 1 : -1); e.Handled = true; }
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        return content;
    }
}
