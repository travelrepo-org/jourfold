using Avalonia;
using Avalonia.Animation;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace Jourfold.Mobile.Controls;

/// <summary>
/// A sheet over the current page. On phones it comes up from the bottom and stops at detents (for example glance,
/// operational and expanded); dragging the handle or the header moves it between them, dragging it below the lowest
/// one closes it, and the content scrolls inside. On tablets it is a panel on the right without a handle. It stays
/// above the on-screen keyboard.
/// </summary>
public sealed class Sheet : Grid
{
    private readonly Border panel;
    private readonly Border scrim;
    private readonly ScrollViewer scroll;
    private bool placed;
    private readonly double[] detents;
    private readonly bool side;
    private int detent;
    // Where a drag started and how tall the sheet was then; the height follows the finger from there.
    private double? dragFrom, dragHeight;
    private double dragStartHeight;
    private double keyboard;

    /// <summary>Raised when the sheet closes, by dragging down, tapping outside, the back gesture or <see cref="Close"/>.</summary>
    public event Action? Closed;
    public int Detent => detent;

    /// <param name="detents">Heights as parts of the available height, lowest first, for example 0.4, 0.62, 0.94.</param>
    /// <param name="side">Show as a panel on the right instead of a bottom sheet.</param>
    public Sheet(Control header, Control content, double[] detents, int start, bool side, string label)
    {
        this.detents = detents; this.side = side; detent = Math.Clamp(start, 0, detents.Length - 1);
        scrim = new Border { Background = new SolidColorBrush(Colors.Black, side ? 0.18 : 0.32) };
        scrim.PointerPressed += (_, e) => { Close(); e.Handled = true; };
        scroll = new ScrollViewer { Content = content, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        // The whole header takes the drag, not only the handle, so it needs a background to be hit everywhere.
        var top = new Border { Child = side ? header : Ui.V(0, Handle(), header), Background = Brushes.Transparent };
        var layout = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        layout.Children.Add(top); layout.Children.Add(scroll.Row(1));
        panel = new Border
        {
            Child = layout,
            ClipToBounds = true,
            CornerRadius = side ? new CornerRadius(16, 0, 0, 16) : new CornerRadius(20, 20, 0, 0),
            VerticalAlignment = side ? VerticalAlignment.Stretch : VerticalAlignment.Bottom,
            HorizontalAlignment = side ? HorizontalAlignment.Right : HorizontalAlignment.Stretch,
            Width = side ? 400 : double.NaN,
            BoxShadow = BoxShadows.Parse("0 -2 16 0 #26000000"),
            // Hidden until it knows the screen's height; it would otherwise show at the height of its content first.
            IsVisible = side
        }.Res(Border.BackgroundProperty, "Bg.Surface");
        AutomationProperties.SetName(panel, label);
        Children.Add(scrim); Children.Add(panel);
        // Dragging works on the handle and the header, so the content can still scroll.
        top.PointerPressed += OnPressed; top.PointerMoved += OnMoved; top.PointerReleased += OnReleased;
        if (!side) FollowContent(scroll);
        SizeChanged += (_, _) => Place();
    }

    /// <summary>
    /// Below the top detent the content does not scroll, so dragging it moves the whole sheet, as the handle does. At the
    /// top detent it scrolls; dragging down at the top of the content lowers the sheet again.
    /// </summary>
    private void FollowContent(ScrollViewer scroll)
    {
        double? from = null; var dragging = false;
        scroll.AddHandler(PointerPressedEvent, (_, e) => { from = e.GetPosition(this).Y; dragging = false; }, Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: true);
        scroll.AddHandler(PointerMovedEvent, (_, e) =>
        {
            if (from is not { } start) return;
            var y = e.GetPosition(this).Y;
            if (!dragging)
            {
                var up = y - start < -12 && detent < detents.Length - 1; var down = y - start > 12 && scroll.Offset.Y <= 0.5;
                if (!up && !down) return;
                dragging = true; dragFrom = y; dragHeight = dragStartHeight = panel.Bounds.Height; panel.Transitions?.Clear(); e.Pointer.Capture(scroll);
            }
            OnMoved(scroll, e); e.Handled = true;
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: true);
        scroll.AddHandler(PointerReleasedEvent, (_, e) =>
        {
            from = null;
            if (!dragging) return;
            dragging = false; OnReleased(scroll, e); e.Handled = true;
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: true);
        scroll.AddHandler(PointerCaptureLostEvent, (_, _) => { if (dragging && dragFrom is not null) { dragging = false; from = null; Settle(dragHeight ?? panel.Bounds.Height); } }, Avalonia.Interactivity.RoutingStrategies.Bubble, handledEventsToo: true);
    }

    private Control Handle()
    {
        var bar = new Border { Width = 40, Height = 5, CornerRadius = new CornerRadius(3), HorizontalAlignment = HorizontalAlignment.Center }.Res(Border.BackgroundProperty, "Line.Strong");
        return new Border { Child = bar, Padding = new Thickness(0, 10, 0, 6), Background = Brushes.Transparent };
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (TopLevel.GetTopLevel(this)?.InputPane is { } pane)
            pane.StateChanged += (_, args) => { keyboard = args.NewState == Avalonia.Controls.Platform.InputPaneState.Open ? args.EndRect.Height : 0; Place(); };
    }

    /// <summary>Moves to a detent; on tablets there is only one size.</summary>
    public void Show(int index) { detent = Math.Clamp(index, 0, detents.Length - 1); Place(); }

    public void Close() { if (Parent is Panel host) host.Children.Remove(this); Closed?.Invoke(); }

    private void Place()
    {
        if (Bounds.Height <= 0) return;
        panel.Margin = new Thickness(0, 0, 0, keyboard);
        if (side) return;
        var available = Bounds.Height - keyboard;
        // With the keyboard up the sheet takes the room above it.
        panel.Height = keyboard > 0 ? available * 0.96 : dragHeight ?? available * detents[detent];
        // Touch scrolling would take every drag in the content; below the top detent the drag belongs to the sheet.
        var scrolls = keyboard > 0 || detent == detents.Length - 1;
        scroll.VerticalScrollBarVisibility = scrolls ? Avalonia.Controls.Primitives.ScrollBarVisibility.Auto : Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled;
        if (!scrolls) scroll.Offset = default;
        // The first placement is immediate; later changes of detent glide.
        if (!placed) { placed = true; panel.IsVisible = true; panel.Transitions = [Glide()]; }
    }

    private void OnPressed(object? sender, PointerPressedEventArgs e)
    {
        if (side) return;
        dragFrom = e.GetPosition(this).Y; dragHeight = dragStartHeight = panel.Bounds.Height; panel.Transitions?.Clear(); e.Pointer.Capture(sender as IInputElement);
    }

    private void OnMoved(object? sender, PointerEventArgs e)
    {
        if (dragFrom is not { } from || side) return;
        dragHeight = Math.Clamp(dragStartHeight + (from - e.GetPosition(this).Y), 40, Bounds.Height * detents[^1]);
        Place();
    }

    private void OnReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (dragFrom is null || side) return;
        e.Pointer.Capture(null);
        Settle(dragHeight ?? panel.Bounds.Height);
    }

    private static DoubleTransition Glide() => new() { Property = HeightProperty, Duration = TimeSpan.FromMilliseconds(180), Easing = new Avalonia.Animation.Easings.CubicEaseOut() };

    /// <summary>Lets go of a drag: the nearest detent, or closed when well below the lowest.</summary>
    private void Settle(double height)
    {
        dragFrom = null; dragHeight = null;
        panel.Transitions = [Glide()];
        var share = height / Math.Max(1, Bounds.Height);
        // Well below the lowest detent closes; otherwise the nearest detent wins.
        if (share < detents[0] * 0.6) { Close(); return; }
        detent = Enumerable.Range(0, detents.Length).OrderBy(i => Math.Abs(detents[i] - share)).First();
        Place();
    }
}
