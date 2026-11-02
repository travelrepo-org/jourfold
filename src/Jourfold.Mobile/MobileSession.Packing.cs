using Jourfold.Application;
using TravelRepo.Core;
using TravelRepo.Repository;

namespace Jourfold.Mobile;

public sealed partial class MobileSession
{
    /// <summary>What is left to pack for the user: their items and those for everyone they have not packed; without "me", all open items.</summary>
    public int OpenPacking => Trip is { } trip ? PackingLists.All(trip).SelectMany(l => l.Items).Count(i => Me is { } me ? i.IsFor(me.Id) && !i.IsPackedBy(me.Id) : !i.Done) : 0;

    /// <summary>The packing list or item that the page opened by <see cref="ShowPacking"/> shows.</summary>
    public Guid? PackingShown { get; private set; }

    /// <summary>Opens a packing list's page (its name, or deleting it) or an item's page (everything about it).</summary>
    public void ShowPacking(Guid id)
    {
        if (Trip?.Find(id) is not { } entity) return;
        PackingShown = id;
        foreach (var key in Drafts.Keys.Where(k => k.StartsWith("packing.edit", StringComparison.Ordinal)).ToArray()) Drafts.Remove(key);
        OpenPage(PackingLists.IsList(entity) ? "packing.list" : "packing.item");
    }

    /// <summary>Marks an item as packed, or not, by the user.</summary>
    public Task SetPackedAsync(Guid item, bool packed, CancellationToken ct = default) => Me is { } me ? SetPackedAsync(item, me.Id, packed, ct) : Task.CompletedTask;

    /// <summary>Marks an item as packed, or not, by one of the people who bring it, so one phone can keep the list for everyone.</summary>
    public Task SetPackedAsync(Guid item, Guid person, bool packed, CancellationToken ct = default) => ChangeAsync(async () =>
    {
        if (Workspace is not { } workspace || workspace.State.Trip.Find(item) is not { } task || !PackingLists.IsItem(task)) return;
        await workspace.EditAsync(PackingLists.SetPacked(workspace.State.Trip, task, person, packed), ct); NoteChange();
    }, ct);

    /// <summary>The user brings an item that nobody brought yet.</summary>
    public Task TakePackingItemAsync(Guid item, CancellationToken ct = default) => ChangeAsync(async () =>
    {
        if (Workspace is not { } workspace || Me is not { } me || workspace.State.Trip.Find(item) is not { } task || !PackingLists.IsItem(task)) return;
        await workspace.EditAsync(PackingLists.Assign(task, [me.Id], everyone: false), ct); NoteChange();
    }, ct);

    /// <summary>Saves an item's page: title, group, quantity, who brings it and the list it is on.</summary>
    public Task SavePackingItemAsync(Guid item, string title, string? group, string? quantity, IReadOnlyList<Guid> people, bool everyone, Guid list, CancellationToken ct = default) => ChangeAsync(async () =>
    {
        if (Workspace is not { } workspace || workspace.State.Trip.Find(item) is not { } task || !PackingLists.IsItem(task)) return;
        var trip = workspace.State.Trip;
        var changed = PackingLists.Assign(PackingLists.Describe(PackingLists.Rename(task, title), group, quantity), people, everyone);
        await workspace.ApplyAsync([new(changed.Id, changed), .. PackingLists.MoveItem(trip, item, list).Select(l => new EntityEdit(l.Id, l))], ct); NoteChange();
        if (Page == "packing.item") GoBack();
    }, ct);

    /// <summary>Deletes an item from its list.</summary>
    public Task DeletePackingItemAsync(Guid item, CancellationToken ct = default) => ChangeAsync(async () =>
    {
        if (Workspace is not { } workspace) return;
        await workspace.DeleteAsync(item, ct); NoteChange();
        if (Page == "packing.item") GoBack();
    }, ct);

    public Task RenamePackingListAsync(Guid list, string title, CancellationToken ct = default) => ChangeAsync(async () =>
    {
        if (Workspace is not { } workspace || workspace.State.Trip.Find(list) is not { } collection || !PackingLists.IsList(collection)) return;
        await workspace.EditAsync(PackingLists.Rename(collection, title), ct); NoteChange();
        if (Page == "packing.list") GoBack();
    }, ct);

    /// <summary>Deletes a list and its items; items that are also on another list stay there.</summary>
    public Task DeletePackingListAsync(Guid list, CancellationToken ct = default) => ChangeAsync(async () =>
    {
        if (Workspace is not { } workspace) return;
        var (removed, updated) = PackingLists.DeleteList(workspace.State.Trip, list);
        await workspace.ApplyAsync([.. removed.Select(id => new EntityEdit(id, null)), .. updated.Select(e => new EntityEdit(e.Id, e))], ct); NoteChange();
        if (Page == "packing.list") GoBack();
    }, ct);

    /// <summary>
    /// Adds an item from one line, such as "2x Charger #Electronics @all"; without "@" it is the user's own. A line
    /// with a name nobody or several people have is not added; <paramref name="addPeople"/> adds the missing people.
    /// </summary>
    public Task<PackingLine?> AddPackingItemAsync(Guid list, string text, bool addPeople = false, CancellationToken ct = default) => ChangeAsync<PackingLine?>(async () =>
    {
        if (Workspace is not { } workspace) return null;
        var (line, edits) = PackingInput.Add(workspace.State.Trip, list, text, Me?.Id, addPeople);
        if (edits.Length == 0) return line;
        await workspace.ApplyAsync(edits, ct); NoteChange();
        Drafts.Remove("packing:" + list);
        return line;
    }, ct);

    /// <summary>Adds a packing list, ready for its first item.</summary>
    public Task AddPackingListAsync(string title, CancellationToken ct = default) => ChangeAsync(async () =>
    {
        if (Workspace is not { } workspace || string.IsNullOrWhiteSpace(title)) return;
        var list = PackingLists.CreateList(title.Trim());
        await workspace.EditAsync(list, ct);
        FocusDraft = "packing:" + list.Id; NoteChange();
    }, ct);
}
