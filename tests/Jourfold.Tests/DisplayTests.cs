using System.Globalization;
using Jourfold.Desktop;
using Xunit;
namespace Jourfold.Tests;

public class DisplayTests
{
    [Fact]
    public void UnitsAndLocaleDoNotChangeCanonicalValues()
    { Assert.Equal("1,61 km", DisplayValues.Distance(1609.344, false, CultureInfo.GetCultureInfo("de-DE"))); Assert.Equal("1 mi", DisplayValues.Distance(1609.344, true, CultureInfo.GetCultureInfo("en-US"))); Assert.Equal(1609.344, DisplayValues.ParseDistance("1 mi", true, CultureInfo.InvariantCulture), 6); }
}
