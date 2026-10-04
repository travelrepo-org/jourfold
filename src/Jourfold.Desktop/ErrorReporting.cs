using System.Text;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;

namespace Jourfold.Desktop;

/// <summary>
/// Last line of defence against programming errors. An exception that escapes an event handler stops the action that
/// caused it instead of closing Jourfold: it is written to <c>logs/errors.log</c> in the local state folder and the
/// person sees a short notice with the details on request. Nothing is sent anywhere.
/// </summary>
public static class ErrorReporting
{
    private const long MaxLogBytes = 512 * 1024;
    private static string? logFile;
    private static readonly Queue<DateTime> Recent = new();

    public static string? LogFile => logFile;

    /// <summary>Where errors are written: <c>logs/errors.log</c> under <paramref name="stateRoot"/>.</summary>
    public static void UseLog(string stateRoot) => logFile = Path.Combine(stateRoot, "logs", "errors.log");

    public static void Install(string stateRoot)
    {
        UseLog(stateRoot);
        Dispatcher.UIThread.UnhandledException += (_, e) => { e.Handled = true; Handle(e.Exception); };
        TaskScheduler.UnobservedTaskException += (_, e) => { Write(e.Exception); e.SetObserved(); };
        AppDomain.CurrentDomain.UnhandledException += (_, e) => { if (e.ExceptionObject is Exception ex) Write(ex); };
    }

    /// <summary>Records the error and tells the person, unless errors repeat so fast that a notice would not help.</summary>
    public static void Handle(Exception exception)
    {
        Write(exception);
        var now = DateTime.UtcNow; Recent.Enqueue(now);
        while (Recent.Count > 0 && now - Recent.Peek() > TimeSpan.FromSeconds(10)) Recent.Dequeue();
        if (Recent.Count > 3) return;
        if (Avalonia.Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime { MainWindow: MainWindow window }) return;
        var s = window.Model.Strings; var details = Details(exception);
        window.Model.Interaction.Toast(s["UnexpectedError"], s["Details"], () => window.Model.Interaction.ShowAsync(s["UnexpectedErrorTitle"], string.Format(s["UnexpectedErrorBody"], logFile) + "\n\n" + details));
    }

    public static string Details(Exception exception) => "Jourfold " + AppVersion.Informational + "\n" + AppVersion.Platform + "\n\n" + exception;

    private static void Write(Exception exception)
    {
        if (logFile is null) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(logFile)!);
            if (File.Exists(logFile) && new FileInfo(logFile).Length > MaxLogBytes) File.Move(logFile, logFile + ".1", true);
            File.AppendAllText(logFile, DateTimeOffset.Now.ToString("O") + "\n" + Details(exception) + "\n\n", new UTF8Encoding(false));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
