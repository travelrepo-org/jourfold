using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Jourfold.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using TravelRepo.Git;

namespace Jourfold.Desktop;

public partial class MainWindow : Window
{
    private static readonly Dictionary<string, MainWindow> OpenWindows = new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    public MainViewModel Model { get; }
    public Dialogs Dialogs { get; }
    private readonly DispatcherTimer poll;
    private readonly Dictionary<string, Vector> scrollOffsets = new();
    private Guid? inspectorFor;
    private bool renderQueued;

    public MainWindow()
    {
        InitializeComponent();
        MainViewModel? vm = null; var store = App.Services.GetRequiredService<LocalStore>();
        Dialogs = new Dialogs(this, () => vm?.Strings ?? new Localization("en"));
        vm = new MainViewModel(store, App.Services.GetRequiredService<IGitBackend>(), App.Services.GetRequiredService<OsSecretStore>(), Dialogs); Model = vm; DataContext = Model;
        Model.ContentChanged += (_, _) => QueueRender();
        Model.SelectionChanged += (_, _) => { if (Model.HasTrip) { RenderInspector(); RenderMain(); } };
        Model.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(MainViewModel.Notice) or nameof(MainViewModel.SyncKey) or nameof(MainViewModel.HasUncommitted) or nameof(MainViewModel.Saving) or nameof(MainViewModel.PrivacyWarning) or nameof(MainViewModel.GitHubConnected))
                Dispatcher.UIThread.Post(() => { if (Model.HasTrip) { RenderSidebar(); RenderHeader(); RenderBanner(); } });
        };
        Model.OpenRequested += path => Dispatcher.UIThread.Post(() => OpenTrip(path));
        poll = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        poll.Tick += async (_, _) => { if (FocusManager?.GetFocusedElement() is not TextBox && DialogLayer.Children.Count == 0) await Model.PollAsync(); };
        poll.Start();
        Closed += (_, _) => { poll.Stop(); if (Model.Workspace is not null) OpenWindows.Remove(Model.Workspace.Repository.Root); Model.Dispose(); };
        if (PlatformSettings is { } platform) platform.ColorValuesChanged += (_, _) => Dispatcher.UIThread.Post(Render);
        SizeChanged += (_, _) => UpdateScale();
        KeyDown += OnKeyDown;
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, (_, e) => { if (Model.HasTrip && e.DataTransfer.Contains(DataFormat.File)) e.DragEffects = DragDropEffects.Copy; });
        AddHandler(DragDrop.DropEvent, async (_, e) =>
        {
            if (!Model.HasTrip || e.Handled) return;
            var files = e.DataTransfer.TryGetFiles()?.Select(f => f.TryGetLocalPath()).OfType<string>().ToArray();
            if (files is { Length: > 0 }) { e.Handled = true; await Model.AddFilesAsync(files); }
        });
        if (OperatingSystem.IsWindows())
        {
            ExtendClientAreaToDecorationsHint = true; ExtendClientAreaTitleBarHeightHint = 34; TitleBar.IsVisible = true;
            TitleBar.PointerPressed += (_, e) => { if (e.ClickCount == 2) WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized; else BeginMoveDrag(e); };
        }
        Render();
    }

    private void OpenTrip(string path)
    {
        path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        if (OpenWindows.TryGetValue(path, out var existing)) { existing.Activate(); return; }
        if (Model.Workspace is not null) { var window = new MainWindow(); window.Show(); window.OpenTrip(path); return; }
        _ = Model.RunAsync(async () => { await Model.OpenAsync(path); OpenWindows[path] = this; Render(); });
    }
    public void CloseTrip()
    {
        if (Model.Workspace is not null) OpenWindows.Remove(Model.Workspace.Repository.Root);
        Model.CloseTrip();
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (DialogLayer.Children.Count > 0 || e.Handled) return;
        var typing = FocusManager?.GetFocusedElement() is TextBox or AutoCompleteBox or NumericUpDown;
        if (e.Key == Key.Escape && Model.Selected is not null && !typing) { Model.Selected = null; e.Handled = true; }
        if (e.Key == Key.Delete && Model.Selected is { Type: not "trip" } && !typing) { _ = Model.DeleteAsync(Model.Selected.Id); e.Handled = true; }
        if (e.Key == Key.I && e.KeyModifiers == KeyModifiers.Control && Model.HasTrip) { Model.InboxOpen = !Model.InboxOpen; e.Handled = true; }
        if (e.KeyModifiers == KeyModifiers.Alt && Model.HasTrip && e.Key is >= Key.D1 and <= Key.D9)
        {
            var index = e.Key - Key.D1; var keys = SidebarView.Primary; if (index < keys.Count) { Model.Navigate(keys[index]); e.Handled = true; }
        }
    }

    private void QueueRender()
    {
        if (renderQueued) return; renderQueued = true;
        Dispatcher.UIThread.Post(() => { renderQueued = false; Render(); }, DispatcherPriority.Render);
    }

    public bool HighContrast => Model.Store.Get("highcontrast") == "true" || PlatformSettings?.GetColorValues().ContrastPreference == Avalonia.Platform.ColorContrastPreference.High;

    private void ApplyTheme()
    {
        RequestedThemeVariant = Model.Store.Get("theme") switch { "Light" => ThemeVariant.Light, "Dark" => ThemeVariant.Dark, _ => ThemeVariant.Default };
        Classes.Set("compact", Model.Store.Get("density") == "Compact");
        foreach (var key in HighContrastKeys) Resources.Remove(key);
        if (HighContrast)
        {
            var dark = ActualThemeVariant == ThemeVariant.Dark;
            IBrush fg = dark ? Brushes.White : Brushes.Black, bg = dark ? Brushes.Black : Brushes.White, accent = dark ? Brushes.Yellow : new SolidColorBrush(Color.Parse("#00507A"));
            foreach (var key in HighContrastKeys)
                Resources[key] = key switch
                {
                    "Text" or "Text.Muted" or "Text.Subtle" or "Line" or "Line.Strong" or "Accent.SoftText" => fg,
                    "Accent" or "Accent.Hover" or "Accent.Pressed" => accent,
                    "Text.OnAccent" => bg,
                    _ when key.EndsWith(".Fg", StringComparison.Ordinal) || key.EndsWith(".Bar", StringComparison.Ordinal) => fg,
                    _ => bg
                };
        }
        Title = Model.HasTrip ? Model.TripTitle + " – Jourfold" : "Jourfold"; TitleText.Text = Title;
    }
    private static readonly string[] HighContrastKeys = ["Bg.App", "Bg.Surface", "Bg.Raised", "Bg.Subtle", "Bg.Muted", "Bg.Sidebar", "Bg.Grid", "Line", "Line.Soft", "Line.Strong", "Text", "Text.Muted", "Text.Subtle", "Text.OnAccent", "Accent", "Accent.Hover", "Accent.Pressed", "Accent.Soft", "Accent.SoftHover", "Accent.SoftText",
        "Kind.Activity.Bg", "Kind.Activity.Fg", "Kind.Activity.Bar", "Kind.Transport.Bg", "Kind.Transport.Fg", "Kind.Transport.Bar", "Kind.Accommodation.Bg", "Kind.Accommodation.Fg", "Kind.Accommodation.Bar", "Kind.Food.Bg", "Kind.Food.Fg", "Kind.Food.Bar",
        "Kind.Sightseeing.Bg", "Kind.Sightseeing.Fg", "Kind.Sightseeing.Bar", "Kind.Culture.Bg", "Kind.Culture.Fg", "Kind.Culture.Bar", "Kind.Nature.Bg", "Kind.Nature.Fg", "Kind.Nature.Bar", "Kind.Other.Bg", "Kind.Other.Fg", "Kind.Other.Bar"];

    private void UpdateScale()
    {
        var scale = Model.Store.Get("textsize") switch { "Large" => 1.2, "Larger" => 1.4, _ => 1.0 };
        RootLayout.LayoutTransform = scale == 1 ? null : new ScaleTransform(scale, scale);
        WorkspaceLayout.Width = ClientSize.Width / scale; WorkspaceLayout.Height = ClientSize.Height / scale;
    }

    public void Render()
    {
        ApplyTheme(); UpdateScale();
        LibraryHost.IsVisible = !Model.HasTrip; TripShell.IsVisible = Model.HasTrip;
        if (!Model.HasTrip) { LibraryHost.Content = LibraryView.Build(this); return; }
        LibraryHost.Content = null;
        RenderSidebar(); RenderHeader(); RenderBanner(); RenderMain(); RenderInspector(force: false);
    }
    private void RenderSidebar() { if (Model.HasTrip) Sidebar.Child = SidebarView.Build(this); }
    private void RenderHeader() { if (Model.HasTrip) Header.Child = HeaderView.Build(this); }
    private void RenderBanner() { Banner.Child = Model.HasPrivacyWarning ? HeaderView.PrivacyBanner(this) : Model.Notice.Length > 0 ? HeaderView.MergeNotice(this) : null; }

    public void RenderToolbar() { if (Model.HasTrip) ToolbarHost.Child = ViewToolbar.Build(this); }
    /// <summary>Rebuild the main view, keeping each view's scroll position.</summary>
    public void RenderMain()
    {
        if (!Model.HasTrip) return;
        if (MainContent.Content is Control old && old.Tag is string oldKey && old.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault() is { } oldScroll) scrollOffsets[oldKey] = oldScroll.Offset;
        ToolbarHost.Child = ViewToolbar.Build(this);
        InboxHost.Child = Model.InboxOpen ? InboxView.Build(this) : null;
        var key = Model.View + (Model.View == "Plan" ? ":" + Model.StartDate + ":" + Model.Zoom + ":" + Model.Lanes : "");
        Control content = Model.View switch
        {
            "Plan" => new TimetableView(this),
            "List" => AgendaView.Build(this),
            "Map" => new MapView(this),
            "History" => HistoryView.Build(this),
            "Variants" => VariantsView.Build(this),
            _ => CollectionViews.Build(this)
        };
        content.Tag = key; MainContent.Content = content;
        if (scrollOffsets.TryGetValue(key, out var offset))
            Dispatcher.UIThread.Post(() => { if (MainContent.Content == content && content.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault() is { } scroll) scroll.Offset = offset; }, DispatcherPriority.Loaded);
    }

    /// <summary>Rebuild the inspector unless the user is typing in it for the same item.</summary>
    public void RenderInspector(bool force = true)
    {
        var selected = Model.Selected;
        InspectorHost.IsVisible = selected is not null;
        if (selected is null) { InspectorHost.Child = null; inspectorFor = null; return; }
        var focused = FocusManager?.GetFocusedElement() as Control;
        var typing = focused is TextBox && focused.GetVisualAncestors().Contains(InspectorHost);
        if (typing && inspectorFor == selected.Id && !force) return;
        if (typing && inspectorFor == selected.Id) return;
        var focusKey = focused?.GetVisualAncestors().Contains(InspectorHost) == true ? focused.Tag as string : null;
        InspectorHost.Child = InspectorView.Build(this, selected); inspectorFor = selected.Id;
        if (focusKey is not null) Dispatcher.UIThread.Post(() => InspectorHost.GetVisualDescendants().OfType<Control>().FirstOrDefault(c => c.Tag as string == focusKey)?.Focus(), DispatcherPriority.Background);
    }

    public void ShowToast(string message, string? action, Func<Task>? onAction)
    {
        var text = Ui.Text(message).Res(TextBlock.ForegroundProperty, "Text");
        var row = Ui.H(14, Ui.Icon("circle-check", 16, "Accent"), text);
        Border? toast = null;
        if (action is not null && onAction is not null)
        {
            var button = Ui.Button(action, null, "link"); button.Click += async (_, _) => { if (toast is not null) ToastLayer.Children.Remove(toast); await onAction(); };
            row.Children.Add(button);
        }
        var close = Ui.IconButton("x", Model.Strings["Close"], "small"); row.Children.Add(close);
        toast = new Border { Child = row, Padding = new Thickness(16, 8, 8, 8), MaxWidth = 560 }.Classed("raised");
        Avalonia.Automation.AutomationProperties.SetLiveSetting(toast, Avalonia.Automation.AutomationLiveSetting.Polite);
        close.Click += (_, _) => ToastLayer.Children.Remove(toast);
        ToastLayer.Children.Add(toast);
        while (ToastLayer.Children.Count > 3) ToastLayer.Children.RemoveAt(0);
        DispatcherTimer.RunOnce(() => ToastLayer.Children.Remove(toast), TimeSpan.FromSeconds(action is null ? 4 : 8));
    }
}
