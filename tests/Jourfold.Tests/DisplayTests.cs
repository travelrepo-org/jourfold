using System.Globalization;
using Avalonia.Input;
using Jourfold.Desktop;
using Jourfold.UI;
using NodaTime;
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

    [Fact]
    public void GermanDatesHaveAFullStopAfterTheDay()
    {
        var before = CultureInfo.CurrentCulture;
        var date = new LocalDate(2027, 6, 4);
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            Assert.StartsWith("Freitag, 4. Juni", Formats.LongDay(date));
            Assert.StartsWith("4. Jun", Formats.Date(date));
            Assert.StartsWith("4. Jun", Formats.ShortDate(date));
            Assert.StartsWith("4. Jun", Formats.DayNumber(date));
            Assert.StartsWith("4.–6. Jun", Formats.DateRange(date, date.PlusDays(2)));
            var notice = date.At(new LocalTime(9, 0)).InUtc().ToString(new Localization("de")["DayShortFormat"] + " HH:mm", CultureInfo.GetCultureInfo("de-DE"));
            Assert.Contains(", 4. Jun", notice);
            Assert.EndsWith(" 09:00", notice);
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-GB");
            Assert.Equal("Friday, 4 June", Formats.LongDay(date));
            Assert.Equal("4–6 Jun 2027", Formats.DateRange(date, date.PlusDays(2)));
        }
        finally { CultureInfo.CurrentCulture = before; }
    }

    [Fact]
    public void MenuShortcutsBecomeRealGesturesOnEverySystem()
    {
        Assert.Equal(new KeyGesture(Key.Z, KeyModifiers.Control | KeyModifiers.Shift), Shortcuts.Gesture("Ctrl+Shift+Z", mac: false));
        Assert.Equal(new KeyGesture(Key.Z, KeyModifiers.Meta | KeyModifiers.Shift), Shortcuts.Gesture("Ctrl+Shift+Z", mac: true));
        Assert.Equal(new KeyGesture(Key.Z, KeyModifiers.Meta), Shortcuts.Gesture("Ctrl+Z", mac: true));
        Assert.Equal(new KeyGesture(Key.F5), Shortcuts.Gesture("F5", mac: true));
    }
}
