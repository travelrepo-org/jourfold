using CoreGraphics;
using Jourfold.Mobile;
using UIKit;

namespace Jourfold.iOS;

/// <summary>
/// The bottom bar on iPhones as iOS's own tab bar, which iOS 26 draws as floating Liquid Glass that shrinks while the
/// page scrolls down. The material, its highlight for the chosen place, VoiceOver, Larger Text and the settings Reduce
/// Transparency, Increase Contrast and Reduce Motion come from the system.
/// </summary>
/// <remarks>
/// The bar belongs to a <see cref="UITabBarController"/> laid over Avalonia's view in the same window. Its pages are
/// empty and let every touch through to Avalonia, which draws the page under the bar; only the bar itself takes
/// touches. Each empty page has an invisible scroll view that follows the Avalonia page, since that is what the tab bar
/// watches to shrink. The shell decides what the bar shows and which place is marked; a tap only goes back to it.
/// </remarks>
internal sealed class IosTabBar : ISystemBar
{
    private readonly UITabBarController controller = new();
    private Overlay? overlay;
    private UIView? host;
    private UIPanGestureRecognizer? following;
    private IReadOnlyList<SystemBarItem> shown = [];
    private (UIColor Accent, uint Light, uint Dark)? accent;
    private double height;
    private bool? atTop;
    // Where the Avalonia page is: how far scrolled, how long, how much shows.
    private (double Offset, double Extent, double Viewport) page;

    public event Action<int>? Tapped;
    public event Action? HeightChanged;

    public IosTabBar()
    {
        controller.Delegate = new Choice(this);
        // The bar always sits at the bottom, also on an iPad, whose own tab bar would move to the top. The shell shows
        // it only below 600 dp; wider windows have the rail.
        if (OperatingSystem.IsIOSVersionAtLeast(17) && UIDevice.CurrentDevice.UserInterfaceIdiom == UIUserInterfaceIdiom.Pad) controller.TraitOverrides.HorizontalSizeClass = UIUserInterfaceSizeClass.Compact;
        if (OperatingSystem.IsIOSVersionAtLeast(26)) controller.TabBarMinimizeBehavior = UITabBarMinimizeBehavior.OnScrollDown;
    }

    public double Height => overlay is { Hidden: false } ? height : 0;

    public void Show(IReadOnlyList<SystemBarItem> items, int selected, Avalonia.Media.Color lightAccent, Avalonia.Media.Color darkAccent)
    {
        if (!Install()) return;
        Tint(lightAccent, darkAccent);
        if (!items.SequenceEqual(shown))
        {
            shown = items;
            // From iOS 18 as tabs, which iOS 26 shrinks while scrolling; before that as tab bar items.
            if (OperatingSystem.IsIOSVersionAtLeast(18)) controller.SetTabs(Tabs(items), false);
            else controller.SetViewControllers(items.Select((item, i) => (UIViewController)new Page(this) { TabBarItem = new UITabBarItem(item.Label, Image(item).Normal, Image(item).Chosen) { Tag = i } }).ToArray(), false);
        }
        if (OperatingSystem.IsIOSVersionAtLeast(18)) { if (selected >= 0 && selected < controller.Tabs.Length && controller.SelectedTab != controller.Tabs[selected]) controller.SelectedTab = controller.Tabs[selected]; }
        else if (selected >= 0 && selected < items.Count && controller.SelectedIndex != selected) controller.SelectedIndex = selected;
        Follow();
        if (overlay!.Hidden) { overlay.Hidden = false; Measured(); }
    }

    [System.Runtime.Versioning.SupportedOSPlatform("ios18.0")]
    private UITab[] Tabs(IReadOnlyList<SystemBarItem> items) =>
        items.Select((item, i) => item.Search ? new UISearchTab(_ => new Page(this)) { Title = item.Label } : new UITab(item.Label, Image(item).Normal, "place" + i, _ => new Page(this))).ToArray();

    /// <summary>From iOS 26 the search is a place of the bar, set apart at its end.</summary>
    public bool SearchTab => OperatingSystem.IsIOSVersionAtLeast(26);

    /// <summary>
    /// Lets the marked page's invisible scroll view feel the finger on Avalonia's view, as its own drag: the tab bar
    /// shrinks only when the user scrolls, not when a page is scrolled for them. Avalonia still gets every touch.
    /// </summary>
    private void Follow()
    {
        if (host is null || controller.SelectedViewController is not Page page) return;
        _ = page.View;
        var pan = page.Proxy.PanGestureRecognizer;
        if (pan == following) return;
        if (following is not null) host.RemoveGestureRecognizer(following);
        pan.CancelsTouchesInView = false; pan.DelaysTouchesBegan = false; pan.DelaysTouchesEnded = false;
        // When the finger lets go, the scroll view takes the page's place again: a swipe the page did not follow, such
        // as one along a row of days, must not leave the bar shrunk over a page at its top.
        pan.AddTarget(() => { if (pan.State is UIGestureRecognizerState.Ended or UIGestureRecognizerState.Cancelled or UIGestureRecognizerState.Failed) Settle(); });
        host.AddGestureRecognizer(pan);
        following = pan;
    }

