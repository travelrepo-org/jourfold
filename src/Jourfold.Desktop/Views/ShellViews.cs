using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Jourfold.Application;
using NodaTime;
using TravelRepo.Core;
using TravelRepo.Repository;

namespace Jourfold.Desktop;

/// <summary>Left navigation for an open trip.</summary>
public static class SidebarView
{
    public static readonly IReadOnlyList<string> Primary = ["Plan", "Map", "Bookings", "Costs", "Tasks", "Files", "People", "Places", "Collections"];
    public static readonly IReadOnlyList<string> Secondary = ["History", "Variants"];

    public static Control Build(MainWindow w)
    {
        var m = w.Model; var s = m.Strings; var trip = m.Trip!;
        var root = new DockPanel { LastChildFill = true };
        var logo = new Image { Source = Brand(w), Height = 30, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(20, 20, 20, 14) };
        AutomationProperties.SetName(logo, "Jourfold"); DockPanel.SetDock(logo, Dock.Top); root.Children.Add(logo);

        var tripButton = new Button { Content = TripBadge(w), HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(10, 0, 10, 10) }.Classed("row").Named(s["TripMenu"]);
        tripButton.Flyout = TripMenu(w); DockPanel.SetDock(tripButton, Dock.Top); root.Children.Add(tripButton);
        var search = new Button { Margin = new Thickness(10, 0, 10, 8), HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch, FontWeight = FontWeight.Normal, Content = Ui.Columns("Auto,*,Auto", Ui.Icon("search", 15, "Text.Subtle"), Ui.Line(s["SearchPlaceholder"], "subtle").Margin(8, 0), new Border { Child = Ui.Text("Ctrl K", "caption").Also(t => t.FontSize = 11), Padding = new Thickness(6, 1), CornerRadius = new CornerRadius(4) }.Res(Border.BackgroundProperty, "Bg.Subtle")) }.Named(s["SearchTitle"]);
        search.Res(Button.BackgroundProperty, "Bg.Surface"); search.Command = m.SearchCommand; DockPanel.SetDock(search, Dock.Top); root.Children.Add(search);

        var footer = Ui.V(2); footer.Margin = new Thickness(10, 6, 10, 12); DockPanel.SetDock(footer, Dock.Bottom);
        var inbox = NavButton(s["Inbox"], "inbox", m.InboxOpen, m.InboxItems.Count > 0 ? m.InboxItems.Count : null);
        inbox.Click += (_, _) => { m.InboxOpen = !m.InboxOpen; if (!m.InboxOpen) m.InboxPinned = false; };
        ToolTip.SetTip(inbox, s["InboxTip"]); footer.Children.Add(inbox);
        footer.Children.Add(SyncRow(w));
        var settings = NavButton(s["Settings"], "settings", false); settings.Command = m.SettingsCommand; footer.Children.Add(settings);
        root.Children.Add(footer);

        var nav = Ui.V(2); nav.Margin = new Thickness(10, 4);
        nav.Children.Add(Ui.Text(s["PlanSection"].ToUpperInvariant(), "overline").Margin(10, 6, 0, 6));
        foreach (var key in Primary) nav.Children.Add(Nav(w, key));
        nav.Children.Add(Ui.Text(s["VersionsSection"].ToUpperInvariant(), "overline").Margin(10, 14, 0, 6));
        foreach (var key in Secondary) nav.Children.Add(Nav(w, key));
        root.Children.Add(new ScrollViewer { Content = nav });
        return root;
    }

    public static IImage Brand(Control c) => (IImage)c.FindResource(c.ActualThemeVariant == ThemeVariant.Dark || (c is MainWindow mw && mw.HighContrast && c.ActualThemeVariant == ThemeVariant.Dark) ? "Brand.Lockup.Dark" : "Brand.Lockup.Light")!;

