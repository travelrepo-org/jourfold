using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Jourfold.Desktop;
using Jourfold.Infrastructure;
using NodaTime;
using TravelRepo.Core;
using TravelRepo.Git;
using TravelRepo.Repository;
using Xunit;
namespace Jourfold.Tests;

/// <summary>Findings from planning a real trip: deleting linked items, rebuilding views, and explaining Git failures.</summary>
public sealed class UsageFixTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "jourfold-usage-" + Guid.NewGuid());
    public void Dispose() => TestFiles.Delete(root);

    private static MainWindow Window()
    {
        var w = new MainWindow(); w.Model.SetPreference("Language", "en"); w.Model.SetPreference("TextSize", "Normal"); w.Model.SetPreference("HighContrast", "false");
        w.Model.SetPreference("OpenView", "Plan"); w.Model.SetPreference("Density", "Comfortable"); w.Model.SetPreference("Theme", "Light"); w.Model.SetLanes(false);
        w.Show(); Probe.Layout(); return w;
    }

    private async Task<string> Trip()
    {
        var repository = new TravelRepository(Path.Combine(root, "trip")); await repository.InitializeAsync(Entity.CreateTrip("Rotorua", "en", "Pacific/Auckland"));
        await new GitRepository(repository, new GitCliBackend()).InitializeAsync("Test", "test@example.invalid");
        return repository.Root;
    }

    private async Task<MainViewModel> Open(MainWindow w)
    {
        await w.Model.OpenAsync(await Trip()); w.Model.InboxOpen = false; Probe.Layout();
        return w.Model;
    }

    /// <summary>A view model without a window, with scripted answers.</summary>
    private async Task<(MainViewModel, TestInteraction, IDisposable)> Model()
    {
        var store = new LocalStore(Path.Combine(root, "local")); var interaction = new TestInteraction();
        var vm = new MainViewModel(store, new GitCliBackend(), new OsSecretStore(), interaction); await vm.OpenAsync(await Trip());
        return (vm, interaction, new Both(vm, store));
    }
    private sealed class Both(IDisposable a, IDisposable b) : IDisposable { public void Dispose() { a.Dispose(); b.Dispose(); } }

    private static async Task<(Entity Lodge, Entity Stay, Entity Booking)> Stay(MainViewModel m)
    {
        var lodge = Entity.Create("place", "Aroha Lodge");
        var stay = Entity.Create("schedule_item", "Night in Rotorua"); stay.Data["default_place"] = lodge.Id.ToString();
        stay.Data["components"]!["accommodation"] = new JsonObject { ["place"] = lodge.Id.ToString() };
        var booking = Entity.Create("booking", "Booking 4711"); booking.Data["items"] = new JsonArray(stay.Id.ToString());
        await m.Workspace!.ApplyAsync([new(lodge.Id, lodge), new(stay.Id, stay), new(booking.Id, booking)]);
        return (lodge, stay, booking);
    }

    [AvaloniaFact]
    public async Task DeletingAStayAsksAboutItsPlaceAndUndoRestoresEverything()
    {
        var (m, interaction, scope) = await Model(); using (scope)
        {
            var (lodge, stay, booking) = await Stay(m);
            await m.DeleteAsync(stay.Id);
            var prompt = Assert.Single(interaction.DeletionPrompts);
            Assert.Contains(prompt.Affected, e => e.Id == booking.Id);
            Assert.Equal([lodge.Id], prompt.Preselected.ToArray());
            Assert.Contains(prompt.Unused, e => e.Id == booking.Id);
            var trip = m.Workspace!.State.Trip;
            Assert.Null(trip.Find(stay.Id)); Assert.Null(trip.Find(lodge.Id));
            Assert.Empty(trip.Find(booking.Id)!.Data["items"]!.AsArray());
            Assert.DoesNotContain(m.Workspace.State.Diagnostics, d => d.Severity == Severity.Error);
            Assert.Empty(interaction.Errors);

            await m.UndoCommand.ExecuteAsync(null);
            trip = m.Workspace.State.Trip;
            Assert.NotNull(trip.Find(stay.Id)); Assert.NotNull(trip.Find(lodge.Id)); Assert.Single(trip.Find(booking.Id)!.Data["items"]!.AsArray());
        }

    }

    [AvaloniaFact]
    public async Task DeletingAPlaceInUseKeepsTheItemsAndCancelKeepsEverything()
    {
        var (m, interaction, scope) = await Model(); using (scope)
        {
            var (lodge, stay, _) = await Stay(m);
            interaction.Deletion = _ => null;
            await m.DeleteAsync(lodge.Id);
            Assert.NotNull(m.Workspace!.State.Trip.Find(lodge.Id));

            interaction.Deletion = p => p.Preselected;
            await m.DeleteAsync(lodge.Id);
            Assert.Contains(interaction.DeletionPrompts[^1].Affected, e => e.Id == stay.Id);
            var after = m.Workspace.State.Trip.Find(stay.Id)!;
            Assert.Null(after.Data["default_place"]); Assert.Null(after.Data["components"]!["accommodation"]!["place"]);
            Assert.Empty(interaction.Errors);
        }

    }

    [AvaloniaFact]
    public async Task AnUnlinkedItemIsDeletedWithoutAsking()
    {
        var (m, interaction, scope) = await Model(); using (scope)
        {
            var walk = Entity.Create("schedule_item", "Walk"); await m.Workspace!.EditAsync(walk);
            await m.DeleteAsync(walk.Id);
            Assert.Empty(interaction.DeletionPrompts); Assert.Null(m.Workspace.State.Trip.Find(walk.Id));
        }

    }

    [AvaloniaFact]
    public async Task SelectingInAListDoesNotRebuildTheView()
    {
        var w = Window(); try
        {
            var m = await Open(w);
            for (var i = 0; i < 20; i++) { var place = Entity.Create("place", "Place " + i.ToString("00")); await m.Workspace!.EditAsync(place); }
            m.Navigate("Places"); Probe.Layout();
            var host = w.FindControl<ContentControl>("MainContent")!; var view = host.Content;
            var row = Probe.All<Button>(host).First(b => b.Tag is Guid && Probe.All<TextBlock>(b).Any(t => t.Text == "Place 15"));
            row.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); Probe.Layout();
            Assert.Same(view, host.Content);
            Assert.Contains("selected", row.Classes);
            Assert.Equal(1, Probe.All<Button>(host).Count(b => b.Classes.Contains("selected") && b.Tag is Guid));
        }
        finally { w.Close(); }
    }

    [AvaloniaFact]
    public async Task ARebuiltListIsScrolledBeforeItIsDrawn()
    {
        var w = Window(); try
        {
            var m = await Open(w);
            for (var i = 0; i < 30; i++) { var place = Entity.Create("place", "Place " + i.ToString("00")); await m.Workspace!.EditAsync(place); }
            m.Navigate("Places"); Probe.Layout();
            var host = w.FindControl<ContentControl>("MainContent")!;
            ScrollViewer Scroll() => Probe.All<ScrollViewer>(host).First();
            Scroll().Offset = new Vector(0, 400); Probe.Layout(); var offset = Scroll().Offset.Y; Assert.True(offset > 100);
            // Rebuild synchronously and look at the new view before any queued work runs.
            w.RenderMain();
            Assert.Equal(offset, Scroll().Offset.Y, 1.0);
        }
        finally { w.Close(); }
    }

    [AvaloniaFact]
    public async Task FilesAndPeopleOfferARightClickMenu()
    {
        var w = Window(); try
        {
            var m = await Open(w);
            var alex = Entity.Create("person", "Alex"); await m.Workspace!.EditAsync(alex);
            var note = Entity.Create("note", "Packing"); await m.Workspace.EditAsync(note);
            foreach (var (view, title) in new[] { ("People", "Alex"), ("Files", "Packing") })
            {
                m.Navigate(view); Probe.Layout();
                var button = Probe.All<Button>(w.FindControl<ContentControl>("MainContent")!).First(b => b.Tag is Guid && Probe.All<TextBlock>(b).Any(t => t.Text == title));
                var menu = Assert.IsType<ContextMenu>(button.ContextMenu);
                Assert.Contains(menu.Items.OfType<MenuItem>(), i => i.Header as string == m.Strings["Delete"]);
            }
        }
        finally { w.Close(); }
    }

    [AvaloniaFact]
    public async Task AStaleLockIsExplainedAndRemovedOnRequest()
    {
        var (m, interaction, scope) = await Model(); using (scope)
        {
            var place = Entity.Create("place", "Hobbiton"); await m.Workspace!.EditAsync(place);
            var lockFile = Path.Combine(m.Workspace.Repository.Root, ".git", "index.lock"); await File.WriteAllTextAsync(lockFile, "");
            File.SetLastWriteTimeUtc(lockFile, DateTime.UtcNow.AddMinutes(-5));
            interaction.Answers.Enqueue("lock");
            await m.RunAsync(() => m.Workspace.Git.CreateVersionAsync("Add Hobbiton"));
            var problem = Assert.Single(interaction.Problems);
            Assert.Contains(problem.Actions, a => a.Id == "lock");
            // Depending on the Git version, staging or committing is the step that meets the lock.
            Assert.Matches(@"\ngit (add|commit) ", problem.Details); Assert.Contains("lock file: .git/index.lock", problem.Details);
            Assert.False(File.Exists(lockFile));
            Assert.Empty(await m.Workspace.Git.StatusAsync());
        }

    }

    [AvaloniaFact]
    public void MapLabelsCanBeTurnedOff()
    {
        var w = Window(); try
        {
            Assert.Equal("auto", MapView.LabelMode(w.Model));
            w.Model.Store.Set("map.labels", "off"); Assert.Equal("off", MapView.LabelMode(w.Model));
            w.Model.Store.Set("map.labels", "nonsense"); Assert.Equal("auto", MapView.LabelMode(w.Model));
        }
        finally { w.Model.Store.Set("map.labels", "auto"); w.Close(); }
    }
}
