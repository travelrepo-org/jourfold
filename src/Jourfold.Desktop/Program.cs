using Avalonia;
namespace Jourfold.Desktop;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        if (args is ["--install-desktop"]) { Console.WriteLine(Jourfold.Infrastructure.DesktopIntegration.InstallLinuxLauncher(AppContext.BaseDirectory)); return; }
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().With(new X11PlatformOptions { WmClass = "Jourfold", OverlayPopups = true }).UsePlatformDetect().LogToTrace();
}
