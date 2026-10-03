using System.Globalization;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.Input;
using TravelRepo.Core;
using TravelRepo.Git;
using TravelRepo.Merge;

namespace Jourfold.Desktop;

/// <summary>A history row prepared for display.</summary>
public sealed record HistoryItem(VersionEntry Entry, string Author, string Heading, string Message, DateTimeOffset When, bool Merge);

public partial class MainViewModel
{
    /// <summary>Changes since the last version, for Create Version.</summary>
    public async Task<ChangeSummary> PendingChangesAsync()
    {
        if (Workspace is null) return new([], []);
        var baseline = await Workspace.Git.SnapshotAsync("HEAD");
        return ChangeSummary.Between(baseline, Workspace.State.Trip);
    }

    public string SuggestVersionMessage(ChangeSummary summary)
    {
        string Name(EntityChange c) => c.Type == "trip" ? Strings["TripDetails"] : c.Title;
        if (summary.Entities.Count == 1)
        {
            var c = summary.Entities[0];
            return string.Format(Strings[c.Kind switch { ChangeKind.Added => "VersionAdd", ChangeKind.Removed => "VersionRemove", _ => "VersionUpdate" }], Name(c));
        }
        var added = summary.Entities.Where(c => c.Kind == ChangeKind.Added).ToArray();
        var updated = summary.Entities.Where(c => c.Kind == ChangeKind.Updated).ToArray();
        var removed = summary.Entities.Where(c => c.Kind == ChangeKind.Removed).ToArray();
        var parts = new List<string>();
        if (added.Length > 0) parts.Add(string.Format(Strings[added.Length == 1 ? "VersionAdd" : "VersionAddMany"], added.Length == 1 ? Name(added[0]) : added.Length));
        if (updated.Length > 0) parts.Add(string.Format(Strings[updated.Length == 1 ? "VersionUpdate" : "VersionUpdateMany"], updated.Length == 1 ? Name(updated[0]) : updated.Length));
        if (removed.Length > 0) parts.Add(string.Format(Strings[removed.Length == 1 ? "VersionRemove" : "VersionRemoveMany"], removed.Length == 1 ? Name(removed[0]) : removed.Length));
        if (parts.Count == 0) return Strings["VersionSuggestion"];
        var text = string.Join(Strings["ListSeparator"], parts);
        return char.ToUpper(text[0], CultureInfo.CurrentCulture) + text[1..];
    }

    [RelayCommand]
    private Task CreateVersion() => RunAsync(async () =>
    {
        if (Workspace is null) return;
        var summary = await PendingChangesAsync();
        if (summary.IsEmpty && string.IsNullOrEmpty(await Workspace.Git.StatusAsync())) { Interaction.Toast(Strings["NothingToVersion"]); return; }
        var message = await Interaction.CreateVersionAsync(SuggestVersionMessage(summary), summary); if (string.IsNullOrWhiteSpace(message)) return;
        await Workspace.Git.CreateVersionAsync(message.Trim()); await UpdateLocalStateAsync(); Status = Strings["Saved"];
        Interaction.Toast(Strings["VersionCreated"]); ContentChanged?.Invoke(this, EventArgs.Empty); _ = UpdateSyncStatusAsync();
    });

    public async Task<IReadOnlyList<HistoryItem>> HistoryAsync()
    {
        if (Workspace is null) return [];
        var trip = Workspace.State.Trip; var result = new List<HistoryItem>();
        foreach (var entry in await Workspace.Git.HistoryAsync())
        {
            var author = TripSummaries.PersonForGitEmail(trip, entry.Email)?.Title ?? entry.Author;
            var action = entry.Message.Split('\n').FirstOrDefault(line => line.StartsWith("TravelRepo-Action: ", StringComparison.Ordinal))?[19..];
            var heading = !entry.ApplicationGenerated ? "HistoryExternal" : action switch { "semantic-merge" => "HistoryMerge", "initialize" => "HistoryCreate", "create-variant" => "HistoryNewVariant", "variant-metadata" => "HistoryVariant", _ => "HistoryVersion" };
            var message = entry.Message.Split('\n')[0];
            result.Add(new(entry, author, heading, message, DateTimeOffset.Parse(entry.Timestamp, CultureInfo.InvariantCulture), action == "semantic-merge"));
        }
        return result;
    }

    public async Task<ChangeSummary> ChangesInAsync(VersionEntry entry)
    {
        if (Workspace is null) return new([], []);
        var after = await Workspace.Git.SnapshotAsync(entry.Commit); var parent = entry.Parents.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (parent is null) return ChangeSummary.Between(new TripSnapshot(after.Manifest, new Dictionary<Guid, Entity>()), after);
        return ChangeSummary.Between(await Workspace.Git.SnapshotAsync(parent), after);
    }

