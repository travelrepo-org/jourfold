using NodaTime;
using TravelRepo.Core;
using TravelRepo.Repository;

namespace Jourfold.Mobile;

/// <summary>
/// Costs on the phone and the tablet: paying back, payments still to pay, the trip's currency and the upgrade of
/// costs older versions wrote. The rules live in TravelRepo's <see cref="Expenses"/>, <see cref="Budgets"/> and
/// <see cref="CostUpgrade"/>.
/// </summary>
public sealed partial class MobileSession
{
    private LocalDate TripToday(TripSnapshot trip) => Now.InZone(ScheduleQueries.TripZone(trip)).Date;

    /// <summary>The currency the travellers count in, from the trip; until it is chosen, the one the costs suggest or EUR.</summary>
    public string TripCurrencyCode => Trip is { } trip ? TripCurrency.Of(trip) ?? "EUR" : "EUR";

    /// <summary>Reference rates for estimates where no charged amount was entered, only when fetching them is allowed.</summary>
    public ExchangeRates? EstimateRates => RatesOn ? Rates : null;

    /// <summary>
    /// Marks what one person owes another for an expense as paid back today in the trip's time zone, or takes the
    /// mark back.
    /// </summary>
    public Task SetDebtPaidAsync(DebtKey key, bool paid, CancellationToken ct = default) => ChangeAsync(async () =>
    {
        if (Workspace is not { } workspace || workspace.State.Trip.Find(key.Expense) is not { Type: "expense" } expense) return;
        var trip = workspace.State.Trip;
        await workspace.EditAsync(paid ? Expenses.Settle(trip, [key], TripToday(trip)).Single() : Expenses.Unsettle(expense, key.From, key.To), ct); NoteChange();
    }, ct);

    /// <summary>Marks a payment that was still to pay as paid, by the local user when it names nobody yet.</summary>
    public Task MarkPaymentPaidAsync(Guid expenseId, CancellationToken ct = default) => ChangeAsync(async () =>
    {
        if (Workspace is not { } workspace || workspace.State.Trip.Find(expenseId) is not { Type: "expense" } expense) return;
        await workspace.EditAsync(Expenses.MarkPaid(expense, Me is { } me ? [me.Id] : null), ct); NoteChange();
    }, ct);

    /// <summary>After everyone paid what the suggestion says, marks every open item of the trip as paid back today.</summary>
    public Task SettleAllAsync(CancellationToken ct = default) => ChangeAsync(async () =>
    {
        if (Workspace is not { } workspace || Expenses.Open(workspace.State.Trip).Count == 0) return;
        var trip = workspace.State.Trip;
        await workspace.ApplyAsync(Expenses.SettleAll(trip, TripToday(trip)).Select(e => new EntityEdit(e.Id, e)).ToArray(), ct); NoteChange();
    }, ct);

    /// <summary>Asks before marking everything as paid back, since it is meant for after everyone paid.</summary>
    public void ConfirmSettleAll() => ShowMenu(new ActionMenu(Strings["SettleAllTitle"], Strings["SettleAllBody"],
        [new MenuAction(Strings["SettleAll"], "circle-check", _ => SettleAllAsync())]));

    /// <summary>What upgrading this trip's costs would change; empty when nothing needs it.</summary>
    public CostUpgradePlan CostUpgradePlan => Trip is { } trip ? CostUpgrade.Plan(trip, RegionCurrency()) : new([], []);

    private static string? RegionCurrency()
    {
        try { return System.Globalization.RegionInfo.CurrentRegion.ISOCurrencySymbol; }
        catch (ArgumentException) { return null; }
    }

    /// <summary>Whether the upgrade was postponed for this trip until the app starts again.</summary>
    public bool CostUpgradeLater { get; private set; }

    /// <summary>
    /// Offers to bring costs older versions wrote into the current shape, listing what changes. Saved as one step
    /// and, when nothing else is unsaved, as a version of its own. Nothing changes without the person's OK.
    /// </summary>
    public void OfferCostUpgrade()
    {
        var plan = CostUpgradePlan;
        if (plan.IsEmpty) return;
        var lines = Jourfold.UI.CostTexts.Upgrade(Strings, plan);
        ShowMenu(new ActionMenu(Strings["UpgradeTitle"], Strings["UpgradeBody"] + "\n\n" + string.Join("\n", lines.Select(l => "• " + l)) + "\n\n" + Strings["UpgradeSafe"],
            [new MenuAction(Strings["UpgradeConfirm"], "circle-check", _ => UpgradeCostsAsync()), new MenuAction(Strings["ReminderLater"], "clock", _ => { CostUpgradeLater = true; Changed?.Invoke(); return Task.CompletedTask; })]));
    }

    /// <summary>Saves the upgrade of the trip's costs.</summary>
    public Task UpgradeCostsAsync(CancellationToken ct = default) => ChangeAsync(async () =>
    {
        if (Workspace is not { } workspace) return;
        await workspace.UpgradeCostsAsync(CostUpgrade.Plan(workspace.State.Trip, RegionCurrency()), Strings["UpgradeVersion"], ct);
        NoteChange();
    }, ct);

    /// <summary>
    /// Opens Quick Add for a payment of a booking: an expense that belongs to it, shared by its travellers,
    /// <paramref name="planned"/> when it is still to pay.
    /// </summary>
    public void ShowPaymentFor(Guid booking, bool planned = false)
    {
        QuickAddRelated = booking; QuickAddPlanned = planned;
        ShowQuickAdd("expense");
    }

    /// <summary>The booking or plan a new expense from Quick Add belongs to; cleared when Quick Add closes.</summary>
    public Guid? QuickAddRelated { get; private set; }
    /// <summary>Whether a new expense from Quick Add is still to pay.</summary>
    public bool QuickAddPlanned { get; private set; }
    internal void ClearQuickAddPayment() { QuickAddRelated = null; QuickAddPlanned = false; }
}
