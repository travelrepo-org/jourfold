using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;
using Jourfold.Mobile.Controls;
using Jourfold.Mobile.Views;

namespace Jourfold.Mobile;

/// <summary>Width classes from the design spec. The layout follows the window, not the device.</summary>
public enum WidthClass { Phone, CompactTablet, Tablet, Wide }

/// <summary>
/// The app's frame: the trip list, or the open trip with its four places. Phones get the bottom bar with the plus in
/// the middle; from 600 dp the places move to a rail on the left. It renders again whenever the session changes.
/// </summary>
public sealed class AppShell : UserControl
{
    public MobileSession Session { get; }
    public WidthClass Breakpoint { get; private set; }
    private readonly Border root = new();
    private readonly Border main = new();
    private readonly Panel overlays = new();
    private Sheet? sheet;
    private (Overlay Kind, Guid? Item) sheetFor;
    private Avalonia.Controls.Platform.IInsetsManager? insets;
    private Thickness safeArea;
    private Border? bottomBar;
    // The map stays while its trip is open, so it keeps its position and tiles when the page is drawn again.
    private MapPage? map;
    // Redrawing replaces every control on the page, so a text field being typed in would lose its focus and the
    // keyboard would close. While someone types, redraws that only refresh data wait until the field is left.
    private bool pending, acting, keyboard;
    // A field that was sent with Enter gets the focus back once the page shows what it added.
    private string? refocus;
    private object? drawnAt;
    // The page drawn last and where it was, so drawing the same page again keeps how far it was scrolled.
    private Control? drawnPage;
    private object? pageAt;

