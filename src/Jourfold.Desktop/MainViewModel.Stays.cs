using System.Text.Json.Nodes;
using NodaTime;
using TravelRepo.Core;
using TravelRepo.Repository;

namespace Jourfold.Desktop;

/// <summary>
/// Stays: moving them by nights, keeping their time in line with check-in and check-out, and merging one item per
/// night into one stay. The rules live in TravelRepo's <see cref="Stays"/>; this only orchestrates undoable edits.
/// </summary>
public partial class MainViewModel
{
    /// <summary>Moves a stay by whole nights, keeping its length and its check-in and check-out hours.</summary>
    public Task MoveStayAsync(Guid id, int nights) => RunEditAsync(async () =>
    {
        if (Workspace is null || Workspace.State.Trip.Find(id) is not { } item || Stays.Resolve(Workspace.State.Trip, item) is not { FirstNight: { } first } stay) return;
        await Workspace.EditAsync(Stays.WithNights(Workspace.State.Trip, item, first.PlusDays(nights), Math.Max(1, stay.Nights)));
    });

    /// <summary>Sets how many nights a stay covers, keeping its first night and its hours.</summary>
    public Task SetStayNightsAsync(Guid id, int nights) => RunEditAsync(async () =>
    {
        if (Workspace is null || Workspace.State.Trip.Find(id) is not { } item || Stays.Resolve(Workspace.State.Trip, item) is not { FirstNight: { } first }) return;
        await Workspace.EditAsync(Stays.WithNights(Workspace.State.Trip, item, first, Math.Max(1, nights)));
    });

    /// <summary>Makes a stay's general time, check-in and check-out agree; check-in and check-out win.</summary>
    public Task AlignStayAsync(Guid id) => RunEditAsync(async () =>
    {
        if (Workspace is null || Workspace.State.Trip.Find(id) is not { } item) return;
        await Workspace.EditAsync(Stays.Align(Workspace.State.Trip, item));
    });

    /// <summary>
    /// Applies a new general time to a stay: an exact span becomes check-in and check-out, a day or a night moves the
    /// stay to that night, keeping its length and hours.
    /// </summary>
    public static Entity WithStayTime(TripSnapshot trip, Entity item, JsonObject? time)
    {
        if (!Stays.IsStay(item) || time is null) return item;
        if (time["precision"]?.ToString() is "exact" or "approximate" && time["start"] is JsonObject start && time["end"] is JsonObject end)
        {
            var checkIn = ZonedTime.From(start); var checkOut = ZonedTime.From(end);
            if (checkOut.ToInstant() > checkIn.ToInstant()) return Stays.WithSpan(item, checkIn, checkOut);
        }
        // Resolve the nights the new time alone describes, then keep the stay's length.
        var probe = item.Copy(); probe.Data["time"] = time.DeepClone();
        var accommodation = (JsonObject)probe.Data["components"]!["accommodation"]!; accommodation.Remove("check_in"); accommodation.Remove("check_out");
        if (Stays.Resolve(trip, probe) is not { FirstNight: { } first }) return item;
        var length = Stays.Resolve(trip, item) is { Nights: > 0 } current ? current.Nights : 1;
        return Stays.WithNights(trip, item, first, length);
    }

    /// <summary>Stays that should be one stay, for example one item per night at the same place.</summary>
    public IReadOnlyList<StayMergeCandidate> StayMergeCandidates => Workspace is null ? [] : Stays.MergeCandidates(Workspace.State.Trip);

    /// <summary>Offers to merge stays that are one item per night into one stay each. All merges are one undoable step.</summary>
    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private Task MergeStays() => RunEditAsync(async () =>
    {
        if (Workspace is null) return;
        var trip = Workspace.State.Trip; var candidates = Stays.MergeCandidates(trip);
        if (candidates.Count == 0) { Interaction.Toast(Strings["NoStaysToMerge"]); return; }
        var choices = candidates.Select((c, i) => new Choice(i.ToString(System.Globalization.CultureInfo.InvariantCulture), c.Keep.Item.Title,
            "bed-double", Formats.DateRange(c.FirstNight, c.FirstNight.PlusDays(c.Nights)) + " · " + (c.Nights == 1 ? Strings["OneNight"] : string.Format(Strings["NightsCount"], c.Nights)) + " · " + string.Format(Strings["StayEntries"], c.Stays.Count))).ToArray();
        var picked = await Interaction.PickAsync(Strings["MergeStaysTitle"], Strings["MergeStaysBody"], choices, choices.Select(c => c.Id).ToArray(), Strings["MergeStaysConfirm"]);
        if (picked is not { Count: > 0 }) return;
        var edits = new Dictionary<Guid, EntityEdit>();
        foreach (var candidate in picked.Select(id => candidates[int.Parse(id, System.Globalization.CultureInfo.InvariantCulture)]))
        {
            var merge = Stays.Merge(trip, candidate);
            foreach (var removed in merge.Removed) edits[removed] = new(removed, null);
            edits[merge.Kept.Id] = new(merge.Kept.Id, merge.Kept);
            // Two merges can redirect the same booking; apply the second on top of the first.
            foreach (var redirected in merge.Redirected)
            {
                var basis = edits.TryGetValue(redirected.Id, out var earlier) && earlier.Value is { } before ? before : null;
                edits[redirected.Id] = new(redirected.Id, basis is null ? redirected : TripReferences.Redirect(basis, merge.Removed.ToHashSet(), merge.Kept.Id));
            }
        }
        await Workspace.ApplyAsync(edits.Values.ToArray());
        var removedCount = edits.Values.Count(e => e.Value is null);
        Interaction.Toast(removedCount == 1 ? Strings["StaysMergedOne"] : string.Format(Strings["StaysMerged"], removedCount), Strings["Undo"], () => UndoCommand.ExecuteAsync(null));
    });
}
