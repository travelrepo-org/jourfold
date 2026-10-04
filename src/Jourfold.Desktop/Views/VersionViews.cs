using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using TravelRepo.Core;
using TravelRepo.Git;
using TravelRepo.Merge;

namespace Jourfold.Desktop;

/// <summary>Human-readable version history with expandable semantic changes.</summary>
public static class HistoryView
{
    public static Control Build(MainWindow w)
    {
        var m = w.Model; var s = m.Strings;
        var timeline = Ui.V(0);
        var version = Ui.Button(s["Version"], "save", m.HasUncommitted ? "primary" : ""); version.Command = m.CreateVersionCommand;
        var header = Ui.Columns("*,Auto", Ui.V(4, Ui.Text(s["History"], "h1"), Ui.Text(s["HistoryHint"], "muted")), version);
        var panel = Ui.V(20, header);
        if (m.HasUncommitted)
            panel.Children.Add(new Border { Padding = new Thickness(16, 12), CornerRadius = new CornerRadius(12), Child = Ui.Columns("Auto,*", Ui.Icon("circle-dot", 18, "Warning"), Ui.V(2, Ui.Text(s["UnversionedChanges"], "title"), Ui.Text(s["UnversionedChangesHint"], "caption")).Margin(12, 0, 0, 0)) }.Res(Border.BackgroundProperty, "Warning.Soft"));
        panel.Children.Add(timeline); panel.Margin = new Thickness(32, 24, 32, 40); panel.MaxWidth = 900;
        timeline.Children.Add(Ui.Text(s["Loading"], "caption"));
        _ = LoadAsync(w, timeline);
        return new ScrollViewer { Content = panel };
    }

    private static async Task LoadAsync(MainWindow w, StackPanel timeline)
    {
        var m = w.Model; var s = m.Strings;
        IReadOnlyList<HistoryItem> items;
        try { items = await m.HistoryAsync(); }
        catch (DomainException ex) { timeline.Children.Clear(); timeline.Children.Add(Ui.Text(s.Error(ex, m.Advanced), "muted")); return; }
        timeline.Children.Clear();
        for (var i = 0; i < items.Count; i++) timeline.Children.Add(Entry(w, items[i], i == items.Count - 1, i == 0));
    }

    private static Control Entry(MainWindow w, HistoryItem item, bool last, bool latest)
    {
        var m = w.Model; var s = m.Strings;
        var icon = item.Heading switch { "HistoryMerge" => "git-merge", "HistoryCreate" => "sparkles", "HistoryNewVariant" => "git-branch", "HistoryVariant" => "pencil", "HistoryExternal" => "external-link", _ => "save" };
        var dot = new Border { Width = 32, Height = 32, CornerRadius = new CornerRadius(16), Child = Ui.Icon(icon, 15, "Accent").Also(i => i.HorizontalAlignment = HorizontalAlignment.Center) }.Res(Border.BackgroundProperty, "Accent.Soft");
        var line = new Border { Width = 2, HorizontalAlignment = HorizontalAlignment.Center, IsVisible = !last }.Res(Border.BackgroundProperty, "Line");
        var rail = new Grid { RowDefinitions = new RowDefinitions("Auto,*"), Width = 40 }; rail.Children.Add(dot); rail.Children.Add(line.Row(1));
        var headline = string.Format(s[item.Heading + "Line"], item.Author);
        var changes = Ui.V(4); changes.IsVisible = false; changes.Margin = new Thickness(0, 4, 0, 0); var loaded = false;
        var toggle = Ui.Button(s["ShowChanges"], "chevron-down", "link");
        var expander = Ui.V(4, toggle, changes);
        toggle.Click += async (_, _) =>
        {
            changes.IsVisible = !changes.IsVisible; toggle.Content = Ui.H(7, Ui.Icon(changes.IsVisible ? "chevron-up" : "chevron-down", 15), Ui.Text(s[changes.IsVisible ? "HideChanges" : "ShowChanges"]));
            if (!changes.IsVisible || loaded) return; loaded = true;
            changes.Children.Add(Ui.Text(s["Loading"], "caption"));
            try { var summary = await m.ChangesInAsync(item.Entry); changes.Children.Clear(); foreach (var row in ChangeRows(m, summary)) changes.Children.Add(row); if (summary.IsEmpty) changes.Children.Add(Ui.Text(s["NoContentChanges"], "caption")); }
            catch (DomainException ex) { changes.Children.Clear(); changes.Children.Add(Ui.Text(s.Error(ex, m.Advanced), "caption")); }
        };
        var restore = Ui.Button(s["RestoreThis"], "rotate-ccw", "ghost", "compact"); restore.IsVisible = !latest; restore.Click += (_, _) => _ = m.RestoreVersionAsync(item.Entry);
        var body = Ui.V(4,
            Ui.Columns("*,Auto", Ui.Text(headline, "title"), Ui.Text(Formats.Relative(item.When, s), "caption").Also(t => ToolTip.SetTip(t, item.When.ToLocalTime().ToString("f", CultureInfo.CurrentCulture)))),
            item.Message.Length > 0 ? Ui.Text(item.Message, "muted") : null,
            Ui.H(8, Ui.Avatar(item.Author, 20), Ui.Text(item.Author, "caption"), latest ? Ui.Pill(s["Latest"], null, "Accent.Soft", "Accent.SoftText") : null),
            expander,
            m.Advanced ? new SelectableTextBlock { Text = item.Entry.Commit + "\n" + s["Parents"] + ": " + item.Entry.Parents + "\n" + item.Entry.Refs + "\n" + item.Entry.Author + " <" + item.Entry.Email + ">\n" + item.Entry.Committer + " <" + item.Entry.CommitterEmail + ">\n\n" + item.Entry.Message, FontFamily = FontFamily.Parse("monospace"), FontSize = 11, TextWrapping = TextWrapping.Wrap }.Classed("muted") : null,
            restore);
        var card = Ui.Card(body, 14).Margin(12, 0, 0, 16);
        return Ui.Columns("Auto,*", rail, card);
    }

