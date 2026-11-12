using Jourfold.Application;
using TravelRepo.Core;
using TravelRepo.Git;
using TravelRepo.Merge;
using TravelRepo.Providers;
using TravelRepo.Providers.GitHub;

namespace Jourfold.Mobile;

/// <summary>
/// Signing in to GitHub, putting a trip there, getting a shared trip, and keeping the open trip in step. Sync runs
/// after each automatic version and when a trip opens; without a network it waits quietly for the next time.
/// </summary>
public sealed partial class MobileSession
{
    private HttpClient? http;
    private CancellationTokenSource? signIn;

    /// <summary>The GitHub account this phone is signed in with, or null.</summary>
    public string? GitHubLogin { get; private set; }
    /// <summary>Whether the stored sign-in has been read once, so a null <see cref="GitHubLogin"/> means signed out.</summary>
    public bool GitHubLoaded { get; private set; }
    /// <summary>The code to enter on GitHub while a sign-in waits for confirmation.</summary>
    public DeviceAuthorization? SignInCode { get; private set; }
    /// <summary>Where the open trip is shared, such as "origin"; null when it is only on this phone.</summary>
    public string? Remote { get; private set; }
    public bool Syncing { get; private set; }
    /// <summary>Why the last sync did not finish, in words for the user.</summary>
    public string? SyncProblem { get; private set; }
    /// <summary>Why the last sign-in failed, in words for the user.</summary>
    public string? SignInProblem { get; private set; }
    /// <summary>Trips on GitHub that can be brought to this phone; null until the user asks for them.</summary>
    public IReadOnlyList<RemoteRepository>? RemoteTrips { get; private set; }
    /// <summary>How far the search for trips on GitHub has come, while it runs.</summary>
    public DiscoveryProgress? Searching { get; private set; }
    /// <summary>Why the last search for trips failed, in words for the user.</summary>
    public string? SearchProblem { get; private set; }
    /// <summary>
    /// Signed in, but the GitHub App may not use any repository yet: signing in does not install it. The user chooses
    /// repositories at <see cref="RepositoryAccessUri"/>.
    /// </summary>
    public bool NeedsRepositoryAccess { get; private set; }
    /// <summary>The page on GitHub where the user chooses the repositories the App may use; null for an App of their own.</summary>
    public Uri? RepositoryAccessUri => GitHub().RepositoryAccessUri;

    /// <summary>The GitHub App to sign in with: the one named in settings, otherwise the one built into the app.</summary>
    public string? ClientId => Settings.GitHubClientId is { Length: > 0 } id ? id : Host.GitHubClientId is { Length: > 0 } built ? built : null;

    public DateTimeOffset? LastSync => Workspace is { } w && Settings.LastSync.TryGetValue(Path.GetFileName(w.Repository.Root), out var at) ? at : null;

    // The built-in App's name only goes with the built-in App.
    private GitHubProvider GitHub() => new(http ??= Host.Http is null ? new HttpClient() : new HttpClient(Host.Http, disposeHandler: false), Host.Secrets, ClientId ?? "",
        Settings.GitHubClientId is { Length: > 0 } ? null : Host.GitHubAppSlug);

    // The code of a sign-in that is waiting for GitHub, kept with the secrets so it is not in a readable file. If
    // Android ends the app while the user is in the browser, the sign-in goes on when the app starts again.
    private const string PendingKey = "github.pending-sign-in";
    private sealed record PendingSignIn(DeviceAuthorization Code, DateTimeOffset Expires);

    /// <summary>
    /// Reads the stored sign-in without going online; the account name comes from the last sign-in. A sign-in that was
    /// still waiting when the app last ended goes on waiting.
    /// </summary>
    public async Task LoadGitHubAsync(CancellationToken ct = default)
    {
        var provider = GitHub();
        GitHubLogin = await provider.IsConnectedAsync(ct) ? Settings.GitHubLogin ?? "GitHub" : null;
        GitHubLoaded = true;
        Changed?.Invoke();
        if (GitHubLogin is not null) _ = CheckSignInAsync(provider, ct);
        if (GitHubLogin is null && SignInCode is null && await Host.Secrets.ReadAsync(PendingKey, ct) is { } text)
        {
            PendingSignIn? pending = null;
            try { pending = System.Text.Json.JsonSerializer.Deserialize<PendingSignIn>(text); } catch (System.Text.Json.JsonException) { }
            if (pending is not null && pending.Expires > DateTimeOffset.UtcNow && ClientId is not null) await WaitForSignInAsync(provider, pending.Code, ct);
            else await Host.Secrets.DeleteAsync(PendingKey, ct);
        }
    }

