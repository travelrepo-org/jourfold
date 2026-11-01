using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Jourfold.Application;
using Jourfold.Mobile.Controls;
using NodaTime;
using TravelRepo.Core;

namespace Jourfold.Mobile.Views;

/// <summary>
/// The map of a day, drawn like the desktop's: the day's places as numbered pins in the order of the plan and a line
/// through them, journeys as arcs from start to end. It works without a network on a plain grid; street tiles from
/// OpenStreetMap come only after the user turns on online maps, with the credit the licence asks for. A sheet below
/// lists the day's stops; dragged down it leaves only its handle, so the map has the whole screen. A tap on a pin or a
/// stop selects it and shows it first, with Navigate and the way to its details. The control stays while the trip is
/// open, so the map keeps its position and tiles when the page is drawn again.
/// </summary>
public sealed class MapPage : UserControl
{
    private readonly MobileSession s;
    private readonly Workspace workspace;
    private MapSurface surface = null!;
    private bool online, built;
    private readonly Canvas markers = new() { ClipToBounds = true };
    private readonly Panel overlay = new();
    private readonly Grid layers = new();
    private Sheet? sheet;
    private bool sideSheet;
    private WidthClass width;
    private LocalDate? fitted;
    private Stop[] stops = [];

    /// <summary>One line of the day with where it is; journeys also know where they start.</summary>
    private sealed record Stop(int Number, AgendaEntry Entry, (double Lat, double Lon)? At, (double Lat, double Lon)? From);

    public MapPage(MobileSession session)
    {
        s = session; workspace = session.Workspace!;
        Content = layers;
        AutomationProperties.SetName(this, s.Strings["Map"]);
        SizeChanged += (_, e) => { if (fitted is null && e.NewSize.Width > 0) Fit(); else Place(); };
    }

    /// <summary>Whether this map belongs to the open trip; a newly opened trip gets a new one.</summary>
    public bool Shows(Workspace? open) => ReferenceEquals(open, workspace);

    /// <summary>Brings the map in step with the session: the day, the selection, online maps and the screen width.</summary>
    public void Update(WidthClass width)
    {
        this.width = width;
        if (!built || online != s.Settings.OnlineMaps) Build();
        var day = s.Agenda.Find(s.SelectedDate) ?? s.Agenda.Days.FirstOrDefault();
        stops = Stops(day);
        surface.Path = stops.SelectMany(x => new[] { x.From, x.At }).OfType<(double Lat, double Lon)>().ToArray();
        surface.Routes = stops.Where(x => x.From is not null && x.At is not null).Select(x => (x.From!.Value, x.At!.Value, x.Entry.Status is "confirmed" or "completed", x.Entry.Item.Id == s.SelectedItem)).ToArray();
        var side = width != WidthClass.Phone;
        var newSheet = sheet is null || sideSheet != side;
        sideSheet = side;
        var header = Header(day); var list = List(day);
        if (newSheet)
        {
            if (sheet is not null) layers.Children.Remove(sheet);
            sheet = new Sheet(header, list, [0.1, 0.42, 0.88], 1, side, s.Strings["Map"], docked: true);
            layers.Children.Add(sheet);
        }
        else sheet?.Replace(header, list);
        BuildOverlay();
        if (day?.Date != fitted && Bounds.Width > 0) Fit();
        else if (s.SelectedItem is { } chosen && stops.FirstOrDefault(x => x.Entry.Item.Id == chosen)?.At is { } at) { surface.CenterOn(at); Shift(); }
        Place();
    }

    private void Build()
    {
        online = s.Settings.OnlineMaps;
        var tiles = online ? new MapTiles(s.Host.DataRoot, s.MapsHttp, s.MapProvider) : null;
        if (online) _ = MapProviders.RefreshAsync(s.Settings, s.MapsHttp);
        var previous = built ? surface : null;
        surface = new MapSurface(tiles); built = true;
        surface.Changed += Place;
        layers.Children.Clear();
        layers.Children.Add(surface); layers.Children.Add(markers); layers.Children.Add(overlay);
        if (sheet is not null) layers.Children.Add(sheet);
        if (previous is not null) { var (x, y, zoom) = previous.State; surface.Restore(x, y, zoom); }
    }