    public static IEnumerable<Control> ChangeRows(MainViewModel m, ChangeSummary summary)
    {
        var s = m.Strings;
        foreach (var change in summary.Entities)
        {
            var (icon, brush) = change.Kind switch { ChangeKind.Added => ("plus", "Success"), ChangeKind.Removed => ("minus", "Danger"), _ => ("pencil", "Info") };
            var label = string.Format(s[change.Kind switch { ChangeKind.Added => "ChangeAdded", ChangeKind.Removed => "ChangeRemoved", _ => "ChangeUpdated" }], s[change.Type == "schedule_item" ? "activity" : change.Type], change.Type == "trip" ? s["TripDetails"] : change.Title);
            var fields = change.Kind == ChangeKind.Updated && change.Fields.Count > 0 ? string.Join(", ", change.Fields.Select(f => m.FieldLabel(f))) : null;
            yield return Ui.Columns("Auto,*", Ui.Icon(icon, 14, brush), Ui.V(0, Ui.Text(label), fields is null ? null : Ui.Text(fields, "caption")).Margin(8, 0, 0, 0));
        }
        foreach (var resource in summary.Resources)
            yield return Ui.Columns("Auto,*", Ui.Icon("file", 14, "Text.Subtle"), Ui.Text(Path.GetFileName(resource.Path), "caption").Margin(8, 0, 0, 0));
    }
}

/// <summary>Variants: alternative versions of the plan backed by Git branches.</summary>
public static class VariantsView
{
    public static Control Build(MainWindow w)
    {
        var m = w.Model; var s = m.Strings;
        var create = Ui.Button(s["NewVariant"], "git-branch", "primary"); create.Command = m.NewVariantCommand;
        var header = Ui.Columns("*,Auto", Ui.V(4, Ui.Text(s["Variants"], "h1"), Ui.Text(s["VariantsHint"], "muted")), create);
        var list = Ui.V(12, Ui.Text(s["Loading"], "caption"));
        var panel = Ui.V(20, header, list); panel.Margin = new Thickness(32, 24, 32, 40); panel.MaxWidth = 960;
        _ = LoadAsync(w, list);
        return new ScrollViewer { Content = panel };
    }

