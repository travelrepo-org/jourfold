using Jourfold.Application;
using TravelRepo.Core;

namespace Jourfold.Mobile;

public sealed partial class MobileSession
{
    /// <summary>What is left to pack for the user: their items and those for everyone they have not packed; without "me", all open items.</summary>
    public int OpenPacking => Trip is { } trip ? PackingLists.All(trip).SelectMany(l => l.Items).Count(i => Me is { } me ? i.IsFor(me.Id) && !i.IsPackedBy(me.Id) : !i.Done) : 0;

    /// <summary>Marks an item as packed, or not, by the user.</summary>
    public async Task SetPackedAsync(Guid item, bool packed, CancellationToken ct = default)
    {
        if (Workspace is not { } workspace || Me is not { } me || workspace.State.Trip.Find(item) is not { } task) return;
        await workspace.EditAsync(PackingLists.SetPacked(workspace.State.Trip, task, me.Id, packed), ct); NoteChange();
    }

    /// <summary>Adds an item from one line, such as "Charger #Electronics @all"; without "@" it is the user's own.</summary>
    public async Task AddPackingItemAsync(Guid list, string text, CancellationToken ct = default)
    {
        if (Workspace is not { } workspace || workspace.State.Trip.Find(list) is not { } collection || !PackingLists.IsList(collection)) return;
        var (title, group, people, everyone) = PackingInput.Parse(workspace.State.Trip, text);
        if (title.Length == 0) return;
        if (people.Count == 0 && !everyone && Me is { } me) people = [me.Id];
        var (item, updated) = PackingLists.AddItem(collection, title, group, null, people, everyone);
        await workspace.ApplyAsync([new(item.Id, item), new(updated.Id, updated)], ct); NoteChange();
    }

    /// <summary>Adds a packing list and opens it.</summary>
    public async Task AddPackingListAsync(string title, CancellationToken ct = default)
    {
        if (Workspace is not { } workspace || string.IsNullOrWhiteSpace(title)) return;
        var list = PackingLists.CreateList(title.Trim());
        await workspace.EditAsync(list, ct); NoteChange();
    }
}
