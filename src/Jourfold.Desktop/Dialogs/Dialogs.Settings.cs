using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Jourfold.Desktop;

public sealed partial class Dialogs
{
    private static string settingsPage = "General";

    public async Task SettingsAsync(MainViewModel model)
    {
        var nav = Ui.V(2); nav.Width = 200; var page = Ui.V(4);
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), MinHeight = 480 };
        grid.Children.Add(nav); grid.Children.Add(new ScrollViewer { Content = page.Margin(28, 0, 6, 0), MaxHeight = 560 }.Col(1));
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
                    Row(s["ClearMapCache"], s["ClearMapCacheHint"], Run("ClearMapCache", "trash-2"));
                    break;
                case "Providers":
                    page.Children.Add(Ui.Text(s["ProvidersHint"], "muted").Margin(0, 0, 0, 8));
                    _ = model.HasGitHubTokenAsync().ContinueWith(_ => { }, TaskScheduler.Default);
                    Row("GitHub", model.GitHubConnected ? s["GitHubConnectedHint"] : s["GitHubNotConnected"], Run("ConnectGitHub", "cloud", !model.GitHubConnected));
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
