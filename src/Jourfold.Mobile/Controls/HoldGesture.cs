using Avalonia;

namespace Jourfold.Mobile.Controls;

/// <summary>What a finger did, once it is lifted.</summary>
public enum HoldOutcome
{
    /// <summary>Lifted before <see cref="HoldGesture.Threshold"/>, where it was put down: a normal tap.</summary>
    Tap,
    /// <summary>Held at least <see cref="HoldGesture.Threshold"/>, then lifted where it was put down: a long press.</summary>
    Hold,
    /// <summary>Moved, so a scroll or a swipe, or taken over by another control: neither a tap nor a long press.</summary>
    None,
}

/// <summary>
/// Tells a long press from a tap and from a scroll, by when and where the finger goes down, moves and comes up. A long
/// press counts only once the finger is lifted, so a hold that turns into a scroll can never open a menu. The times are
/// the input events' own timestamps in milliseconds, which keeps the rules testable without a clock.
/// </summary>
public sealed class HoldGesture
{
    /// <summary>How long a finger has to stay down for a long press.</summary>
    public static readonly TimeSpan Threshold = TimeSpan.FromMilliseconds(500);
    /// <summary>How far a finger may wander, in dp, and still count as held in place.</summary>
    public const double Tolerance = 10;

    private Point? start;
    private ulong pressedAt;

    /// <summary>Whether a finger is down and has not moved away.</summary>
    public bool Active => start is not null;

    public void Press(Point at, ulong timestamp) { start = at; pressedAt = timestamp; }

    /// <summary>Returns true when this move turned the press into a scroll or swipe.</summary>
    public bool Move(Point at)
    {
        if (start is not { } from || Point.Distance(from, at) <= Tolerance) return false;
        start = null;
        return true;
    }

    /// <summary>The finger went to another control, such as a scrolling list, or the control went away.</summary>
    public void Cancel() => start = null;

    public HoldOutcome Release(ulong timestamp)
    {
        if (start is null) return HoldOutcome.None;
        start = null;
        return timestamp >= pressedAt && TimeSpan.FromMilliseconds(timestamp - pressedAt) >= Threshold ? HoldOutcome.Hold : HoldOutcome.Tap;
    }
}