    private static Button Nav(MainWindow w, string key)
    {
        var m = w.Model; var trip = m.Trip!;
        int? count = key switch
        {
            "Tasks" => trip.Entities.Values.Count(e => e.Type == "task" && e.Data["status"]?.ToString() is not ("completed" or "cancelled")),
            "Bookings" => trip.Entities.Values.Count(e => e.Type == "booking"),
            "Files" => trip.Entities.Values.Count(e => e.Type == "document"),
            "People" => trip.Entities.Values.Count(e => e.Type == "person"),
            _ => null
        };
        var selected = m.View == key || key == "Plan" && m.View == "List";
        var button = NavButton(m.Strings[key], MainViewModel.NavIcon(key), selected, count is > 0 ? count : null, subtle: true);
        button.Click += (_, _) => m.Navigate(key);
        return button;
    }

    public static Button NavButton(string label, string icon, bool selected, int? count = null, bool subtle = false)
    {
        var badge = count is { } n ? new Border { Child = Ui.Text(n.ToString(System.Globalization.CultureInfo.CurrentCulture)), Classes = { "badge" } } : null;
        if (badge is not null && subtle) { badge.Res(Border.BackgroundProperty, selected ? "Accent" : "Bg.Muted"); ((TextBlock)badge.Child!).Res(TextBlock.ForegroundProperty, selected ? "Text.OnAccent" : "Text.Muted"); }
        var content = Ui.Columns("Auto,*,Auto", Ui.Icon(icon, 17), Ui.Line(label).Margin(10, 0, 6, 0), badge);
        var button = new Button { Content = content }.Classed("nav");
        if (selected) button.Classes.Add("selected");
        AutomationProperties.SetName(button, count is { } c ? label + ", " + c : label);
        return button;
    }

    private static Control TripBadge(MainWindow w)
    {
        var m = w.Model; var trip = m.Trip!; var (start, end) = TripQueries.EffectiveDates(trip);
        var meta = Formats.DateRange(start, end);
        return Ui.Columns("Auto,*,Auto", Cover(m, trip, 40), Ui.V(1, Ui.Line(trip.Manifest.Title, "title"), Ui.Line(meta.Length > 0 ? meta : m.Strings["NoDatesYet"], "caption")).Margin(10, 0, 4, 0), Ui.Icon("chevron-down", 14, "Text.Subtle"));
    }