    /// <summary>The day's lines in plan order that have a place, numbered as in the list, steps of groups on their own.</summary>
    private Stop[] Stops(AgendaDay? day)
    {
        if (day is null) return [];
        var trip = s.Trip!; var result = new List<Stop>();
        foreach (var entry in day.Entries.Where(e => e.Role != AgendaRole.Continued).SelectMany(e => e.IsGroup ? e.Steps : [e]))
        {
            if (Navigation.Target(trip, entry.Item) is not { } target) continue;
            (double, double)? At((double? Lat, double? Lon) p) => p.Lat is { } a && p.Lon is { } b && a is >= -85 and <= 85 ? (a, b) : null;
            var from = ScheduleQueries.Route(trip, entry.Item).From is { } start && Places.Location(start) is { } f ? At((f.Latitude, f.Longitude)) : null;
            result.Add(new(result.Count + 1, entry, At((target.Lat, target.Lon)), entry.Role == AgendaRole.Item ? from : null));
        }
        return result.ToArray();
    }

    private void Fit()
    {
        var day = s.Agenda.Find(s.SelectedDate) ?? s.Agenda.Days.FirstOrDefault();
        fitted = day?.Date;
        var points = stops.SelectMany(x => new[] { x.At, x.From }).OfType<(double Lat, double Lon)>().ToArray();
        // A day without positions shows the whole trip's places, or the world.
        if (points.Length == 0) points = s.Trip!.Entities.Values.Where(e => e.Type == "place").Select(Places.Location).OfType<(double Latitude, double Longitude)>().Select(p => (p.Latitude, p.Longitude)).ToArray();
        var visible = Visible();
        surface.Fit(points, visible.Size);
        if (points.Length > 0) Shift();
        Place();
    }

    /// <summary>The part of the map that the date strip and the sheet leave free: above the sheet on phones, left of it on tablets.</summary>
    private Rect Visible()
    {
        const double strip = 72;
        return sideSheet ? new Rect(0, strip, Math.Max(200, Bounds.Width - 400), Math.Max(200, Bounds.Height - strip))
            : new Rect(0, strip, Bounds.Width, Math.Max(160, Bounds.Height * 0.58 - strip));
    }

    /// <summary>Moves what is in the middle of the map into the middle of the free part.</summary>
    private void Shift()
    {
        var visible = Visible();
        surface.Pan(Bounds.Width / 2 - visible.Center.X, Bounds.Height / 2 - visible.Center.Y);
    }

    /// <summary>The numbered pins, drawn again whenever the map moves.</summary>
    private void Place()
    {
        markers.Children.Clear();
        if (!built) return;
        // Stops at the same place, such as checking in and exploring from the hotel, share one pin: "1·2".
        var groups = stops.Where(x => x.At is not null).GroupBy(x => (Math.Round(x.At!.Value.Lat, 5), Math.Round(x.At!.Value.Lon, 5))).Select(g => g.ToArray());
        foreach (var group in groups.OrderBy(g => g.Any(x => x.Entry.Item.Id == s.SelectedItem)))
        {
            var point = surface.ToScreen(group[0].At!.Value);
            if (point.X < -40 || point.Y < -40 || point.X > Bounds.Width + 40 || point.Y > Bounds.Height + 40) continue;
            var chosen = group.FirstOrDefault(x => x.Entry.Item.Id == s.SelectedItem);
            var shown = chosen ?? group[0];
            var numbers = string.Join("·", group.Select(x => x.Number.ToString(CultureInfo.InvariantCulture)));
            var size = chosen is not null ? 38 : 30;
            var key = "Kind." + Visuals.KindKey(shown.Entry.Kind);
            var pin = new Border
            {
                MinWidth = size,
                Height = size,
                CornerRadius = new CornerRadius(size / 2),
                BorderThickness = new Thickness(chosen is not null ? 3 : 2),
                Padding = new Thickness(group.Length > 1 ? 8 : 0, 0),
                Child = Ui.Text(numbers, "strong").Also(t => { t.Foreground = Brushes.White; t.HorizontalAlignment = HorizontalAlignment.Center; t.VerticalAlignment = VerticalAlignment.Center; }),
                BoxShadow = BoxShadows.Parse("0 2 6 0 #400F2A3D")
            }.Res(Border.BackgroundProperty, key + ".Bar").Res(Border.BorderBrushProperty, chosen is not null ? "Accent" : "Bg.Surface");
            var content = Ui.V(3, pin.Also(p => p.HorizontalAlignment = HorizontalAlignment.Center));
            if (chosen is not null) content.Children.Add(new Border { Child = Ui.Text(chosen.Entry.Item.Title, "caption").Also(t => { t.FontWeight = FontWeight.SemiBold; t.TextWrapping = TextWrapping.NoWrap; }), Padding = new Thickness(6, 1), CornerRadius = new CornerRadius(6), HorizontalAlignment = HorizontalAlignment.Center }.Res(Border.BackgroundProperty, "Bg.Surface"));
            var button = new Button { Content = content, Padding = new Thickness(0), Background = Brushes.Transparent, BorderThickness = new Thickness(0), MinHeight = 0, Width = 160 };
            AutomationProperties.SetName(button, string.Join(", ", group.Select(x => x.Number.ToString(CultureInfo.InvariantCulture) + ". " + x.Entry.Item.Title)));
            // A tap selects the first stop of the pin, the next tap the next one.
            var next = group[(Array.IndexOf(group, chosen) + 1) % group.Length].Entry.Item.Id;
            button.Click += (_, _) => { s.Select(next); sheet?.Show(1); };
            Canvas.SetLeft(button, point.X - 80); Canvas.SetTop(button, point.Y - size / 2.0); button.ZIndex = chosen is not null ? 2 : 1;
            markers.Children.Add(button);
        }
    }

