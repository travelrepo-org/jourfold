using System.Globalization;
using Avalonia.Controls;
using TravelRepo.Core;

namespace Jourfold.Mobile.Views;

/// <summary>Hands a place to the phone's navigation app. Jourfold does no turn-by-turn navigation itself.</summary>
public static class Navigation
{
    /// <summary>
    /// The place to go to: the destination of a journey, otherwise the item's place. With a position, the maps app gets
    /// it exactly; without, it searches for the address or, failing that, the place's name. Null without a place.
    /// </summary>
    public static (double? Lat, double? Lon, string Name, string Query)? Target(TripSnapshot trip, Entity item)
    {
        var (_, to) = ScheduleQueries.Route(trip, item);
        if ((to ?? ScheduleQueries.PrimaryPlace(trip, item)) is not { } place) return null;
        var at = Places.Location(place);
        return (at?.Latitude, at?.Longitude, place.Title, Places.Address(place) ?? place.Title);
    }

    /// <summary>
    /// A geo link. With a position it carries the place's name as its label; without, the maps app searches for the
    /// query. Android offers the user's maps and navigation apps for it. iOS has no geo links; there it is a link to
    /// Apple Maps, which opens the Maps app (<paramref name="apple"/>, by default on iOS).
    /// </summary>
    public static Uri Link(double? lat, double? lon, string name, string query, bool? apple = null)
    {
        var at = lat is { } a && lon is { } b ? a.ToString("0.######", CultureInfo.InvariantCulture) + "," + b.ToString("0.######", CultureInfo.InvariantCulture) : null;
        if (apple ?? OperatingSystem.IsIOS())
            return new Uri("https://maps.apple.com/?" + (at is null ? "q=" + Uri.EscapeDataString(query) : "ll=" + at + "&q=" + Uri.EscapeDataString(name)));
        if (at is null) return new Uri("geo:0,0?q=" + Uri.EscapeDataString(query));
        return new Uri("geo:" + at + "?q=" + at + "(" + Uri.EscapeDataString(name) + ")");
    }

    /// <summary>A Navigate button, or null when the item has no place.</summary>
    public static Button? Button(MobileSession s, Entity item)
    {
        if (Target(s.Trip!, item) is not { } target) return null;
        var button = Ui.Button(s.Strings["Navigate"], "navigation", "primary");
        button.Click += async (_, _) =>
        {
            if (TopLevel.GetTopLevel(button)?.Launcher is { } launcher) await launcher.LaunchUriAsync(Link(target.Lat, target.Lon, target.Name, target.Query));
        };
        return button;
    }
}
