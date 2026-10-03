using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using NodaTime;
using TravelRepo.Core;
using TravelRepo.Merge;

namespace Jourfold.Desktop;

/// <summary>
/// Semantic comparison of two plans: a side-by-side day timetable with added, removed and changed items
/// marked by icon and label (not color alone), and a structured list of field changes.
/// </summary>
public sealed class ComparisonView : TabControl
{
    private readonly TripSnapshot current, incoming;
    private readonly Localization s;
    private readonly MainViewModel? model;
    private readonly string currentTitle, otherTitle;

    public ComparisonView(TripSnapshot current, TripSnapshot incoming, Localization strings, string? currentTitle = null, string? otherTitle = null, MainViewModel? model = null)
    {
        this.current = current; this.incoming = incoming; s = strings; this.model = model;
        this.currentTitle = currentTitle ?? strings["UseCurrent"]; this.otherTitle = otherTitle ?? strings["UseVariant"];
        ItemsSource = new[] { new TabItem { Header = strings["ScheduleComparison"], Content = Timetable() }, new TabItem { Header = strings["DetailsComparison"], Content = Details() } };
    }

    private string State(Entity e, TripSnapshot other)
    {
        var counterpart = other.Find(e.Id);
        if (counterpart is null) return ReferenceEquals(other, incoming) ? "Removed" : "Added";
        return JsonNode.DeepEquals(e.Data, counterpart.Data) ? "" : "ChangedDetails";
    }

    private Control Timetable()
    {
        var zone = ScheduleQueries.TripZone(current);
        LocalDate? Day(TripSnapshot trip, Entity e) => ScheduleQueries.Span(trip, e).Start?.InZone(zone).Date;
        var days = new[] { current, incoming }.SelectMany(t => t.Entities.Values.Where(e => e.Type == "schedule_item" && e.Data["children"] is not JsonArray { Count: > 0 }).Select(e => Day(t, e))).OfType<LocalDate>().Distinct().Order().ToArray();
        var changedDays = days.Where(d => new[] { (current, incoming), (incoming, current) }.Any(p => p.Item1.Entities.Values.Any(e => e.Type == "schedule_item" && Day(p.Item1, e) == d && State(e, p.Item2).Length > 0))).ToArray();
        var dayChoice = new ComboBox { ItemsSource = days.Select(d => new Choice(d.ToString("yyyy-MM-dd", null), Formats.LongDay(d) + (changedDays.Contains(d) ? " •" : ""))).ToArray(), MinWidth = 260 };
        dayChoice.SelectedIndex = days.Length == 0 ? -1 : Array.IndexOf(days, changedDays.FirstOrDefault(days[0]));
        AutomationProperties.SetName(dayChoice, s["ChooseDay"]);
        var canvas = new Canvas { Height = 24 * 48 + 40 }; var scroll = new ScrollViewer { Content = canvas };
        var legend = Ui.H(12, Ui.Pill(s["Added"], "plus", "Success.Soft", "Success"), Ui.Pill(s["Removed"], "minus", "Danger.Soft", "Danger"), Ui.Pill(s["ChangedDetails"], "pencil", "Info.Soft", "Info"));
        var toolbar = Ui.Columns("Auto,*,Auto", dayChoice, null, legend); toolbar.Margin = new Thickness(0, 10);
        void Draw()
        {
            canvas.Children.Clear(); var width = Math.Max(600, Bounds.Width - 30); canvas.Width = width; var column = (width - 60) / 2;
            void Put(Control c, double x, double y) { Canvas.SetLeft(c, x); Canvas.SetTop(c, y); canvas.Children.Add(c); }
            Put(Ui.Text(currentTitle, "h3"), 64, 4); Put(Ui.Text(otherTitle, "h3"), 64 + column, 4);
            for (var h = 0; h < 24; h++)
            {
                var y = 34 + h * 48;
                canvas.Children.Add(new Line { StartPoint = new Point(56, y), EndPoint = new Point(width, y), StrokeThickness = 1 }.Res(Shape.StrokeProperty, "Line.Soft"));
                Put(Ui.Text(Formats.Time(new LocalTime(h, 0)), "caption").Also(t => t.FontSize = 11), 6, y - 8);
            }
            if (dayChoice.SelectedItem is not Choice choice) return; var day = NodaTime.Text.LocalDatePattern.Iso.Parse(choice.Id).Value;
            foreach (var (trip, other, index) in new[] { (current, incoming, 0), (incoming, current, 1) })
                foreach (var e in trip.Entities.Values.Where(e => e.Type == "schedule_item" && e.Data["children"] is not JsonArray { Count: > 0 } && Day(trip, e) == day))
                {
                    var span = ScheduleQueries.Span(trip, e); var start = span.Start!.Value.InZone(zone); var minutes = start.Hour * 60 + start.Minute;
                    var state = State(e, other); var (icon, kind) = Visuals.For(trip, e);
                    var (stateIcon, bg, fg) = state switch { "Added" => ("plus", "Success.Soft", "Success"), "Removed" => ("minus", "Danger.Soft", "Danger"), "ChangedDetails" => ("pencil", "Info.Soft", "Info"), _ => ("", "", "") };
                    var height = Math.Max(30, Math.Min(400, (span.Length ?? Duration.FromHours(1)).TotalMinutes / 60 * 48) - 2);
                    var content = Ui.V(2, Ui.Columns("Auto,*,Auto", Ui.Icon(icon, 13, "Kind." + kind + ".Fg"), Ui.Text(Formats.Time(start.TimeOfDay), "caption").Margin(6, 0), state.Length > 0 ? Ui.Pill(s[state], stateIcon, bg, fg) : null), Ui.Line(e.Title, "title"));
                    var card = new Border { Width = column - 16, Height = height, CornerRadius = new CornerRadius(8), Padding = new Thickness(8, 5), BorderThickness = new Thickness(state.Length > 0 ? 2 : 1), Child = content, ClipToBounds = true }
                        .Res(Border.BackgroundProperty, "Kind." + kind + ".Bg").Res(Border.BorderBrushProperty, state.Length > 0 ? fg : "Kind." + kind + ".Bar");
                    if (state == "Removed") card.Opacity = 0.75;
                    AutomationProperties.SetName(card, e.Title + (state.Length > 0 ? ", " + s[state] : ""));
                    Put(card, 64 + index * column, 34 + minutes / 60d * 48 + 1);
                }
        }
        dayChoice.SelectionChanged += (_, _) => Draw(); SizeChanged += (_, _) => Draw();
        var grid = new Grid { RowDefinitions = new RowDefinitions("Auto,*") }; grid.Children.Add(toolbar); grid.Children.Add(scroll.Row(1));
        if (days.Length == 0) return Ui.EmptyState("calendar", s["NoScheduleToCompare"], "");
        return grid;
    }