    public AppShell(MobileSession session)
    {
        Session = session; Classes.Add("shell");
        session.Strings.Pickers(Resources);
        Content = root; root.Res(Border.BackgroundProperty, "Bg.App");
        root.Child = new Panel { Children = { main, overlays } };
        session.Changed += () => Dispatcher.UIThread.Post(RequestRender);
        // A tap on a button on the page is the user acting, so what follows is shown right away.
        AddHandler(Button.ClickEvent, (_, _) => acting = true, RoutingStrategies.Bubble, handledEventsToo: true);
        AddHandler(GotFocusEvent, (_, e) => { if (e.Source is TextBox) acting = false; }, RoutingStrategies.Bubble, handledEventsToo: true);
        AddHandler(LostFocusEvent, (_, _) => Dispatcher.UIThread.Post(Catch, DispatcherPriority.Background), RoutingStrategies.Bubble, handledEventsToo: true);
        // A new width class changes the layout, and so does crossing the height the timetable needs.
        SizeChanged += (_, e) => { if (Classify(e.NewSize.Width) != Breakpoint || e.PreviousSize.Height >= 600 != e.NewSize.Height >= 600) Render(); };
        // With a mouse, as on Samsung DeX or a tablet with a trackpad, rows and cards show where the pointer is;
        // with a finger they do not, or the last tapped row would stay highlighted.
        AddHandler(PointerMovedEvent, (_, e) => { if (e.Pointer.Type == Avalonia.Input.PointerType.Mouse) Classes.Add("mouse"); }, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerPressedEvent, (_, e) => { if (e.Pointer.Type != Avalonia.Input.PointerType.Mouse) Classes.Remove("mouse"); }, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(KeyDownEvent, OnKey, RoutingStrategies.Bubble);
        // Enter in a one-line field sends it, as its button would, so what it added shows at once.
        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key == Avalonia.Input.Key.Enter && e.Source is TextBox { AcceptsReturn: false, Name: { } name } box && name.StartsWith(MorePages.DraftPrefix, StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(box.Text))
            { acting = true; refocus = name; }
        }, RoutingStrategies.Tunnel, handledEventsToo: true);
        Render();
    }

    /// <summary>
    /// Shortcuts for a hardware keyboard: Ctrl+N adds, Ctrl+1 to Ctrl+4 open the four places, the arrow keys move
    /// between days, Escape goes back. Typing in a field keeps its keys, except Escape.
    /// </summary>
    private void OnKey(object? sender, Avalonia.Input.KeyEventArgs e)
    {
        var ctrl = e.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Control) || e.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Meta);
        if (e.Key == Avalonia.Input.Key.Escape) { OnBack(this, e); e.Handled = true; return; }
        if (e.Source is TextBox || Session.Workspace is null) return;
        switch (e.Key)
        {
            case Avalonia.Input.Key.N when ctrl: Session.ShowQuickAdd(); break;
            case >= Avalonia.Input.Key.D1 and <= Avalonia.Input.Key.D4 when ctrl: Session.ShowTab(Places[e.Key - Avalonia.Input.Key.D1].Tab); break;
            case Avalonia.Input.Key.Left when Session.Showing == Overlay.None && Session.Tab is MobileTab.Plan or MobileTab.Map: Session.StepDay(-1); break;
            case Avalonia.Input.Key.Right when Session.Showing == Overlay.None && Session.Tab is MobileTab.Plan or MobileTab.Map: Session.StepDay(1); break;
            default: return;
        }
        e.Handled = true;
    }

    public static WidthClass Classify(double width) => width switch { < 600 => WidthClass.Phone, < 840 => WidthClass.CompactTablet, < 1200 => WidthClass.Tablet, _ => WidthClass.Wide };

    /// <summary>
    /// Whether the app has drawn its first picture, so the system's starting screen can give way to it without the
    /// empty, black drawing surface showing in between.
    /// </summary>
    public static bool Drawn { get; private set; }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (TopLevel.GetTopLevel(this) is not { } top) return;
        // The frame after the first one: by then the first is on the screen.
        if (!Drawn) top.RequestAnimationFrame(_ => top.RequestAnimationFrame(_ => Drawn = true));
        // Content is drawn edge to edge; the safe area keeps it clear of the status bar, notch and gesture bar.
        if (top.InsetsManager is { } insets)
        {
            insets.DisplayEdgeToEdgePreference = true;
            // Avalonia would pad the whole view by the safe area too, which doubles the space at the top and bottom.
            TopLevel.SetAutoSafeAreaPadding(top, false); TopLevel.SetAutoSafeAreaPadding(this, false);
            // Read again on every render: Avalonia reports the first values before it knows the screen's scaling
            // (in pixels, not dp) and does not report the corrected ones.
            this.insets = insets;
            // Only the padding changes. Drawing the page again would take the focus from a text field, and Android
            // reports changed insets each time the keyboard opens, which would close it right away.
            insets.SafeAreaChanged += (_, _) => Inset();
            top.ScalingChanged += (_, _) => Inset();
        }
        top.BackRequested += OnBack;
        Dispatcher.UIThread.UnhandledException += OnUnhandled;
        // Pages end above the on-screen keyboard and scroll the field being typed in into view. Sheets keep clear of
        // the keyboard themselves.
        if (top.InputPane is { } pane)
            pane.StateChanged += (_, args) =>
            {
                Keyboard(args.NewState == Avalonia.Controls.Platform.InputPaneState.Open, args.EndRect.Height);
                if (args.NewState == Avalonia.Controls.Platform.InputPaneState.Open)
                    Dispatcher.UIThread.Post(() => (top.FocusManager?.GetFocusedElement() as Control)?.BringIntoView(), DispatcherPriority.Background);
                else Dispatcher.UIThread.Post(Catch, DispatcherPriority.Background);
            };
        if (Session.Trips.Count == 0 && Session.Workspace is null) { _ = Session.LoadTripsAsync(); _ = Session.LoadGitHubAsync(); }
    }

    /// <summary>Leaves room for the on-screen keyboard, or takes it back.</summary>
    private void Keyboard(bool open, double height)
    {
        // The bottom bar steps aside while typing, as on other Android apps.
        keyboard = open;
        if (bottomBar is not null) bottomBar.IsVisible = !keyboard;
        // With the bottom bar the page reaches the screen's edge; without it the safe area is already left free.
        var edge = bottomBar is not null ? 0 : safeArea.Bottom;
        main.Margin = new Thickness(0, 0, 0, keyboard ? Math.Max(0, height - edge) : 0);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is { } top) top.BackRequested -= OnBack;
        Dispatcher.UIThread.UnhandledException -= OnUnhandled;
        base.OnDetachedFromVisualTree(e);
    }

    /// <summary>
    /// A tap whose change fails, for example because the trip has errors, shows why instead of ending the app. Buttons
    /// run their changes without waiting, so the failure arrives here.
    /// </summary>
    private void OnUnhandled(object? sender, DispatcherUnhandledExceptionEventArgs e)
    {
        if (e.Handled || !MobileSession.IsExpected(e.Exception)) return;
        Session.ShowProblem(e.Exception); e.Handled = true;
    }

    /// <summary>
    /// The system back gesture: back through the details opened from other details, then out of a page to where it was
    /// opened from, then back to Today, then back to the trip list, then out of the app.
    /// </summary>
    private void OnBack(object? sender, RoutedEventArgs e)
    {
        if (Session.ChangeProblem is not null) { Session.DismissProblem(); e.Handled = true; return; }
        if (Session.Asking is not null || Session.Menu is not null) { Session.CloseOverlay(); e.Handled = true; return; }
        if (Session.TaxiCard is not null) { Session.ShowTaxiCard(null); e.Handled = true; return; }
        if (Session.Viewing.Count > 0) { Session.CloseFile(); e.Handled = true; return; }
        if (Session.Showing != Overlay.None) { if (Session.CanGoBackInDetails) Session.DetailsBack(); else Session.CloseOverlay(); e.Handled = true; return; }
        if (Session.Workspace is null) { if (Session.RemoteTrips is not null) { Session.CloseRemoteTrips(); e.Handled = true; } return; }
        if (Session.GoBack()) { }
        else if (Session.Tab != MobileTab.Today) Session.ShowTab(MobileTab.Today);
        else Session.Close();
        e.Handled = true;
    }

    /// <summary>
    /// Keeps the content inside the safe area. The bottom bar reaches the screen's edge and keeps its buttons above the
    /// gesture bar.
    /// </summary>
    private void Inset()
    {
        safeArea = insets is { DisplaysEdgeToEdge: true } ? insets.SafeAreaPadding : default;
        var bar = Session.Workspace is not null && Breakpoint == WidthClass.Phone;
        root.Padding = new Thickness(safeArea.Left, safeArea.Top, safeArea.Right, bar ? 0 : safeArea.Bottom);
        if (bottomBar is not null) bottomBar.Padding = new Thickness(0, 0, 0, safeArea.Bottom);
    }

    /// <summary>Where the user is. When it changes, the page is drawn at once, even while someone types.</summary>
    private object Location() => (Session.Workspace, Session.Tab, Session.Page, Session.Showing, Session.OverlayItem, Session.SelectedDate, Session.RemoteTrips, Session.SignInCode, Session.Asking, Session.Menu, Session.Viewing.Count, Session.FileProblem, Session.TaxiCard);

    /// <summary>Whether a text field on the page has the focus. Sheets are not redrawn while they are open.</summary>
    private bool Typing() => TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is TextBox box && Avalonia.VisualTree.VisualExtensions.GetVisualAncestors(box).Contains(main);

    private void RequestRender()
    {
        if (!acting && Equals(Location(), drawnAt) && Typing()) { pending = true; return; }
        Render();
    }

    /// <summary>Draws what waited while someone typed.</summary>
    private void Catch() { if (pending && !Typing()) Render(); }

    private void Render()
    {
        pending = false; acting = false; drawnAt = Location();
        Breakpoint = Classify(Bounds.Width > 0 ? Bounds.Width : 412);
        bottomBar = null;
        Inset();
        if (Session.Workspace is null) { main.Child = TripListView.Build(Session, Breakpoint); Overlays(docked: false); return; }
        // Ticking something off, or any change that draws the page again, leaves it where it was scrolled to. The plan
        // keeps its own place per day, and the map has none.
        var here = (Session.Workspace, Session.Tab, Session.Page);
        var scrolled = Session.Tab is MobileTab.Today or MobileTab.More && Equals(here, pageAt) && PageScroll(drawnPage) is { } before ? before.Offset : (Vector?)null;
        var page = Session.Tab switch
        {
            MobileTab.Today => TodayView.Build(Session, Breakpoint),
            MobileTab.Plan => PlanView.Build(Session, Breakpoint, PageWidth(), Bounds.Height > 0 ? Bounds.Height : 915),
            MobileTab.Map => Map(),
            _ => MorePages.Build(Session, Breakpoint)
        };
        drawnPage = page; pageAt = here;
        if (scrolled is { } offset && PageScroll(page) is { } after) ScrollPlace.Set(after, () => after.Offset = offset);
        // Lists and forms under More read better in a column than across a wide screen. The column is as wide on every
        // page, whatever it holds, so the way back stays where it was; a stretched element narrower than the room is centred.
        if (Session.Tab == MobileTab.More && Breakpoint >= WidthClass.Tablet) page = new Border { Child = page, MaxWidth = 760 };
        // From 840 dp the details are a fixed column next to the page instead of a sheet over it.
        var docked = Breakpoint >= WidthClass.Tablet && Session.Showing == Overlay.Details && Session.OverlayItem is { } shown && Session.Trip?.Find(shown) is not null;
        if (Breakpoint == WidthClass.Phone)
        {
            var grid = new Grid { RowDefinitions = new RowDefinitions("*,Auto") };
            grid.Children.Add(page); grid.Children.Add(BottomBar().Row(1));
            main.Child = grid;
        }
        else
        {
            var side = Breakpoint == WidthClass.Wide && Session.Settings.Values.GetValueOrDefault("sidebar") != "collapsed" ? Sidebar() : Rail();
            main.Child = docked ? Ui.Columns("Auto,*,400", side, page, DetailColumn()) : Ui.Columns("Auto,*", side, page);
        }
        Overlays(docked);
        // The new page takes its scroll position before it is drawn, so its top does not flash up first.
        ScrollPlace.Settle(main);
        if ((Session.FocusDraft is { } key ? MorePages.DraftPrefix + key : refocus) is { } target)
        {
            Session.FocusDraft = null; refocus = null;
            Dispatcher.UIThread.Post(() =>
            {
                if (Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(main).OfType<TextBox>().FirstOrDefault(b => b.Name == target) is not { } field) return;
                field.Focus(); field.CaretIndex = field.Text?.Length ?? 0;
                // The keyboard stayed open, but Android reported it closed while the old field went away, and does not
                // report it open again. Its room is read here, so the field shows above it.
                DispatcherTimer.RunOnce(() =>
                {
                    if (!field.IsFocused) return;
                    // Measured from the window's bottom, as the keyboard's own reports are.
                    if (TopLevel.GetTopLevel(this) is { InputPane: { State: Avalonia.Controls.Platform.InputPaneState.Open } pane } top) Keyboard(true, top.Bounds.Height - pane.OccludedRect.Y);
                    Dispatcher.UIThread.Post(() => field.BringIntoView(), DispatcherPriority.Background);
                }, TimeSpan.FromMilliseconds(150));
            }, DispatcherPriority.Loaded);
        }
    }

    /// <summary>The scrolling part of a page: the page itself, or the first one inside it.</summary>
    private static ScrollViewer? PageScroll(Control? page) =>
        page as ScrollViewer ?? page?.GetLogicalDescendants().OfType<ScrollViewer>().FirstOrDefault();

    /// <summary>The width left for the page beside the rail or sidebar and an open details column.</summary>
    private double PageWidth()
    {
        var width = Bounds.Width > 0 ? Bounds.Width : 412;
        if (Breakpoint == WidthClass.Phone) return width;
        var side = Breakpoint == WidthClass.Wide && Session.Settings.Values.GetValueOrDefault("sidebar") != "collapsed" ? 232 : 88;
        var details = Breakpoint >= WidthClass.Tablet && Session.Showing == Overlay.Details ? 400 : 0;
        return width - side - details;
    }

    private Control Map()
    {
        if (map is null || !map.Shows(Session.Workspace)) map = new MapPage(Session);
        Ui.Detach(map);
        map.Update(Breakpoint);
        return map;
    }

    /// <summary>The details as a fixed column on wide screens: no handle, a close button instead.</summary>
    private Control DetailColumn()
    {
        var item = Session.Trip!.Find(Session.OverlayItem!.Value)!;
        var layout = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        layout.Children.Add(DetailView.Header(Session, item, Session.CloseOverlay).Margin(0, 12, 0, 0));
        layout.Children.Add(new ScrollViewer { Content = DetailView.Body(Session, item) }.Row(1));
        return new Border { Child = layout, BorderThickness = new Thickness(1, 0, 0, 0) }.Classed("bar");
    }

    /// <summary>
    /// Keeps the sheet in step with the session: the details are drawn again after every change, at the same detent;
    /// Quick Add and the editor stay as they are, so typed text is not lost.
    /// </summary>
    private void Overlays(bool docked)
    {
        var want = docked ? Overlay.None : Session.Showing;
        var item = Session.OverlayItem is { } id ? Session.Trip?.Find(id) : null;
        // A new place has no item yet; everything else needs one that still exists.
        if (want is not (Overlay.None or Overlay.QuickAdd or Overlay.Share) && item is null && !(want == Overlay.Place && Session.OverlayItem is null)) want = Overlay.None;
        if (want == Overlay.Share && Session.Incoming is null) want = Overlay.None;
        var keep = sheet is not null && sheetFor == (want, Session.OverlayItem) && want is Overlay.QuickAdd or Overlay.Editor or Overlay.Place or Overlay.Share;
        if (!keep)
        {
            // Details open at the glance detent: time, place, reference and Navigate. Dragging up shows more.
            var detent = sheet is not null && sheetFor == (want, Session.OverlayItem) ? sheet.Detent : 0;
            if (sheet is not null) { sheet.Closed -= OnSheetClosed; overlays.Children.Remove(sheet); sheet = null; }
            var side = Breakpoint != WidthClass.Phone;
            sheet = want switch
            {
                Overlay.Details => new Sheet(DetailView.Header(Session, item!, Session.CloseOverlay), DetailView.Body(Session, item!), [0.4, 0.62, 0.94], detent, side, item!.Title),
                Overlay.QuickAdd or Overlay.Editor => new QuickAddView(Session, want == Overlay.Editor ? item : null) is var form
                    ? new Sheet(form.Header, form.View, [0.94], 0, side, Session.Strings[want == Overlay.Editor ? "Edit" : "QuickAdd"]) : null,
                Overlay.Place => new PlaceView(Session, item) is var place ? new Sheet(place.Header, place.View, [0.94], 0, side, item?.Title ?? Session.Strings["AddPlace"]) : null,
                Overlay.Share => new ShareView(Session, Session.Incoming!) is var share ? new Sheet(share.Header, share.View, [0.94], 0, side, Session.Strings["SharedWithJourfold"]) : null,
                _ => null
            };
            sheetFor = (want, Session.OverlayItem);
            if (sheet is not null) { sheet.Closed += OnSheetClosed; overlays.Children.Insert(0, sheet); }
        }
        // A long-press menu sits above the page and the sheet, a question above everything else.
        foreach (var old in overlays.Children.OfType<Control>().Where(b => b.Classes.Contains("question") || b.Classes.Contains("menu") || b.Classes.Contains("viewer")).ToArray()) overlays.Children.Remove(old);
        // A file opened from the details covers the whole screen; back returns to the details.
        if (Session.Viewing.Count > 0) overlays.Children.Add(FileView.Build(Session, Session.Viewing[^1]).Classed("viewer"));
        if (Session.TaxiCard is { } taxi && Session.Trip?.Find(taxi) is { } taxiPlace) overlays.Children.Add(TaxiCardView.Build(Session, taxiPlace).Classed("viewer"));
        if (Session.Menu is { } menu) overlays.Children.Add(Menu(menu));
        if (Session.Asking is { } question) overlays.Children.Add(Question(question));
        if (Session.ChangeProblem is { } problem) overlays.Children.Add(Problem(problem));
    }

    /// <summary>Why a change was not saved, above everything else until it is closed.</summary>
    private Control Problem(string problem)
    {
        var s = Session.Strings;
        var close = Ui.Button(s["Close"], null, "primary"); close.Click += (_, _) => Session.DismissProblem();
        var card = Ui.Card(Ui.V(12, Ui.H(8, Ui.Icon("alert-triangle", 20, "Danger"), Ui.Text(s["NotSaved"], "title")),
                Ui.Text(problem).Also(t => t.TextWrapping = TextWrapping.Wrap), Ui.Columns("*,Auto", null, close.Col(1))), 20)
            .Also(c => { c.MaxWidth = 420; c.Margin = new Thickness(24); c.VerticalAlignment = VerticalAlignment.Center; });
        Avalonia.Automation.AutomationProperties.SetLiveSetting(card, Avalonia.Automation.AutomationLiveSetting.Assertive);
        AutomationProperties.SetName(card, s["NotSaved"] + ". " + problem);
        Dispatcher.UIThread.Post(() => close.Focus(), DispatcherPriority.Background);
        return new Border { Child = card, Background = new SolidColorBrush(Colors.Black, 0.32) }.Classed("question");
    }

    /// <summary>A long-press menu: a list of actions from the bottom on phones, a card in the middle on wider screens.</summary>
    private Control Menu(ActionMenu menu)
    {
        var list = Ui.V(0, Ui.V(2, Ui.Text(menu.Title, "title"), menu.Detail is { } detail ? Ui.Text(detail, "caption") : null).Margin(20, 16, 20, 8));
        foreach (var action in menu.Actions)
        {
            var row = new Button { Content = Ui.Columns("Auto,*", Ui.Icon(action.Icon, 20, action.Danger ? "Danger" : "Text.Muted").Margin(0, 0, 16, 0), Ui.Text(action.Label).Also(t => { if (action.Danger) t.Res(TextBlock.ForegroundProperty, "Danger"); })) }.Classed("row");
            AutomationProperties.SetName(row, action.Label);
            row.Click += async (_, _) => await Session.ChooseAsync(action, TopLevel.GetTopLevel(this));
            list.Children.Add(row);
        }
        var phone = Breakpoint == WidthClass.Phone;
        var panel = new Border
        {
            Child = new ScrollViewer { Content = list },
            CornerRadius = phone ? new CornerRadius(20, 20, 0, 0) : new CornerRadius(16),
            Padding = new Thickness(0, 0, 0, phone ? 12 + safeArea.Bottom : 12),
            MaxHeight = Math.Max(240, Bounds.Height * 0.8),
            MaxWidth = phone ? double.PositiveInfinity : 420,
            VerticalAlignment = phone ? VerticalAlignment.Bottom : VerticalAlignment.Center,
            Margin = phone ? default : new Thickness(24),
            BoxShadow = BoxShadows.Parse("0 -2 16 0 #26000000")
        }.Res(Border.BackgroundProperty, "Bg.Surface");
        AutomationProperties.SetName(panel, menu.Title);
        var scrim = new Border { Background = new SolidColorBrush(Colors.Black, 0.32) };
        scrim.PointerPressed += (_, e) => { Session.CloseOverlay(); e.Handled = true; };
        return new Panel { Children = { scrim, panel } }.Classed("menu");
    }

    /// <summary>A short notice at the bottom, above the sheet and the bottom bar, that goes away by itself.</summary>
    public void Notify(string message)
    {
        foreach (var old in overlays.Children.OfType<Control>().Where(c => c.Classes.Contains("notice")).ToArray()) overlays.Children.Remove(old);
        var notice = new Border
        {
            Child = Ui.Text(message).Also(t => { t.Foreground = Brushes.White; t.TextWrapping = TextWrapping.Wrap; }),
            Background = new SolidColorBrush(Color.Parse("#E6202A36")),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(16, 10),
            MaxWidth = 420,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(24, 0, 24, 88 + safeArea.Bottom),
            IsHitTestVisible = false
        }.Classed("notice");
        Avalonia.Automation.AutomationProperties.SetLiveSetting(notice, Avalonia.Automation.AutomationLiveSetting.Polite);
        AutomationProperties.SetName(notice, message);
        overlays.Children.Add(notice);
        DispatcherTimer.RunOnce(() => overlays.Children.Remove(notice), TimeSpan.FromSeconds(2));
    }

    private void OnSheetClosed() { sheet = null; sheetFor = (Overlay.None, null); Session.CloseOverlay(); }

    private Control Question(Question question)
    {
        var no = Ui.Button(Session.Strings["Cancel"], null, "ghost"); no.Click += async (_, _) => await Session.AnswerAsync(false);
        var yes = Ui.Button(question.Action, null, "primary"); yes.Click += async (_, _) => await Session.AnswerAsync(true);
        var card = Ui.Card(Ui.V(16, Ui.Text(question.Text, "title"), Ui.Columns("*,Auto,Auto", null, no.Col(1), yes.Col(2).Margin(8, 0, 0, 0))), 20)
            .Also(c => { c.MaxWidth = 420; c.Margin = new Thickness(24); c.VerticalAlignment = VerticalAlignment.Center; });
        return new Border { Child = card, Background = new SolidColorBrush(Colors.Black, 0.32) }.Classed("question");
    }

    private static readonly (MobileTab Tab, string Icon, string Label)[] Places =
        [(MobileTab.Today, "calendar", "Today"), (MobileTab.Plan, "list", "Plan"), (MobileTab.Map, "map", "Map"), (MobileTab.More, "ellipsis", "More")];

    private Button Place(MobileTab tab, string icon, string label)
    {
        var s = Session.Strings; var text = s[label];
        var key = Array.FindIndex(Places, p => p.Tab == tab) + 1;
        var button = new ChoiceButton { IsSelected = Session.Tab == tab, Content = Ui.V(3, Ui.Icon(icon, 22).Also(i => i.HorizontalAlignment = HorizontalAlignment.Center), Ui.Line(text).Also(t => { t.HorizontalAlignment = HorizontalAlignment.Center; t.FontSize = 12; })) }.Classed("tab");
        AutomationProperties.SetName(button, text);
        ToolTip.SetTip(button, text + " (Ctrl+" + key + ")");
        button.Click += (_, _) => Session.ShowTab(tab);
        return button;
    }

    private Button Plus()
    {
        var label = Session.Strings["QuickAdd"];
        var plus = new Button { Content = Ui.Icon("plus", 26).Also(i => i.HorizontalAlignment = HorizontalAlignment.Center) }.Classed("plus");
        AutomationProperties.SetName(plus, label); ToolTip.SetTip(plus, label);
        plus.Click += (_, _) => Session.ShowQuickAdd();
        return plus;
    }

    /// <summary>Today, Plan, the plus, Map, More: the plus sits in the middle, within thumb reach.</summary>
    private Control BottomBar()
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,Auto,*,*"), Margin = new Thickness(4, 0) };
        grid.Children.Add(Place(Places[0].Tab, Places[0].Icon, Places[0].Label).Col(0));
        grid.Children.Add(Place(Places[1].Tab, Places[1].Icon, Places[1].Label).Col(1));
        grid.Children.Add(Plus().Margin(8, 4).Col(2));
        grid.Children.Add(Place(Places[2].Tab, Places[2].Icon, Places[2].Label).Col(3));
        grid.Children.Add(Place(Places[3].Tab, Places[3].Icon, Places[3].Label).Col(4));
        return bottomBar = new Border { Child = grid, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(0, 0, 0, safeArea.Bottom), IsVisible = !keyboard }.Classed("bar");
    }

    /// <summary>
    /// The rail on tablets: the plus on top, then the four places. On wide screens it opens into the sidebar with the
    /// button in the bottom left corner, where the sidebar has it to fold again.
    /// </summary>
    private Control Rail()
    {
        var items = Ui.V(4, Plus().Also(p => p.HorizontalAlignment = HorizontalAlignment.Center).Margin(0, 12, 0, 16));
        foreach (var (tab, icon, label) in Places) items.Children.Add(Place(tab, icon, label).Also(b => b.Width = 80));
        var layout = new Grid { RowDefinitions = new RowDefinitions("*,Auto"), Children = { items } };
        if (Breakpoint == WidthClass.Wide) layout.Children.Add(SidebarToggle(open: true).Row(1));
        return new Border { Child = layout, Width = 88, BorderThickness = new Thickness(0, 0, 1, 0) }.Classed("bar");
    }

    /// <summary>
    /// The sidebar on wide screens, as in the desktop app: the trip, Quick Add with its name, and the four places with
    /// their names and shortcuts. It folds into the rail and remembers that on this device.
    /// </summary>
    private Control Sidebar()
    {
        var s = Session.Strings;
        var add = Ui.Button(s["QuickAdd"], "plus", "primary").Also(b => { b.HorizontalAlignment = HorizontalAlignment.Stretch; ToolTip.SetTip(b, s["QuickAdd"] + " (Ctrl+N)"); });
        add.Click += (_, _) => Session.ShowQuickAdd();
        var trip = Session.Trip!;
        var items = Ui.V(4, Ui.Text(trip.Manifest.Title, "strong").Also(t => t.TextTrimming = TextTrimming.CharacterEllipsis).Margin(8, 16, 8, 2),
            Ui.Text(Formats.DateRange(TravelRepo.Core.TripQueries.EffectiveDates(trip).Start, TravelRepo.Core.TripQueries.EffectiveDates(trip).End), "caption").Margin(8, 0, 8, 14),
            add.Margin(0, 0, 0, 12));
        for (var i = 0; i < Places.Length; i++)
        {
            var (tab, icon, label) = Places[i];
            var text = s[label];
            var button = new ChoiceButton
            {
                IsSelected = Session.Tab == tab && !(tab == MobileTab.More && Session.Page is not null),
                Content = Ui.Columns("Auto,*,Auto", Ui.Icon(icon, 20).Margin(0, 0, 12, 0), Ui.Text(text).Also(t => t.VerticalAlignment = VerticalAlignment.Center), Ui.Text("Ctrl+" + (i + 1), "caption").Also(t => t.VerticalAlignment = VerticalAlignment.Center))
            }.Classed("sidebar");
            AutomationProperties.SetName(button, text);
            button.Click += (_, _) => Session.ShowTab(tab);
            items.Children.Add(button);
        }
        // As in the desktop's sidebar, the trip's pages are one tap away, each with what matters about it.
        items.Children.Add(Ui.Text(s["TripTools"], "overline").Margin(10, 18, 0, 4));
        var open = TravelRepo.Core.TripSummaries.Costs(trip).Select(c => c.Currency).ToArray();
        foreach (var (page, icon, label, detail) in new (string, string, string, string?)[]
        {
            ("bookings", "ticket", "Bookings", trip.Entities.Values.Count(e => e.Type == "booking").ToString(CultureInfo.CurrentCulture)),
            ("tasks", "square-check", "Tasks", MoreView.OpenTasks(trip) is > 0 and var tasks ? tasks.ToString(CultureInfo.CurrentCulture) : null),
            ("costs", "wallet", "Costs", open.Length > 0 ? string.Join(" + ", open) : null),
            ("places", "map-pin", "Places", null),
            ("files", "paperclip", "Files", null),
            ("travellers", "users", "Travellers", null)
        })
            items.Children.Add(PageRow(page, icon, s[label], detail));
        // Where the trip is kept, as the desktop says at the bottom of its sidebar; a tap opens sync and versions.
        var where = Session.Remote is null ? s["OnlyOnThisPhone"] : Session.SyncProblem ?? (Session.LastSync is { } at ? string.Format(CultureInfo.CurrentCulture, s["SyncedWithGitHub"], Formats.Relative(at, s)) : s["NotSyncedYet"]);
        var state = new Button { Content = Ui.Columns("Auto,*", Ui.Icon(Session.Remote is null || Session.SyncProblem is not null ? "cloud-off" : "cloud", 18, "Text.Muted").Margin(0, 0, 10, 0), Ui.Text(where, "caption").Also(t => t.TextWrapping = TextWrapping.Wrap)) }.Classed("sidebar");
        AutomationProperties.SetName(state, s["SyncAndVersions"] + ": " + where);
        state.Click += (_, _) => Session.ShowPage("sync");
        // The right padding lies inside the scrolling, so its scroll bar runs in the margin beside the rows, not over them.
        var layout = new Grid { RowDefinitions = new RowDefinitions("*,Auto,Auto") };
        layout.Children.Add(new ScrollViewer { Content = items.Margin(0, 0, SidebarPadding, 0), HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled });
        layout.Children.Add(state.Margin(0, 8, SidebarPadding, 4).Row(1));
        layout.Children.Add(SidebarToggle(open: false, inset: SidebarPadding).Row(2));
        return new Border { Child = layout, Width = 232, Padding = new Thickness(SidebarPadding, 0, 0, 0), BorderThickness = new Thickness(0, 0, 1, 0) }.Classed("bar");
    }

    private const double SidebarPadding = 12;

    /// <summary>A page of the trip in the sidebar, marked while it is open.</summary>
    private Button PageRow(string page, string icon, string label, string? detail)
    {
        var row = new ChoiceButton
        {
            IsSelected = Session.Tab == MobileTab.More && Session.Page == page,
            Content = Ui.Columns("Auto,*,Auto", Ui.Icon(icon, 18).Margin(0, 0, 12, 0), Ui.Text(label).Also(t => t.VerticalAlignment = VerticalAlignment.Center), detail is null ? null : Ui.Text(detail, "caption").Also(t => t.VerticalAlignment = VerticalAlignment.Center))
        }.Classed("sidebar");
        row.Padding = new Thickness(10, 7);
        AutomationProperties.SetName(row, label);
        row.Click += (_, _) => Session.ShowPage(page);
        return row;
    }

    /// <summary>The button that folds and opens the sidebar, at the same place in the bottom left corner either way.</summary>
    /// <param name="inset">How far in from the screen's edge the panel holding it starts.</param>
    private Button SidebarToggle(bool open, double inset = 0)
    {
        var label = Session.Strings[open ? "ShowSidebar" : "HideSidebar"];
        var toggle = Ui.IconButton("panel-left", label);
        toggle.HorizontalAlignment = HorizontalAlignment.Left; toggle.VerticalAlignment = VerticalAlignment.Bottom; toggle.Margin = new Thickness(20 - inset, 0, 0, 12);
        ToolTip.SetTip(toggle, label);
        toggle.Click += (_, _) => { Session.Settings.Values["sidebar"] = open ? "open" : "collapsed"; Session.Settings.Save(); Render(); };
        return toggle;
    }
}