    public void Hide()
    {
        if (overlay is null || overlay.Hidden) return;
        overlay.Hidden = true; HeightChanged?.Invoke();
    }

    public void Scrolled(double offset, double extent, double viewport)
    {
        page = (offset, extent, viewport);
        if (controller.SelectedViewController is not Page { IsViewLoaded: true } shownPage) return;
        var proxy = shownPage.Proxy;
        // As long as the page, so the finger moves both alike; while the finger is on it, the scroll view follows it.
        var inset = proxy.AdjustedContentInset;
        proxy.ContentSize = new CGSize(proxy.Bounds.Width, Math.Max(0, extent - viewport + proxy.Bounds.Height - inset.Top - inset.Bottom));
        // Back at the top, the bar opens again, as it does when the page is scrolled back up.
        if (offset <= 0.5 && atTop is false) Open();
        atTop = offset <= 0.5;
        if (!proxy.Dragging && !proxy.Decelerating) Settle();
    }

    /// <summary>Puts the scroll view where the page is, and opens the bar when the page is at its top.</summary>
    private void Settle()
    {
        if (controller.SelectedViewController is not Page { IsViewLoaded: true } shownPage) return;
        var proxy = shownPage.Proxy; var (offset, extent, viewport) = page;
        proxy.SetContentOffset(new CGPoint(0, -proxy.AdjustedContentInset.Top + Math.Clamp(offset, 0, Math.Max(0, extent - viewport))), false);
        if (offset <= 0.5) Open();
    }

    /// <summary>Opens the shrunken bar. iOS has no call for it; choosing the behaviour again does it.</summary>
    private void Open()
    {
        if (!OperatingSystem.IsIOSVersionAtLeast(26)) return;
        controller.TabBarMinimizeBehavior = UITabBarMinimizeBehavior.Never;
        controller.TabBarMinimizeBehavior = UITabBarMinimizeBehavior.OnScrollDown;
    }

    /// <summary>Lays the bar over Avalonia's view the first time it is needed, once the window is there.</summary>
    private bool Install()
    {
        if (overlay is not null) return true;
        var window = UIApplication.SharedApplication.ConnectedScenes.OfType<UIWindowScene>().SelectMany(s => s.Windows)
            .OrderBy(w => w.IsKeyWindow ? 0 : 1).FirstOrDefault(w => w.RootViewController is not null);
        if (window?.RootViewController is not { View: { } host } root) return false;
        this.host = host;
        // Inside Avalonia's view, so sheets the app presents, such as sharing or the camera, still cover the bar.
        overlay = new Overlay(controller) { Frame = host.Bounds, AutoresizingMask = UIViewAutoresizing.FlexibleDimensions, BackgroundColor = UIColor.Clear, Hidden = true };
        root.AddChildViewController(controller);
        controller.View!.Frame = overlay.Bounds;
        controller.View.AutoresizingMask = UIViewAutoresizing.FlexibleDimensions;
        controller.View.BackgroundColor = UIColor.Clear;
        overlay.AddSubview(controller.View);
        host.AddSubview(overlay);
        controller.DidMoveToParentViewController(root);
        return true;
    }

    /// <summary>The marked place in the app's accent, Jourfold's teal, in its light and dark shade.</summary>
    private void Tint(Avalonia.Media.Color light, Avalonia.Media.Color dark)
    {
        if (accent is { } known && known.Light == light.ToUInt32() && known.Dark == dark.ToUInt32()) return;
        static UIColor Of(Avalonia.Media.Color c) => UIColor.FromRGBA(c.R, c.G, c.B, c.A);
        var color = UIColor.FromDynamicProvider(traits => traits.UserInterfaceStyle == UIUserInterfaceStyle.Dark ? Of(dark) : Of(light));
        accent = (color, light.ToUInt32(), dark.ToUInt32());
        controller.TabBar.TintColor = color;
        shown = [];
    }

    /// <summary>
    /// The button's image. Adding is not a place: a filled circle in the accent, as the plus is in the shell's own bar.
    /// </summary>
    private (UIImage? Normal, UIImage? Chosen) Image(SystemBarItem item)
    {
        if (!item.Action) return Symbol(item.Icon);
        var plus = UIImage.GetSystemImage("plus.circle.fill", UIImageSymbolConfiguration.Create(UIImageSymbolScale.Large))
            ?.ApplyTintColor(accent?.Accent ?? UIColor.SystemBlue, UIImageRenderingMode.AlwaysOriginal);
        return (plus, plus);
    }

