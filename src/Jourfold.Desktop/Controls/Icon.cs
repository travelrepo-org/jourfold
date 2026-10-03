using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;

namespace Jourfold.Desktop;

/// <summary>
/// Strokes a Lucide icon geometry (24 x 24 grid) with the inherited foreground. Icons are decorative;
/// the owning control carries the accessible name.
/// </summary>
public sealed class Icon : Control
{
    public static readonly StyledProperty<string?> KindProperty = AvaloniaProperty.Register<Icon, string?>(nameof(Kind));
    public static readonly StyledProperty<IBrush?> ForegroundProperty = TextElement.ForegroundProperty.AddOwner<Icon>();
    public static readonly StyledProperty<double> StrokeWidthProperty = AvaloniaProperty.Register<Icon, double>(nameof(StrokeWidth), 2);
    private Geometry? geometry;

    static Icon()
    {
        AffectsRender<Icon>(KindProperty, ForegroundProperty, StrokeWidthProperty);
        Avalonia.Automation.AutomationProperties.AccessibilityViewProperty.OverrideDefaultValue<Icon>(Avalonia.Automation.AccessibilityView.Raw);
    }
    public Icon() { IsHitTestVisible = false; }
    public Icon(string kind, double size = 16) : this() { Kind = kind; Width = size; Height = size; }
    public string? Kind { get => GetValue(KindProperty); set => SetValue(KindProperty, value); }
    public IBrush? Foreground { get => GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }
    public double StrokeWidth { get => GetValue(StrokeWidthProperty); set => SetValue(StrokeWidthProperty, value); }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == KindProperty) geometry = null;
    }
    protected override Size MeasureOverride(Size availableSize) => new(double.IsNaN(Width) ? 16 : Width, double.IsNaN(Height) ? 16 : Height);
    public override void Render(DrawingContext context)
    {
        if (Kind is null || Foreground is null) return;
        geometry ??= this.TryFindResource("Icon." + Kind, out var resource) ? resource as Geometry : null;
        if (geometry is null) return;
        var scale = Math.Min(Bounds.Width, Bounds.Height) / 24;
        if (scale <= 0) return;
        using (context.PushTransform(Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation((Bounds.Width - 24 * scale) / 2, (Bounds.Height - 24 * scale) / 2)))
            context.DrawGeometry(null, new Pen(Foreground, StrokeWidth, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), geometry);
    }
}
