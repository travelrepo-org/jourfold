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

/// <summary>How a line looks: normal, already over (muted), or the current or selected one with more context.</summary>
public enum LineState { Normal, Past, Expanded }

/// <summary>Pieces shared by Today and Plan: the trip header, item lines, status chips and groups.</summary>
public static class Lines
{
    /// <summary>The compact trip header: cover, name, dates and travellers. Trip switching is the back button, not a menu.</summary>
    public static Control TripHeader(MobileSession s)
    {
        var trip = s.Trip!; var (start, end) = TripQueries.EffectiveDates(trip);
        var people = trip.Entities.Values.Where(e => e.Type == "person").Select(p => p.Title).Order(StringComparer.CurrentCulture).ToArray();
        var meta = string.Join(" · ", new[] { Formats.DateRange(start, end), Names(people, s) }.Where(t => t.Length > 0));
        var back = Ui.IconButton("chevron-left", s.Strings["Trips"]); back.Click += (_, _) => s.Close();
        return Ui.Columns("Auto,Auto,*", back,
            Covers.Thumbnail(s.Workspace?.Repository, trip, 44, 44, 10).Margin(0, 0, 12, 0),
            Ui.V(1, Ui.Line(trip.Manifest.Title, "title"), Ui.Line(meta.Length > 0 ? meta : s.Strings["NoDatesYet"], "caption"))).Margin(4, 6, 16, 6);
    }

    /// <summary>"Alex and Sam", "Alex, Sam and Kim".</summary>
    public static string Names(IReadOnlyList<string> names, MobileSession s) => names.Count switch
    {
        0 => "",
        1 => names[0],
        _ => string.Join(", ", names.Take(names.Count - 1)) + " " + s.Strings["And"] + " " + names[^1]
    };

    public static string Where(TripSnapshot trip, Entity item)
    {
        var (from, to) = ScheduleQueries.Route(trip, item);
        if (from is not null && to is not null) return from.Title + " → " + to.Title;
        return ScheduleQueries.PrimaryPlace(trip, item)?.Title ?? "";
    }

    public static string? BookingReference(TripSnapshot trip, Entity item)
    {
        var reference = item.Data["components"]?["booking"]?["ref"] ?? item.Data["components"]?["accommodation"]?["booking"];
        return Guid.TryParse(reference?.ToString(), out var id) ? trip.Find(id)?.Data["reference"]?.ToString() : null;
    }

    /// <summary>The status as a word and an icon, only where it says something: planned is the normal case and not shown.</summary>
    public static Control? StatusChip(string? status, MobileSession s) => status switch
    {
        "idea" => Ui.Pill(s.Strings["idea"], "lightbulb", "Bg.Subtle", "Text.Muted"),
        "reserved" => Ui.Pill(s.Strings["reserved"], "clock", "Warning.Soft", "Warning"),
        "confirmed" => Ui.Pill(s.Strings["confirmed"], "check", "Success.Soft", "Success"),
        "completed" => Ui.Pill(s.Strings["completed"], "check", "Bg.Subtle", "Text.Muted"),
        "cancelled" => Ui.Pill(s.Strings["cancelled"], "x", "Danger.Soft", "Danger"),
        _ => null
    };

