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

    /// <summary>
    /// Sets the trip's currency for everyone. Budgets in another currency stay as they are; when the rates allow, the
    /// phone then offers to convert them, listing each, as the computer does.
    /// </summary>
    public Task SetTripCurrencyAsync(string code, CancellationToken ct = default) => ChangeAsync(async () =>
    {
        if (Workspace is not { } workspace || ExchangeRates.Code(code) is not { } currency || TripCurrency.Stored(workspace.State.Trip) == currency) return;
        await workspace.EditAsync(TripCurrency.With(workspace.State.Trip.Manifest, currency), ct); NoteChange();
        if (Budgets.ConvertTo(workspace.State.Trip, currency, EstimateRates) is { Count: > 0 } converted && EstimateRates is { } rates)
        {
            var lines = converted.Select(c => "• " + c.Budget.Title + ": " + Formats.Money(c.Before.Amount, c.Before.Currency) + " → ≈ " + Formats.Money(c.After.Amount, c.After.Currency));
            Confirm(Strings["ConvertBudgetsTitle"], Strings["ConvertBudgets"], async () =>
            {
                // Converted with the rates of the moment the user says yes, should the trip have changed meanwhile.
                if (Workspace is not { } now || Budgets.ConvertTo(now.State.Trip, currency, EstimateRates) is not { Count: > 0 } latest) return;
                await now.ApplyAsync(latest.Select(c => new EntityEdit(c.Budget.Id, c.Budget)).ToArray(), ct); NoteChange();
            }, string.Format(System.Globalization.CultureInfo.CurrentCulture, Strings["ConvertBudgetsBody"], currency, Formats.Date(LocalDate.FromDateOnly(rates.Date))) + "\n\n" + string.Join("\n", lines), Strings["KeepBudgets"]);
        }
        Changed?.Invoke();
    }, ct);

    /// <summary>Reference rates for estimates where no charged amount was entered, only when fetching them is allowed.</summary>
    public ExchangeRates? EstimateRates => RatesOn ? Rates : null;

    /// <summary>The note beside estimates, with the day of the rates, which change from day to day.</summary>
    public string EstimatedNote => string.Format(Strings["EstimatedWithRates"], EstimateRates is { } rates ? Formats.Date(LocalDate.FromDateOnly(rates.Date)) : "");

    /// <summary>
    /// Marks what one person owes another for an expense as paid back today in the trip's time zone, or takes the
    /// mark back.
    /// </summary>
    public Task SetDebtPaidAsync(DebtKey key, bool paid, CancellationToken ct = default) => ChangeAsync(async () =>
    {
        if (Workspace is not { } workspace || workspace.State.Trip.Find(key.Expense) is not { Type: "expense" } expense) return;
        var trip = workspace.State.Trip;
        // Paid back in the trip's currency, what was transferred is kept with the mark.
        await workspace.EditAsync(paid ? Expenses.Settle(trip, [key], TripToday(trip), TripCurrencyCode, EstimateRates).Single() : Expenses.Unsettle(expense, key.From, key.To), ct); NoteChange();
    }, ct);

    /// <summary>Marks a payment that was still to pay as paid, by the local user when it names nobody yet.</summary>
    public Task MarkPaymentPaidAsync(Guid expenseId, CancellationToken ct = default) => ChangeAsync(async () =>
    {
        if (Workspace is not { } workspace || workspace.State.Trip.Find(expenseId) is not { Type: "expense" } expense) return;
        await workspace.EditAsync(Expenses.MarkPaid(expense, Me is { } me ? [me.Id] : null, TripToday(workspace.State.Trip)), ct); NoteChange();
    }, ct);

    /// <summary>After everyone paid what the suggestion says, marks every open item of the trip as paid back today.</summary>
    public Task SettleAllAsync(CancellationToken ct = default) => ChangeAsync(async () =>
    {
        if (Workspace is not { } workspace || Expenses.Open(workspace.State.Trip).Count == 0) return;
        var trip = workspace.State.Trip;
        await workspace.ApplyAsync(Expenses.SettleAll(trip, TripToday(trip), TripCurrencyCode, EstimateRates).Select(e => new EntityEdit(e.Id, e)).ToArray(), ct); NoteChange();
    }, ct);

    /// <summary>Whose open items the pay function shows; the user at first.</summary>
    public Guid? PayPerson { get; private set; }

    /// <summary>The open items chosen in the pay function; null until a person is chosen, then all of theirs.</summary>
    public HashSet<DebtKey>? PayChosen { get; private set; }

    /// <summary>
    /// Opens the pay function: the person (the user, otherwise the first with something open) chooses which open items
    /// to settle now and sees what that comes to in the trip's currency.
    /// </summary>
    public void ShowPay()
    {
        if (Trip is not { } trip) return;
        var person = Me is { } me && Expenses.Open(trip, me.Id).Count > 0 ? me.Id : TripPeople.Travelers(trip).Select(p => p.Id).FirstOrDefault(p => Expenses.Open(trip, p).Count > 0);
        ChoosePayPerson(person == Guid.Empty ? Me?.Id : person, redraw: false);
        OpenPage("pay");
    }

    /// <summary>Shows another person's open items, all of them chosen.</summary>
    public void ChoosePayPerson(Guid? person, bool redraw = true)
    {
        PayPerson = person;
        PayChosen = Trip is { } trip && person is { } p ? Expenses.Open(trip, p).Select(d => d.Key).ToHashSet() : [];
        if (redraw) Changed?.Invoke();
    }

    /// <summary>Chooses an open item in the pay function, or takes it out.</summary>
    public void TogglePayItem(DebtKey key)
    {
        if (PayChosen is null) return;
        if (!PayChosen.Remove(key)) PayChosen.Add(key);
        Changed?.Invoke();
    }

    /// <summary>Marks the chosen items as paid back today, in one change, and goes back.</summary>
    public Task PayChosenAsync(CancellationToken ct = default) => ChangeAsync(async () =>
    {
        if (Workspace is not { } workspace || PayPerson is not { } person || PayChosen is not { Count: > 0 } chosen) return;
        var trip = workspace.State.Trip;
        var items = Expenses.Pay(trip, person, chosen).Items.Select(i => i.Key).ToArray();
        if (items.Length == 0) return;
        await workspace.ApplyAsync(Expenses.Settle(trip, items, TripToday(trip), TripCurrencyCode, EstimateRates).Select(e => new EntityEdit(e.Id, e)).ToArray(), ct); NoteChange();
        PayChosen = null; GoBack();
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

    /// <summary>Whose parts "By category" counts: empty for every expense in full, otherwise the chosen people's parts together.</summary>
    public HashSet<Guid> CategoryPeople { get; } = [];

    /// <summary>Counts a person's part in "By category" or not; null goes back to everyone.</summary>
    public void ToggleCategoryPerson(Guid? person)
    {
        if (person is not { } p) CategoryPeople.Clear();
        else if (!CategoryPeople.Remove(p)) CategoryPeople.Add(p);
        Changed?.Invoke();
    }

    /// <summary>The booking or plan a new expense from Quick Add belongs to; cleared when Quick Add closes.</summary>
    public Guid? QuickAddRelated { get; private set; }
    /// <summary>Whether a new expense from Quick Add is still to pay.</summary>
    public bool QuickAddPlanned { get; private set; }
    internal void ClearQuickAddPayment() { QuickAddRelated = null; QuickAddPlanned = false; }
}
