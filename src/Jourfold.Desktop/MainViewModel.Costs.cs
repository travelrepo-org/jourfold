using NodaTime;
using TravelRepo.Core;

namespace Jourfold.Desktop;

/// <summary>
/// Costs: payments, dividing them, paying back, the trip's currency and budget. The rules (who shares what, rounding,
/// debts, setting off, the pay function, the upgrade of older costs) live in TravelRepo's <see cref="Expenses"/>,
/// <see cref="Budgets"/> and <see cref="CostUpgrade"/>; this part turns clicks into undoable edits.
/// </summary>
public partial class MainViewModel
{
    /// <summary>Today in the trip's time zone, the day a payment is recorded on.</summary>
    public LocalDate Today => SystemClock.Instance.GetCurrentInstant().InZone(Trip is { } trip ? ScheduleQueries.TripZone(trip) : DateTimeZone.Utc).Date;

    /// <summary>
    /// The currency the travellers count in, stored in the trip and shared by everyone: the budget, totals and who
    /// owes whom are in it, and amounts charged are entered in it. Until it is chosen, the one the costs suggest, or
    /// this computer's region's.
    /// </summary>
    public string TripCurrencyCode => Trip is { } trip ? TripCurrency.Of(trip) ?? SystemCurrency : SystemCurrency;

    /// <summary>Sets the trip's currency for everyone, as an undoable step.</summary>
    public Task SetTripCurrencyAsync(string code) => RunEditAsync(async () =>
    {
        if (Workspace?.State.Trip is not { } trip || ExchangeRates.Code(code) is not { } currency || TripCurrency.Stored(trip) == currency) return;
        await Workspace.EditAsync(TripCurrency.With(trip.Manifest, currency));
    });

    /// <summary>The currency of the computer's region, read before Jourfold sets its own language for the interface.</summary>
    internal static readonly string SystemCurrency = ReadSystemCurrency();

    // A static constructor makes the region be read before the first instance sets the interface culture.
    static MainViewModel() { }

    private static string ReadSystemCurrency()
    {
        try { return System.Globalization.RegionInfo.CurrentRegion.ISOCurrencySymbol is { Length: 3 } code ? code : "EUR"; }
        catch (ArgumentException) { return "EUR"; }
    }

    /// <summary>Reference rates for estimates where no charged amount was entered, only when the person allowed fetching them.</summary>
    public ExchangeRates? EstimateRates => RatesOn ? Rates : null;

    /// <summary>The day of the reference rates estimates use, for the note beside ≈, which says the numbers move with them.</summary>
    public string RatesDay => EstimateRates is { } rates ? Formats.Date(LocalDate.FromDateOnly(rates.Date)) : "";

    /// <summary>The note beside estimates: with the day of the rates, and that they change from day to day.</summary>
    public string EstimatedNote => string.Format(Strings["EstimatedWithRates"], RatesDay);

    /// <summary>Whether the open trip still has costs in the form older versions wrote, so the Costs page offers the upgrade.</summary>
    public bool CostUpgradePending => Workspace is { } workspace && !CostUpgrade.Plan(workspace.State.Trip, SystemCurrency).IsEmpty;

    /// <summary>Trips whose cost upgrade was postponed in this session; the offer comes again the next time Jourfold starts.</summary>
    private readonly HashSet<string> upgradeLater = [];

    /// <summary>
    /// Offers once per trip and session to bring costs written by older versions into the current shape (see
    /// <see cref="CostUpgrade"/>), showing what changes. Saved as one undoable step and, when nothing else is
    /// unsaved, as a version of its own. Nothing changes without the person's OK.
    /// </summary>
    // Not an edit while it asks, so the header does not say "Saving" before the answer.
    public Task OfferCostUpgradeAsync(bool again = false) => RunAsync(async () =>
    {
        if (Workspace is not { } workspace || !again && upgradeLater.Contains(workspace.Repository.Root)) return;
        var plan = CostUpgrade.Plan(workspace.State.Trip, SystemCurrency);
        if (plan.IsEmpty) return;
        var lines = Jourfold.UI.CostTexts.Upgrade(Strings, plan);
        var message = Strings["UpgradeBody"] + "\n\n" + string.Join("\n", lines.Select(l => "• " + l)) + "\n\n" + Strings["UpgradeSafe"];
        if (!await Interaction.ConfirmAsync(Strings["UpgradeTitle"], message, Strings["UpgradeConfirm"])) { upgradeLater.Add(workspace.Repository.Root); return; }
        await workspace.UpgradeCostsAsync(CostUpgrade.Plan(workspace.State.Trip, SystemCurrency), Strings["UpgradeVersion"]);
        await UpdateLocalStateAsync();
        Interaction.Toast(Strings["UpgradeDone"]);
    });

