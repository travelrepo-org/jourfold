using System.Runtime.InteropServices;
using Avalonia.iOS;
using Foundation;
using Jourfold.Mobile;
using ObjCRuntime;
using UIKit;

namespace Jourfold.iOS;

/// <summary>
/// Ticket shortcuts on the app icon, as on Android: a long press on Jourfold's icon lists the tickets of the next
/// journeys, and one opens that ticket. Avalonia's scene delegate neither answers quick actions nor passes on the one
/// that started the app, so Jourfold steps in at the Objective-C level: it adds
/// <c>windowScene:performActionForShortcutItem:completionHandler:</c> to that class and wraps its
/// <c>scene:willConnectToSession:options:</c>, whose options carry a quick action that started the app.
/// </summary>
internal static class IosShortcuts
{
    private const string TicketType = "dev.jourfold.app.ticket", TripKey = "trip", ItemKey = "item";
    private static IntPtr connect;

    /// <summary>The ticket shortcuts, the soonest first. The notices may run in the background; UIKit is asked on the main thread.</summary>
    public static void Set(IReadOnlyList<TicketShortcut> tickets) => NSThread.MainThread.InvokeOnMainThread(() =>
    {
        UIApplication.SharedApplication.ShortcutItems = tickets.Select(t => new UIApplicationShortcutItem(TicketType, t.Label, null,
            UIApplicationShortcutIcon.FromSystemImageName("ticket"),
            NSDictionary<NSString, NSObject>.FromObjectsAndKeys(
                [new NSString(Path.GetFileName(t.TripFolder)), new NSString(t.Item.ToString())],
                [new NSString(TripKey), new NSString(ItemKey)]))).ToArray();
    });

    /// <summary>
    /// Teaches Avalonia's scene delegate the quick actions. Called once while the app launches, before the scene
    /// connects. Without that class, as in another version of Avalonia, the app works as before, without shortcuts.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2026",
        Justification = "The scene delegate is registered with Objective-C and named in Avalonia's scene configuration, so trimming keeps it; if it is ever gone, the app runs without shortcuts.")]
    public static unsafe void Install()
    {
        if (connect != IntPtr.Zero || typeof(AvaloniaView).Assembly.GetType("Avalonia.iOS.AvaloniaSceneDelegate") is not { } scene) return;
        var type = Class.GetHandle(scene);
        if (type == IntPtr.Zero) return;
        delegate* unmanaged<IntPtr, IntPtr, IntPtr, IntPtr, IntPtr, void> perform = &Performed, connected = &Connect;
        class_addMethod(type, Selector.GetHandle("windowScene:performActionForShortcutItem:completionHandler:"), (IntPtr)perform, "v@:@@@?");
        var selector = Selector.GetHandle("scene:willConnectToSession:options:");
        connect = class_getMethodImplementation(type, selector);
        class_replaceMethod(type, selector, (IntPtr)connected, "v@:@@@");
    }

    /// <summary>Avalonia connects its window; then a quick action that started the app is answered.</summary>
    [UnmanagedCallersOnly]
    private static unsafe void Connect(IntPtr self, IntPtr selector, IntPtr scene, IntPtr session, IntPtr options)
    {
        ((delegate* unmanaged<IntPtr, IntPtr, IntPtr, IntPtr, IntPtr, void>)connect)(self, selector, scene, session, options);
        // An exception must not cross into Objective-C.
        try { if (Runtime.GetNSObject<UISceneConnectionOptions>(options)?.ShortcutItem is { } item) Perform(item); }
        catch (Exception ex) when (ex is ObjCException or InvalidCastException or ArgumentException) { }
    }

    /// <summary>A quick action while the app runs or was suspended.</summary>
    [UnmanagedCallersOnly]
    private static unsafe void Performed(IntPtr self, IntPtr selector, IntPtr scene, IntPtr item, IntPtr completion)
    {
        var handled = false;
        try { handled = Runtime.GetNSObject<UIApplicationShortcutItem>(item) is { } shortcut && Perform(shortcut); }
        catch (Exception ex) when (ex is ObjCException or InvalidCastException or ArgumentException) { }
        // The completion handler is a block: its function follows the class pointer, the flags and a reserved field.
        if (completion != IntPtr.Zero) ((delegate* unmanaged<IntPtr, byte, void>)Marshal.ReadIntPtr(completion, 16))(completion, handled ? (byte)1 : (byte)0);
    }

    private static bool Perform(UIApplicationShortcutItem item)
    {
        if (item.Type != TicketType || item.UserInfo is not { } info || MobileApp.Host is not { } host) return false;
        if (info[TripKey]?.ToString() is not { } trip || !Guid.TryParse(info[ItemKey]?.ToString(), out var id)) return false;
        // The app's folder moves with updates of the app; the trip is found by its folder's name.
        var folder = Path.Combine(host.TripsRoot, trip);
        AppDelegate.WhenReady(session => session.OpenNoticeAsync(folder, id, ticket: true));
        return true;
    }

    [DllImport("/usr/lib/libobjc.dylib")]
    private static extern IntPtr class_getMethodImplementation(IntPtr type, IntPtr selector);

    [DllImport("/usr/lib/libobjc.dylib")]
    private static extern IntPtr class_replaceMethod(IntPtr type, IntPtr selector, IntPtr implementation, string types);

    [DllImport("/usr/lib/libobjc.dylib")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool class_addMethod(IntPtr type, IntPtr selector, IntPtr implementation, string types);
}
