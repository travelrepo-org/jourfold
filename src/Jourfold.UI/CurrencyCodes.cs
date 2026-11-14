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

    /// <summary>A typed code such as "usd", as an upper-case three-letter code, or null when it is none.</summary>
    public static string? Read(string? text) => text?.Trim().ToUpperInvariant() is { } code && IsCode(code) ? code : null;

    private static bool IsCode(string code) => code.Length == 3 && code.All(c => c is >= 'A' and <= 'Z');
}
