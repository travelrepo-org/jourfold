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
    /// Apple Maps, which opens the Maps app (<paramref name="apple"/>, by default on iOS), and <see cref="GoAsync"/>
    /// offers the other maps apps beside it.
    /// </summary>
    public static Uri Link(double? lat, double? lon, string name, string query, bool? apple = null)
    {
        var at = lat is { } a && lon is { } b ? a.ToString("0.######", CultureInfo.InvariantCulture) + "," + b.ToString("0.######", CultureInfo.InvariantCulture) : null;
        if (apple ?? OperatingSystem.IsIOS())
            return new Uri("https://maps.apple.com/?" + (at is null ? "q=" + Uri.EscapeDataString(query) : "ll=" + at + "&q=" + Uri.EscapeDataString(name)));
        if (at is null) return new Uri("geo:0,0?q=" + Uri.EscapeDataString(query));
        return new Uri("geo:" + at + "?q=" + at + "(" + Uri.EscapeDataString(name) + ")");
    }

    /// <summary>A maps app that can take the place: the key of its name in the strings, and its link.</summary>
    public sealed record MapsApp(string NameKey, Uri Link);

    /// <summary>
    /// The maps apps iOS can hand a place to, Apple Maps first. iOS has no geo links that let the user choose, so
    /// Jourfold offers the apps itself; each one is only listed when it is installed (<see cref="MobileHost.CanOpen"/>,
    /// with the schemes in <c>LSApplicationQueriesSchemes</c>).
    /// </summary>
    public static IReadOnlyList<MapsApp> AppleApps(double? lat, double? lon, string name, string query)
    {
        var at = lat is { } a && lon is { } b ? a.ToString("0.######", CultureInfo.InvariantCulture) + "," + b.ToString("0.######", CultureInfo.InvariantCulture) : null;
        return
        [
            new("MapsApple", Link(lat, lon, name, query, apple: true)),
            new("MapsGoogle", new Uri("comgooglemaps://?q=" + (at ?? Uri.EscapeDataString(query)))),
            new("MapsWaze", new Uri("waze://?" + (at is null ? "q=" + Uri.EscapeDataString(query) : "ll=" + at) + "&navigate=yes"))
        ];
    }

    /// <summary>
    /// Hands the place to the maps app. On Android the system offers the user's apps for the geo link; on iOS Jourfold
    /// asks which app, when more than Apple Maps is installed.
    /// </summary>
    public static async Task GoAsync(MobileSession s, TopLevel? top, (double? Lat, double? Lon, string Name, string Query) target)
    {
        if (top?.Launcher is not { } launcher) return;
        if (!OperatingSystem.IsIOS() || s.Host.CanOpen is not { } canOpen) { await launcher.LaunchUriAsync(Link(target.Lat, target.Lon, target.Name, target.Query)); return; }
        var choices = Choices(AppleApps(target.Lat, target.Lon, target.Name, target.Query), canOpen);
        if (choices.Count == 1) { await launcher.LaunchUriAsync(choices[0].Link); return; }
        s.ShowMenu(new ActionMenu(s.Strings["NavigateWith"], target.Name,
            choices.Select(app => new MenuAction(s.Strings[app.NameKey], "map", async t => { if ((t ?? top).Launcher is { } l) await l.LaunchUriAsync(app.Link); })).ToArray()));
    }

    /// <summary>Apple Maps, which is always there, and the other apps that are installed.</summary>
    public static IReadOnlyList<MapsApp> Choices(IReadOnlyList<MapsApp> apps, Func<Uri, bool> canOpen) =>
        apps.Skip(1).Where(app => canOpen(app.Link)).Prepend(apps[0]).ToArray();

    /// <summary>A Navigate button, or null when the item has no place.</summary>
    public static Button? Button(MobileSession s, Entity item)
    {
        if (Target(s.Trip!, item) is not { } target) return null;
        var button = Ui.Button(s.Strings["Navigate"], "navigation", "primary");
        button.Click += async (_, _) => await GoAsync(s, TopLevel.GetTopLevel(button), target);
        return button;
    }
}
