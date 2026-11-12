using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Jourfold.Desktop;
using Xunit;

namespace Jourfold.Tests;

/// <summary>The mouse wheel scrolls the page, also over a date, amount or currency that was just changed.</summary>
public sealed class WheelTests
{
    [AvaloniaFact]
    public void TheWheelOverADateAmountOrChoiceScrollsThePage()
    {
        var date = new DateField { SelectedDate = new DateTime(2026, 11, 12), Width = 200 };
        var amount = new NumericUpDown { Value = 12.5m, Increment = 1, ShowButtonSpinner = false, Width = 200 };
        var choice = new ComboBox { ItemsSource = new[] { "EUR", "NZD", "JPY" }, SelectedIndex = 1, Width = 200 };
        var column = new StackPanel { Spacing = 8 };
        column.Children.Add(date); column.Children.Add(amount); column.Children.Add(choice); column.Children.Add(new Border { Height = 2000 });
        var scroll = new ScrollViewer { Content = column };
        var w = new Window { Width = 400, Height = 300, Content = scroll };
        w.Show(); Probe.Layout();
        try
        {
            foreach (var field in new Control[] { date, amount, choice })
            {
                field.Focus(); Probe.Layout();
                var before = scroll.Offset.Y;
                var at = field.TranslatePoint(new Point(20, 10), w)!.Value;
                w.MouseWheel(at, new Vector(0, -1)); Probe.Layout();
                Assert.Equal(new DateTime(2026, 11, 12), date.SelectedDate);
                Assert.Equal(12.5m, amount.Value);
                Assert.Equal(1, choice.SelectedIndex);
                Assert.True(scroll.Offset.Y > before, field.GetType().Name + " kept the wheel");
                scroll.Offset = default; Probe.Layout();
            }
        }
        finally { w.Close(); }
    }
}
