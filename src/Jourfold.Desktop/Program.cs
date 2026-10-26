using Avalonia;
namespace Jourfold.Desktop;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Apps started from the Finder get a short PATH without Homebrew or MacPorts, where Git usually comes from.
        if (OperatingSystem.IsMacOS())
            Environment.SetEnvironmentVariable("PATH", string.Join(':', new[] { "/opt/homebrew/bin", "/usr/local/bin", "/opt/local/bin" }.Concat((Environment.GetEnvironmentVariable("PATH") ?? "/usr/bin:/bin").Split(':')).Distinct()));
        if (args is ["--install-desktop"]) { Console.WriteLine(Jourfold.Infrastructure.DesktopIntegration.InstallLinuxLauncher(AppContext.BaseDirectory)); return; }
        if (args is ["--mcp", ..]) { Environment.ExitCode = ServeAssistant(args); return; }
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }
    /// <summary>
    /// <c>--mcp TRIP [--read-only]</c>: serves one trip to an AI assistant over standard input and output (see
    /// "Connect an AI assistant"). No window opens; standard output carries only the protocol. Weather comes only from
    /// Jourfold's cache, and only while weather is turned on.
    /// </summary>
    private static int ServeAssistant(string[] args)
    {
        if (args.Skip(1).FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal)) is not { } trip) { Console.Error.WriteLine("Usage: --mcp TRIP-FOLDER [--read-only]"); return 2; }
        try { TravelRepo.Mcp.TripServer.RunStdioAsync(trip, new(ReadOnly: args.Contains("--read-only"), ClientName: "Jourfold", Weather: () => MainViewModel.CachedWeatherForAssistants())).GetAwaiter().GetResult(); return 0; }
        catch (TravelRepo.Core.DomainException ex) { Console.Error.WriteLine(ex.Message); return 2; }
    }
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().With(new X11PlatformOptions { WmClass = "Jourfold", OverlayPopups = true }).UsePlatformDetect().LogToTrace();
}