    /// <summary>Trip cover thumbnail, or a calm generated fallback with the trip's initials.</summary>
    public static Control Cover(MainViewModel m, TripSnapshot trip, double size, double? height = null, double radius = 8)
    {
        var h = height ?? size;
        if (CoverImage(m.Workspace?.Repository ?? null, trip) is { } bitmap)
            return new Border { Width = size, Height = h, CornerRadius = new CornerRadius(radius), ClipToBounds = true, Child = new Image { Source = bitmap, Stretch = Stretch.UniformToFill } };
        return GeneratedCover(trip.Manifest.Title, size, h, radius);
    }
    public static Bitmap? CoverImage(TravelRepository? repository, TripSnapshot trip)
    {
        if (repository is null || !Guid.TryParse(trip.Manifest.Data["cover"]?["document"]?.ToString(), out var id) || trip.Find(id) is not { } doc) return null;
        try { var path = repository.DocumentPath(doc); return File.Exists(path) ? Bitmap.DecodeToWidth(File.OpenRead(path), 640) : null; }
        catch (Exception ex) when (ex is DomainException or IOException or ArgumentException or NotSupportedException) { return null; }
    }
    private static readonly (string A, string B)[] Gradients = [("#0F7B7A", "#45C4AF"), ("#103B5C", "#1593A5"), ("#1B4C8A", "#5B9BEA"), ("#4C3796", "#9C82EA"), ("#1C653C", "#4FC07E"), ("#844210", "#E2843A"), ("#842B58", "#DD6FA5")];
    public static Control GeneratedCover(string title, double width, double height, double radius = 8)
    {
        var hash = 0; foreach (var ch in title) hash = unchecked(hash * 31 + ch);
        var (a, b) = Gradients[Math.Abs(hash % Gradients.Length)];
        var brush = new LinearGradientBrush { StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative), GradientStops = { new GradientStop(Color.Parse(a), 0), new GradientStop(Color.Parse(b), 1) } };
        // Soft rolling hills echo the route-and-landscape idea of the mark without repeating the logo.
        var hills = new Avalonia.Controls.Shapes.Path { Data = Geometry.Parse("M0,70 C25,52 45,60 60,66 C75,72 85,58 100,50 L100,100 L0,100 Z"), Fill = new SolidColorBrush(Colors.White, 0.16), Stretch = Stretch.Fill, VerticalAlignment = VerticalAlignment.Bottom, Height = height * 0.55 };
        var hills2 = new Avalonia.Controls.Shapes.Path { Data = Geometry.Parse("M0,80 C20,70 40,86 65,74 C80,67 90,76 100,72 L100,100 L0,100 Z"), Fill = new SolidColorBrush(Colors.White, 0.14), Stretch = Stretch.Fill, VerticalAlignment = VerticalAlignment.Bottom, Height = height * 0.4 };
        var initials = new TextBlock { Text = Ui.Initials(title), Foreground = Brushes.White, FontWeight = FontWeight.Bold, FontSize = Math.Max(11, Math.Min(width, height) * 0.32), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Opacity = width > 120 ? 0 : 0.95 };
        return new Border { Width = width, Height = height, CornerRadius = new CornerRadius(radius), ClipToBounds = true, Background = brush, Child = new Panel { Children = { hills, hills2, initials } } };
    }

    private static MenuFlyout TripMenu(MainWindow w)
    {
        var m = w.Model; var s = m.Strings; var menu = new MenuFlyout { Placement = PlacementMode.BottomEdgeAlignedLeft };
        MenuItem Item(string text, string icon, Action action) { var item = new MenuItem { Header = text, Icon = Ui.Icon(icon, 16) }; item.Click += (_, _) => action(); return item; }
        menu.Items.Add(Item(s["AllTrips"], "layout-grid", w.CloseTrip));
        foreach (var recent in m.Recent.Where(r => r.Path != m.Workspace?.Repository.Root).Take(5)) menu.Items.Add(Item(recent.Title, "map", () => m.OpenRecent(recent)));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item(s["TripDetails"], "info", () => m.Selected = m.Trip!.Manifest));
        menu.Items.Add(Item(s["SaveACopy"], "copy", () => m.SaveAsCommand.Execute(null)));
        menu.Items.Add(Item(s["Export"], "download", () => m.ExportCommand.Execute(null)));
        menu.Items.Add(Item(s["ShowInFolder"], "folder-open", () => m.Interaction.Open(m.Workspace!.Repository.Root)));
        return menu;
    }

    private static Control SyncRow(MainWindow w)
    {
        var m = w.Model; var s = m.Strings;
        var (icon, brush) = m.SyncKey switch
        {
            "SyncSynced" => ("cloud", "Success"),
            "Syncing" => ("refresh-cw", "Accent"),
            "SyncFailed" or "SyncDiverged" => ("triangle-alert", "Warning"),
            "SyncRemoteChanges" => ("download", "Info"),
            "SyncLocalChanges" => ("upload", "Text.Muted"),
            _ => ("monitor", "Text.Muted")
        };
        var detail = m.SyncKey == "SyncLocalOnly" ? s["SyncLocalOnlyHint"] : m.Store.Get("synced:" + m.Workspace?.Repository.Root) is { } when && DateTimeOffset.TryParse(when, out var at) ? string.Format(s["LastSynced"], Formats.Relative(at, s)) : s["SyncNow"];
        var content = Ui.Columns("Auto,*", Ui.Icon(icon, 17, brush), Ui.V(0, Ui.Line(s[m.SyncKey], "strong"), Ui.Line(detail, "caption")).Margin(10, 0, 0, 0));
        var button = new Button { Content = content }.Classed("nav");
        button.Click += (_, _) => { if (m.SyncKey == "SyncLocalOnly") m.ShareCommand.Execute(null); else m.SyncCommand.Execute(null); };
        AutomationProperties.SetName(button, s[m.SyncKey] + ". " + detail);
        return button;
    }
}

