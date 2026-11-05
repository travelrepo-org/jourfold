using Android;
using Android.Content.PM;
using Android.Locations;
using Android.OS;
using Java.Util.Functions;

namespace Jourfold.Android;

/// <summary>
/// The phone's position for "Where I am" and the map's "My location", asked for only when the user taps one. The
/// permission is requested then, not at start; without it, or without a fix within a few seconds, the answer is that the
/// position is unknown. Nothing is kept or sent; the position only fills the place's fields or shows on the map.
/// </summary>
public static class AndroidLocation
{
    private const int RequestCode = 4711;
    private static TaskCompletionSource<bool>? asking;

    public static async Task<(double Latitude, double Longitude)?> LocateAsync(CancellationToken ct)
    {
        if (MainActivity.Current is not { } activity || !await AllowedAsync(activity)) return null;
        if (activity.GetSystemService(global::Android.Content.Context.LocationService) is not LocationManager manager) return null;
        var providers = manager.GetProviders(true);
        var provider = new[] { OperatingSystem.IsAndroidVersionAtLeast(31) ? LocationManager.FusedProvider : null, LocationManager.NetworkProvider, LocationManager.GpsProvider }
            .FirstOrDefault(p => p is not null && providers.Contains(p));
        if (provider is null) return null;
        Location? found = null;
        if (OperatingSystem.IsAndroidVersionAtLeast(30))
        {
            var answer = new TaskCompletionSource<Location?>();
            using var cancel = new CancellationSignal();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(15));
            using var _ = timeout.Token.Register(() => { cancel.Cancel(); answer.TrySetResult(null); });
            manager.GetCurrentLocation(provider, cancel, activity.MainExecutor!, new Receiver(answer));
            found = await answer.Task;
        }
        // Android 10 has no request for a single position; the last known one is recent enough for a place.
        found ??= manager.GetLastKnownLocation(provider);
        return found is null ? null : (found.Latitude, found.Longitude);
    }

    private static async Task<bool> AllowedAsync(MainActivity activity)
    {
        bool Granted(string permission) => activity.CheckSelfPermission(permission) == Permission.Granted;
        if (Granted(Manifest.Permission.AccessFineLocation) || Granted(Manifest.Permission.AccessCoarseLocation)) return true;
        asking = new TaskCompletionSource<bool>();
        activity.RequestPermissions([Manifest.Permission.AccessFineLocation, Manifest.Permission.AccessCoarseLocation], RequestCode);
        return await asking.Task;
    }

    /// <summary>The user's answer to the permission question.</summary>
    public static void Answered(int requestCode, Permission[] results)
    {
        if (requestCode == RequestCode) asking?.TrySetResult(results.Any(r => r == Permission.Granted));
    }

    private sealed class Receiver(TaskCompletionSource<Location?> answer) : Java.Lang.Object, IConsumer
    {
        public void Accept(Java.Lang.Object? value) => answer.TrySetResult(value as Location);
    }
}
