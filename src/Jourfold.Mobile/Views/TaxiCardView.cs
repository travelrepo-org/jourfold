using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using TravelRepo.Core;

namespace Jourfold.Mobile.Views;

/// <summary>
/// A place to show a taxi driver or a passer-by: the name and address in the local script as large as they fit, the
/// usual spelling and the phone number below, on a calm background at full brightness.
/// </summary>
public static class TaxiCardView
{
    public static Control Build(MobileSession s, Entity place)
    {
        var str = s.Strings;
        var back = Ui.IconButton("chevron-left", str["Back"]); back.Click += (_, _) => s.ShowTaxiCard(null);
        var local = Places.LocalName(place); var localAddress = Places.LocalAddress(place);
        var body = Ui.V(18,
            Ui.Text(str["TaxiCardHint"], "overline"),
            Ui.Text(local ?? place.Title, "h1").Also(t => { t.FontSize = 40; t.TextWrapping = TextWrapping.Wrap; }),
            (localAddress ?? Places.Address(place)) is { } shown ? new SelectableTextBlock { Text = shown, FontSize = 28, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap } : null,
            local is not null || localAddress is not null ? Ui.V(4, Ui.Text(place.Title, "strong"), Places.Address(place) is { } usual ? Ui.Text(usual, "muted").Also(t => t.TextWrapping = TextWrapping.Wrap) : null) : null,
            Places.Contact(place)?.Phone is { Length: > 0 } phone ? new SelectableTextBlock { Text = phone, FontSize = 22 } : null);
        var layout = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        layout.Children.Add(Ui.Columns("Auto,*", back, Ui.Line(str["TaxiCard"], "title").Margin(4, 0)).Margin(4, 6, 8, 6));
        layout.Children.Add(new ScrollViewer { Content = body.Margin(20, 8, 20, 32), HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled }.Row(1));
        var page = new Border { Child = layout }.Res(Border.BackgroundProperty, "Bg.App");
        AutomationProperties.SetName(page, str["TaxiCard"]);
        return page;
    }

    /// <summary>"Taxi card" for a plan whose place, stay or destination has an address or a local name.</summary>
    public static Button? Button(MobileSession s, Entity item)
    {
        var trip = s.Trip!;
        var place = Stays.Resolve(trip, item) is { Place: { } stay } ? trip.Find(stay) : ScheduleQueries.Route(trip, item).To ?? ScheduleQueries.PrimaryPlace(trip, item);
        if (place is null || (Places.LocalName(place) ?? Places.LocalAddress(place) ?? Places.Address(place)) is null) return null;
        var button = Ui.Button(s.Strings["TaxiCard"], "car-taxi-front", "soft");
        button.Click += (_, _) => s.ShowTaxiCard(place.Id);
        return button;
    }
}
