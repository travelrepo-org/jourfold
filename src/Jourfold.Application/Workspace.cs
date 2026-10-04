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
        var inverse = edits.Select(e => new EntityEdit(e.Id, State.Trip.Find(e.Id)?.Copy())).ToArray();
        var inverseResources = (resources ?? []).Select(r => new ResourceEdit(r.Path, State.Trip.Resources.GetValueOrDefault(r.Path)?.ToArray())).ToArray();
        State = await Repository.ApplyAsync(State, edits, ct, resources); undo.Push(new(inverse, edits.Select(e => new EntityEdit(e.Id, e.Value?.Copy())).ToArray(), inverseResources, (resources ?? []).Select(r => new ResourceEdit(r.Path, r.Bytes?.ToArray())).ToArray())); redo.Clear(); Changed?.Invoke(this, EventArgs.Empty);
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
    public Task DeleteAsync(Guid id, CancellationToken ct = default) => ApplyAsync([new(id, null)], ct);
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
        var baseline = await Git.SnapshotAsync(await Git.MergeBaseAsync(branch, ct), ct); var incoming = await Git.SnapshotAsync(branch, ct); return SemanticMerge.Plan(baseline, State.Trip, incoming);
    }
    public async Task ApplyMergeAsync(string branch, MergePlan plan, CancellationToken ct = default)
    {
        if (!string.IsNullOrEmpty(await Git.StatusAsync(ct))) throw new DomainException("git.dirty", "Create a version before merging.");
        var variants = await Git.VariantsAsync(ct); var other = variants.Single(v => v.Branch == branch || "refs/remotes/" + v.Branch == branch);
        if (plan.ExpectedCurrent is null || plan.ExpectedIncoming is null || SemanticMerge.Diff(plan.ExpectedCurrent, State.Trip).Count != 0 || SemanticMerge.Diff(plan.ExpectedIncoming, await Git.SnapshotAsync(other.Commit, ct)).Count != 0) throw new DomainException("merge.stale", "The compared plans changed. Review the merge again.");
        State = await Repository.ApplyAsync(State, plan.EditsAgainst(State.Trip), ct, plan.ResourcesAgainst(State.Trip)); Changed?.Invoke(this, EventArgs.Empty); await Git.RecordMergeAsync(other.Commit, "Merge variant: " + other.Manifest.Data["variant"]!["title"], ct); undo.Clear(); redo.Clear();
    }
    public void Dispose() => ownership.Dispose();
}
