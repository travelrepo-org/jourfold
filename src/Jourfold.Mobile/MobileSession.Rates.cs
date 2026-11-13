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

    /// <summary>
    /// Other currencies chosen in the converter lately, newest first, so one picked under Other stays to tap after
    /// switching to another. Kept on this device only.
    /// </summary>
    public IReadOnlyList<string> RecentCurrencies => (Settings.Values.GetValueOrDefault("converter.recent") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries);

    /// <summary>Chooses a currency on one side of the converter; one the trip does not offer is kept among the recent ones.</summary>
    public void ChooseConverterCurrency(string side, string code)
    {
        Drafts[side] = code;
        if (!TripCurrencies().Contains(code))
        {
            Settings.Values["converter.recent"] = string.Join(',', RecentCurrencies.Where(c => c != code).Prepend(code).Take(4));
            Settings.Save();
        }
        Redraw();
    }

    /// <summary>Takes a currency off the recent ones, from a long press on its chip.</summary>
    public void ForgetConverterCurrency(string code)
    {
        Settings.Values["converter.recent"] = string.Join(',', RecentCurrencies.Where(c => c != code));
        Settings.Save(); Redraw();
    }

    /// <summary>
    /// The currencies to offer first: the trip's own, the ones its costs are in, and the phone's region's. No currency
    /// is offered just because the rates are quoted against it.
    /// </summary>
    public IReadOnlyList<string> TripCurrencies()
    {
        var region = System.Globalization.RegionInfo.CurrentRegion.ISOCurrencySymbol;
        return (Trip is { } trip ? TravelRepo.Core.TripSummaries.Costs(trip).Select(c => c.Currency).Prepend(TripCurrencyCode) : []).Append(region)
            .Where(c => c.Length == 3).Distinct().ToArray();
    }
}
