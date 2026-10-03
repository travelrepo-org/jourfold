using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using NodaTime;
using TravelRepo.Core;
using TravelRepo.Merge;
namespace Jourfold.Desktop;

public sealed class ComparisonView : TabControl
{
    public ComparisonView(TripSnapshot current, TripSnapshot incoming, Localization strings)
    {
        var details = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), RowDefinitions = new RowDefinitions("Auto,*") };
        var left = new TextBlock { Text = strings["UseCurrent"], FontSize = 20, FontWeight = FontWeight.Bold, Margin = new Thickness(12) }; var right = new TextBlock { Text = strings["UseVariant"], FontSize = 20, FontWeight = FontWeight.Bold, Margin = new Thickness(12) }; Grid.SetColumn(right, 1); details.Children.Add(left); details.Children.Add(right);
        var changes = SemanticMerge.Diff(current, incoming);
        var ids = changes.Where(c => c.Entity != Guid.Empty && !c.Path.StartsWith("/variant", StringComparison.Ordinal)).Select(c => c.Entity).Distinct().OrderBy(id => (current.Find(id) ?? incoming.Find(id))?.Title).ToArray();
        var resources = changes.Where(c => c.Entity == Guid.Empty).Select(c => c.Path[11..]).ToArray();
        var a = Column(current, incoming, ids, resources, strings); var b = Column(incoming, current, ids, resources, strings); Grid.SetRow(a, 1); Grid.SetRow(b, 1); Grid.SetColumn(b, 1); details.Children.Add(a); details.Children.Add(b);
        ItemsSource = new[] { new TabItem { Header = strings["ScheduleComparison"], Content = Timetable(current, incoming, strings) }, new TabItem { Header = strings["DetailsComparison"], Content = details } };
    }
    private static Control Timetable(TripSnapshot current, TripSnapshot incoming, Localization strings)
    {
        var zone = DateTimeZoneProviders.Tzdb.GetZoneOrNull(current.Manifest.Data["default_timezone"]?.ToString() ?? "Etc/UTC") ?? DateTimeZone.Utc;
        LocalDate? Date(TripSnapshot trip, Entity e) => TripQueries.Range(trip, e).Start?.InZone(zone).Date ?? (e.Data["time"]?["date"] is { } d && NodaTime.Text.LocalDatePattern.Iso.Parse(d.ToString()).TryGetValue(default, out var day) ? day : null);
        var days = new[] { current, incoming }.SelectMany(t => t.Entities.Values.Where(e => e.Type == "schedule_item").Select(e => Date(t, e))).Where(d => d is not null).Select(d => d!.Value).Distinct().Order().ToArray();
        var dayChoice = new ComboBox { ItemsSource = days, SelectedIndex = days.Length > 0 ? 0 : -1, Margin = new Thickness(12) }; AutomationProperties.SetName(dayChoice, strings["ChooseDay"]);
        var canvas = new Canvas { Width = 980, Height = 24 * 54 + 55 }; var grid = new Grid { RowDefinitions = new RowDefinitions("Auto,*") }; var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Children = { dayChoice, new TextBlock { Text = zone.Id, VerticalAlignment = VerticalAlignment.Center } } }; grid.Children.Add(toolbar); var scroll = new ScrollViewer { Content = canvas, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto }; Grid.SetRow(scroll, 1); grid.Children.Add(scroll);
        void Text(string text, double x, double y, double width) { var block = new TextBlock { Text = text, Width = width, TextWrapping = TextWrapping.Wrap }; Canvas.SetLeft(block, x); Canvas.SetTop(block, y); canvas.Children.Add(block); }
        void Draw()
        {
            canvas.Children.Clear(); Text(strings["UseCurrent"], 65, 10, 430); Text(strings["UseVariant"], 530, 10, 430);
            for (var h = 0; h < 24; h++) { Text(h.ToString("00") + ":00", 5, h * 54 + 45, 55); var line = new Avalonia.Controls.Shapes.Line { StartPoint = new Point(60, h * 54 + 45), EndPoint = new Point(970, h * 54 + 45), Stroke = Brushes.Gray, Opacity = .3 }; canvas.Children.Add(line); }
            if (dayChoice.SelectedItem is not LocalDate day) return;
            foreach (var (trip, other, column) in new[] { (current, incoming, 0), (incoming, current, 1) })
                foreach (var entity in trip.Entities.Values.Where(e => e.Type == "schedule_item" && Date(trip, e) == day))
                {
                    var range = TripQueries.Range(trip, entity); var time = range.Start?.InZone(zone).TimeOfDay ?? new LocalTime(12, 0); var minute = time.Hour * 60 + time.Minute;
                    var state = other.Find(entity.Id) is null ? column == 0 ? "Removed" : "Added" : SemanticMerge.Diff(new(trip.Manifest, new Dictionary<Guid, Entity> { [entity.Id] = entity }), new(trip.Manifest, new Dictionary<Guid, Entity> { [entity.Id] = other.Find(entity.Id)! })).Count > 0 ? "ChangedDetails" : "";
                    var text = entity.Title + "\n" + time.ToString("HH:mm", null) + (state.Length > 0 ? " · " + strings[state] : "");
                    var card = new Border { Width = 440, Height = Math.Max(44, Math.Min(240, range.End is { } end && range.Start is { } start ? (end - start).TotalMinutes / 60 * 54 : 54)), Background = new SolidColorBrush(Color.Parse("#D7EEF0")), BorderBrush = Brushes.Teal, BorderThickness = new Thickness(state.Length > 0 ? 2 : 1), CornerRadius = new CornerRadius(5), Padding = new Thickness(8), Child = new TextBlock { Text = text, Foreground = Brushes.Black, TextWrapping = TextWrapping.Wrap } };
                    AutomationProperties.SetName(card, text); Canvas.SetLeft(card, 65 + column * 465); Canvas.SetTop(card, minute / 60d * 54 + 45); canvas.Children.Add(card);
                }
        }
        dayChoice.SelectionChanged += (_, _) => Draw(); Draw(); return grid;
    }
    private static ScrollViewer Column(TripSnapshot snapshot, TripSnapshot other, Guid[] ids, string[] resources, Localization strings)
    {
        var panel = new StackPanel { Spacing = 12, Margin = new Thickness(12) };
        foreach (var id in ids)
        {
            var e = snapshot.Find(id); var details = new StackPanel { Spacing = 5, MinHeight = 110 };
            if (e is not null)
            {
                details.Children.Add(new TextBlock { Text = e.Title, FontSize = 18, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap });
                foreach (var field in EditorModel.Fields(e))
                {
                    var value = EditorModel.Get(e, field.Path); if (value is null) continue;
                    var text = value is System.Text.Json.Nodes.JsonArray array ? string.Join(", ", array.Select(n => Guid.TryParse(n?.ToString(), out var refId) ? snapshot.Find(refId)?.Title : n?.ToString())) : value.ToString();
                    details.Children.Add(new TextBlock { Text = strings[field.Label] + ": " + (field.Kind == "choice" ? strings[text] : text), TextWrapping = TextWrapping.Wrap });
                }
                if (e.Type == "document") details.Children.Add(new TextBlock { Text = strings["Files"] + ": " + (System.Text.Json.Nodes.JsonNode.DeepEquals(e.Data["blob"], other.Find(id)?.Data["blob"]) ? "=" : "≠") });
            }
            else details.Children.Add(new TextBlock { Text = strings["Removed"], FontSize = 20 });
            panel.Children.Add(new Border { Classes = { "panel" }, Padding = new Thickness(12), Child = details });
        }
        foreach (var path in resources)
        {
            panel.Children.Add(new TextBlock { Text = path.StartsWith("assets/", StringComparison.Ordinal) ? strings["Files"] : Path.GetFileName(path), FontWeight = FontWeight.SemiBold });
            if (!snapshot.Resources.TryGetValue(path, out var bytes)) panel.Children.Add(new TextBlock { Text = strings["Removed"] });
            else if (path.EndsWith(".md", StringComparison.Ordinal)) panel.Children.Add(new MarkdownView(System.Text.Encoding.UTF8.GetString(bytes)));
            else panel.Children.Add(new TextBlock { Text = bytes.Length.ToString("N0") + " B" });
        }
        return new ScrollViewer { Content = panel };
    }
}
