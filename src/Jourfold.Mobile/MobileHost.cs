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
/// <param name="OpenFile">
/// Hands a file in <see cref="SharedRoot"/> to another app that can show it, such as a PDF viewer, with its media type.
/// False when no app can; null where the platform cannot do it.
/// </param>
public sealed record MobileHost(string DataRoot, IGitBackend Git, IClock Clock, Func<DateTimeZone> PhoneZone, string Language, ISecretStore Secrets, HttpMessageHandler? Http = null, string? GitHubClientId = null, Func<string, string, Task<bool>>? OpenFile = null, string? GitHubAppSlug = null)
{
    /// <summary>Copies of files handed to other apps. Emptied each time, so they do not pile up.</summary>
    public string SharedRoot => Path.Combine(DataRoot, "shared");
    public string TripsRoot => Path.Combine(DataRoot, "trips");
    public string RecoveryRoot => Path.Combine(DataRoot, "recovery");
}
