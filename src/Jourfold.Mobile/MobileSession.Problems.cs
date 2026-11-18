using System.Text;
using Jourfold.Application;
using TravelRepo.Core;

namespace Jourfold.Mobile;

public sealed partial class MobileSession
{
    private const long MaxLogBytes = 512 * 1024;

    /// <summary>The heading of the problem being shown, such as "Not saved".</summary>
    public string? ProblemTitle { get; private set; }

    /// <summary>Why something the user did failed, shown until they close it.</summary>
    public string? ChangeProblem { get; private set; }

    /// <summary>Where unexpected errors are written. They stay on this device; nothing is sent.</summary>
    public string ErrorLog => Path.Combine(Host.DataRoot, "logs", "errors.log");

    /// <summary>
    /// Shows why something the user did failed instead of ending the app. Expected failures, such as a change TravelRepo
    /// refuses, say why; any other error says that something went wrong and is written to <see cref="ErrorLog"/>.
    /// </summary>
    public void ShowProblem(Exception exception)
    {
        var expected = IsExpected(exception);
        if (!expected) LogError(exception);
        // The first problem stays: a failure often brings others with it.
        if (ChangeProblem is not null) return;
        ProblemTitle = Strings[expected ? "NotSaved" : "SomethingWentWrong"];
        ChangeProblem = expected ? Strings.Error(exception) : Strings["UnexpectedErrorPhone"];
        ProblemCanBeMailed = !expected && Host.SendMail is not null;
        Changed?.Invoke();
    }

    /// <summary>
    /// Whether the problem shown can be sent by e-mail (<see cref="SendProblemByMailAsync"/>): an unexpected failure,
    /// on a device that has a mail app to hand it to. Expected failures say all there is in their message.
    /// </summary>
    public bool ProblemCanBeMailed { get; private set; }

    public void DismissProblem() { if (ChangeProblem is null) return; ChangeProblem = null; ProblemTitle = null; ProblemCanBeMailed = false; Changed?.Invoke(); }

    /// <summary>
    /// Opens the user's mail app with a problem report: a few lines to fill in, the facts about the device the app knows
    /// without any permission (model, system, build, screen), and a copy of the error log as attachment. The user reads,
    /// edits and sends it there; Jourfold sends nothing. The address is the public contact of the project on GitHub,
    /// looked up only now; without it the mail opens without one.
    /// </summary>
    public async Task SendProblemByMailAsync(CancellationToken ct = default)
    {
        if (Host.SendMail is not { } send) return;
        var to = await SupportContact.EmailAsync(Settings, MapsHttp, ct: ct);
        var facts = new List<(string Key, string Value)> { ("ReportInfoApp", "Jourfold " + (Host.AppVersion ?? "?") + ((Host.Distribution ?? AppDistribution.Name) is { Length: > 0 } source ? " (" + source + ")" : "")) };
        facts.AddRange(Host.SystemInfo?.Invoke() ?? []);
        facts.Add(("ReportInfoKind", Host.Device));
        facts.Add(("ReportInfoLanguage", Host.Language));
        var body = Strings["ReportTemplate"] + "\n\n" + Strings["ReportMailFacts"] + "\n" + string.Join("\n", facts.Where(f => f.Value.Length > 0).Select(f => Strings[f.Key] + ": " + f.Value));
        // The log goes as a copy beside the other files handed to other apps, which the next one replaces.
        if (Directory.Exists(Host.SharedRoot)) Directory.Delete(Host.SharedRoot, recursive: true);
        Directory.CreateDirectory(Host.SharedRoot);
        var attachments = new List<string>();
        foreach (var log in new[] { ErrorLog + ".1", ErrorLog }.Where(File.Exists))
        {
            var copy = Path.Combine(Host.SharedRoot, Path.GetFileName(log) == "errors.log" ? "jourfold-errors.txt" : "jourfold-errors-older.txt");
            File.Copy(log, copy, overwrite: true); attachments.Add(copy);
        }
        if (await send(new ProblemMail(to, string.Format(System.Globalization.CultureInfo.CurrentCulture, Strings["ReportSubject"], Host.AppVersion ?? ""), body, attachments)))
        {
            DismissProblem(); return;
        }
        ChangeProblem = Strings["ReportNoMailApp"]; ProblemCanBeMailed = false; Changed?.Invoke();
    }

    /// <summary>
    /// Failures that come from the trip or the device rather than from a bug, such as a trip with errors that TravelRepo
    /// will not change. Their message is shown as it is.
    /// </summary>
    public static bool IsExpected(Exception exception) => exception is DomainException or IOException or UnauthorizedAccessException;

    /// <summary>Writes an error with the app's version to <see cref="ErrorLog"/>, which is kept below half a megabyte.</summary>
    public void LogError(Exception exception)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ErrorLog)!);
            if (File.Exists(ErrorLog) && new FileInfo(ErrorLog).Length > MaxLogBytes) File.Move(ErrorLog, ErrorLog + ".1", true);
            File.AppendAllText(ErrorLog, DateTimeOffset.Now.ToString("O") + "\nJourfold " + (Host.AppVersion ?? "") + " (" + Host.Device + ")\n\n" + exception + "\n\n", new UTF8Encoding(false));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>The open trip's errors, in words. TravelRepo refuses every change while there are any.</summary>
    public IReadOnlyList<string> TripErrors =>
        Workspace?.State.Diagnostics.Where(d => d.Severity == Severity.Error).Select(d => Strings.Diagnostic(d.Code)).Distinct().ToArray() ?? [];
}
