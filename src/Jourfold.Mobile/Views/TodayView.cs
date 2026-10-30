using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using NodaTime;
using TravelRepo.Core;

namespace Jourfold.Mobile.Views;

/// <summary>
/// Today on the road: what is next and when, the rest of the day on a thin rail in the phone's time with past items
/// faded and a red line at the current time, and where the night is. No planning controls here.
/// </summary>
public static class TodayView
{
    /// <summary>Asks once who the user is in the trip, so reminders cover their own plans.</summary>
    private static Control? WhoAmI(MobileSession s)
    {
        if (!s.AskWhoAmI) return null;
        var choose = Ui.Button(s.Strings["ChooseWhoIAm"], "user-check", "soft").Also(b => b.HorizontalAlignment = HorizontalAlignment.Left);
        choose.Click += (_, _) => { s.ShowTab(MobileTab.More); s.ShowPage("travellers"); };
        return Ui.Card(Ui.V(8, Ui.Text(s.Strings["WhoAmITitle"], "strong"), Ui.Text(s.Strings["WhoAmIPhone"], "caption").Also(t => t.TextWrapping = Avalonia.Media.TextWrapping.Wrap), choose), 14).Margin(16, 0);
    }

    public static Control Build(MobileSession s, WidthClass width)
    {
        var now = s.Now; var phone = s.PhoneZone; var today = s.PhoneToday;
        var day = s.Agenda.Find(today);
        var page = Ui.V(16, Lines.TripHeader(s), WhoAmI(s));
        if (day is null) { page.Children.Add(OutsideTrip(s, today)); return Ui.Scroll(page); }

        var lines = DayLines(s, day, today, phone);
        var current = lines.FirstOrDefault(l => !l.IsIdea && l.Status != "cancelled" && l.IsTimed && l.Start <= now && (l.End ?? l.Start) > now)
            ?? lines.FirstOrDefault(l => !l.IsIdea && l.Status != "cancelled" && l.IsTimed && l.Start > now);
        page.Children.Add(Ui.V(2, Ui.Text(Lines.DayTitle(today, s) + " · " + string.Format(CultureInfo.CurrentCulture, s.Strings["DayN"], day.Number), "h2"),
            Views.Lines.ClockLine(s, TimeText.PhoneHeading(phone, s.Strings), phone)).Margin(16, 0));
        var next = current is null ? null : NextCard(s, current, now, phone);
        var rail = Ui.V(10);
        var nowShown = false;
        foreach (var line in lines)
        {
            if (!nowShown && line.Start is { } at && at > now) { rail.Children.Add(NowLine(s, now, phone)); nowShown = true; }
            var past = (line.End ?? line.Start) is { } end && end <= now;
            var state = past ? LineState.Past : ReferenceEquals(line, current) ? LineState.Expanded : LineState.Normal;
            var time = TimeText.OnPhone(line, phone, s.Strings, s.Trip);
            rail.Children.Add(Ui.Columns("64,*", Views.Lines.TimeColumn(time, state, TimeText.Second(line, s.SecondZone, phone)), Views.Lines.Card(s, line, state, () => s.ShowDetails(line.Item.Id), time: time)));
        }
        if (!nowShown) rail.Children.Add(NowLine(s, now, phone));
        var tonight = Tonight(s, day);
        // From 840 dp what comes next and tonight's stay stand beside the day instead of above and below it.
        if (width >= WidthClass.Tablet)
            page.Children.Add(Ui.Columns("5*,24,6*", Ui.V(16, next, tonight).Also(v => v.VerticalAlignment = VerticalAlignment.Top), null, rail.Col(2)).Margin(16, 0));
        else
        {
            if (next is not null) page.Children.Add(next.Margin(16, 0));
            page.Children.Add(rail.Margin(16, 0));
            if (tonight is not null) page.Children.Add(tonight.Margin(16, 0));
        }
        page.Margin = new Thickness(0, 0, 0, 24);
        return Ui.Scroll(page);
    }

    /// <summary>Everything that happens on the phone's today, steps of groups as their own lines, in time order.</summary>
    private static List<AgendaEntry> DayLines(MobileSession s, AgendaDay day, LocalDate today, DateTimeZone phone)
    {
        var from = phone.AtStartOfDay(today).ToInstant(); var to = phone.AtStartOfDay(today.PlusDays(1)).ToInstant();
        bool Touches(AgendaEntry e) => e.Start is { } a ? a < to && (e.End ?? a) >= from : e.Day == today;
        // Today reads spans itself, so an item from yesterday that is still running shows once, with its own times.
        var all = s.Agenda.Days.SelectMany(d => d.Entries).Where(e => e.Role != AgendaRole.Continued).SelectMany(e => e.IsGroup ? e.Steps : [e]).Where(Touches);
        return all.DistinctBy(e => (e.Item.Id, e.Role)).OrderBy(e => e.Precision == TimePrecision.AllDay ? 0 : e.Start is null ? 2 : 1).ThenBy(e => e.Start).ToList();
    }