    /// <summary>
    /// One item as a card with a coloured bar for its kind. Ideas get a lighter fill and a dashed outline; done items
    /// are muted; cancelled ones are struck through. Groups show their step count and unfold inline.
    /// </summary>
    /// <remarks>A long press opens the item's menu (<see cref="Menus.Item"/>).</remarks>
    public static Control Card(MobileSession s, AgendaEntry e, LineState state, Action? onTap, Control? extra = null, bool expandedGroup = false, ClockText? time = null)
    {
        var trip = s.Trip!; var key = Visuals.KindKey(e.Kind);
        var icon = Visuals.KindIcon(e.Kind, ScheduleCategories.TransportType(e.Item));
        var title = Ui.Text(e.Item.Title, "title").Also(t => { if (e.Status == "cancelled") t.TextDecorations = TextDecorations.Strikethrough; });
        var place = Where(trip, e.Item);
        var reference = BookingReference(trip, e.Item);
        var role = e.Role switch
        {
            AgendaRole.CheckIn => s.Strings["CheckIn"],
            AgendaRole.CheckOut => s.Strings["CheckOut"],
            AgendaRole.Continued => !TimeText.EndsOnItsDay(e) ? s.Strings["ContinuesLine"] : e.Kind == ScheduleKind.Transport ? s.Strings["ArrivalLine"] : s.Strings["EndsLine"],
            _ => ""
        };
        var arrival = time?.Arrival is { } arrives ? string.Format(CultureInfo.CurrentCulture, s.Strings["ArrivesAt"], arrives) : "";
        var details = string.Join(" · ", new[] { role, place, arrival, e.IsGroup ? string.Format(CultureInfo.CurrentCulture, s.Strings["StepsCount"], e.Steps.Count) : "", reference ?? "" }.Where(t => t.Length > 0));
        var header = Ui.Columns("Auto,*,Auto",
            Ui.Icon(icon, 18, "Kind." + key + ".Fg").Also(i => i.VerticalAlignment = VerticalAlignment.Top).Margin(0, 2, 10, 0),
            Ui.V(2, title, details.Length > 0 ? Ui.Text(details, "caption") : null, time?.Other is { } other ? Ui.Text(other, "caption", "subtle") : null),
            e.IsGroup ? Ui.Icon(expandedGroup ? "chevron-up" : "chevron-down", 18, "Text.Muted").Margin(8, 0, 0, 0) : StatusChip(e.Status, s)?.Also(c => { c.VerticalAlignment = VerticalAlignment.Top; c.Margin = new Thickness(8, 0, 0, 0); }));
        var body = Ui.V(8, header, extra);
        var idea = e.IsIdea;
        var card = new Border
        {
            Child = body,
            Padding = new Thickness(14, 10, 12, 10),
            CornerRadius = new CornerRadius(12),
            BorderThickness = new Thickness(idea ? 0 : 4, 0, 0, 0)
        }.Res(Border.BorderBrushProperty, "Kind." + key + ".Bar").Res(Border.BackgroundProperty, idea ? "Bg.Surface" : "Kind." + key + ".Bg");
        Control visual = idea ? Dashed(card, "Kind." + key + ".Bar") : card;
        if (state == LineState.Past || e.Status == "completed") visual.Opacity = 0.55;
        if (onTap is null) return visual;
        var button = new Button { Content = visual }.Classed("card");
        AutomationProperties.SetName(button, e.Item.Title + (details.Length > 0 ? ", " + details : ""));
        Menus.OnHold(button, () => s.ShowMenu(Menus.Item(s, e.Item)));
        button.Click += (_, _) => onTap();
        return button;
    }

    /// <summary>A dashed outline around a tentative item, drawn over its card.</summary>
    private static Control Dashed(Control card, string brushKey)
    {
        var outline = new Rectangle { RadiusX = 12, RadiusY = 12, StrokeThickness = 1.5, StrokeDashArray = [4, 3], IsHitTestVisible = false }.Res(Shape.StrokeProperty, brushKey);
        return new Panel { Children = { card, outline } };
    }

    /// <summary>The steps of a group, unfolded inline: time and title on a thin rail.</summary>
    public static Control Steps(MobileSession s, AgendaEntry group, Func<AgendaEntry, ClockText> time)
    {
        var list = Ui.V(6);
        foreach (var step in group.Steps)
        {
            var clock = time(step);
            list.Children.Add(Ui.Columns("52,*", Ui.Text(clock.Start, "time", "small", "muted").Also(t => t.VerticalAlignment = VerticalAlignment.Top), Ui.V(0, Ui.Text(step.Item.Title, "strong"), Where(s.Trip!, step.Item) is { Length: > 0 } w ? Ui.Text(w, "caption") : null)));
        }
        return new Border { Child = list, BorderThickness = new Thickness(2, 0, 0, 0), Padding = new Thickness(10, 2, 0, 2) }.Res(Border.BorderBrushProperty, "Line.Strong");
    }

    /// <summary>The time column next to a card: the main time, its end and, for a journey across zones, its place.</summary>
    public static Control TimeColumn(ClockText time, LineState state)
    {
        // Words such as "Afternoon" or "All day" are set smaller so they fit the narrow column.
        var column = Ui.V(1, time.Start.Any(char.IsDigit) ? Ui.Text(time.Start, "time") : Ui.Text(time.Start, "small", "strong"),
            time.End is { } end ? Ui.Text(end, "small", "muted") : null,
            time.Zone is { } zone ? Ui.Text(zone, "small", "muted").Also(t => t.TextTrimming = TextTrimming.CharacterEllipsis) : null);
        if (state == LineState.Past) column.Opacity = 0.55;
        column.VerticalAlignment = VerticalAlignment.Top; column.Margin = new Thickness(0, 10, 8, 0);
        return column;
    }

    public static string DayTitle(LocalDate date, MobileSession s) => date.ToString(s.Strings["DayTitleFormat"], CultureInfo.GetCultureInfo(s.Strings.Language));
    /// <summary>A day in short, such as "Sun 16 May" or "So., 16. Mai".</summary>
    public static string DayShort(LocalDate date, Localization s) => date.ToString(s["DayShortFormat"], CultureInfo.GetCultureInfo(s.Language));
}
