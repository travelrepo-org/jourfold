using CommunityToolkit.Mvvm.Input;
using TravelRepo.Core;
using TravelRepo.Git;
using TravelRepo.Merge;
using TravelRepo.Providers;
using TravelRepo.Providers.GitHub;

namespace Jourfold.Desktop;

public partial class MainViewModel
{
    public TimeSpan MergeGracePeriod { get; init; } = TimeSpan.FromSeconds(10);
    private CancellationTokenSource? mergeCancellation;
    private int syncTicks;

    public async Task<IReadOnlyDictionary<string, string>> RemotesAsync() => Workspace is null ? new Dictionary<string, string>() : await Workspace.Git.RemotesAsync();

    private async Task<string?> PreferredRemote(bool ask = true)
    {
        if (Workspace is null) return null; var remotes = await Workspace.Git.RemotesAsync(); var preferred = Store.Get("remote:" + Workspace.Repository.Root); if (preferred is not null && remotes.ContainsKey(preferred)) return preferred;
        if (remotes.Count == 0) return null;
        if (remotes.Count == 1) preferred = remotes.Keys.First();
        else if (ask) preferred = await Interaction.ChooseAsync(Strings["Remote"], remotes.Select(p => new Choice(p.Key, p.Key, "cloud", p.Value)).ToArray());
        else return null;
        if (preferred is not null) Store.Set("remote:" + Workspace.Repository.Root, preferred); return preferred;
    }

    /// <summary>Update the human sync label from local Git state. Never contacts the network.</summary>
    public async Task UpdateSyncStatusAsync()
    {
        if (Workspace is not { } workspace) return;
        try
        {
            var remote = await PreferredRemote(ask: false);
            var status = await workspace.Git.SyncStatusAsync(remote);
            SyncKey = status.State switch { SyncState.LocalOnly => "SyncLocalOnly", SyncState.Synced => "SyncSynced", SyncState.RemoteChanges => "SyncRemoteChanges", SyncState.Diverged => "SyncDiverged", _ => remote is null ? "SyncLocalOnly" : "SyncLocalChanges" };
            HasUncommitted = status.HasLocalChanges;
        }
        catch (DomainException) { }
    }

    [RelayCommand] private Task Sync() => RunAsync(() => SynchronizeAsync(false));
    private async Task SynchronizeAsync(bool background)
    {
        if (Workspace is null) return;
        var remote = background ? await PreferredRemote(ask: false) : await PreferredRemote();
        if (remote is null) { if (!background) await ShareCommand.ExecuteAsync(null); return; }
        if (!background && !string.IsNullOrEmpty(await Workspace.Git.StatusAsync()))
        {
            if (await Interaction.ConfirmAsync(Strings["Sync"], Strings["SyncNeedsVersion"], Strings["CreateVersionAndSync"]))
                await Workspace.Git.CreateVersionAsync(SuggestVersionMessage(await PendingChangesAsync()));
        }
        SyncKey = "Syncing";
        try
        {
            var state = await Workspace.Git.SyncAsync(remote); await Workspace.ReloadAsync(); Refresh();
            if (state == SyncState.Diverged)
            {
                var branch = "refs/remotes/" + remote + "/" + await Workspace.Git.CurrentBranchAsync(); var plan = await Workspace.CompareMergeAsync(branch);
                if (plan.CanApply)
                {
                    Notice = Strings["MergeNotice"]; mergeCancellation?.Dispose(); mergeCancellation = new();
                    try { await Task.Delay(MergeGracePeriod, mergeCancellation.Token); await Workspace.ApplyMergeAsync(branch, plan); await Workspace.Git.PushAsync(remote); Refresh(); Status = Strings["Merged"]; Interaction.Toast(Strings["MergedRemote"]); }
                    catch (OperationCanceledException) { Status = Strings["MergeCancelled"]; }
                    finally { Notice = ""; }
                }
                else if (background) Status = Strings["SyncConflict"];
                else if (plan.Conflicts.Count > 0) await MergeAsync(branch, remote);
                else await Interaction.ShowAsync(Strings["Review"], string.Join('\n', plan.Diagnostics.Select(d => Strings.Diagnostic(d.Code))));
                return;
            }
            Status = state == SyncState.LocalChanges ? Strings["Changed"] : Strings["Saved"]; Store.Set("sync:" + Workspace.Repository.Root, state == SyncState.LocalChanges ? "Changed" : "Saved");
            Store.Set("synced:" + Workspace.Repository.Root, DateTimeOffset.Now.ToString("O"));
            if (!background) Interaction.Toast(state == SyncState.LocalChanges ? Strings["SyncedLocalChanges"] : Strings["SyncedNow"]);
        }
        catch when (background) { SyncKey = "SyncFailed"; throw; }
        finally { if (SyncKey == "Syncing") await UpdateSyncStatusAsync(); }
    }
    [RelayCommand] private void CancelMerge() { mergeCancellation?.Cancel(); Notice = ""; }
    [RelayCommand]
    private async Task ReviewMerge()
    {
        mergeCancellation?.Cancel();
        if (Workspace is not null && await PreferredRemote() is { } remote)
        {
            var branch = "refs/remotes/" + remote + "/" + await Workspace.Git.CurrentBranchAsync();
            await Interaction.CompareAsync(Workspace.State.Trip, await Workspace.Git.SnapshotAsync(branch), Strings["ThisComputer"], remote);
        }
    }

