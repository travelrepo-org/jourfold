using System.Diagnostics;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;

namespace Jourfold.Desktop;

/// <summary>In-window dialogs implementing <see cref="IInteraction"/>.</summary>
public sealed partial class Dialogs(Window owner, Func<Localization> strings) : IInteraction
{
    private Localization S => strings();
    private MainWindow? Main => owner as MainWindow;

    private Button Action(string key, bool primary = false, string? icon = null)
    {
        var button = Ui.Button(S[key], icon, primary ? "primary" : ""); button.IsDefault = primary;
        return button;
    }
    private static StackPanel Footer(params Control[] buttons) => Ui.H(8, buttons).Also(h => { h.HorizontalAlignment = HorizontalAlignment.Right; h.Margin = new Thickness(0, 8, 0, 0); });

    public async Task<string?> PromptAsync(string title, string initial = "", bool multiline = false)
    {
        var text = new TextBox { Text = initial, AcceptsReturn = multiline, TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap, MinHeight = multiline ? 180 : 36 }; AutomationProperties.SetName(text, title);
        var ok = Action("Save", true); var cancel = Action("Cancel");
        var sheet = new DialogSheet(title, Ui.V(16, text, Footer(cancel, ok)), multiline ? 640 : 480);
        ok.Click += (_, _) => sheet.Close(text.Text); cancel.Click += (_, _) => sheet.Close(null);
        sheet.Opened += (_, _) => { text.Focus(); text.SelectAll(); };
        return await sheet.ShowDialog<string?>(owner);
    }

    public async Task<string?> ChooseAsync(string title, IReadOnlyList<Choice> choices)
    {
        DialogSheet? sheet = null;
        var filter = new TextBox { Watermark = S["Filter"], IsVisible = choices.Count > 8 }.Classed("search"); AutomationProperties.SetName(filter, S["Filter"]);
        var list = Ui.V(2);
        void Fill()
        {
            list.Children.Clear();
            foreach (var choice in choices.Where(c => string.IsNullOrWhiteSpace(filter.Text) || c.Label.Contains(filter.Text, StringComparison.CurrentCultureIgnoreCase)))
            {
                var content = Ui.Columns("Auto,*,Auto", choice.Icon is null ? null : Ui.Icon(choice.Icon, 18, "Accent").Margin(0, 0, 12, 0), Ui.V(1, Ui.Line(choice.Label, "title"), choice.Hint is null ? null : Ui.Text(choice.Hint, "caption")), Ui.Icon("chevron-right", 14, "Text.Subtle"));
                var row = new Button { Content = content, Padding = new Thickness(12, 10) }.Classed("row").Named(choice.Label);
                row.Click += (_, _) => sheet?.Close(choice.Id); list.Children.Add(row);
            }
        }
        filter.TextChanged += (_, _) => Fill(); Fill();
        var cancel = Action("Cancel");
        sheet = new DialogSheet(title, Ui.V(10, filter.IsVisible ? new Panel { Children = { filter, Ui.Icon("search", 15, "Text.Subtle").Also(i => { i.HorizontalAlignment = HorizontalAlignment.Left; i.Margin = new Thickness(11, 0, 0, 0); }) } } : null, new ScrollViewer { Content = list, MaxHeight = 440 }, Footer(cancel)), 520);
        cancel.Click += (_, _) => sheet.Close(null);
        return await sheet.ShowDialog<string?>(owner);
    }

    public Task<bool> ConfirmAsync(string message) => ConfirmAsync(S["PleaseConfirm"], message, S["Continue"]);
    public async Task<bool> ConfirmAsync(string title, string message, string confirm, bool danger = false)
    {
        var ok = Ui.Button(confirm, null, danger ? "danger" : "primary"); ok.IsDefault = true; var cancel = Action("Cancel");
        var sheet = new DialogSheet(title, Ui.V(16, new SelectableTextBlock { Text = message, TextWrapping = TextWrapping.Wrap }.Classed("muted"), Footer(cancel, ok)), 500);
        ok.Click += (_, _) => sheet.Close(true); cancel.Click += (_, _) => sheet.Close(false);
        return await sheet.ShowDialog<bool>(owner);
    }

