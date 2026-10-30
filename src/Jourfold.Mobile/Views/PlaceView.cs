using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Jourfold.Application;
using TravelRepo.Core;

namespace Jourfold.Mobile.Views;

/// <summary>
/// A place's name, address and position. Coordinates copied from a map, such as "-37.0093, 174.7841", or a map link
/// pasted into any field fill latitude and longitude, however they arrive: the keyboard's clipboard suggestion inserts
/// text without a paste event, so the fields read their text as it changes. The address search asks the map
/// provider's server and only works after the user turned on online maps; "Where I am" asks the device.
/// </summary>
public sealed class PlaceView
{
    private readonly MobileSession s;
    private readonly Entity? place;
    private readonly TextBox name = new();
    private readonly TextBox address = new() { AcceptsReturn = true, TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    private readonly TextBox latitude = new() { PlaceholderText = "-37.0093" };
    private readonly TextBox longitude = new() { PlaceholderText = "174.7841" };
    private readonly StackPanel results = Ui.V(0);
    private readonly TextBlock problem = Ui.Text("", "caption").Also(t => { t.IsVisible = false; t.Res(TextBlock.ForegroundProperty, "Danger"); });
    private bool filling;
    // What each field held before its last change, to tell pasted text from typed characters.
    private readonly Dictionary<TextBox, int> lengths = new();

    public Control View { get; }
    public Control Header { get; }

    public PlaceView(MobileSession session, Entity? place)
    {
        s = session; this.place = place; var str = s.Strings;
        AutomationProperties.SetName(name, str["Title"]); AutomationProperties.SetName(address, str["Address"]);
        AutomationProperties.SetName(latitude, str["Latitude"]); AutomationProperties.SetName(longitude, str["Longitude"]);
        if (place is not null)
        {
            name.Text = place.Title; address.Text = Places.Address(place);
            if (Places.Location(place) is { } at) Fill(at.Latitude, at.Longitude);
        }
        else if (s.PlaceDraft is { } draft)
        {
            name.Text = draft.Name; address.Text = draft.Address;
            if (draft.Latitude is { } lat && draft.Longitude is { } lon) Fill(lat, lon);
        }
        foreach (var box in new[] { latitude, longitude, address, name }) { lengths[box] = box.Text?.Length ?? 0; box.TextChanged += (_, _) => ReadPosition(box); }

        var close = Ui.IconButton("x", str["Close"]); close.Click += (_, _) => s.CloseOverlay();
        Header = Ui.Columns("*,Auto", Ui.Text(place is null ? str["AddPlace"] : place.Title, "h3"), close).Margin(16, 0, 8, 8);

        var search = Ui.Button(str["Search"], "search", "soft");
        search.Click += async (_, _) => await SearchAsync(search);
        var here = s.Host.Locate is null ? null : Ui.Button(str["WhereIAm"], "locate-fixed", "soft");
        if (here is not null) here.Click += async (_, _) => await LocateAsync(here);
        var save = Ui.Button(str["Save"], "check", "primary").Also(b => b.HorizontalAlignment = HorizontalAlignment.Stretch);
        save.Click += async (_, _) => await SaveAsync(save);

        View = Ui.V(14,
            Field(str["Title"], name),
            Field(str["Address"], Ui.V(8, address, Ui.H(8, search, here))),
            results,
            Ui.Columns("*,12,*", Field(str["Latitude"], latitude), null, Field(str["Longitude"], longitude).Col(2)),
            Ui.Text(str["CoordinatesPasteHint"], "caption").Also(t => t.TextWrapping = Avalonia.Media.TextWrapping.Wrap),
            problem, save).Margin(16, 0, 16, 24);
    }

    private static Control Field(string label, Control value) => Ui.V(6, Ui.Text(label, "overline"), value);

    private void Fill(double lat, double lon)
    {
        filling = true;
        latitude.Text = lat.ToString("0.######", CultureInfo.InvariantCulture); longitude.Text = lon.ToString("0.######", CultureInfo.InvariantCulture);
        lengths[latitude] = latitude.Text.Length; lengths[longitude] = longitude.Text.Length;
        filling = false;
    }

    /// <summary>
    /// A pair or a map link that arrives in a field at once, as pasting does, becomes the position; in the address or
    /// name it is taken out again. Typed characters do not count, or "35.68, 1" would be read halfway through typing.
    /// </summary>
    private void ReadPosition(TextBox box)
    {
        var before = lengths[box]; var now = box.Text?.Length ?? 0; lengths[box] = now;
        if (filling || now - before < 3 || !Coordinates.TryParseLink(box.Text, out var lat, out var lon)) return;
        // A single number in a coordinate field is just being typed; only a pair or a link counts there.
        if ((box == latitude || box == longitude) && Coordinates.TryParse(box.Text, out _)) return;
        // Changing a field's text while it reports its own change does not hold; the fill waits for the next turn.
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            Fill(lat, lon);
            if (box == address || box == name) { filling = true; box.Text = ""; lengths[box] = 0; filling = false; }
            problem.IsVisible = false;
        });
    }

    private async Task SearchAsync(Button search)
    {
        var str = s.Strings; results.Children.Clear(); problem.IsVisible = false;
        var query = string.IsNullOrWhiteSpace(address.Text) ? name.Text : address.Text;
        if (string.IsNullOrWhiteSpace(query)) return;
        if (!s.Settings.OnlineMaps)
        {
            // Searching sends the text to the map provider's server, so it waits for the user's yes, as on the desktop.
            var turnOn = Ui.Button(str["ShowStreetMap"], "globe", "primary");
            turnOn.Click += async (_, _) => { s.SetOnlineMaps(true); await SearchAsync(search); };
            results.Children.Add(Ui.Card(Ui.V(10, Ui.Text(str["StreetMapTitle"], "strong"), Ui.Text(str["OnlineSearchBody"], "caption").Also(t => t.TextWrapping = Avalonia.Media.TextWrapping.Wrap), turnOn), 14));
            return;
        }
        search.IsEnabled = false; results.Children.Add(Ui.Text(str["Searching"], "caption"));
        try
        {
            var found = await s.SearchPlacesAsync(query.Trim());
            results.Children.Clear();
            if (found.Count == 0) results.Children.Add(Ui.Text(str["NothingFound"], "caption"));
            foreach (var result in found)
            {
                var row = new Button { Content = Ui.Columns("Auto,*", Ui.Icon("map-pin", 18, "Text.Muted").Margin(0, 0, 12, 0), Ui.V(1, Ui.Text(result.Name, "strong"), Ui.Text(result.Address, "caption"))) }.Classed("row");
                AutomationProperties.SetName(row, result.Name + ", " + result.Address);
                row.Click += (_, _) =>
                {
                    if (string.IsNullOrWhiteSpace(name.Text)) name.Text = result.Name;
                    filling = true; address.Text = result.Address; lengths[address] = result.Address.Length; filling = false;
                    Fill(result.Latitude, result.Longitude); results.Children.Clear();
                };
                results.Children.Add(row);
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { results.Children.Clear(); Show(str["NetworkError"]); }
        finally { search.IsEnabled = true; }
    }

    private async Task LocateAsync(Button here)
    {
        here.IsEnabled = false; problem.IsVisible = false;
        try
        {
            if (await s.Host.Locate!(CancellationToken.None) is { } at) Fill(at.Latitude, at.Longitude);
            else Show(s.Strings["NoPosition"]);
        }
        finally { here.IsEnabled = true; }
    }

    private void Show(string text) { problem.Text = text; problem.IsVisible = true; }

    private async Task SaveAsync(Button save)
    {
        var str = s.Strings; problem.IsVisible = false;
        double? lat = null, lon = null;
        var hasLat = !string.IsNullOrWhiteSpace(latitude.Text); var hasLon = !string.IsNullOrWhiteSpace(longitude.Text);
        if (hasLat || hasLon)
        {
            if (!Coordinates.TryParse(latitude.Text, out var a) || !Coordinates.IsLatitude(a)) { Show(str["LatitudeRange"]); return; }
            if (!Coordinates.TryParse(longitude.Text, out var b) || !Coordinates.IsLongitude(b)) { Show(str["LongitudeRange"]); return; }
            lat = a; lon = b;
        }
        save.IsEnabled = false;
        try { await s.SavePlaceAsync(place?.Id, name.Text ?? "", address.Text, lat, lon); }
        catch (DomainException ex) { Show(ex.Code == "mobile.title" ? ex.Message : str.Diagnostic(ex.Code)); }
        finally { save.IsEnabled = true; }
    }
}
