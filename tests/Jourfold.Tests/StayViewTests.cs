using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using Avalonia.Headless.XUnit;
using Jourfold.Desktop;
using Jourfold.Infrastructure;
using NodaTime;
using TravelRepo.Core;
using TravelRepo.Git;
using TravelRepo.Repository;
using Xunit;
namespace Jourfold.Tests;

/// <summary>Stays shown as nights below the timetable, moved and merged through undoable edits.</summary>
public sealed class StayViewTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "jourfold-stays-" + Guid.NewGuid());
    public void Dispose() => TestFiles.Delete(root);
    private const string Zone = "Pacific/Auckland";

    private async Task<string> Trip()
    {
        var repository = new TravelRepository(Path.Combine(root, "trip")); var manifest = Entity.CreateTrip("New Zealand", "en", Zone);
        manifest.Data["dates"] = new JsonObject { ["start"] = "2026-11-09", ["end"] = "2026-11-13" };
        await repository.InitializeAsync(manifest);
        await new GitRepository(repository, new GitCliBackend()).InitializeAsync("Test", "test@example.invalid");
        return repository.Root;
    }

    private static Entity Night(Entity place, string date, string status = "reserved")
    {
        var e = Entity.Create("schedule_item", "Night at the lodge"); e.Data["status"] = status;
        e.Data["time"] = new JsonObject { ["precision"] = "day_part", ["date"] = date, ["day_part"] = "night", ["timezone"] = Zone };
        e.Data["components"]!["accommodation"] = new JsonObject { ["place"] = place.Id.ToString() };
        return e;
    }

    [AvaloniaFact]
    public async Task StaysAreNightsBelowTheGridAndEmptyNightsOfferAStay()
    {
        var w = new MainWindow(); w.Model.SetPreference("Language", "en"); w.Model.SetLanes(false); w.Show(); Probe.Layout();
        try
        {
            var m = w.Model; await m.OpenAsync(await Trip()); m.InboxOpen = false;
            var lodge = Entity.Create("place", "Lodge"); var stay = Entity.Create("schedule_item", "Lodge"); stay.Data["status"] = "confirmed";
            stay.Data["components"]!["accommodation"] = new JsonObject { ["place"] = lodge.Id.ToString() };
            await m.Workspace!.ApplyAsync([new(lodge.Id, lodge), new(stay.Id, Stays.WithNights(m.Workspace.State.Trip, stay, new LocalDate(2026, 11, 9), 2))]);
            m.Navigate("Plan"); m.ShowDate(new LocalDate(2026, 11, 9)); Probe.Layout();
            var timetable = Assert.IsType<TimetableView>(w.FindControl<ContentControl>("MainContent")!.Content);
            var blocks = Probe.All<Button>(timetable).Where(b => b.Tag is Guid id && id == stay.Id).ToArray();
            var bar = Assert.Single(blocks);
            Assert.Contains("2 nights", Avalonia.Automation.AutomationProperties.GetName(bar));
            // Nights 11 and 12 have no stay; their placeholders add one.
            Assert.Equal(2, Probe.All<Button>(timetable).Count(b => Avalonia.Automation.AutomationProperties.GetName(b)?.StartsWith(m.Strings["NoStayTonight"], StringComparison.Ordinal) == true));

            // The bar's tooltip opens above it and never takes a click, also when popups are drawn inside the window.
            Assert.Equal(PlacementMode.Top, ToolTip.GetPlacement(bar));
            ToolTip.SetIsOpen(bar, true); Probe.Layout();
            try
            {
                await Probe.Until(() => { Probe.Layout(); return Probe.All<ToolTip>(w).Any(t => Equals(t.Content, ToolTip.GetTip(bar))); });
                var tip = Probe.All<ToolTip>(w).First(t => Equals(t.Content, ToolTip.GetTip(bar)));
                Assert.False(tip.IsHitTestVisible);
                var inside = tip.TranslatePoint(new Point(tip.Bounds.Width / 2, tip.Bounds.Height / 2), w)!.Value;
                Assert.False(w.InputHitTest(inside) is Visual hit && hit.GetSelfAndVisualAncestors().Contains(tip));
            }
            finally { ToolTip.SetIsOpen(bar, false); Probe.Layout(); }

            // A real left click on the bar selects the stay (the button handles the release itself).
            m.Selected = null; Probe.Layout();
            var point = bar.TranslatePoint(new Avalonia.Point(30, 12), w)!.Value;
            w.MouseDown(point, Avalonia.Input.MouseButton.Left); w.MouseUp(point, Avalonia.Input.MouseButton.Left);
            await Probe.Until(() => { Probe.Layout(); return m.Selected?.Id == stay.Id; });
            Assert.Equal(stay.Id, m.Selected?.Id);

            // Selected, the stay's check-in and check-out markers sit above the activities; otherwise behind them.
            static int[] Markers(TimetableView view, string text) => [.. Probe.All<Border>(view).Where(b => !b.IsHitTestVisible && Probe.All<TextBlock>(b).Any(t => t.Text?.StartsWith(text, StringComparison.Ordinal) == true)).Select(b => b.ZIndex)];
            await m.Workspace.EditAsync(Stays.WithSpan(m.Workspace.State.Trip.Find(stay.Id)!, new ZonedTime("2026-11-09T15:00:00", Zone), new ZonedTime("2026-11-11T10:00:00", Zone)));
            m.Selected = m.Workspace.State.Trip.Find(stay.Id);
            var checkIn = m.Strings["CheckInAt"].Split('{')[0];
            TimetableView Current() => Assert.IsType<TimetableView>(w.FindControl<ContentControl>("MainContent")!.Content);
            await Probe.Until(() => { Probe.Layout(); return Markers(Current(), checkIn) is { Length: > 0 } z && z.All(v => v > 2); });
            m.Selected = null;
            await Probe.Until(() => { Probe.Layout(); return Markers(Current(), checkIn) is { Length: > 0 } z && z.All(v => v < 2); });

            // A time that differs from check-in and check-out is explained once, in the Nights section with its fix,
            // in the interface language even in Advanced mode.
            var differing = m.Workspace.State.Trip.Find(stay.Id)!.Copy();
            differing.Data["time"] = new JsonObject { ["precision"] = "day_part", ["date"] = "2026-11-12", ["day_part"] = "night", ["timezone"] = Zone };
            await m.Workspace.EditAsync(differing); m.Advanced = true; m.Selected = m.Workspace.State.Trip.Find(stay.Id); Probe.ShowDetails(w);
            try
            {
                Assert.Contains(m.Diagnostics, d => d.Code == "stay.time_mismatch");
                var texts = Probe.All<TextBlock>(Probe.Named<StackPanel>(w, "Inspector")).Select(t => t.Text).ToArray();
                Assert.Contains(m.Strings["StayTimeMismatch"], texts);
                Assert.DoesNotContain(texts, t => t?.Contains("check-in and check-out apply", StringComparison.Ordinal) == true || t == m.Strings.Diagnostic("stay.time_mismatch"));
            }
            finally { m.Advanced = false; }
        }
        finally { w.Close(); }
    }

    [AvaloniaFact]
    public async Task StaysMoveByNightsAndMergeAsOneUndoableStep()
    {
        using var store = new LocalStore(Path.Combine(root, "local")); var interaction = new TestInteraction();
        using var m = new MainViewModel(store, new GitCliBackend(), new OsSecretStore(), interaction); await m.OpenAsync(await Trip());
        var lodge = Entity.Create("place", "Lodge");
        var first = Night(lodge, "2026-11-09", "idea"); var second = Night(lodge, "2026-11-10", "confirmed");
        var booking = Entity.Create("booking", "Lodge booking"); booking.Data["items"] = new JsonArray(first.Id.ToString(), second.Id.ToString());
        await m.Workspace!.ApplyAsync([new(lodge.Id, lodge), new(first.Id, first), new(second.Id, second), new(booking.Id, booking)]);
        Assert.Single(m.StayMergeCandidates);

        await m.MergeStaysCommand.ExecuteAsync(null);
        var trip = m.Workspace.State.Trip;
        Assert.Null(trip.Find(second.Id));
        var merged = Stays.Resolve(trip, trip.Find(first.Id)!)!;
        Assert.Equal(new LocalDate(2026, 11, 9), merged.FirstNight); Assert.Equal(2, merged.Nights); Assert.Equal(StayIssues.None, merged.Issues);
        Assert.Equal("confirmed", merged.Item.Data["status"]!.ToString());
        Assert.Single(trip.Find(booking.Id)!.Data["items"]!.AsArray());
        Assert.Empty(m.StayMergeCandidates);

        await m.MoveStayAsync(first.Id, 2);
        var moved = Stays.Resolve(m.Workspace.State.Trip, m.Workspace.State.Trip.Find(first.Id)!)!;
        Assert.Equal(new LocalDate(2026, 11, 11), moved.FirstNight); Assert.Equal(2, moved.Nights);
        await m.SetStayNightsAsync(first.Id, 3);
        Assert.Equal(3, Stays.Resolve(m.Workspace.State.Trip, m.Workspace.State.Trip.Find(first.Id)!)!.Nights);

        await m.UndoCommand.ExecuteAsync(null); await m.UndoCommand.ExecuteAsync(null); await m.UndoCommand.ExecuteAsync(null);
        Assert.NotNull(m.Workspace.State.Trip.Find(second.Id)); Assert.Equal(2, m.Workspace.State.Trip.Find(booking.Id)!.Data["items"]!.AsArray().Count);
        Assert.Empty(interaction.Errors);
    }

    [AvaloniaFact]
    public async Task ChangingTheTimeOfAStayMovesCheckInAndCheckOut()
    {
        using var store = new LocalStore(Path.Combine(root, "local")); using var m = new MainViewModel(store, new GitCliBackend(), new OsSecretStore(), new TestInteraction()); await m.OpenAsync(await Trip());
        var lodge = Entity.Create("place", "Lodge"); var stay = Night(lodge, "2026-11-09");
        stay = Stays.WithNights(new TripSnapshot(Entity.CreateTrip("x", "en", Zone), new Dictionary<Guid, Entity>()), stay, new LocalDate(2026, 11, 9), 2);
        var trip = m.Workspace!.State.Trip;
        var night = MainViewModel.WithStayTime(trip, stay, new JsonObject { ["precision"] = "day_part", ["date"] = "2026-11-12", ["day_part"] = "night", ["timezone"] = Zone });
        var resolved = Stays.Resolve(trip, night)!; Assert.Equal(new LocalDate(2026, 11, 12), resolved.FirstNight); Assert.Equal(2, resolved.Nights); Assert.Equal(StayIssues.None, resolved.Issues);
        var exact = MainViewModel.WithStayTime(trip, stay, new JsonObject { ["precision"] = "exact", ["start"] = new ZonedTime("2026-11-10T17:00:00", Zone).ToJson(), ["end"] = new ZonedTime("2026-11-11T09:30:00", Zone).ToJson() });
        Assert.Equal("2026-11-10T17:00:00", exact.Data["components"]!["accommodation"]!["check_in"]!["local"]!.ToString());
        MainViewModel.Unschedule(exact);
        Assert.False(Stays.Resolve(trip, exact)!.IsScheduled);
    }
}
