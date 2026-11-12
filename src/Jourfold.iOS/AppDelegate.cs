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
        // The build fails without the framework (RequireLibgit2 in the project). Should it still be missing, the app
        // starts and says that something went wrong instead of closing before its first picture.
        try { LibGit2Library.Use(Path.Combine(NSBundle.MainBundle.PrivateFrameworksPath!, "git2.framework", "git2")); }
        catch (DllNotFoundException missing) { WhenReady(session => { session.ShowProblem(missing); return Task.CompletedTask; }); }
        var secrets = new IosSecretStore();
        var notices = new IosNotices(data, language, device);
        // A build can name the GitHub App to sign in with: -p:JourfoldGitHubClientId=... -p:JourfoldGitHubAppSlug=...
        string? Built(string key) => typeof(AppDelegate).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyMetadataAttribute), false)
            .OfType<System.Reflection.AssemblyMetadataAttribute>().FirstOrDefault(a => a.Key == key)?.Value;
        MobileApp.Host = new MobileHost(data, new LibGit2Backend(new GitHubCredentialBroker(secrets)), SystemClock.Instance,
            () => DateTimeZoneProviders.Tzdb.GetSystemDefault(), language, secrets, GitHubClientId: Built("GitHubClientId"), OpenFile: IosSystem.OpenFileAsync,
            GitHubAppSlug: Built("GitHubAppSlug"), Locate: IosLocation.LocateAsync, Device: device, Calendar: new IosCalendar(data), Notices: notices,
            Brightness: IosSystem.Bright, TakePhoto: IosSystem.HasCamera ? IosSystem.TakePhotoAsync : null, ShareText: IosSystem.ShareTextAsync,
            AppVersion: NSBundle.MainBundle.ObjectForInfoDictionary("CFBundleShortVersionString")?.ToString(), Haptic: Haptic);
        notices.Start();
        zone = NSTimeZone.SystemTimeZone.Name;
        // The build's smoke test on the simulator starts with the sample trip open, which needs Git to work.
        if (NSProcessInfo.ProcessInfo.Arguments.Contains("--open-sample")) WhenReady(async session => await session.OpenAsync(await session.CreateSampleAsync()));
        return base.CustomizeAppBuilder(builder);
    }

    /// <summary>
    /// Runs <paramref name="action"/> once the app is ready. After a cold start, such as from a notification or a file
    /// opened in Jourfold, that takes a moment, however long the device needs.
    /// </summary>
    internal static void WhenReady(Func<MobileSession, Task> action)
    {
        _ = Run();
        async Task Run()
        {
            var session = await MobileApp.Ready;
            Avalonia.Threading.Dispatcher.UIThread.Post(async () => await action(session));
        }
    }

    /// <summary>The short tap felt when a long press is reached, as on Android.</summary>
    private static readonly Lazy<UIImpactFeedbackGenerator> Impact = new(() => new UIImpactFeedbackGenerator(UIImpactFeedbackStyle.Medium));
    private static void Haptic() => Impact.Value.ImpactOccurred();

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
        // When the time iOS gives runs out, the version stops before the app is suspended, so no Git lock file or
        // half-written state is left for the next start.
        var cancel = new CancellationTokenSource();
        void End() { if (Interlocked.Exchange(ref ended, 1) == 0) application.EndBackgroundTask(task); }
        task = application.BeginBackgroundTask("version", () => { cancel.Cancel(); End(); });
        _ = Save();
        async Task Save()
        {
            try { await session.AutoSaveAsync(cancel.Token); }
            catch (OperationCanceledException) { }
            finally { End(); cancel.Dispose(); }
        }
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
