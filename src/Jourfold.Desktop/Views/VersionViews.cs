using System.Globalization;
using Avalonia;
using Avalonia.Automation;
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
        IReadOnlyList<VariantOverview> variants; IReadOnlyList<DeletedVariant> deleted;
        bool shared;
        try { variants = await m.VariantOverviewAsync(); deleted = await m.DeletedVariantsAsync(); shared = (await m.RemotesAsync()).Count > 0; }
        catch (DomainException ex) { list.Children.Clear(); list.Children.Add(Ui.Text(s.Error(ex))); return; }
        list.Children.Clear();
        var gone = variants.Where(v => v.UpstreamGone && v.LocalBranch is not null).ToArray();
        if (gone.Length > 0)
        {
            var clean = Ui.Button(s["CleanUpVariants"], "git-branch", "primary", "compact"); clean.Command = m.CleanUpVariantsCommand;
            list.Children.Add(Ui.Card(Ui.Columns("Auto,*,Auto", Ui.Icon("triangle-alert", 16, "Warning"), Ui.Text(string.Format(gone.Length == 1 ? s["VariantsDeletedOnlineOne"] : s["VariantsDeletedOnlineMany"], gone.Length, string.Join(", ", gone.Select(v => v.Title))), "muted").Margin(10, 0), clean), 14));
        }
        void Section(string title, IEnumerable<VariantOverview> items, bool collapsed = false)
        {
            var cards = items.Select(v => Card(w, v, variants, shared)).ToArray(); if (cards.Length == 0) return;
            var body = Ui.V(12, cards);
            list.Children.Add(collapsed ? new Expander { Header = Ui.Text(title + " (" + cards.Length + ")", "strong"), Content = body, HorizontalAlignment = HorizontalAlignment.Stretch } : Ui.V(10, Ui.Text(title, "overline"), body));
        }
        Section(s["MainPlan"], variants.Where(v => v.Primary));
        Section(s["Variants"], variants.Where(v => !v.Primary && !v.Archived && !v.RemoteOnly));
        Section(s["OnlyOnline"], variants.Where(v => !v.Primary && !v.Archived && v.RemoteOnly));
        Section(s["MergedAndArchived"], variants.Where(v => !v.Primary && v.Archived), collapsed: true);
        if (deleted.Count > 0)
        {
            var rows = Ui.V(6);
            foreach (var d in deleted)
            {
                var days = Math.Max(0, (int)Math.Ceiling((MainViewModel.DeletedVariantsKept - (DateTimeOffset.UtcNow - d.DeletedAt)).TotalDays));
                var restore = Ui.Button(s["Restore"], "rotate-ccw", "compact"); restore.Click += (_, _) => _ = m.RestoreVariantAsync(d);
                AutomationProperties.SetName(restore, s["Restore"] + ": " + d.Title);
                rows.Children.Add(Ui.Columns("Auto,*,Auto", Ui.Icon("trash-2", 15, "Text.Subtle"), Ui.V(1, Ui.Line(d.Title, "title"), Ui.Text(string.Format(s["DeletedVariantInfo"], Formats.Relative(d.DeletedAt, s), days), "caption")).Margin(10, 0), restore));
            }
            list.Children.Add(new Expander { Header = Ui.Text(s["DeletedVariants"] + " (" + deleted.Count + ")", "strong"), Content = Ui.Card(rows, 14), HorizontalAlignment = HorizontalAlignment.Stretch });
        }
        if (variants.Count <= 1) list.Children.Add(Ui.Card(Ui.Columns("Auto,*", Ui.Icon("info", 16, "Info"), Ui.Text(s["VariantsExplainer"], "muted").Margin(10, 0, 0, 0)), 16));
    }

    /// <summary>One variant: where it lives, how it differs from its online copy and from the main plan, and what can be done.</summary>
    /// <param name="shared">Whether the trip has a remote; without one, where a variant lives needs no label.</param>
    private static Control Card(MainWindow w, VariantOverview v, IReadOnlyList<VariantOverview> all, bool shared)
    {
        var m = w.Model; var s = m.Strings;
        var legacy = new Variant(v.Branch, "", new Entity(v.Manifest), v.RemoteOnly);
        var badges = Ui.Flow(6,
            v.Current ? Ui.Pill(s["YouAreHere"], "check", "Accent.Soft", "Accent.SoftText") : null,
            v.Merged ? Ui.Pill(s["TakenOver"], "git-merge", "Success.Soft", "Success") : v.Archived ? Ui.Pill(s["archived"], "archive", "Bg.Subtle", "Text.Muted") : null,
            v.UpstreamGone ? Ui.Pill(s["DeletedOnline"], "triangle-alert", "Warning.Soft", "Warning")
                : v.RemoteOnly ? Ui.Pill(s["OnlyOnline"], "cloud", "Info.Soft", "Info")
                : !shared ? null
                : v.LocalOnly || !v.OnRemote ? Ui.Pill(s["OnlyOnThisComputer"], "monitor", "Bg.Subtle", "Text.Muted")
                : Ui.Pill(s["Online"], "cloud", "Bg.Subtle", "Text.Muted"));
        var lines = Ui.V(4);
        // How the local copy and the online copy differ, in words and arrows, never by colour alone.
        if (v.LocalBranch is not null && v.OnRemote)
        {
            var (icon, text) = v.Diverged ? ("git-compare", string.Format(s["VariantDiverged"], v.Ahead, v.Behind))
                : v.Ahead > 0 ? ("upload", string.Format(v.Ahead == 1 ? s["SyncAheadOne"] : s["SyncAhead"], v.Ahead))
                : v.Behind > 0 ? ("download", string.Format(v.Behind == 1 ? s["SyncBehindOne"] : s["SyncBehind"], v.Behind))
                : ("circle-check", s["SyncSame"]);
            var sync = v.Ahead + v.Behind > 0 ? Ui.Button(s["Sync"], "refresh-cw", "link").Also(b => b.Click += (_, _) => _ = m.SyncVariantAsync(v)) : null;
            lines.Children.Add(Ui.H(6, Ui.Icon(icon, 13, v.Diverged ? "Warning" : "Text.Subtle"), Ui.Text(text, "caption"), sync));
        }
        if (!v.Primary && !v.Archived && v.BehindPrimary > 0)
        {
            var update = v.Current ? Ui.Button(s["UpdateFromMainPlan"], "download", "link").Also(b => b.Click += (_, _) => _ = m.UpdateFromPrimaryAsync()) : null;
            lines.Children.Add(Ui.H(6, Ui.Icon("git-branch", 13, "Text.Subtle"), Ui.Text(string.Format(v.BehindPrimary == 1 ? s["BehindMainOne"] : s["BehindMain"], v.BehindPrimary, all.FirstOrDefault(x => x.Primary)?.Title ?? s["MainPlan"]), "caption"), update));
        }
        if (v.LastChanged is { } changed) lines.Children.Add(Ui.Text(string.Format(s["LastChangedBy"], v.LastAuthor ?? "", Formats.Relative(changed, s)), "caption"));
        if (m.Advanced) lines.Children.Add(Ui.Line(string.Join(" · ", new[] { v.LocalBranch, v.RemoteBranch }.Where(b => b is not null)), "caption").Also(t => t.FontFamily = FontFamily.Parse("monospace")));

        var actions = Ui.Flow(6);
        Button Add(string label, string icon, Func<Task> run, bool primary = false) { var b = Ui.Button(label, icon, "compact", primary ? "soft" : ""); b.Click += (_, _) => _ = run(); AutomationProperties.SetName(b, label + ": " + v.Title); actions.Children.Add(b); return b; }
        if (!v.Current) Add(s["Switch"], "arrow-right", () => m.VariantActionAsync(legacy, "Switch"), primary: true);
        if (!v.Current) Add(s["Compare"], "git-compare", () => m.VariantActionAsync(legacy, "Compare"));
        if (!v.Primary && !v.Archived) Add(s["PromoteVariant"], "git-merge", () => m.PromoteVariantAsync(v));
        if (v.LocalBranch is not null && (v.LocalOnly || v.UpstreamGone)) Add(s["ShareAgain"], "cloud", () => m.ShareLocalVariantAsync(v));
        var more = Ui.IconButton("ellipsis", s["More"], "small"); var menu = new MenuFlyout();
        void Item(string label, string icon, Func<Task> run) { var item = new MenuItem { Header = label, Icon = Ui.Icon(icon, 15) }; item.Click += (_, _) => _ = run(); menu.Items.Add(item); }
        if (!v.Current && !v.Primary && !v.Archived) Item(s["Merge"], "git-merge", () => m.VariantActionAsync(legacy, "Merge"));
        if (v.OnRemote || v.LocalBranch is not null) Item(s["ShareVariant"], "share-2", () => m.VariantActionAsync(legacy, "ShareVariant"));
        if (v.LocalBranch is not null) Item(s["Rename"], "pencil", () => m.VariantActionAsync(legacy, "Rename"));
        if (v.LocalBranch is not null && !v.Current && !v.Primary && !v.Archived) Item(s["Archive"], "archive", () => m.VariantActionAsync(legacy, "Archive"));
        if (v.LocalBranch is not null && !v.Primary) { if (menu.Items.Count > 0) menu.Items.Add(new Separator()); Item(s["Delete"], "trash-2", () => m.DeleteVariantAsync(v)); }
        if (menu.Items.Count > 0) { more.Flyout = menu; actions.Children.Add(more); }

        var tile = Ui.Tile(v.Current ? "check" : v.Primary ? "house" : "git-branch", v.Current ? "Activity" : "Transport", 38);
        var body = Ui.V(10, Ui.Columns("Auto,*", tile, Ui.V(4, Ui.Line(v.Title, "h3"), badges, lines).Margin(14, 0, 0, 0)), actions);
        var card = Ui.Card(body, 16); if (v.Current) card.Res(Border.BorderBrushProperty, "Accent");
        if (v.Archived) card.Opacity = 0.8;
        AutomationProperties.SetName(card, v.Title);
        return card;
    }
}
