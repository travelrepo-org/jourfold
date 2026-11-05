using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.iOS;
using Avalonia.Platform.Storage;
using Foundation;
using Jourfold.Application;
using Jourfold.Mobile;
using NodaTime;
using TravelRepo.Git.LibGit2;
using UIKit;

namespace Jourfold.iOS;

public static class Program
{
    private static void Main(string[] args) => UIApplication.Main(args, null, typeof(AppDelegate));
}

/// <summary>
/// Starts Avalonia with the shared mobile app and hands it this device's folders, Git, Keychain, clock and time zone.
/// It also takes files opened in Jourfold from other apps, and turns open changes into a version when the app leaves
/// the screen.
/// </summary>
[Register("AppDelegate")]
public sealed class AppDelegate : AvaloniaAppDelegate<MobileApp>
{
    private string? zone;

    public AppDelegate()
    {
        ((IAvaloniaAppDelegate)this).Activated += OnActivated;
        ((IAvaloniaAppDelegate)this).Deactivated += OnDeactivated;
    }

    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
    {
        // Trips live in Application Support: private to the app, kept in the device's backup, not shown in Files.
        var data = NSFileManager.DefaultManager.GetUrls(NSSearchPathDirectory.ApplicationSupportDirectory, NSSearchPathDomain.User)[0].Path!;
        Directory.CreateDirectory(data);
        // The language the system chose for Jourfold, also when set for this app alone in the Settings app.
        var language = NSBundle.MainBundle.PreferredLocalizations.FirstOrDefault() == "de" ? "de" : "en";
        var device = NSProcessInfo.ProcessInfo.IsiOSApplicationOnMac ? "device" : UIDevice.CurrentDevice.UserInterfaceIdiom == UIUserInterfaceIdiom.Pad ? "tablet" : "phone";
        LibGit2Backend.UseNativeLibrary(Path.Combine(NSBundle.MainBundle.PrivateFrameworksPath!, "git2.framework", "git2"));
        var secrets = new IosSecretStore();
        var notices = new IosNotices(data, language, device);
        // A build can name the GitHub App to sign in with: -p:JourfoldGitHubClientId=... -p:JourfoldGitHubAppSlug=...
        string? Built(string key) => typeof(AppDelegate).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyMetadataAttribute), false)
            .OfType<System.Reflection.AssemblyMetadataAttribute>().FirstOrDefault(a => a.Key == key)?.Value;
        MobileApp.Host = new MobileHost(data, new LibGit2Backend(new GitHubCredentialBroker(secrets)), SystemClock.Instance,
            () => DateTimeZoneProviders.Tzdb.GetSystemDefault(), language, secrets, GitHubClientId: Built("GitHubClientId"), OpenFile: IosSystem.OpenFileAsync,
            GitHubAppSlug: Built("GitHubAppSlug"), Locate: IosLocation.LocateAsync, Device: device, Calendar: new IosCalendar(data), Notices: notices,
            Brightness: IosSystem.Bright, TakePhoto: IosSystem.HasCamera ? IosSystem.TakePhotoAsync : null, ShareText: IosSystem.ShareTextAsync,
            AppVersion: NSBundle.MainBundle.ObjectForInfoDictionary("CFBundleShortVersionString")?.ToString());
        notices.Start();
        zone = NSTimeZone.SystemTimeZone.Name;
        // The build's smoke test on the simulator starts with the sample trip open, which needs Git to work.
        if (NSProcessInfo.ProcessInfo.Arguments.Contains("--open-sample")) WhenReady(async session => await session.OpenAsync(await session.CreateSampleAsync()));
        return base.CustomizeAppBuilder(builder);
    }

    /// <summary>
    /// Runs <paramref name="action"/> once the app is ready. After a cold start, such as from a notification or a file
    /// opened in Jourfold, that takes a moment.
    /// </summary>
    internal static void WhenReady(Func<MobileSession, Task> action, int tries = 25) => Avalonia.Threading.Dispatcher.UIThread.Post(async () =>
    {
        if (MobileApp.Session is { } session) { await action(session); return; }
        if (tries == 0) return;
        await Task.Delay(200);
        WhenReady(action, tries - 1);
    });

    private void OnActivated(object? sender, ActivatedEventArgs e)
    {
        if (e is FileActivatedEventArgs opened) { _ = ReceiveAsync(opened.Files); return; }
        if (e.Kind != ActivationKind.Background || MobileApp.Host is not { } host) return;
        // Back on screen: notices may be due, and after a flight the time zone may have changed.
        var now = NSTimeZone.SystemTimeZone.Name; var changed = zone is not null && now != zone; zone = now;
        _ = IosNotices.RunAsync(host, changed);
    }

    /// <summary>
    /// Leaving the screen turns open changes into a version right away. iOS suspends the app a few seconds later, so it
    /// asks for the time the version needs.
    /// </summary>
    private static void OnDeactivated(object? sender, ActivatedEventArgs e)
    {
        if (e.Kind != ActivationKind.Background || MobileApp.Session is not { } session) return;
        var application = UIApplication.SharedApplication; nint task = 0; var ended = 0;
        void End() { if (Interlocked.Exchange(ref ended, 1) == 0) application.EndBackgroundTask(task); }
        task = application.BeginBackgroundTask("version", End);
        _ = Save();
        async Task Save() { try { await session.AutoSaveAsync(); } finally { End(); } }
    }

    /// <summary>
    /// Files opened in Jourfold from another app, such as a ticket from Mail or a PDF from the share sheet. iOS hands
    /// over a copy in the app's Inbox folder, which is removed once read.
    /// </summary>
    private static async Task ReceiveAsync(IEnumerable<IStorageItem> items)
    {
        var files = new List<SharedFile>();
        foreach (var file in items.OfType<IStorageFile>())
        {
            try
            {
                await using (var stream = await file.OpenReadAsync())
                {
                    using var copy = new MemoryStream(); await stream.CopyToAsync(copy);
                    files.Add(new(file.Name, TravelRepo.Documents.MediaTypes.FromFileName(file.Name), copy.ToArray()));
                }
                if (file.TryGetLocalPath() is { } path && path.Contains("/Inbox/", StringComparison.Ordinal)) File.Delete(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            finally { file.Dispose(); }
        }
        if (files.Count > 0) WhenReady(session => { session.Receive(new SharedContent(files, null)); return Task.CompletedTask; });
    }
}
