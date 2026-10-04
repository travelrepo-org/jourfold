using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Jourfold.Desktop;

public sealed partial class Dialogs
{
    /// <summary>
    /// How to let an AI assistant work on the open trip: the MCP configuration to paste into the assistant, a read-only
    /// choice, what happens to the assistant's changes, and the agent guide for assistants without MCP.
    /// </summary>
    public async Task ConnectAssistantAsync(MainViewModel model)
    {
        var s = model.Strings;
        var readOnly = new CheckBox { Content = s["AssistantReadOnly"], IsChecked = false };
        var configuration = new TextBox { Text = model.AssistantConfiguration(false), IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, FontFamily = FontFamily.Parse("monospace"), FontSize = 12, Tag = "assistant-configuration" };
        AutomationProperties.SetName(configuration, s["AssistantConfiguration"]);
        readOnly.IsCheckedChanged += (_, _) => configuration.Text = model.AssistantConfiguration(readOnly.IsChecked == true);

        Control Wrapped(string text, string style) => Ui.Text(text, style).Also(t => t.TextWrapping = TextWrapping.Wrap);
        var guide = Ui.Button(s["SaveAgentGuide"], "file-text", "ghost", "compact"); guide.Click += async (_, _) => await model.SaveAgentGuideAsync();
        var body = Ui.V(12,
            Wrapped(s["AssistantIntro"], "muted"),
            readOnly,
            Ui.V(6, Ui.Text(s["AssistantConfiguration"], "strong"), configuration),
            Ui.Card(Ui.V(6, Ui.Columns("Auto,*", Ui.Icon("shield", 15, "Text.Subtle").Also(i => i.VerticalAlignment = VerticalAlignment.Top), Wrapped(s["AssistantChanges"], "caption").Margin(8, 0, 0, 0)),
                Ui.Columns("Auto,*", Ui.Icon("lock", 15, "Text.Subtle").Also(i => i.VerticalAlignment = VerticalAlignment.Top), Wrapped(s["AssistantPrivacy"], "caption").Margin(8, 0, 0, 0))), 12),
            Ui.V(2, Wrapped(s["AgentGuideHint"], "caption"), Ui.H(4, guide).Also(h => h.Margin = new Avalonia.Thickness(-8, 0, 0, 0))));

        var copy = Ui.Button(s["CopyConfiguration"], "copy", "ghost"); var close = Action("Close", true);
        var sheet = new DialogSheet(s["ConnectAssistant"], Ui.V(12, body, Footer(copy, close)), 640);
        copy.Click += async (_, _) => { await CopyAsync(configuration.Text ?? ""); Toast(s["Copied"]); };
        close.Click += (_, _) => sheet.Close();
        await sheet.ShowDialog(owner);
    }
}
