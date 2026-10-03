using System.Globalization;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.VisualTree;
namespace Jourfold.Desktop;

public sealed class TooltipTextConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is Control control ? string.Join(" · ", control.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text)) : value?.ToString();
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
