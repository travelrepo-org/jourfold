using CoreLocation;
using Foundation;

namespace Jourfold.iOS;

/// <summary>
/// The device's position for "Where I am" and the map's "My location", asked for only when the user taps one. The
/// permission is requested then, not at start; without it, or without a fix within 15 seconds, the answer is that the
/// position is unknown. Nothing is kept or sent; the position only fills the place's fields or shows on the map.
/// </summary>
internal static class IosLocation
{
    private static CLLocationManager? manager;
    private static readonly Listener listener = new();

    public static async Task<(double Latitude, double Longitude)?> LocateAsync(CancellationToken ct)
    {
        manager ??= new CLLocationManager { Delegate = listener, DesiredAccuracy = CLLocation.AccuracyHundredMeters };
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        if (manager.AuthorizationStatus == CLAuthorizationStatus.NotDetermined)
        {
            listener.Asking = new TaskCompletionSource();
            manager.RequestWhenInUseAuthorization();
            try { await listener.Asking.Task.WaitAsync(ct); }
            catch (OperationCanceledException) { return null; }
        }
        if (manager.AuthorizationStatus is not (CLAuthorizationStatus.AuthorizedWhenInUse or CLAuthorizationStatus.AuthorizedAlways)) return null;
        listener.Finding = new TaskCompletionSource<CLLocation?>();
        manager.RequestLocation();
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        try { return await listener.Finding.Task.WaitAsync(timeout.Token) is { } found ? (found.Coordinate.Latitude, found.Coordinate.Longitude) : null; }
        catch (OperationCanceledException) { manager.StopUpdatingLocation(); return null; }
    }

    private sealed class Listener : CLLocationManagerDelegate
    {
        public TaskCompletionSource? Asking { get; set; }
        public TaskCompletionSource<CLLocation?>? Finding { get; set; }

        // Also called once when the manager is made, before the user answered.
        public override void DidChangeAuthorization(CLLocationManager manager)
        {
            if (manager.AuthorizationStatus != CLAuthorizationStatus.NotDetermined) Asking?.TrySetResult();
        }

        public override void LocationsUpdated(CLLocationManager manager, CLLocation[] locations) => Finding?.TrySetResult(locations.LastOrDefault());
        public override void Failed(CLLocationManager manager, NSError error) => Finding?.TrySetResult(null);
    }
}
