using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Jourfold.Application;

namespace Jourfold.UI;

/// <summary>
/// A map in Web Mercator: basemap tiles or an offline grid, plus route lines. Pans with the mouse or a finger, zooms
/// with the wheel, two fingers or a double tap. Markers are drawn by the views on top, through <see cref="ToScreen"/>.
/// </summary>
public sealed class MapSurface : Control
{
    private readonly MapTiles? tiles;
    private readonly Dictionary<(int, int, int), Bitmap?> cache = new();
    private readonly HashSet<(int, int, int)> pending = new();
    /// <summary>Tiles in the last drawn view; replaced, never changed, so loaders on other threads can read it.</summary>
    private volatile HashSet<(int, int, int)> onScreen = new();
    private double centerX = 0.5, centerY = 0.5; // world coordinates in [0, 1]
    public int Zoom { get; private set; } = 3;
    private Point? dragFrom;
    // Fingers on the map, for zooming with two of them; the distance between them when the last zoom step happened.
    private readonly Dictionary<int, Point> touches = new();
    private double? pinchFrom;
    private ((double Lat, double Lon) A, (double Lat, double Lon) B, bool Solid, bool Highlight)[] routeCoordinates = [];
    public ((double Lat, double Lon) A, (double Lat, double Lon) B, bool Solid, bool Highlight)[] Routes { set { routeCoordinates = value; InvalidateVisual(); } }
    private (double Lat, double Lon)[] path = [];
    /// <summary>A line through places in order, such as a day's stops, drawn under the routes.</summary>
    public (double Lat, double Lon)[] Path { set { path = value; InvalidateVisual(); } }
    public event Action? Changed;
    public (double X, double Y, int Zoom) State => (centerX, centerY, Zoom);

    public MapSurface(MapTiles? tiles)
    {
        this.tiles = tiles; ClipToBounds = true; Cursor = new Cursor(StandardCursorType.SizeAll); Focusable = true;
        DoubleTapped += (_, e) => { ZoomAt(1, e.GetPosition(this)); e.Handled = true; };
    }

    private static (double X, double Y) World((double Lat, double Lon) p)
    {
        var lat = Math.Clamp(p.Lat, -85.0511, 85.0511) * Math.PI / 180;
        return ((p.Lon + 180) / 360, (1 - Math.Log(Math.Tan(lat) + 1 / Math.Cos(lat)) / Math.PI) / 2);
    }
    private double Scale => 256 * Math.Pow(2, Zoom);
    public Point ToScreen((double Lat, double Lon) p) { var (x, y) = World(p); return new Point((x - centerX) * Scale + Bounds.Width / 2, (y - centerY) * Scale + Bounds.Height / 2); }

