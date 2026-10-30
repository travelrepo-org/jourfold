using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Jourfold.Infrastructure;
using TravelRepo.Core;
using Jourfold.Application;

namespace Jourfold.Desktop;

/// <summary>
/// Trip map in Web Mercator. Places and transport routes come from canonical coordinates; the street
/// basemap is optional (OpenStreetMap, opt-in). Without it the map keeps working offline on a plain grid.
/// </summary>
public sealed class MapView : UserControl
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };
    private readonly MapProvider provider;
    private static (double X, double Y, int Zoom)? remembered;
    private readonly MainWindow window;
    private readonly MainViewModel vm;
    private readonly MapSurface surface;
    private readonly Canvas markers = new() { ClipToBounds = true };
    private readonly Panel overlay = new();
    private readonly Entity[] places;
    private readonly (Entity Item, Entity From, Entity To)[] routes;

    public MapView(MainWindow owner)
    {
        window = owner; vm = owner.Model; var trip = vm.Trip!; var s = vm.Strings;
        places = trip.Entities.Values.Where(e => e.Type == "place" && Coordinates(e) is not null).ToArray();
        routes = trip.Entities.Values.Where(e => e.Type == "schedule_item").Select(e => (e, ScheduleQueries.Route(trip, e))).Where(x => x.Item2.From is not null && x.Item2.To is not null && Coordinates(x.Item2.From) is not null && Coordinates(x.Item2.To) is not null).Select(x => (x.e, x.Item2.From!, x.Item2.To!)).ToArray();
        provider = MapProviders.Current(vm.Store);
        surface = new MapSurface(vm.OnlineMaps ? new MapTiles(vm.Store.Root, Http, provider) : null);
        if (vm.OnlineMaps)
        {
            // Check the provider list (at most daily) and keep the cache within its limit, off the UI thread.
            _ = Task.Run(async () => { await MapProviders.RefreshAsync(vm.Store, Http); MapTiles.Prune(vm.Store.Root); });
        }
        SetRoutes();
        surface.Changed += Layout;
        AutomationProperties.SetName(this, s["Map"]);
        var root = new Panel { Children = { surface, markers, overlay } };
        Content = root;
        BuildOverlay();
        var placedOnce = false;
        DetachedFromVisualTree += (_, _) => { if (placedOnce) remembered = surface.State; };
        SizeChanged += (_, e) =>
        {
            if (!placedOnce && e.NewSize.Width > 0 && e.NewSize.Height > 0)
            {
                placedOnce = true;
                // Keep the area the person looked at last. Rebuilding the view, for example after a sync, must not move the map.
                if (remembered is { } r && places.Length > 0) { surface.Restore(r.X, r.Y, r.Zoom); Reveal(e.NewSize); } else Fit(e.NewSize);
            }
            Layout();
        };
    }

    private bool Highlighted(Entity item, Entity from, Entity to) => vm.Selected is { } s && (s.Id == item.Id || s.Id == from.Id || s.Id == to.Id);
    private void SetRoutes() => surface.Routes = routes.Select(r => (Coordinates(r.From)!.Value, Coordinates(r.To)!.Value, r.Item.Data["status"]?.ToString() is "confirmed" or "completed", Highlighted(r.Item, r.From, r.To))).ToArray();

    /// <summary>Shows the new selection without rebuilding the map or changing the visible area, unless the selected place is off screen.</summary>
    public void UpdateSelection() { SetRoutes(); Reveal(Bounds.Size); surface.Redraw(); }

    /// <summary>Moves the map so a selected place that is outside the visible area comes into view. The zoom stays.</summary>
    private void Reveal(Size size)
    {
        if (vm.Selected is not { Type: "place" } selected || Coordinates(selected) is not { } focus || size.Width <= 0) return;
        var point = surface.ToScreen(focus);
        if (point.X < 40 || point.Y < 40 || point.X > size.Width - 40 || point.Y > size.Height - 40) surface.CenterOn(focus);
    }

    /// <summary>How map labels are shown: <c>auto</c> (only when they fit), <c>always</c> or <c>off</c>. A local setting.</summary>
    public static string LabelMode(MainViewModel vm) => vm.Store.Get("map.labels") switch { "always" => "always", "off" => "off", _ => "auto" };

    /// <summary>A place's position, within the latitudes the Web Mercator map can show.</summary>
    public static (double Lat, double Lon)? Coordinates(Entity place) => Places.Location(place) is { Latitude: >= -85 and <= 85 } p ? (p.Latitude, p.Longitude) : null;

    private void Fit(Size? size = null)
    {
        // Focus on where the trip happens: stays and activity places. Far-away departure airports stay reachable by zooming out.
        var trip = vm.Trip!;
        var local = trip.Entities.Values.Where(e => e.Type == "schedule_item" && e.Data["components"]?["transport"] is null).Select(e => ScheduleQueries.PrimaryPlace(trip, e)).OfType<Entity>().Where(p => Coordinates(p) is not null).DistinctBy(p => p.Id).ToArray();
        var points = (local.Length >= 2 ? local : places).Select(p => Coordinates(p)!.Value).ToArray();
        if (vm.Selected is { Type: "place" } selected && Coordinates(selected) is { } focus) points = [focus];
        surface.Fit(points, size ?? Bounds.Size);
    }

    private void BuildOverlay()
    {
        var s = vm.Strings; overlay.Children.Clear();
        var zoomIn = Ui.IconButton("plus", s["ZoomIn"]); zoomIn.Click += (_, _) => surface.ZoomAt(1, new Point(Bounds.Width / 2, Bounds.Height / 2));
        var zoomOut = Ui.IconButton("minus", s["ZoomOut"]); zoomOut.Click += (_, _) => surface.ZoomAt(-1, new Point(Bounds.Width / 2, Bounds.Height / 2));
        var fit = Ui.IconButton("maximize-2", s["FitMap"]); fit.Click += (_, _) => { remembered = null; Fit(); };
        var layers = Ui.IconButton("layers", s["MapLayers"]);
        var menu = new MenuFlyout();
        var street = new MenuItem { Header = s["StreetMap"], Icon = Ui.Icon(vm.OnlineMaps ? "check" : "globe", 15) }; street.Click += (_, _) => vm.SetOnlineMaps(!vm.OnlineMaps); menu.Items.Add(street);
        menu.Items.Add(new Separator());
        menu.Items.Add(new MenuItem { Header = s["MapLabels"], IsEnabled = false });
        foreach (var (mode, label) in new[] { ("auto", "MapLabelsAuto"), ("always", "MapLabelsAlways"), ("off", "MapLabelsOff") })
        {
            var item = new MenuItem { Header = s[label], ToggleType = MenuItemToggleType.Radio, IsChecked = LabelMode(vm) == mode, GroupName = "map-labels" };
            item.Click += (_, _) => { vm.Store.Set("map.labels", mode); BuildOverlay(); Layout(); };
            menu.Items.Add(item);
        }
        layers.Flyout = menu;
        var controls = new Border { Child = Ui.V(2, zoomIn, zoomOut, fit, layers), Padding = new Thickness(4), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(16) }.Classed("raised");
        overlay.Children.Add(controls);
        if (vm.OnlineMaps)
        {
            var attribution = Ui.Button(provider.Attribution, null, "link"); attribution.FontSize = 11; attribution.Click += (_, _) => vm.Interaction.Open(provider.AttributionUrl);
            overlay.Children.Add(new Border { Child = attribution, Padding = new Thickness(8, 3), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, CornerRadius = new CornerRadius(6, 0, 0, 0) }.Res(Border.BackgroundProperty, "Bg.Surface"));
        }
        else if (!vm.MapPromptDismissed)
        {
            var enable = Ui.Button(s["ShowStreetMap"], "globe", "primary"); enable.Click += (_, _) => vm.SetOnlineMaps(true);
            var later = Ui.Button(s["NotNow"], null, "ghost"); later.Click += (_, _) => { vm.MapPromptDismissed = true; window.RenderMain(); };
            var card = Ui.Card(Ui.V(10, Ui.Columns("Auto,*", Ui.Tile("map", "Transport", 34), Ui.Text(s["StreetMapTitle"], "h3").Margin(12, 0, 0, 0)), Ui.Text(s["StreetMapBody"], "muted"), Ui.H(8, enable, later)), 18);
            card.Width = 380; card.HorizontalAlignment = HorizontalAlignment.Left; card.VerticalAlignment = VerticalAlignment.Top; card.Margin = new Thickness(16); card.Classes.Add("raised");
            overlay.Children.Add(card);
        }
        var missing = vm.Trip!.Entities.Values.Count(e => e.Type == "place" && Coordinates(e) is null);
        if (places.Length == 0)
        {
            var add = Ui.Button(s["AddPlace"], "map-pin", "primary"); add.Click += (_, _) => _ = vm.AddAsync("place");
            overlay.Children.Add(Ui.Card(Ui.EmptyState("map-pin", s["MapEmptyTitle"], vm.OnlineMaps ? s["MapEmptyBodyOnline"] : s["MapEmptyBody"], add), 0).Also(c => { c.HorizontalAlignment = HorizontalAlignment.Center; c.VerticalAlignment = VerticalAlignment.Center; c.Classes.Add("raised"); }));
        }
        else if (missing > 0)
        {
            var link = Ui.Button(string.Format(s["PlacesWithoutLocation"], missing), "map-pin", "ghost", "compact"); link.Click += (_, _) => vm.Navigate("Places");
            overlay.Children.Add(new Border { Child = link, Padding = new Thickness(4), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(16) }.Classed("raised"));
        }
    }

    private void Layout()
    {
        markers.Children.Clear(); var trip = vm.Trip; if (trip is null) return;
        var mode = LabelMode(vm);
        var showLabels = mode == "always" || mode == "auto" && (places.Length <= 14 || surface.Zoom >= 12);
        foreach (var place in places.OrderBy(p => vm.Selected?.Id == p.Id))
        {
            var point = surface.ToScreen(Coordinates(place)!.Value); if (point.X < -40 || point.Y < -40 || point.X > Bounds.Width + 40 || point.Y > Bounds.Height + 40) continue;
            var users = TravelRepo.Core.TripSummaries.ReferencesTo(trip, place.Id).Where(e => e.Type == "schedule_item").ToArray();
            var (icon, kind) = users.Length > 0 ? Visuals.For(trip, users[0]) : ("map-pin", "Sightseeing");
            var selected = vm.Selected is { } sel && (sel.Id == place.Id || users.Any(u => u.Id == sel.Id));
            var size = selected ? 36 : 30;
            var pin = new Border { Width = size, Height = size, CornerRadius = new CornerRadius(size / 2), BorderThickness = new Thickness(selected ? 3 : 2), Child = Ui.Icon(icon, size * 0.5, "Kind." + kind + ".Fg").Also(i => i.HorizontalAlignment = HorizontalAlignment.Center), BoxShadow = BoxShadows.Parse("0 2 6 0 #400F2A3D") }
                .Res(Border.BackgroundProperty, "Kind." + kind + ".Bg").Res(Border.BorderBrushProperty, selected ? "Accent" : "Bg.Surface");
            var content = Ui.V(3, pin);
            if (showLabels || selected) content.Children.Add(new Border { Child = Ui.Text(place.Title, "caption").Also(t => { t.FontWeight = FontWeight.SemiBold; t.Res(TextBlock.ForegroundProperty, "Text"); t.TextWrapping = TextWrapping.NoWrap; }), Padding = new Thickness(6, 1), CornerRadius = new CornerRadius(6), HorizontalAlignment = HorizontalAlignment.Center }.Res(Border.BackgroundProperty, "Bg.Surface"));
            var button = new Button { Content = content, Padding = new Thickness(0), Background = Brushes.Transparent, BorderThickness = new Thickness(0), MinHeight = 0 }.Classed("block").Named(place.Title);
            button.Click += (_, _) => vm.Selected = vm.Workspace?.State.Trip.Find(place.Id);
            button.ContextMenu = EntityMenu.Context(window, place);
            Canvas.SetLeft(button, point.X - 80); Canvas.SetTop(button, point.Y - size / 2.0); button.Width = 160; content.HorizontalAlignment = HorizontalAlignment.Center;
            button.ZIndex = selected ? 2 : 1; markers.Children.Add(button);
        }
        foreach (var (item, from, to) in routes)
        {
            var a = surface.ToScreen(Coordinates(from)!.Value); var b = surface.ToScreen(Coordinates(to)!.Value);
            // Label at the middle of the drawn arc (same bend as the surface), so opposite directions do not collide.
            var mid = new Point((a.X + b.X) / 2 - (b.Y - a.Y) * 0.09, (a.Y + b.Y) / 2 + (b.X - a.X) * 0.09);
            var chosen = vm.Selected?.Id == item.Id;
            if (mode == "off" && !chosen || mode != "always" && !chosen && Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y) < 120) continue;
            var (icon, kind) = Visuals.For(trip, item);
            var chip = new Button { Content = Ui.H(5, Ui.Icon(icon, 13, "Kind." + kind + ".Fg"), Ui.Text(item.Title, "caption").Also(t => { t.FontWeight = FontWeight.SemiBold; t.TextWrapping = TextWrapping.NoWrap; })), Padding = new Thickness(8, 3), MinHeight = 0, CornerRadius = new CornerRadius(12) }.Named(item.Title);
            chip.Res(Button.BackgroundProperty, "Bg.Surface"); chip.Click += (_, _) => vm.Selected = vm.Workspace?.State.Trip.Find(item.Id);
            Canvas.SetLeft(chip, mid.X - 60); Canvas.SetTop(chip, mid.Y - 12); chip.ZIndex = 0; markers.Children.Add(chip);
        }
    }
}
