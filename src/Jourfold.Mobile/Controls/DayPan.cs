using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;

namespace Jourfold.Mobile.Controls;

/// <summary>
/// The days of the timetable side by side on one strip that glides sideways under the finger, with momentum, and comes
/// to rest with a day in the middle: on the tablet several days around it, on the phone one day with a part of the day
/// before and after showing at the edges. The time column stays where it is. Each part that moves (the days' heads, the
/// all-day strip, the grid and the nights) is one layer; they all move together.
/// </summary>
public sealed class DayPan
{
    private readonly List<TranslateTransform> layers = [];
    private readonly Func<double> viewport;
    private double offset;

    /// <param name="dayWidth">The width of one day's column.</param>
    /// <param name="count">How many days the strip holds.</param>
    /// <param name="viewport">The width the days are seen through, without the time column.</param>
    public DayPan(double dayWidth, int count, Func<double> viewport)
    {
        DayWidth = dayWidth; Count = count; this.viewport = viewport;
    }

    /// <summary>How far a drag must go before its speed carries the days on.</summary>
    public const double MinFlick = 40;

    /// <summary>The shortest time between two moves that the finger's speed is measured over, in milliseconds.</summary>
    public const ulong MinGap = 8;

    public double DayWidth { get; }
    public int Count { get; }
    public double Offset => offset;

    /// <summary>
    /// Raised when the strip has come to rest, with the day now in the middle. A glide that the finger catches again
    /// before it ends does not come to rest and is not reported.
    /// </summary>
    public event Action<int>? Settled;

    /// <summary>
    /// Raised as soon as a glide begins, with the day it is heading for, so what names the day, such as a date strip,
    /// can change with the days and not after them.
    /// </summary>
    public event Action<int>? Aimed;

    /// <summary>Raised when the finger takes hold of the days, also in the middle of a glide.</summary>
    public event Action? Grabbed;

    // Counts the glides, so one that was caught by the finger or replaced by another does not report its rest.
    private int glide;

    /// <summary>A part that moves with the days: as wide as all of them, seen through a frame as wide as the viewport.</summary>
    public Control Layer(Control content)
    {
        var move = new TranslateTransform(-offset, 0);
        layers.Add(move);
        content.Width = DayWidth * Count; content.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left; content.RenderTransform = move;
        content.RenderTransformOrigin = RelativePoint.TopLeft;
        return new Border { Child = content, ClipToBounds = true };
    }

    private double Viewport => Math.Max(1, viewport());
    private double Max => Math.Max(0, DayWidth * Count - Viewport);

    /// <summary>The offset that puts <paramref name="day"/> in the middle, or as near as the first and last day allow.</summary>
    public double OffsetOf(int day) => Math.Clamp(day * DayWidth - (Viewport - DayWidth) / 2, 0, Max);

    /// <summary>The day whose middle is nearest to the middle of the viewport at <paramref name="at"/>.</summary>
    public int DayAt(double at) => Math.Clamp((int)Math.Floor((at + Viewport / 2) / DayWidth), 0, Count - 1);

    /// <summary>The day in the middle now.</summary>
    public int Middle => DayAt(offset);

    /// <summary>The first and last day that can be seen, even in part.</summary>
    public (int First, int Last) Visible => (Math.Clamp((int)Math.Floor(offset / DayWidth), 0, Count - 1), Math.Clamp((int)Math.Ceiling((offset + Viewport) / DayWidth) - 1, 0, Count - 1));

    /// <summary>Puts the strip at once at <paramref name="value"/>, without gliding.</summary>
    public void Jump(double value) { offset = value; foreach (var move in layers) { move.Transitions = null; move.X = -offset; } }

    /// <summary>Glides to <paramref name="day"/> in the middle and reports it once there.</summary>
    public void GlideTo(int day)
    {
        var target = OffsetOf(Math.Clamp(day, 0, Count - 1));
        var distance = Math.Abs(target - offset);
        var time = TimeSpan.FromMilliseconds(Math.Clamp(140 + distance / DayWidth * 70, 160, 420));
        offset = target;
        var mine = ++glide;
        foreach (var move in layers) { move.Transitions = [new DoubleTransition { Property = TranslateTransform.XProperty, Duration = time, Easing = new CubicEaseOut() }]; move.X = -offset; }
        Aimed?.Invoke(Middle);
        DispatcherTimer.RunOnce(() => { if (mine == glide) Settled?.Invoke(Middle); }, time + TimeSpan.FromMilliseconds(20));
    }

    /// <summary>
    /// Stops a glide where the days are right now, so the finger takes them from there: a second swipe right after the
    /// first carries on instead of waiting for the days to come to rest.
    /// </summary>
    /// <returns>Whether the days were still on their way, short of the day they were heading for.</returns>
    private bool Catch()
    {
        glide++;
        if (layers.Count == 0) return false;
        // While gliding, the layer's place is the one on its way, not the one it is heading for.
        var now = -layers[0].X;
        var onTheWay = Math.Abs(now - offset) > 1;
        foreach (var move in layers) { move.Transitions = null; move.X = -now; }
        offset = now;
        return onTheWay;
    }

