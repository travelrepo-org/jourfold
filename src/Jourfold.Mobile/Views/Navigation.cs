using System.Globalization;
using Avalonia.Controls;
using TravelRepo.Core;

namespace Jourfold.Mobile.Views;

/// <summary>Hands a place to the phone's navigation app. Jourfold does no turn-by-turn navigation itself.</summary>
public static class Navigation
{
    /// <summary>The place to go to: the destination of a journey, otherwise the item's place. Null when it has no coordinates.</summary>
    public static (double Lat, double Lon, string Name)? Target(TripSnapshot trip, Entity item)
    {
        var (_, to) = ScheduleQueries.Route(trip, item);
        var place = to ?? ScheduleQueries.PrimaryPlace(trip, item);
        if (place?.Data["location"] is not { } location) return null;
        if (!double.TryParse(location["latitude"]?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var lat) || !double.TryParse(location["longitude"]?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var lon)) return null;
        return (lat, lon, place.Title);
    }

    /// <summary>
    /// A geo link with the place's name. Android offers the user's maps and navigation apps for it; iOS needs a maps
    /// link instead and will get it when the iOS app exists.
    /// </summary>
    public static Uri Link(double lat, double lon, string name)
    {
        var at = lat.ToString("0.######", CultureInfo.InvariantCulture) + "," + lon.ToString("0.######", CultureInfo.InvariantCulture);
        return new Uri("geo:" + at + "?q=" + at + "(" + Uri.EscapeDataString(name) + ")");
    }

    /// <summary>A Navigate button, or null when the item has no place with coordinates.</summary>
    public static Button? Button(MobileSession s, Entity item)
    {
        if (Target(s.Trip!, item) is not { } target) return null;
        var button = Ui.Button(s.Strings["Navigate"], "navigation", "primary");
        button.Click += async (_, _) =>
        {
            if (TopLevel.GetTopLevel(button)?.Launcher is { } launcher) await launcher.LaunchUriAsync(Link(target.Lat, target.Lon, target.Name));
        };
        return button;
    }
}