    public Task RestoreVersionAsync(VersionEntry entry) => RunAsync(async () =>
    {
        if (Workspace is null) return;
        var plan = await Workspace.Repository.PreviewRestoreAsync(await Workspace.Git.SnapshotAsync(entry.Commit));
        if (plan.Changes.Count == 0) { Interaction.Toast(Strings["AlreadyThisVersion"]); return; }
        if (!await Interaction.ConfirmAsync(Strings["RestoreVersion"], string.Format(Strings["RestoreVersionBody"], entry.Message.Split('\n')[0], plan.Changes.Count), Strings["Restore"])) return;
        await Workspace.Repository.ApplyRestoreAsync(plan); await Workspace.ReloadAsync(); Refresh(); await UpdateLocalStateAsync();
        Interaction.Toast(Strings["Restored"]);
    });

    public async Task<IReadOnlyList<Variant>> VariantsAsync() => Workspace is null ? [] : await Workspace.Git.VariantsAsync();

    [RelayCommand]
    private Task NewVariant() => RunAsync(async () =>
    {
        if (Workspace is null) return;
        if (!string.IsNullOrEmpty(await Workspace.Git.StatusAsync()))
        {
            if (!await Interaction.ConfirmAsync(Strings["NewVariant"], Strings["VariantNeedsVersion"], Strings["CreateVersionAndContinue"])) return;
            var summary = await PendingChangesAsync(); await Workspace.Git.CreateVersionAsync(SuggestVersionMessage(summary));
        }
        var values = await Interaction.FormAsync(Strings["NewVariant"], [new("title", Strings["VariantTitle"], Required: true, Hint: Strings["VariantHint"])]); if (values is null) return;
        await Workspace.Git.CreateVariantAsync("variants/" + Slug(values["title"]) + "-" + Guid.CreateVersion7().ToString()[..8], values["title"]); await Workspace.ReloadAsync(); await UpdateLocalStateAsync(); Refresh();
        Interaction.Toast(string.Format(Strings["VariantCreated"], values["title"]));
    });
    private static string Slug(string title) { var slug = new string(title.ToLowerInvariant().Select(c => char.IsAsciiLetterOrDigit(c) ? c : '-').ToArray()).Trim('-'); while (slug.Contains("--")) slug = slug.Replace("--", "-"); return slug.Length == 0 ? "variant" : slug[..Math.Min(32, slug.Length)]; }

    public Task VariantActionAsync(Variant variant, string action) => RunAsync(async () =>
    {
        if (Workspace is null) return;
        if (action == "ShareVariant") { await ShareVariantAsync(variant); return; }
        if (action == "Switch")
        {
            await EnsureCleanAsync(); await Workspace.Git.SwitchAsync(variant.Branch); await Workspace.ReloadAsync(); await UpdateLocalStateAsync(); Selected = null; Refresh();
            Interaction.Toast(string.Format(Strings["SwitchedTo"], variant.Manifest.Data["variant"]?["title"])); return;
        }
        if (action == "Compare") { var other = await Workspace.Git.SnapshotAsync(variant.Branch); await Interaction.CompareAsync(Workspace.State.Trip, other, VariantTitle, variant.Manifest.Data["variant"]?["title"]?.ToString() ?? variant.Branch); return; }
        if (action == "Merge") { await EnsureCleanAsync(); await MergeAsync(variant.Branch, variant.Manifest.Data["variant"]?["title"]?.ToString() ?? variant.Branch); return; }
        await EnsureCleanAsync();
        var current = await Workspace.Git.CurrentBranchAsync();
        if (action == "Rename")
        {
            var values = await Interaction.FormAsync(Strings["Rename"], [new("title", Strings["VariantTitle"], Value: variant.Manifest.Data["variant"]!["title"]!.ToString(), Required: true)]); if (values is null) return;
            await Workspace.Git.SwitchAsync(variant.Branch); await Workspace.Git.UpdateVariantAsync(values["title"]);
        }
        if (action == "Archive")
        {
            if (variant.Branch == current) { await Interaction.ShowAsync(Strings["Archive"], Strings["ArchiveCurrent"]); return; }
            await Workspace.Git.SwitchAsync(variant.Branch); await Workspace.Git.UpdateVariantAsync(archive: true);
        }
        if (await Workspace.Git.CurrentBranchAsync() != current) await Workspace.Git.SwitchAsync(current);
        await Workspace.ReloadAsync(); Refresh();
    });

    /// <summary>Variant operations need a clean tree. Offer to create a version instead of failing.</summary>
    private async Task EnsureCleanAsync()
    {
        if (Workspace is null || string.IsNullOrEmpty(await Workspace.Git.StatusAsync())) return;
        if (!await Interaction.ConfirmAsync(Strings["UnversionedChanges"], Strings["UnversionedChangesBody"], Strings["CreateVersionAndContinue"])) throw new OperationCanceledException();
        await Workspace.Git.CreateVersionAsync(SuggestVersionMessage(await PendingChangesAsync()));
    }