    public void Restore(double x, double y, int zoom) { centerX = x; centerY = y; Zoom = zoom; Refresh(); }
    public void CenterOn((double Lat, double Lon) p) { (centerX, centerY) = World(p); Refresh(); }
    public void Redraw() => Refresh();
    /// <summary>Moves the map by screen pixels; positive values move the view down and right, so the content moves up and left.</summary>
    public void Pan(double dx, double dy) { centerX += dx / Scale; centerY = Math.Clamp(centerY + dy / Scale, 0, 1); Refresh(); }
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

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            var p = e.GetPosition(this); dragFrom = p; e.Pointer.Capture(this);
            if (e.Pointer.Type == PointerType.Touch) { touches[e.Pointer.Id] = p; if (touches.Count == 2) { pinchFrom = Spread(); dragFrom = null; } }
        }
        base.OnPointerPressed(e);
    }
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        var p = e.GetPosition(this);
        if (e.Pointer.Type == PointerType.Touch && touches.ContainsKey(e.Pointer.Id)) touches[e.Pointer.Id] = p;
        // Two fingers zoom one level each time their distance grows or shrinks by half, around the point between them.
        if (touches.Count >= 2 && pinchFrom is { } from)
        {
            var spread = Spread(); var middle = new Point(touches.Values.Take(2).Average(t => t.X), touches.Values.Take(2).Average(t => t.Y));
            if (spread / from > 1.5) { ZoomAt(1, middle); pinchFrom = spread; }
            else if (spread / from < 0.67) { ZoomAt(-1, middle); pinchFrom = spread; }
        }
        else if (dragFrom is { } start) { centerX -= (p.X - start.X) / Scale; centerY -= (p.Y - start.Y) / Scale; centerY = Math.Clamp(centerY, 0, 1); dragFrom = p; Refresh(); }
        base.OnPointerMoved(e);
    }
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        touches.Remove(e.Pointer.Id);
        if (touches.Count < 2) pinchFrom = null;
        // Lifting one of two fingers goes on panning with the other.
        dragFrom = touches.Count == 1 ? touches.Values.First() : null;
        e.Pointer.Capture(null); base.OnPointerReleased(e);
    }
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e) { touches.Remove(e.Pointer.Id); if (touches.Count < 2) pinchFrom = null; if (touches.Count == 0) dragFrom = null; base.OnPointerCaptureLost(e); }
    private double Spread() { var two = touches.Values.Take(2).ToArray(); return Math.Max(1, Point.Distance(two[0], two[1])); }

    /// <summary>The place under a point on the screen.</summary>
    public (double Lat, double Lon) ToMap(Point p) => FromScreen(p);
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
        // Lines are cut to what is on the screen before they are drawn: zoomed in close, a flight's line is millions of
        // pixels long, and dashing all of it kept the renderer busy for minutes.
        var view = new Rect(Bounds.Size).Inflate(32);
        if (path.Length > 1)
        {
            var line = new StreamGeometry();
            using (var g = line.Open()) for (var i = 1; i < path.Length; i++) Segment(g, ToScreen(path[i - 1]), ToScreen(path[i]), view);
            context.DrawGeometry(null, new Pen(accent, 2.5, new DashStyle([3, 2], 0), PenLineCap.Round, PenLineJoin.Round), line);
        }
        foreach (var (a, b, solid, highlight) in routeCoordinates)
        {
            var p1 = ToScreen(a); var p2 = ToScreen(b);
            var pen = new Pen(highlight ? accent : transport, highlight ? 4.5 : 3, solid ? null : new DashStyle([2, 2], 0), PenLineCap.Round);
            // A gentle arc keeps long flights readable and distinguishes them from roads.
            var mid = new Point((p1.X + p2.X) / 2, (p1.Y + p2.Y) / 2); var dx = p2.X - p1.X; var dy = p2.Y - p1.Y; var bend = 0.18;
            var control = new Point(mid.X - dy * bend, mid.Y + dx * bend);
            var geometry = new StreamGeometry(); using (var g = geometry.Open()) Curve(g, p1, control, p2, view, 0);
            context.DrawGeometry(null, pen, geometry);
        }
    }

    /// <summary>The part of the straight line from <paramref name="a"/> to <paramref name="b"/> that lies in <paramref name="view"/>.</summary>
    private static void Segment(StreamGeometryContext g, Point a, Point b, Rect view)
    {
        // Liang-Barsky: the line is a + t(b - a), kept for the t that lie inside all four edges.
        double from = 0, to = 1, dx = b.X - a.X, dy = b.Y - a.Y;
        foreach (var (p, q) in new[] { (-dx, a.X - view.Left), (dx, view.Right - a.X), (-dy, a.Y - view.Top), (dy, view.Bottom - a.Y) })
        {
            if (p == 0) { if (q < 0) return; continue; }
            var t = q / p;
            if (p < 0) { if (t > to) return; from = Math.Max(from, t); }
            else { if (t < from) return; to = Math.Min(to, t); }
        }
        g.BeginFigure(new Point(a.X + from * dx, a.Y + from * dy), false); g.LineTo(new Point(a.X + to * dx, a.Y + to * dy)); g.EndFigure(false);
    }

    /// <summary>
    /// The part of a quadratic curve that lies in <paramref name="view"/>: halved until a piece is short or off the
    /// screen, so only the pieces on the screen are drawn, as short straight lines.
    /// </summary>
    private static void Curve(StreamGeometryContext g, Point a, Point control, Point b, Rect view, int depth)
    {
        var box = new Rect(new Point(Math.Min(a.X, Math.Min(control.X, b.X)), Math.Min(a.Y, Math.Min(control.Y, b.Y))), new Point(Math.Max(a.X, Math.Max(control.X, b.X)), Math.Max(a.Y, Math.Max(control.Y, b.Y))));
        if (!box.Intersects(view)) return;
        if (depth >= 28 || Point.Distance(a, control) + Point.Distance(control, b) < 200)
        {
            var previous = a;
            for (var i = 1; i <= 8; i++)
            {
                var t = i / 8.0; var u = 1 - t;
                var next = new Point(u * u * a.X + 2 * u * t * control.X + t * t * b.X, u * u * a.Y + 2 * u * t * control.Y + t * t * b.Y);
                Segment(g, previous, next, view); previous = next;
            }
            return;
        }
        Point Mid(Point p, Point q) => new((p.X + q.X) / 2, (p.Y + q.Y) / 2);
        var left = Mid(a, control); var right = Mid(control, b); var middle = Mid(left, right);
        Curve(g, a, left, middle, view, depth + 1); Curve(g, middle, right, b, view, depth + 1);
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
        var visible = new HashSet<(int, int, int)>(); var missing = new List<(int, int, int)>();
        var x0 = (int)Math.Floor(originX / 256); var y0 = (int)Math.Floor(originY / 256); var x1 = (int)Math.Floor((originX + Bounds.Width) / 256); var y1 = (int)Math.Floor((originY + Bounds.Height) / 256);
        for (var ty = Math.Max(0, y0); ty <= Math.Min(count - 1, y1); ty++)
            for (var tx = x0; tx <= x1; tx++)
            {
                var key = (Zoom, ((tx % count) + count) % count, ty); visible.Add(key);
                var rect = new Rect(tx * 256 - originX, ty * 256 - originY, 256, 256);
                if (cache.TryGetValue(key, out var bitmap)) { if (bitmap is not null) context.DrawImage(bitmap, rect); continue; }
                if (pending.Add(key)) missing.Add(key);
            }
        // The view is known before any tile loads: a loader may ask at once whether its tile is still on screen, and
        // with the previous view it would drop the new tiles until something happened to draw the map again.
        onScreen = visible;
        foreach (var key in missing) _ = LoadAsync(key);
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
