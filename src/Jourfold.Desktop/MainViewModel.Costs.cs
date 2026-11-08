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
        var answer = await Interaction.PayAsync(new PayRequest(trip, people, who.Value, Me?.Id));
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

    /// <summary>Applies one of TravelRepo's expense edits to the saved expense as an undoable step.</summary>
    public Task ChangeExpenseAsync(Guid id, Func<TripSnapshot, Entity, Entity> change) => EditExpenseAsync(id, change);

    private Task EditExpenseAsync(Guid id, Func<TripSnapshot, Entity, Entity> change) => RunEditAsync(async () =>
    {
        if (Workspace?.State.Trip is not { } trip || trip.Find(id) is not { Type: "expense" } expense) return;
        var changed = change(trip, expense);
        if (!System.Text.Json.Nodes.JsonNode.DeepEquals(changed.Data, expense.Data)) await Workspace.EditAsync(changed);
    });
}
