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
        // The app's settings, such as the name and email versions are saved under, are here as well as under More in a
        // trip, where most people would not look before opening one.
        var settings = Ui.IconButton("settings", s.Strings["Settings"]); settings.Click += (_, _) => s.ShowPage("settings");
        var top = Ui.Columns("*,Auto", logo.Also(l => l.VerticalAlignment = VerticalAlignment.Center), settings.Also(b => b.VerticalAlignment = VerticalAlignment.Center).Col(1));
        var page = Ui.V(14, top.Margin(20, 10, 8, 0));
        if (UpdateNotice(s) is { } update) page.Children.Add(update.Margin(16, 0));
        page.Children.Add(Ui.Text(s.Strings["Trips"], "h1").Margin(20, 0));
        // Something shared from another app waits for the trip it belongs to.
        if (s.Incoming is { } shared)
        {
            var what = shared.Files.Count > 0 ? string.Join(", ", shared.Files.Select(f => f.Name)) : (shared.Lines.FirstOrDefault() ?? shared.Text ?? "");
            var cancel = Ui.Button(s.Strings["Cancel"], null, "ghost", "compact"); cancel.Click += (_, _) => s.DropIncoming();
            page.Children.Add(Ui.Card(Ui.V(6, Ui.Text(s.Strings["ChooseTripForShare"], "strong"), Ui.Text(what, "caption").Also(t => t.TextWrapping = TextWrapping.Wrap), cancel.Also(b => b.HorizontalAlignment = HorizontalAlignment.Left)), 14)
                .Also(c => c.BorderThickness = new Thickness(1)).Res(Border.BorderBrushProperty, "Accent").Margin(16, 0));
        }
        // Until the folders are read, the page stays empty below its title: showing the welcome for a moment and then
        // the trips would flicker.
        if (!s.TripsLoaded) return Ui.Scroll(page);
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
        // A trip someone shares on GitHub comes to the phone from here, once it is known whether the user is signed in.
        if (s.GitHubLoaded)
        {
            page.Children.Add(Ui.Text(s.Strings["SharedTrips"], "overline").Margin(20, 8, 20, 0));
            if (s.GitHubLogin is null || s.SignInCode is not null) page.Children.Add(GitHubView.Account(s).Margin(16, 0, 16, 24));
            else page.Children.Add(GitHubView.FindTrips(s).Margin(16, 0, 16, 24));
        }
        // On wide screens the list keeps a readable width in the middle.
        if (width >= WidthClass.Tablet) page.MaxWidth = 1100;
        return Ui.Scroll(page);
    }

    /// <summary>
    /// A newer Jourfold on GitHub: its release page, skipping just this version, or no longer looking. After the last,
    /// the card says once where to turn it back on.
    /// </summary>
    private static Control? UpdateNotice(MobileSession s)
    {
        var str = s.Strings;
        Button Plain(string text, Action click) { var b = Ui.Button(text, null, "ghost", "compact").Also(b => b.HorizontalAlignment = HorizontalAlignment.Left); b.Click += (_, _) => click(); return b; }
        if (s.UpdatesJustStopped)
            return Ui.Card(Ui.V(6, Ui.Text(str["UpdateStopped"], "caption").Also(t => t.TextWrapping = TextWrapping.Wrap), Plain(str["Close"], s.DismissUpdatesStopped)), 14);
        if (s.UpdateOffer is not { } release) return null;
        var view = Ui.Button(str["UpdateView"], "external-link", "soft").Also(b => b.HorizontalAlignment = HorizontalAlignment.Stretch);
        view.Click += async (_, _) => { if (TopLevel.GetTopLevel(view)?.Launcher is { } launcher) await launcher.LaunchUriAsync(release.Page); };
        var card = Ui.Card(Ui.V(8,
            Ui.Columns("Auto,*", Ui.Icon("sparkles", 20, "Info").Also(i => i.VerticalAlignment = VerticalAlignment.Top).Margin(0, 2, 12, 0),
                Ui.V(2, Ui.Text(string.Format(CultureInfo.CurrentCulture, str["UpdateAvailable"], release.Title), "strong").Also(t => t.TextWrapping = TextWrapping.Wrap),
                    Ui.Text(string.Format(CultureInfo.CurrentCulture, str["UpdateInstalled"], s.AppVersion), "caption"))),
            view,
            Ui.V(0, Plain(str["UpdateSkip"], () => s.SkipUpdate(release)), Plain(str["UpdateStop"], () => s.SetUpdateChecks(false, fromNotice: true)))), 14)
            .Also(c => c.BorderThickness = new Thickness(1)).Res(Border.BorderBrushProperty, "Info");
        AutomationProperties.SetName(card, string.Format(CultureInfo.CurrentCulture, str["UpdateAvailable"], release.Title));
        return card;
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
