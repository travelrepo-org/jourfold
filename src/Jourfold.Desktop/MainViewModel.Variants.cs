using CommunityToolkit.Mvvm.Input;
using NodaTime;
using TravelRepo.Core;
using TravelRepo.Git;

namespace Jourfold.Desktop;

/// <summary>
/// Variant management: one overview of local and online variants, taking a variant over into the main plan, deleting
/// with a recycle bin, and cleaning up after variants that were deleted online. Git rules live in TravelRepo.
/// </summary>
public partial class MainViewModel
{
    /// <summary>How long deleted variants stay restorable.</summary>
    public static readonly TimeSpan DeletedVariantsKept = TimeSpan.FromDays(30);

    /// <summary>Variants deleted online whose copies are still here. Shown as a reminder until handled.</summary>
    public IReadOnlyList<VariantOverview> GoneVariants { get; private set; } = [];

    public async Task<IReadOnlyList<VariantOverview>> VariantOverviewAsync() => Workspace is null ? [] : await Workspace.Git.VariantOverviewAsync(await PreferredRemote(ask: false));
    public async Task<IReadOnlyList<DeletedVariant>> DeletedVariantsAsync() => Workspace is null ? [] : await Workspace.Git.DeletedVariantsAsync();

    /// <summary>Refreshes the list of variants deleted online, purges old deleted variants and updates the reminders.</summary>
    public async Task UpdateGoneVariantsAsync()
    {
        if (Workspace is null) return;
        try
        {
            await Workspace.Git.PurgeDeletedVariantsAsync(DeletedVariantsKept);
            GoneVariants = (await VariantOverviewAsync()).Where(v => v.UpstreamGone && v.LocalBranch is not null).ToArray();
        }
        catch (DomainException) { GoneVariants = []; }
        await UpdateRemindersAsync();
    }

    private Reminder? GoneVariantsReminder() => GoneVariants.Count == 0 ? null
        : new("variants-gone:" + string.Join(",", GoneVariants.Select(v => v.LocalBranch)), ReminderKind.VariantsDeletedOnline, null, Strings["VariantsDeletedOnlineTitle"], null, false, []);

    private string PrimaryTitle(IReadOnlyList<VariantOverview> all) => all.FirstOrDefault(v => v.Primary)?.Title ?? Strings["MainPlan"];

    /// <summary>
    /// Takes a variant over into the main plan: switches to the main plan and merges the variant into it, with the usual
    /// questions when both changed the same detail. The variant is then marked as taken over, and Jourfold offers to
    /// delete it, online too.
    /// </summary>
    public Task PromoteVariantAsync(VariantOverview variant) => RunAsync(async () =>
    {
        if (Workspace is null) return;
        var all = await VariantOverviewAsync();
        if (all.FirstOrDefault(v => v.Primary && v.LocalBranch is not null) is not { } primary || variant.Primary) return;
        await EnsureCleanAsync();
        var branch = variant.LocalBranch ?? variant.RemoteBranch!;
        if (await Workspace.Git.CurrentBranchAsync() != primary.LocalBranch) { await Workspace.Git.SwitchAsync(primary.LocalBranch!); await Workspace.ReloadAsync(); await UpdateLocalStateAsync(); }
        if (!await MergeAsync(branch, variant.Title)) return;
        var deleted = await OfferDeleteAsync(await FindAsync(variant), merged: true);
        Refresh();
        Interaction.Toast(deleted ? string.Format(Strings["VariantPromotedAndDeleted"], variant.Title) : string.Format(Strings["VariantPromoted"], variant.Title));
    });

    /// <summary>Brings the main plan's newer versions into the open variant.</summary>
    public Task UpdateFromPrimaryAsync() => RunAsync(async () =>
    {
        if (Workspace is null) return;
        var all = await VariantOverviewAsync();
        if (all.FirstOrDefault(v => v.Primary && v.LocalBranch is not null) is not { } primary || all.FirstOrDefault(v => v.Current) is not { Primary: false }) return;
        await EnsureCleanAsync();
        await MergeAsync(primary.LocalBranch!, primary.Title);
    });

    private async Task<VariantOverview?> FindAsync(VariantOverview variant) =>
        (await VariantOverviewAsync()).FirstOrDefault(v => v.VariantId == variant.VariantId && (v.LocalBranch == variant.LocalBranch || v.RemoteBranch == variant.RemoteBranch));

    /// <summary>Deletes a variant after asking; the open variant is left for the main plan first.</summary>
    public Task DeleteVariantAsync(VariantOverview variant) => RunAsync(async () =>
    {
        if (Workspace is null || variant.Primary || variant.LocalBranch is null) return;
        if (variant.Current)
        {
            if (!await Interaction.ConfirmAsync(Strings["DeleteVariant"], string.Format(Strings["DeleteOpenVariant"], variant.Title), Strings["SwitchToMainPlan"])) return;
            await EnsureCleanAsync();
            var primary = (await VariantOverviewAsync()).First(v => v.Primary && v.LocalBranch is not null);
            await Workspace.Git.SwitchAsync(primary.LocalBranch!); await Workspace.ReloadAsync(); await UpdateLocalStateAsync(); Refresh();
        }
        if (await FindAsync(variant) is { } fresh && await OfferDeleteAsync(fresh, fresh.Merged)) Interaction.Toast(string.Format(Strings["VariantDeleted"], variant.Title), Strings["Undo"], () => RestoreLatestAsync(variant.LocalBranch));
    });

