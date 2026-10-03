using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;

namespace Jourfold.Desktop;

/// <summary>
/// Small factory kit for code-built views. Every color comes from a theme token so Light, Dark and
/// high contrast stay consistent; no view chooses raw colors.
/// </summary>
public static class Ui
{
    public static T Res<T>(this T control, AvaloniaProperty property, string key) where T : Control
    {
        control.Bind(property, control.GetResourceObservable(key));
        return control;
    }
    public static T Classed<T>(this T control, params string[] classes) where T : StyledElement
    {
        foreach (var c in classes) if (c.Length > 0) control.Classes.Add(c);
        return control;
    }
    public static T Named<T>(this T control, string name) where T : Control
    {
        AutomationProperties.SetName(control, name);
        if (control is not TextBlock) ToolTip.SetTip(control, name);
        return control;
    }
    public static T Margin<T>(this T control, double l, double t = double.NaN, double r = double.NaN, double b = double.NaN) where T : Control
    {
        control.Margin = double.IsNaN(t) ? new Thickness(l) : new Thickness(l, t, double.IsNaN(r) ? l : r, double.IsNaN(b) ? t : b);
        return control;
    }
    public static T Col<T>(this T control, int column, int span = 1) where T : Control { Grid.SetColumn(control, column); if (span > 1) Grid.SetColumnSpan(control, span); return control; }
    public static T Row<T>(this T control, int row, int span = 1) where T : Control { Grid.SetRow(control, row); if (span > 1) Grid.SetRowSpan(control, span); return control; }

