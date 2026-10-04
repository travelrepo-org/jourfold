using Avalonia;
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
        var services = new ServiceCollection(); services.AddSingleton<LocalStore>(); services.AddSingleton<OsSecretStore>(); services.AddSingleton<IGitBackend>(sp => new GitCliBackend(credentialBroker: new GitHubCredentialBroker(sp.GetRequiredService<OsSecretStore>()))); Services = services.BuildServiceProvider();
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Only the real desktop app catches stray exceptions; tests and the screenshot tool must still see them.
            ErrorReporting.Install(Services.GetRequiredService<LocalStore>().Root);
            var window = new MainWindow(); desktop.MainWindow = window;
            if (desktop.Args?.FirstOrDefault() is { } link && link.StartsWith("jourfold:", StringComparison.OrdinalIgnoreCase)) window.Opened += async (_, _) => await window.Model.OpenLinkAsync(link); desktop.Exit += (_, _) => (Services as IDisposable)?.Dispose();
        }
        base.OnFrameworkInitializationCompleted();
    }
}
