using System.Text.Json.Nodes;
using Avalonia.Headless.XUnit;
using Jourfold.Application;
using Jourfold.Desktop;
using Jourfold.Infrastructure;
using NodaTime;
using TravelRepo.Core;
using TravelRepo.Git;
using TravelRepo.Repository;
using Xunit;
namespace Jourfold.Tests;

/// <summary>Reminders in Jourfold: dismissed and postponed only on this computer, urgent ones notified once.</summary>
public sealed class ReminderTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "jourfold-reminders-" + Guid.NewGuid());
    public void Dispose() => TestFiles.Delete(root);
    private const string Zone = "Pacific/Auckland";
    private static Instant At(string local) => new ZonedTime(local, Zone).ToInstant();

    private async Task<string> Trip()
    {
        var repository = new TravelRepository(Path.Combine(root, "trip")); var manifest = Entity.CreateTrip("New Zealand", "en", Zone);
        manifest.Data["dates"] = new JsonObject { ["start"] = "2026-11-08", ["end"] = "2026-11-09" };
        await repository.InitializeAsync(manifest);
        await new GitRepository(repository, new GitCliBackend()).InitializeAsync("Test", "test@example.invalid");
        return repository.Root;
    }

    [AvaloniaFact]
    public async Task RemindersAreDismissedPostponedAndNotifiedOnce()
    {
        using var store = new LocalStore(Path.Combine(root, "local"));
        using var m = new MainViewModel(store, new GitCliBackend(), new OsSecretStore(), new TestInteraction());
        var now = At("2026-11-04T09:00:00"); m.Clock = () => now;
        await m.OpenAsync(await Trip());
        var lodge = Entity.Create("schedule_item", "Lakeview Lodge"); lodge.Data["status"] = "confirmed";
        lodge.Data["components"]!["accommodation"] = new JsonObject { ["cancellation"] = new JsonObject { ["free_until"] = new ZonedTime("2026-11-05T18:00:00", Zone).ToJson() } };
        lodge = Stays.WithNights(m.Workspace!.State.Trip, lodge, new LocalDate(2026, 11, 8), 1);
        var task = Entity.Create("task", "Book rental car"); task.Data["due"] = new JsonObject { ["date"] = "2026-11-10" };
        await m.Workspace.ApplyAsync([new(lodge.Id, lodge), new(task.Id, task)]);
        var notified = new List<string>(); m.DesktopNotifier = (title, body) => { notified.Add(title + ": " + body); return Task.CompletedTask; };
        await m.UpdateRemindersAsync();

        var cancellation = Assert.Single(m.Reminders, r => r.Kind == ReminderKind.CancellationDeadline);
        Assert.True(cancellation.Urgent); Assert.Equal(1, m.UrgentReminders);
        Assert.Contains("Free cancellation until", m.ReminderText(cancellation));
        Assert.Equal(["Lakeview Lodge: " + m.ReminderText(cancellation)], notified.ToArray());
        await m.UpdateRemindersAsync(); Assert.Single(notified);

        await m.SnoozeReminderAsync(cancellation); Assert.Empty(m.Reminders);
        now = At("2026-11-05T10:00:00"); await m.UpdateRemindersAsync();
        Assert.Single(m.Reminders); Assert.Single(notified);
        await m.DismissReminderAsync(m.Reminders[0]); Assert.Empty(m.Reminders);

        // Dismissing is remembered on this computer; the trip files are unchanged.
        Assert.Contains(cancellation.Key, store.Get("reminders.dismissed:" + m.Workspace.Repository.Root));
        m.ShowReminder(cancellation); Assert.Equal("Plan", m.View); Assert.Equal(lodge.Id, m.Selected?.Id);

        m.SetDesktopNotifications(false); now = At("2026-11-10T08:00:00"); await m.UpdateRemindersAsync();
        Assert.Contains(m.Reminders, r => r.Kind == ReminderKind.TaskDue && r.Urgent); Assert.Single(notified);
    }

    [AvaloniaFact]
    public void QuickAddDoesNotInventCheckInHours()
    {
        var trip = new TripSnapshot(Entity.CreateTrip("New Zealand", "en", Zone), new Dictionary<Guid, Entity>());
        var (stay, _) = QuickAdd.Build(new QuickAddDraft("accommodation", "Lodge") { Date = new LocalDate(2026, 11, 8), Nights = 2, Timezone = Zone }, trip);
        var resolved = Stays.Resolve(trip, stay)!;
        Assert.Equal(2, resolved.Nights); Assert.True(resolved.CheckInApproximate); Assert.True(resolved.CheckOutApproximate);
        Assert.Equal("approximate", stay.Data["time"]!["precision"]!.ToString());
        var (timed, _) = QuickAdd.Build(new QuickAddDraft("accommodation", "Lodge") { Date = new LocalDate(2026, 11, 8), Start = new LocalTime(18, 30), Nights = 1, Timezone = Zone }, trip);
        Assert.False(Stays.Resolve(trip, timed)!.CheckInApproximate);
    }
}
