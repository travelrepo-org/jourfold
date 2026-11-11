using TravelRepo.Core;

namespace Jourfold.Mobile;

public sealed partial class MobileSession
{
    /// <summary>Why the last change was not saved, shown until the user closes it.</summary>
    public string? ChangeProblem { get; private set; }

    /// <summary>Shows that something the user did failed in an expected way, such as a change TravelRepo refuses.</summary>
    public void ShowProblem(Exception exception) { ChangeProblem = Strings.Error(exception); Changed?.Invoke(); }
    public void DismissProblem() { if (ChangeProblem is null) return; ChangeProblem = null; Changed?.Invoke(); }

    /// <summary>
    /// Failures that come from the trip or the device rather than from a bug, such as a trip with errors that TravelRepo
    /// will not change. They are shown instead of ending the app.
    /// </summary>
    public static bool IsExpected(Exception exception) => exception is DomainException or IOException or UnauthorizedAccessException;

    /// <summary>The open trip's errors, in words. TravelRepo refuses every change while there are any.</summary>
    public IReadOnlyList<string> TripErrors =>
        Workspace?.State.Diagnostics.Where(d => d.Severity == Severity.Error).Select(d => Strings.Diagnostic(d.Code)).Distinct().ToArray() ?? [];
}
