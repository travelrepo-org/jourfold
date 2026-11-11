using System.Text.Json.Nodes;
using NodaTime;
using TravelRepo.Core;
using TravelRepo.Git;
using TravelRepo.Merge;
using TravelRepo.Repository;
namespace Jourfold.Application;

/// <summary>Client orchestration. Every ordinary mutation uses the validated public repository command API.</summary>
public sealed class Workspace : IDisposable
{
    public TravelRepository Repository { get; }
    public GitRepository Git { get; }
    public RepositoryState State { get; private set; } = null!;
    private sealed record ActionBatch(EntityEdit[] Undo, EntityEdit[] Redo, ResourceEdit[] UndoResources, ResourceEdit[] RedoResources);
    private readonly Stack<ActionBatch> undo = new(), redo = new();
    private readonly FileStream ownership;
    public bool CanUndo => undo.Count > 0;
    public bool CanRedo => redo.Count > 0;
    public event EventHandler? Changed;
    public Workspace(string path, IGitBackend backend)
    {
        Repository = new(path); Directory.CreateDirectory(Repository.RecoveryRoot);
        try { ownership = new FileStream(Path.Combine(Repository.RecoveryRoot, "jourfold.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException) { throw new DomainException("workspace.owned", "This trip is already open in another writable workspace."); }
        Git = new(Repository, backend);
    }
    public async Task OpenAsync(CancellationToken ct = default) { await Repository.RecoverAsync(ct); State = await Repository.ReadAsync(ct); Changed?.Invoke(this, EventArgs.Empty); }
    public async Task<bool> ReloadAsync(CancellationToken ct = default)
    {
        var next = await Repository.ReadAsync(ct); var changed = !TravelRepository.SameFiles(State, next);
        State = next; if (changed) { undo.Clear(); redo.Clear(); Changed?.Invoke(this, EventArgs.Empty); }
        return changed;
    }
    public async Task EditAsync(Entity entity, CancellationToken ct = default) => await ApplyAsync([new(entity.Id, entity)], ct);
    public async Task ApplyAsync(EntityEdit[] edits, CancellationToken ct = default, ResourceEdit[]? resources = null)
    {
        var joined = Joining(edits);
        // Expenses that named nobody were shared by the travellers so far; they keep that when someone joins, in the same step.
        if (joined.Count > 0) edits = [.. edits, .. Expenses.FixShares(State.Trip).Where(e => edits.All(x => x.Id != e.Id)).Select(e => new EntityEdit(e.Id, e))];
        var inverse = edits.Select(e => new EntityEdit(e.Id, State.Trip.Find(e.Id)?.Copy())).ToArray();
        var inverseResources = (resources ?? []).Select(r => new ResourceEdit(r.Path, State.Trip.Resources.GetValueOrDefault(r.Path)?.ToArray())).ToArray();
        State = await Repository.ApplyAsync(State, edits, ct, resources); undo.Push(new(inverse, edits.Select(e => new EntityEdit(e.Id, e.Value?.Copy())).ToArray(), inverseResources, (resources ?? []).Select(r => new ResourceEdit(r.Path, r.Bytes?.ToArray())).ToArray())); redo.Clear(); Changed?.Invoke(this, EventArgs.Empty);
        if (joined.Count > 0) TravellersJoined?.Invoke(this, joined);
    }

    /// <summary>
    /// Raised after travellers joined the trip. Earlier expenses stay with the people who shared them; a client may
    /// offer to let the new travellers share some of them.
    /// </summary>
    public event EventHandler<IReadOnlyList<Guid>>? TravellersJoined;

    /// <summary>The travellers that <paramref name="edits"/> add to the trip.</summary>
    private IReadOnlyList<Guid> Joining(EntityEdit[] edits)
    {
        var trip = State.Trip;
        if (!edits.Any(e => e.Id == trip.Manifest.Id || e.Value?.Type == "person")) return [];
        var manifest = edits.LastOrDefault(e => e.Id == trip.Manifest.Id)?.Value ?? trip.Manifest;
        var entities = trip.Entities.ToDictionary(p => p.Key, p => p.Value);
        foreach (var e in edits.Where(e => e.Id != trip.Manifest.Id)) { if (e.Value is null) entities.Remove(e.Id); else entities[e.Id] = e.Value; }
        var before = TripPeople.Travelers(trip).Select(p => p.Id).ToHashSet();
        return TripPeople.Travelers(trip with { Manifest = manifest, Entities = entities }).Select(p => p.Id).Where(id => !before.Contains(id)).ToArray();
    }
    public async Task UndoAsync(CancellationToken ct = default)
    { if (!undo.TryPeek(out var action)) return; State = await Repository.ApplyAsync(State, action.Undo, ct, action.UndoResources); undo.Pop(); redo.Push(action); Changed?.Invoke(this, EventArgs.Empty); }
    public async Task RedoAsync(CancellationToken ct = default)
    { if (!redo.TryPeek(out var action)) return; State = await Repository.ApplyAsync(State, action.Redo, ct, action.RedoResources); redo.Pop(); undo.Push(action); Changed?.Invoke(this, EventArgs.Empty); }
    public async Task<Entity> ImportDocumentAsync(string path, string mediaType, bool allowLarge = false, CancellationToken ct = default)
    {
        var import = await TravelRepository.PrepareDocumentAsync(path, mediaType, allowLarge, ct);
        await ApplyAsync([new(import.Document.Id, import.Document)], ct, [import.Resource]); return import.Document;
    }
    public async Task ReplaceDocumentAsync(Guid id, string path, string mediaType, bool allowLarge = false, CancellationToken ct = default)
    {
        var existing = State.Trip.Find(id)?.Copy() ?? throw new DomainException("document.missing", "Document not found.");
        if (existing.Type != "document") throw new DomainException("document.type", "Choose a document to replace.");
        var import = await TravelRepository.PrepareDocumentAsync(path, mediaType, allowLarge, ct); existing.Data["blob"] = import.Document.Data["blob"]!.DeepClone(); existing.Data["media_type"] = mediaType;
        await ApplyAsync([new(id, existing)], ct, [import.Resource]);
    }
    /// <summary>
    /// Saves an upgrade of the trip's costs (see <see cref="CostUpgrade"/>) as one undoable step. When nothing else was
    /// changed since the last version, it becomes a version of its own with <paramref name="message"/>; otherwise it
    /// joins the next version, so the travellers' own changes are never filed under it.
    /// </summary>
    public async Task UpgradeCostsAsync(CostUpgradePlan plan, string message, CancellationToken ct = default)
    {
        if (plan.IsEmpty) return;
        var clean = string.IsNullOrEmpty(await Git.StatusAsync(ct));
        await ApplyAsync(plan.Edits.Select(e => new EntityEdit(e.Id, e)).ToArray(), ct);
        if (clean) await Git.CreateVersionAsync(message, "cost-upgrade", ct);
    }
    /// <summary>Deletes an entity and removes references to it, as planned by <see cref="TripDeletion.Plan"/>. Unused places or files are kept.</summary>
    public Task DeleteAsync(Guid id, CancellationToken ct = default) { var plan = TripDeletion.Plan(State.Trip, [id]); return ApplyAsync(plan.Edits, ct, plan.ResourceEdits); }
    public async Task ScheduleAsync(Guid id, ZonedTime start, Duration duration, CancellationToken ct = default)
    {
        var e = State.Trip.Find(id)?.Copy() ?? throw new DomainException("entity.missing", "Item not found.");
        var arrivalZone = e.Data["time"]?["end"]?["timezone"]?.ToString() ?? start.Timezone;
        e.Data["time"] = new JsonObject { ["precision"] = "exact", ["start"] = start.ToJson(), ["end"] = ZonedTime.At(start.ToInstant() + duration, arrivalZone).ToJson() };
        await EditAsync(e, ct);
    }
    public Task<Guid> PlanMaterialAsync(Guid id, ZonedTime start, Duration duration, CancellationToken ct = default) => PlanMaterialAsync(id,
        new JsonObject { ["precision"] = "exact", ["start"] = start.ToJson(), ["end"] = ZonedTime.At(start.ToInstant() + duration, start.Timezone).ToJson() }, ct);
    public async Task<Guid> PlanMaterialAsync(Guid id, JsonObject? time, CancellationToken ct = default)
    {
        var source = State.Trip.Find(id) ?? throw new DomainException("entity.missing", "Item not found.");
        if (source.Type is not ("schedule_item" or "note" or "document")) throw new DomainException("schedule.material", "Choose an activity, note, or document to schedule.");
        var activity = source.Type == "schedule_item" ? source.Copy() : Entity.Create("schedule_item", source.Title);
        if (source.Type != "schedule_item") activity.Data["content"] = new JsonArray(source.Type == "note" ? new JsonObject { ["type"] = "reference", ["entity"] = id.ToString() } : new JsonObject { ["type"] = source.Data["media_type"]?.ToString().StartsWith("image/", StringComparison.Ordinal) == true ? "image" : "pdf", ["document"] = id.ToString() });
        activity.Data["time"] = time?["precision"]?.ToString() == "unscheduled" ? null : time?.DeepClone();
        // Giving an idea a time makes it part of the plan. Both changes are one edit, so one Undo reverts them.
        if (activity.Data["time"] is not null && activity.Data["status"]?.ToString() == "idea") activity.Data["status"] = "planned";
        await EditAsync(activity, ct); return activity.Id;
    }
    public async Task<MergePlan> CompareMergeAsync(string branch, CancellationToken ct = default)
    {
        var (baseline, incoming) = await MergeSidesAsync(branch, ct); return SemanticMerge.Plan(baseline, State.Trip, incoming);
    }
    /// <summary>The common ancestor and the other side of a merge with <paramref name="branch"/>, to answer its conflicts.</summary>
    public async Task<(TripSnapshot Baseline, TripSnapshot Incoming)> MergeSidesAsync(string branch, CancellationToken ct = default) =>
        (await Git.SnapshotAsync(await Git.MergeBaseAsync(branch, ct), ct), await Git.SnapshotAsync(branch, ct));
    public async Task ApplyMergeAsync(string branch, MergePlan plan, CancellationToken ct = default)
    {
        if (!string.IsNullOrEmpty(await Git.StatusAsync(ct))) throw new DomainException("git.dirty", "Create a version before merging.");
        var variants = await Git.VariantsAsync(ct); var other = variants.Single(v => v.Branch == branch || "refs/remotes/" + v.Branch == branch);
        if (plan.ExpectedCurrent is null || plan.ExpectedIncoming is null || SemanticMerge.Diff(plan.ExpectedCurrent, State.Trip).Count != 0 || SemanticMerge.Diff(plan.ExpectedIncoming, await Git.SnapshotAsync(other.Commit, ct)).Count != 0) throw new DomainException("merge.stale", "The compared plans changed. Review the merge again.");
        State = await Repository.ApplyAsync(State, plan.EditsAgainst(State.Trip), ct, plan.ResourcesAgainst(State.Trip)); Changed?.Invoke(this, EventArgs.Empty); await Git.RecordMergeAsync(other.Commit, "Merge variant: " + other.Manifest.Data["variant"]!["title"], ct); undo.Clear(); redo.Clear();
    }
    public void Dispose() => ownership.Dispose();
}
