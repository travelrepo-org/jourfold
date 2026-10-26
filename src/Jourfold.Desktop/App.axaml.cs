using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Jourfold.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using TravelRepo.Git;
namespace Jourfold.Desktop;

public partial class App : Avalonia.Application
{
    public static IServiceProvider Services { get; private set; } = null!;
    public override void Initialize() => AvaloniaXamlLoader.Load(this);
    public override void OnFrameworkInitializationCompleted()
    {
        // Only the real desktop app may show system dialogs for stored secrets; headless tests run without anyone to answer.
        var services = new ServiceCollection(); services.AddSingleton<LocalStore>(); services.AddSingleton(new OsSecretStore(interactive: ApplicationLifetime is IClassicDesktopStyleApplicationLifetime)); services.AddSingleton<IGitBackend>(sp => new GitCliBackend(credentialBroker: new GitHubCredentialBroker(sp.GetRequiredService<OsSecretStore>()))); Services = services.BuildServiceProvider();
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Only the real desktop app catches stray exceptions; tests and the screenshot tool must still see them.
            ErrorReporting.Install(Services.GetRequiredService<LocalStore>().Root);
            var window = new MainWindow(); desktop.MainWindow = window;
            if (desktop.Args?.FirstOrDefault() is { } link && link.StartsWith("jourfold:", StringComparison.OrdinalIgnoreCase)) window.Opened += async (_, _) => await window.Model.OpenLinkAsync(link); desktop.Exit += (_, _) => (Services as IDisposable)?.Dispose();
            if (OperatingSystem.IsMacOS()) NativeMenu.SetMenu(this, AppMenu(desktop, window));
            // macOS hands trip links to the running app instead of passing them on the command line.
            if (TryGetFeature(typeof(IActivatableLifetime)) is IActivatableLifetime activation)
                activation.Activated += async (_, e) =>
                {
                    if (e is ProtocolActivatedEventArgs { Uri: { Scheme: "jourfold" } uri }) await ((desktop.MainWindow as MainWindow) ?? window).Model.OpenLinkAsync(uri.OriginalString);
                };
        }
        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// The application menu on macOS: About and Settings open Jourfold's own dialogs in the front window, instead of
    /// the defaults Avalonia would add.
    /// </summary>
    private static NativeMenu AppMenu(IClassicDesktopStyleApplicationLifetime desktop, MainWindow first)
    {
        MainWindow Front() => desktop.Windows.OfType<MainWindow>().FirstOrDefault(w => w.IsActive) ?? (desktop.MainWindow as MainWindow) ?? first;
        var strings = Front().Model.Strings;
        var about = new NativeMenuItem(strings["About"]); about.Click += (_, _) => Front().Model.AboutCommand.Execute(null);
        var settings = new NativeMenuItem(strings["Settings"] + "…");
        settings.Click += (_, _) => Front().Model.SettingsCommand.Execute(null);
        return new NativeMenu { Items = { about, new NativeMenuItemSeparator(), settings } };
    }
}