    public async Task ShowAsync(string title, string message)
    {
        var close = Action("Close", true); var copy = Ui.Button(S["Copy"], "copy", "ghost");
        var sheet = new DialogSheet(title, Ui.V(16, new ScrollViewer { MaxHeight = 480, Content = new SelectableTextBlock { Text = message, TextWrapping = TextWrapping.Wrap } }, Footer(copy, close)), 600);
        close.Click += (_, _) => sheet.Close(); copy.Click += async (_, _) => { await CopyAsync(message); Toast(S["Copied"]); };
        await sheet.ShowDialog(owner);
    }

    public async Task<string?> FolderAsync(string? title = null) => (await owner.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = title ?? S["SelectFolder"], AllowMultiple = false })).FirstOrDefault()?.TryGetLocalPath();
    public async Task<string?> FileAsync(bool save = false, string? extension = null, string? suggestedName = null)
    {
        if (save) return (await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = S["Export"], DefaultExtension = extension, SuggestedFileName = (suggestedName ?? "trip") + "." + extension, ShowOverwritePrompt = true }))?.TryGetLocalPath();
        return (await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = S["AddFile"], AllowMultiple = false })).FirstOrDefault()?.TryGetLocalPath();
    }
    public async Task CopyAsync(string value) { if (owner.Clipboard is not null) await owner.Clipboard.SetTextAsync(value); }
    public void Open(string path) { try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); } catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { Toast(S["CannotOpen"]); } }
    public void Toast(string message, string? action = null, Func<Task>? onAction = null) => Main?.ShowToast(message, action, onAction);

    public async Task<IReadOnlyDictionary<string, string>?> FormAsync(string title, IReadOnlyList<FormField> fields)
    {
        var controls = fields.Select(f => new FormControl(f)).ToArray();
        var error = Ui.Text(S["InvalidFields"]).Res(TextBlock.ForegroundProperty, "Danger"); error.IsVisible = false;
        var save = Action("Save", true); var cancel = Action("Cancel");
        var panel = Ui.V(14); foreach (var c in controls) panel.Children.Add(c); panel.Children.Add(error); panel.Children.Add(Footer(cancel, save));
        var sheet = new DialogSheet(title, panel, 520);
        save.Click += (_, _) => { if (controls.Any(c => !c.IsValid)) { error.IsVisible = true; return; } sheet.Close(controls.ToDictionary(c => c.Field.Key, c => c.Value) as IReadOnlyDictionary<string, string>); };
        cancel.Click += (_, _) => sheet.Close();
        sheet.Opened += (_, _) => controls.FirstOrDefault()?.Editor.Focus();
        return await sheet.ShowDialog<IReadOnlyDictionary<string, string>>(owner);
    }

    public async Task<IReadOnlyList<string>?> SelectManyAsync(string title, IReadOnlyList<Choice> choices, IReadOnlyList<string> selected)
    {
        var chosen = selected.ToHashSet(); var list = Ui.V(2);
        foreach (var choice in choices.OrderBy(c => c.Label, StringComparer.CurrentCulture))
        {
            var check = new CheckBox { Content = Ui.H(10, Ui.Avatar(choice.Label, 24), Ui.Text(choice.Label)), IsChecked = chosen.Contains(choice.Id), Margin = new Thickness(4, 2) };
            AutomationProperties.SetName(check, choice.Label);
            check.IsCheckedChanged += (_, _) => { if (check.IsChecked == true) chosen.Add(choice.Id); else chosen.Remove(choice.Id); };
            list.Children.Add(check);
        }
        var save = Action("Save", true); var cancel = Action("Cancel");
        var sheet = new DialogSheet(title, Ui.V(14, new ScrollViewer { Content = list, MaxHeight = 380 }, Footer(cancel, save)), 460);
        save.Click += (_, _) => sheet.Close(choices.Where(c => chosen.Contains(c.Id)).Select(c => c.Id).Concat(chosen.Where(id => choices.All(c => c.Id != id))).ToArray() as IReadOnlyList<string>);
        cancel.Click += (_, _) => sheet.Close();
        return await sheet.ShowDialog<IReadOnlyList<string>>(owner);
    }
}

internal static class PanelExtensions
{
    public static T With<T>(this T panel, params Control[] controls) where T : Panel { foreach (var control in controls) panel.Children.Add(control); return panel; }
}
