using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Runtime;
using Avalonia.Android;
using Jourfold.Mobile;
using NodaTime;
using TravelRepo.Git.LibGit2;

namespace Jourfold.Android;

/// <summary>Starts Avalonia with the shared mobile app and hands it this phone's folders, Git, clock and time zone.</summary>
[Application(Label = "Jourfold", Icon = "@mipmap/ic_launcher", RoundIcon = "@mipmap/ic_launcher")]
public sealed class JourfoldApplication : AvaloniaAndroidApplication<MobileApp>
{
    private JourfoldApplication(IntPtr handle, JniHandleOwnership ownership) : base(handle, ownership) { }

    public override void OnCreate()
    {
        var language = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "de" ? "de" : "en";
        MobileApp.Host = new MobileHost(FilesDir!.AbsolutePath, new LibGit2Backend(), NodaTime.SystemClock.Instance, () => DateTimeZoneProviders.Tzdb.GetSystemDefault(), language);
        base.OnCreate();
    }
}

[Activity(Label = "Jourfold", Theme = "@style/Theme.Jourfold.Starting", MainLauncher = true, Exported = true,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public sealed class MainActivity : AvaloniaMainActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        AndroidX.Core.SplashScreen.SplashScreen.InstallSplashScreen(this);
        base.OnCreate(savedInstanceState);
    }
}
