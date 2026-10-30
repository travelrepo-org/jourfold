using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Runtime;
using Avalonia.Android;
using Jourfold.Application;
using Jourfold.Mobile;
using NodaTime;
using TravelRepo.Git.LibGit2;

namespace Jourfold.Android;

/// <summary>Starts Avalonia with the shared mobile app and hands it this phone's folders, Git, secure storage, clock and time zone.</summary>
[Application(Label = "Jourfold", Icon = "@mipmap/ic_launcher", RoundIcon = "@mipmap/ic_launcher")]
public sealed class JourfoldApplication : AvaloniaAndroidApplication<MobileApp>
{
    private JourfoldApplication(IntPtr handle, JniHandleOwnership ownership) : base(handle, ownership) { }

    public override void OnCreate()
    {
        var language = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "de" ? "de" : "en";
        var secrets = new AndroidSecretStore(this);
        // A build can name the GitHub App to sign in with: -p:JourfoldGitHubClientId=... -p:JourfoldGitHubAppSlug=...
        string? Built(string key) => typeof(JourfoldApplication).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyMetadataAttribute), false)
            .OfType<System.Reflection.AssemblyMetadataAttribute>().FirstOrDefault(a => a.Key == key)?.Value;
        MobileApp.Host = new MobileHost(FilesDir!.AbsolutePath, new LibGit2Backend(new GitHubCredentialBroker(secrets)), NodaTime.SystemClock.Instance,
            () => DateTimeZoneProviders.Tzdb.GetSystemDefault(), language, secrets, GitHubClientId: Built("GitHubClientId"), OpenFile: OpenFile, GitHubAppSlug: Built("GitHubAppSlug"),
            Locate: AndroidLocation.LocateAsync, Device: Device(), Calendar: new AndroidCalendar(this), Notices: new AndroidNotices(this, language, Device()), Brightness: Bright);
        base.OnCreate();
    }

    /// <summary>
    /// What the system would call this device: a phone when it can make calls (foldables too, as their makers call
    /// them), a computer-like device such as a Chromebook, otherwise a tablet. The window size does not count; a
    /// phone on a monitor in DeX stays a phone.
    /// </summary>
    private string Device()
    {
        if (PackageManager?.HasSystemFeature("android.hardware.type.pc") == true) return "device";
        return GetSystemService(TelephonyService) is global::Android.Telephony.TelephonyManager { IsVoiceCapable: true } ? "phone" : "tablet";
    }

    /// <summary>Full brightness while a ticket is on screen, so a scanner reads it; back to the system's setting after.</summary>
    private static void Bright(bool on)
    {
        if (MainActivity.Current?.Window is not { } window || window.Attributes is not { } attributes) return;
        attributes.ScreenBrightness = on ? 1f : global::Android.Views.WindowManagerLayoutParams.BrightnessOverrideNone;
        window.Attributes = attributes;
    }

    /// <summary>Offers a file to the apps that can show its media type, through the app's FileProvider.</summary>
    private Task<bool> OpenFile(string path, string mediaType)
    {
        var uri = AndroidX.Core.Content.FileProvider.GetUriForFile(this, PackageName + ".files", new Java.IO.File(path));
        var view = new Intent(Intent.ActionView).SetDataAndType(uri, mediaType).AddFlags(ActivityFlags.GrantReadUriPermission);
        if (view.ResolveActivity(PackageManager!) is null) return Task.FromResult(false);
        StartActivity(Intent.CreateChooser(view, (string?)null)!.AddFlags(ActivityFlags.NewTask | ActivityFlags.GrantReadUriPermission));
        return Task.FromResult(true);
    }
}

// Tickets, PDFs, e-mails and places come in through the system share sheet.
[IntentFilter([Intent.ActionSend, Intent.ActionSendMultiple], Categories = [Intent.CategoryDefault], DataMimeType = "*/*")]
[Activity(Label = "Jourfold", Theme = "@style/Theme.Jourfold.Starting", MainLauncher = true, Exported = true, LaunchMode = LaunchMode.SingleTask,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density
        | ConfigChanges.Keyboard | ConfigChanges.KeyboardHidden | ConfigChanges.Navigation)]
public sealed class MainActivity : AvaloniaMainActivity
{
    /// <summary>The activity on screen, for questions such as the location permission.</summary>
    public static MainActivity? Current { get; private set; }

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        Current = this;
        AndroidX.Core.SplashScreen.SplashScreen.InstallSplashScreen(this);
        base.OnCreate(savedInstanceState);
        // Not again when Android rebuilds the activity, for example after the screen turned.
        if (savedInstanceState is null) Share(Intent);
    }

    protected override void OnNewIntent(Intent? intent)
    {
        base.OnNewIntent(intent);
        Share(intent);
    }

    private void Share(Intent? intent)
    {
        // A tap on a notification opens its trip and plan.
        if (intent?.GetStringExtra(AndroidNotices.ExtraTrip) is { } trip)
        {
            var item = Guid.TryParse(intent.GetStringExtra(AndroidNotices.ExtraItem), out var id) ? id : (Guid?)null;
            var ticket = intent.GetBooleanExtra(AndroidNotices.ExtraTicket, false);
            OpenNotice(trip, item, ticket, tries: 25);
            return;
        }
        if (AndroidSharing.Read(this, intent) is { } shared) Avalonia.Threading.Dispatcher.UIThread.Post(() => MobileApp.Session?.Receive(shared));
    }

    /// <summary>Opens what a notification is about once the app is ready; after a cold start that takes a moment.</summary>
    private static void OpenNotice(string trip, Guid? item, bool ticket, int tries) => Avalonia.Threading.Dispatcher.UIThread.Post(async () =>
    {
        if (MobileApp.Session is { } session) { await session.OpenNoticeAsync(trip, item, ticket); return; }
        if (tries == 0) return;
        await Task.Delay(200);
        OpenNotice(trip, item, ticket, tries - 1);
    });

    protected override void OnDestroy() { if (Current == this) Current = null; base.OnDestroy(); }

    public override void OnRequestPermissionsResult(int requestCode, string[] permissions, Permission[] grantResults)
    {
        base.OnRequestPermissionsResult(requestCode, permissions, grantResults);
        AndroidLocation.Answered(requestCode, grantResults);
        AndroidCalendar.Answered(requestCode, grantResults);
        AndroidNotices.Answered(requestCode, grantResults);
    }

    /// <summary>Leaving the app turns open changes into a version right away; Android may stop the app at any time after.</summary>
    protected override void OnPause()
    {
        base.OnPause();
        if (MobileApp.Session is { } session) _ = session.AutoSaveAsync();
    }
}