    public Task PollAsync() => Busy || Workspace is null ? Task.CompletedTask : ExecuteAsync(PollCoreAsync, true, background: true);
    private async Task PollCoreAsync()
    {
        if (Workspace is null) return;
        try
        {
            if (await Workspace.ReloadAsync()) { Refresh(); Status = Strings["External"]; Interaction.Toast(Strings["External"]); await UpdateLocalStateAsync(); }
            if (++syncTicks >= 20)
            {
                syncTicks = 0;
                if (await PreferredRemote(ask: false) is not null)
                {
                    Busy = true;
                    try { await SynchronizeAsync(true); }
                    catch (Exception ex) when (ex is DomainException or HttpRequestException or IOException or OperationCanceledException) { Status = Strings["SyncFailed"]; SyncKey = "SyncFailed"; }
                    finally { Busy = false; }
                }
            }
        }
        catch (Exception ex) when (ex is DomainException or IOException) { Status = Strings["Error"] + ": " + Strings.Error(ex, Advanced); }
    }

    // Sharing ---------------------------------------------------------------------------------------------

    [RelayCommand] private Task Share() => Interaction.ShareAsync(this);

    /// <summary>Create a private GitHub repository for the open trip, connect it as origin and push.</summary>
    public Task PublishToGitHubAsync(string name) => RunAsync(async () =>
    {
        if (Workspace is null) return;
        if (!await HasGitHubTokenAsync()) await ConnectGitHubCoreAsync();
        var created = await GitHub().CreateAsync(name.Trim()); await Workspace.Git.AddRemoteAsync("origin", created.CloneUrl); await RememberRemoteAsync("origin", true);
        if (!string.IsNullOrEmpty(await Workspace.Git.StatusAsync())) await Workspace.Git.CreateVersionAsync(SuggestVersionMessage(await PendingChangesAsync()));
        await Workspace.Git.PushAsync("origin"); await UpdateSyncStatusAsync();
        Interaction.Toast(string.Format(Strings["PublishedPrivately"], created.FullName));
    });

    public Task AddRemoteAsync(string name, string url) => RunAsync(async () =>
    {
        if (Workspace is null) return;
        await Workspace.Git.AddRemoteAsync(name.Trim(), url.Trim()); await RememberRemoteAsync(name.Trim(), IsGitHubUrl(url));
        try { await Workspace.Git.PushAsync(name.Trim()); Interaction.Toast(Strings["RemoteAddedPushed"]); }
        catch (DomainException) { Interaction.Toast(Strings["RemoteAddedNotPushed"]); }
        await UpdateSyncStatusAsync();
    });
    [RelayCommand]
    private Task AddRemote() => RunAsync(async () =>
    {
        var values = await Interaction.FormAsync(Strings["AddRemote"], [new("url", Strings["RemoteUrl"], Required: true, Hint: Strings["RemoteUrlHint"]), new("name", Strings["RemoteName"], Value: "origin", Required: true)]);
        if (values is not null) await AddRemoteAsync(values["name"], values["url"]);
    });

