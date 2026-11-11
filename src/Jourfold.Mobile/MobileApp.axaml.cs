using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace Jourfold.Mobile;

/// <summary>
/// The phone and tablet app. The platform app sets <see cref="Host"/> before Avalonia starts; on Android every
/// activity gets its own shell over the one shared session.
/// </summary>
public sealed class MobileApp : Avalonia.Application
{
    public static MobileHost? Host { get; set; }
    public static MobileSession? Session { get; private set; }

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        var host = Host ?? throw new InvalidOperationException("The platform app must set MobileApp.Host before starting.");
        var session = Session ??= new MobileSession(host);
        // A failure nobody waited for ends nothing, but is kept in the error log on this device.
        TaskScheduler.UnobservedTaskException += (_, e) => { session.LogError(e.Exception); e.SetObserved(); };
        Control Shell() => new AppShell(session);
        if (ApplicationLifetime is IActivityApplicationLifetime activity) activity.MainViewFactory = Shell;
        else if (ApplicationLifetime is ISingleViewApplicationLifetime single) single.MainView = Shell();
        base.OnFrameworkInitializationCompleted();
    }
}
