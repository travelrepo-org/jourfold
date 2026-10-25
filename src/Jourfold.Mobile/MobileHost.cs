using NodaTime;
using TravelRepo.Git;

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
public sealed record MobileHost(string DataRoot, IGitBackend Git, IClock Clock, Func<DateTimeZone> PhoneZone, string Language)
{
    public string TripsRoot => Path.Combine(DataRoot, "trips");
    public string RecoveryRoot => Path.Combine(DataRoot, "recovery");
}
