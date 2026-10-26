using Avalonia.Input;
namespace Jourfold.Desktop;

/// <summary>
/// Keyboard shortcuts follow the platform: Ctrl on Windows and Linux, Cmd on macOS. Shortcuts are written once with
/// Ctrl and adapted here, so key bindings, key handlers and the labels in menus and tooltips agree.
/// </summary>
public static class Shortcuts
{
    /// <summary>The modifier of command shortcuts such as undo or quick add.</summary>
    public static KeyModifiers Command => OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;

    /// <summary>
    /// A shortcut written as "Ctrl+Shift+Z" the way the platform shows it: unchanged on Windows and Linux, "⇧⌘Z" on
    /// macOS, with the modifiers in Apple's order.
    /// </summary>
    public static string Label(string gesture) => Label(gesture, OperatingSystem.IsMacOS());

    /// <summary>The label of a shortcut on macOS or elsewhere, whatever the system the app runs on.</summary>
    public static string Label(string gesture, bool mac)
    {
        if (!mac) return gesture;
        var parts = gesture.Split('+');
        var key = parts[^1].Length == 0 ? "+" : parts[^1];
        string Has(string name, string symbol) => parts[..^1].Contains(name, StringComparer.OrdinalIgnoreCase) ? symbol : "";
        return Has("Alt", "⌥") + Has("Shift", "⇧") + Has("Ctrl", "⌘") + key;
    }

    /// <summary>On macOS, moves an element's Ctrl key bindings to Cmd.</summary>
    public static void Adapt(InputElement element)
    {
        if (!OperatingSystem.IsMacOS()) return;
        foreach (var binding in element.KeyBindings)
            if (binding.Gesture is { } gesture && gesture.KeyModifiers.HasFlag(KeyModifiers.Control))
                binding.Gesture = new KeyGesture(gesture.Key, (gesture.KeyModifiers & ~KeyModifiers.Control) | KeyModifiers.Meta);
    }
}
