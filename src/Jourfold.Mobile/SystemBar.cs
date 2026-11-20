using Avalonia.Media;

namespace Jourfold.Mobile;

/// <summary>A button of the system's bar: a place, or the action in the middle that adds to the trip.</summary>
/// <param name="Icon">The icon's Lucide name, as the shell's own bar uses; the platform picks its counterpart.</param>
/// <param name="Label">What the button is called, already in the app's language.</param>
/// <param name="Action">True for a button that does something instead of being a place, so it is never marked.</param>
/// <param name="Search">True for the search, which the system sets apart from the places, as iOS 26 does.</param>
public sealed record SystemBarItem(string Icon, string Label, bool Action = false, bool Search = false);

/// <summary>
/// The platform's own tab bar, drawn instead of the shell's bottom bar on phones, such as the one iOS gives its apps.
/// The shell decides which buttons it has and which is marked; the bar draws them, reports taps and leaves the rest
/// to the system, such as its material, haptics, the screen reader and the accessibility settings.
/// </summary>
public interface ISystemBar
{
    /// <summary>Shows the bar with these buttons and <paramref name="selected"/> marked, in the app's accent colour.</summary>
    void Show(IReadOnlyList<SystemBarItem> items, int selected, Color lightAccent, Color darkAccent);

    /// <summary>Takes the bar away, for the rail on wider screens, the trip list, the keyboard and sheets.</summary>
    void Hide();

    /// <summary>
    /// How far the bar reaches up from the bottom of the screen in device-independent pixels, the safe area included,
    /// so a page can scroll its last line above it. Zero while it is hidden.
    /// </summary>
    double Height { get; }

    /// <summary>A button was tapped: its number in the list last shown.</summary>
    event Action<int>? Tapped;

    /// <summary><see cref="Height"/> changed, such as once the bar is laid out or the screen turned.</summary>
    event Action? HeightChanged;

    /// <summary>The page scrolled: how far, how long it is and how much of it shows, so the bar can shrink while reading.</summary>
    void Scrolled(double offset, double extent, double viewport);

    /// <summary>
    /// Whether the bar has a place for the search of its own, as iOS 26 sets it apart beside the places. The page's top
    /// then has no search button.
    /// </summary>
    bool SearchTab => false;
}