    /// <summary>
    /// After travellers joined, offers to let them share earlier expenses. Nothing is chosen at first, since expenses
    /// from before usually were not theirs.
    /// </summary>
    private void TravellersJoined(IReadOnlyList<Guid> joined)
    {
        // Asked after the step that added them is done, outside its edit.
        using (ExecutionContext.SuppressFlow()) Avalonia.Threading.Dispatcher.UIThread.Post(() => _ = OfferSharesForAsync(joined));
    }

    private Task OfferSharesForAsync(IReadOnlyList<Guid> joined) => RunAsync(async () =>
    {
        if (Workspace?.State.Trip is not { } trip) return;
        var expenses = trip.Entities.Values.Where(e => e.Type == "expense").OrderBy(e => e.Title, StringComparer.CurrentCulture).ToArray();
        if (expenses.Length == 0) return;
        var names = string.Join(", ", joined.Select(id => trip.Find(id)?.Title).OfType<string>());
        var choices = expenses.Select(e => new Choice(e.Id.ToString(), e.Title + (TripSummaries.Money(e.Data["amount"]) is { } m ? " · " + Formats.Money(m.Amount, m.Currency) : ""))).ToArray();
        var chosen = await Interaction.PickAsync(Strings["ShareEarlierTitle"], string.Format(Strings["ShareEarlierBody"], names), choices, [], Strings["ShareEarlierConfirm"]);
        if (chosen is not { Count: > 0 }) return;
        var current = Workspace.State.Trip; var edits = new List<TravelRepo.Repository.EntityEdit>();
        foreach (var id in chosen.Select(Guid.Parse))
        {
            if (current.Find(id) is not { Type: "expense" } expense) continue;
            var changed = joined.Aggregate(expense, (e, person) => Expenses.WithPerson(current, e, person, true));
            edits.Add(new(changed.Id, changed));
        }
        await Workspace.ApplyAsync(edits.ToArray());
    });

    /// <summary>
    /// Records what the card actually charged for an expense, as typed ("190,40" works), in the trip's currency, on
    /// <paramref name="day"/> or the expense's own day; empty text removes it. Text that is no number changes nothing.
    /// </summary>
    public async Task<bool> SetChargeAsync(Guid expenseId, string? text, string currency, LocalDate? day = null)
    {
        if (string.IsNullOrWhiteSpace(text)) { await EditExpenseAsync(expenseId, (_, e) => Expenses.WithoutCharge(e)); return true; }
        if (ExchangeRates.Amount(text) is not { } value) return false;
        await EditExpenseAsync(expenseId, (trip, e) => Expenses.WithCharge(e, value, currency, day ?? Expenses.ChargeOf(e)?.Date ?? Expenses.DayOf(trip, e) ?? Today));
        return true;
    }

    /// <summary>Changes the day of an amount charged, keeping the amount.</summary>
    public Task SetChargeDayAsync(Guid expenseId, LocalDate day) => EditExpenseAsync(expenseId, (_, e) =>
        Expenses.ChargeOf(e) is { } charge ? Expenses.WithCharge(e, charge.Value, charge.Currency, day, charge.Source ?? "card") : e);

    /// <summary>
    /// Adds a payment for a booking: the add-expense form opens with the booking's title, paid by the local user,
    /// shared by the booking's travellers; <paramref name="planned"/> records one still to pay, such as the rest due
    /// on site. A price an older version stored is offered as the amount.
    /// </summary>
    public Task AddPaymentForBookingAsync(Guid bookingId, bool planned = false) => RunEditAsync(async () =>
    {
        if (Workspace?.State.Trip is not { } trip || trip.Find(bookingId) is not { Type: "booking" } booking) return;
        var legacy = TripSummaries.Money(booking.Data["price"]) is { } price ? CostUpgrade.Rest(trip, booking, price) is { Amount: > 0 } rest ? rest : price : null;
        var currency = legacy?.Currency ?? Expenses.ForBooking(trip, bookingId).Select(e => TripSummaries.Money(e.Data["amount"])?.Currency).OfType<string>().FirstOrDefault() ?? TripCurrencyCode;
        var request = new QuickAddRequest("expense", trip, StartDate, null, null, ScheduleQueries.TripZone(trip).Id, planned ? null : Me?.Id, false, currency) { Title = booking.Title, Amount = legacy?.Amount, Related = booking.Id, Planned = planned };
        if (await Interaction.QuickAddAsync(request) is not { } draft) return;
        var (_, built) = Jourfold.Application.QuickAdd.Build(draft, Workspace.State.Trip);
        var edits = built.ToList();
        // The price an older version stored is now recorded as payments.
        if (legacy is not null && Workspace.State.Trip.Find(bookingId)?.Copy() is { } withoutPrice) { withoutPrice.Data.Remove("price"); edits.Add(new(withoutPrice.Id, withoutPrice)); }
        await Workspace.ApplyAsync(edits.ToArray());
        Interaction.Toast(string.Format(Strings["ExpenseAddedFor"], booking.Title));
    });