    private async Task MergeAsync(string branch, string otherTitle)
    {
        if (Workspace is null) return;
        var baseline = await Workspace.Git.SnapshotAsync(await Workspace.Git.MergeBaseAsync(branch)); var incoming = await Workspace.Git.SnapshotAsync(branch);
        var plan = SemanticMerge.Plan(baseline, Workspace.State.Trip, incoming);
        var resolutions = new Dictionary<string, JsonNode?>(); var duplicates = new Dictionary<Guid, Entity>();
        if (plan.Conflicts.Count > 0)
        {
            var prompts = plan.Conflicts.Select(conflict =>
            {
                var title = Workspace.State.Trip.Find(conflict.Entity)?.Title ?? incoming.Find(conflict.Entity)?.Title ?? Strings["Files"];
                var actions = conflict.Entity == Guid.Empty && conflict.Kind == ConflictKind.Binary ? new[] { "UseCurrent", "UseVariant" } : conflict.Entity != Guid.Empty && incoming.Find(conflict.Entity) is { Type: not "trip" } ? new[] { "UseCurrent", "UseVariant", "KeepBoth", "EditResult" } : new[] { "UseCurrent", "UseVariant", "EditResult" };
                var current = conflict.Kind == ConflictKind.Binary ? Strings["BinaryConflict"] : DescribeValue(conflict.Current, Workspace.State.Trip);
                var other = conflict.Kind == ConflictKind.Binary ? Strings["BinaryConflict"] : DescribeValue(conflict.Incoming, incoming);
                return new ConflictPrompt(title, FieldLabel(conflict.Path), current, other, actions, conflict.Current?.ToString() ?? "");
            }).ToArray();
            var answers = await Interaction.ResolveConflictsAsync(prompts, VariantTitle, otherTitle); if (answers is null) return;
            var keptBoth = new HashSet<Guid>();
            for (var i = 0; i < plan.Conflicts.Count; i++)
            {
                var conflict = plan.Conflicts[i]; var answer = answers[i]; var key = conflict.Entity + conflict.Path;
                if (answer.Action == "KeepBoth" || keptBoth.Contains(conflict.Entity))
                {
                    if (keptBoth.Add(conflict.Entity)) foreach (var duplicate in TripCommands.DuplicateSubtree(incoming, conflict.Entity)) duplicates[duplicate.Id] = duplicate;
                    resolutions[key] = conflict.Current?.DeepClone(); continue;
                }
                if (answer.Action == "EditResult") resolutions[key] = conflict.Current is JsonObject or JsonArray || conflict.Incoming is JsonObject or JsonArray ? JsonNode.Parse(answer.Text ?? "null") : JsonValue.Create(answer.Text ?? "");
                else resolutions[key] = (answer.Action == "UseCurrent" ? conflict.Current : conflict.Incoming)?.DeepClone();
            }
            plan = SemanticMerge.Plan(baseline, Workspace.State.Trip, incoming, resolutions);
            if (duplicates.Count > 0) { var all = plan.Result.Entities.ToDictionary(p => p.Key, p => p.Value); foreach (var duplicate in duplicates) all[duplicate.Key] = duplicate.Value; plan = plan with { Result = plan.Result with { Entities = all } }; }
        }
        else if (!await Interaction.ConfirmAsync(Strings["Merge"], string.Format(Strings["MergeNoConflicts"], otherTitle, ChangeSummary.Between(Workspace.State.Trip, plan.Result).Count), Strings["Merge"])) return;
        await Workspace.ApplyMergeAsync(branch, plan);
        var currentBranch = await Workspace.Git.CurrentBranchAsync();
        if ((await Workspace.Git.VariantsAsync()).Any(v => v.Branch == branch && !v.IsRemote && v.Manifest.Data["variant"]?["role"]?.ToString() == "variant")) { await Workspace.Git.SwitchAsync(branch); await Workspace.Git.UpdateVariantAsync(archive: true); await Workspace.Git.SwitchAsync(currentBranch); await Workspace.ReloadAsync(); }
        Refresh(); Status = Strings["Merged"]; Interaction.Toast(string.Format(Strings["MergedInto"], otherTitle, VariantTitle));
    }

    public string FieldLabel(string path)
    {
        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return Strings["WholeItem"];
        if (parts[0] == "resources") return Strings["Content"];
        var key = parts[0] == "components" && parts.Length > 1 ? parts[1] : parts[0];
        return Strings["field." + key] is var label && label != "field." + key ? label : key.Replace('_', ' ');
    }

    public string DescribeValue(JsonNode? value, TripSnapshot trip)
    {
        if (value is null) return Strings["Removed"];
        if (value is JsonArray array) return array.Count == 0 ? Strings["Empty"] : string.Join(", ", array.Select(v => DescribeValue(v, trip)));
        if (value is JsonObject obj)
        {
            if (obj["local"] is { } local && obj["timezone"] is { } zone && NodaTime.Text.LocalDateTimePattern.ExtendedIso.Parse(local.ToString()).TryGetValue(default, out var wall)) return wall.ToString("g", CultureInfo.CurrentCulture) + " (" + Formats.ZoneName(zone.ToString()) + ")";
            if (TripSummaries.Money(obj) is { } money) return Formats.Money(money.Amount, money.Currency);
            return string.Join("; ", obj.Where(p => p.Key is not ("id" or "extensions") && p.Value is not null).Select(p => FieldLabel(p.Key) + ": " + DescribeValue(p.Value, trip)));
        }
        return Guid.TryParse(value.ToString(), out var id) && trip.Find(id) is { } entity ? entity.Title : Strings.Optional(value.ToString());
    }
}
