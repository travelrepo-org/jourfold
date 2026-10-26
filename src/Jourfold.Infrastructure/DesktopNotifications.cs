using System.Runtime.InteropServices;
using Tmds.DBus.Protocol;

namespace Jourfold.Infrastructure;

/// <summary>
/// Shows a notification of the operating system while Jourfold runs: through the desktop's notification service
/// (org.freedesktop.Notifications) on Linux, and as a notification area message on Windows. Failures are silent,
/// because a missing notification service must never interrupt planning.
/// </summary>
public static class DesktopNotifications
{
    /// <param name="window">The native window handle on Windows; ignored elsewhere.</param>
    public static async Task<bool> ShowAsync(string title, string body, IntPtr window = default)
    {
        try
        {
            if (OperatingSystem.IsLinux() || OperatingSystem.IsFreeBSD()) return await FreedesktopAsync(title, body).ConfigureAwait(false);
            if (OperatingSystem.IsWindows()) return Windows(title, body, window);
        }
        catch (Exception ex) when (ex is DBusExceptionBase or IOException or InvalidOperationException or ExternalException or ObjectDisposedException or TimeoutException) { }
        return false;
    }

    private static async Task<bool> FreedesktopAsync(string title, string body)
    {
        if (string.IsNullOrEmpty(DBusAddress.Session)) return false;
        var connection = DBusConnection.Session;
        await connection.CallMethodAsync(NotifyMessage(connection, title, body)).WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        return true;
    }

    // The writer is a ref struct, so the message is built outside the async method.
    private static MessageBuffer NotifyMessage(DBusConnection connection, string title, string body)
    {
        using var writer = connection.GetMessageWriter();
        writer.WriteMethodCallHeader(destination: "org.freedesktop.Notifications", path: "/org/freedesktop/Notifications", @interface: "org.freedesktop.Notifications", member: "Notify", signature: "susssasa{sv}i");
        writer.WriteString("Jourfold");
        writer.WriteUInt32(0);
        writer.WriteString("jourfold");
        writer.WriteString(title);
        writer.WriteString(body);
        writer.WriteArray(Array.Empty<string>());
        var hints = writer.WriteDictionaryStart(); writer.WriteDictionaryEnd(hints);
        writer.WriteInt32(-1);
        return writer.CreateMessage();
    }
    private static bool Windows(string title, string body, IntPtr window)
    {
        if (window == IntPtr.Zero) return false;
        var data = new NotifyIconData
        {
            Size = Marshal.SizeOf<NotifyIconData>(),
            Window = window,
            Id = 0x4A46,
            Flags = IconFlag | TipFlag | InfoFlag,
            Icon = LoadIcon(IntPtr.Zero, (IntPtr)32512),
            Tip = "Jourfold",
            InfoTitle = Truncate(title, 63),
            Info = Truncate(body, 255),
            InfoFlags = 1
        };
        Shell_NotifyIcon(Delete, ref data);
        if (!Shell_NotifyIcon(Add, ref data)) return false;
        // The message stays in the notification centre; the temporary tray icon is removed after a while.
        _ = Task.Delay(TimeSpan.FromSeconds(15)).ContinueWith(_ => Shell_NotifyIcon(Delete, ref data), TaskScheduler.Default);
        return true;
    }

    private static string Truncate(string text, int length) => text.Length <= length ? text : text[..(length - 1)] + "…";

    private const uint Add = 0, Delete = 2, IconFlag = 0x2, TipFlag = 0x4, InfoFlag = 0x10;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public int Size;
        public IntPtr Window;
        public uint Id;
        public uint Flags;
        public uint CallbackMessage;
        public IntPtr Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Tip;
        public uint State;
        public uint StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Info;
        public uint Version;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string InfoTitle;
        public uint InfoFlags;
        public Guid Item;
        public IntPtr BalloonIcon;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)][return: MarshalAs(UnmanagedType.Bool)] private static extern bool Shell_NotifyIcon(uint message, ref NotifyIconData data);
    [DllImport("user32.dll")] private static extern IntPtr LoadIcon(IntPtr instance, IntPtr name);
}