    private async Task RememberRemoteAsync(string name, bool github)
    {
        if (Workspace is null) return;
        Store.Set("remote:" + Workspace.Repository.Root, name);
        Store.Remember(Workspace.Repository.Root, Workspace.State.Trip, "remote");
        shareKey = github ? "Invite" : "Share"; OnPropertyChanged(nameof(SharingLabel)); OnPropertyChanged(nameof(ShareKey));
        _ = RefreshPrivacyAsync((await Workspace.Git.RemotesAsync()).Values);
    }

    /// <summary>Text a user can send to a travel companion. It contains no credentials.</summary>
    public async Task<string> ShareTextAsync(string? branch = null)
    {
        if (Workspace is null || await PreferredRemote() is not { } remote) return "";
        var url = (await Workspace.Git.RemotesAsync())[remote]; GitRepository.ValidateRemote(url);
        return string.Format(Strings["ShareMessage"], Workspace.State.Trip.Manifest.Title, new ShareLink(url, branch ?? await Workspace.Git.CurrentBranchAsync()).ToUri(), url);
    }

    public async Task ShareVariantAsync(Variant variant)
    {
        if (Workspace is null || await PreferredRemote() is null) { await Interaction.ShowAsync(Strings["ShareVariant"], Strings["PublishFirst"]); return; }
        var branch = variant.IsRemote ? variant.Branch[(variant.Branch.IndexOf('/') + 1)..] : variant.Branch;
        var text = await ShareTextAsync(branch); await Interaction.CopyAsync(text); Interaction.Toast(Strings["LinkCopied"]);
    }

    public Task InviteAsync(string username) => RunAsync(async () =>
    {
        if (Workspace is null || await PreferredRemote() is not { } remote) return;
        var url = (await Workspace.Git.RemotesAsync())[remote]; if (!IsGitHubUrl(url)) return;
        var name = GitHubName(url); var repo = await GitHub().InspectAsync(name);
        if (!repo.IsPrivate && !await Interaction.ConfirmAsync(Strings["PublicWarning"])) return;
        await GitHub().InviteAsync(name, username.Trim().TrimStart('@')); Interaction.Toast(string.Format(Strings["Invited"], username.Trim()));
    });

