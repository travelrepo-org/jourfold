using System.Globalization;
using TravelRepo.Core;

namespace Jourfold.UI;

/// <summary>Currency codes to suggest while typing one: those of the world's regions and any the exchange rates know.</summary>
public static class CurrencyCodes
{
    private static readonly string[] Regions = CultureInfo.GetCultures(CultureTypes.SpecificCultures)
        .Select(c => { try { return new RegionInfo(c.Name).ISOCurrencySymbol; } catch (ArgumentException) { return null; } })
        .OfType<string>().Where(IsCode).Distinct().ToArray();

    /// <summary>All known codes, sorted.</summary>
    public static IReadOnlyList<string> Known(ExchangeRates? rates) =>
        Regions.Concat(rates?.Currencies ?? []).Where(IsCode).Distinct().Order(StringComparer.Ordinal).ToArray();

    /// <summary>
    /// The currency of the country a time zone belongs to, such as NZD for Pacific/Auckland, or null for a zone of no
    /// single country (UTC). Phones set their zone from the network when travelling, so the phone's zone tells the
    /// local money without asking for the location. From the time zone database's zone.tab, offline.
    /// </summary>
    public static string? OfZone(NodaTime.DateTimeZone zone)
    {
        var source = NodaTime.TimeZones.TzdbDateTimeZoneSource.Default;
        var id = source.CanonicalIdMap.TryGetValue(zone.Id, out var canonical) ? canonical : zone.Id;
        if (source.ZoneLocations?.FirstOrDefault(l => l.ZoneId == id)?.CountryCode is not { Length: 2 } country) return null;
        try { return new RegionInfo(country).ISOCurrencySymbol is var code && IsCode(code) ? code : null; }
        catch (ArgumentException) { return null; }
    }

    /// <summary>A typed code such as "usd", as an upper-case three-letter code, or null when it is none.</summary>
    public static string? Read(string? text) => text?.Trim().ToUpperInvariant() is { } code && IsCode(code) ? code : null;

    private static bool IsCode(string code) => code.Length == 3 && code.All(c => c is >= 'A' and <= 'Z');
}
