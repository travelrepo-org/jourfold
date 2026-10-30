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
    /// <param name="width">From 600 dp the trips stand side by side, two or three in a row.</param>
    public static Control Build(MobileSession s, WidthClass width = WidthClass.Phone)
    {
        if (s.RemoteTrips is { } remote) return GitHubView.RemoteTrips(s, remote);
        var logo = new Image { Height = 30, HorizontalAlignment = HorizontalAlignment.Left, Stretch = Stretch.Uniform };
        logo.Bind(Image.SourceProperty, logo.GetResourceObservable(Avalonia.Application.Current?.ActualThemeVariant == Avalonia.Styling.ThemeVariant.Dark ? "Brand.Lockup.Dark" : "Brand.Lockup.Light"));
        var page = Ui.V(14, logo.Margin(20, 16, 20, 4), Ui.Text(s.Strings["Trips"], "h1").Margin(20, 0));
        // Something shared from another app waits for the trip it belongs to.
        if (s.Incoming is { } shared)
        {
            var what = shared.Files.Count > 0 ? string.Join(", ", shared.Files.Select(f => f.Name)) : (shared.Lines.FirstOrDefault() ?? shared.Text ?? "");
            var cancel = Ui.Button(s.Strings["Cancel"], null, "ghost", "compact"); cancel.Click += (_, _) => s.DropIncoming();
            page.Children.Add(Ui.Card(Ui.V(6, Ui.Text(s.Strings["ChooseTripForShare"], "strong"), Ui.Text(what, "caption").Also(t => t.TextWrapping = TextWrapping.Wrap), cancel.Also(b => b.HorizontalAlignment = HorizontalAlignment.Left)), 14)
                .Also(c => c.BorderThickness = new Thickness(1)).Res(Border.BorderBrushProperty, "Accent").Margin(16, 0));
        }
        var columns = width switch { WidthClass.Phone => 1, WidthClass.CompactTablet => 2, _ => 3 };
        if (columns == 1) foreach (var trip in s.Trips) page.Children.Add(Card(s, trip).Margin(16, 0));
        else if (s.Trips.Count > 0)
        {
            var grid = new Avalonia.Controls.Primitives.UniformGrid { Columns = columns, Margin = new Thickness(10, 0) };
            foreach (var trip in s.Trips) grid.Children.Add(Card(s, trip).Margin(6));
            page.Children.Add(grid);
        }
        if (s.Trips.Count == 0) page.Children.Add(Welcome(s).Margin(16, 0));
        var sample = Ui.Button(s.Strings["ExploreSample"], "sparkles", s.Trips.Count == 0 ? "primary" : "soft");
        sample.Click += async (_, _) => { sample.IsEnabled = false; await s.OpenAsync(await s.CreateSampleAsync()); };
        page.Children.Add(sample.Also(b => b.HorizontalAlignment = HorizontalAlignment.Stretch).Margin(16, 8, 16, 8));
        // A trip someone shares on GitHub comes to the phone from here.
        page.Children.Add(Ui.Text(s.Strings["SharedTrips"], "overline").Margin(20, 8, 20, 0));
        if (s.GitHubLogin is null || s.SignInCode is not null) page.Children.Add(GitHubView.Account(s).Margin(16, 0, 16, 24));
        else page.Children.Add(GitHubView.FindTrips(s).Margin(16, 0, 16, 24));
        // On wide screens the list keeps a readable width in the middle.
        if (width >= WidthClass.Tablet) { page.MaxWidth = 1100; page.HorizontalAlignment = HorizontalAlignment.Center; }
        return Ui.Scroll(page);
    }

    /// <summary>Before the first trip: the mark on a generated cover and what the app is for, like the desktop's welcome.</summary>
    private static Control Welcome(MobileSession s)
    {
        var mark = new Image { Width = 64, Height = 64, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        mark.Bind(Image.SourceProperty, mark.GetResourceObservable("Brand.Mark"));
        var tile = new Border { Width = 100, Height = 100, CornerRadius = new CornerRadius(26), Child = mark, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
            .Res(Border.BackgroundProperty, "Bg.Surface");
        var art = Covers.Generated("Jourfold", double.NaN, 180, 16).Also(c => c.HorizontalAlignment = HorizontalAlignment.Stretch);
        return Ui.V(14, new Panel { Children = { art, tile } },
            Ui.Text(s.Strings["WelcomeTitle"], "h2").Also(t => { t.TextAlignment = TextAlignment.Center; t.TextWrapping = TextWrapping.Wrap; }),
            Ui.Text(s.Strings["WelcomePhoneBody"], "muted").Also(t => { t.TextAlignment = TextAlignment.Center; t.TextWrapping = TextWrapping.Wrap; }).Margin(8, 0));
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
