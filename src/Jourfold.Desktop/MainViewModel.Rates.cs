using Jourfold.Application;
using Jourfold.Infrastructure;
using TravelRepo.Core;

namespace Jourfold.Desktop;

/// <summary>
/// Exchange rates for the currency converter, off until the person turns them on, as on the phone. Jourfold fetches
/// the European Central Bank's reference rates at most once a day, when a trip opens or syncs, and keeps them for use
/// offline. AI assistants read the same file and never fetch.
/// </summary>
public partial class MainViewModel
{
    private ExchangeRates? rates;
    private bool fetchingRates;
    private readonly RateSchedule rateSchedule = new();

    /// <summary>Fetches rates; tests and screenshots replace it with one that never goes online.</summary>
    public Func<CancellationToken, Task<ExchangeRates>>? RatesSource { get; set; }

    public bool RatesOn => Store.Get("rates.online") == "true";
    public static string RatesFile(LocalStore store) => Path.Combine(store.Root, "rates.json");
    /// <summary>The rates kept on this computer from the last fetch; they work offline.</summary>
    public ExchangeRates? Rates => rates ??= ExchangeRates.Load(RatesFile(Store));
    public bool FetchingRates => fetchingRates;

    /// <summary>The rates file for AI assistants (<c>--mcp</c>), or null while rates are off.</summary>
    public static string? RatesForAssistants(string? storeRoot = null)
    {
        using var store = new LocalStore(storeRoot);
        return store.Get("rates.online") == "true" && File.Exists(RatesFile(store)) ? RatesFile(store) : null;
    }

    public async Task SetRatesAsync(bool enabled)
    {
        Store.Set("rates.online", enabled ? "true" : "false"); OnPropertyChanged(nameof(RatesOn));
        if (enabled) await FetchRatesAsync(force: true); else ContentChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>When the rates on this computer were fetched, or null before the first fetch.</summary>
    public DateTimeOffset? RatesFetched => RateSchedule.Fetched(RatesFile(Store));

    /// <summary>Fetches the newest rates when allowed and due; see <see cref="RateSchedule"/>.</summary>
    public async Task FetchRatesAsync(bool force = false)
    {
        if (!RatesOn || fetchingRates || !rateSchedule.Begin(Rates, RatesFile(Store), force)) return;
        await FetchRatesCoreAsync();
    }

    /// <summary>
    /// Fetches the rates again now because the person asked for it. False when that failed, such as offline; the last
    /// rates stay. Asking again within a minute does nothing.
    /// </summary>
    public async Task<bool> RefreshRatesAsync()
    {
        if (!RatesOn || fetchingRates || !rateSchedule.BeginRefresh()) return true;
        return await FetchRatesCoreAsync();
    }

    private async Task<bool> FetchRatesCoreAsync()
    {
        fetchingRates = true;
        try
        {
            var fresh = RatesSource is { } source ? await source(CancellationToken.None) : await ExchangeRates.FetchAsync(weatherHttp, OnlineIdentity.UserAgent);
            fresh.Save(RatesFile(Store)); rates = fresh;
            return true;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or System.Xml.XmlException or FormatException or InvalidOperationException) { return false; }
        finally { fetchingRates = false; ContentChanged?.Invoke(this, EventArgs.Empty); }
    }

    /// <summary>The currencies to offer first: the trip's, the person's own and the euro.</summary>
    public IReadOnlyList<string> TripCurrencies() =>
        (Trip is { } trip ? TripSummaries.Costs(trip).Select(c => c.Currency) : []).Append(Store.Get("currency") ?? "").Append("EUR")
            .Where(c => c.Length == 3).Distinct().ToArray();
}
