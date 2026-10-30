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
            Locate: AndroidLocation.LocateAsync);
        base.OnCreate();
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

[Activity(Label = "Jourfold", Theme = "@style/Theme.Jourfold.Starting", MainLauncher = true, Exported = true,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public sealed class MainActivity : AvaloniaMainActivity
{
    /// <summary>The activity on screen, for questions such as the location permission.</summary>
    public static MainActivity? Current { get; private set; }

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        Current = this;
        AndroidX.Core.SplashScreen.SplashScreen.InstallSplashScreen(this);
        base.OnCreate(savedInstanceState);
    }

    protected override void OnDestroy() { if (Current == this) Current = null; base.OnDestroy(); }

    public override void OnRequestPermissionsResult(int requestCode, string[] permissions, Permission[] grantResults)
    {
        base.OnRequestPermissionsResult(requestCode, permissions, grantResults);
        AndroidLocation.Answered(requestCode, grantResults);
    }

    /// <summary>Leaving the app turns open changes into a version right away; Android may stop the app at any time after.</summary>
    protected override void OnPause()
    {
        base.OnPause();
        if (MobileApp.Session is { } session) _ = session.AutoSaveAsync();
    }
}