    /// <summary>Sets the day a payment still to pay is due, or none; a reminder comes a week before.</summary>
    public Task SetDueAsync(Guid expenseId, LocalDate? day) => EditExpenseAsync(expenseId, (_, e) => Expenses.WithDue(e, day));

    /// <summary>Whose parts "By category" counts: empty for every expense in full, otherwise the chosen people's parts together.</summary>
    public HashSet<Guid> CategoryPeople { get; } = [];

    /// <summary>Counts a person's part in "By category" or not; null goes back to everyone.</summary>
    public void ToggleCategoryPerson(Guid? person)
    {
        if (person is not { } p) CategoryPeople.Clear();
        else if (!CategoryPeople.Remove(p)) CategoryPeople.Add(p);
        ContentChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Marks a payment that was still to pay as paid, by the local user when it names nobody yet.</summary>
    public Task MarkPaymentPaidAsync(Guid expenseId) => EditExpenseAsync(expenseId, (_, e) => Expenses.MarkPaid(e, Me is { } me ? [me.Id] : null));

    /// <summary>Removes a price an older version stored on a booking, after the travellers looked at it.</summary>
    public Task DropLegacyPriceAsync(Guid bookingId) => RunEditAsync(async () =>
    {
        if (Workspace?.State.Trip.Find(bookingId)?.Copy() is not { Type: "booking" } booking || booking.Data["price"] is null) return;
        booking.Data.Remove("price"); await Workspace.EditAsync(booking);
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
    /// <remarks>Paid back in the trip's currency, what was transferred is kept with the mark, so it no longer moves with the rates.</remarks>
    public Task SetDebtPaidAsync(DebtKey key, bool paid) => EditExpenseAsync(key.Expense, (trip, e) => paid ? Expenses.Settle(trip, [key], Today, TripCurrencyCode, EstimateRates).Single() : Expenses.Unsettle(e, key.From, key.To));

    /// <summary>Marks everything owed for an expense as paid back today, or takes all its marks back.</summary>
    public Task SetExpensePaidAsync(Guid expenseId, bool paid) => EditExpenseAsync(expenseId, (trip, e) => paid ? Expenses.Settle(trip, e, null, Today, TripCurrencyCode, EstimateRates) : Expenses.Unsettle(e));

    /// <summary>
    /// The pay function: the person (the local user unless another is given) chooses which of their open items to
    /// settle now and sees what that comes to in the trip's currency; confirming marks the chosen items as paid in one
    /// undoable step.
    /// </summary>
    public Task PayAsync(Guid? person = null) => RunEditAsync(async () =>
    {
        if (Workspace is null) return;
        var trip = Workspace.State.Trip;
        var people = TripPeople.Travelers(trip).Concat(trip.Entities.Values.Where(e => e.Type == "person")).DistinctBy(p => p.Id).ToArray();
        var who = person ?? Me?.Id ?? people.FirstOrDefault(p => Expenses.Open(trip, p.Id).Count > 0)?.Id;
        if (who is null || people.Length == 0) return;
        var answer = await Interaction.PayAsync(new PayRequest(trip, people, who.Value, Me?.Id) { Currency = TripCurrencyCode, Rates = EstimateRates });
        if (answer is null || answer.Items.Count == 0) return;
        var items = Expenses.Pay(Workspace.State.Trip, answer.Person, answer.Items).Items.Select(i => i.Key).ToArray();
        if (items.Length == 0) return;
        await Workspace.ApplyAsync(Expenses.Settle(Workspace.State.Trip, items, Today, TripCurrencyCode, EstimateRates).Select(e => new TravelRepo.Repository.EntityEdit(e.Id, e)).ToArray());
        Interaction.Toast(string.Format(Strings["PaidItems"], items.Length));
    });

    /// <summary>After everyone paid what the suggestion says, marks every open item of the trip as paid.</summary>
    public Task SettleAllAsync() => RunEditAsync(async () =>
    {
        if (Workspace is null || Expenses.Open(Workspace.State.Trip).Count == 0) return;
        if (!await Interaction.ConfirmAsync(Strings["SettleAllTitle"], Strings["SettleAllBody"], Strings["SettleAll"])) return;
        await Workspace.ApplyAsync(Expenses.SettleAll(Workspace.State.Trip, Today, TripCurrencyCode, EstimateRates).Select(e => new TravelRepo.Repository.EntityEdit(e.Id, e)).ToArray());
    });

    /// <summary>Sets what a budget is limited to, such as one person's food; no limits is the trip's budget. One undoable step.</summary>
    public Task SetBudgetFiltersAsync(Guid budgetId, BudgetFilters filters) => RunEditAsync(async () =>
    {
        if (Workspace?.State.Trip is not { } trip || trip.Find(budgetId) is not { Type: "budget" } budget) return;
        var changed = Budgets.WithFilters(trip, budget, filters);
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
