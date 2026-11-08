using NodaTime;
using TravelRepo.Core;

namespace Jourfold.Mobile;

public sealed partial class MobileSession
{
    /// <summary>
    /// Marks what one person owes another for an expense as paid back today in the trip's time zone, or takes the
    /// mark back. The rules live in TravelRepo's <see cref="Expenses"/>.
    /// </summary>
    public Task SetDebtPaidAsync(DebtKey key, bool paid, CancellationToken ct = default) => ChangeAsync(async () =>
    {
        if (Workspace is not { } workspace || workspace.State.Trip.Find(key.Expense) is not { Type: "expense" } expense) return;
        var trip = workspace.State.Trip;
        var today = SystemClock.Instance.GetCurrentInstant().InZone(ScheduleQueries.TripZone(trip)).Date;
        await workspace.EditAsync(paid ? Expenses.Settle(trip, [key], today).Single() : Expenses.Unsettle(expense, key.From, key.To), ct); NoteChange();
    }, ct);
}
