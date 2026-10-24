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
    /// Adds an item from one line of text. "@name" assigns a person, "@all" (or "@alle") everyone, and "#group" sets the
    /// group, for example "Grill @Sam #Cooking". Without "@" the item is unassigned, or the local user's in "Mine".
    /// </summary>
    public Task AddPackingItemAsync(Guid listId, string text) => RunEditAsync(async () =>
    {
        if (Workspace is null || Workspace.State.Trip.Find(listId) is not { } list || !PackingLists.IsList(list)) return;
        var (title, group, people, everyone) = ParsePackingLine(Workspace.State.Trip, text);
        if (title.Length == 0) return;
        if (people.Count == 0 && !everyone && PackingFilter == "mine" && Me is { } me) people = [me.Id];
        var (item, updated) = PackingLists.AddItem(list, title, group, null, people, everyone);
        await Workspace.ApplyAsync([new(item.Id, item), new(updated.Id, updated)]);
    });

    public static (string Title, string? Group, IReadOnlyList<Guid> People, bool Everyone) ParsePackingLine(TripSnapshot trip, string text)
    {
        var people = new List<Guid>(); var everyone = false; string? group = null; var words = new List<string>();
        var persons = trip.Entities.Values.Where(e => e.Type == "person").ToArray();
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (word.Length > 1 && word[0] == '#') { group = word[1..].Replace('_', ' '); continue; }
            if (word.Length > 1 && word[0] == '@')
            {
                var name = word[1..];
                if (name.ToLowerInvariant() is "all" or "alle" or "everyone") { everyone = true; continue; }
                var matches = persons.Where(p => p.Title.Equals(name, StringComparison.CurrentCultureIgnoreCase) || p.Title.Split(' ')[0].Equals(name, StringComparison.CurrentCultureIgnoreCase)).ToArray();
                if (matches.Length == 1) { people.Add(matches[0].Id); continue; }
            }
            words.Add(word);
        }
        return (string.Join(' ', words).Trim(), group, people, everyone);
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

    /// <summary>Deletes a packing list together with its items that no other list uses, as one undoable step.</summary>
    public Task DeletePackingListAsync(Guid listId) => RunEditAsync(async () =>
    {
        if (Workspace is null || Workspace.State.Trip.Find(listId) is not { } list || !PackingLists.IsList(list)) return;
        var trip = Workspace.State.Trip; var resolved = PackingLists.Resolve(trip, list);
        if (!await Interaction.ConfirmAsync(Strings["DeletePackingList"], string.Format(Strings["DeletePackingListBody"], list.Title, resolved.Items.Count), Strings["Delete"], danger: true)) return;
        var elsewhere = PackingLists.All(trip).Where(l => l.Collection.Id != listId).SelectMany(l => l.Items).Select(i => i.Task.Id).ToHashSet();
        var removed = resolved.Items.Select(i => i.Task.Id).Where(id => !elsewhere.Contains(id)).Append(listId).ToHashSet();
        // Anything else that links to the removed entities, such as comments, loses the link.
        var edits = removed.Select(id => new EntityEdit(id, null)).ToList();
        foreach (var source in trip.Entities.Values.Where(e => !removed.Contains(e.Id) && TripReferences.From(e).Any(r => removed.Contains(r.Target))))
            edits.Add(new(source.Id, TripReferences.Detach(source, removed)));
        await Workspace.ApplyAsync([.. edits]);
        if (Selected is { } selected && removed.Contains(selected.Id)) Selected = null;
        Interaction.Toast(string.Format(Strings["PackingListDeleted"], list.Title), Strings["Undo"], () => UndoCommand.ExecuteAsync(null));
    });
}
