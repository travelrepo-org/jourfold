using System.Globalization;
using System.Text.RegularExpressions;

namespace Jourfold.Application;

/// <summary>
/// Reads coordinates as people type or paste them. A point or a comma both work as decimal separator, because
/// coordinates never use thousands separators, and pairs such as "-37.0093, 174.7841" (as copied from map services)
/// split into latitude and longitude.
/// </summary>
public static partial class Coordinates
{
    /// <summary>Parses one value in decimal degrees, for example "-37.0093" or "-37,0093".</summary>
    public static bool TryParse(string? text, out double value)
    {
        value = 0; text = text?.Trim();
        if (string.IsNullOrEmpty(text) || !Single().IsMatch(text)) return false;
        return double.TryParse(text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out value) && double.IsFinite(value);
    }

    public static bool IsLatitude(double value) => value is >= -90 and <= 90;
    public static bool IsLongitude(double value) => value is >= -180 and <= 180;

    /// <summary>
    /// Parses a latitude and longitude pair separated by a comma, semicolon or space, optionally in brackets:
    /// "-37.0093, 174.7841", "(-37.0093; 174.7841)" or "-37,0093 174,7841". Both values must be in range.
    /// </summary>
    public static bool TryParsePair(string? text, out double latitude, out double longitude)
    {
        latitude = longitude = 0;
        var match = Pair().Match(text?.Trim() ?? "");
        return match.Success && TryParse(match.Groups["lat"].Value, out latitude) && TryParse(match.Groups["lon"].Value, out longitude)
            && IsLatitude(latitude) && IsLongitude(longitude);
    }

    /// <summary>
    /// Reads a position from a map link or a geo URI as map apps share them: Google Maps (<c>@lat,lon</c>,
    /// <c>q=</c>, <c>query=</c>, <c>ll=</c>, <c>!3d…!4d…</c>), Apple Maps (<c>ll=</c>, <c>q=</c>), OpenStreetMap
    /// (<c>mlat</c>/<c>mlon</c>, <c>#map=zoom/lat/lon</c>) and <c>geo:lat,lon</c>. Short links that need the network to
    /// resolve, such as maps.app.goo.gl, are not read. A plain pair works too.
    /// </summary>
    public static bool TryParseLink(string? text, out double latitude, out double longitude)
    {
        latitude = longitude = 0; text = text?.Trim();
        if (string.IsNullOrEmpty(text)) return false;
        if (TryParsePair(text, out latitude, out longitude)) return true;
        var decoded = Uri.UnescapeDataString(text.Replace('+', ' '));
        foreach (var pattern in Links())
            if (pattern.Match(decoded) is { Success: true } m
                && double.TryParse(m.Groups["lat"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out latitude)
                && double.TryParse(m.Groups["lon"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out longitude)
                && IsLatitude(latitude) && IsLongitude(longitude)) return true;
        latitude = longitude = 0; return false;
    }

    private const string Number = @"[+-]?\d{1,3}(?:\.\d+)?";
    private static Regex[] Links() =>
    [
        // A place's own position comes before the map's centre in Google links.
        new(@"!3d(?<lat>" + Number + @")!4d(?<lon>" + Number + ")"),
        new(@"^geo:(?<lat>" + Number + @"),(?<lon>" + Number + ")"),
        new(@"[?&](?:q|query|ll|sll|daddr|destination)=(?<lat>" + Number + @")\s*,\s*(?<lon>" + Number + ")"),
        new(@"[?&]mlat=(?<lat>" + Number + @")&mlon=(?<lon>" + Number + ")"),
        new(@"#map=\d+/(?<lat>" + Number + @")/(?<lon>" + Number + ")"),
        new(@"@(?<lat>" + Number + @"),(?<lon>" + Number + ")")
    ];

    [GeneratedRegex(@"^[+-]?\d{1,3}([.,]\d+)?$")]
    private static partial Regex Single();

    // Separated by ";", by a comma followed by a space, or by whitespace, so "-37,0093 174,7841" works; a bare comma
    // only separates two values written with decimal points ("-37.0093,174.7841"), never "12,5".
    [GeneratedRegex(@"^\(?\s*(?:(?<lat>[+-]?\d{1,3}(?:[.,]\d+)?)\s*(?:;\s*|,\s+|\s+)(?<lon>[+-]?\d{1,3}(?:[.,]\d+)?)|(?<lat>[+-]?\d{1,3}\.\d+),(?<lon>[+-]?\d{1,3}\.\d+))\s*\)?$")]
    private static partial Regex Pair();
}
