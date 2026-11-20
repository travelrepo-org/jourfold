using Avalonia.Controls;

namespace Jourfold.Mobile;

/// <summary>
/// The text size the user chose for the whole device, such as Larger Text on iOS. The theme's sizes are resources
/// (<c>Size.Body</c>, <c>Size.Caption</c>, ...) that follow it, and sizes set in code go through <see cref="Size"/>.
/// Only text grows: spacing, icons and touch targets keep their size, and rows grow with their text.
/// </summary>
public static class TextScale
{
    /// <summary>
    /// The largest factor. Beyond it fixed parts such as the date strip and the timetable's hour rows no longer fit
    /// their text; the largest accessibility sizes on iOS reach about 3.
    /// </summary>
    public const double Largest = 1.65;

    /// <summary>The smallest factor, for the smallest text sizes, which still keep captions readable.</summary>
    public const double Smallest = 0.85;

    /// <summary>The theme's text sizes at the standard setting, by resource key.</summary>
    public static IReadOnlyList<(string Key, double Size)> Sizes { get; } =
    [
        ("Size.Body", 15), ("Size.H1", 24), ("Size.H2", 20), ("Size.H3", 17), ("Size.Title", 16), ("Size.Caption", 13),
        ("Size.Small", 12), ("Size.Time", 14), ("Size.Chip", 14), ("Size.Tab", 12),
        // Fluent's own controls, such as text fields and the date and time pickers.
        ("ControlContentThemeFontSize", 15)
    ];

    /// <summary>How much larger text is than at the standard setting: 1 there.</summary>
    public static double Factor { get; private set; } = 1;

    /// <summary>A text size from code, at the chosen setting.</summary>
    public static double Size(double size) => Math.Round(size * Factor, 1);

    /// <summary>
    /// Sets the factor and the theme's sizes in <paramref name="resources"/>. Pages drawn from code read
    /// <see cref="Size"/> when they are drawn next.
    /// </summary>
    public static void Apply(IResourceDictionary resources, double factor)
    {
        Factor = double.IsFinite(factor) ? Math.Clamp(factor, Smallest, Largest) : 1;
        foreach (var (key, size) in Sizes) resources[key] = Size(size);
    }

    /// <summary>
    /// The factor for iOS's text size (<c>UIContentSizeCategory</c>), from the size of body text Apple gives each
    /// setting: 17 points at Large, the standard.
    /// </summary>
    public static double FromIosCategory(string? category) => (category switch
    {
        "UICTContentSizeCategoryXS" => 14,
        "UICTContentSizeCategoryS" => 15,
        "UICTContentSizeCategoryM" => 16,
        "UICTContentSizeCategoryXL" => 19,
        "UICTContentSizeCategoryXXL" => 21,
        "UICTContentSizeCategoryXXXL" => 23,
        "UICTContentSizeCategoryAccessibilityM" => 28,
        "UICTContentSizeCategoryAccessibilityL" => 33,
        "UICTContentSizeCategoryAccessibilityXL" => 40,
        "UICTContentSizeCategoryAccessibilityXXL" => 47,
        "UICTContentSizeCategoryAccessibilityXXXL" => 53,
        _ => 17
    }) / 17.0;
}
