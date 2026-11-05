using System.Text.Json.Nodes;
using TravelRepo.Core;
using TravelRepo.Merge;

namespace Jourfold.Application;

/// <summary>
/// Turns the user's answers to merge conflicts into a merge plan, the same way on the desktop and the phone: keep this
/// side, take the other side, keep both (the other side's item is added as a copy), or a value written by hand.
/// </summary>
public static class MergeAnswers
{
    public const string UseCurrent = "UseCurrent", UseOther = "UseVariant", KeepBoth = "KeepBoth", EditResult = "EditResult";

    /// <summary>What can be done with a conflict: files only choose a side; items can also be kept twice.</summary>
    public static IReadOnlyList<string> Actions(MergeConflict conflict, TripSnapshot incoming, bool canEdit = true) =>
        conflict.Entity == Guid.Empty && conflict.Kind == ConflictKind.Binary ? [UseCurrent, UseOther]
        : conflict.Entity != Guid.Empty && incoming.Find(conflict.Entity) is { Type: not "trip" } ? canEdit ? [UseCurrent, UseOther, KeepBoth, EditResult] : [UseCurrent, UseOther, KeepBoth]
        : canEdit ? [UseCurrent, UseOther, EditResult] : [UseCurrent, UseOther];

    /// <summary>The plan with every conflict answered; <paramref name="answers"/> follow the order of the plan's conflicts.</summary>
    public static MergePlan Resolve(TripSnapshot baseline, TripSnapshot current, TripSnapshot incoming, MergePlan plan, IReadOnlyList<(string Action, string? Text)> answers)
    {
        var resolutions = new Dictionary<string, JsonNode?>(); var duplicates = new Dictionary<Guid, Entity>(); var keptBoth = new HashSet<Guid>();
        for (var i = 0; i < plan.Conflicts.Count; i++)
        {
            var conflict = plan.Conflicts[i]; var (action, text) = answers[i]; var key = conflict.Entity + conflict.Path;
            if (action == KeepBoth || keptBoth.Contains(conflict.Entity))
            {
                if (keptBoth.Add(conflict.Entity)) foreach (var duplicate in TripCommands.DuplicateSubtree(incoming, conflict.Entity)) duplicates[duplicate.Id] = duplicate;
                resolutions[key] = conflict.Current?.DeepClone(); continue;
            }
            if (action == EditResult) resolutions[key] = conflict.Current is JsonObject or JsonArray || conflict.Incoming is JsonObject or JsonArray ? JsonNode.Parse(text ?? "null") : JsonValue.Create(text ?? "");
            else resolutions[key] = (action == UseCurrent ? conflict.Current : conflict.Incoming)?.DeepClone();
        }
        var resolved = SemanticMerge.Plan(baseline, current, incoming, resolutions);
        if (duplicates.Count == 0) return resolved;
        var all = resolved.Result.Entities.ToDictionary(p => p.Key, p => p.Value);
        foreach (var duplicate in duplicates) all[duplicate.Key] = duplicate.Value;
        return resolved with { Result = resolved.Result with { Entities = all } };
    }
}
