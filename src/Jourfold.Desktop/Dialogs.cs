using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using System.Diagnostics;
namespace Jourfold.Desktop;

public sealed partial class Dialogs(Window owner, Func<Localization> strings) : IInteraction
{
    private DialogSheet Window(string title, Control body, int width = 560) => new(title, body, width);
    public async Task<string?> PromptAsync(string title, string initial = "", bool multiline = false)
    {
        var text = new TextBox { Text = initial, AcceptsReturn = multiline, MinHeight = multiline ? 180 : 32 }; Avalonia.Automation.AutomationProperties.SetName(text, title);
        var ok = new Button { Content = strings()["Continue"], IsDefault = true }; var cancel = new Button { Content = strings()["Cancel"], IsCancel = true };
        var panel = new StackPanel { Spacing = 12, Children = { new TextBlock { Text = title, TextWrapping = Avalonia.Media.TextWrapping.Wrap }, text, new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { ok, cancel } } } };
        var window = Window(title, panel); ok.Click += (_, _) => window.Close(text.Text); cancel.Click += (_, _) => window.Close(null); window.Opened += (_, _) => text.Focus(); return await window.ShowDialog<string?>(owner);
    }
    public async Task<string?> ChooseAsync(string title, IReadOnlyList<Choice> choices)
    {
        var list = new ListBox { ItemsSource = choices, SelectedIndex = choices.Count > 0 ? 0 : -1, MinHeight = 100, MaxHeight = 450 }; Avalonia.Automation.AutomationProperties.SetName(list, title);
        var ok = new Button { Content = strings()["Continue"], IsDefault = true }; var cancel = new Button { Content = strings()["Cancel"], IsCancel = true };
        var window = Window(title, new StackPanel { Spacing = 12, Children = { new TextBlock { Text = title, TextWrapping = Avalonia.Media.TextWrapping.Wrap }, list, new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { ok, cancel } } } });
        ok.Click += (_, _) => { if (list.SelectedItem is Choice c) window.Close(c.Id); }; cancel.Click += (_, _) => window.Close(null); list.DoubleTapped += (_, _) => { if (list.SelectedItem is Choice c) window.Close(c.Id); }; window.Opened += (_, _) => list.Focus(); return await window.ShowDialog<string?>(owner);
    }
    public async Task<bool> ConfirmAsync(string message)
    {
        var ok = new Button { Content = strings()["Continue"], IsDefault = true, Classes = { "primary" } };
        var cancel = new Button { Content = strings()["Cancel"], IsCancel = true };
        var sheet = Window(strings()["Review"], new StackPanel { Spacing = 16, Children = { new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap }, new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { cancel, ok } } } });
        ok.Click += (_, _) => sheet.Close(true); cancel.Click += (_, _) => sheet.Close(false);
        return await sheet.ShowDialog<bool>(owner);
    }
    public async Task<string?> FolderAsync() => (await owner.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = strings()["SelectFolder"], AllowMultiple = false })).FirstOrDefault()?.TryGetLocalPath();
    public async Task<string?> FileAsync(bool save = false, string? extension = null)
    {
        if (save) return (await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = strings()["Export"], DefaultExtension = extension, SuggestedFileName = "trip." + extension, ShowOverwritePrompt = true }))?.TryGetLocalPath();
        return (await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = strings()["AddFile"], AllowMultiple = false })).FirstOrDefault()?.TryGetLocalPath();
    }
    public async Task ShowAsync(string title, string message)
    {
        var close = new Button { Content = strings()["Continue"], IsDefault = true, IsCancel = true }; var panel = new StackPanel { Spacing = 12, Children = { new ScrollViewer { MaxHeight = 520, Content = new SelectableTextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap } }, close } }; var w = Window(title, panel, 680); close.Click += (_, _) => w.Close(); await w.ShowDialog(owner);
    }
    public async Task CompareAsync(TravelRepo.Core.TripSnapshot current, TravelRepo.Core.TripSnapshot incoming)
    {
        var close = new Button { Content = strings()["Continue"], IsCancel = true, IsDefault = true, HorizontalAlignment = HorizontalAlignment.Right };
        var grid = new Grid { RowDefinitions = new RowDefinitions("*,Auto") }; grid.Children.Add(new ComparisonView(current, incoming, strings())); Grid.SetRow(close, 1); grid.Children.Add(close);
        grid.Height = 620; var window = Window(strings()["Compare"], grid, 1080);
        close.Click += (_, _) => window.Close(); await window.ShowDialog(owner);
    }
    public async Task CopyAsync(string value) { if (owner.Clipboard is not null) await owner.Clipboard.SetTextAsync(value); }
    public void Open(string path) { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
}
