using System.Globalization;
using NodaTime;
using TravelRepo.Core;

namespace Jourfold.Desktop;

/// <summary>Icon and color-token choices for domain concepts. Classification itself comes from TravelRepo.</summary>
public static class Visuals
{
    public static string KindKey(ScheduleKind kind) => kind.ToString();

    public static string KindIcon(ScheduleKind kind, string? transportType = null) => kind switch
    {
        ScheduleKind.Transport => TransportIcon(transportType),
        ScheduleKind.Accommodation => "bed-double",
        ScheduleKind.Food => "utensils",
        ScheduleKind.Sightseeing => "camera",
        ScheduleKind.Culture => "landmark",
        ScheduleKind.Nature => "mountain",
        ScheduleKind.Shopping => "shopping-bag",
        ScheduleKind.Nightlife => "wine",
        ScheduleKind.Meeting => "users",
        ScheduleKind.FreeTime => "coffee",
        ScheduleKind.Other => "circle-dot",
        _ => "star"
    };

    public static string TransportIcon(string? type) => type switch
    {
        "flight" => "plane",
        "train" => "train-front",
        "bus" => "bus",
        "car" => "car",
        "taxi" or "rideshare" => "car-taxi-front",
        "ferry" or "ship" => "ship",
        "bicycle" => "bike",
        "walking" => "footprints",
        _ => "route"
    };

    public static string CategoryIcon(string category) => KindIcon(ScheduleCategories.FromCategory(category), category == "transport" ? null : category);

    public static string EntityIcon(string type) => type switch
    {
        "schedule_item" => "calendar",
        "booking" => "ticket",
        "place" => "map-pin",
        "person" => "user",
        "task" => "square-check",
        "expense" => "receipt",
        "budget" => "piggy-bank",
        "document" => "file",
        "note" => "sticky-note",
        "comment" => "message-square",
        "collection" => "layers",
        "trip" => "house",
        _ => "circle-dot"
    };

    /// <summary>Visual for an entity: schedule items use their classified kind, other types a stable color.</summary>
    public static (string Icon, string KindKey) For(TripSnapshot trip, Entity entity)
    {
        if (entity.Type == "schedule_item")
        {
            var kind = ScheduleCategories.Classify(trip, entity);
            return (KindIcon(kind, ScheduleCategories.TransportType(entity)), KindKey(kind));
        }
        var key = entity.Type switch
        {
            "booking" => "Transport",
            "place" => "Sightseeing",
            "person" => "Accommodation",
            "task" => "Activity",
            "expense" or "budget" => "Food",
            "document" => "Meeting",
            "note" => "Nightlife",
            "collection" => "Culture",
            _ => "Other"
        };
        if (entity.Type == "document" && (entity.Data["media_type"]?.ToString() ?? "").StartsWith("image/", StringComparison.Ordinal)) return ("image", key);
        if (entity.Type == "document" && entity.Data["media_type"]?.ToString() == "application/pdf") return ("file-text", key);
        return (EntityIcon(entity.Type), key);
    }

    /// <summary>Status pill: localization key, icon, background token, foreground token.</summary>
    public static (string Icon, string Background, string Foreground) Status(string? status) => status switch
    {
        "confirmed" or "completed" => ("circle-check", "Success.Soft", "Success"),
        "reserved" or "pending" or "in_progress" => ("clock", "Warning.Soft", "Warning"),
        "planned" or "open" => ("calendar", "Info.Soft", "Info"),
        "cancelled" => ("x", "Danger.Soft", "Danger"),
        _ => ("sparkles", "Bg.Subtle", "Text.Muted")
    };
}

/// <summary>Locale-aware display formatting. Canonical values are never formatted with these helpers.</summary>
public static class Formats
{
    public static string Time(LocalTime time) => time.ToString(CultureInfo.CurrentCulture.DateTimeFormat.ShortTimePattern.Contains('H') ? "HH:mm" : "h:mm tt", CultureInfo.CurrentCulture);
    public static string Time(LocalDateTime time) => Time(time.TimeOfDay);
    public static string DayHeader(LocalDate date) => date.ToString("ddd", CultureInfo.CurrentCulture);
    public static string DayNumber(LocalDate date) => date.ToString("d MMM", CultureInfo.CurrentCulture);
    public static string LongDay(LocalDate date) => date.ToString("dddd, d MMMM", CultureInfo.CurrentCulture);
    public static string Date(LocalDate date) => date.ToString("d MMM yyyy", CultureInfo.CurrentCulture);
    public static string ShortDate(LocalDate date) => date.ToString("d MMM", CultureInfo.CurrentCulture);

    public static string DateRange(LocalDate? start, LocalDate? end)
    {
        if (start is null && end is null) return "";
        if (start is null || end is null || start == end) return Date((start ?? end)!.Value);
        var a = start.Value; var b = end.Value;
        if (a.Year == b.Year && a.Month == b.Month) return a.Day.ToString(CultureInfo.CurrentCulture) + "–" + Date(b);
        if (a.Year == b.Year) return ShortDate(a) + " – " + Date(b);
        return Date(a) + " – " + Date(b);
    }

    public static string Duration(NodaTime.Duration duration, Localization s)
    {
        var minutes = (long)Math.Round(duration.TotalMinutes);
        if (minutes < 60) return string.Format(CultureInfo.CurrentCulture, s["DurationMinutesShort"], minutes);
        if (minutes % 60 == 0) return string.Format(CultureInfo.CurrentCulture, s["DurationHoursShort"], minutes / 60);
        return string.Format(CultureInfo.CurrentCulture, s["DurationHoursMinutesShort"], minutes / 60, minutes % 60);
    }

    public static string Relative(DateTimeOffset when, Localization s)
    {
        var delta = DateTimeOffset.Now - when;
        if (delta < TimeSpan.FromMinutes(1)) return s["JustNow"];
        if (delta < TimeSpan.FromHours(1)) return string.Format(CultureInfo.CurrentCulture, s["MinutesAgo"], (int)delta.TotalMinutes);
        if (delta < TimeSpan.FromDays(1)) return string.Format(CultureInfo.CurrentCulture, s["HoursAgo"], (int)delta.TotalHours);
        if (delta < TimeSpan.FromDays(7)) return string.Format(CultureInfo.CurrentCulture, s["DaysAgo"], (int)delta.TotalDays);
        return when.ToLocalTime().ToString("d MMM yyyy", CultureInfo.CurrentCulture);
    }

    public static string Money(decimal amount, string currency)
    {
        try
        {
            var culture = (CultureInfo)CultureInfo.CurrentCulture.Clone();
            culture.NumberFormat.CurrencySymbol = currency;
            return amount.ToString("C", culture).Replace(currency, currency + " ").Replace("  ", " ").Trim();
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException) { return amount.ToString("0.00", CultureInfo.CurrentCulture) + " " + currency; }
    }

    public static string Bytes(long bytes) => bytes switch
    {
        < 1024 => bytes + " B",
        < 1024 * 1024 => (bytes / 1024d).ToString("0.#", CultureInfo.CurrentCulture) + " KB",
        _ => (bytes / 1024d / 1024d).ToString("0.#", CultureInfo.CurrentCulture) + " MB"
    };

    public static string ZoneName(string? zone) => string.IsNullOrEmpty(zone) ? "" : zone.Split('/')[^1].Replace('_', ' ');
}