/// <summary>Trip header: title, current variant, save state and primary actions.</summary>
public static class HeaderView
{
    public static Control Build(MainWindow w)
    {
        var m = w.Model; var s = m.Strings; var trip = m.Trip!;
        var title = new TextBlock { Text = trip.Manifest.Title, Classes = { "h2" }, FontSize = 20, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        var titleButton = new Button { Content = title, Padding = new Thickness(4, 0), MinHeight = 0 }.Classed("ghost").Named(s["TripDetails"]);
        titleButton.Click += (_, _) => m.Selected = trip.Manifest;

        var variant = new Button { Content = Ui.H(6, Ui.Icon("git-branch", 14), Ui.Text(m.VariantTitle).Also(t => t.FontWeight = FontWeight.SemiBold), m.Advanced && m.CurrentBranch.Length > 0 ? Ui.Text(m.CurrentBranch, "caption") : null, Ui.Icon("chevron-down", 13)) }.Classed("chip", "soft").Named(s["CurrentVariant"]);
        variant.Flyout = VariantMenu(w);

        var (start, end) = TripQueries.EffectiveDates(trip);
        var dates = Formats.DateRange(start, end);
        var zone = trip.Manifest.Data["default_timezone"]?.ToString();
        var saveText = m.Saving ? s["Saving"] : m.HasUncommitted ? s["NotInVersion"] : s["SavedInVersion"];
        var saveIcon = m.Saving ? "refresh-cw" : m.HasUncommitted ? "circle-dot" : "circle-check";
        var save = Ui.H(5, Ui.Icon(saveIcon, 13, m.HasUncommitted ? "Warning" : "Success"), Ui.Text(saveText, "caption"));
        ToolTip.SetTip(save, m.HasUncommitted ? s["NotInVersionTip"] : s["SavedTip"]);
        var meta = Ui.H(10,
            dates.Length > 0 ? Ui.H(5, Ui.Icon("calendar", 13, "Text.Subtle"), Ui.Text(dates, "caption")) : null,
            zone is { Length: > 0 } ? Ui.H(5, Ui.Icon("globe", 13, "Text.Subtle"), Ui.Text(Formats.ZoneName(zone), "caption")) : null,
            m.IsSample ? Ui.Pill(s["SampleTrip"], "sparkles", "Info.Soft", "Info") : null,
            save);
        var left = Ui.H(10, titleButton, variant);

        var people = trip.Entities.Values.Where(e => e.Type == "person").Select(p => p.Title).ToArray();
        var avatars = new Button { Content = people.Length > 0 ? Ui.AvatarStack(people, 28) : Ui.Icon("user-plus", 16), Padding = new Thickness(4) }.Classed("ghost").Named(people.Length > 0 ? string.Join(", ", people) : s["AddPeople"]);
        avatars.Click += (_, _) => m.Navigate("People");


        var share = Ui.Button(m.SharingLabel, m.ShareKey == "Invite" ? "user-plus" : "share-2"); share.Command = m.ShareCommand;
        var version = Ui.Button(s["Version"], "save"); version.Command = m.CreateVersionCommand; ToolTip.SetTip(version, s["VersionTip"]);
        if (m.HasUncommitted) version.Classes.Add("soft");
        var add = Ui.Button(s["Add"], "plus", "primary"); add.Command = m.QuickAddCommand; ToolTip.SetTip(add, s["QuickAdd"] + " (Ctrl+N)");
        var more = Ui.IconButton("ellipsis", s["More"]); more.Flyout = MoreMenu(w);
        var right = Ui.H(8, avatars, share, version, add, more);
        left.ClipToBounds = true; title.MaxWidth = 420;
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), RowDefinitions = new RowDefinitions("Auto,Auto") };
        grid.Children.Add(left); grid.Children.Add(right.Margin(12, 0, 0, 0).Col(1));
        grid.Children.Add(meta.Margin(4, 4, 0, 0).Row(1).Col(0, 2));
        return new Border { Child = grid, Padding = new Thickness(20, 12, 16, 12) };
    }

    private static MenuFlyout VariantMenu(MainWindow w)
    {
        var m = w.Model; var s = m.Strings; var menu = new MenuFlyout();
        foreach (var v in m.KnownVariants.Where(v => !v.IsRemote && v.Manifest.Data["variant"]?["state"]?.ToString() != "archived"))
        {
            var current = v.Branch == m.CurrentBranch; var item = new MenuItem { Header = v.Manifest.Data["variant"]?["title"]?.ToString() ?? v.Branch, Icon = Ui.Icon(current ? "check" : "git-branch", 15) };
            if (!current) item.Click += (_, _) => _ = m.VariantActionAsync(v, "Switch"); else item.FontWeight = FontWeight.SemiBold;
            menu.Items.Add(item);
        }
        if (menu.Items.Count > 0) menu.Items.Add(new Separator());
        var create = new MenuItem { Header = s["NewVariant"], Icon = Ui.Icon("plus", 15) }; create.Click += (_, _) => m.NewVariantCommand.Execute(null); menu.Items.Add(create);
        var manage = new MenuItem { Header = s["ManageVariants"], Icon = Ui.Icon("git-compare", 15) }; manage.Click += (_, _) => m.Navigate("Variants"); menu.Items.Add(manage);
        return menu;
    }

    private static MenuFlyout MoreMenu(MainWindow w)
    {
        var m = w.Model; var s = m.Strings; var menu = new MenuFlyout();
        void Item(string text, string icon, Action action, string? gesture = null, bool enabled = true) { var item = new MenuItem { Header = text, Icon = Ui.Icon(icon, 16), IsEnabled = enabled, InputGesture = gesture is null ? null : Avalonia.Input.KeyGesture.Parse(gesture) }; item.Click += (_, _) => action(); menu.Items.Add(item); }
        Item(s["Undo"], "undo-2", () => m.UndoCommand.Execute(null), "Ctrl+Z", m.Workspace?.CanUndo == true);
        Item(s["Redo"], "redo-2", () => m.RedoCommand.Execute(null), "Ctrl+Shift+Z", m.Workspace?.CanRedo == true);
        menu.Items.Add(new Separator());
        Item(s["SyncNow"], "refresh-cw", () => m.SyncCommand.Execute(null), "F5");
        Item(s["AddFile"], "paperclip", () => m.AddFileCommand.Execute(null));
        Item(s["Export"], "download", () => m.ExportCommand.Execute(null));
        Item(s["SaveACopy"], "copy", () => m.SaveAsCommand.Execute(null));
        Item(s["TripDetails"], "info", () => m.Selected = m.Trip!.Manifest);
        menu.Items.Add(new Separator());
        Item(s["CloseTrip"], "log-out", w.CloseTrip);
        return menu;
    }

    public static Control PrivacyBanner(MainWindow w)
    {
        var s = w.Model.Strings;
        return new Border { Padding = new Thickness(20, 10), BorderThickness = new Thickness(0, 0, 0, 1), Child = Ui.Columns("Auto,*", Ui.Icon("triangle-alert", 18, "Warning"), Ui.Text(w.Model.PrivacyWarning).Res(TextBlock.ForegroundProperty, "Text").Margin(12, 0, 0, 0)) }
            .Res(Border.BackgroundProperty, "Warning.Soft").Res(Border.BorderBrushProperty, "Warning.Line");
    }
    public static Control MergeNotice(MainWindow w)
    {
        var m = w.Model; var s = m.Strings;
        var review = Ui.Button(s["Review"], null, "compact"); review.Command = m.ReviewMergeCommand;
        var cancel = Ui.Button(s["Cancel"], null, "compact", "ghost"); cancel.Command = m.CancelMergeCommand;
        return new Border { Padding = new Thickness(20, 8), Child = Ui.Columns("Auto,*,Auto", Ui.Icon("git-merge", 18, "Info"), Ui.Text(m.Notice).Margin(12, 0), Ui.H(6, review, cancel)) }.Res(Border.BackgroundProperty, "Info.Soft");
    }
}