    /// <summary>Zoom and fit on the side, the street map offer or its credit, and a way to the places without a position.</summary>
    private void BuildOverlay()
    {
        var str = s.Strings; overlay.Children.Clear();
        var zoomIn = Ui.IconButton("plus", str["ZoomIn"]); zoomIn.Click += (_, _) => surface.ZoomAt(1, new Point(Bounds.Width / 2, Bounds.Height / 2));
        var zoomOut = Ui.IconButton("minus", str["ZoomOut"]); zoomOut.Click += (_, _) => surface.ZoomAt(-1, new Point(Bounds.Width / 2, Bounds.Height / 2));
        var fit = Ui.IconButton("maximize-2", str["FitMap"]); fit.Click += (_, _) => Fit();
        var layersButton = Ui.IconButton(online ? "globe" : "layers", str["StreetMap"]); layersButton.Click += (_, _) => s.SetOnlineMaps(!online);
        // On tablets the sheet is a column on the right; the controls stay left of it.
        overlay.Margin = new Thickness(0, 0, sideSheet ? 400 : 0, 0);
        overlay.Children.Add(new Border { Child = Ui.V(2, zoomIn, zoomOut, fit, layersButton), Padding = new Thickness(4), CornerRadius = new CornerRadius(12), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 76, 12, 0) }
            .Classed("raised").Res(Border.BackgroundProperty, "Bg.Surface"));
        var strip = new Border { Child = PlanView.DateStrip(s), Padding = new Thickness(0, 6), VerticalAlignment = VerticalAlignment.Top }.Res(Border.BackgroundProperty, "Bg.App");
        overlay.Children.Add(strip);
        if (online)
        {
            var provider = s.MapProvider;
            var credit = Ui.Button(provider.Attribution, null, "link"); credit.FontSize = 11;
            credit.Click += async (_, _) => { if (TopLevel.GetTopLevel(credit)?.Launcher is { } launcher) await launcher.LaunchUriAsync(new Uri(provider.AttributionUrl)); };
            overlay.Children.Add(new Border { Child = credit, Padding = new Thickness(8, 2), CornerRadius = new CornerRadius(6), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 76, 64, 0) }
                .Res(Border.BackgroundProperty, "Bg.Surface"));
        }
    }

    /// <summary>The day in the sheet's header, with how many of its stops are on the map.</summary>
    private Control Header(AgendaDay? day)
    {
        var str = s.Strings;
        if (day is null) return Ui.Text(str["NothingPlanned"], "muted").Margin(16, 0, 16, 10);
        var missing = stops.Count(x => x.At is null);
        var caption = string.Format(CultureInfo.CurrentCulture, str["DayN"], day.Number) + (missing > 0 ? " · " + string.Format(CultureInfo.CurrentCulture, str["StopsWithoutPosition"], missing) : "");
        return Ui.V(2, Ui.Text(Lines.DayTitle(day.Date, s), "h3"), Ui.Text(caption, "caption")).Margin(16, sideSheet ? 16 : 0, 16, 10);
    }

    /// <summary>The selected stop first with Navigate and Details, then all the day's stops in order.</summary>
    private Control List(AgendaDay? day)
    {
        var str = s.Strings; var trip = s.Trip!;
        var page = Ui.V(10);
        if (stops.FirstOrDefault(x => x.Entry.Item.Id == s.SelectedItem) is { } chosen)
        {
            var details = Ui.Button(str["Details"], "info", "soft"); details.Click += (_, _) => s.ShowDetails(chosen.Entry.Item.Id);
            var time = TimeText.Local(chosen.Entry, s.PhoneZone, str, trip).Primary;
            page.Children.Add(Ui.Card(Ui.V(10,
                Ui.Columns("Auto,*", Badge(chosen).Margin(0, 0, 12, 0), Ui.V(1, Ui.Text(chosen.Entry.Item.Title, "strong"), Ui.Text(string.Join(" · ", new[] { time, Lines.Where(trip, chosen.Entry.Item) }.Where(t => t.Length > 0)), "caption"))),
                Ui.H(8, Navigation.Button(s, chosen.Entry.Item), details)), 14).Also(c => c.BorderThickness = new Thickness(1)).Res(Border.BorderBrushProperty, "Accent"));
        }
        // The offer of street maps sits in the list, so it covers no part of the map.
        if (!online && s.Settings.Values.GetValueOrDefault("map.prompt") != "dismissed")
        {
            var enable = Ui.Button(str["ShowStreetMap"], "globe", "primary"); enable.Click += (_, _) => s.SetOnlineMaps(true);
            var later = Ui.Button(str["NotNow"], null, "ghost"); later.Click += (_, _) => { s.Settings.Values["map.prompt"] = "dismissed"; s.Settings.Save(); Update(width); };
            page.Children.Add(Ui.Card(Ui.V(10, Ui.Text(str["StreetMapTitle"], "strong"), Ui.Text(str["OnlineSearchBody"], "caption").Also(t => t.TextWrapping = TextWrapping.Wrap), Ui.H(8, enable, later)), 14));
        }
        foreach (var stop in stops)
        {
            var time = TimeText.Local(stop.Entry, s.PhoneZone, str, trip).Primary;
            var detail = string.Join(" · ", new[] { time, Lines.Where(trip, stop.Entry.Item), stop.At is null ? str["NoPositionYet"] : "" }.Where(t => t.Length > 0));
            var row = new Button { Content = Ui.Columns("Auto,*", Badge(stop).Margin(0, 0, 12, 0), Ui.V(1, Ui.Text(stop.Entry.Item.Title, "strong"), Ui.Text(detail, "caption"))) }.Classed("row");
            AutomationProperties.SetName(row, stop.Number.ToString(CultureInfo.InvariantCulture) + ". " + stop.Entry.Item.Title);
            var id = stop.Entry.Item.Id;
            // A stop without a position opens its details, where its place can get one.
            row.Click += (_, _) => { if (stop.At is null) s.ShowDetails(id); else if (s.SelectedItem != id) s.Select(id); };
            page.Children.Add(row);
        }
        if (stops.Length == 0 && day is not null) page.Children.Add(Ui.Text(str["NoPlacesThisDay"], "muted"));
        if (stops.Any(x => x.At is null))
        {
            var places = Ui.Button(str["Places"], "map-pin", "ghost", "compact"); places.Click += (_, _) => s.OpenPage("places");
            page.Children.Add(Ui.V(4, Ui.Text(str["PlacesWithoutPosition"], "caption").Also(t => t.TextWrapping = TextWrapping.Wrap), places.Also(b => b.HorizontalAlignment = HorizontalAlignment.Left)));
        }
        return page.Margin(16, 0, 16, 24);
    }

    private static Control Badge(Stop stop) =>
        new Border
        {
            Width = 30,
            Height = 30,
            CornerRadius = new CornerRadius(15),
            Opacity = stop.At is null ? 0.45 : 1,
            Child = Ui.Text(stop.Number.ToString(CultureInfo.InvariantCulture), "strong").Also(t => { t.HorizontalAlignment = HorizontalAlignment.Center; t.VerticalAlignment = VerticalAlignment.Center; t.Foreground = Brushes.White; })
        }.Res(Border.BackgroundProperty, "Kind." + Visuals.KindKey(stop.Entry.Kind) + ".Bar");
}