    private async Task RefreshPrivacyAsync(IEnumerable<string> remotes)
    {
        PrivacyWarning = ""; var workspace = Workspace;
        foreach (var url in remotes.Where(IsGitHubUrl))
        {
            var name = GitHubName(url);
            if (Store.Get("privacy:" + name) == "public") PrivacyWarning = Strings["PublicWarning"];
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5)); var repository = await GitHub().InspectAsync(name, timeout.Token);
                if (Workspace != workspace) return; Store.Set("privacy:" + name, repository.IsPrivate ? "private" : "public"); if (!repository.IsPrivate) PrivacyWarning = Strings["PublicWarning"];
            }
            catch (Exception ex) when (ex is DomainException or HttpRequestException or OperationCanceledException) { }
        }
    }

    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty] private bool gitHubConnected;
    /// <summary>The connected GitHub account, once GitHub has confirmed it. <see cref="GitHubLogin"/> also works offline.</summary>
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty] private ProviderAccount? gitHubAccount;
    public string? GitHubLogin => GitHubAccount?.Login ?? (Store.Get("github.login") is { Length: > 0 } login ? login : null);
    public bool GitHubSessionOnly => secrets.SessionOnly;
    public async Task<bool> HasGitHubTokenAsync() { GitHubConnected = await GitHub().IsConnectedAsync(); if (!GitHubConnected) GitHubAccount = null; return GitHubConnected; }

    /// <summary>
    /// Checks the stored sign-in and asks GitHub which account it belongs to. A sign-in GitHub rejects counts as not
    /// connected. Without network the last known login is kept.
    /// </summary>
    public async Task RefreshGitHubAccountAsync()
    {
        if (!await HasGitHubTokenAsync()) return;
        try
        {
            GitHubAccount = await GitHub().AccountAsync();
            if (GitHubAccount is null) GitHubConnected = false;
            Store.Set("github.login", GitHubAccount?.Login ?? "");
        }
        catch (Exception ex) when (ex is DomainException or HttpRequestException or OperationCanceledException) { }
        OnPropertyChanged(nameof(GitHubLogin));
    }

    /// <summary>Downloads the account picture, or returns <c>null</c> when there is none or the network fails.</summary>
    public async Task<byte[]?> GitHubAvatarAsync()
    {
        if (GitHubAccount?.AvatarUrl is not { } url) return null;
        try { return await githubHttp.GetByteArrayAsync(url + (url.Query.Length > 0 ? "&" : "?") + "s=96"); }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException) { return null; }
    }

    [RelayCommand]
    private Task DisconnectGitHub() => RunAsync(async () =>
    {
        if (!await Interaction.ConfirmAsync(Strings["DisconnectGitHub"], string.Format(Strings["DisconnectGitHubBody"], GitHubLogin ?? "GitHub"), Strings["Disconnect"], danger: true)) return;
        await GitHub().SignOutAsync(); Store.Set("github.login", ""); GitHubAccount = null; GitHubConnected = false; OnPropertyChanged(nameof(GitHubLogin));
        Interaction.Toast(Strings["GitHubDisconnected"]);
    });
    [RelayCommand] private Task ConnectGitHub() => RunAsync(ConnectGitHubCoreAsync);
    private async Task ConnectGitHubCoreAsync()
    {
        if (Store.Get("github.client") is null)
        {
            var values = await Interaction.FormAsync(Strings["ConnectGitHub"], [new("client", Strings["GitHubClient"], Required: true, Hint: Strings["GitHubClientHint"])]); if (values is null) throw new OperationCanceledException();
            Store.Set("github.client", values["client"]);
        }
        var provider = GitHub(); var code = await provider.BeginAsync(); await Interaction.CopyAsync(code.UserCode); Interaction.Open(code.VerificationUri);
        await Interaction.ShowAsync(Strings["GitHubCode"], string.Format(Strings["GitHubCodeBody"], code.UserCode)); await provider.CompleteAsync(code);
        if (secrets.SessionOnly) await Interaction.ShowAsync(Strings["ConnectGitHub"], Strings["SessionOnly"]);
        await RefreshGitHubAccountAsync();
        Interaction.Toast(GitHubLogin is { } login ? string.Format(Strings["GitHubConnectedAs"], login) : Strings["GitHubConnected"]);
    }
    [RelayCommand]
    private Task DiscoverGitHub() => RunAsync(async () =>
    {
        if (!await HasGitHubTokenAsync()) await ConnectGitHubCoreAsync();
        var repos = await GitHub().DiscoverAsync();
        if (repos.Count == 0) { await Interaction.ShowAsync(Strings["DiscoverGitHub"], Strings["NoGitHubTrips"]); return; }
        var choice = await Interaction.ChooseAsync(Strings["DiscoverGitHub"], repos.Select(r => new Choice(r.FullName, r.FullName, r.IsPrivate ? "lock" : "globe", r.IsPrivate ? Strings["Private"] : Strings["Public"])).ToArray()); if (choice is null) return; var repo = repos.Single(r => r.FullName == choice);
        if (!repo.IsPrivate && !await Interaction.ConfirmAsync(Strings["PublicWarning"])) return;
        var folder = UniqueFolder(DefaultTripsFolder, repo.FullName.Split('/')[^1]); await GitRepository.CloneAsync(backend, repo.CloneUrl, folder); if (OpenRequested is not null) OpenRequested(folder); else await OpenAsync(folder);
    });
}