    private Control Details()
    {
        var summary = ChangeSummary.Between(current, incoming);
        var list = Ui.V(10); list.Margin = new Thickness(0, 12);
        if (summary.IsEmpty) return Ui.EmptyState("check", s["PlansIdentical"], s["PlansIdenticalBody"]);
        foreach (var change in summary.Entities)
        {
            var a = current.Find(change.Entity); var b = incoming.Find(change.Entity);
            var (icon, bg, fg, label) = change.Kind switch { ChangeKind.Added => ("plus", "Success.Soft", "Success", "OnlyIn"), ChangeKind.Removed => ("minus", "Danger.Soft", "Danger", "OnlyIn"), _ => ("pencil", "Info.Soft", "Info", "ChangedDetails") };
            var heading = Ui.Columns("Auto,*,Auto", Ui.Icon(Visuals.EntityIcon(change.Type), 16, "Text.Muted"), Ui.Text(change.Type == "trip" ? s["TripDetails"] : change.Title, "title").Margin(8, 0), Ui.Pill(change.Kind == ChangeKind.Updated ? s[label] : string.Format(s[label], change.Kind == ChangeKind.Added ? otherTitle : currentTitle), icon, bg, fg));
            var rows = Ui.V(6);
            if (change.Kind == ChangeKind.Updated && a is not null && b is not null)
                foreach (var field in change.Fields.Where(f => f != "variant"))
                {
                    var path = field.Split('/'); JsonNode? Get(Entity e) { JsonNode? n = e.Data; foreach (var p in path) n = n?[p]; return n; }
                    var left = model?.DescribeValue(Get(a), current) ?? Get(a)?.ToJsonString() ?? "—"; var right = model?.DescribeValue(Get(b), incoming) ?? Get(b)?.ToJsonString() ?? "—";
                    rows.Children.Add(Ui.Columns("140,*,*", Ui.Text(model?.FieldLabel(field) ?? field, "label"), Ui.Text(left, "muted").Margin(8, 0), Ui.Text(right).Margin(8, 0)));
                }
            list.Children.Add(Ui.Card(Ui.V(8, heading, rows.Children.Count > 0 ? Ui.V(6, Ui.Columns("140,*,*", null, Ui.Text(currentTitle, "overline").Margin(8, 0), Ui.Text(otherTitle, "overline").Margin(8, 0)), rows) : null), 14));
        }
        foreach (var resource in summary.Resources) list.Children.Add(Ui.Card(Ui.H(8, Ui.Icon("file", 16, "Text.Muted"), Ui.Text(System.IO.Path.GetFileName(resource.Path))), 12));
        return new ScrollViewer { Content = list };
    }
}
