using NodaTime;
using TravelRepo.Core;

namespace Jourfold.Desktop;

/// <summary>
/// Dividing expenses and paying back. The rules (who shares what, rounding, debts, setting off, the pay function)
/// live in TravelRepo's <see cref="Expenses"/>; this part turns clicks into undoable edits.
/// </summary>
public partial class MainViewModel
{
    /// <summary>Today in the trip's time zone, the day a payment is recorded on.</summary>
    public LocalDate Today => SystemClock.Instance.GetCurrentInstant().InZone(Trip is { } trip ? ScheduleQueries.TripZone(trip) : DateTimeZone.Utc).Date;

    /// <summary>
    /// The currency the travellers count in at home, such as EUR: what a card charged is entered in it, and costs and
    /// paying back can be shown in it. Kept on this computer; the region's currency until chosen.
    /// </summary>
    public string HomeCurrency
    {
        get => Store.Get("costs.home") is { Length: 3 } code ? code : ChargedCurrency() ?? SystemCurrency;
        set { if (ExchangeRates.Code(value) is { } code) { Store.Set("costs.home", code); ContentChanged?.Invoke(this, EventArgs.Empty); } }
    }

    /// <summary>The currency most amounts in the trip were charged in, which is the travellers' home currency.</summary>
    private string? ChargedCurrency() => Trip is { } trip ? trip.Entities.Values.Where(e => e.Type == "expense").Select(Expenses.ChargeOf).OfType<Charge>()
        .GroupBy(c => c.Currency).OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal).FirstOrDefault()?.Key : null;

    /// <summary>The currency of the computer's region, read before Jourfold sets its own language for the interface.</summary>
    private static readonly string SystemCurrency = ReadSystemCurrency();

    // A static constructor makes the region be read before the first instance sets the interface culture.
    static MainViewModel() { }

    private static string ReadSystemCurrency()
    {
        try { return System.Globalization.RegionInfo.CurrentRegion.ISOCurrencySymbol is { Length: 3 } code ? code : "EUR"; }
        catch (ArgumentException) { return "EUR"; }
    }

    /// <summary>Whether who owes whom is shown in <see cref="HomeCurrency"/> (the default) or in each expense's own currency.</summary>
    public bool SettleInHomeCurrency
    {
        get => Store.Get("costs.settle") != "each";
        set { Store.Set("costs.settle", value ? "home" : "each"); ContentChanged?.Invoke(this, EventArgs.Empty); }
    }

    /// <summary>The currency debts are settled in, or null for each expense's own; see <see cref="Expenses.Balances"/>.</summary>
    public string? SettleCurrency => SettleInHomeCurrency ? HomeCurrency : null;

    /// <summary>Reference rates for estimates where no charged amount was entered, only when the person allowed fetching them.</summary>
    public ExchangeRates? EstimateRates => RatesOn ? Rates : null;

    /// <summary>
    /// Records what the card actually charged for an expense, as typed ("190,40" works), in <paramref name="currency"/>
    /// on today's date; empty text removes it. Text that is no number changes nothing.
    /// </summary>
    public async Task<bool> SetChargeAsync(Guid expenseId, string? text, string currency)
    {
        if (string.IsNullOrWhiteSpace(text)) { await EditExpenseAsync(expenseId, (_, e) => Expenses.WithoutCharge(e)); return true; }
        if (ExchangeRates.Amount(text) is not { } value) return false;
        await EditExpenseAsync(expenseId, (_, e) => Expenses.WithCharge(e, value, currency, Today));
        return true;
    }

    /// <summary>
    /// Records a booking's price as an expense: the add-expense form opens with the booking's title, price and
    /// currency, paid by the local user, and the new expense belongs to the booking. Nothing is added when it is cancelled.
    /// </summary>
    public Task AddExpenseForBookingAsync(Guid bookingId) => RunEditAsync(async () =>
    {
        if (Workspace?.State.Trip is not { } trip || trip.Find(bookingId) is not { Type: "booking" } booking) return;
        var start = Expenses.FromBooking(trip, booking);
        var price = TripSummaries.Money(start.Data["amount"])!;
        var request = new QuickAddRequest("expense", trip, StartDate, null, null, ScheduleQueries.TripZone(trip).Id, Me?.Id, false, price.Currency) { Title = start.Title, Amount = price.Amount, Related = booking.Id };
        if (await Interaction.QuickAddAsync(request) is not { } draft) return;
        var (main, edits) = Jourfold.Application.QuickAdd.Build(draft, Workspace.State.Trip);
        await Workspace.ApplyAsync(edits);
        Interaction.Toast(string.Format(Strings["ExpenseAddedFor"], booking.Title));
        Selected = Workspace.State.Trip.Find(main.Id);
    });

    public Task SetSplitMethodAsync(Guid expenseId, SplitMethod method) => EditExpenseAsync(expenseId, (trip, e) => Expenses.ChangeMethod(trip, e, method));

    public Task SetSplitPersonAsync(Guid expenseId, Guid person, bool shares) => EditExpenseAsync(expenseId, (trip, e) => Expenses.WithPerson(trip, e, person, shares));

    /// <summary>Sets one person's amount, weight or percentage as typed ("12,50" works). Text that is no number changes nothing.</summary>
    public async Task<bool> SetSplitValueAsync(Guid expenseId, Guid person, string? text)
    {
        if (ExchangeRates.Amount(text) is not { } value) return false;
        await EditExpenseAsync(expenseId, (trip, e) => Expenses.WithValue(trip, e, person, value));
        return true;
    }

    /// <summary>Marks what one person owes another for an expense as paid back today, or takes the mark back.</summary>
    public Task SetDebtPaidAsync(DebtKey key, bool paid) => EditExpenseAsync(key.Expense, (trip, e) => paid ? Expenses.Settle(trip, [key], Today).Single() : Expenses.Unsettle(e, key.From, key.To));

    /// <summary>Marks everything owed for an expense as paid back today, or takes all its marks back.</summary>
    public Task SetExpensePaidAsync(Guid expenseId, bool paid) => EditExpenseAsync(expenseId, (trip, e) => paid ? Expenses.Settle(trip, e, null, Today) : Expenses.Unsettle(e));

    /// <summary>
    /// The pay function: the person (the local user unless another is given) chooses which of their open items to
    /// settle now and sees what that comes to; confirming marks the chosen items as paid in one undoable step.
    /// </summary>
    public Task PayAsync(Guid? person = null) => RunEditAsync(async () =>
    {
        if (Workspace is null) return;
        var trip = Workspace.State.Trip;
        var people = TripPeople.Travelers(trip).Concat(trip.Entities.Values.Where(e => e.Type == "person")).DistinctBy(p => p.Id).ToArray();
        var who = person ?? Me?.Id ?? people.FirstOrDefault(p => Expenses.Open(trip, p.Id).Count > 0)?.Id;
        if (who is null || people.Length == 0) return;
        var answer = await Interaction.PayAsync(new PayRequest(trip, people, who.Value, Me?.Id) { Currency = SettleCurrency, Rates = EstimateRates });
        if (answer is null || answer.Items.Count == 0) return;
        var items = Expenses.Pay(Workspace.State.Trip, answer.Person, answer.Items).Items.Select(i => i.Key).ToArray();
        if (items.Length == 0) return;
        await Workspace.ApplyAsync(Expenses.Settle(Workspace.State.Trip, items, Today).Select(e => new TravelRepo.Repository.EntityEdit(e.Id, e)).ToArray());
        Interaction.Toast(string.Format(Strings["PaidItems"], items.Length));
    });

    /// <summary>After everyone paid what the suggestion says, marks every open item of the trip as paid.</summary>
    public Task SettleAllAsync() => RunEditAsync(async () =>
    {
        if (Workspace is null || Expenses.Open(Workspace.State.Trip).Count == 0) return;
        if (!await Interaction.ConfirmAsync(Strings["SettleAllTitle"], Strings["SettleAllBody"], Strings["SettleAll"])) return;
        await Workspace.ApplyAsync(Expenses.SettleAll(Workspace.State.Trip, Today).Select(e => new TravelRepo.Repository.EntityEdit(e.Id, e)).ToArray());
    });

    /// <summary>Sets what a budget covers: the whole trip, a category, a person's id or a collection's id, as an undoable step.</summary>
    public Task SetBudgetScopeAsync(Guid budgetId, string scope, string? target = null) => RunEditAsync(async () =>
    {
        if (Workspace?.State.Trip is not { } trip || trip.Find(budgetId) is not { Type: "budget" } budget) return;
        var changed = Budgets.WithScope(trip, budget, scope, target);
        if (!System.Text.Json.Nodes.JsonNode.DeepEquals(changed.Data, budget.Data)) await Workspace.EditAsync(changed);
    });

    /// <summary>Applies one of TravelRepo's expense edits to the saved expense as an undoable step.</summary>
    public Task ChangeExpenseAsync(Guid id, Func<TripSnapshot, Entity, Entity> change) => EditExpenseAsync(id, change);

    private Task EditExpenseAsync(Guid id, Func<TripSnapshot, Entity, Entity> change) => RunEditAsync(async () =>
    {
        if (Workspace?.State.Trip is not { } trip || trip.Find(id) is not { Type: "expense" } expense) return;
        var changed = change(trip, expense);
        if (!System.Text.Json.Nodes.JsonNode.DeepEquals(changed.Data, expense.Data)) await Workspace.EditAsync(changed);
    });
}
