using Avalonia;
namespace Jourfold.Desktop;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        if (args is ["--install-desktop"]) { Console.WriteLine(Jourfold.Infrastructure.DesktopIntegration.InstallLinuxLauncher(AppContext.BaseDirectory)); return; }
        if (args is ["--mcp", ..]) { Environment.ExitCode = ServeAssistant(args); return; }
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }
    /// <summary>
    /// <c>--mcp TRIP [--read-only]</c>: serves one trip to an AI assistant over standard input and output (see
    /// "Connect an AI assistant"). No window opens; standard output carries only the protocol.
    /// </summary>
    private static int ServeAssistant(string[] args)
    {
        if (args.Skip(1).FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal)) is not { } trip) { Console.Error.WriteLine("Usage: --mcp TRIP-FOLDER [--read-only]"); return 2; }
        try { TravelRepo.Mcp.TripServer.RunStdioAsync(trip, new(ReadOnly: args.Contains("--read-only"), ClientName: "Jourfold")).GetAwaiter().GetResult(); return 0; }
        catch (TravelRepo.Core.DomainException ex) { Console.Error.WriteLine(ex.Message); return 2; }
    }
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().With(new X11PlatformOptions { WmClass = "Jourfold", OverlayPopups = true }).UsePlatformDetect().LogToTrace();
}
