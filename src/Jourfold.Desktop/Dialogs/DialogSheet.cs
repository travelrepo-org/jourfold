using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Jourfold.Desktop;

/// <summary>
/// An in-window modal sheet. Application forms stay inside the owning window so focus and activation
/// behave on every desktop; only operating-system file pickers open native dialogs.
/// </summary>
public sealed class DialogSheet(string title, Control body, double width = 560, string? subtitle = null, bool top = false)
{
    private readonly TaskCompletionSource<object?> completion = new();
    public event EventHandler? Opened;
    public bool ShowClose { get; init; } = true;
    public void Close(object? result = null) => completion.TrySetResult(result);

    public async Task<T?> ShowDialog<T>(Window owner)
    {
        var layer = owner.FindControl<Grid>("DialogLayer") ?? throw new InvalidOperationException("Dialog host is missing.");
        var layout = owner.FindControl<LayoutTransformControl>("RootLayout")!;
        var previous = owner.FocusManager?.GetFocusedElement();
        var previousSheet = layer.Children.LastOrDefault();
        if (previousSheet is not null) previousSheet.IsEnabled = false;
        layout.IsEnabled = false;
        var close = Ui.IconButton("x", owner is MainWindow mw ? mw.Model.Strings["Close"] : "Close", "small"); close.IsVisible = ShowClose; close.Click += (_, _) => Close();
        var heading = Ui.Columns("*,Auto", Ui.V(4, Ui.Text(title, "h2"), subtitle is null ? null : Ui.Text(subtitle, "muted")), close.Also(c => c.VerticalAlignment = VerticalAlignment.Top));
        heading.Margin = new Thickness(0, 0, 0, 18);
        var content = new DockPanel(); DockPanel.SetDock(heading, Dock.Top); content.Children.Add(heading); content.Children.Add(body);
        var card = new Border
        {
            Padding = new Thickness(26, 22, 26, 24),
            Width = Math.Min(width, Math.Max(340, owner.Bounds.Width - 48)),
            MaxHeight = Math.Max(320, owner.Bounds.Height - (top ? 120 : 64)),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = top ? VerticalAlignment.Top : VerticalAlignment.Center,
            Margin = new Thickness(0, top ? 90 : 0, 0, 0),
            Child = new ScrollViewer { Content = content, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled }
        }.Classed("raised");
        Avalonia.Automation.AutomationProperties.SetName(card, title);
        var shield = new Grid { Children = { card } }.Res(Panel.BackgroundProperty, "Overlay");
        shield.PointerPressed += (_, e) => { if (e.Source == shield) Close(); };
        KeyboardNavigation.SetTabNavigation(shield, KeyboardNavigationMode.Cycle);
        void HandleKeys(object? sender, KeyEventArgs e)
        {
            if (layer.Children.LastOrDefault() != shield) return;
            if (e.Key == Key.Escape) { Close(); e.Handled = true; }
            if (e.Key == Key.Enter && !e.Handled)
            {
                if (e.Source is TextBox { AcceptsReturn: true } && e.KeyModifiers != KeyModifiers.Control) return;
                if (e.Source is Button or ListBoxItem || (e.Source as Control)?.FindAncestorOfType<AutoCompleteBox>() is { IsDropDownOpen: true }) return;
                var button = body.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => b.IsDefault && b.IsEffectivelyEnabled && b.IsEffectivelyVisible);
                if (button is not null) { button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); e.Handled = true; }
            }
            if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key is Key.N or Key.K or Key.Z or Key.S) e.Handled = true;
        }
        owner.AddHandler(InputElement.KeyDownEvent, HandleKeys, RoutingStrategies.Tunnel);
        void OwnerClosed(object? sender, EventArgs e) => Close();
        owner.Closed += OwnerClosed;
        layer.Children.Add(shield);
        try
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (completion.Task.IsCompleted || layer.Children.LastOrDefault() != shield) return;
                (body.GetVisualDescendants().OfType<Control>().FirstOrDefault(c => c is TextBox or AutoCompleteBox or ListBox && c.Focusable && c.IsEffectivelyVisible && c.IsEffectivelyEnabled)
                 ?? body.GetVisualDescendants().OfType<Control>().FirstOrDefault(c => c.Focusable && c.IsEffectivelyVisible && c.IsEffectivelyEnabled))?.Focus();
                Opened?.Invoke(this, EventArgs.Empty);
            }, DispatcherPriority.Loaded);
            var result = await completion.Task;
            return result is T typed ? typed : default;
        }
        finally
        {
            owner.Closed -= OwnerClosed;
            owner.RemoveHandler(InputElement.KeyDownEvent, HandleKeys);
            layer.Children.Remove(shield);
            if (previousSheet is not null) previousSheet.IsEnabled = true;
            else layout.IsEnabled = true;
            if (owner.IsActive && owner.IsVisible) previous?.Focus();
        }
    }
    public async Task ShowDialog(Window owner) => await ShowDialog<object>(owner);
}
