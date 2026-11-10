using Jourfold.Application;
using TravelRepo.Core;

namespace Jourfold.Mobile;

public sealed partial class MobileSession
{
    private ExchangeRates? rates;
    private readonly RateSchedule rateSchedule = new();
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

    /// <summary>When the rates on this device were fetched, or null before the first fetch.</summary>
    public DateTimeOffset? RatesFetched => RateSchedule.Fetched(RatesPath);

    /// <summary>Fetches the newest rates when allowed and due, with every sync and when the converter opens; see <see cref="RateSchedule"/>.</summary>
    public async Task FetchRatesAsync(bool force = false, CancellationToken ct = default)
    {
        if (!RatesOn || FetchingRates || !rateSchedule.Begin(Rates, RatesPath, force)) return;
        await FetchRatesCoreAsync(ct);
    }

    /// <summary>
    /// Fetches the rates again now because the user asked for it. False when that failed, such as offline; the last
    /// rates stay. Asking again within a minute does nothing.
    /// </summary>
    public async Task<bool> RefreshRatesAsync(CancellationToken ct = default)
    {
        if (!RatesOn || FetchingRates || !rateSchedule.BeginRefresh()) return true;
        return await FetchRatesCoreAsync(ct);
    }

    private async Task<bool> FetchRatesCoreAsync(CancellationToken ct)
    {
        FetchingRates = true; Changed?.Invoke();
        try
        {
            var fresh = await ExchangeRates.FetchAsync(MapsHttp, OnlineIdentity.UserAgent, ct);
            fresh.Save(RatesPath); rates = fresh;
            return true;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or System.Xml.XmlException or FormatException or InvalidOperationException) { return false; }
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