    /// <summary>
    /// Starts GitHub's device sign-in and waits until the user has entered <see cref="SignInCode"/> on GitHub, the code
    /// expires, or <see cref="CancelSignIn"/> is called. Problems end up in <see cref="SignInProblem"/>.
    /// </summary>
    public async Task SignInAsync(string? clientId = null, CancellationToken ct = default)
    {
        if (!string.IsNullOrWhiteSpace(clientId)) { Settings.GitHubClientId = clientId.Trim(); Settings.Save(); }
        SignInProblem = null;
        if (ClientId is null) { SignInProblem = Strings["GitHubClientNeeded"]; Changed?.Invoke(); return; }
        var provider = GitHub();
        DeviceAuthorization code;
        try { code = await provider.BeginAsync(ct); }
        catch (HttpRequestException) { SignInProblem = Strings["SyncOffline"]; Changed?.Invoke(); return; }
        catch (DomainException ex) { SignInProblem = Problem(ex); Changed?.Invoke(); return; }
        await Host.Secrets.WriteAsync(PendingKey, System.Text.Json.JsonSerializer.Serialize(new PendingSignIn(code, DateTimeOffset.UtcNow.AddSeconds(code.ExpiresIn))), ct);
        await WaitForSignInAsync(provider, code, ct);
    }

    /// <summary>
    /// Shows the code and waits for GitHub. Polling survives the phone sleeping or changing networks while the user is
    /// in the browser; only a refusal, the code expiring or <see cref="CancelSignIn"/> end it.
    /// </summary>
    private async Task WaitForSignInAsync(GitHubProvider provider, DeviceAuthorization code, CancellationToken ct)
    {
        signIn?.Dispose(); signIn = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var token = signIn.Token;
        SignInCode = code; Changed?.Invoke();
        try
        {
            await provider.CompleteAsync(code, token);
            await Host.Secrets.DeleteAsync(PendingKey, CancellationToken.None);
            GitHubLogin = "GitHub";
            // The account's name is shown and is a good start for the name versions are saved under. Without a
            // network right now, the sign-in still counts; the name comes with the next sign-in.
            try
            {
                var account = await provider.AccountAsync(token);
                GitHubLogin = account?.Login ?? "GitHub";
                if (Settings.Name is null && account?.Name is { Length: > 0 } name) Settings.Name = name;
                Remember(account);
                // A trip open during the sign-in saves its next versions under the account.
                await ApplyIdentityAsync(token);
            }
            catch (Exception ex) when (GitHubProvider.IsTransient(ex, token)) { }
            // A first sign-in leaves the App without repositories; say so right away instead of after a search.
            try { NeedsRepositoryAccess = !await provider.HasRepositoryAccessAsync(token); }
            catch (Exception ex) when (GitHubProvider.IsTransient(ex, token)) { }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested && !ct.IsCancellationRequested) { await Host.Secrets.DeleteAsync(PendingKey, CancellationToken.None); }
        catch (DomainException ex) { SignInProblem = Problem(ex); await Host.Secrets.DeleteAsync(PendingKey, CancellationToken.None); }
        finally { if (SignInCode == code) { SignInCode = null; signIn?.Dispose(); signIn = null; } Changed?.Invoke(); }
    }

    public void CancelSignIn() => signIn?.Cancel();

    /// <summary>
    /// Asks GitHub in the background whether the stored sign-in still counts. Removing Jourfold from the GitHub
    /// account ends it; the phone then shows the sign-in again instead of failing later. Without a network nothing changes.
    /// </summary>
    private async Task CheckSignInAsync(GitHubProvider provider, CancellationToken ct)
    {
        try
        {
            if (!await provider.IsConnectedAsync(ct)) return;
            if (await provider.AccountAsync(ct) is not { } account) { await SignInEndedAsync(); return; }
            // Signed in before the phone kept the account's number and email: they come with this check.
            if (account.Login != Settings.GitHubLogin || account.Id != Settings.GitHubId || account.PublicEmail != Settings.GitHubEmail)
            {
                Remember(account);
                await ApplyIdentityAsync(ct);
            }
        }
        catch (Exception ex) when (GitHubProvider.IsTransient(ex, ct) || ex is DomainException) { }
    }

    /// <summary>Keeps the account's login, number and public email for versions made while offline.</summary>
    private void Remember(TravelRepo.Providers.ProviderAccount? account)
    {
        Settings.GitHubLogin = account?.Login; Settings.GitHubId = account?.Id; Settings.GitHubEmail = account?.PublicEmail;
        Settings.Save();
    }

    /// <summary>GitHub no longer accepts the sign-in: forget it and say why.</summary>
    private async Task SignInEndedAsync()
    {
        await GitHub().SignOutAsync();
        GitHubLogin = null; Settings.GitHubLogin = null; Settings.GitHubId = null; Settings.GitHubEmail = null; Settings.Save();
        RemoteTrips = null; NeedsRepositoryAccess = false; SearchProblem = null;
        SignInProblem = Strings["GitHubSignInEnded"]; Changed?.Invoke();
    }