    /// <summary>The SF Symbol for an icon of the shell's bar, and its filled form for the marked place where there is one.</summary>
    private static (UIImage? Normal, UIImage? Chosen) Symbol(string icon)
    {
        var names = icon switch
        {
            "calendar" => new[] { "calendar" },
            "list" => ["list.bullet"],
            "map" => ["map"],
            "wallet" => ["wallet.bifold", "creditcard"],
            "ticket" => ["ticket"],
            "square-check" => ["checkmark.square"],
            "backpack" => ["backpack", "bag"],
            "ellipsis" => ["ellipsis"],
            "search" => ["magnifyingglass"],
            _ => ["circle"]
        };
        var normal = names.Select(n => (Name: n, Image: UIImage.GetSystemImage(n))).FirstOrDefault(x => x.Image is not null);
        var filled = normal.Name is { } name ? UIImage.GetSystemImage(name + ".fill") : null;
        return (normal.Image, filled ?? normal.Image);
    }

    /// <summary>Reads again how much of the screen the bar covers, which the empty page knows as its safe area.</summary>
    private void Measured()
    {
        if (controller.SelectedViewController is not { IsViewLoaded: true, View: { } view }) return;
        // The shrunken bar covers less, but the page keeps the room of the full one, so its end does not jump while
        // scrolling.
        var now = Math.Max((double)view.SafeAreaInsets.Bottom, height);
        if (Math.Abs(now - height) < 0.5) return;
        height = now; HeightChanged?.Invoke();
    }

    /// <summary>
    /// Whether a tap goes to the place: the shell is told and decides what opens, while the bar marks it right away. The
    /// plus is no place, so the marked place stays.
    /// </summary>
    private sealed class Choice(IosTabBar bar) : UITabBarControllerDelegate
    {
        public override bool ShouldSelectViewController(UITabBarController tabBarController, UIViewController viewController)
        {
            var index = Array.IndexOf(tabBarController.ViewControllers ?? [], viewController);
            if (index < 0) return false;
            bar.Tapped?.Invoke(index);
            return !bar.shown[index].Action;
        }

        [System.Runtime.Versioning.SupportedOSPlatform("ios18.0")]
        public override bool ShouldSelectTab(UITabBarController tabBarController, UITab tab)
        {
            var index = Array.IndexOf(tabBarController.Tabs, tab);
            if (index < 0) return false;
            bar.Tapped?.Invoke(index);
            return !bar.shown[index].Action;
        }
    }

    /// <summary>An empty page of the tab bar. Avalonia draws the real one beneath it.</summary>
    private sealed class Page(IosTabBar bar) : UIViewController
    {
        /// <summary>
        /// The page itself: an empty, clear scroll view that follows the Avalonia page's scrolling, so the tab bar
        /// shrinks and grows with it. Nothing of it shows, and Avalonia keeps the touches.
        /// </summary>
        public UIScrollView Proxy { get; } = new Follower()
        {
            BackgroundColor = UIColor.Clear, ScrollsToTop = false,
            ShowsVerticalScrollIndicator = false, ShowsHorizontalScrollIndicator = false, AccessibilityElementsHidden = true
        };

        public override void LoadView()
        {
            View = Proxy;
            SetContentScrollView(Proxy, NSDirectionalRectEdge.All);
        }

        public override void ViewSafeAreaInsetsDidChange()
        {
            base.ViewSafeAreaInsetsDidChange();
            bar.Measured();
        }
    }

    /// <summary>
    /// The empty page's scroll view. Its drag listens on Avalonia's view beside every other gesture there, such as the
    /// swipe from the edge that goes back, and never keeps one of them from starting.
    /// </summary>
    private sealed class Follower : UIScrollView
    {
        [Foundation.Export("gestureRecognizer:shouldRecognizeSimultaneouslyWithGestureRecognizer:")]
        public bool RecognizeTogether(UIGestureRecognizer gesture, UIGestureRecognizer other) => true;
    }

    /// <summary>Holds the tab bar's controller over Avalonia's view and lets every touch beside the bar through to it.</summary>
    private sealed class Overlay(UITabBarController controller) : UIView
    {
        public override UIView? HitTest(CGPoint point, UIEvent? uievent)
        {
            var hit = base.HitTest(point, uievent);
            for (var view = hit; view is not null; view = view.Superview)
                if (view == controller.TabBar) return hit;
            return null;
        }
    }
}