    /// <summary>
    /// Follows a sideways drag on <paramref name="area"/>: the strip moves with the finger, a little at the first and
    /// last day, and on letting go it carries on with the finger's speed and settles with the nearest day in the middle.
    /// A drag that starts mostly up or down is left to the grid's own scrolling, and a tap stays a tap.
    /// </summary>
    /// <param name="ignore">A part that scrolls sideways by itself, such as the phone's date strip, or null.</param>
    public void Follow(Control area, Control? ignore = null)
    {
        Point? start = null; bool? sideways = null; double from = 0, lastX = 0, speed = 0; ulong lastTime = 0; var fromDay = 0; var caught = false;
        area.AddHandler(InputElement.PointerPressedEvent, (_, e) =>
        {
            var inside = ignore is not null && e.Source is Visual v && (v == ignore || Avalonia.VisualTree.VisualExtensions.GetVisualAncestors(v).Contains(ignore));
            start = inside ? null : e.GetPosition(area); sideways = null; speed = 0; lastTime = e.Timestamp; lastX = start?.X ?? 0;
        }, RoutingStrategies.Tunnel, handledEventsToo: true);
        area.AddHandler(InputElement.PointerMovedEvent, (_, e) =>
        {
            if (start is not { } begin) return;
            var at = e.GetPosition(area); var delta = at - begin;
            if (sideways is null)
            {
                if (Math.Abs(delta.X) > 12 && Math.Abs(delta.X) > 1.3 * Math.Abs(delta.Y))
                {
                    sideways = true; e.Pointer.Capture(area);
                    // A flick counts from the day the glide before was heading for, so two quick flicks move two days.
                    fromDay = Middle; caught = Catch(); from = offset;
                    Grabbed?.Invoke();
                }
                else if (Math.Abs(delta.Y) > 12) { sideways = false; start = null; return; }
                else return;
            }
            // Two moves that arrive a millisecond apart say little about the finger's speed; a screen reports one about every
            // eight milliseconds at most, so a shorter gap counts as that long.
            if (e.Timestamp > lastTime) { speed = 0.6 * speed + 0.4 * (at.X - lastX) / Math.Max(MinGap, e.Timestamp - lastTime); lastTime = e.Timestamp; lastX = at.X; }
            var wanted = from - delta.X;
            // Past the first or last day the strip follows only a little, to show there is nothing further.
            var shown = wanted < 0 ? wanted * 0.25 : wanted > Max ? Max + (wanted - Max) * 0.25 : wanted;
            offset = shown; foreach (var move in layers) move.X = -offset;
            e.Handled = true;
        }, RoutingStrategies.Tunnel, handledEventsToo: true);
        area.AddHandler(InputElement.PointerReleasedEvent, (_, e) =>
        {
            if (sideways != true) { start = null; return; }
            sideways = null; start = null; e.Handled = true; e.Pointer.Capture(null);
            // A short slip, such as while tapping, is no flick, however quick.
            var dragged = offset - from;
            Release(Math.Abs(dragged) < MinFlick ? 0 : speed, fromDay, caught && Math.Abs(dragged) >= MinFlick ? Math.Sign(dragged) : 0);
        }, RoutingStrategies.Tunnel, handledEventsToo: true);
        area.AddHandler(InputElement.PointerCaptureLostEvent, (_, _) => { if (sideways == true) { sideways = null; start = null; Release(0, fromDay); } }, RoutingStrategies.Bubble, handledEventsToo: true);
    }

    /// <summary>
    /// Lets go with <paramref name="speed"/> (pixels per millisecond, positive to the right): the strip carries on as
    /// far as the speed takes it and settles with the day nearest that place in the middle. A flick moves at least one day
    /// from <paramref name="fromDay"/>, the day in the middle when the drag began, or the day a glide was heading for
    /// when the finger caught it.
    /// </summary>
    /// <param name="onward">
    /// 1 or -1 when the finger caught the days on their way and dragged them on, later or earlier: that swipe moves at
    /// least one day beyond the one the first was heading for, so two swipes in a row are two days. 0 otherwise.
    /// </param>
    public void Release(double speed, int fromDay, int onward = 0)
    {
        var aim = DayAt(Math.Clamp(offset - speed * 280, 0, Max));
        if (aim == fromDay && Math.Abs(speed) > 0.35) aim = fromDay + (speed < 0 ? 1 : -1);
        if (onward > 0 && aim <= fromDay) aim = fromDay + 1;
        if (onward < 0 && aim >= fromDay) aim = fromDay - 1;
        // Near the first and last day several days share the same place; the flick goes on to one that moves the strip.
        while (aim > 0 && aim < Count - 1 && OffsetOf(aim) == OffsetOf(fromDay) && aim != fromDay && Math.Abs(speed) > 0.35) aim += speed < 0 ? 1 : -1;
        GlideTo(aim);
    }
}
