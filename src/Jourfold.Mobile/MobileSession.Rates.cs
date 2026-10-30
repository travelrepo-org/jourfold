using Jourfold.Application;

namespace Jourfold.Mobile;

public sealed partial class MobileSession
{
    private ExchangeRates? rates;
    private string RatesPath => Path.Combine(Host.DataRoot, "rates.json");

    /// <summary>Whether exchange rates are fetched from the European Central Bank; off until the user says yes once.</summary>
    public bool RatesOn => Settings.Values.GetValueOrDefault("rates.online") == "on";
    /// <summary>The rates kept on this device, from the last fetch; they work offline.</summary>
    public ExchangeRates? Rates => rates ??= ExchangeRates.Load(RatesPath);
    public bool FetchingRates { get; private set; }

    /// <summary>Allows fetching rates, then fetches them.</summary>
    public async Task TurnOnRatesAsync(CancellationToken ct = default)
    {
        Settings.Values["rates.online"] = "on"; Settings.Save();
        await FetchRatesAsync(force: true, ct);
    }

    /// <summary>Fetches today's rates when allowed and not fetched today; with every sync and when the converter opens.</summary>
    public async Task FetchRatesAsync(bool force = false, CancellationToken ct = default)
    {
        if (!RatesOn || FetchingRates || !force && Rates?.Date >= DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1))) return;
        FetchingRates = true; Changed?.Invoke();
        try
        {
            var fresh = await ExchangeRates.FetchAsync(MapsHttp, ct);
            fresh.Save(RatesPath); rates = fresh;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or System.Xml.XmlException or FormatException or InvalidOperationException) { }
        finally { FetchingRates = false; Changed?.Invoke(); }
    }

    /// <summary>The currencies to offer first: the trip's, the euro and the phone's region's.</summary>
    public IReadOnlyList<string> TripCurrencies()
    {
        var region = System.Globalization.RegionInfo.CurrentRegion.ISOCurrencySymbol;
        return (Trip is { } trip ? TravelRepo.Core.TripSummaries.Costs(trip).Select(c => c.Currency) : []).Append("EUR").Append(region)
            .Where(c => c.Length == 3).Distinct().ToArray();
    }
}