    public static TextBlock Text(string? text, params string[] classes)
    {
        var block = new TextBlock { Text = text ?? "", TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        return block.Classed(classes);
    }
    public static TextBlock Line(string? text, params string[] classes)
    {
        var block = new TextBlock { Text = text ?? "", TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = TextWrapping.NoWrap, VerticalAlignment = VerticalAlignment.Center };
        ToolTip.SetTip(block, text);
        return block.Classed(classes);
    }
    public static Desktop.Icon Icon(string kind, double size = 16, string? brush = null)
    {
        var icon = new Desktop.Icon(kind, size) { VerticalAlignment = VerticalAlignment.Center };
        return brush is null ? icon : icon.Res(Desktop.Icon.ForegroundProperty, brush);
    }
    public static StackPanel V(double spacing, params Control?[] children)
    {
        var panel = new StackPanel { Spacing = spacing };
        foreach (var c in children) if (c is not null) panel.Children.Add(c);
        return panel;
    }
    public static StackPanel H(double spacing, params Control?[] children)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = spacing };
        foreach (var c in children) if (c is not null) { c.VerticalAlignment = c.VerticalAlignment == VerticalAlignment.Stretch ? VerticalAlignment.Center : c.VerticalAlignment; panel.Children.Add(c); }
        return panel;
    }
    /// <summary>A grid whose children are placed in consecutive columns.</summary>
    public static Grid Columns(string definitions, params Control?[] children)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions(definitions) };
        var index = 0;
        foreach (var c in children) { if (c is not null) { if (Grid.GetColumn(c) == 0) Grid.SetColumn(c, index); grid.Children.Add(c); } index++; }
        return grid;
    }
    public static Border Card(Control child, double padding = 16, string cls = "card") => new Border { Child = child, Padding = new Thickness(padding) }.Classed(cls);
    public static Border Divider() => new Border().Classed("divider");

    public static Button Button(string text, string? icon = null, params string[] classes)
    {
        var button = new Button { Content = icon is null ? text : H(7, Icon(icon, 15), new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center }) };
        AutomationProperties.SetName(button, text);
        return button.Classed(classes);
    }
    public static Button IconButton(string icon, string label, params string[] classes)
    {
        var button = new Button { Content = Icon(icon, 16) }.Classed("icon").Classed(classes);
        return button.Named(label);
    }

    public static Border Pill(string text, string? icon, string background, string foreground)
    {
        var content = H(5, icon is null ? null : Icon(icon, 12).Res(Desktop.Icon.ForegroundProperty, foreground), Text(text, "caption").Res(TextBlock.ForegroundProperty, foreground));
        if (content.Children.LastOrDefault() is TextBlock t) { t.FontWeight = FontWeight.SemiBold; t.FontSize = 11.5; t.TextWrapping = TextWrapping.NoWrap; }
        return new Border { Child = content, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left }.Classed("pill").Res(Border.BackgroundProperty, background);
    }

    /// <summary>A colored square with an icon, used for entity kinds.</summary>
    public static Border Tile(string icon, string kindKey, double size = 32)
    {
        return new Border
        {
            Width = size, Height = size, CornerRadius = new CornerRadius(size / 4),
            Child = Icon(icon, size * 0.5, "Kind." + kindKey + ".Fg").Also(i => i.HorizontalAlignment = HorizontalAlignment.Center),
            VerticalAlignment = VerticalAlignment.Center
        }.Res(Border.BackgroundProperty, "Kind." + kindKey + ".Bg");
    }

    private static readonly string[] AvatarColors = ["#14968F", "#3B7DD8", "#7C5CD6", "#E2843A", "#36A464", "#C9558E", "#6366D1", "#D9605F", "#4F86A6"];
    public static string Initials(string name)
    {
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) return "?";
        return parts.Length == 1 ? parts[0][..Math.Min(2, parts[0].Length)].ToUpperInvariant() : (parts[0][..1] + parts[^1][..1]).ToUpperInvariant();
    }
    public static Border Avatar(string name, double size = 28, IImage? image = null)
    {
        var hash = 0; foreach (var ch in name) hash = unchecked(hash * 31 + ch);
        var color = Color.Parse(AvatarColors[Math.Abs(hash % AvatarColors.Length)]);
        Control content = image is not null
            ? new Image { Source = image, Stretch = Stretch.UniformToFill }
            : new TextBlock { Text = Initials(name), Foreground = Brushes.White, FontSize = Math.Max(9, size * 0.38), FontWeight = FontWeight.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var avatar = new Border { Width = size, Height = size, CornerRadius = new CornerRadius(size / 2), Background = new SolidColorBrush(color), Child = content, ClipToBounds = true, VerticalAlignment = VerticalAlignment.Center };
        avatar.Res(Border.BorderBrushProperty, "Bg.Surface"); avatar.BorderThickness = new Thickness(size >= 24 ? 2 : 1.5);
        ToolTip.SetTip(avatar, name); AutomationProperties.SetName(avatar, name);
        return avatar;
    }
    public static Control AvatarStack(IEnumerable<string> names, double size = 24, int max = 4)
    {
        var list = names.ToArray(); var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = -size * 0.3 };
        foreach (var name in list.Take(max)) panel.Children.Add(Avatar(name, size));
        if (list.Length > max)
            panel.Children.Add(new Border { Width = size, Height = size, CornerRadius = new CornerRadius(size / 2), Child = Text("+" + (list.Length - max), "caption").Also(t => { t.FontSize = size * 0.38; t.FontWeight = FontWeight.Bold; t.HorizontalAlignment = HorizontalAlignment.Center; }) }.Res(Border.BackgroundProperty, "Bg.Muted"));
        AutomationProperties.SetName(panel, string.Join(", ", list));
        return panel;
    }

    public static Control EmptyState(string icon, string title, string body, params Control[] actions)
    {
        var circle = new Border { Width = 64, Height = 64, CornerRadius = new CornerRadius(32), Child = Icon(icon, 28, "Accent").Also(i => i.HorizontalAlignment = HorizontalAlignment.Center), HorizontalAlignment = HorizontalAlignment.Center }.Res(Border.BackgroundProperty, "Accent.Soft");
        var panel = V(10, circle, Text(title, "h3").Also(t => t.TextAlignment = TextAlignment.Center), Text(body, "muted").Also(t => { t.TextAlignment = TextAlignment.Center; t.MaxWidth = 420; }));
        if (actions.Length > 0) panel.Children.Add(H(8, actions).Also(h => { h.HorizontalAlignment = HorizontalAlignment.Center; h.Margin = new Thickness(0, 8, 0, 0); }));
        panel.HorizontalAlignment = HorizontalAlignment.Center; panel.VerticalAlignment = VerticalAlignment.Center; panel.Margin = new Thickness(24, 48);
        return panel;
    }

    public static Control Section(string title, Control content, Control? action = null)
    {
        var header = Columns("*,Auto", Text(title.ToUpperInvariant(), "overline"), action);
        return V(8, header, content);
    }
    public static Control Field(string label, Control value) => V(5, Text(label, "label"), value);

    public static Control Scroll(Control content, double padding = 0) => new ScrollViewer { Content = padding > 0 ? new Border { Padding = new Thickness(padding), Child = content } : content, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };

    public static T? FindDescendant<T>(this Visual root, Func<T, bool> predicate) where T : Visual => Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(root).OfType<T>().FirstOrDefault(predicate);
    public static T Also<T>(this T value, Action<T> configure) { configure(value); return value; }

    public static IBrush? Brush(StyledElement element, string key) => element.TryFindResource(key, element.ActualThemeVariant, out var value) ? value as IBrush : null;
}
