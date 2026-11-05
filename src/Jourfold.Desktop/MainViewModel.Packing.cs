using Jourfold.Application;
using System.Text.Json.Nodes;
using TravelRepo.Core;
using TravelRepo.Repository;

namespace Jourfold.Desktop;

/// <summary>
/// Packing lists in Jourfold. The rules (who brings what, "everyone", done) live in TravelRepo's
/// <see cref="PackingLists"/>; this part keeps the chosen list and filter and turns clicks into undoable edits.
/// </summary>
public partial class MainViewModel
{
    /// <summary>Which items the Packing view shows: all, the local user's, or those nobody brings yet.</summary>
    public string PackingFilter
    {
        get => Store.Get("packing.filter") is { } value && value is "mine" or "unassigned" ? value : "all";
        set { Store.Set("packing.filter", value); ContentChanged?.Invoke(this, EventArgs.Empty); }
    }

    private string? PackingListKey => Workspace is null ? null : "packing.list:" + Workspace.Repository.Root;

    /// <summary>The packing list shown, remembered per trip; the first list when none was chosen.</summary>
    public PackingList? CurrentPackingList
    {
        get
        {
            if (Trip is not { } trip) return null;
            var lists = PackingLists.All(trip);
            return lists.FirstOrDefault(l => PackingListKey is { } key && l.Collection.Id.ToString() == Store.Get(key)) ?? lists.FirstOrDefault();
        }
    }

    public void ShowPackingList(Guid id) { if (PackingListKey is { } key) Store.Set(key, id.ToString()); ContentChanged?.Invoke(this, EventArgs.Empty); }

    /// <summary>Items still to pack: the local user's own when known, otherwise every item that is not done.</summary>
    public int PackingOpenCount => Trip is { } trip ? PackingLists.All(trip).SelectMany(l => l.Items).Count(i => Me is { } me ? i.IsFor(me.Id) && !i.IsPackedBy(me.Id) && !(i.Done && !i.Everyone) : !i.Done) : 0;

    public Task CreatePackingListAsync() => RunEditAsync(async () =>
    {
        if (Workspace is null || await Interaction.PromptAsync(Strings["NewPackingList"]) is not { Length: > 0 } title || title.Trim().Length == 0) return;
        var list = PackingLists.CreateList(title.Trim()); await Workspace.EditAsync(list);
        ShowPackingList(list.Id); Selected = Workspace.State.Trip.Find(list.Id);
    });

    /// <summary>
    /// Adds an item from one line of text, read by <see cref="PackingInput"/> as on the phone, for example
    /// "2x Grill @Sam #Cooking". Without "@" the item is nobody's yet, or the local user's in "Mine". A line with a
    /// name nobody or several people have is not added; <paramref name="addPeople"/> adds the missing people.
    /// </summary>
    public async Task<PackingLine?> AddPackingItemAsync(Guid listId, string text, bool addPeople = false)
    {
        PackingLine? read = null;
        await RunEditAsync(async () =>
        {
            if (Workspace is null) return;
            var (line, edits) = PackingInput.Add(Workspace.State.Trip, listId, text, PackingFilter == "mine" ? Me?.Id : null, addPeople);
            read = line;
            if (edits.Length > 0) await Workspace.ApplyAsync(edits);
        });
        return read;
    }

    /// <summary>Asks who brings an item: some of the travellers, or everyone.</summary>
    public Task AssignPackingItemAsync(Guid itemId) => RunEditAsync(async () =>
    {
        if (Workspace is null || Workspace.State.Trip.Find(itemId) is not { } task || !PackingLists.IsItem(task)) return;
        var trip = Workspace.State.Trip; var item = PackingLists.Item(trip, task);
        var people = trip.Entities.Values.Where(e => e.Type == "person").OrderBy(e => e.Title, StringComparer.CurrentCulture).ToArray();
        IReadOnlyList<Choice> choices = [new("everyone", Strings["Everyone"], "users", Strings["EveryoneHint"]), .. people.Select(p => new Choice(p.Id.ToString(), p.Title + (Me?.Id == p.Id ? " (" + Strings["You"] + ")" : ""), "user"))];
        var selected = item.Everyone ? ["everyone"] : item.Assignees.Select(a => a.Id.ToString()).ToArray();
        var picked = await Interaction.PickAsync(Strings["WhoBringsIt"], string.Format(Strings["WhoBringsItBody"], task.Title), choices, selected, Strings["ApplyChoice"]);
        if (picked is null) return;
        var everyone = picked.Contains("everyone");
        await Workspace.EditAsync(PackingLists.Assign(task, everyone ? [] : picked.Select(Guid.Parse).ToArray(), everyone));
    });