    /// <summary>GitHub's answers carry their HTTP status in the code; they all read the same to the user.</summary>
    private string Problem(Exception ex) => ex is DomainException { Code: var code } && code.StartsWith("github.http.", StringComparison.Ordinal) ? Strings["GitHubRequestFailed"] : Strings.Error(ex);

    /// <summary>Forgets the sign-in on this phone. The trips stay.</summary>
    public async Task SignOutAsync(CancellationToken ct = default)
    {
        await GitHub().SignOutAsync(ct);
        GitHubLogin = null; Settings.GitHubLogin = null; Settings.GitHubId = null; Settings.GitHubEmail = null; Settings.Save(); RemoteTrips = null; NeedsRepositoryAccess = false; SearchProblem = null; Changed?.Invoke();
    }

    private async Task RefreshRemoteAsync(CancellationToken ct)
    {
        var remotes = Workspace is { } w ? await w.Git.RemotesAsync(ct) : new Dictionary<string, string>();
        Remote = remotes.ContainsKey("origin") ? "origin" : remotes.Keys.FirstOrDefault();
    }

    /// <summary>Puts the open trip into a new private repository on GitHub and syncs it there.</summary>
    public async Task ShareOnGitHubAsync(CancellationToken ct = default)
    {
        if (Workspace is not { } workspace) return;
        var name = string.Concat(workspace.State.Trip.Manifest.Title.Select(c => char.IsAsciiLetterOrDigit(c) ? char.ToLowerInvariant(c) : '-')).Trim('-');
        while (name.Contains("--", StringComparison.Ordinal)) name = name.Replace("--", "-", StringComparison.Ordinal);
        var repository = await GitHub().CreateAsync(name.Length > 0 ? name : "trip", ct);
        await workspace.Git.AddRemoteAsync("origin", repository.CloneUrl, ct);
        Remote = "origin";
        await SyncAsync(quiet: false, ct);
    }

    /// <summary>
    /// Brings the open trip in step with GitHub: open changes become a version first, then the phone takes the other
    /// side's versions and sends its own. When both sides changed, the changes are combined if they do not touch the
    /// same thing; otherwise the phone keeps its state and asks for the computer, which can compare both.
    /// </summary>
    public async Task SyncAsync(bool quiet = true, CancellationToken ct = default)
    {
        if (Workspace is not { } workspace || Remote is not { } remote || Syncing) return;
        Syncing = true; SyncProblem = null; Changed?.Invoke();
        try
        {
            await SaveChangesAsync(ct);
            // Changes that are not in a version would not be sent, and the trip would only look synced.
            if (VersionProblem is { } notSaved) { SyncProblem = notSaved; return; }
            var state = await workspace.Git.SyncAsync(remote, ct);
            PendingMerge = null;
            // Changes made while the sync ran are not sent yet, and nothing was combined; the next sync takes them.
            if (state == SyncState.LocalChanges) { SyncProblem = Strings["SyncChangesWaiting"]; return; }
            if (state == SyncState.Diverged)
            {
                var branch = "refs/remotes/" + remote + "/" + await workspace.Git.CurrentBranchAsync(ct);
                var (baseline, incoming) = await workspace.MergeSidesAsync(branch, ct);
                var plan = SemanticMerge.Plan(baseline, workspace.State.Trip, incoming);
                if (!plan.CanApply)
                {
                    // Both changed the same thing: the user chooses on this device, then the sync goes on.
                    if (plan.Conflicts.Count > 0) { PendingMerge = new(branch, remote, baseline, incoming, plan); SyncProblem = Strings["SyncChoose"]; }
                    else SyncProblem = Strings["SyncNeedsComputer"];
                    return;
                }
                await workspace.ApplyMergeAsync(branch, plan, ct);
                await workspace.Git.PushAsync(remote, ct);
            }
            await workspace.ReloadAsync(ct);
            Settings.LastSync[Path.GetFileName(workspace.Repository.Root)] = DateTimeOffset.Now; Settings.Save();
            // With a network at hand, the exchange rates come along.
            _ = FetchRatesAsync(ct: CancellationToken.None);
            _ = UpdateWeatherAsync(CancellationToken.None);
        }
        catch (Exception ex) when (ex is DomainException or HttpRequestException or IOException)
        {
            SyncProblem = ex is GitCommandException { Failure: GitFailure.Authentication } ? Strings["SyncSignInAgain"]
                : ex is GitCommandException { Failure: GitFailure.Network } || ex is HttpRequestException ? Strings["SyncOffline"]
                : quiet ? Strings["SyncLater"] : Problem(ex);
        }
        finally
        {
            Syncing = false;
            if (Workspace == workspace) await RefreshVersionsAsync(ct);
            Changed?.Invoke();
        }
    }

