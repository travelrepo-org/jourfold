using Avalonia.Input;
namespace Jourfold.Desktop;

/// <summary>
/// Keyboard shortcuts follow the platform: Ctrl on Windows and Linux, Cmd on macOS. Shortcuts are written once with
/// Ctrl and adapted here, so key bindings, key handlers and the labels in menus and tooltips agree.
/// </summary>
public static class Shortcuts
{
    /// <summary>
    /// Whether shortcuts follow macOS. True on a Mac; headless tests set it to false, because headless rendering has
    /// no system fonts to draw ⌘ with.
    /// </summary>
    public static bool Mac { get; set; } = OperatingSystem.IsMacOS();

    /// <summary>The modifier of command shortcuts such as undo or quick add.</summary>
    public static KeyModifiers Command => Mac ? KeyModifiers.Meta : KeyModifiers.Control;

    /// <summary>
    /// A shortcut written as "Ctrl+Shift+Z" the way the platform shows it: unchanged on Windows and Linux, "⇧⌘Z" on
    /// macOS, with the modifiers in Apple's order.
    /// </summary>
    public static string Label(string gesture) => Label(gesture, Mac);

    /// <summary>The label of a shortcut on macOS or elsewhere, whatever the system the app runs on.</summary>
    public static string Label(string gesture, bool mac)
    {
        if (!mac) return gesture;
        var parts = gesture.Split('+');
        var key = parts[^1].Length == 0 ? "+" : parts[^1];
        string Has(string name, string symbol) => parts[..^1].Contains(name, StringComparer.OrdinalIgnoreCase) ? symbol : "";
        return Has("Alt", "⌥") + Has("Shift", "⇧") + Has("Ctrl", "⌘") + key;
    }

    /// <summary>
    /// The key gesture of a shortcut written as "Ctrl+Shift+Z", for menus and key bindings. Takes that spelling and not
    /// a <see cref="Label(string)"/>, which is for reading only: KeyGesture cannot parse "⌘Z".
    /// </summary>
    public static KeyGesture Gesture(string gesture) => Gesture(gesture, Mac);

    /// <summary>The key gesture of a shortcut on macOS or elsewhere, whatever the system the app runs on.</summary>
    public static KeyGesture Gesture(string gesture, bool mac) => mac ? ToCommand(KeyGesture.Parse(gesture)) : KeyGesture.Parse(gesture);

    /// <summary>On macOS, moves an element's Ctrl key bindings to Cmd.</summary>
    public static void Adapt(InputElement element)
    {
        if (!Mac) return;
        foreach (var binding in element.KeyBindings)
            if (binding.Gesture is { } gesture) binding.Gesture = ToCommand(gesture);
    }

    private static KeyGesture ToCommand(KeyGesture gesture) => gesture.KeyModifiers.HasFlag(KeyModifiers.Control)
        ? new KeyGesture(gesture.Key, (gesture.KeyModifiers & ~KeyModifiers.Control) | KeyModifiers.Meta)
        : gesture;
}
