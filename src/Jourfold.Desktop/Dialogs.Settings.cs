using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Jourfold.Desktop;

public sealed partial class Dialogs
{
    public async Task SettingsAsync(MainViewModel model)
    {
        var categories = new StackPanel { Spacing = 5, Width = 170 };
        var page = new StackPanel { Spacing = 18, Margin = new Thickness(24, 0, 0, 0) };
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), MinHeight = 450 }; grid.Children.Add(categories); Grid.SetColumn(page, 1); grid.Children.Add(page);
        var category = "General";
        void Render()
        {
            categories.Children.Clear(); page.Children.Clear();
            foreach (var key in new[] { "General", "Appearance", "LocalIdentity", "Providers", "Plugins", "Advanced" })
            {
                var button = new Button { Content = model.Strings[key], HorizontalAlignment = HorizontalAlignment.Stretch, Classes = { "navigation" } };
                if (category == key) button.Classes.Add("selected"); ToolTip.SetTip(button, model.Strings[key]);
                button.Click += (_, _) => { category = key; Render(); }; categories.Children.Add(button);
            }
            page.Children.Add(new TextBlock { Text = model.Strings[category], FontWeight = FontWeight.SemiBold, FontSize = 20 });
            void Option(string key, string fallback, params string[] values)
            {
                page.Children.Add(new TextBlock { Text = model.Strings[key] });
                var choices = values.Select(v => new Choice(v, model.Strings[v])).ToArray();
                var combo = new ComboBox { ItemsSource = choices, SelectedItem = choices.FirstOrDefault(c => c.Id == (model.Store.Get(key == "OpenView" ? "openingView" : key.ToLowerInvariant()) ?? fallback)), HorizontalAlignment = HorizontalAlignment.Stretch, Tag = key };
                ToolTip.SetTip(combo, model.Strings[key]);
                combo.SelectionChanged += (_, _) => { if (combo.SelectedItem is Choice c) { model.SetPreference(key, c.Id); if (key == "Language") Render(); } }; page.Children.Add(combo);
            }
            void Toggle(string key) { var check = new CheckBox { Content = model.Strings[key], IsChecked = model.Store.Get(key.ToLowerInvariant()) == "true" }; check.IsCheckedChanged += (_, _) => model.SetPreference(key, check.IsChecked == true ? "true" : "false"); page.Children.Add(check); }
            void ActionButton(string key)
            {
                var button = Action(key); button.HorizontalAlignment = HorizontalAlignment.Left;
                button.Click += async (_, _) => { button.IsEnabled = false; try { await model.SettingsActionAsync(key); } finally { button.IsEnabled = true; } }; page.Children.Add(button);
            }
            switch (category)
            {
                case "General": Option("Language", model.Strings.Language, "en", "de"); Option("OpenView", "Remember", "Remember", "Plan", "Map", "List"); Option("Units", "Metric", "Metric", "Imperial"); break;
                case "Appearance": Option("Theme", "System", "System", "Light", "Dark"); Option("Density", "Comfortable", "Comfortable", "Compact"); Option("TextSize", "Normal", "Normal", "Large", "Larger"); Toggle("HighContrast"); break;
                case "LocalIdentity":
                    page.Children.Add(new TextBlock { Text = model.Strings["IdentityHint"], TextWrapping = TextWrapping.Wrap });
                    var name = new FormControl(new("name", model.Strings["Name"], Value: model.Store.Get("identity.name") ?? ""));
                    var email = new FormControl(new("email", model.Strings["Email"], Value: model.Store.Get("identity.email") ?? "")); page.Children.Add(name); page.Children.Add(email);
                    var save = Action("Save"); save.Click += async (_, _) => await model.RunAsync(async () => { model.Store.Set("identity.name", name.Value); model.Store.Set("identity.email", email.Value); if (model.Workspace is not null && name.Value.Length > 0 && email.Value.Length > 0) await model.Workspace.Git.SetIdentityAsync(name.Value, email.Value); }); page.Children.Add(save); break;
                case "Providers": ActionButton("ConnectGitHub"); ActionButton("DiscoverGitHub"); if (model.HasTrip) { ActionButton("AddRemote"); ActionButton("PreferredRemote"); } break;
                case "Plugins": ActionButton("Plugins"); break;
                case "Advanced": Toggle("Advanced"); if (model.HasTrip) { ActionButton("Diagnostics"); ActionButton("Repair"); } ActionButton("Report"); break;
            }
        }
        Render(); var close = Action("Close"); var sheet = Window(model.Strings["Settings"], new StackPanel { Spacing = 20, Children = { grid, Buttons(close) } }, 880);
        close.Click += (_, _) => sheet.Close(); await sheet.ShowDialog(owner);
    }
}