    /// <summary>The next item, or the one happening now, with when it starts and a way to get there.</summary>
    private static Control NextCard(MobileSession s, AgendaEntry e, Instant now, DateTimeZone phone)
    {
        var happening = e.Start <= now;
        var time = TimeText.OnPhone(e, phone, s.Strings, s.Trip);
        var when = happening ? string.Format(CultureInfo.CurrentCulture, s.Strings["UntilTime"], e.End is { } end ? end.InZone(phone).TimeOfDay.ToString("HH:mm", CultureInfo.InvariantCulture) : "") : TimeText.Until(now, e.Start!.Value, s.Strings);
        var place = Views.Lines.Where(s.Trip!, e.Item);
        var navigate = Navigation.Button(s, e.Item);
        var content = Ui.V(10,
            Ui.Text(happening ? s.Strings["NowLabel"] : s.Strings["NextUp"], "overline"),
            Ui.V(2, Ui.Text(e.Item.Title, "h3"), Ui.Text(string.Join(" · ", new[] { time.Primary, place }.Where(t => t.Length > 0)), "muted"), time.Other is { } other ? Ui.Text(other, "caption") : null),
            Ui.Columns("*,Auto", Ui.H(6, Ui.Icon("clock", 16, "Accent"), Ui.Text(when, "strong")), navigate));
        return Ui.Card(content, 16).Also(c => c.BorderThickness = new Thickness(1)).Res(Border.BorderBrushProperty, "Accent");
    }

    private static Control NowLine(MobileSession s, Instant now, DateTimeZone phone)
    {
        var label = Ui.Text(now.InZone(phone).TimeOfDay.ToString("HH:mm", CultureInfo.InvariantCulture), "time", "now");
        var line = new Border { Height = 2, VerticalAlignment = VerticalAlignment.Center }.Res(Border.BackgroundProperty, "NowLine");
        return Ui.Columns("64,*,Auto", label, line, Ui.Text(s.Strings["NowLabel"], "small", "now").Margin(8, 0, 0, 0));
    }

    private static Control? Tonight(MobileSession s, AgendaDay day)
    {
        if (day.Nights.FirstOrDefault() is not { } night) return null;
        var text = night.Run.Main.Item.Title + " · " + string.Format(CultureInfo.CurrentCulture, s.Strings["NightOf"], night.Number, night.Run.Nights);
        return Ui.Card(Ui.Columns("Auto,*", Ui.Icon("moon", 18, "Kind.Accommodation.Fg").Margin(0, 0, 10, 0), Ui.V(1, Ui.Text(s.Strings["Tonight"], "overline"), Ui.Text(text, "strong"))), 14)
            .Res(Border.BackgroundProperty, "Kind.Accommodation.Bg");
    }

    /// <summary>
    /// Before or after the trip: how long until it starts, or that it is over. Before it, the first journey, the
    /// first stay and the first activity show how the trip begins, and open tasks are one tap away.
    /// </summary>
    private static Control OutsideTrip(MobileSession s, LocalDate today)
    {
        var first = s.Agenda.Days.FirstOrDefault()?.Date; var last = s.Agenda.Days.LastOrDefault()?.Date;
        var before = first is { } f && today < f;
        var text = before ? string.Format(CultureInfo.CurrentCulture, s.Strings["TripStartsIn"], Period.DaysBetween(today, first!.Value))
            : last is not null ? s.Strings["TripIsOver"] : s.Strings["NoDatesYet"];
        var plan = Ui.Button(s.Strings["OpenPlan"], "list", "soft"); plan.Click += (_, _) => s.ShowTab(MobileTab.Plan);
        var page = Ui.V(16, Ui.Text(text, "h2").Margin(16, 8, 16, 0));
        if (before)
        {
            var starts = Starts(s);
            if (starts.Count > 0)
            {
                var list = Ui.V(12, Ui.Text(s.Strings["HowItStarts"], "overline"));
                foreach (var (label, entry) in starts)
                {
                    var time = TimeText.Local(entry, s.PhoneZone, s.Strings, s.Trip);
                    var day = entry.Day is { } d ? Views.Lines.DayTitle(d, s) : "";
                    list.Children.Add(Ui.V(6, Ui.Text(string.Join(" · ", new[] { label, day, entry.IsTimed ? time.Primary : "" }.Where(t => t.Length > 0)), "caption"),
                        Views.Lines.Card(s, entry, LineState.Normal, () => s.ShowDetails(entry.Item.Id), time: time)));
                }
                page.Children.Add(list.Margin(16, 0));
            }
            if (MoreView.OpenTasks(s.Trip!) is > 0 and var open)
            {
                var tasks = Ui.Button(string.Format(CultureInfo.CurrentCulture, s.Strings["OpenTasksBeforeTrip"], open), "square-check", "ghost");
                tasks.Click += (_, _) => s.ShowPage("tasks");
                page.Children.Add(tasks.Margin(8, 0));
            }
        }
        page.Children.Add(plan.Also(b => b.HorizontalAlignment = HorizontalAlignment.Left).Margin(16, 0, 16, 24));
        return page;
    }

    /// <summary>The first journey, the first stay and the first activity of the trip, in time order. Ideas and cancelled items are left out.</summary>
    private static List<(string Label, AgendaEntry Entry)> Starts(MobileSession s)
    {
        var lines = s.Agenda.Days.SelectMany(d => d.Entries).Where(e => e.Role is AgendaRole.Item or AgendaRole.CheckIn && !e.IsIdea && e.Status != "cancelled").ToList();
        var journey = lines.FirstOrDefault(e => e.Role == AgendaRole.Item && e.Kind == ScheduleKind.Transport);
        var stay = lines.FirstOrDefault(e => e.Role == AgendaRole.CheckIn);
        var activity = lines.FirstOrDefault(e => e.Role == AgendaRole.Item && e.Kind is not (ScheduleKind.Transport or ScheduleKind.Accommodation));
        return new (string, AgendaEntry?)[] { (s.Strings["FirstJourney"], journey), (s.Strings["FirstStay"], stay), (s.Strings["FirstActivity"], activity) }
            .Where(x => x.Item2 is not null).Select(x => (x.Item1, x.Item2!)).OrderBy(x => lines.IndexOf(x.Item2)).ToList();
    }
}
