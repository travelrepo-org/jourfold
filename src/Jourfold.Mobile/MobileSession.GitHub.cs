using TravelRepo.Core;
using TravelRepo.Git;
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

    /// <summary>The GitHub App to sign in with: the one named in settings, otherwise the one built into the app.</summary>
    public string? ClientId => Settings.GitHubClientId is { Length: > 0 } id ? id : Host.GitHubClientId is { Length: > 0 } built ? built : null;

    public DateTimeOffset? LastSync => Workspace is { } w && Settings.LastSync.TryGetValue(Path.GetFileName(w.Repository.Root), out var at) ? at : null;

    private GitHubProvider GitHub() => new(http ??= Host.Http is null ? new HttpClient() : new HttpClient(Host.Http, disposeHandler: false), Host.Secrets, ClientId ?? "");

    /// <summary>Reads the stored sign-in without going online; the account name comes from the last sign-in.</summary>
    public async Task LoadGitHubAsync(CancellationToken ct = default)
    {
        GitHubLogin = await GitHub().IsConnectedAsync(ct) ? Settings.GitHubLogin ?? "GitHub" : null;
        Changed?.Invoke();
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
        signIn?.Dispose(); signIn = CancellationTokenSource.CreateLinkedTokenSource(ct);
        try
        {
            SignInCode = await provider.BeginAsync(signIn.Token); Changed?.Invoke();
            await provider.CompleteAsync(SignInCode, signIn.Token);
            var account = await provider.AccountAsync(signIn.Token);
            GitHubLogin = account?.Login ?? "GitHub"; Settings.GitHubLogin = account?.Login;
            // The account's name is a good start for the name versions are saved under.
            if (Settings.Name is null && account?.Name is { Length: > 0 } name) Settings.Name = name;
            Settings.Save();
        }
        catch (OperationCanceledException) when (signIn.IsCancellationRequested && !ct.IsCancellationRequested) { }
        catch (HttpRequestException) { SignInProblem = Strings["SyncOffline"]; }
        catch (DomainException ex) { SignInProblem = Problem(ex); }
        finally { SignInCode = null; signIn.Dispose(); signIn = null; Changed?.Invoke(); }
    }

    public void CancelSignIn() => signIn?.Cancel();

    /// <summary>GitHub's answers carry their HTTP status in the code; they all read the same to the user.</summary>
    private string Problem(Exception ex) => ex is DomainException { Code: var code } && code.StartsWith("github.http.", StringComparison.Ordinal) ? Strings["GitHubRequestFailed"] : Strings.Error(ex);

    /// <summary>Forgets the sign-in on this phone. The trips stay.</summary>
    public async Task SignOutAsync(CancellationToken ct = default)
    {
        await GitHub().SignOutAsync(ct);
        GitHubLogin = null; Settings.GitHubLogin = null; Settings.Save(); RemoteTrips = null; Changed?.Invoke();
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
            var state = await workspace.Git.SyncAsync(remote, ct);
            if (state == SyncState.Diverged)
            {
                var branch = "refs/remotes/" + remote + "/" + await workspace.Git.CurrentBranchAsync(ct);
                var plan = await workspace.CompareMergeAsync(branch, ct);
                if (!plan.CanApply) { SyncProblem = Strings["SyncNeedsComputer"]; return; }
                await workspace.ApplyMergeAsync(branch, plan, ct);
                await workspace.Git.PushAsync(remote, ct);
            }
            await workspace.ReloadAsync(ct);
            Settings.LastSync[Path.GetFileName(workspace.Repository.Root)] = DateTimeOffset.Now; Settings.Save();
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

    /// <summary>Lists the trips on GitHub this account can open.</summary>
    public async Task FindRemoteTripsAsync(CancellationToken ct = default)
    {
        RemoteTrips = await GitHub().DiscoverAsync(ct); Changed?.Invoke();
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