    private static async Task LoadAsync(MainWindow w, StackPanel list)
    {
        var m = w.Model; var s = m.Strings;
        IReadOnlyList<Variant> variants;
        try { variants = await m.VariantsAsync(); } catch (DomainException ex) { list.Children.Clear(); list.Children.Add(Ui.Text(s.Error(ex, m.Advanced))); return; }
        list.Children.Clear();
        var current = variants.FirstOrDefault(v => v.Branch == m.CurrentBranch);
        var ordered = variants.OrderByDescending(v => v.Branch == m.CurrentBranch).ThenBy(v => v.IsRemote).ThenBy(v => v.Manifest.Data["variant"]?["state"]?.ToString() == "archived").ThenBy(v => v.Manifest.Data["variant"]?["title"]?.ToString(), StringComparer.CurrentCulture).ToArray();
        var archived = 0;
        foreach (var variant in ordered)
        {
            var meta = variant.Manifest.Data["variant"]; var isCurrent = variant.Branch == m.CurrentBranch; var isArchived = meta?["state"]?.ToString() == "archived";
            if (isArchived) { archived++; if (!m.Advanced) continue; }
            if (variant.IsRemote && !m.Advanced && variants.Any(v => !v.IsRemote && v.Commit == variant.Commit)) continue;
            var title = meta?["title"]?.ToString() ?? variant.Branch;
            var created = DateTimeOffset.TryParse(meta?["created_at"]?.ToString(), CultureInfo.InvariantCulture, out var at) ? string.Format(s["CreatedAgo"], Formats.Relative(at, s)) : "";
            var parent = meta?["parent"]?["branch"]?.ToString();
            var subtitle = string.Join(" · ", new[] { meta?["role"]?.ToString() == "primary" ? s["MainPlan"] : parent is null ? null : string.Format(s["BasedOn"], ordered.FirstOrDefault(v => v.Branch == parent)?.Manifest.Data["variant"]?["title"]?.ToString() ?? parent), created, variant.IsRemote ? s["OnlineCopy"] : null }.Where(x => !string.IsNullOrEmpty(x)));
            var badges = Ui.H(6, isCurrent ? Ui.Pill(s["YouAreHere"], "check", "Accent.Soft", "Accent.SoftText") : null, isArchived ? Ui.Pill(s["archived"], "archive", "Bg.Subtle", "Text.Muted") : null);
            var actions = Ui.H(6);
            Button Action(string key, string icon, bool primary = false) { var b = Ui.Button(s[key], icon, "compact", primary ? "soft" : ""); b.Click += (_, _) => _ = m.VariantActionAsync(variant, key); actions.Children.Add(b); return b; }
            if (!isCurrent) { Action("Switch", "arrow-right", true); Action("Compare", "git-compare"); Action("Merge", "git-merge"); }
            Action("ShareVariant", "share-2");
            var more = Ui.IconButton("ellipsis", s["More"], "small"); var menu = new MenuFlyout();
            var rename = new MenuItem { Header = s["Rename"], Icon = Ui.Icon("pencil", 15) }; rename.Click += (_, _) => _ = m.VariantActionAsync(variant, "Rename"); menu.Items.Add(rename);
            if (!isCurrent && !isArchived) { var archive = new MenuItem { Header = s["Archive"], Icon = Ui.Icon("archive", 15) }; archive.Click += (_, _) => _ = m.VariantActionAsync(variant, "Archive"); menu.Items.Add(archive); }
            more.Flyout = menu; actions.Children.Add(more);
            var body = Ui.V(10,
                Ui.Columns("Auto,*,Auto", Ui.Tile(isCurrent ? "check" : "git-branch", isCurrent ? "Activity" : "Transport", 38), Ui.V(2, Ui.H(8, Ui.Line(title, "h3"), badges), Ui.Line(subtitle, "caption"), m.Advanced ? Ui.Line(variant.Branch + " · " + variant.Commit[..Math.Min(10, variant.Commit.Length)], "caption").Also(t => t.FontFamily = FontFamily.Parse("monospace")) : null).Margin(14, 0, 0, 0)),
                actions);
            var card = Ui.Card(body, 16); if (isCurrent) card.Res(Border.BorderBrushProperty, "Accent");
            if (isArchived) card.Opacity = 0.7;
            list.Children.Add(card);
        }
        if (archived > 0 && !m.Advanced) list.Children.Add(Ui.Text(string.Format(s["ArchivedHidden"], archived), "caption"));
        if (variants.Count <= 1) list.Children.Add(Ui.Card(Ui.Columns("Auto,*", Ui.Icon("info", 16, "Info"), Ui.Text(s["VariantsExplainer"], "muted").Margin(10, 0, 0, 0)), 16));
    }
}
