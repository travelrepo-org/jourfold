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
            Locate: AndroidLocation.LocateAsync, Device: Device(), Calendar: new AndroidCalendar(this), Notices: new AndroidNotices(this, language, Device()), Brightness: Bright, TakePhoto: MainActivity.TakePhotoAsync, ShareText: ShareText,
            AppVersion: InstalledVersion(), Haptic: Haptic);
        base.OnCreate();
    }

    /// <summary>The version name of the installed app, such as a preview's, which the assemblies do not carry.</summary>
    private string? InstalledVersion()
    {
        if (PackageManager is not { } packages || PackageName is not { } name) return null;
        if (OperatingSystem.IsAndroidVersionAtLeast(33)) return packages.GetPackageInfo(name, PackageManager.PackageInfoFlags.Of(0L)).VersionName;
#pragma warning disable CA1422 // Replaced by the overload above from Android 13.
        return packages.GetPackageInfo(name, 0)?.VersionName;
#pragma warning restore CA1422
    }

    /// <summary>
    /// What the system would call this device; see <see cref="MobileHost.AndroidDevice"/>. The window size does not
    /// count; a phone on a monitor in DeX stays a phone.
    /// </summary>
    private string Device()
    {
        // The system's own metrics are the built-in screen's, whatever window or monitor the app is on.
        var screen = global::Android.Content.Res.Resources.System?.DisplayMetrics;
        var shortest = screen is { Density: > 0 } ? Math.Min(screen.WidthPixels, screen.HeightPixels) / screen.Density : 0;
        return MobileHost.AndroidDevice(PackageManager?.HasSystemFeature("android.hardware.type.pc") == true,
            GetSystemService(TelephonyService) is global::Android.Telephony.TelephonyManager { IsVoiceCapable: true },
            PackageManager?.HasSystemFeature("android.hardware.sensor.hinge_angle") == true, shortest);
    }

    /// <summary>Offers a text, such as a day of the plan, to a messenger or mail app through the share sheet.</summary>
    private Task ShareText(string title, string text)
    {
        var send = new Intent(Intent.ActionSend).SetType("text/plain")!.PutExtra(Intent.ExtraText, text)!.PutExtra(Intent.ExtraSubject, title)!;
        StartActivity(Intent.CreateChooser(send, title)!.AddFlags(ActivityFlags.NewTask));
        return Task.CompletedTask;
    }

    /// <summary>The system's long-press feedback, which follows the user's touch-feedback setting.</summary>
    private static void Haptic() => MainActivity.Current?.Window?.DecorView.PerformHapticFeedback(global::Android.Views.FeedbackConstants.LongPress);

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
        // The starting screen stays until the app has drawn, at most three seconds, and then fades out a moment later:
        // Avalonia's drawing surface is black until its first picture reaches the screen, which would flash between
        // the two.
        var started = System.Diagnostics.Stopwatch.StartNew();
        var splash = AndroidX.Core.SplashScreen.SplashScreen.InstallSplashScreen(this);
        splash.SetKeepOnScreenCondition(new KeepStarting(() => !AppShell.Drawn && started.Elapsed < TimeSpan.FromSeconds(3)));
        splash.SetOnExitAnimationListener(new FadeOut());
        base.OnCreate(savedInstanceState);
        // Not again when Android rebuilds the activity, for example after the screen turned.
        if (savedInstanceState is null) Share(Intent);
    }

    private sealed class KeepStarting(Func<bool> keep) : Java.Lang.Object, AndroidX.Core.SplashScreen.SplashScreen.IKeepOnScreenCondition
    {
        public bool ShouldKeepOnScreen() => keep();
    }

    private sealed class FadeOut : Java.Lang.Object, AndroidX.Core.SplashScreen.SplashScreen.IOnExitAnimationListener
    {
        public void OnSplashScreenExit(AndroidX.Core.SplashScreen.SplashScreenViewProvider provider) =>
            provider.View.Animate()?.Alpha(0).SetStartDelay(250).SetDuration(150).WithEndAction(new Java.Lang.Runnable(provider.Remove))?.Start();
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

    private const int PhotoRequest = 4714;
    private static TaskCompletionSource<bool>? photo;

    /// <summary>
    /// Takes a photo with the phone's camera app, such as of a receipt, and makes it small enough for the trip: at
    /// most 1600 pixels on the longer side, upright, as JPEG. Null when cancelled or when there is no camera app.
    /// </summary>
    public static async Task<SharedFile?> TakePhotoAsync(CancellationToken ct)
    {
        if (Current is not { } activity) return null;
        var folder = Path.Combine(activity.FilesDir!.AbsolutePath, "photos"); Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "photo.jpg"); File.Delete(path);
        var uri = AndroidX.Core.Content.FileProvider.GetUriForFile(activity, activity.PackageName + ".files", new Java.IO.File(path));
        var capture = new Intent(global::Android.Provider.MediaStore.ActionImageCapture).PutExtra(global::Android.Provider.MediaStore.ExtraOutput, uri)!
            .AddFlags(ActivityFlags.GrantWriteUriPermission | ActivityFlags.GrantReadUriPermission)!;
        if (capture.ResolveActivity(activity.PackageManager!) is null) return null;
        photo = new TaskCompletionSource<bool>();
        activity.StartActivityForResult(capture, PhotoRequest);
        if (!await photo.Task || !File.Exists(path)) return null;
        try { return new SharedFile(DateTime.Now.ToString("yyyy-MM-dd HH-mm", System.Globalization.CultureInfo.InvariantCulture) + ".jpg", "image/jpeg", await Task.Run(() => Shrink(path), ct)); }
        finally { File.Delete(path); }
    }

    private static byte[] Shrink(string path)
    {
        var bounds = new global::Android.Graphics.BitmapFactory.Options { InJustDecodeBounds = true };
        global::Android.Graphics.BitmapFactory.DecodeFile(path, bounds);
        var sample = 1; while (Math.Max(bounds.OutWidth, bounds.OutHeight) / (sample * 2) >= 1600) sample *= 2;
        using var decoded = global::Android.Graphics.BitmapFactory.DecodeFile(path, new global::Android.Graphics.BitmapFactory.Options { InSampleSize = sample })!;
        var scale = Math.Min(1f, 1600f / Math.Max(decoded.Width, decoded.Height));
        // The camera notes how the phone was held; the picture is turned so it reads upright everywhere.
        var turn = new global::Android.Media.ExifInterface(path).GetAttributeInt(global::Android.Media.ExifInterface.TagOrientation, 1) switch { 6 => 90f, 3 => 180f, 8 => 270f, _ => 0f };
        using var matrix = new global::Android.Graphics.Matrix(); matrix.PostScale(scale, scale); matrix.PostRotate(turn);
        using var upright = global::Android.Graphics.Bitmap.CreateBitmap(decoded, 0, 0, decoded.Width, decoded.Height, matrix, true)!;
        using var output = new MemoryStream();
        upright.Compress(global::Android.Graphics.Bitmap.CompressFormat.Jpeg!, 80, output);
        return output.ToArray();
    }

    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        if (requestCode == PhotoRequest) photo?.TrySetResult(resultCode == Result.Ok);
    }

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
