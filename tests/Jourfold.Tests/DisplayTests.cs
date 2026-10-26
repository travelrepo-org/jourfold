using System.Globalization;
using Jourfold.Desktop;
using Xunit;
namespace Jourfold.Tests;

public class DisplayTests
{
    [Fact]
    public void UnitsAndLocaleDoNotChangeCanonicalValues()
    { Assert.Equal("1,61 km", DisplayValues.Distance(1609.344, false, CultureInfo.GetCultureInfo("de-DE"))); Assert.Equal("1 mi", DisplayValues.Distance(1609.344, true, CultureInfo.GetCultureInfo("en-US"))); Assert.Equal(1609.344, DisplayValues.ParseDistance("1 mi", true, CultureInfo.InvariantCulture), 6); }

    [Fact]
    public void ShortcutsReadAsTheSystemWritesThem()
    {
        Assert.Equal("Ctrl+Shift+Z", Shortcuts.Label("Ctrl+Shift+Z", mac: false));
        Assert.Equal("⇧⌘Z", Shortcuts.Label("Ctrl+Shift+Z", mac: true));
        Assert.Equal("⌘,", Shortcuts.Label("Ctrl+,", mac: true));
        Assert.Equal("⌘K", Shortcuts.Label("Ctrl+K", mac: true));
    }
}
