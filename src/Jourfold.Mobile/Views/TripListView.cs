using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Jourfold.Mobile.Views;

/// <summary>The trips on this device. Opening one shows its Today; the back gesture comes back here.</summary>
public static class TripListView
{
    public static Control Build(MobileSession s)
    {
        if (s.RemoteTrips is { } remote) return GitHubView.RemoteTrips(s, remote);
        var logo = new Image { Height = 30, HorizontalAlignment = HorizontalAlignment.Left, Stretch = Stretch.Uniform };
        logo.Bind(Image.SourceProperty, logo.GetResourceObservable(Avalonia.Application.Current?.ActualThemeVariant == Avalonia.Styling.ThemeVariant.Dark ? "Brand.Lockup.Dark" : "Brand.Lockup.Light"));
        var page = Ui.V(14, logo.Margin(20, 16, 20, 4), Ui.Text(s.Strings["Trips"], "h1").Margin(20, 0));
        foreach (var trip in s.Trips) page.Children.Add(Card(s, trip).Margin(16, 0));
        if (s.Trips.Count == 0) page.Children.Add(Ui.Text(s.Strings["NoTripsYet"], "muted").Margin(20, 0));
        var sample = Ui.Button(s.Strings["ExploreSample"], "sparkles", s.Trips.Count == 0 ? "primary" : "soft");
        sample.Click += async (_, _) => { sample.IsEnabled = false; await s.OpenAsync(await s.CreateSampleAsync()); };
        page.Children.Add(sample.Also(b => b.HorizontalAlignment = HorizontalAlignment.Stretch).Margin(16, 8, 16, 8));
        // A trip someone shares on GitHub comes to the phone from here.
        page.Children.Add(Ui.Text(s.Strings["SharedTrips"], "overline").Margin(20, 8, 20, 0));
        if (s.GitHubLogin is null || s.SignInCode is not null) page.Children.Add(GitHubView.Account(s).Margin(16, 0, 16, 24));
        else
        {
            var problem = Ui.Text("", "caption").Also(t => { t.IsVisible = false; t.Res(TextBlock.ForegroundProperty, "Danger"); });
            var find = Ui.Button(s.Strings["GetTripFromGitHub"], "download");
            find.Click += async (_, _) =>
            {
                find.IsEnabled = false;
                try { await s.FindRemoteTripsAsync(); }
                catch (Exception ex) when (ex is TravelRepo.Core.DomainException or HttpRequestException) { problem.Text = s.Strings.Error(ex); problem.IsVisible = true; find.IsEnabled = true; }
            };
            page.Children.Add(Ui.V(8, find.Also(b => b.HorizontalAlignment = HorizontalAlignment.Stretch), problem).Margin(16, 0, 16, 24));
        }
        return Ui.Scroll(page);
    }

    private static Control Card(MobileSession s, TripCard trip)
    {
        var dates = Formats.DateRange(trip.Start, trip.End);
        var content = Ui.V(0,
            Covers.Thumbnail(trip.Repository, trip.Trip, double.NaN, 132, 0).Also(c => c.HorizontalAlignment = HorizontalAlignment.Stretch),
            Ui.V(4,
                Ui.Text(trip.Trip.Manifest.Title, "h3"),
                Ui.Text(dates.Length > 0 ? dates : s.Strings["NoDatesYet"], "muted"),
                trip.Places.Count > 0 ? Ui.Text(string.Join(", ", trip.Places), "caption") : null,
                trip.Travellers.Count > 0 ? Ui.AvatarStack(trip.Travellers, 26).Margin(0, 4, 0, 0) : null).Margin(16, 12, 16, 14));
        var card = new Border { Child = content, ClipToBounds = true }.Classed("card");
        var button = new Button { Content = card }.Classed("card");
        AutomationProperties.SetName(button, trip.Trip.Manifest.Title + (dates.Length > 0 ? ", " + dates : ""));
        Menus.OnHold(button, () => s.ShowMenu(Menus.Trip(s, trip)));
        button.Click += async (_, _) => await s.OpenAsync(trip.Path);
        return button;
    }
}