    /// <summary>Asks whether to delete a variant and its online copy, then deletes it. Returns whether it was deleted.</summary>
    private async Task<bool> OfferDeleteAsync(VariantOverview? variant, bool merged)
    {
        if (Workspace is null || variant?.LocalBranch is null || variant.Current || variant.Primary) return false;
        var message = string.Join("\n\n", new[]
        {
            merged ? string.Format(Strings["DeleteMergedVariantBody"], variant.Title) : string.Format(Strings["DeleteVariantBody"], variant.Title),
            variant.OnlyHere > 0 && !merged ? string.Format(variant.OnlyHere == 1 ? Strings["VariantOnlyHereOne"] : Strings["VariantOnlyHere"], variant.OnlyHere) : null,
            Strings["DeleteVariantRestore"]
        }.Where(t => t is not null));
        IReadOnlyList<Choice> options = variant.OnRemote ? [new("remote", Strings["AlsoDeleteOnline"], "cloud", Strings["AlsoDeleteOnlineHint"])] : [];
        var picked = await Interaction.PickAsync(Strings["DeleteVariant"], message, options, merged ? ["remote"] : [], Strings["Delete"]);
        if (picked is null) return false;
        var remote = await PreferredRemote(ask: false);
        await Workspace.Git.DeleteVariantAsync(variant.LocalBranch, picked.Contains("remote"), remote);
        await UpdateLocalStateAsync(); await UpdateGoneVariantsAsync(); ContentChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    private async Task RestoreLatestAsync(string? branch)
    {
        if (Workspace is null || branch is null) return;
        if ((await Workspace.Git.DeletedVariantsAsync()).FirstOrDefault(d => d.Branch == branch) is { } deleted) await RestoreVariantAsync(deleted);
    }

    public Task RestoreVariantAsync(DeletedVariant deleted) => RunAsync(async () =>
    {
        if (Workspace is null) return;
        await Workspace.Git.RestoreVariantAsync(deleted);
        await UpdateLocalStateAsync(); ContentChanged?.Invoke(this, EventArgs.Empty);
        Interaction.Toast(string.Format(Strings["VariantRestored"], deleted.Title));
    });

    /// <summary>Uploads a variant that was kept only on this computer.</summary>
    public Task ShareLocalVariantAsync(VariantOverview variant) => RunAsync(async () =>
    {
        if (Workspace is null || variant.LocalBranch is null || await PreferredRemote() is not { } remote) return;
        await Workspace.Git.ShareVariantAsync(variant.LocalBranch, remote);
        await UpdateSyncStatusAsync(); ContentChanged?.Invoke(this, EventArgs.Empty);
        Interaction.Toast(string.Format(Strings["VariantShared"], variant.Title));
    });

    /// <summary>Syncs a variant that is not open. A diverged one has to be opened, because merging needs the plan.</summary>
    public Task SyncVariantAsync(VariantOverview variant) => RunAsync(async () =>
    {
        if (Workspace is null || variant.LocalBranch is null || await PreferredRemote() is not { } remote) return;
        if (variant.Current) { await SyncCommand.ExecuteAsync(null); return; }
        await Workspace.Git.FetchAsync(remote);
        var state = await Workspace.Git.SyncVariantAsync(variant.LocalBranch, remote);
        ContentChanged?.Invoke(this, EventArgs.Empty);
        Interaction.Toast(state == SyncState.Diverged ? string.Format(Strings["VariantDivergedOpen"], variant.Title) : string.Format(Strings["VariantSynced"], variant.Title));
    });

    /// <summary>
    /// Asks what to do with local copies of variants that were deleted online. Unchanged copies are suggested for
    /// deletion; copies with versions only here and the open variant are kept unless chosen. Kept copies stay only on
    /// this computer, so the next sync does not bring them back for the others.
    /// </summary>
    [RelayCommand]
    private Task CleanUpVariants() => RunAsync(async () =>
    {
        if (Workspace is null) return;
        await UpdateGoneVariantsAsync();
        var gone = GoneVariants; if (gone.Count == 0) { Interaction.Toast(Strings["NoVariantsToCleanUp"]); return; }
        string Hint(VariantOverview v) => string.Join(" · ", new[]
        {
            v.Current ? Strings["VariantOpenNow"] : null,
            v.OnlyHere == 0 ? Strings["VariantUnchanged"] : string.Format(v.OnlyHere == 1 ? Strings["VariantOnlyHereOne"] : Strings["VariantOnlyHere"], v.OnlyHere)
        }.Where(t => t is not null));
        var choices = gone.Select(v => new Choice(v.LocalBranch!, v.Title, "git-branch", Hint(v))).ToArray();
        var picked = await Interaction.PickAsync(Strings["VariantsDeletedOnlineTitle"], Strings["VariantsDeletedOnlineBody"], choices, gone.Where(v => v.OnlyHere == 0 && !v.Current).Select(v => v.LocalBranch!).ToArray(), Strings["ApplyChoice"]);
        if (picked is null) return;
        var remote = await PreferredRemote(ask: false);
        foreach (var variant in gone)
        {
            if (!picked.Contains(variant.LocalBranch!)) { await Workspace.Git.KeepLocalOnlyAsync(variant.LocalBranch!); continue; }
            if (variant.Current)
            {
                await EnsureCleanAsync();
                var primary = (await VariantOverviewAsync()).First(v => v.Primary && v.LocalBranch is not null);
                await Workspace.Git.SwitchAsync(primary.LocalBranch!); await Workspace.ReloadAsync();
            }
            await Workspace.Git.DeleteVariantAsync(variant.LocalBranch!, false, remote);
        }
        await UpdateLocalStateAsync(); await UpdateGoneVariantsAsync(); await UpdateSyncStatusAsync(); Refresh();
        Interaction.Toast(string.Format(Strings["VariantsCleanedUp"], picked.Count, gone.Count - picked.Count));
    });
}