    /// <summary>A sync that waits for the user to choose, for each thing both sides changed, which version to keep.</summary>
    public PendingMerge? PendingMerge { get; private set; }

    /// <summary>
    /// Finishes a sync that waited for choices: <paramref name="answers"/> follow the order of the conflicts. The
    /// combined trip becomes a version and is sent. If the trip changed meanwhile, the sync starts over and asks again.
    /// </summary>
    public async Task ResolveMergeAsync(IReadOnlyList<string> answers, CancellationToken ct = default)
    {
        if (Workspace is not { } workspace || PendingMerge is not { } pending) return;
        Syncing = true; SyncProblem = null; Changed?.Invoke();
        try
        {
            var plan = MergeAnswers.Resolve(pending.Baseline, workspace.State.Trip, pending.Incoming, pending.Plan, answers.Select(a => (a, (string?)null)).ToArray());
            if (!plan.CanApply) { SyncProblem = Strings["SyncNeedsComputer"]; return; }
            await workspace.ApplyMergeAsync(pending.Branch, plan, ct);
            await workspace.Git.PushAsync(pending.Remote, ct);
            PendingMerge = null;
            await workspace.ReloadAsync(ct);
            Settings.LastSync[Path.GetFileName(workspace.Repository.Root)] = DateTimeOffset.Now; Settings.Save();
            // Back on the sync page the merge was opened from, without it waiting behind back.
            if (Page == "merge") { if (trail.Count > 0 && trail[^1] == (MobileTab.More, "sync")) trail.RemoveAt(trail.Count - 1); Page = "sync"; }
        }
        catch (DomainException ex) when (ex.Code == "merge.stale") { Syncing = false; PendingMerge = null; await SyncAsync(quiet: false, ct); return; }
        catch (Exception ex) when (ex is DomainException or HttpRequestException or IOException) { SyncProblem = Problem(ex); }
        finally { Syncing = false; if (Workspace == workspace) await RefreshVersionsAsync(ct); Changed?.Invoke(); }
    }

    /// <summary>
    /// Lists the trips on GitHub this account can open, with <see cref="Searching"/> telling how many repositories
    /// have been checked. Finds out first whether the App may use any repository at all.
    /// </summary>
    public async Task FindRemoteTripsAsync(CancellationToken ct = default)
    {
        Searching = new(0, 0); SearchProblem = null; Changed?.Invoke();
        try
        {
            var provider = GitHub();
            NeedsRepositoryAccess = !await provider.HasRepositoryAccessAsync(ct);
            if (NeedsRepositoryAccess) return;
            var shown = -1;
            var progress = new Progress<DiscoveryProgress>(p =>
            {
                // Checks finish out of order; a late report must not move the bar back.
                if (Searching is not { } now || (p.Total == now.Total && p.Checked < now.Checked)) return;
                Searching = p;
                // Drawing once per percent keeps thousands of repositories from redrawing the page thousands of times.
                var percent = p.Total == 0 ? -1 : p.Checked * 100 / p.Total;
                if (percent != shown) { shown = percent; Changed?.Invoke(); }
            });
            RemoteTrips = await provider.DiscoverAsync(progress, ct);
        }
        catch (DomainException ex) when (ex.Code == "github.http.401") { await SignInEndedAsync(); }
        catch (Exception ex) when (ex is DomainException or HttpRequestException) { SearchProblem = ex is HttpRequestException ? Strings["SyncOffline"] : Problem(ex); }
        finally { Searching = null; Changed?.Invoke(); }
    }

    public void CloseRemoteTrips() { RemoteTrips = null; Changed?.Invoke(); }

    /// <summary>Copies a trip from GitHub to this phone and opens it.</summary>
    public async Task GetTripAsync(RemoteRepository repository, CancellationToken ct = default)
    {
        var name = repository.FullName.Split('/')[^1];
        var path = Path.Combine(Host.TripsRoot, name);
        for (var i = 2; Directory.Exists(path); i++) path = Path.Combine(Host.TripsRoot, name + " " + i);
        await GitRepository.CloneAsync(Host.Git, repository.CloneUrl, path, ct);
        Settings.LastSync[Path.GetFileName(path)] = DateTimeOffset.Now; Settings.Save();
        RemoteTrips = null;
        await LoadTripsAsync(ct);
        await OpenAsync(path, ct);
    }
}

/// <summary>A merge with GitHub's version of the trip that needs the user's choices.</summary>
public sealed record PendingMerge(string Branch, string Remote, TripSnapshot Baseline, TripSnapshot Incoming, TravelRepo.Merge.MergePlan Plan);
