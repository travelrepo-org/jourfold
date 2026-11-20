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
    // The system's own tab bar where the platform has one (iOS): the buttons it shows and what each does, and what of
    // the page makes room for it at the bottom.
    private bool barWanted;
    private SystemBarItem[] barItems = [];
    private Action[] barActions = [];
    private int barSelected = -1;
    private double barHeight;
    private (Control Target, Thickness Original)? barInset;
    // The map stays while its trip is open, so it keeps its position and tiles when the page is drawn again.
    private MapPage? map;
    // Redrawing replaces every control on the page, so a text field being typed in would lose its focus and the
    // keyboard would close. While someone types, redraws that only refresh data wait until the field is left.
    private bool pending, acting, keyboard;
    private double keyboardHeight;
    // The menu shown now, which moves above the keyboard while its search field is typed in.
    private Border? menuPanel;
    // A field that was sent with Enter gets the focus back once the page shows what it added.
    private string? refocus;
    private object? drawnAt;
    // The page drawn last and where it was, so drawing the same page again keeps how far it was scrolled.
    private Control? drawnPage;
    private object? pageAt;
    // The finger on the screen and how far and fast it has scrolled the page, to tell a tap, a nudge and a scroll apart.
    // In device-independent pixels: a tap moves less than TapSlop; a scroll ends the typing once it covers
    // ScrollDistance in all, or moves faster than ScrollSpeed per second, as a flick does.
    private const double TapSlop = 10, ScrollDistance = 96, ScrollSpeed = 900;
    private (int Id, Point At, bool OnControl)? touch;
    private double scrolled, lastScroll;
    private readonly System.Diagnostics.Stopwatch touchClock = new();

    static AppShell()
    {
        // A date or time wheel opens over the page, but Avalonia passes its touches on to the field it belongs to and
        // the form around it. A finger between two wheels, which no wheel takes, scrolled the form behind instead.
        // Inside a wheel the wheel scrolls as before.
        static void KeepTouch(Control presenter, Avalonia.Input.PointerPressedEventArgs e)
        {
            if (e.Source is Visual source && !Avalonia.VisualTree.VisualExtensions.GetSelfAndVisualAncestors(source).TakeWhile(v => v != presenter).Any(v => v is ScrollViewer))
                e.PreventGestureRecognition();
        }
        Avalonia.Input.InputElement.PointerPressedEvent.AddClassHandler<DatePickerPresenter>(KeepTouch, RoutingStrategies.Bubble, handledEventsToo: true);
        Avalonia.Input.InputElement.PointerPressedEvent.AddClassHandler<TimePickerPresenter>(KeepTouch, RoutingStrategies.Bubble, handledEventsToo: true);
        // Where the system has its own date and time pickers, the fields open those instead.
        SystemPickers.Install();
    }

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
        // A tap beside the text field being typed in closes the on-screen keyboard, as in other apps, and so does
        // scrolling the page far or fast; nudging it a little keeps the keyboard. The shell takes the focus, since
        // Avalonia has no way to just drop it. Taps on buttons and other controls keep it, so their clicks are not
        // lost to a page drawn again when the field lets go.
        Focusable = true;
        AddHandler(PointerPressedEvent, (_, e) =>
        {
            if (e.Pointer.Type == Avalonia.Input.PointerType.Mouse) return;
            var onControl = e.Source is Visual source && Avalonia.VisualTree.VisualExtensions.GetSelfAndVisualAncestors(source).Any(v => v is TextBox or Button or TimePicker or DatePicker or ComboBox or Slider);
            touch = (e.Pointer.Id, e.GetPosition(this), onControl); scrolled = 0; touchClock.Restart(); lastScroll = 0;
        }, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerReleasedEvent, (_, e) =>
        {
            if (touch is not { } t || t.Id != e.Pointer.Id) return;
            touch = null;
            var moved = e.GetPosition(this) - t.At;
            if (!t.OnControl && Math.Abs(moved.X) + Math.Abs(moved.Y) < TapSlop && TypingAnywhere()) Focus(Avalonia.Input.NavigationMethod.Pointer);
        }, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(ScrollViewer.ScrollChangedEvent, (_, e) =>
        {
            // Only scrolling by the finger counts: not the page bringing the field into view, nor a flick's glide.
            if (touch is null || !TypingAnywhere()) return;
            var step = Math.Abs(e.OffsetDelta.X) + Math.Abs(e.OffsetDelta.Y);
            if (step == 0) return;
            var now = touchClock.Elapsed.TotalSeconds; var speed = step / Math.Max(now - lastScroll, 0.008);
            lastScroll = now; scrolled += step;
            if (scrolled > ScrollDistance || (speed > ScrollSpeed && scrolled > TapSlop)) Focus(Avalonia.Input.NavigationMethod.Pointer);
        }, RoutingStrategies.Bubble, handledEventsToo: true);
        if (session.Host.Bar is { } systemBar)
        {
            systemBar.Tapped += i => Dispatcher.UIThread.Post(() => { if (i < barActions.Length) { acting = true; barActions[i](); } });
            systemBar.HeightChanged += () => Dispatcher.UIThread.Post(BarInset);
        }
        AddHandler(KeyDownEvent, OnKey, RoutingStrategies.Bubble);
        AddHandler(KeyDownEvent, OnCommandKey, RoutingStrategies.Tunnel);
        // Enter in a one-line field sends it, as its button would, so what it added shows at once.
        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key == Avalonia.Input.Key.Enter && e.Source is TextBox { AcceptsReturn: false, Name: { } name } box && name.StartsWith(MorePages.DraftPrefix, StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(box.Text))
            { acting = true; refocus = name; }
        }, RoutingStrategies.Tunnel, handledEventsToo: true);
        Render();
    }

    /// <summary>
    /// Shortcuts for a hardware keyboard: Ctrl+N adds, Ctrl+K or Ctrl+F searches, Ctrl+1 to Ctrl+4 open the four places, the arrow keys move
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
            case Avalonia.Input.Key.K or Avalonia.Input.Key.F when ctrl: Session.ShowSearch(); break;
            case >= Avalonia.Input.Key.D1 and <= Avalonia.Input.Key.D4 when ctrl: Go(ShownPlaces()[e.Key - Avalonia.Input.Key.D1]); break;
            case Avalonia.Input.Key.Left when Session.Showing == Overlay.None && Session.Tab is MobileTab.Plan or MobileTab.Map: Session.StepDay(-1); break;
            case Avalonia.Input.Key.Right when Session.Showing == Overlay.None && Session.Tab is MobileTab.Plan or MobileTab.Map: Session.StepDay(1); break;
            default: return;
        }
        e.Handled = true;
    }

    /// <summary>
    /// Whether the keyboard is Apple's, on iPhone and iPad: shortcuts read "⌘1" rather than "Ctrl+1", and text fields
    /// take ⌘C, ⌘V and the like. Headless tests keep it off, as their rendering has no font to draw ⌘ with. Both Ctrl
    /// and ⌘ work for the shortcuts on every platform; this only changes how they are written and which one text
    /// fields take for editing.
    /// </summary>
    public static bool AppleKeys { get; set; } = OperatingSystem.IsIOS() || OperatingSystem.IsMacCatalyst();

    /// <summary>A command shortcut with the given key, as the platform writes it.</summary>
    public static string Shortcut(string key) => AppleKeys ? "⌘" + key : "Ctrl+" + key;

    /// <summary>
    /// ⌘C, ⌘X, ⌘V, ⌘A, ⌘Z and ⇧⌘Z in a text field. Avalonia knows these keys only with Ctrl on iOS, so ⌘V would type a
    /// "v" instead of pasting.
    /// </summary>
    private void OnCommandKey(object? sender, Avalonia.Input.KeyEventArgs e)
    {
        if (!AppleKeys || e.Source is not TextBox box || !e.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Meta)) return;
        var shift = e.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Shift);
        switch (e.Key)
        {
            case Avalonia.Input.Key.C: box.Copy(); break;
            case Avalonia.Input.Key.X: box.Cut(); break;
            case Avalonia.Input.Key.V: box.Paste(); break;
            case Avalonia.Input.Key.A: box.SelectAll(); break;
            case Avalonia.Input.Key.Z when shift: box.Redo(); break;
            case Avalonia.Input.Key.Z: box.Undo(); break;
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
        shown = this;
        Dispatcher.UIThread.UnhandledException += OnUnhandled;
        if (Avalonia.Application.Current?.TryGetFeature<Avalonia.Controls.ApplicationLifetimes.IActivatableLifetime>() is { } lifetime) lifetime.Deactivated += OnDeactivated;
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
        _ = Session.CheckForUpdatesAsync();
    }

    private void OnDeactivated(object? sender, Avalonia.Controls.ApplicationLifetimes.ActivatedEventArgs e)
    {
        if (e.Kind == Avalonia.Controls.ApplicationLifetimes.ActivationKind.Background) Left();
    }

    /// <summary>
    /// The app went to the background. The system hides the keyboard then without telling the app, so the page and the
    /// sheet stop leaving room for it; otherwise a form came back squeezed into the top half above the dimmed page,
    /// where a tap closed it.
    /// </summary>
    public void Left()
    {
        if (keyboard) Keyboard(false, 0);
        sheet?.KeyboardHidden();
    }

    /// <summary>Leaves room for the on-screen keyboard, or takes it back.</summary>
    private void Keyboard(bool open, double height)
    {
        // The bottom bar steps aside while typing, as on other Android apps.
        keyboard = open; keyboardHeight = open ? height : 0;
        if (bottomBar is not null) bottomBar.IsVisible = !keyboard;
        // A menu being searched in moves up with the keyboard.
        if (menuPanel is { } panel)
        {
            var phone = Breakpoint == WidthClass.Phone;
            panel.Margin = phone ? new Thickness(0, 0, 0, keyboardHeight) : new Thickness(24, 24, 24, 24 + keyboardHeight);
            panel.MaxHeight = Math.Max(240, (Bounds.Height - keyboardHeight) * 0.8);
        }
        SystemBar();
        // With the bottom bar the page reaches the screen's edge; without it the safe area is already left free.
        var edge = bottomBar is not null || barWanted ? 0 : safeArea.Bottom;
        main.Margin = new Thickness(0, 0, 0, keyboard ? Math.Max(0, height - edge) : 0);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is { } top) top.BackRequested -= OnBack;
        if (shown == this) shown = null;
        Dispatcher.UIThread.UnhandledException -= OnUnhandled;
        if (Avalonia.Application.Current?.TryGetFeature<Avalonia.Controls.ApplicationLifetimes.IActivatableLifetime>() is { } lifetime) lifetime.Deactivated -= OnDeactivated;
        base.OnDetachedFromVisualTree(e);
    }

    /// <summary>
    /// A tap whose action fails shows why instead of ending the app, for example when the trip has errors or a server
    /// answers unexpectedly. Buttons run their actions without waiting, so the failure arrives here.
    /// </summary>
    private void OnUnhandled(object? sender, DispatcherUnhandledExceptionEventArgs e)
    {
        if (e.Handled) return;
        Session.ShowProblem(e.Exception); e.Handled = true;
    }

    private static AppShell? shown;

    /// <summary>
    /// The same step back as the system's back gesture, for platforms whose system has none, such as iOS, where the
    /// app's own swipe from the left edge calls it. At the trip list with nothing open, nothing happens.
    /// </summary>
    public static void GoBack() => shown?.OnBack(shown, new RoutedEventArgs());

    /// <summary>
    /// Whether a swipe from the left edge goes back. Not on the map with nothing over it, where a drag from the edge
    /// pans the map; its back button and the bar still lead away.
    /// </summary>
    public static bool EdgeSwipeGoesBack => shown is not { Session: var s } || !(s.Workspace is not null && s.Tab == MobileTab.Map && s.Showing == Overlay.None
        && s.Menu is null && s.Asking is null && s.ChangeProblem is null && s.Viewing.Count == 0 && s.TaxiCard is null);

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
        if (Session.Workspace is null)
        {
            if (Session.RemoteTrips is not null) { Session.CloseRemoteTrips(); e.Handled = true; }
            else if (Session.Page is not null) { Session.GoBack(); e.Handled = true; }
            return;
        }
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
        // Sheets reach the screen's edge too, so their last button stays above the system's buttons.
        if (sheet is not null) sheet.BottomInset = bar ? safeArea.Bottom : 0;
    }

    /// <summary>Where the user is. When it changes, the page is drawn at once, even while someone types.</summary>
    private object Location() => (Session.Workspace, Session.Tab, Session.Page, Session.Showing, Session.OverlayItem, Session.SelectedDate, Session.RemoteTrips, Session.SignInCode, Session.Asking, Session.Menu, Session.Viewing.Count, Session.FileProblem, Session.TaxiCard,
        // Something shared anew while the share sheet is open, such as from iOS's share extension on coming back.
        Session.Incoming);

    /// <summary>Whether a text field on the page has the focus. Sheets are not redrawn while they are open.</summary>
    private bool Typing() => TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is TextBox box && Avalonia.VisualTree.VisualExtensions.GetVisualAncestors(box).Contains(main);
    /// <summary>Whether any text field has the focus, on the page or in a sheet.</summary>
    private bool TypingAnywhere() => TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is TextBox;

    private void RequestRender()
    {
        if (!acting && Equals(Location(), drawnAt) && Typing()) { pending = true; return; }
        Render();
    }

    /// <summary>Draws what waited while someone typed.</summary>
    private void Catch()
    {
        if (keyboard && !TypingAnywhere()) Keyboard(false, 0);
        if (pending && !Typing()) Render();
    }

    private void Render()
    {
        pending = false; acting = false; drawnAt = Location();
        Breakpoint = Classify(Bounds.Width > 0 ? Bounds.Width : 412);
        bottomBar = null; barWanted = false; barInset = null;
        Session.SearchInBar = Breakpoint == WidthClass.Phone && Session.Host.Bar is { SearchTab: true };
        Inset();
        // The keyboard goes with the field typed in. Android does not always say so, for example when back leaves the
        // trip while it is up; the page would then end half way up the screen, without the bottom bar, from then on.
        if (keyboard && !TypingAnywhere()) Keyboard(false, 0);
        if (Session.Workspace is null)
        {
            // The settings open from the trip list too, in the same column as under More.
            var settings = MorePages.IsAppPage(Session.Page) && Session.RemoteTrips is null ? MorePages.Build(Session, Breakpoint) : null;
            main.Child = settings is null ? TripListView.Build(Session, Breakpoint) : Breakpoint >= WidthClass.Tablet ? new Border { Child = settings, MaxWidth = 760 } : settings;
            Overlays(docked: false); SystemBar(); return;
        }
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
        // A new page starts at its top, and so does the system's bar, at full size.
        if (!Equals(here, pageAt)) Session.Host.Bar?.Scrolled(0, 0, 1);
        drawnPage = page; pageAt = here;
        if (scrolled is { } offset && PageScroll(page) is { } after) ScrollPlace.Set(after, () => after.Offset = offset);
        // Lists and forms under More read better in a column than across a wide screen. The column is as wide on every
        // page, whatever it holds, so the way back stays where it was; a stretched element narrower than the room is centred.
        if (Session.Tab == MobileTab.More && Breakpoint >= WidthClass.Tablet) page = new Border { Child = page, MaxWidth = 760 };
        // From 840 dp the details are a fixed column next to the page instead of a sheet over it.
        var docked = Breakpoint >= WidthClass.Tablet && Session.Showing == Overlay.Details && Session.OverlayItem is { } shown && Session.Trip?.Find(shown) is not null;
        if (Breakpoint == WidthClass.Phone && Session.Host.Bar is not null)
        {
            // The system's bar floats over the page, which scrolls on beneath it and ends with room for it.
            main.Child = page;
            SystemBarPlaces(); barWanted = true;
            barInset = BarInsetTarget(page);
            // The page scrolling under the bar lets it shrink, as in other apps.
            if (barInset?.Target is ScrollViewer scroll)
                scroll.ScrollChanged += (_, e) => { if (barWanted && (e.OffsetDelta.Y != 0 || e.ExtentDelta.Y != 0 || e.ViewportDelta.Y != 0)) Session.Host.Bar?.Scrolled(scroll.Offset.Y, scroll.Extent.Height, scroll.Viewport.Height); };
            BarInset();
        }
        else if (Breakpoint == WidthClass.Phone)
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
        SystemBar();
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
        if (want is not (Overlay.None or Overlay.QuickAdd or Overlay.Share or Overlay.Welcome) && item is null && !(want == Overlay.Place && Session.OverlayItem is null)) want = Overlay.None;
        if (want == Overlay.Share && Session.Incoming is null) want = Overlay.None;
        var keep = sheet is not null && sheetFor == (want, Session.OverlayItem) && want is Overlay.QuickAdd or Overlay.Editor or Overlay.Place or Overlay.Share or Overlay.Welcome;
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
                    ? new Sheet(form.Header, form.View, [0.94], 0, side, Session.Strings[want == Overlay.Editor ? "Edit" : "QuickAdd"], form: true) : null,
                Overlay.Place => new PlaceView(Session, item) is var place ? new Sheet(place.Header, place.View, [0.94], 0, side, item?.Title ?? Session.Strings["AddPlace"], form: true) : null,
                Overlay.Share => new ShareView(Session, Session.Incoming!) is var share ? new Sheet(share.Header, share.View, [0.94], 0, side, Session.Strings["SharedWithJourfold"], form: true) : null,
                Overlay.Welcome => new TripWelcomeView(Session) is var welcome ? new Sheet(welcome.Header, welcome.View, [0.94], 0, side, Session.Strings["WhoAmITitle"], form: true) : null,
                _ => null
            };
            sheetFor = (want, Session.OverlayItem);
            if (sheet is not null) { sheet.Closed += OnSheetClosed; overlays.Children.Insert(0, sheet); Inset(); }
        }
        // A long-press menu sits above the page and the sheet, a question above everything else.
        foreach (var old in overlays.Children.OfType<Control>().Where(b => b.Classes.Contains("question") || b.Classes.Contains("menu") || b.Classes.Contains("viewer")).ToArray()) overlays.Children.Remove(old);
        menuPanel = null;
        // A file opened from the details covers the whole screen; back returns to the details.
        if (Session.Viewing.Count > 0) overlays.Children.Add(FileView.Build(Session, Session.Viewing[^1]).Classed("viewer"));
        if (Session.TaxiCard is { } taxi && Session.Trip?.Find(taxi) is { } taxiPlace) overlays.Children.Add(TaxiCardView.Build(Session, taxiPlace).Classed("viewer"));
        if (Session.Menu is { } menu && !SystemMenu(menu)) overlays.Children.Add(Menu(menu));
        if (Session.Asking is { } question && !SystemQuestion(question)) overlays.Children.Add(Question(question));
        if (Session.ChangeProblem is { } problem) overlays.Children.Add(Problem(problem));
    }

    /// <summary>Why a change was not saved, above everything else until it is closed.</summary>
    private Control Problem(string problem)
    {
        var s = Session.Strings;
        var close = Ui.Button(s["Close"], null, "primary"); close.Click += (_, _) => Session.DismissProblem();
        var title = Session.ProblemTitle ?? s["NotSaved"];
        var body = Ui.V(12, Ui.H(8, Ui.Icon("triangle-alert", 20, "Danger"), Ui.Text(title, "title")), Ui.Text(problem).Also(t => t.TextWrapping = TextWrapping.Wrap));
        if (Session.ProblemCanBeMailed)
        {
            // The report opens in the user's mail app, where they see and edit everything before sending it themselves.
            var mail = Ui.Button(s["ReportByMailPhone"], "mail", "soft").Also(b => b.HorizontalAlignment = HorizontalAlignment.Left);
            mail.Click += async (_, _) => await Session.SendProblemByMailAsync();
            body.Children.Add(mail);
        }
        body.Children.Add(Ui.Columns("*,Auto", null, close.Col(1)));
        var card = Ui.Card(body, 20)
            .Also(c => { c.MaxWidth = 420; c.Margin = new Thickness(24); c.VerticalAlignment = VerticalAlignment.Center; });
        Avalonia.Automation.AutomationProperties.SetLiveSetting(card, Avalonia.Automation.AutomationLiveSetting.Assertive);
        AutomationProperties.SetName(card, title + ". " + problem);
        Dispatcher.UIThread.Post(() => close.Focus(), DispatcherPriority.Background);
        return new Border { Child = card, Background = new SolidColorBrush(Colors.Black, 0.32) }.Classed("question");
    }

    /// <summary>A long-press menu: a list of actions from the bottom on phones, a card in the middle on wider screens.</summary>
    private Control Menu(ActionMenu menu)
    {
        var list = Ui.V(0);
        var rows = new List<(Button Row, string Label)>();
        foreach (var action in menu.Actions)
        {
            var row = new Button { Content = Ui.Columns("Auto,*", Ui.Icon(action.Icon, 20, action.Danger ? "Danger" : "Text.Muted").Margin(0, 0, 16, 0), Ui.Text(action.Label).Also(t => { if (action.Danger) t.Res(TextBlock.ForegroundProperty, "Danger"); })) }.Classed("row");
            AutomationProperties.SetName(row, action.Label);
            row.Click += async (_, _) => await Session.ChooseAsync(action, TopLevel.GetTopLevel(this));
            list.Children.Add(row); rows.Add((row, action.Label));
        }
        var head = Ui.V(8, Ui.V(2, Ui.Text(menu.Title, "title"), menu.Detail is { } detail ? Ui.Text(detail, "caption") : null).Margin(20, 16, 20, 0));
        // Choosing from many, such as the trip's plans and bookings: a field narrows the list, by any part of an entry,
        // regardless of case and accents. It stays above the entries, which scroll beneath it.
        if (menu.Searchable || menu.Actions.Count >= 12)
        {
            var str = Session.Strings;
            var search = new TextBox { PlaceholderText = str["SearchList"], InnerLeftContent = Ui.Icon("search", 16, "Text.Subtle").Margin(12, 0, 0, 0), MinHeight = 44 }.Classed("menu-search");
            AutomationProperties.SetName(search, str["SearchList"]);
            var none = Ui.Text("", "caption").Margin(20, 8);
            none.IsVisible = false;
            var compare = System.Globalization.CultureInfo.CurrentCulture.CompareInfo;
            search.TextChanged += (_, _) =>
            {
                var query = (search.Text ?? "").Trim();
                foreach (var (row, label) in rows) row.IsVisible = query.Length == 0 || compare.IndexOf(label, query, System.Globalization.CompareOptions.IgnoreCase | System.Globalization.CompareOptions.IgnoreNonSpace) >= 0;
                none.IsVisible = query.Length > 0 && rows.All(r => !r.Row.IsVisible);
                none.Text = none.IsVisible ? string.Format(System.Globalization.CultureInfo.CurrentCulture, str["NothingMatches"], query) : "";
            };
            head.Children.Add(search.Margin(16, 0));
            list.Children.Add(none);
        }
        var phone = Breakpoint == WidthClass.Phone;
        var panel = new Border
        {
            Child = new Grid { RowDefinitions = new RowDefinitions("Auto,*"), Children = { head.Margin(0, 0, 0, 8), new ScrollViewer { Content = list }.Row(1) } },
            CornerRadius = phone ? new CornerRadius(20, 20, 0, 0) : new CornerRadius(16),
            Padding = new Thickness(0, 0, 0, phone ? 12 + safeArea.Bottom : 12),
            MaxHeight = Math.Max(240, (Bounds.Height - keyboardHeight) * 0.8),
            MaxWidth = phone ? double.PositiveInfinity : 420,
            VerticalAlignment = phone ? VerticalAlignment.Bottom : VerticalAlignment.Center,
            // Above the keyboard while its search field is typed in.
            Margin = phone ? new Thickness(0, 0, 0, keyboardHeight) : new Thickness(24, 24, 24, 24 + keyboardHeight),
            BoxShadow = BoxShadows.Parse("0 -2 16 0 #26000000")
        }.Res(Border.BackgroundProperty, "Bg.Surface");
        AutomationProperties.SetName(panel, menu.Title);
        var scrim = new Border { Background = new SolidColorBrush(Colors.Black, 0.32) };
        scrim.PointerPressed += (_, e) => { Session.CloseOverlay(); e.Handled = true; };
        menuPanel = panel;
        return new Panel { Children = { scrim, panel } }.Classed("menu");
    }

    // The menu or question shown in the system's own sheet now, so drawing the page again does not show it twice.
    private object? systemDialog;

    /// <summary>Shows the menu in the system's action sheet where there is one; false when the app draws it itself.</summary>
    private bool SystemMenu(ActionMenu menu)
    {
        if (Session.Host.ShowMenu is not { } show || !SystemDialogs.Fits(menu)) return false;
        if (ReferenceEquals(systemDialog, menu)) return true;
        systemDialog = menu;
        var entries = menu.Actions.Select(a => new SystemMenuEntry(a.Label, a.Danger)).ToArray();
        _ = Choose();
        return true;
        async Task Choose()
        {
            var chosen = await show(new SystemMenu(menu.Title, menu.Detail, entries, Session.Strings["Cancel"]));
            if (ReferenceEquals(systemDialog, menu)) systemDialog = null;
            // Something else may have replaced the menu meanwhile, such as a notification opening a trip.
            if (!ReferenceEquals(Session.Menu, menu)) return;
            if (chosen is { } i && i >= 0 && i < menu.Actions.Count) await Session.ChooseAsync(menu.Actions[i], TopLevel.GetTopLevel(this));
            else Session.CloseOverlay();
        }
    }

    /// <summary>Asks the question in the system's alert where there is one; false when the app draws it itself.</summary>
    private bool SystemQuestion(Question question)
    {
        if (Session.Host.Ask is not { } ask) return false;
        if (ReferenceEquals(systemDialog, question)) return true;
        systemDialog = question;
        _ = Answer();
        return true;
        async Task Answer()
        {
            var yes = await ask(new SystemQuestion(question.Text, question.Detail, question.Action, question.Decline ?? Session.Strings["Cancel"]));
            if (ReferenceEquals(systemDialog, question)) systemDialog = null;
            if (ReferenceEquals(Session.Asking, question)) await Session.AnswerAsync(yes);
        }
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
        var no = Ui.Button(question.Decline ?? Session.Strings["Cancel"], null, "ghost"); no.Click += async (_, _) => await Session.AnswerAsync(false);
        var yes = Ui.Button(question.Action, null, "primary"); yes.Click += async (_, _) => await Session.AnswerAsync(true);
        var detail = question.Detail is { } more ? Ui.Text(more).Also(t => t.TextWrapping = TextWrapping.Wrap) : null;
        // Long answers go below each other instead of being cut off.
        var buttons = question.Decline is null ? Ui.Columns("*,Auto,Auto", null, no.Col(1), yes.Col(2).Margin(8, 0, 0, 0))
            : (Control)Ui.V(8, yes.Also(b => b.HorizontalAlignment = HorizontalAlignment.Stretch), no.Also(b => b.HorizontalAlignment = HorizontalAlignment.Stretch));
        var card = Ui.Card(Ui.V(16, Ui.Text(question.Text, "title").Also(t => t.TextWrapping = TextWrapping.Wrap), detail, buttons), 20)
            .Also(c => { c.MaxWidth = 420; c.Margin = new Thickness(24); c.VerticalAlignment = VerticalAlignment.Center; });
        return new Border { Child = card, Background = new SolidColorBrush(Colors.Black, 0.32) }.Classed("question");
    }

    /// <summary>A place of the bar, rail or sidebar: a tab, or a page of the trip shown as a place of its own.</summary>
    private readonly record struct PlaceEntry(MobileTab Tab, string? Page, string Icon, string Label);

    private static readonly PlaceEntry Today = new(MobileTab.Today, null, "calendar", "Today"), Plan = new(MobileTab.Plan, null, "list", "Plan"),
        MapPlace = new(MobileTab.Map, null, "map", "Map"), More = new(MobileTab.More, null, "ellipsis", "More");

    /// <summary>The third button as chosen in the settings: the costs unless the user chose otherwise.</summary>
    public static (string Icon, string Label) ThirdPlaceLook(string place) => place switch
    {
        "map" => ("map", "Map"),
        "bookings" => ("ticket", "Bookings"),
        "tasks" => ("square-check", "Tasks"),
        "packing" => ("backpack", "Packing"),
        _ => ("wallet", "Costs")
    };

    /// <summary>Today, Plan, what the user chose as the third, and More: the places of the bottom bar and the rail.</summary>
    private PlaceEntry[] BarPlaces()
    {
        var third = Session.ThirdPlace; var (icon, label) = ThirdPlaceLook(third);
        return [Today, Plan, third == "map" ? MapPlace : new(MobileTab.More, third, icon, label), More];
    }

    /// <summary>Whether the open sidebar shows, which lists the map and every page of the trip itself.</summary>
    public static bool HasSidebar(MobileSession session, WidthClass width) => width == WidthClass.Wide && session.Settings.Values.GetValueOrDefault("sidebar") != "collapsed";

    /// <summary>The four places as shown now: the sidebar keeps the map, since it lists the trip's pages below.</summary>
    private PlaceEntry[] ShownPlaces() => HasSidebar(Session, Breakpoint) ? [Today, Plan, MapPlace, More] : BarPlaces();

    private void Go(PlaceEntry place) { if (place.Page is null) Session.ShowTab(place.Tab); else Session.ShowThirdPlace(); }

    /// <summary>
    /// Whether the user is at a place of the bar. A page that is the third place marks its own button, wherever it
    /// was opened from; everything else under More marks More, and so does the map when it has no button.
    /// </summary>
    private bool IsAt(PlaceEntry place)
    {
        var third = BarPlaces()[2];
        if (place.Page is { } page) return Session.Tab == MobileTab.More && Session.Page == page;
        if (place.Tab == MobileTab.More) return Session.Tab == MobileTab.More && !(third.Page is not null && Session.Page == third.Page) || Session.Tab == MobileTab.Map && third.Tab != MobileTab.Map;
        return Session.Tab == place.Tab;
    }

    private Button Place(PlaceEntry place, int number)
    {
        var s = Session.Strings; var text = s[place.Label];
        // As in Material 3 Expressive: the chosen place's icon sits in a pill, so it shows without relying on colour.
        var chosen = IsAt(place);
        var pill = new Border { Width = 56, Height = 32, CornerRadius = new CornerRadius(16), HorizontalAlignment = HorizontalAlignment.Center, Child = Ui.Icon(place.Icon, 24, chosen ? "Accent.SoftText" : null).Also(i => i.HorizontalAlignment = HorizontalAlignment.Center) }.Classed("indicator");
        if (chosen) pill.Res(Border.BackgroundProperty, "Accent.Soft");
        var button = new ChoiceButton { IsSelected = chosen, Content = Ui.V(4, pill, Ui.Line(text).Also(t => { t.HorizontalAlignment = HorizontalAlignment.Center; t.FontSize = TextScale.Size(12); })) }.Classed("tab");
        AutomationProperties.SetName(button, text);
        ToolTip.SetTip(button, text + " (" + Shortcut(number.ToString(CultureInfo.InvariantCulture)) + ")");
        button.Click += (_, _) => Go(place);
        return button;
    }

    private Button Plus()
    {
        var label = Session.Strings["QuickAdd"];
        var plus = new Button { Content = Ui.Icon("plus", 26).Also(i => i.HorizontalAlignment = HorizontalAlignment.Center) }.Classed("plus");
        AutomationProperties.SetName(plus, label); ToolTip.SetTip(plus, label);
        plus.Click += (_, _) => QuickAdd();
        return plus;
    }

    /// <summary>Quick Add, ready for an expense on the costs and for a booking on the bookings.</summary>
    private void QuickAdd() => Session.ShowQuickAdd(Session.Page switch { "costs" => "expense", "bookings" => "booking", _ => null });

    /// <summary>
    /// The bottom bar's buttons for the system's bar: the same places in the same order, the plus in the middle, and the
    /// search set apart after them where the system has a place for it.
    /// </summary>
    private void SystemBarPlaces()
    {
        var s = Session.Strings; var places = BarPlaces();
        PlaceEntry[] order = [places[0], places[1], default, places[2], places[3]];
        var items = order.Select((p, i) => i == 2 ? new SystemBarItem("plus", s["Add"], Action: true) : new SystemBarItem(p.Icon, s[p.Label])).ToList();
        var actions = order.Select((p, i) => i == 2 ? QuickAdd : (Action)(() => Go(p))).ToList();
        var searching = Session.Tab == MobileTab.More && Session.Page == "search";
        barSelected = searching ? -1 : Array.FindIndex(order, p => p.Label is not null && IsAt(p));
        if (Session.SearchInBar)
        {
            items.Add(new SystemBarItem("search", s["Search"], Search: true));
            actions.Add(Session.ShowSearch);
            if (searching) barSelected = items.Count - 1;
        }
        barItems = [.. items]; barActions = [.. actions];
    }

    /// <summary>
    /// Shows the system's bar where the page has it, and hides it for the keyboard, a sheet, a menu or a question,
    /// which cover the bottom bar too.
    /// </summary>
    private void SystemBar()
    {
        if (Session.Host.Bar is not { } bar) return;
        var covered = keyboard || sheet is not null || overlays.Children.OfType<Control>().Any(c => !c.Classes.Contains("notice"));
        if (!barWanted || covered) { bar.Hide(); return; }
        bar.Show(barItems, barSelected, Accent(Avalonia.Styling.ThemeVariant.Light), Accent(Avalonia.Styling.ThemeVariant.Dark));
        static Color Accent(Avalonia.Styling.ThemeVariant theme) =>
            Avalonia.Application.Current?.TryGetResource("Accent", theme, out var value) == true && value is ISolidColorBrush brush ? brush.Color : Colors.Teal;
    }

    /// <summary>
    /// The class of a part of a page that ends above the system's bar as a whole, such as a timetable with the nights
    /// band below its scrolling grid, which would otherwise lie under the bar.
    /// </summary>
    public const string AboveBar = "above-bar";

    /// <summary>
    /// What of the page leaves room for the system's bar: a part marked <see cref="AboveBar"/>, else its scrolling
    /// part, so the last line scrolls above the bar, or else the page itself, such as the map, which then ends above it.
    /// </summary>
    private static (Control, Thickness) BarInsetTarget(Control page) =>
        page is MapPage ? (page, default)
        : (page.Classes.Contains(AboveBar) ? page : page.GetLogicalDescendants().OfType<Control>().FirstOrDefault(c => c.Classes.Contains(AboveBar))) is { } part ? (part, part.Margin)
        : (page as ScrollViewer ?? page.GetLogicalDescendants().OfType<ScrollViewer>().FirstOrDefault(v => v.VerticalScrollBarVisibility != Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled)) is { } scroll
            ? (scroll, scroll.Padding) : (page, page.Margin);

    /// <summary>Makes room for the system's bar at the page's bottom, as far as the bar reaches up.</summary>
    private void BarInset()
    {
        if (Session.Host.Bar is { Height: > 0 and var height }) barHeight = height;
        if (barInset is not ({ } target, var original)) return;
        var room = new Thickness(original.Left, original.Top, original.Right, original.Bottom + barHeight);
        if (target is ScrollViewer scroll) scroll.Padding = room; else target.Margin = room;
    }

    /// <summary>Today, Plan, the plus, the third place (the costs unless chosen otherwise), More: the plus sits in the middle, within thumb reach.</summary>
    private Control BottomBar()
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,Auto,*,*"), Margin = new Thickness(4, 0) };
        var places = BarPlaces();
        grid.Children.Add(Place(places[0], 1).Col(0));
        grid.Children.Add(Place(places[1], 2).Col(1));
        grid.Children.Add(Plus().Also(p => p.VerticalAlignment = VerticalAlignment.Center).Margin(8, 4).Col(2));
        grid.Children.Add(Place(places[2], 3).Col(3));
        grid.Children.Add(Place(places[3], 4).Col(4));
        // No line above it; the bar's surface sets it apart from the page.
        return bottomBar = new Border { Child = grid, Padding = new Thickness(0, 0, 0, safeArea.Bottom), IsVisible = !keyboard }.Classed("bar");
    }

    /// <summary>
    /// The rail on tablets: the plus on top, then the four places. On wide screens it opens into the sidebar with the
    /// button in the bottom left corner, where the sidebar has it to fold again.
    /// </summary>
    private Control Rail()
    {
        var items = Ui.V(4, Plus().Also(p => p.HorizontalAlignment = HorizontalAlignment.Center).Margin(0, 12, 0, 16));
        var places = BarPlaces();
        for (var i = 0; i < places.Length; i++) items.Children.Add(Place(places[i], i + 1).Also(b => b.Width = 80));
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
        var add = Ui.Button(s["QuickAdd"], "plus", "primary").Also(b => { b.HorizontalAlignment = HorizontalAlignment.Stretch; ToolTip.SetTip(b, s["QuickAdd"] + " (" + Shortcut("N") + ")"); });
        add.Click += (_, _) => Session.ShowQuickAdd(Session.Page switch { "costs" => "expense", "bookings" => "booking", _ => null });
        var trip = Session.Trip!;
        var items = Ui.V(4, Ui.Text(trip.Manifest.Title, "strong").Also(t => t.TextTrimming = TextTrimming.CharacterEllipsis).Margin(8, 16, 8, 2),
            Ui.Text(Formats.DateRange(TravelRepo.Core.TripQueries.EffectiveDates(trip).Start, TravelRepo.Core.TripQueries.EffectiveDates(trip).End), "caption").Margin(8, 0, 8, 14),
            add.Margin(0, 0, 0, 12));
        var places = ShownPlaces();
        for (var i = 0; i < places.Length; i++)
        {
            var (tab, _, icon, label) = places[i];
            var text = s[label];
            var button = new ChoiceButton
            {
                IsSelected = Session.Tab == tab && !(tab == MobileTab.More && Session.Page is not null),
                Content = Ui.Columns("Auto,*,Auto", Ui.Icon(icon, 20).Margin(0, 0, 12, 0), Ui.Text(text).Also(t => t.VerticalAlignment = VerticalAlignment.Center), Ui.Text(Shortcut((i + 1).ToString(CultureInfo.InvariantCulture)), "caption").Also(t => t.VerticalAlignment = VerticalAlignment.Center))
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