/// <summary>Per-view toolbar under the header.</summary>
public static class ViewToolbar
{
    public static Control Build(MainWindow w)
    {
        var m = w.Model; var s = m.Strings;
        if (m.View is "Plan" or "List" or "Map")
        {
            var segments = Segmented([("Plan", s["Timetable"], "calendar-days"), ("List", s["List"], "list"), ("Map", s["Map"], "map")], m.View, key => m.Navigate(key));
            var left = Ui.H(12, segments);
            if (m.View == "Plan")
            {
                var today = Ui.Button(s["Today"], null, "compact"); ToolTip.SetTip(today, s["TodayTip"]); today.Click += (_, _) => m.ShowDate(SystemClock.Instance.GetCurrentInstant().InZone(ScheduleQueries.TripZone(m.Trip!)).Date);
                var step = Math.Max(1, m.VisibleDays);
                var previous = Ui.IconButton("chevron-left", s["Previous"], "small"); previous.Click += (_, _) => m.ShowDate(m.StartDate.PlusDays(-step));
                var next = Ui.IconButton("chevron-right", s["Next"], "small"); next.Click += (_, _) => m.ShowDate(m.StartDate.PlusDays(step));
                var range = Formats.DateRange(m.StartDate, m.StartDate.PlusDays(step - 1));
                var picker = new Button { Content = Ui.H(6, Ui.Text(range).Also(t => t.FontWeight = FontWeight.SemiBold), Ui.Icon("chevron-down", 13)) }.Classed("ghost", "compact").Named(s["ChooseDate"]);
                var calendar = new Calendar { SelectedDate = m.StartDate.ToDateTimeUnspecified(), DisplayDate = m.StartDate.ToDateTimeUnspecified() };
                var flyout = new Flyout { Content = calendar }; picker.Flyout = flyout;
                calendar.SelectedDatesChanged += (_, _) => { if (calendar.SelectedDate is { } d) { flyout.Hide(); m.ShowDate(LocalDate.FromDateTime(d)); } };
                left.Children.Add(Ui.H(2, previous, picker, next)); left.Children.Add(today);
                var zoomOut = Ui.IconButton("zoom-out", s["ZoomOut"], "small"); zoomOut.IsEnabled = m.Zoom > .5; zoomOut.Click += (_, _) => m.SetZoom(m.Zoom - .25);
                var zoomIn = Ui.IconButton("zoom-in", s["ZoomIn"], "small"); zoomIn.IsEnabled = m.Zoom < 2; zoomIn.Click += (_, _) => m.SetZoom(m.Zoom + .25);
                var lanes = Ui.Button(s["ByPerson"], "users", "chip"); if (m.Lanes) lanes.Classes.Add("selected"); lanes.Click += (_, _) => m.SetLanes(!m.Lanes); ToolTip.SetTip(lanes, s["ByPersonTip"]);
                var zone = Ui.Icon("globe", 15, "Text.Subtle"); ToolTip.SetTip(zone, string.Format(s["TimesIn"], ScheduleQueries.TripZone(m.Trip!).Id)); Avalonia.Automation.AutomationProperties.SetName(zone, string.Format(s["TimesIn"], ScheduleQueries.TripZone(m.Trip!).Id));
                return Bar(Ui.Columns("*,Auto", left.Also(l => l.ClipToBounds = true), Ui.H(8, zone, lanes, Ui.H(0, zoomOut, zoomIn))));
            }
            return Bar(left);
        }
        return new Border { Height = 0 };
    }

    private static Border Bar(Control content) => new Border { Child = content, Padding = new Thickness(16, 10) }.Res(Border.BorderBrushProperty, "Line").Also(b => b.BorderThickness = new Thickness(0, 0, 0, 1));

    public static Control Segmented(IEnumerable<(string Key, string Label, string? Icon)> items, string selected, Action<string> select)
    {
        var panel = new WrapPanel { Orientation = Orientation.Horizontal, ItemSpacing = 2, LineSpacing = 2 };
        foreach (var (key, label, icon) in items)
        {
            var button = Ui.Button(label, icon, "segment"); if (key == selected) button.Classes.Add("selected");
            button.Click += (_, _) => select(key); panel.Children.Add(button);
        }
        return new Border { Child = panel }.Classed("segments");
    }
}
