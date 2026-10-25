using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using TravelRepo.Core;

namespace Jourfold.Mobile.Views;

/// <summary>
/// The day's places in order, numbered as they will be on the map, each with a way to get there. The drawn map from
/// the desktop app comes on top of this list as its sheet.
/// </summary>
public static class MapView
{
    public static Control Build(MobileSession s, WidthClass width)
    {
        var day = s.Agenda.Find(s.SelectedDate) ?? s.Agenda.Days.FirstOrDefault();
        var page = Ui.V(12, Lines.TripHeader(s));
        if (day is null) return Ui.Scroll(page);
        page.Children.Add(Ui.Text(Lines.DayTitle(day.Date, s), "h2").Margin(16, 4, 16, 0));
        var number = 0;
        foreach (var entry in day.Entries.SelectMany(e => e.IsGroup ? e.Steps : [e]))
        {
            if (Navigation.Target(s.Trip!, entry.Item) is null) continue;
            number++;
            var badge = new Border { Width = 30, Height = 30, CornerRadius = new CornerRadius(15), Child = Ui.Text(number.ToString(CultureInfo.InvariantCulture), "strong").Also(t => { t.HorizontalAlignment = HorizontalAlignment.Center; t.Foreground = Avalonia.Media.Brushes.White; }) }
                .Res(Border.BackgroundProperty, "Kind." + Visuals.KindKey(entry.Kind) + ".Bar");
            var time = TimeText.Local(entry, s.PhoneZone, s.Strings, s.Trip).Primary;
            page.Children.Add(Ui.Card(Ui.Columns("Auto,*,Auto", badge.Margin(0, 0, 12, 0), Ui.V(1, Ui.Text(entry.Item.Title, "strong"), Ui.Text(string.Join(" · ", new[] { time, Lines.Where(s.Trip!, entry.Item) }.Where(t => t.Length > 0)), "caption")), Navigation.Button(s, entry.Item)), 12).Margin(16, 0));
        }
        if (number == 0) page.Children.Add(Ui.Text(s.Strings["NoPlacesThisDay"], "muted").Margin(16, 8));
        page.Margin = new Thickness(0, 0, 0, 24);
        return Ui.Scroll(page);
    }
}
