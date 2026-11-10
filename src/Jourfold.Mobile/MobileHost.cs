using NodaTime;
using TravelRepo.Git;
using TravelRepo.Providers;

namespace Jourfold.Mobile;

/// <summary>
/// What the platform app (Android, iOS) provides: where trips live, how Git runs, and the clock and time zone of the
/// device. Tests and screenshots pass a fixed clock and zone.
/// </summary>
/// <param name="DataRoot">The app's private folder. Trips live in its <c>trips</c> folder.</param>
/// <param name="Git">The built-in Git on phones and tablets.</param>
/// <param name="Clock">The current time.</param>
/// <param name="PhoneZone">The device's time zone, read again each time because it changes when travelling.</param>
/// <param name="Language">"en" or "de".</param>
/// <param name="Secrets">The device's secure storage for the GitHub sign-in. Git reads it through <see cref="Jourfold.Application.GitHubCredentialBroker"/>.</param>
/// <param name="Http">How the app reaches GitHub; tests pass a fake.</param>
/// <param name="GitHubClientId">The GitHub App this build signs in with, if one is built in. Settings can name another.</param>
/// <param name="GitHubAppSlug">That App's name in its GitHub address, for the page where users choose its repositories.</param>
/// <param name="Device">
/// What the app runs on, as the system calls it: <c>phone</c>, <c>tablet</c> or <c>device</c>; texts such as "Only on
/// this phone" use it. Android decides with <see cref="AndroidDevice"/>, iOS by iPhone or iPad.
/// </param>
/// <param name="Locate">
/// The device's current position, asked for only when the user taps "Where I am" or the map's "My location"; it may ask
/// for the permission first.
/// Null when the device cannot tell or the user declines; the delegate itself is null where the platform has none.
/// </param>
/// <param name="TakePhoto">Takes a photo with the camera app, such as of a receipt, made small enough for the trip; null when cancelled.</param>
/// <param name="ShareText">Hands a text to another app, such as a messenger, through the system's share sheet: title, text.</param>
/// <param name="Brightness">Turns the screen to full brightness (true) or back to normal, for showing a ticket.</param>
/// <param name="Notices">The device's notifications; null where the platform has none.</param>
/// <param name="Calendar">The device's calendar app, where trips can appear; null where the platform has none.</param>
/// <param name="OpenFile">
/// Hands a file in <see cref="SharedRoot"/> to another app that can show it, such as a PDF viewer, with its media type.
/// False when no app can; null where the platform cannot do it.
/// </param>
public sealed record MobileHost(string DataRoot, IGitBackend Git, IClock Clock, Func<DateTimeZone> PhoneZone, string Language, ISecretStore Secrets, HttpMessageHandler? Http = null, string? GitHubClientId = null, Func<string, string, Task<bool>>? OpenFile = null, string? GitHubAppSlug = null, Func<CancellationToken, Task<(double Latitude, double Longitude)?>>? Locate = null, string Device = "phone", ISystemCalendar? Calendar = null, ISystemNotices? Notices = null, Action<bool>? Brightness = null, Func<CancellationToken, Task<SharedFile?>>? TakePhoto = null, Func<string, string, Task>? ShareText = null, string? AppVersion = null)
{
    /// <summary>Copies of files handed to other apps. Emptied each time, so they do not pile up.</summary>
    public string SharedRoot => Path.Combine(DataRoot, "shared");
    public string TripsRoot => Path.Combine(DataRoot, "trips");
    public string RecoveryRoot => Path.Combine(DataRoot, "recovery");

    /// <summary>
    /// What an Android device would call itself, for <see cref="Device"/>: a computer-like device such as a Chromebook,
    /// a phone when it can make calls and folds or has a phone-sized built-in screen, otherwise a tablet. Tablets with a
    /// SIM can often make calls too, which is why calls alone do not decide. The built-in screen counts, not the window,
    /// so a phone on a monitor in DeX stays a phone, and so does an unfolded foldable, as their makers call them.
    /// </summary>
    /// <param name="computer">The system says it is a PC, such as a Chromebook.</param>
    /// <param name="calls">The device can make voice calls.</param>
    /// <param name="folds">The device has a hinge.</param>
    /// <param name="shortestSide">The shorter side of the built-in screen in dp.</param>
    public static string AndroidDevice(bool computer, bool calls, bool folds, double shortestSide) =>
        computer ? "device" : calls && (folds || shortestSide < 600) ? "phone" : "tablet";
}
