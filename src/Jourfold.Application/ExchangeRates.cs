using System.Globalization;
using System.Text.Json;
using System.Xml.Linq;

namespace Jourfold.Application;

/// <summary>
/// Reference rates against the euro on one day, as the European Central Bank publishes them for about 30 currencies.
/// Converting between two currencies goes through the euro. They are for orientation: card and cash rates differ.
/// </summary>
public sealed record ExchangeRates(DateOnly Date, IReadOnlyDictionary<string, decimal> PerEuro)
{
    public const string Source = "https://www.ecb.europa.eu/stats/eurofxref/eurofxref-daily.xml";

    public IEnumerable<string> Currencies => PerEuro.Keys.Append("EUR").Distinct().Order(StringComparer.Ordinal);

    /// <summary><paramref name="amount"/> in <paramref name="from"/> as <paramref name="to"/>; null when a currency is not known.</summary>
    public decimal? Convert(decimal amount, string from, string to)
    {
        decimal? Rate(string code) => code == "EUR" ? 1m : PerEuro.TryGetValue(code, out var rate) && rate > 0 ? rate : null;
        return Rate(from) is { } a && Rate(to) is { } b ? amount / a * b : null;
    }

    /// <summary>Reads the ECB's daily reference rates (XML).</summary>
    public static ExchangeRates Parse(string xml)
    {
        var day = XDocument.Parse(xml).Descendants().First(e => e.Name.LocalName == "Cube" && e.Attribute("time") is not null);
        var rates = day.Elements().Where(e => e.Attribute("currency") is not null)
            .ToDictionary(e => e.Attribute("currency")!.Value, e => decimal.Parse(e.Attribute("rate")!.Value, CultureInfo.InvariantCulture));
        return new(DateOnly.ParseExact(day.Attribute("time")!.Value, "yyyy-MM-dd", CultureInfo.InvariantCulture), rates);
    }

    /// <summary>Fetches today's rates; the request carries no trip data.</summary>
    public static async Task<ExchangeRates> FetchAsync(HttpClient http, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, Source); OnlineIdentity.Apply(request);
        using var response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return Parse(await response.Content.ReadAsStringAsync(ct));
    }

    /// <summary>The rates kept on the device from the last fetch, or null.</summary>
    public static ExchangeRates? Load(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            var saved = JsonSerializer.Deserialize<Saved>(File.ReadAllText(path));
            return saved is null ? null : new(DateOnly.ParseExact(saved.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture), saved.PerEuro);
        }
        catch (Exception ex) when (ex is JsonException or FormatException or IOException) { return null; }
    }

    public void Save(string path) => File.WriteAllText(path, JsonSerializer.Serialize(new Saved(Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), PerEuro.ToDictionary())));

    private sealed record Saved(string Date, Dictionary<string, decimal> PerEuro);
}
