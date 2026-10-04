using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace Jourfold.Desktop;

public sealed partial class Dialogs
{
    private static string settingsPage = "General";

    public async Task SettingsAsync(MainViewModel model)
    {
        var nav = Ui.V(2); nav.Width = 200; var page = Ui.V(4);
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), MinHeight = 480 };
        grid.Children.Add(nav); grid.Children.Add(new ScrollViewer { Content = page.Margin(28, 0, 6, 0), MaxHeight = 560 }.Col(1));
        // GitHub is checked once per opening of Settings; the page redraws when the answer arrives.
        var gitHubChecked = false; var avatarRequested = false; Bitmap? avatar = null;
        var pages = new[] { ("General", "sliders-horizontal"), ("Appearance", "palette"), ("LocalIdentity", "user"), ("MapsAndPlaces", "map"), ("Providers", "cloud"), ("Plugins", "plug"), ("Advanced", "wrench") };
        void Render()
        {
            var s = model.Strings; nav.Children.Clear(); page.Children.Clear();
            foreach (var (key, icon) in pages) { var b = SidebarView.NavButton(s[key], icon, settingsPage == key); b.Click += (_, _) => { settingsPage = key; Render(); }; nav.Children.Add(b); }
            page.Children.Add(Ui.Text(s[settingsPage], "h2").Margin(0, 0, 0, 12));
            void Row(string title, string? description, Control control)
            {
                var row = Ui.Columns("*,Auto", Ui.V(2, Ui.Text(title, "strong"), description is null ? null : Ui.Text(description, "caption")).Margin(0, 0, 16, 0), control.Also(c => { c.VerticalAlignment = VerticalAlignment.Center; if (c.Width is double.NaN && c is ComboBox) c.Width = 220; }));
                page.Children.Add(new Border { Child = row, Padding = new Thickness(0, 12), BorderThickness = new Thickness(0, 0, 0, 1) }.Res(Border.BorderBrushProperty, "Line.Soft"));
            }
            ComboBox Option(string key, string fallback, params string[] values)
            {
                var choices = values.Select(v => new Choice(v, s[v])).ToArray();
                var combo = new ComboBox { ItemsSource = choices, SelectedItem = choices.FirstOrDefault(c => c.Id == model.Preference(key, fallback)), Width = 220, Tag = key }; AutomationProperties.SetName(combo, s[key]);
                combo.SelectionChanged += (_, _) => { if (combo.SelectedItem is Choice c && c.Id != model.Preference(key, fallback)) { model.SetPreference(key, c.Id); if (key is "Language") Render(); } };
                return combo;
            }
            Control Segments(string key, string fallback, params (string Value, string Icon)[] values) => ViewToolbar.Segmented(values.Select(v => (v.Value, s[v.Value], (string?)v.Icon)), model.Preference(key, fallback), v => { model.SetPreference(key, v); Render(); });
            ToggleSwitch Toggle(bool value, Action<bool> changed, string label) { var t = new ToggleSwitch { IsChecked = value, OnContent = null, OffContent = null }; AutomationProperties.SetName(t, label); t.IsCheckedChanged += (_, _) => changed(t.IsChecked == true); return t; }
            Button Run(string key, string icon, bool primary = false, string? label = null) { var b = Ui.Button(label ?? s[key], icon, primary ? "primary" : ""); b.Click += async (_, _) => { b.IsEnabled = false; try { await model.SettingsActionAsync(key); } finally { b.IsEnabled = true; Render(); } }; return b; }
            void RenderLater() => Dispatcher.UIThread.Post(() => { if (settingsPage == "Providers") Render(); });
            Control GitHubCard()
            {
                Control Title(Control? badge) => Ui.H(10, Ui.Text("GitHub", "h3"), badge);
                if (!gitHubChecked)
                {
                    gitHubChecked = true;
                    _ = model.RefreshGitHubAccountAsync().ContinueWith(_ => RenderLater(), TaskScheduler.Default);
                    return Ui.Card(Ui.Columns("Auto,*", Ui.Tile("cloud", "Transport", 44), Ui.V(4, Title(null), Ui.Text(s["Checking"], "muted")).Margin(14, 0, 0, 0)), 16);
                }
                if (!model.GitHubConnected)
                {
                    var connect = Run("ConnectGitHub", "cloud", true).Also(b => b.VerticalAlignment = VerticalAlignment.Center);
                    return Ui.Card(Ui.Columns("Auto,*,Auto", Ui.Tile("cloud", "Transport", 44), Ui.V(4, Title(null), Ui.Text(s["GitHubAccountHint"], "muted").Also(t => t.TextWrapping = TextWrapping.Wrap)).Margin(14, 0, 14, 0), connect), 16);
                }
                var account = model.GitHubAccount; var login = model.GitHubLogin;
                if (!avatarRequested && account?.AvatarUrl is not null)
                {
                    avatarRequested = true;
                    _ = model.GitHubAvatarAsync().ContinueWith(t =>
                    {
                        if (t.Result is not { } bytes) return;
                        Dispatcher.UIThread.Post(() => { try { avatar = new Bitmap(new MemoryStream(bytes)); } catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException) { return; } RenderLater(); });
                    }, TaskScheduler.Default);
                }
                var badge = Ui.Pill(s["Connected"], "circle-check", "Success.Soft", "Success");
                var who = Ui.Text(login is null ? s["GitHubSignedIn"] : string.Format(s["GitHubSignedInAs"], login), "strong");
                var name = account?.Name is { } full ? Ui.Text(full, "muted") : null;
                var session = model.GitHubSessionOnly ? Ui.Text(s["SessionOnly"], "caption").Also(t => t.TextWrapping = TextWrapping.Wrap) : null;
                var profile = account?.ProfileUrl is { } url ? Ui.Button(s["GitHubProfile"], "external-link", "ghost").Also(b => b.Click += (_, _) => Open(url.AbsoluteUri)) : null;
                var disconnect = Ui.Button(s["Disconnect"], "log-out", "ghost");
                disconnect.Click += async (_, _) => { disconnect.IsEnabled = false; await model.DisconnectGitHubCommand.ExecuteAsync(null); avatar = null; avatarRequested = false; Render(); };
                var actions = Ui.H(4, profile, disconnect).Also(h => h.VerticalAlignment = VerticalAlignment.Center);
                return Ui.Card(Ui.Columns("Auto,*,Auto", Ui.Avatar(account?.Name ?? login ?? "GitHub", 44, avatar), Ui.V(3, Title(badge), who, name, session).Margin(14, 0, 14, 0), actions), 16);
            }
            // Own map servers, for example a commercial provider with a key in the URL. Empty fields use the default servers.
            Control MapServers()
            {
                var store = model.Store; var current = Jourfold.Infrastructure.MapProviders.Current(store);
                TextBox Box(string key, string label, string watermark) { var box = new TextBox { Text = store.Get(key) ?? "", Watermark = watermark, Tag = key }; AutomationProperties.SetName(box, label); return box; }
                var defaults = Jourfold.Infrastructure.MapProvider.OpenStreetMap;
                var tiles = Box(Jourfold.Infrastructure.MapProviders.TilesKey, s["MapTilesServer"], defaults.TilesUrl);
                var attribution = Box(Jourfold.Infrastructure.MapProviders.AttributionKey, s["MapAttribution"], defaults.Attribution);
                var search = Box(Jourfold.Infrastructure.MapProviders.SearchKey, s["MapSearchServer"], defaults.SearchUrl);
                var problem = Ui.Text(s["MapServersInvalid"], "caption").Res(TextBlock.ForegroundProperty, "Danger").Also(t => { t.IsVisible = false; t.TextWrapping = TextWrapping.Wrap; });
                var save = Ui.Button(s["Save"], "check", "primary"); var reset = Ui.Button(s["UseDefaultServers"], "rotate-ccw", "ghost");
                save.Click += (_, _) =>
                {
                    string Or(TextBox box, string fallback) => string.IsNullOrWhiteSpace(box.Text) ? fallback : box.Text.Trim();
                    var candidate = current with { TilesUrl = Or(tiles, current.TilesUrl), Attribution = Or(attribution, current.Attribution), SearchUrl = Or(search, current.SearchUrl) };
                    if (!candidate.IsValid) { problem.IsVisible = true; return; }
                    store.Set(Jourfold.Infrastructure.MapProviders.TilesKey, tiles.Text?.Trim() ?? ""); store.Set(Jourfold.Infrastructure.MapProviders.AttributionKey, attribution.Text?.Trim() ?? ""); store.Set(Jourfold.Infrastructure.MapProviders.SearchKey, search.Text?.Trim() ?? "");
                    Toast(s["MapServersSaved"]); model.RaiseContentChanged(); Render();
                };
                reset.Click += (_, _) => { foreach (var key in new[] { Jourfold.Infrastructure.MapProviders.TilesKey, Jourfold.Infrastructure.MapProviders.AttributionKey, Jourfold.Infrastructure.MapProviders.SearchKey }) store.Set(key, ""); model.RaiseContentChanged(); Render(); };
                var form = Ui.V(10, Ui.Text(s["MapServersHint"], "caption").Also(t => t.TextWrapping = TextWrapping.Wrap),
                    Ui.Field(s["MapTilesServer"], tiles), Ui.Field(s["MapAttribution"], attribution), Ui.Field(s["MapSearchServer"], search), problem, Ui.H(8, save, reset));
                var source = Jourfold.Infrastructure.MapProviders.HasOwnServers(store) ? s["MapServersOwn"] : s["MapServersDefault"];
                return new Expander { Header = Ui.V(2, Ui.Text(s["MapServers"], "strong"), Ui.Text(string.Format(source, new Uri(current.TilesUrl.Replace("{z}", "0").Replace("{x}", "0").Replace("{y}", "0")).Host), "caption")), Content = form, HorizontalAlignment = HorizontalAlignment.Stretch, IsExpanded = Jourfold.Infrastructure.MapProviders.HasOwnServers(store) };
            }
            switch (settingsPage)
            {
                case "General":
                    Row(s["Language"], s["LanguageHint"], Option("Language", model.Strings.Language, "en", "de"));
                    Row(s["OpenView"], s["OpenViewHint"], Option("OpenView", "Remember", "Remember", "Plan", "Map", "List"));
                    Row(s["Units"], s["UnitsHint"], Option("Units", "Metric", "Metric", "Imperial"));
                    var folder = Ui.Button(model.DefaultTripsFolder, "folder-open", "ghost"); folder.MaxWidth = 280; folder.Click += async (_, _) => { if (await FolderAsync(s["TripsFolder"]) is { } chosen) { model.Store.Set("trips.folder", chosen); Render(); } };
                    Row(s["TripsFolder"], s["TripsFolderHint"], folder);
                    break;
                case "Appearance":
                    Row(s["Theme"], s["ThemeHint"], Segments("Theme", "System", ("System", "monitor"), ("Light", "sun"), ("Dark", "moon")));
                    Row(s["Density"], s["DensityHint"], Segments("Density", "Comfortable", ("Comfortable", "layout-grid"), ("Compact", "list")));
                    Row(s["TextSize"], s["TextSizeHint"], Option("TextSize", "Normal", "Normal", "Large", "Larger"));
                    Row(s["HighContrast"], s["HighContrastHint"], Toggle(model.Store.Get("highcontrast") == "true", v => model.SetPreference("HighContrast", v ? "true" : "false"), s["HighContrast"]));
                    break;
                case "LocalIdentity":
                    page.Children.Add(Ui.Text(s["IdentityHint"], "muted").Margin(0, 0, 0, 12));
                    var name = new TextBox { Text = model.Store.Get("identity.name") ?? "", Watermark = s["YourName"] }; AutomationProperties.SetName(name, s["YourName"]);
                    var email = new TextBox { Text = model.Store.Get("identity.email") ?? "", Watermark = s["EmailHint"] }; AutomationProperties.SetName(email, s["Email"]);
                    var save = Ui.Button(s["Save"], "check", "primary"); save.Click += async (_, _) => await model.SaveIdentityAsync(name.Text ?? "", email.Text ?? "");
                    page.Children.Add(Ui.V(12, Ui.Field(s["YourName"], name), Ui.Field(s["EmailOptional"], email), save.Also(b => b.HorizontalAlignment = HorizontalAlignment.Left)));
                    break;
                case "MapsAndPlaces":
                    page.Children.Add(Ui.Card(Ui.Columns("Auto,*", Ui.Icon("shield", 18, "Accent"), Ui.Text(s["OnlineMapsPrivacy"], "muted").Margin(12, 0, 0, 0)), 14));
                    Row(s["OnlineMaps"], s["OnlineMapsHint"], Toggle(model.OnlineMaps, model.SetOnlineMaps, s["OnlineMaps"]));
                    Row(s["ClearMapCache"], string.Format(s["MapCacheSize"], Formats.Bytes(Jourfold.Infrastructure.MapTiles.CacheSize(model.Store.Root)), Formats.Bytes(Jourfold.Infrastructure.MapTiles.MaxCacheBytes)), Run("ClearMapCache", "trash-2"));
                    page.Children.Add(MapServers().Margin(0, 12, 0, 0));
                    break;
                case "Providers":
                    page.Children.Add(Ui.Text(s["ProvidersHint"], "muted").Margin(0, 0, 0, 8));
                    page.Children.Add(GitHubCard().Margin(0, 4, 0, 8));
                    Row(s["DiscoverGitHub"], s["DiscoverGitHubHint"], Run("DiscoverGitHub", "search"));
                    if (model.HasTrip) { Row(s["AddRemote"], s["UseOwnGitHint"], Run("AddRemote", "plus")); Row(s["PreferredRemote"], s["PreferredRemoteHint"], Run("PreferredRemote", "cloud")); }
                    break;
                case "Plugins":
                    page.Children.Add(Ui.Text(s["PluginsHint"], "muted").Margin(0, 0, 0, 8));
                    var known = Jourfold.Infrastructure.PluginCatalog.Known(model.Store);
                    Row(known.Count == 0 ? s["LoadPlugin"] : s["LoadAnotherPlugin"], known.Count == 0 ? s["NoPluginLoaded"] : null, Run("Plugins", "plug"));
                    if (known.Count > 0) page.Children.Add(Ui.Text(s["KnownPlugins"], "h3").Margin(0, 16, 0, 0));
                    foreach (var plugin in known)
                    {
                        var forget = Ui.IconButton("x", s["RemoveFromList"], "small");
                        forget.Click += async (_, _) => { await model.SettingsActionAsync("ForgetPlugin:" + plugin.Directory); Render(); };
                        Row(plugin.Manifest.DisplayName + " " + plugin.Manifest.Version, plugin.Manifest.Description ?? plugin.Manifest.Id, Ui.H(4, Run("UsePlugin:" + plugin.Directory, "plug", label: s["UsePlugin"]), forget));
                    }
                    break;
                case "Advanced":
                    Row(s["Advanced"], s["AdvancedHint"], Toggle(model.Advanced, v => { model.SetPreference("Advanced", v ? "true" : "false"); }, s["Advanced"]));
                    Row(s["Diagnostics"], s["DiagnosticsHint"], Run("Diagnostics", "info"));
                    if (model.HasTrip) Row(s["Repair"], s["RepairHint"], Run("Repair", "wrench"));
                    Row(s["Report"], s["ReportHint"], Run("Report", "message-square"));
                    break;
            }
        }
        Render();
        var close = Action("Close", true);
        var about = Ui.Button(model.Strings["About"], "info", "ghost");
        var footer = Ui.Columns("Auto,*", about, Footer(close).Col(1)); footer.Margin = new Thickness(0, 8, 0, 0);
        var sheet = new DialogSheet(model.Strings["Settings"], Ui.V(16, grid, footer), 920);
        close.Click += (_, _) => sheet.Close(); about.Click += async (_, _) => await AboutAsync(model);
        await sheet.ShowDialog(owner);
    }
}
