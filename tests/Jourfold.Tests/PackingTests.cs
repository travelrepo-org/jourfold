using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Jourfold.Desktop;
using Jourfold.Infrastructure;
using TravelRepo.Core;
using TravelRepo.Git;
using TravelRepo.Repository;
using Xunit;
namespace Jourfold.Tests;

/// <summary>Packing lists in Jourfold: quick add with people and groups, who brings what, packing, filters.</summary>
public sealed class PackingTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "jourfold-packing-" + Guid.NewGuid());
    public void Dispose() => TestFiles.Delete(root);

    private async Task<(string Path, Entity Ana, Entity Ben)> Trip()
    {
        var repository = new TravelRepository(Path.Combine(root, "trip")); await repository.InitializeAsync(Entity.CreateTrip("Lake weekend", "en", "Europe/Berlin"));
        var ana = Entity.Create("person", "Ana Silva"); var ben = Entity.Create("person", "Ben");
        await repository.ApplyAsync(await repository.ReadAsync(), [new(ana.Id, ana), new(ben.Id, ben)]);
        await new GitRepository(repository, new GitCliBackend()).InitializeAsync("Test", "test@example.invalid");
        return (repository.Root, ana, ben);
    }

    [AvaloniaFact]
    public async Task ItemsAreAddedAssignedAndPackedPerPerson()
    {
        using var store = new LocalStore(Path.Combine(root, "local")); var interaction = new TestInteraction();
        using var m = new MainViewModel(store, new GitCliBackend(), new OsSecretStore(), interaction);
        var (path, ana, ben) = await Trip(); await m.OpenAsync(path);

        Assert.Null(m.CurrentPackingList);
        interaction.Answers.Enqueue("Barbecue"); await m.CreatePackingListAsync();
        var list = m.CurrentPackingList!; Assert.Equal("Barbecue", list.Collection.Title);

        // "@first name", "@all" and "#group" in one line.
        await m.AddPackingItemAsync(list.Collection.Id, "Grill @ana #Cooking");
        await m.AddPackingItemAsync(list.Collection.Id, "Drinks @all");
        await m.AddPackingItemAsync(list.Collection.Id, "Plates");
        list = m.CurrentPackingList!;
        Assert.Equal(["Grill", "Drinks", "Plates"], list.Items.Select(i => i.Task.Title));
        Assert.Equal([ana.Id], list.Items[0].Assignees.Select(a => a.Id)); Assert.Equal("Cooking", list.Items[0].Group);
        Assert.True(list.Items[1].Everyone); Assert.True(list.Items[2].Unassigned);

        // Plates: Ben is chosen in the dialog.
        interaction.Pick = (choices, _) => { Assert.Equal("everyone", choices[0].Id); return [ben.Id.ToString()]; };
        await m.AssignPackingItemAsync(list.Items[2].Task.Id);
        list = m.CurrentPackingList!; Assert.Empty(PackingLists.Unassigned(list));

        await m.TogglePackedAsync(list.Items[1].Task.Id, ana.Id);
        list = m.CurrentPackingList!; Assert.True(list.Items[1].IsPackedBy(ana.Id)); Assert.False(list.Items[1].Done);
        await m.TogglePackedAsync(list.Items[1].Task.Id, ben.Id);
        Assert.True(m.CurrentPackingList!.Items[1].Done);

        // Packing items stay out of the task list, and the list is gone with its items in one undoable step.
        Assert.Equal(3, m.Workspace!.State.Trip.Entities.Values.Count(PackingLists.IsItem));
        await m.DeletePackingListAsync(list.Collection.Id);
        Assert.DoesNotContain(m.Workspace.State.Trip.Entities.Values, e => PackingLists.IsItem(e) || PackingLists.IsList(e));
        await m.UndoCommand.ExecuteAsync(null);
        Assert.Equal(3, m.CurrentPackingList!.Items.Count);
    }

    [AvaloniaFact]
    public async Task TheViewFiltersMineAndUnassignedAndChipsTogglePacking()
    {
        var w = new MainWindow(); w.Model.SetPreference("Language", "en"); w.Show(); Probe.Layout();
        try
        {
            var m = w.Model; var (path, ana, ben) = await Trip(); await m.OpenAsync(path); m.InboxOpen = false;
            m.Store.Set("me:" + m.Workspace!.Repository.Root, ana.Id.ToString());
            var list = PackingLists.CreateList("Camping");
            (var tent, list) = PackingLists.AddItem(list, "Tent", "Gear", null, [ana.Id]);
            (var stove, list) = PackingLists.AddItem(list, "Stove", "Gear", null, [ben.Id]);
            (var torch, list) = PackingLists.AddItem(list, "Torch");
            await m.Workspace.ApplyAsync([new(list.Id, list), new(tent.Id, tent), new(stove.Id, stove), new(torch.Id, torch)]);
            m.PackingFilter = "all"; m.Navigate("Packing"); Probe.Layout();
            string[] Rows() => [.. Probe.All<Button>(w.FindControl<ContentControl>("MainContent")!).Where(b => b.Classes.Contains("row")).Select(b => AutomationProperties.GetName(b)!)];
            Assert.Equal(["Tent", "Stove", "Torch"], Rows());

            Probe.Click(w, "Mine"); Probe.Layout(); Assert.Equal(["Tent"], Rows());
            Probe.Click(w, "Nobody yet (1)"); Probe.Layout(); Assert.Equal(["Torch"], Rows());
            Probe.Click(w, "All"); Probe.Layout();

            // Ben's chip on the stove marks it packed for him; Ana's own check box does the same for her.
            Probe.Click(w, "Ben has not packed it yet");
            await Probe.Until(() => PackingLists.Item(m.Workspace.State.Trip, m.Workspace.State.Trip.Find(stove.Id)!).IsPackedBy(ben.Id));
            Probe.Layout();
            var check = Probe.All<CheckBox>(w).Single(c => AutomationProperties.GetName(c) == "Packed: Tent"); check.IsChecked = true;
            await Probe.Until(() => PackingLists.Item(m.Workspace.State.Trip, m.Workspace.State.Trip.Find(tent.Id)!).IsPackedBy(ana.Id));
            Assert.Equal(0, m.PackingOpenCount); // Ana has packed everything she brings
        }
        finally { w.Model.PackingFilter = "all"; w.Close(); }
    }
}
