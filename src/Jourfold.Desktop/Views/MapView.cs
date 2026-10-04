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
        surface.Routes = routes.Select(r => (Coordinates(r.From)!.Value, Coordinates(r.To)!.Value, r.Item.Data["status"]?.ToString() is "confirmed" or "completed", Highlighted(r.Item, r.From, r.To))).ToArray();
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
                if (remembered is { } r && places.Length > 0 && vm.Selected is not { Type: "place" }) surface.Restore(r.X, r.Y, r.Zoom); else Fit(e.NewSize);
            }
            Layout();
        };
    }

    private bool Highlighted(Entity item, Entity from, Entity to) => vm.Selected is { } s && (s.Id == item.Id || s.Id == from.Id || s.Id == to.Id);

    public static (double Lat, double Lon)? Coordinates(Entity place)
    {
        var location = place.Data["location"];
        if (location?["latitude"] is not JsonValue lat || location["longitude"] is not JsonValue lon) return null;
        if (!double.TryParse(lat.ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var a) || !double.TryParse(lon.ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var b)) return null;
        return a is >= -85 and <= 85 && b is >= -180 and <= 180 ? (a, b) : null;
    }

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
        var showLabels = places.Length <= 14 || surface.Zoom >= 12;
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
            Canvas.SetLeft(button, point.X - 80); Canvas.SetTop(button, point.Y - size / 2.0); button.Width = 160; content.HorizontalAlignment = HorizontalAlignment.Center;
            button.ZIndex = selected ? 2 : 1; markers.Children.Add(button);
        }
        foreach (var (item, from, to) in routes)
        {
            var a = surface.ToScreen(Coordinates(from)!.Value); var b = surface.ToScreen(Coordinates(to)!.Value);
            // Label at the middle of the drawn arc (same bend as the surface), so opposite directions do not collide.
            var mid = new Point((a.X + b.X) / 2 - (b.Y - a.Y) * 0.09, (a.Y + b.Y) / 2 + (b.X - a.X) * 0.09); if (Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y) < 120) continue;
            var (icon, kind) = Visuals.For(trip, item);
            var chip = new Button { Content = Ui.H(5, Ui.Icon(icon, 13, "Kind." + kind + ".Fg"), Ui.Text(item.Title, "caption").Also(t => { t.FontWeight = FontWeight.SemiBold; t.TextWrapping = TextWrapping.NoWrap; })), Padding = new Thickness(8, 3), MinHeight = 0, CornerRadius = new CornerRadius(12) }.Named(item.Title);
            chip.Res(Button.BackgroundProperty, "Bg.Surface"); chip.Click += (_, _) => vm.Selected = vm.Workspace?.State.Trip.Find(item.Id);
            Canvas.SetLeft(chip, mid.X - 60); Canvas.SetTop(chip, mid.Y - 12); chip.ZIndex = 0; markers.Children.Add(chip);
        }
    }

    /// <summary>The drawing surface: basemap tiles or an offline grid, plus route lines. Handles pan and zoom.</summary>
    private sealed class MapSurface : Control
    {
        private readonly MapTiles? tiles;
        private readonly Dictionary<(int, int, int), Bitmap?> cache = new();
        private readonly HashSet<(int, int, int)> pending = new();
        /// <summary>Tiles in the last drawn view; replaced, never changed, so loaders on other threads can read it.</summary>
        private volatile HashSet<(int, int, int)> onScreen = new();
        private double centerX = 0.5, centerY = 0.5; // world coordinates in [0, 1]
        public int Zoom { get; private set; } = 3;
        private Point? dragFrom;
        private ((double Lat, double Lon) A, (double Lat, double Lon) B, bool Solid, bool Highlight)[] routeCoordinates = [];
        public ((double Lat, double Lon) A, (double Lat, double Lon) B, bool Solid, bool Highlight)[] Routes { set => routeCoordinates = value; }
        public event Action? Changed;
        public (double X, double Y, int Zoom) State => (centerX, centerY, Zoom);

        public MapSurface(MapTiles? tiles) { this.tiles = tiles; ClipToBounds = true; Cursor = new Cursor(StandardCursorType.SizeAll); Focusable = true; }


        private static (double X, double Y) World((double Lat, double Lon) p)
        {
            var lat = Math.Clamp(p.Lat, -85.0511, 85.0511) * Math.PI / 180;
            return ((p.Lon + 180) / 360, (1 - Math.Log(Math.Tan(lat) + 1 / Math.Cos(lat)) / Math.PI) / 2);
        }
        private double Scale => 256 * Math.Pow(2, Zoom);
        public Point ToScreen((double Lat, double Lon) p) { var (x, y) = World(p); return new Point((x - centerX) * Scale + Bounds.Width / 2, (y - centerY) * Scale + Bounds.Height / 2); }

        public void Restore(double x, double y, int zoom) { centerX = x; centerY = y; Zoom = zoom; Refresh(); }
        public void Fit(IReadOnlyList<(double Lat, double Lon)> points, Size size)
        {
            if (points.Count == 0) { centerX = 0.5; centerY = 0.42; Zoom = 2; Refresh(); return; }
            var world = points.Select(World).ToArray();
            double minX = world.Min(p => p.X), maxX = world.Max(p => p.X), minY = world.Min(p => p.Y), maxY = world.Max(p => p.Y);
            centerX = (minX + maxX) / 2; centerY = (minY + maxY) / 2;
            var width = Math.Max(200, size.Width - 160); var height = Math.Max(200, size.Height - 160);
            Zoom = 16;
            while (Zoom > 2 && ((maxX - minX) * Scale > width || (maxY - minY) * Scale > height)) Zoom--;
            if (points.Count == 1) Zoom = 14;
            Refresh();
        }
        public void ZoomAt(int delta, Point screen)
        {
            var next = Math.Clamp(Zoom + delta, 2, 18); if (next == Zoom) return;
            var wx = centerX + (screen.X - Bounds.Width / 2) / Scale; var wy = centerY + (screen.Y - Bounds.Height / 2) / Scale;
            Zoom = next; centerX = wx - (screen.X - Bounds.Width / 2) / Scale; centerY = wy - (screen.Y - Bounds.Height / 2) / Scale; Refresh();
        }
        private void Refresh() { InvalidateVisual(); Changed?.Invoke(); }

        protected override void OnPointerPressed(PointerPressedEventArgs e) { if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) { dragFrom = e.GetPosition(this); e.Pointer.Capture(this); } base.OnPointerPressed(e); }
        protected override void OnPointerMoved(PointerEventArgs e)
        {
            if (dragFrom is { } from) { var p = e.GetPosition(this); centerX -= (p.X - from.X) / Scale; centerY -= (p.Y - from.Y) / Scale; centerY = Math.Clamp(centerY, 0, 1); dragFrom = p; Refresh(); }
            base.OnPointerMoved(e);
        }
        protected override void OnPointerReleased(PointerReleasedEventArgs e) { dragFrom = null; e.Pointer.Capture(null); base.OnPointerReleased(e); }
        protected override void OnPointerWheelChanged(PointerWheelEventArgs e) { ZoomAt(e.Delta.Y > 0 ? 1 : -1, e.GetPosition(this)); e.Handled = true; }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            var step = 80 / Scale;
            switch (e.Key) { case Key.Left: centerX -= step; break; case Key.Right: centerX += step; break; case Key.Up: centerY -= step; break; case Key.Down: centerY += step; break; case Key.OemPlus or Key.Add: ZoomAt(1, new Point(Bounds.Width / 2, Bounds.Height / 2)); return; case Key.OemMinus or Key.Subtract: ZoomAt(-1, new Point(Bounds.Width / 2, Bounds.Height / 2)); return; default: base.OnKeyDown(e); return; }
            e.Handled = true; Refresh();
        }

        public override void Render(DrawingContext context)
        {
            var land = Ui.Brush(this, "Map.Land") ?? Brushes.WhiteSmoke; var gridBrush = Ui.Brush(this, "Map.Grid") ?? Brushes.LightGray;
            context.FillRectangle(land, new Rect(Bounds.Size));
            if (tiles is not null) DrawTiles(context);
            else
            {
                var pen = new Pen(gridBrush, 1);
                var step = Zoom switch { <= 4 => 10.0, <= 7 => 1.0, <= 10 => 0.25, <= 13 => 0.05, _ => 0.01 };
                var topLeft = FromScreen(new Point(0, 0)); var bottomRight = FromScreen(new Point(Bounds.Width, Bounds.Height));
                for (var lon = Math.Floor(topLeft.Lon / step) * step; lon <= bottomRight.Lon; lon += step) { var x = ToScreen((0, lon)).X; context.DrawLine(pen, new Point(x, 0), new Point(x, Bounds.Height)); }
                for (var lat = Math.Floor(bottomRight.Lat / step) * step; lat <= topLeft.Lat; lat += step) { var y = ToScreen((lat, 0)).Y; context.DrawLine(pen, new Point(0, y), new Point(Bounds.Width, y)); }
            }
            var transport = Ui.Brush(this, "Kind.Transport.Bar") ?? Brushes.SteelBlue; var accent = Ui.Brush(this, "Accent") ?? Brushes.Teal;
            foreach (var (a, b, solid, highlight) in routeCoordinates)
            {
                var p1 = ToScreen(a); var p2 = ToScreen(b);
                var pen = new Pen(highlight ? accent : transport, highlight ? 4.5 : 3, solid ? null : new DashStyle([2, 2], 0), PenLineCap.Round);
                // A gentle arc keeps long flights readable and distinguishes them from roads.
                var mid = new Point((p1.X + p2.X) / 2, (p1.Y + p2.Y) / 2); var dx = p2.X - p1.X; var dy = p2.Y - p1.Y; var bend = 0.18;
                var control = new Point(mid.X - dy * bend, mid.Y + dx * bend);
                var geometry = new StreamGeometry(); using (var g = geometry.Open()) { g.BeginFigure(p1, false); g.QuadraticBezierTo(control, p2); g.EndFigure(false); }
                context.DrawGeometry(null, pen, geometry);
            }
        }
        private (double Lat, double Lon) FromScreen(Point p)
        {
            var x = centerX + (p.X - Bounds.Width / 2) / Scale; var y = centerY + (p.Y - Bounds.Height / 2) / Scale;
            var lon = x * 360 - 180; var n = Math.PI - 2 * Math.PI * y; var lat = 180 / Math.PI * Math.Atan(0.5 * (Math.Exp(n) - Math.Exp(-n)));
            return (lat, lon);
        }
        private void DrawTiles(DrawingContext context)
        {
            var count = 1 << Zoom; var originX = centerX * Scale - Bounds.Width / 2; var originY = centerY * Scale - Bounds.Height / 2;
            var visible = new HashSet<(int, int, int)>();
            var x0 = (int)Math.Floor(originX / 256); var y0 = (int)Math.Floor(originY / 256); var x1 = (int)Math.Floor((originX + Bounds.Width) / 256); var y1 = (int)Math.Floor((originY + Bounds.Height) / 256);
            for (var ty = Math.Max(0, y0); ty <= Math.Min(count - 1, y1); ty++)
                for (var tx = x0; tx <= x1; tx++)
                {
                    var key = (Zoom, ((tx % count) + count) % count, ty); visible.Add(key);
                    var rect = new Rect(tx * 256 - originX, ty * 256 - originY, 256, 256);
                    if (cache.TryGetValue(key, out var bitmap)) { if (bitmap is not null) context.DrawImage(bitmap, rect); continue; }
                    if (pending.Add(key)) _ = LoadAsync(key);
                }
            onScreen = visible;
        }
        private async Task LoadAsync((int Z, int X, int Y) key)
        {
            try
            {
                // Tiles that scrolled out of view before their turn are not downloaded; they load again when visible.
                var (bytes, skipped) = await tiles!.GetAsync(key.Z, key.X, key.Y, () => onScreen.Contains(key));
                if (skipped) { Dispatcher.UIThread.Post(() => pending.Remove(key)); return; }
                Bitmap? bitmap = null; if (bytes is not null) { using var stream = new MemoryStream(bytes); bitmap = new Bitmap(stream); }
                Dispatcher.UIThread.Post(() => { cache[key] = bitmap; pending.Remove(key); InvalidateVisual(); });
            }
            catch (Exception ex) when (ex is IOException or HttpRequestException or OperationCanceledException or ArgumentException) { Dispatcher.UIThread.Post(() => { cache[key] = null; pending.Remove(key); }); }
        }
    }
}
