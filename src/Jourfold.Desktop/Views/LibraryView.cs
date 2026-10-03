using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Jourfold.Application;
using Jourfold.Infrastructure;
using TravelRepo.Core;
using TravelRepo.Repository;

namespace Jourfold.Desktop;

/// <summary>The start screen: the local trip library. Derived from local state and the trip folders themselves.</summary>
public static class LibraryView
{
    private sealed record CardData(TripSnapshot Trip, TravelRepository Repository, DateTime Changed);
    private static readonly Dictionary<string, (DateTime Stamp, CardData Data)> Cache = new();
    private static string filter = "All";
    private static string query = "";

    public static Control Build(MainWindow w)
    {
        var m = w.Model; var s = m.Strings;
        var logo = new Image { Source = SidebarView.Brand(w), Height = 30, HorizontalAlignment = HorizontalAlignment.Left }; AutomationProperties.SetName(logo, "Jourfold");
        var searchBox = new TextBox { Watermark = s["SearchTrips"], Width = 320, Text = query }.Classed("search"); AutomationProperties.SetName(searchBox, s["SearchTrips"]);
        var search = new Panel { Children = { searchBox, Ui.Icon("search", 15, "Text.Subtle").Also(i => { i.HorizontalAlignment = HorizontalAlignment.Left; i.Margin = new Thickness(11, 0, 0, 0); }) } };
        var settings = Ui.IconButton("settings", s["Settings"]); settings.Command = m.SettingsCommand;
        var open = Ui.Button(s["Open"], "folder-open"); open.Flyout = OpenMenu(m);
        var create = Ui.Button(s["NewTrip"], "plus", "primary"); create.Command = m.NewTripCommand;
        var top = new Border { Padding = new Thickness(28, 16), BorderThickness = new Thickness(0, 0, 0, 1), Child = Ui.Columns("*,Auto,*", logo, search, Ui.H(8, settings, open, create).Also(h => h.HorizontalAlignment = HorizontalAlignment.Right)) }
            .Res(Border.BackgroundProperty, "Bg.Surface").Res(Border.BorderBrushProperty, "Line");

        var trips = m.Recent.ToArray();
        var body = Ui.V(20); body.Margin = new Thickness(40, 32, 40, 40); body.MaxWidth = 1320;
        if (trips.Length == 0) { body.Children.Add(Welcome(w)); }
        else
        {
            body.Children.Add(Ui.Columns("*,Auto",
                Ui.V(4, Ui.Text(s["YourTrips"], "h1"), Ui.Text(s["LibraryHint"], "muted")),
                ViewToolbar.Segmented([("All", s["AllTrips"], null), ("Local", s["OnThisComputer"], "monitor"), ("Remote", s["Shared"], "cloud")], filter, key => { filter = key; w.Render(); }).Also(c => c.VerticalAlignment = VerticalAlignment.Bottom)));
            var grid = new WrapPanel { Orientation = Orientation.Horizontal, ItemSpacing = 20, LineSpacing = 20 };
            void Fill()
            {
                grid.Children.Clear();
                var visible = trips.Where(t => filter == "All" || (filter == "Remote") == (t.State == "remote")).Where(t => query.Length == 0 || t.Title.Contains(query, StringComparison.CurrentCultureIgnoreCase)).ToArray();
                foreach (var trip in visible) grid.Children.Add(Card(w, trip));
                if (filter == "All" && query.Length == 0) grid.Children.Add(NewCard(w));
                if (visible.Length == 0 && query.Length > 0) grid.Children.Add(Ui.Text(string.Format(s["NoTripsMatch"], query), "muted"));
            }
            Fill();
            searchBox.TextChanged += (_, _) => { query = searchBox.Text ?? ""; Fill(); };
            body.Children.Add(grid);
        }
        var scroll = new ScrollViewer { Content = new Border { Child = body, HorizontalAlignment = HorizontalAlignment.Center } };
        return new DockPanel { Children = { top.Also(t => DockPanel.SetDock(t, Dock.Top)), scroll } };
    }

