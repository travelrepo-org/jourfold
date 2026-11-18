using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Foundation;
using Jourfold.Mobile;
using NodaTime;
using ObjCRuntime;

namespace Jourfold.iOS;

/// <summary>
/// The travel day as a Live Activity on the Lock Screen and in the Dynamic Island, the iOS counterpart of Android's
/// ongoing notification. ActivityKit is Swift only, so a small Swift bridge (LiveActivity/JourfoldLive.swift, built into
/// JourfoldLive.framework) starts, updates and ends it, and a widget extension draws it. What it shows comes from the
/// shared <see cref="LiveNotice"/>, as on Android; the countdown and the progress move on by themselves, because iOS
/// does not wake the app every minute.
/// </summary>
internal static class IosLiveActivities
{
    private static readonly Lazy<IntPtr> Bridge = new(() => Class.GetHandle("JourfoldLive"));

    /// <summary>Whether the system shows Live Activities here: not before iOS 16.2, on an iPad, or without the widget.</summary>
    public static LiveSupport Support => Bridge.Value == IntPtr.Zero ? LiveSupport.None : SendInt(Bridge.Value, Selector.GetHandle("support")) switch
    {
        2 => LiveSupport.On,
        1 => LiveSupport.Off,
        _ => LiveSupport.None
    };

    /// <summary>Whether an activity is shown right now.</summary>
    public static bool Showing => Bridge.Value != IntPtr.Zero && SendBool(Bridge.Value, Selector.GetHandle("isShowing"));

    /// <summary>
    /// Shows, updates or (with null) ends the travel day. Returns the part of the day the user swiped away, when that is
    /// the part asked for, so it stays away (<see cref="NoticeRunner.Dismiss"/>).
    /// </summary>
    public static string? Show(LiveNotice? live, string? tripFolder, Jourfold.UI.Localization strings)
    {
        if (Bridge.Value == IntPtr.Zero) return null;
        using var json = live is null || tripFolder is null ? null : new NSString(JsonSerializer.Serialize(Payload(live, tripFolder, strings), LiveJson.Default.LivePayload));
        var swiped = SendString(Bridge.Value, Selector.GetHandle("show:"), json?.Handle ?? IntPtr.Zero);
        return swiped == IntPtr.Zero ? null : Runtime.GetNSObject<NSString>(swiped)?.ToString();
    }

    /// <summary>What the activity shows, as the bridge reads it; times in seconds since 1970.</summary>
    internal static LivePayload Payload(LiveNotice live, string tripFolder, Jourfold.UI.Localization strings)
    {
        static double? Seconds(Instant? at) => at?.ToUnixTimeMilliseconds() / 1000.0;
        var trip = Uri.EscapeDataString(Path.GetFileName(tripFolder)); var item = live.Item.ToString();
        var open = $"{IosNotices.Scheme}://notice?trip={trip}&item={item}";
        return new LivePayload(item, Seconds(live.Until), new LiveState(
            live.Key, live.Title, live.Text, live.Chip, Seconds(live.CountdownFrom), Seconds(live.From), Seconds(live.Until), live.Icon,
            open, live.Ticket ? open + "&ticket=1" : null, live.Ticket ? strings["ShowTicket"] : null,
            strings["LiveTimeLeft"], strings["LiveProgress"]));
    }

    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern nint SendInt(IntPtr receiver, IntPtr selector);

    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool SendBool(IntPtr receiver, IntPtr selector);

    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern IntPtr SendString(IntPtr receiver, IntPtr selector, IntPtr argument);
}

internal sealed record LivePayload(
    [property: JsonPropertyName("item")] string Item,
    [property: JsonPropertyName("stale")] double? Stale,
    [property: JsonPropertyName("state")] LiveState State);

/// <summary>TravelDayAttributes.ContentState in LiveActivity/TravelDayAttributes.swift.</summary>
internal sealed record LiveState(
    [property: JsonPropertyName("key")] string Key,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("text")] string Text,
    [property: JsonPropertyName("chip")] string? Chip,
    [property: JsonPropertyName("countdownFrom")] double? CountdownFrom,
    [property: JsonPropertyName("from")] double? From,
    [property: JsonPropertyName("until")] double? Until,
    [property: JsonPropertyName("icon")] string Icon,
    [property: JsonPropertyName("url")] string Url,
    [property: JsonPropertyName("ticketUrl")] string? TicketUrl,
    [property: JsonPropertyName("ticketLabel")] string? TicketLabel,
    [property: JsonPropertyName("timeLeftLabel")] string TimeLeftLabel,
    [property: JsonPropertyName("progressLabel")] string ProgressLabel);

[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(LivePayload))]
internal sealed partial class LiveJson : JsonSerializerContext;
