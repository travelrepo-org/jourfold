using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Jourfold.Mobile.Controls;

/// <summary>
/// Where a page that was just built should be scrolled to, such as back to where it was before it was drawn again.
/// The shell lays the new page out and scrolls it before anything is drawn (<see cref="Settle"/>). Setting the place
/// later, once the page had loaded, showed its top for a frame or two each time it was rebuilt, as on the desktop
/// before the same change there.
/// </summary>
public static class ScrollPlace
{
    private static readonly ConditionalWeakTable<ScrollViewer, Action> Waiting = new();

    /// <summary>
    /// Runs <paramref name="place"/> once <paramref name="scroll"/> has its size: when the shell settles the page, or
    /// otherwise once it has loaded, for pages drawn elsewhere.
    /// </summary>
    public static void Set(ScrollViewer scroll, Action place)
    {
        Waiting.AddOrUpdate(scroll, place);
        scroll.AttachedToVisualTree += (_, _) => Dispatcher.UIThread.Post(() => Apply(scroll), DispatcherPriority.Loaded);
    }

    /// <summary>Lays <paramref name="root"/> out now, then scrolls each page inside it that waits for its place.</summary>
    public static void Settle(Layoutable root)
    {
        if (!root.IsAttachedToVisualTree()) return;
        root.UpdateLayout();
        foreach (var (scroll, _) in Waiting.ToArray())
            if (scroll.IsAttachedToVisualTree() && scroll.Extent.Height > 0 && root.IsVisualAncestorOf(scroll)) Apply(scroll);
    }

    private static void Apply(ScrollViewer scroll)
    {
        if (!Waiting.TryGetValue(scroll, out var place)) return;
        Waiting.Remove(scroll);
        place();
    }
}
