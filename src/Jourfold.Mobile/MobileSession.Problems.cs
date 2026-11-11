using System.Text;
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
        Changed?.Invoke();
    }

    public void DismissProblem() { if (ChangeProblem is null) return; ChangeProblem = null; ProblemTitle = null; Changed?.Invoke(); }

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