    private static MenuFlyout OpenMenu(MainViewModel m)
    {
        var s = m.Strings; var menu = new MenuFlyout();
        void Item(string text, string icon, string? hint, Action action) { var item = new MenuItem { Header = Ui.V(0, Ui.Text(text).Also(t => t.FontWeight = FontWeight.Medium), hint is null ? null : Ui.Text(hint, "caption")), Icon = Ui.Icon(icon, 16) }; item.Click += (_, _) => action(); menu.Items.Add(item); }
        Item(s["OpenTripFolder"], "folder-open", s["OpenTripFolderHint"], () => m.OpenTripCommand.Execute(null));
        Item(s["Clone"], "link", s["CloneHint"], () => m.CloneCommand.Execute(null));
        Item(s["DiscoverGitHub"], "cloud", s["DiscoverGitHubHint"], () => m.DiscoverGitHubCommand.Execute(null));
        Item(s["ExploreSample"], "sparkles", s["ExploreSampleHint"], () => m.OpenSampleCommand.Execute(null));
        return menu;
    }

    private static Control Welcome(MainWindow w)
    {
        var m = w.Model; var s = m.Strings;
        var art = SidebarView.GeneratedCover("Jourfold", 520, 200, 16);
        var mark = new Image { Source = (IImage)w.FindResource("Brand.Mark")!, Width = 72, Height = 72, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var hero = new Panel { Children = { art, new Border { Width = 112, Height = 112, CornerRadius = new CornerRadius(28), Child = mark, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }.Res(Border.BackgroundProperty, "Bg.Surface") }, HorizontalAlignment = HorizontalAlignment.Center };
        var create = Ui.Button(s["CreateFirstTrip"], "plus", "primary"); create.Command = m.NewTripCommand;
        var sample = Ui.Button(s["ExploreSample"], "sparkles", "soft"); sample.Command = m.OpenSampleCommand;
        var open = Ui.Button(s["OpenTripFolder"], "folder-open", "ghost"); open.Command = m.OpenTripCommand;
        var steps = Ui.Columns("*,*,*", Step("calendar-days", s["WelcomeStep1"], s["WelcomeStep1Body"]), Step("git-branch", s["WelcomeStep2"], s["WelcomeStep2Body"]).Margin(16, 0), Step("share-2", s["WelcomeStep3"], s["WelcomeStep3Body"]));
        steps.MaxWidth = 860;
        var panel = Ui.V(18, hero, Ui.Text(s["WelcomeTitle"], "h1").Also(t => t.TextAlignment = TextAlignment.Center), Ui.Text(s["WelcomeBody"], "muted").Also(t => { t.TextAlignment = TextAlignment.Center; t.MaxWidth = 560; t.FontSize = 14; }), Ui.H(10, create, sample, open).Also(h => h.HorizontalAlignment = HorizontalAlignment.Center), steps.Margin(0, 24, 0, 0));
        panel.HorizontalAlignment = HorizontalAlignment.Center; panel.Margin = new Thickness(0, 24, 0, 0);
        return panel;
    }
    private static Control Step(string icon, string title, string body) => Ui.Card(Ui.V(8, Ui.Tile(icon, "Activity", 36), Ui.Text(title, "title"), Ui.Text(body, "caption")), 18);

    private static Control NewCard(MainWindow w)
    {
        var s = w.Model.Strings;
        var content = Ui.V(10, new Border { Width = 48, Height = 48, CornerRadius = new CornerRadius(24), Child = Ui.Icon("plus", 22, "Accent").Also(i => i.HorizontalAlignment = HorizontalAlignment.Center), HorizontalAlignment = HorizontalAlignment.Center }.Res(Border.BackgroundProperty, "Accent.Soft"),
            Ui.Text(s["NewTrip"], "title").Also(t => t.HorizontalAlignment = HorizontalAlignment.Center), Ui.Text(s["NewTripCardHint"], "caption").Also(t => { t.TextAlignment = TextAlignment.Center; t.MaxWidth = 220; }));
        content.VerticalAlignment = VerticalAlignment.Center;
        var button = new Button { Width = 300, Height = 272, Content = content, BorderThickness = new Thickness(1.5) }.Classed("card").Named(s["NewTrip"]);
        button.Res(Button.BorderBrushProperty, "Line.Strong"); button.Command = w.Model.NewTripCommand;
        return button;
    }

    private static Control Card(MainWindow w, RecentTrip recent)
    {
        var m = w.Model; var s = m.Strings;
        var cover = new Panel { Height = 136 };
        var title = Ui.Line(recent.Title, "h3");
        var dates = Ui.Line("", "caption"); var places = Ui.Line("", "muted"); places.FontSize = 12.5;
        var state = recent.State == "remote" ? Ui.H(5, Ui.Icon("cloud", 13, "Text.Subtle"), Ui.Text(s["Shared"], "caption")) : Ui.H(5, Ui.Icon("monitor", 13, "Text.Subtle"), Ui.Text(s["OnThisComputer"], "caption"));
        var opened = DateTimeOffset.TryParse(recent.Opened, out var when) ? string.Format(s["OpenedAgo"], Formats.Relative(when, s)) : "";
        var footer = Ui.Columns("*,Auto", Ui.Line(opened, "caption"), state);
        var body = Ui.V(4, title, dates, places, footer.Margin(0, 8, 0, 0)); body.Margin = new Thickness(16, 12, 16, 14);
        var layout = new Grid { RowDefinitions = new RowDefinitions("Auto,*") }; layout.Children.Add(cover); layout.Children.Add(body.Row(1));
        var button = new Button { Width = 300, Height = 272, Content = new Border { CornerRadius = new CornerRadius(12), ClipToBounds = true, Child = layout } }.Classed("card").Named(recent.Title);
        button.Click += (_, _) => m.OpenRecent(recent);
        var menu = new ContextMenu();
        MenuItem Item(string text, string icon, Action action) { var item = new MenuItem { Header = text, Icon = Ui.Icon(icon, 16) }; item.Click += (_, _) => action(); return item; }
        menu.Items.Add(Item(s["Open"], "folder-open", () => m.OpenRecent(recent)));
        menu.Items.Add(Item(s["ShowInFolder"], "external-link", () => m.Interaction.Open(recent.Path)));
        menu.Items.Add(Item(s["RemoveFromList"], "x", () => m.ForgetRecent(recent)));
        button.ContextMenu = menu;

        cover.Children.Add(SidebarView.GeneratedCover(recent.Title, 300, 136, 0));
        if (!Directory.Exists(recent.Path))
        {
            dates.Text = s["FolderMissing"]; dates.Res(TextBlock.ForegroundProperty, "Warning"); button.IsEnabled = true;
            button.Click += (_, _) => { };
            return button;
        }
        _ = LoadAsync(recent.Path).ContinueWith(task =>
        {
            if (task.Result is not { } data) return;
            Dispatcher.UIThread.Post(() =>
            {
                var (start, end) = TripQueries.EffectiveDates(data.Trip);
                dates.Text = Formats.DateRange(start, end) is { Length: > 0 } range ? range : s["NoDatesYet"];
                places.Text = string.Join(" · ", TripSummaries.PlaceNames(data.Trip, 3)) is { Length: > 0 } names ? names : string.Format(s["ItemsCount"], data.Trip.Entities.Values.Count(e => e.Type == "schedule_item"));
                title.Text = data.Trip.Manifest.Title;
                if (SidebarView.CoverImage(data.Repository, data.Trip) is { } image) { cover.Children.Clear(); cover.Children.Add(new Image { Source = image, Stretch = Stretch.UniformToFill, Height = 136 }); }
                if (SampleTrip.IsSample(data.Trip)) cover.Children.Add(Ui.Pill(s["SampleTrip"], "sparkles", "Bg.Surface", "Text").Margin(12));
                var people = data.Trip.Entities.Values.Where(e => e.Type == "person").Select(p => p.Title).ToArray();
                if (people.Length > 0) cover.Children.Add(Ui.AvatarStack(people, 26).Also(a => { a.HorizontalAlignment = HorizontalAlignment.Right; a.VerticalAlignment = VerticalAlignment.Bottom; a.Margin = new Thickness(12); }));
            });
        }, TaskScheduler.Default);
        return button;
    }

    private static async Task<CardData?> LoadAsync(string path)
    {
        try
        {
            var stamp = LocalStore.LastChanged(path);
            lock (Cache) if (Cache.TryGetValue(path, out var cached) && cached.Stamp == stamp) return cached.Data;
            var repository = new TravelRepository(path); var state = await repository.ReadAsync();
            var data = new CardData(state.Trip, repository, stamp);
            lock (Cache) Cache[path] = (stamp, data);
            return data;
        }
        catch (Exception ex) when (ex is DomainException or IOException or UnauthorizedAccessException) { return null; }
    }
}
