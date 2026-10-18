using System.Text;
using TravelRepo.Core;
using TravelRepo.Git;

namespace Jourfold.Desktop;

/// <summary>
/// Explaining failures: a plain sentence, the fixes Jourfold can make safely, and technical details the person can
/// copy into a bug report. Nothing is sent anywhere.
/// </summary>
public partial class MainViewModel
{
    /// <summary>Shows the problem and returns the fix the person chose, or <c>null</c>. The caller tries the action again after the fix.</summary>
    private async Task<Func<Task>?> ExplainAsync(Exception exception)
    {
        GitHealth? health = null;
        if (exception is GitCommandException && Workspace is { } workspace)
        {
            try { health = await workspace.Git.DiagnoseAsync(await PreferredRemote(ask: false)); }
            catch (DomainException) { }
        }
        var fixes = await FixesAsync(exception, health);
        var message = Strings.Error(exception) + (exception is DomainException domain && Strings.All.TryGetValue("problem." + domain.Code, out var hint) ? "\n\n" + hint : "");
        var choice = await Interaction.ProblemAsync(new(Strings["SomethingWentWrong"], message, ProblemDetails(exception, health), fixes.Select(f => f.Choice).ToArray()));
        return fixes.FirstOrDefault(f => f.Choice.Id == choice).Run;
    }

    private async Task<List<(Choice Choice, Func<Task> Run)>> FixesAsync(Exception exception, GitHealth? health)
    {
        var list = new List<(Choice, Func<Task>)>();
        if (Workspace is not { } workspace) return list;
        IReadOnlyDictionary<string, string> remotes = exception is GitCommandException { Failure: GitFailure.Authentication } ? await RemotesAsync() : new Dictionary<string, string>();
        var git = workspace.Git;
        switch ((exception as GitCommandException)?.Failure)
        {
            case GitFailure.Locked when health?.LockAge >= StaleLockAge:
                list.Add((new("lock", Strings["FixRemoveLock"], "wrench"), async () => { await git.RemoveStaleLockAsync(StaleLockAge); }));
                break;
            case GitFailure.WouldOverwrite when exception is GitCommandException { Paths.Count: > 0 } blocked:
                list.Add((new("aside", Strings["FixMoveAside"], "wrench"), async () =>
                {
                    var folder = await git.MoveAsideAsync(blocked.Paths);
                    Interaction.Toast(string.Format(Strings["MovedAside"], blocked.Paths.Count), Strings["ShowFolder"], () => { Interaction.Open(folder); return Task.CompletedTask; });
                }
                ));
                break;
            case GitFailure.OperationInProgress:
                list.Add((new("abort", Strings["FixAbortOperation"], "wrench"), async () =>
                {
                    if (!await Interaction.ConfirmAsync(Strings["FixAbortOperation"], Strings["FixAbortOperationBody"], Strings["FixAbortOperation"], danger: true)) throw new OperationCanceledException();
                    await git.AbortOperationAsync();
                }
                ));
                break;
            case GitFailure.Authentication when Store.Get("remote:" + workspace.Repository.Root) is { } remote && remotes.TryGetValue(remote, out var url) && IsGitHubUrl(url):
                list.Add((new("github", Strings["ConnectGitHub"], "cloud"), ConnectGitHubCoreAsync));
                break;
        }
        if (exception is GitCommandException { Failure: not (GitFailure.DubiousOwnership or GitFailure.NotARepository or GitFailure.NothingToCommit or GitFailure.DetachedHead) } && list.Count == 0)
            list.Add((new("retry", Strings["TryAgain"], "refresh-cw"), () => Task.CompletedTask));
        return list;
    }

    /// <summary>A lock file older than this belongs to a Git process that stopped, not to one that is running.</summary>
    public static readonly TimeSpan StaleLockAge = TimeSpan.FromSeconds(30);

    /// <summary>Versions, the error code and message, Git's redacted output and the local Git state, for a bug report.</summary>
    public static string ProblemDetails(Exception exception, GitHealth? health = null)
    {
        var text = new StringBuilder()
            .AppendLine("Jourfold " + AppVersion.Informational)
            .AppendLine("TravelRepo " + TravelRepoInfo.InformationalVersion + ", format " + TravelRepoInfo.FormatVersion)
            .AppendLine(AppVersion.Platform).AppendLine();
        if (exception is DomainException domain) text.AppendLine("error: " + domain.Code).AppendLine(domain.Message);
        else text.AppendLine(exception.ToString());
        if (exception is GitCommandException git) text.AppendLine().AppendLine(git.Details);
        if (health is not null) text.AppendLine().AppendLine(health.Summary());
        return GitErrorText.Redact(text.ToString().TrimEnd());
    }
}
