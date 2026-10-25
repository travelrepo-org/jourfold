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
    private readonly double[] detents;
    private readonly bool side;
    private int detent;
    private double? dragFrom, dragHeight;
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
        var scroll = new ScrollViewer { Content = content, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        var top = side ? header : Ui.V(0, Handle(), header);
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
            BoxShadow = BoxShadows.Parse("0 -2 16 0 #26000000")
        }.Res(Border.BackgroundProperty, "Bg.Surface");
        AutomationProperties.SetName(panel, label);
        if (!side) panel.Transitions = [new DoubleTransition { Property = HeightProperty, Duration = TimeSpan.FromMilliseconds(180), Easing = new Avalonia.Animation.Easings.CubicEaseOut() }];
        Children.Add(scrim); Children.Add(panel);
        // Dragging works on the handle and the header, so the content can still scroll.
        top.PointerPressed += OnPressed; top.PointerMoved += OnMoved; top.PointerReleased += OnReleased;
        SizeChanged += (_, _) => Place();
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
    }

    private void OnPressed(object? sender, PointerPressedEventArgs e)
    {
        if (side) return;
        dragFrom = e.GetPosition(this).Y; dragHeight = panel.Bounds.Height; panel.Transitions?.Clear(); e.Pointer.Capture(sender as IInputElement);
    }

    private void OnMoved(object? sender, PointerEventArgs e)
    {
        if (dragFrom is not { } from || side) return;
        dragHeight = Math.Clamp(panel.Bounds.Height + (from - e.GetPosition(this).Y), 40, Bounds.Height * detents[^1]);
        dragFrom = e.GetPosition(this).Y; Place();
    }

    private void OnReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (dragFrom is null || side) return;
        e.Pointer.Capture(null);
        var height = dragHeight ?? panel.Bounds.Height; dragFrom = null; dragHeight = null;
        panel.Transitions = [new DoubleTransition { Property = HeightProperty, Duration = TimeSpan.FromMilliseconds(180), Easing = new Avalonia.Animation.Easings.CubicEaseOut() }];
        var share = height / Math.Max(1, Bounds.Height);
        // Well below the lowest detent closes; otherwise the nearest detent wins.
        if (share < detents[0] * 0.6) { Close(); return; }
        detent = Enumerable.Range(0, detents.Length).OrderBy(i => Math.Abs(detents[i] - share)).First();
        Place();
    }
}
