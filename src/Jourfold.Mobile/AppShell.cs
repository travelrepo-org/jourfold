using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
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
    private Thickness safeArea;

    public AppShell(MobileSession session)
    {
        Session = session; Classes.Add("shell");
        Content = root; root.Res(Border.BackgroundProperty, "Bg.App");
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
            void Apply() { safeArea = insets.SafeAreaPadding; Render(); }
            insets.SafeAreaChanged += (_, _) => Apply(); Apply();
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
        if (Session.Workspace is null) return;
        if (Session.Tab != MobileTab.Today) Session.ShowTab(MobileTab.Today); else Session.Close();
        e.Handled = true;
    }

    private void Render()
    {
        Breakpoint = Classify(Bounds.Width > 0 ? Bounds.Width : 412);
        // The bottom bar reaches the screen's edge and keeps its buttons above the gesture bar; everything else stays
        // inside the safe area.
        var bar = Session.Workspace is not null && Breakpoint == WidthClass.Phone;
        root.Padding = new Thickness(safeArea.Left, safeArea.Top, safeArea.Right, bar ? 0 : safeArea.Bottom);
        if (Session.Workspace is null) { root.Child = TripListView.Build(Session); return; }
        var page = Session.Tab switch
        {
            MobileTab.Today => TodayView.Build(Session, Breakpoint),
            MobileTab.Plan => PlanView.Build(Session, Breakpoint),
            MobileTab.Map => MapView.Build(Session, Breakpoint),
            _ => MoreView.Build(Session, Breakpoint)
        };
        if (Breakpoint == WidthClass.Phone)
        {
            var grid = new Grid { RowDefinitions = new RowDefinitions("*,Auto") };
            grid.Children.Add(page); grid.Children.Add(BottomBar().Row(1));
            root.Child = grid;
        }
        else root.Child = Ui.Columns("Auto,*", Rail(), page);
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
