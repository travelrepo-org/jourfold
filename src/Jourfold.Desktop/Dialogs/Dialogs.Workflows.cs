using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using TravelRepo.Core;
using TravelRepo.Merge;

namespace Jourfold.Desktop;

public sealed partial class Dialogs
{
    /// <summary>Ctrl+K: search trip content, jump to views and run commands from the keyboard.</summary>
    public async Task<PaletteChoice?> PaletteAsync(MainViewModel model)
    {
        var s = S; DialogSheet? sheet = null;
        var query = new TextBox { Watermark = s["PaletteWatermark"], FontSize = 15, MinHeight = 44 }.Classed("search"); AutomationProperties.SetName(query, s["SearchTitle"]);
        var results = Ui.V(2); var buttons = new List<(Button Button, PaletteChoice Choice)>(); var index = 0;
        void Highlight() { for (var i = 0; i < buttons.Count; i++) buttons[i].Button.Classes.Set("selected", i == index); if (index < buttons.Count) buttons[index].Button.BringIntoView(); }
        void Fill()
        {
            results.Children.Clear(); buttons.Clear(); index = 0;
            var found = model.PaletteSearch(query.Text ?? "");
            foreach (var group in found.GroupBy(r => r.Group))
            {
                results.Children.Add(Ui.Text(group.Key.ToUpperInvariant(), "overline").Margin(10, 10, 0, 4));
                foreach (var choice in group.Take(12))
                {
                    var content = Ui.Columns("Auto,*,Auto", Ui.Icon(choice.Icon, 16, "Text.Muted"), Ui.V(0, Ui.Line(choice.Title, "strong"), choice.Subtitle is null ? null : Ui.Line(choice.Subtitle, "caption")).Margin(12, 0), choice.Shortcut is null ? null : new Border { Child = Ui.Text(choice.Shortcut, "caption"), Padding = new Thickness(6, 1), CornerRadius = new CornerRadius(4) }.Res(Border.BackgroundProperty, "Bg.Subtle"));
                    var button = new Button { Content = content, Padding = new Thickness(10, 7) }.Classed("row").Named(choice.Title);
                    button.Click += (_, _) => sheet?.Close(choice); results.Children.Add(button); buttons.Add((button, choice));
                }
            }
            if (buttons.Count == 0) results.Children.Add(Ui.Text(s["NothingFound"], "muted").Margin(10, 12));
            Highlight();
        }
        query.TextChanged += (_, _) => Fill();
        query.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Down && buttons.Count > 0) { index = Math.Min(buttons.Count - 1, index + 1); Highlight(); e.Handled = true; }
            if (e.Key == Key.Up && buttons.Count > 0) { index = Math.Max(0, index - 1); Highlight(); e.Handled = true; }
            if (e.Key == Key.Enter && buttons.Count > 0) { sheet?.Close(buttons[index].Choice); e.Handled = true; }
        };
        Fill();
        var hint = Ui.H(14, Ui.Text("↑ ↓ " + s["PaletteNavigate"], "caption"), Ui.Text("Enter " + s["PaletteOpen"], "caption"), Ui.Text("Esc " + s["Close"], "caption"));
        var search = new Panel { Children = { query, Ui.Icon("search", 16, "Text.Subtle").Also(i => { i.HorizontalAlignment = HorizontalAlignment.Left; i.Margin = new Thickness(12, 0, 0, 0); }) } };
        sheet = new DialogSheet(s["SearchTitle"], Ui.V(10, search, new ScrollViewer { Content = results, MaxHeight = 420 }, hint), 640, top: true) { ShowClose = false };
        sheet.Opened += (_, _) => query.Focus();
        return await sheet.ShowDialog<PaletteChoice>(owner);
    }

    /// <summary>Create Version: suggested message, editable, with the semantic change list.</summary>
    public async Task<string?> CreateVersionAsync(string suggestion, ChangeSummary changes)
    {
        var s = S; var model = Main?.Model;
        var message = new TextBox { Text = suggestion, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 80 }; AutomationProperties.SetName(message, s["VersionMessage"]);
        var list = Ui.V(6); if (model is not null) foreach (var row in HistoryView.ChangeRows(model, changes)) list.Children.Add(row);
        if (changes.IsEmpty) list.Children.Add(Ui.Text(s["OnlyFormatChanges"], "caption"));
        var create = Ui.Button(s["Version"], "save", "primary"); create.IsDefault = true; var cancel = Action("Cancel");
        var body = Ui.V(14, Ui.V(6, Ui.Text(s["Description"], "label"), message, Ui.Text(s["VersionMessageHint"], "caption")),
            Ui.V(8, Ui.Text(string.Format(s["ChangesCount"], changes.Count), "label"), Ui.Card(new ScrollViewer { Content = list, MaxHeight = 260 }, 12)), Footer(cancel, create));
        var sheet = new DialogSheet(s["Version"], body, 580, s["VersionSubtitle"]);
        create.Click += (_, _) => { if (!string.IsNullOrWhiteSpace(message.Text)) sheet.Close(message.Text); };
        cancel.Click += (_, _) => sheet.Close();
        sheet.Opened += (_, _) => { message.Focus(); message.SelectAll(); };
        return await sheet.ShowDialog<string>(owner);
    }

    public async Task CompareAsync(TripSnapshot current, TripSnapshot incoming, string currentTitle, string otherTitle)
    {
        var close = Action("Close", true);
        var view = new ComparisonView(current, incoming, S, currentTitle, otherTitle, Main?.Model) { Height = Math.Max(360, owner.Bounds.Height - 260) };
        var sheet = new DialogSheet(string.Format(S["CompareTitle"], currentTitle, otherTitle), Ui.V(12, view, Footer(close)), 1180);
        close.Click += (_, _) => sheet.Close();
        await sheet.ShowDialog(owner);
    }

    /// <summary>All merge conflicts in one sheet, each resolved with domain-language choices.</summary>
    public async Task<IReadOnlyList<ConflictAnswer>?> ResolveConflictsAsync(IReadOnlyList<ConflictPrompt> conflicts, string currentTitle, string otherTitle)
    {
        var s = S; var answers = new ConflictAnswer?[conflicts.Count];
        var apply = Ui.Button(s["ApplyMerge"], "git-merge", "primary"); apply.IsDefault = true; apply.IsEnabled = false; var cancel = Action("Cancel");
        var progress = Ui.Text("", "caption");
        void Update() { var done = answers.Count(a => a is not null); apply.IsEnabled = done == conflicts.Count; progress.Text = string.Format(s["ConflictsResolved"], done, conflicts.Count); }
        var list = Ui.V(12);
        for (var i = 0; i < conflicts.Count; i++)
        {
            var index = i; var conflict = conflicts[i];
            var options = Ui.V(6); var edit = new TextBox { Text = conflict.CurrentRaw, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, IsVisible = false, MinHeight = 60 }; AutomationProperties.SetName(edit, s["EditResult"]);
            var buttons = new List<Button>();
            void Select(Button chosen, ConflictAnswer answer) { foreach (var b in buttons) b.Classes.Set("selected", b == chosen); answers[index] = answer; edit.IsVisible = answer.Action == "EditResult"; Update(); }
            foreach (var action in conflict.Actions)
            {
                var (icon, label, detail) = action switch
                {
                    "UseCurrent" => ("check", string.Format(s["KeepFrom"], currentTitle), conflict.Current),
                    "UseVariant" => ("arrow-right", string.Format(s["TakeFrom"], otherTitle), conflict.Incoming),
                    "KeepBoth" => ("copy", s["KeepBoth"], s["KeepBothHint"]),
                    _ => ("pencil", s["EditResult"], s["EditResultHint"])
                };
                var button = new Button { Content = Ui.Columns("Auto,*", Ui.Icon(icon, 16, "Accent"), Ui.V(1, Ui.Text(label, "strong"), Ui.Text(detail, "caption")).Margin(10, 0, 0, 0)), HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(12, 8) }.Classed("tile").Named(label);
                button.Click += (_, _) => Select(button, new ConflictAnswer(action, action == "EditResult" ? edit.Text : null));
                buttons.Add(button); options.Children.Add(button);
            }
            edit.TextChanged += (_, _) => { if (answers[index]?.Action == "EditResult") answers[index] = new("EditResult", edit.Text); };
            list.Children.Add(Ui.Card(Ui.V(10, Ui.Columns("Auto,*", Ui.Icon("triangle-alert", 16, "Warning"), Ui.V(1, Ui.Text(conflict.Title, "title"), Ui.Text(string.Format(s["ConflictField"], conflict.Field), "caption")).Margin(10, 0, 0, 0)), options, edit), 14));
        }
        Update();
        var sheet = new DialogSheet(s["ResolveConflicts"], Ui.V(14, Ui.Text(string.Format(s["ResolveConflictsBody"], otherTitle, currentTitle), "muted"), new ScrollViewer { Content = list, MaxHeight = 520 }, Ui.Columns("*,Auto", progress, Ui.H(8, cancel, apply))), 720);
        apply.Click += (_, _) => sheet.Close(answers.Select(a => a!).ToArray() as IReadOnlyList<ConflictAnswer>);
        cancel.Click += (_, _) => sheet.Close();
        return await sheet.ShowDialog<IReadOnlyList<ConflictAnswer>>(owner);
    }

    /// <summary>Share adapts to where the trip lives: publish, invite on GitHub, or copy a link for a generic Git remote.</summary>
    public async Task ShareAsync(MainViewModel model)
    {
        var s = S; var remotes = await model.RemotesAsync(); var close = Action("Close");
        var body = Ui.V(16); DialogSheet? sheet = null;
        if (remotes.Count == 0)
        {
            await model.HasGitHubTokenAsync();
            var name = new TextBox { Text = MainViewModel.FolderName(model.TripTitle).Replace(' ', '-').ToLowerInvariant() }; AutomationProperties.SetName(name, s["RepositoryName"]);
            var publish = Ui.Button(s["PublishPrivately"], "lock", "primary"); publish.Click += async (_, _) => { if (string.IsNullOrWhiteSpace(name.Text)) return; sheet?.Close(); await model.PublishToGitHubAsync(name.Text); };
            body.Children.Add(Ui.Card(Ui.V(10, Ui.Columns("Auto,*", Ui.Tile("cloud", "Transport", 36), Ui.V(2, Ui.Text(s["PublishToGitHub"], "h3"), Ui.Text(model.GitHubConnected ? (model.GitHubLogin is { } login ? string.Format(s["PublishToGitHubAs"], login) : s["PublishToGitHubConnected"]) : s["PublishToGitHubHint"], "caption")).Margin(12, 0, 0, 0)), Ui.V(6, Ui.Text(s["RepositoryName"], "label"), name), Ui.H(6, Ui.Icon("lock", 13, "Success"), Ui.Text(s["PrivateByDefault"], "caption")), publish), 16));
            var url = new TextBox { Watermark = s["RemoteUrlHint"] }; AutomationProperties.SetName(url, s["RemoteUrl"]);
            var connect = Ui.Button(s["ConnectAndUpload"], "upload"); connect.Click += async (_, _) => { if (string.IsNullOrWhiteSpace(url.Text)) return; sheet?.Close(); await model.AddRemoteAsync("origin", url.Text); };
            body.Children.Add(Ui.Card(Ui.V(10, Ui.Columns("Auto,*", Ui.Tile("git-branch", "Meeting", 36), Ui.V(2, Ui.Text(s["UseOwnGit"], "h3"), Ui.Text(s["UseOwnGitHint"], "caption")).Margin(12, 0, 0, 0)), url, connect), 16));
            var export = Ui.Button(s["Export"], "download", "ghost"); export.Click += (_, _) => { sheet?.Close(); model.ExportCommand.Execute(null); };
            body.Children.Add(Ui.Columns("*,Auto", Ui.Text(s["ShareExportHint"], "caption"), export));
        }
        else
        {
            var github = remotes.Values.FirstOrDefault(MainViewModel.IsGitHubUrl);
            if (model.HasPrivacyWarning) body.Children.Add(new Border { Padding = new Thickness(12), CornerRadius = new CornerRadius(10), Child = Ui.Columns("Auto,*", Ui.Icon("triangle-alert", 16, "Warning"), Ui.Text(model.PrivacyWarning).Margin(10, 0, 0, 0)) }.Res(Border.BackgroundProperty, "Warning.Soft"));
            if (github is not null)
            {
                var user = new TextBox { Watermark = s["GitHubUsername"] }; AutomationProperties.SetName(user, s["GitHubUsername"]);
                var invite = Ui.Button(s["SendInvite"], "user-plus", "primary"); invite.Click += async (_, _) => { if (string.IsNullOrWhiteSpace(user.Text)) return; var name = user.Text; user.Text = ""; await model.InviteAsync(name); };
                body.Children.Add(Ui.Card(Ui.V(10, Ui.Columns("Auto,*", Ui.Tile("user-plus", "Transport", 36), Ui.V(2, Ui.Text(s["InviteTitle"], "h3"), Ui.Text(string.Format(s["InviteHint"], MainViewModel.GitHubName(github)), "caption")).Margin(12, 0, 0, 0)), Ui.Columns("*,Auto", user, invite.Margin(8, 0, 0, 0))), 16));
            }
            var text = await model.ShareTextAsync();
            var preview = new SelectableTextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 12 }.Classed("muted");
            var copy = Ui.Button(s["CopyInvitation"], "copy", github is null ? "primary" : ""); copy.Click += async (_, _) => { await CopyAsync(text); Toast(s["LinkCopied"]); };
            body.Children.Add(Ui.Card(Ui.V(10, Ui.Columns("Auto,*", Ui.Tile("link", "Meeting", 36), Ui.V(2, Ui.Text(s["ShareLinkTitle"], "h3"), Ui.Text(s["ShareNotice"], "caption")).Margin(12, 0, 0, 0)), new Border { Child = preview, Padding = new Thickness(12), CornerRadius = new CornerRadius(8) }.Res(Border.BackgroundProperty, "Bg.Subtle"), copy), 16));
            var addRemote = Ui.Button(s["AddRemote"], "plus", "ghost", "compact"); addRemote.Click += (_, _) => { sheet?.Close(); model.AddRemoteCommand.Execute(null); };
            body.Children.Add(Ui.Columns("*,Auto", Ui.Text(string.Join(", ", remotes.Keys), "caption"), addRemote));
        }
        body.Children.Add(Footer(close));
        sheet = new DialogSheet(model.SharingLabel, body, 600, remotes.Count == 0 ? s["ShareLocalSubtitle"] : s["ShareRemoteSubtitle"]);
        close.Click += (_, _) => sheet.Close();
        await sheet.ShowDialog(owner);
    }
}