    public Task TogglePackedAsync(Guid itemId, Guid person) => RunEditAsync(async () =>
    {
        if (Workspace is null || Workspace.State.Trip.Find(itemId) is not { } task || !PackingLists.IsItem(task)) return;
        var item = PackingLists.Item(Workspace.State.Trip, task);
        await Workspace.EditAsync(PackingLists.SetPacked(Workspace.State.Trip, task, person, !item.IsPackedBy(person)));
    });

    public Task DescribePackingItemAsync(Guid itemId) => RunEditAsync(async () =>
    {
        if (Workspace is null || Workspace.State.Trip.Find(itemId) is not { } task || !PackingLists.IsItem(task)) return;
        var item = PackingLists.Item(Workspace.State.Trip, task);
        var values = await Interaction.FormAsync(task.Title, [new("group", Strings["PackingGroup"], Value: item.Group ?? "", Hint: Strings["PackingGroupHint"]), new("quantity", Strings["Quantity"], Value: item.Quantity ?? "")]);
        if (values is null) return;
        await Workspace.EditAsync(PackingLists.Describe(task, values["group"], values["quantity"]));
    });

    /// <summary>The packing lists an item could move to: those it is not on yet.</summary>
    public IReadOnlyList<PackingList> PackingListsToMoveTo(Guid itemId) =>
        Trip is { } trip ? PackingLists.All(trip).Where(l => l.Items.All(i => i.Task.Id != itemId)).ToArray() : [];

    /// <summary>Asks for another list and moves the item there, off every list it was on, as one undoable step.</summary>
    public Task MovePackingItemAsync(Guid itemId) => RunEditAsync(async () =>
    {
        if (Workspace is null || Workspace.State.Trip.Find(itemId) is not { } task || !PackingLists.IsItem(task)) return;
        var targets = PackingListsToMoveTo(itemId);
        if (targets.Count == 0) return;
        var choice = await Interaction.ChooseAsync(string.Format(Strings["MoveToPackingListBody"], task.Title), targets.Select(l => new Choice(l.Collection.Id.ToString(), l.Collection.Title, "backpack")).ToArray());
        if (choice is null || targets.FirstOrDefault(l => l.Collection.Id.ToString() == choice) is not { } target) return;
        await Workspace.ApplyAsync([.. PackingLists.MoveItem(Workspace.State.Trip, itemId, target.Collection.Id).Select(l => new EntityEdit(l.Id, l))]);
        Interaction.Toast(string.Format(Strings["MovedToPackingList"], target.Collection.Title), Strings["Undo"], () => UndoCommand.ExecuteAsync(null));
    });

    /// <summary>Deletes a packing list together with its items that no other list uses, as one undoable step.</summary>
    public Task DeletePackingListAsync(Guid listId) => RunEditAsync(async () =>
    {
        if (Workspace is null || Workspace.State.Trip.Find(listId) is not { } list || !PackingLists.IsList(list)) return;
        var trip = Workspace.State.Trip; var resolved = PackingLists.Resolve(trip, list);
        if (!await Interaction.ConfirmAsync(Strings["DeletePackingList"], resolved.Items.Count == 0 ? string.Format(Strings["DeleteEmptyPackingList"], list.Title) : string.Format(Strings["DeletePackingListBody"], list.Title, resolved.Items.Count), Strings["Delete"], danger: true)) return;
        var (removed, updated) = PackingLists.DeleteList(trip, listId);
        await Workspace.ApplyAsync([.. removed.Select(id => new EntityEdit(id, null)), .. updated.Select(e => new EntityEdit(e.Id, e))]);
        if (Selected is { } selected && removed.Contains(selected.Id)) Selected = null;
        Interaction.Toast(string.Format(Strings["PackingListDeleted"], list.Title), Strings["Undo"], () => UndoCommand.ExecuteAsync(null));
    });
}
