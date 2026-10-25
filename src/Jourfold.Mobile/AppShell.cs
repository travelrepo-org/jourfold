using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
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

    public AppShell(MobileSession session)
    {
        Session = session; Classes.Add("shell");
        Content = root; root.Res(Border.BackgroundProperty, "Bg.App");
        root.Child = new Panel { Children = { main, overlays } };
        session.Changed += () => Dispatcher.UIThread.Post(Render);
        SizeChanged += (_, e) => { if (Classify(e.NewSize.Width) != Breakpoint) Render(); };
        Render();
    }

    public static WidthClass Classify(double width) => width switch { < 600 => WidthClass.Phone, < 840 => WidthClass.CompactTablet, < 1200 => WidthClass.Tablet, _ => WidthClass.Wide };

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (TopLevel.GetTopLevel(this) is not { } top) return;
        // Content is drawn edge to edge; the safe area keeps it clear of the status bar, notch and gesture bar.
        if (top.InsetsManager is { } insets)
        {
            insets.DisplayEdgeToEdgePreference = true;
            // Read again on every render: Avalonia reports the first values before it knows the screen's scaling
            // (in pixels, not dp) and does not report the corrected ones.
            this.insets = insets;
            insets.SafeAreaChanged += (_, _) => Render();
            top.ScalingChanged += (_, _) => Render();
        }
        top.BackRequested += OnBack;
        if (Session.Trips.Count == 0 && Session.Workspace is null) _ = Session.LoadTripsAsync();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is { } top) top.BackRequested -= OnBack;
        base.OnDetachedFromVisualTree(e);
    }

    /// <summary>The system back gesture: first back to Today, then back to the trip list, then out of the app.</summary>
    private void OnBack(object? sender, RoutedEventArgs e)
    {
        if (Session.Asking is not null || Session.Showing != Overlay.None) { Session.CloseOverlay(); e.Handled = true; return; }
        if (Session.Workspace is null) return;
        if (Session.Tab != MobileTab.Today) Session.ShowTab(MobileTab.Today); else Session.Close();
        e.Handled = true;
    }

    private void Render()
    {
        Breakpoint = Classify(Bounds.Width > 0 ? Bounds.Width : 412);
        // The bottom bar reaches the screen's edge and keeps its buttons above the gesture bar; everything else stays
        // inside the safe area.
        safeArea = insets is { DisplaysEdgeToEdge: true } ? insets.SafeAreaPadding : default;
        var bar = Session.Workspace is not null && Breakpoint == WidthClass.Phone;
        root.Padding = new Thickness(safeArea.Left, safeArea.Top, safeArea.Right, bar ? 0 : safeArea.Bottom);
        if (Session.Workspace is null) { main.Child = TripListView.Build(Session); Overlays(docked: false); return; }
        var page = Session.Tab switch
        {
            MobileTab.Today => TodayView.Build(Session, Breakpoint),
            MobileTab.Plan => PlanView.Build(Session, Breakpoint),
            MobileTab.Map => MapView.Build(Session, Breakpoint),
            _ => MoreView.Build(Session, Breakpoint)
        };
        // From 840 dp the details are a fixed column next to the page instead of a sheet over it.
        var docked = Breakpoint >= WidthClass.Tablet && Session.Showing == Overlay.Details && Session.OverlayItem is { } shown && Session.Trip?.Find(shown) is not null;
        if (Breakpoint == WidthClass.Phone)
        {
            var grid = new Grid { RowDefinitions = new RowDefinitions("*,Auto") };
            grid.Children.Add(page); grid.Children.Add(BottomBar().Row(1));
            main.Child = grid;
        }
        else main.Child = docked ? Ui.Columns("Auto,*,400", Rail(), page, DetailColumn()) : Ui.Columns("Auto,*", Rail(), page);
        Overlays(docked);
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
        if (want != Overlay.None && want != Overlay.QuickAdd && item is null) want = Overlay.None;
        var keep = sheet is not null && sheetFor == (want, Session.OverlayItem) && want is Overlay.QuickAdd or Overlay.Editor;
        if (!keep)
        {
            var detent = sheet is not null && sheetFor == (want, Session.OverlayItem) ? sheet.Detent : 1;
            if (sheet is not null) { sheet.Closed -= OnSheetClosed; overlays.Children.Remove(sheet); sheet = null; }
            var side = Breakpoint != WidthClass.Phone;
            sheet = want switch
            {
                Overlay.Details => new Sheet(DetailView.Header(Session, item!, Session.CloseOverlay), DetailView.Body(Session, item!), [0.4, 0.62, 0.94], detent, side, item!.Title),
                Overlay.QuickAdd or Overlay.Editor => new QuickAddView(Session, want == Overlay.Editor ? item : null) is var form
                    ? new Sheet(form.Header, form.View, [0.94], 0, side, Session.Strings[want == Overlay.Editor ? "Edit" : "QuickAdd"]) : null,
                _ => null
            };
            sheetFor = (want, Session.OverlayItem);
            if (sheet is not null) { sheet.Closed += OnSheetClosed; overlays.Children.Insert(0, sheet); }
        }
        // A question sits above everything else.
        foreach (var old in overlays.Children.OfType<Border>().Where(b => b.Classes.Contains("question")).ToArray()) overlays.Children.Remove(old);
        if (Session.Asking is { } question) overlays.Children.Add(Question(question));
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
        var button = new ChoiceButton { IsSelected = Session.Tab == tab, Content = Ui.V(3, Ui.Icon(icon, 22).Also(i => i.HorizontalAlignment = HorizontalAlignment.Center), Ui.Line(text).Also(t => { t.HorizontalAlignment = HorizontalAlignment.Center; t.FontSize = 12; })) }.Classed("tab");
        AutomationProperties.SetName(button, text);
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
        return new Border { Child = grid, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(0, 0, 0, safeArea.Bottom) }.Classed("bar");
    }

    /// <summary>The rail on tablets: the plus on top, then the four places.</summary>
    private Control Rail()
    {
        var items = Ui.V(4, Plus().Also(p => p.HorizontalAlignment = HorizontalAlignment.Center).Margin(0, 12, 0, 16));
        foreach (var (tab, icon, label) in Places) items.Children.Add(Place(tab, icon, label).Also(b => b.Width = 80));
        return new Border { Child = items, Width = 88, BorderThickness = new Thickness(0, 0, 1, 0) }.Classed("bar");
    }
}
