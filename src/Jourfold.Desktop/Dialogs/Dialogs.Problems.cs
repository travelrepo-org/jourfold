using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using TravelRepo.Core;

namespace Jourfold.Desktop;

public sealed partial class Dialogs
{
    public async Task<string?> ProblemAsync(Problem problem)
    {
        var message = new SelectableTextBlock { Text = problem.Message, TextWrapping = TextWrapping.Wrap };
        var details = new SelectableTextBlock { Text = problem.Details, FontFamily = FontFamily.Parse("monospace"), FontSize = 11, TextWrapping = TextWrapping.Wrap }.Classed("muted");
        AutomationProperties.SetName(details, S["TechnicalDetails"]);
        var expander = new Expander { Header = Ui.Text(S["TechnicalDetails"], "caption"), Content = new ScrollViewer { Content = details, MaxHeight = 240 }, HorizontalAlignment = HorizontalAlignment.Stretch };
        var copy = Ui.Button(S["CopyDetails"], "copy", "ghost");
        var close = Action("Close", primary: problem.Actions.Count == 0);
        DialogSheet? sheet = null;
        var buttons = new List<Control> { close };
        for (var i = 0; i < problem.Actions.Count; i++)
        {
            var choice = problem.Actions[i];
            var button = Ui.Button(choice.Label, choice.Icon, i == problem.Actions.Count - 1 ? "primary" : ""); button.IsDefault = i == problem.Actions.Count - 1;
            button.Click += (_, _) => sheet?.Close(choice.Id); buttons.Add(button);
        }
        var footer = Ui.Columns("Auto,*", copy, Footer(buttons.ToArray()).Also(f => f.Margin = new Thickness(0)));
        footer.Margin = new Thickness(0, 8, 0, 0);
        sheet = new DialogSheet(problem.Title, Ui.V(14, message, expander, footer), 600);
        close.Click += (_, _) => sheet.Close(null);
        copy.Click += async (_, _) => { await CopyAsync(problem.Message + "\n\n" + problem.Details); Toast(S["Copied"]); };
        return await sheet.ShowDialog<string?>(owner);
    }

    public async Task<IReadOnlyList<string>?> PickAsync(string title, string message, IReadOnlyList<Choice> choices, IReadOnlyList<string> selected, string confirm)
    {
        var chosen = selected.ToHashSet(); var list = Ui.V(4);
        foreach (var choice in choices)
        {
            var check = new CheckBox { Content = Ui.Columns("Auto,*", choice.Icon is null ? null : Ui.Icon(choice.Icon, 16, "Kind.Accommodation.Fg").Margin(0, 0, 10, 0), Ui.V(1, Ui.Text(choice.Label), choice.Hint is null ? null : Ui.Text(choice.Hint, "caption"))), IsChecked = chosen.Contains(choice.Id), Margin = new Thickness(2, 2) };
            AutomationProperties.SetName(check, choice.Label + (choice.Hint is null ? "" : ", " + choice.Hint));
            check.IsCheckedChanged += (_, _) => { if (check.IsChecked == true) chosen.Add(choice.Id); else chosen.Remove(choice.Id); };
            list.Children.Add(check);
        }
        var ok = Ui.Button(confirm, null, "primary"); ok.IsDefault = true; var cancel = Action("Cancel");
        var sheet = new DialogSheet(title, Ui.V(14, new SelectableTextBlock { Text = message, TextWrapping = TextWrapping.Wrap }.Classed("muted"), new ScrollViewer { Content = list, MaxHeight = 400 }, Footer(cancel, ok)), 560);
        ok.Click += (_, _) => sheet.Close(choices.Where(c => chosen.Contains(c.Id)).Select(c => c.Id).ToArray() as IReadOnlyList<string>);
        cancel.Click += (_, _) => sheet.Close(null);
        return await sheet.ShowDialog<IReadOnlyList<string>?>(owner);
    }

    public async Task<IReadOnlyList<Guid>?> ConfirmDeletionAsync(DeletionPrompt prompt)
    {
        var chosen = prompt.Preselected.ToHashSet();
        var body = Ui.V(14);
        // The kind is shown too, because a booking is often named like the stay it books.
        Control Names(IEnumerable<Entity> entities, string icon)
        {
            var list = Ui.V(4);
            foreach (var e in entities.Take(8)) list.Children.Add(Ui.H(8, Ui.Icon(icon, 14, "Text.Subtle"), Ui.Line(e.Title, ""), Ui.Text(S[e.Type], "caption")));
            return list;
        }
        if (prompt.Dependents.Count > 0)
            body.Children.Add(Ui.V(6, Ui.Text(S["DeleteAlsoDeleted"], "strong"), Names(prompt.Dependents, "trash-2")));
        if (prompt.Affected.Count > 0)
            body.Children.Add(Ui.V(6, Ui.Text(prompt.Target.Type == "place" ? S["DeletePlaceUsedBy"] : S["DeleteLinkRemovedFrom"], "strong"), Names(prompt.Affected, "link"),
                prompt.Affected.Count > 8 ? Ui.Text(string.Format(S["AndMore"], prompt.Affected.Count - 8), "caption") : null));
        if (prompt.Unused.Count > 0)
        {
            var checks = Ui.V(4);
            foreach (var e in prompt.Unused)
            {
                var check = new CheckBox { Content = Ui.V(1, Ui.Text(e.Title), Ui.Text(S[e.Type], "caption")), IsChecked = chosen.Contains(e.Id), Margin = new Thickness(2, 1) };
                AutomationProperties.SetName(check, string.Format(S["DeleteToo"], e.Title));
                check.IsCheckedChanged += (_, _) => { if (check.IsChecked == true) chosen.Add(e.Id); else chosen.Remove(e.Id); };
                checks.Children.Add(check);
            }
            body.Children.Add(Ui.V(6, Ui.Text(S["DeleteUnusedTitle"], "strong"), Ui.Text(S["DeleteUnusedBody"], "caption"), checks));
        }
        body.Children.Add(Ui.Text(S["DeleteUndoHint"], "caption"));
        var delete = Ui.Button(S["Delete"], "trash-2", "danger"); delete.IsDefault = true; var cancel = Action("Cancel");
        body.Children.Add(Footer(cancel, delete));
        var sheet = new DialogSheet(string.Format(S["DeleteTitle"], prompt.Target.Title), new ScrollViewer { Content = body, MaxHeight = 560 }, 520);
        delete.Click += (_, _) => sheet.Close(prompt.Unused.Where(e => chosen.Contains(e.Id)).Select(e => e.Id).ToArray() as IReadOnlyList<Guid>);
        cancel.Click += (_, _) => sheet.Close(null);
        return await sheet.ShowDialog<IReadOnlyList<Guid>?>(owner);
    }
}
