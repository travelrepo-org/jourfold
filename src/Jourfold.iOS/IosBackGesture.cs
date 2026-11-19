using CoreGraphics;
using Jourfold.Mobile;
using UIKit;

namespace Jourfold.iOS;

/// <summary>
/// Back with a swipe from the left edge of the screen, as iOS apps go back. iOS has no back button or gesture of the
/// system, as Android has; Jourfold's own back buttons stay, and this does what Android's back does
/// (<see cref="AppShell.GoBack"/>): a sheet or menu closes, details go back, a page goes back to where it was opened
/// from, then to Today and to the trip list. Only a swipe that starts at the edge and goes right counts, so the days
/// still swipe sideways; once it is recognised, the touch is taken from the app, so the day does not move as well.
/// </summary>
/// <remarks>
/// A pan that starts at the edge rather than <see cref="UIScreenEdgePanGestureRecognizer"/>: the same on a device, and
/// it also answers the simulator's touches, which carry no edge, so it can be tried there.
/// </remarks>
internal static class IosBackGesture
{
    /// <summary>How far from the left edge a swipe may start, in points.</summary>
    private const double Edge = 24;
    private static bool attached;

    /// <summary>
    /// Adds the gesture to the app's window. Called once the app is ready and again whenever it comes back on screen,
    /// in case its window was not there yet.
    /// </summary>
    public static void Attach()
    {
        if (attached || UIApplication.SharedApplication.ConnectedScenes.OfType<UIWindowScene>().SelectMany(s => s.Windows).FirstOrDefault() is not { } window) return;
        attached = true;
        var swipe = new UIPanGestureRecognizer(Swiped) { MaximumNumberOfTouches = 1 };
        // Where the finger went down; the pan itself only begins after it has moved a little.
        swipe.ShouldReceiveTouch = (recognizer, touch) => { start = touch.LocationInView(recognizer.View).X; return true; };
        swipe.ShouldBegin = Starts;
        window.AddGestureRecognizer(swipe);
    }

    private static nfloat start;

    /// <summary>A swipe to the right that started at the left edge.</summary>
    private static bool Starts(UIGestureRecognizer recognizer)
    {
        if (recognizer is not UIPanGestureRecognizer swipe || swipe.View is not { } view) return false;
        var speed = swipe.VelocityInView(view);
        // On the map a drag from the edge pans the map (AppShell.EdgeSwipeGoesBack).
        return start <= Edge + view.SafeAreaInsets.Left && speed.X > Math.Abs(speed.Y) && AppShell.EdgeSwipeGoesBack;
    }

    private static void Swiped(UIPanGestureRecognizer swipe)
    {
        if (swipe.State != UIGestureRecognizerState.Ended || swipe.View is not { } view) return;
        // A third of the way across the screen, or a quick flick, as in iOS's own navigation.
        CGPoint moved = swipe.TranslationInView(view), speed = swipe.VelocityInView(view);
        if (moved.X > view.Bounds.Width / 3 || (moved.X > 30 && speed.X > 500)) Avalonia.Threading.Dispatcher.UIThread.Post(AppShell.GoBack);
    }
}
