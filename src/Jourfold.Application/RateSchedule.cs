using TravelRepo.Core;

namespace Jourfold.Application;

/// <summary>
/// When the desktop and the phone fetch exchange rates again. The European Central Bank publishes on working days
/// only, so rates from a Friday are still the newest on Sunday: rates fetched in the last 12 hours count as fresh
/// whatever their date. A failed or fruitless attempt is not repeated for 30 minutes, so pages that ask for rates each
/// time they are drawn cannot fetch in a loop.
/// </summary>
public sealed class RateSchedule
{
    public static readonly TimeSpan Fresh = TimeSpan.FromHours(12);
    public static readonly TimeSpan Retry = TimeSpan.FromMinutes(30);
    /// <summary>How long a fetch the person asked for keeps another one from starting, so repeated taps fetch once.</summary>
    public static readonly TimeSpan Again = TimeSpan.FromMinutes(1);
    private DateTime? tried, asked;

    /// <summary>When the rates kept in <paramref name="file"/> were fetched: the file is written only then.</summary>
    public static DateTimeOffset? Fetched(string file) => File.Exists(file) ? new DateTimeOffset(File.GetLastWriteTimeUtc(file), TimeSpan.Zero) : null;

    /// <summary>
    /// Whether to fetch now because the person asked for it, which skips the waits above. False when the person asked
    /// less than <see cref="Again"/> ago. A yes counts as an attempt from now on.
    /// </summary>
    public bool BeginRefresh(DateTime? nowUtc = null)
    {
        var now = nowUtc ?? DateTime.UtcNow;
        if (asked is { } last && now - last < Again) return false;
        asked = tried = now;
        return true;
    }

    /// <summary>
    /// Whether to fetch now, given the rates kept in <paramref name="file"/>. A yes counts as an attempt from now on.
    /// <paramref name="force"/> fetches anyway, for example right after the person turns rates on.
    /// </summary>
    public bool Begin(ExchangeRates? known, string file, bool force = false, DateTime? nowUtc = null)
    {
        var now = nowUtc ?? DateTime.UtcNow;
        if (!force)
        {
            if (tried is { } last && now - last < Retry) return false;
            if (known is not null && (known.Date >= DateOnly.FromDateTime(now.AddDays(-1)) || File.Exists(file) && now - File.GetLastWriteTimeUtc(file) < Fresh)) return false;
        }
        tried = now;
        return true;
    }
}
