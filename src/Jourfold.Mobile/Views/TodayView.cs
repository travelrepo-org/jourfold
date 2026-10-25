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
    public static Control Build(MobileSession s, WidthClass width)
    {
        var now = s.Now; var phone = s.PhoneZone; var today = s.PhoneToday;
        var day = s.Agenda.Find(today);
        var page = Ui.V(16, Lines.TripHeader(s));
        if (day is null) { page.Children.Add(OutsideTrip(s, today)); return Ui.Scroll(page); }

        var lines = DayLines(s, day, today, phone);
        var current = lines.FirstOrDefault(l => !l.IsIdea && l.Status != "cancelled" && l.IsTimed && l.Start <= now && (l.End ?? l.Start) > now)
            ?? lines.FirstOrDefault(l => !l.IsIdea && l.Status != "cancelled" && l.IsTimed && l.Start > now);
        page.Children.Add(Ui.V(2, Ui.Text(Lines.DayTitle(today, s) + " · " + string.Format(CultureInfo.CurrentCulture, s.Strings["DayN"], day.Number), "h2"),
            Ui.Text(Zone(phone, now), "caption")).Margin(16, 0));
        if (current is not null) page.Children.Add(NextCard(s, current, now, phone).Margin(16, 0));

        var rail = Ui.V(10);
        var nowShown = false;
        foreach (var line in lines)
        {
            if (!nowShown && line.Start is { } at && at > now) { rail.Children.Add(NowLine(s, now, phone)); nowShown = true; }
            var past = (line.End ?? line.Start) is { } end && end <= now;
            var state = past ? LineState.Past : ReferenceEquals(line, current) ? LineState.Expanded : LineState.Normal;
            var time = TimeText.OnPhone(line, phone, s.Strings);
            rail.Children.Add(Ui.Columns("64,*", Views.Lines.TimeColumn(time, state), Views.Lines.Card(s, line, state, null, time: time)));
        }
        if (!nowShown) rail.Children.Add(NowLine(s, now, phone));
        page.Children.Add(rail.Margin(16, 0));
        if (Tonight(s, day) is { } tonight) page.Children.Add(tonight.Margin(16, 0));
        page.Margin = new Thickness(0, 0, 0, 24);
        return Ui.Scroll(page);
    }

    /// <summary>Everything that happens on the phone's today, steps of groups as their own lines, in time order.</summary>
    private static List<AgendaEntry> DayLines(MobileSession s, AgendaDay day, LocalDate today, DateTimeZone phone)
    {
        var from = phone.AtStartOfDay(today).ToInstant(); var to = phone.AtStartOfDay(today.PlusDays(1)).ToInstant();
        bool Touches(AgendaEntry e) => e.Start is { } a ? a < to && (e.End ?? a) >= from : e.Day == today;
        var all = s.Agenda.Days.SelectMany(d => d.Entries).SelectMany(e => e.IsGroup ? e.Steps : [e]).Where(Touches);
        return all.DistinctBy(e => (e.Item.Id, e.Role)).OrderBy(e => e.Precision == TimePrecision.AllDay ? 0 : e.Start is null ? 2 : 1).ThenBy(e => e.Start).ToList();
    }

    private static string Zone(DateTimeZone zone, Instant now) => Formats.ZoneName(zone.Id) + " (" + TravelRepo.Repository.ExportTimes.Abbreviation(zone, now) + ")";

    /// <summary>The next item, or the one happening now, with when it starts and a way to get there.</summary>
    private static Control NextCard(MobileSession s, AgendaEntry e, Instant now, DateTimeZone phone)
    {
        var happening = e.Start <= now;
        var time = TimeText.OnPhone(e, phone, s.Strings);
        var when = happening ? string.Format(CultureInfo.CurrentCulture, s.Strings["UntilTime"], e.End is { } end ? end.InZone(phone).TimeOfDay.ToString("HH:mm", CultureInfo.InvariantCulture) : "") : TimeText.Until(now, e.Start!.Value, s.Strings);
        var place = Views.Lines.Where(s.Trip!, e.Item);
        var navigate = Navigation.Button(s, e.Item);
        var content = Ui.V(10,
            Ui.Text(happening ? s.Strings["NowLabel"] : s.Strings["Next"], "overline"),
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

    /// <summary>Before or after the trip: how long until it starts, or that it is over.</summary>
    private static Control OutsideTrip(MobileSession s, LocalDate today)
    {
        var first = s.Agenda.Days.FirstOrDefault()?.Date; var last = s.Agenda.Days.LastOrDefault()?.Date;
        var text = first is { } f && today < f ? string.Format(CultureInfo.CurrentCulture, s.Strings["TripStartsIn"], Period.DaysBetween(today, f))
            : last is not null ? s.Strings["TripIsOver"] : s.Strings["NoDatesYet"];
        var plan = Ui.Button(s.Strings["OpenPlan"], "list", "soft"); plan.Click += (_, _) => s.ShowTab(MobileTab.Plan);
        return Ui.V(12, Ui.Text(text, "h3"), plan).Margin(16, 8);
    }
}
