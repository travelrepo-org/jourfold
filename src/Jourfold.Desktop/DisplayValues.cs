using System.Globalization;
namespace Jourfold.Desktop;

public static class DisplayValues
{
    public static string Distance(double meters, bool imperial, CultureInfo? culture = null) => (meters / (imperial ? 1609.344 : 1000)).ToString("0.##", culture ?? CultureInfo.CurrentCulture) + (imperial ? " mi" : " km");
    public static double ParseDistance(string value, bool imperial, CultureInfo? culture = null) => double.Parse(value.Replace(" km", "").Replace(" mi", ""), culture ?? CultureInfo.CurrentCulture) * (imperial ? 1609.344 : 1000);
}
