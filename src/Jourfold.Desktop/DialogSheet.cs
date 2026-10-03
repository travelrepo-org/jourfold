using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using Avalonia.Threading;

namespace Jourfold.Desktop;

// Keep application forms in their owning window. Only OS file pickers create native dialogs.
internal sealed class DialogSheet(string title, Control body, double width = 560)
{
    private readonly TaskCompletionSource<object?> completion = new();
    public event EventHandler? Opened;
    public void Close(object? result = null) => completion.TrySetResult(result);
    public async Task<T?> ShowDialog<T>(Window owner)
    {
        var layer = owner.FindControl<Grid>("DialogLayer") ?? throw new InvalidOperationException("Dialog host is missing.");
        var layout = owner.FindControl<LayoutTransformControl>("RootLayout")!;
        var previous = owner.FocusManager?.GetFocusedElement();
        var previousSheet = layer.Children.LastOrDefault();
        if (previousSheet is not null) previousSheet.IsEnabled = false;
        layout.IsEnabled = false;
        var heading = new TextBlock { Text = title, Classes = { "heading" }, Margin = new Thickness(0, 0, 0, 18) };
        var content = new StackPanel { Children = { heading, body } };
        var border = new Border { Classes = { "panel" }, Padding = new Thickness(24), Width = Math.Min(width, Math.Max(320, owner.Bounds.Width - 48)), MaxHeight = Math.Max(300, owner.Bounds.Height - 48), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Child = new ScrollViewer { Content = content } };
        var shield = new Grid { Background = new SolidColorBrush(Color.FromArgb(110, 7, 23, 36)), Children = { border } };
        KeyboardNavigation.SetTabNavigation(shield, KeyboardNavigationMode.Cycle);
        void HandleKeys(object? sender, KeyEventArgs e)
        {
            if (layer.Children.LastOrDefault() != shield) return;
            if (e.Key == Key.Escape) { Close(); e.Handled = true; }
            if (e.Key == Key.Enter && e.Source is not TextBox { AcceptsReturn: true })
            {
                var button = body.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => b.IsDefault && b.IsEnabled);
                if (button is not null) { button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); e.Handled = true; }
            }
            if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key is Key.N or Key.K or Key.Z) e.Handled = true;
        }
        owner.KeyDown += HandleKeys;
        void OwnerClosed(object? sender, EventArgs e) => Close();
        owner.Closed += OwnerClosed;
        layer.Children.Add(shield);
        try
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (completion.Task.IsCompleted || layer.Children.LastOrDefault() != shield) return;
                body.GetVisualDescendants().OfType<Control>().FirstOrDefault(c => c.Focusable && c.IsEffectivelyVisible && c.IsEffectivelyEnabled)?.Focus();
                Opened?.Invoke(this, EventArgs.Empty);
            }, DispatcherPriority.Loaded);
            var result = await completion.Task;
            return result is T typed ? typed : default;
        }
        finally
        {
            owner.Closed -= OwnerClosed;
            owner.KeyDown -= HandleKeys;
            layer.Children.Remove(shield);
            if (previousSheet is not null) previousSheet.IsEnabled = true;
            else layout.IsEnabled = true;
            if (owner.IsActive && owner.IsVisible) previous?.Focus();
        }
    }
    public async Task ShowDialog(Window owner) => await ShowDialog<object>(owner);
}
