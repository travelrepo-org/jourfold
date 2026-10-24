using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using TravelRepo.Core;

namespace Jourfold.Desktop;

/// <summary>
/// Packing lists: several lists per trip, items in groups, who brings each item, and who has packed it. Filters show
/// everything, the local user's items, or what nobody brings yet.
/// </summary>
public static class PackingView
{
    public static Control Build(MainWindow w)
    {
        var m = w.Model; var s = m.Strings; var trip = m.Trip!;
        var create = Ui.Button(s["NewPackingList"], "plus", "primary"); create.Click += async (_, _) => await m.CreatePackingListAsync();
        var lists = PackingLists.All(trip);
        var panel = Ui.V(18); panel.Margin = new Thickness(32, 24, 32, 40); panel.MaxWidth = 980;
        panel.Children.Add(Ui.Columns("*,Auto", Ui.V(4, Ui.Text(s["Packing"], "h1"), Ui.Text(s["PackingHint"], "muted").Also(t => t.TextWrapping = TextWrapping.Wrap)), create.Also(b => b.VerticalAlignment = VerticalAlignment.Bottom)));
        if (m.CurrentPackingList is not { } list)
        {
            var first = Ui.Button(s["NewPackingList"], "plus", "primary"); first.Click += async (_, _) => await m.CreatePackingListAsync();
            panel.Children.Add(Ui.Card(Ui.EmptyState("backpack", s["NoPackingListsTitle"], s["NoPackingListsBody"], first), 0));
            return Scroll(panel);
        }

        // One chip per list, with its progress.
        var chips = new WrapPanel { ItemSpacing = 6, LineSpacing = 6 };
        foreach (var l in lists)
        {
            var chip = Ui.Button(l.Collection.Title + "  " + l.DoneCount.ToString(CultureInfo.CurrentCulture) + "/" + l.Items.Count.ToString(CultureInfo.CurrentCulture), "backpack", "chip");
            if (l.Collection.Id == list.Collection.Id) chip.Classes.Add("selected");
            AutomationProperties.SetName(chip, string.Format(s["PackingListProgress"], l.Collection.Title, l.DoneCount, l.Items.Count));
            chip.Click += (_, _) => m.ShowPackingList(l.Collection.Id);
            chips.Children.Add(chip);
        }
        var more = Ui.IconButton("ellipsis", s["More"], "small");
        var menu = new MenuFlyout();
        var rename = new MenuItem { Header = s["RenameList"], Icon = Ui.Icon("pencil", 15) }; rename.Click += (_, _) => m.Selected = trip.Find(list.Collection.Id); menu.Items.Add(rename);
        var delete = new MenuItem { Header = s["DeletePackingList"], Icon = Ui.Icon("trash-2", 15) }; delete.Click += async (_, _) => await m.DeletePackingListAsync(list.Collection.Id); menu.Items.Add(delete);
        more.Flyout = menu;
        panel.Children.Add(Ui.Columns("*,Auto", chips, more));

        // Filters: all, mine (when the local user is known), and what nobody brings yet.
        var me = m.Me; var filter = m.PackingFilter == "mine" && me is null ? "all" : m.PackingFilter;
        var unassigned = PackingLists.Unassigned(list).Count;
        var segments = new List<(string, string, string?)> { ("all", s["All"], "list") };
        if (me is not null) segments.Add(("mine", s["MyItems"], "user-check"));
        segments.Add(("unassigned", unassigned > 0 ? string.Format(s["UnassignedCount"], unassigned) : s["Unassigned"], "user"));
        var who = me is null ? Ui.Button(s["WhoAmITitle"], "user", "link").Also(b => b.Click += async (_, _) => await m.ChooseMeCommand.ExecuteAsync(null)) : null;
        panel.Children.Add(Ui.Columns("Auto,*,Auto", ViewToolbar.Segmented(segments, filter, v => m.PackingFilter = v), new Border(), who));

        var input = new TextBox { Watermark = s["AddPackingItemPlaceholder"] }; AutomationProperties.SetName(input, s["AddPackingItem"]);
        input.KeyDown += async (_, e) =>
        {
            if (e.Key != Key.Enter || string.IsNullOrWhiteSpace(input.Text)) return;
            var text = input.Text; input.Text = ""; e.Handled = true;
            await m.AddPackingItemAsync(list.Collection.Id, text);
        };
        panel.Children.Add(input);

        var items = filter switch
        {
            "mine" => PackingLists.For(list, me!.Id),
            "unassigned" => PackingLists.Unassigned(list),
            _ => list.Items
        };
        if (items.Count == 0) panel.Children.Add(Ui.Text(filter == "unassigned" ? s["EverythingAssigned"] : list.Items.Count == 0 ? s["PackingListEmpty"] : s["NothingHere"], "muted").Margin(4, 8));
        var groups = PackingLists.Groups(items);
        foreach (var (group, members) in groups)
        {
            var rows = Ui.V(2); foreach (var item in members) rows.Children.Add(Row(w, item, me));
            panel.Children.Add(Ui.V(8, group is null && groups.Count == 1 ? null : Ui.Text(group ?? s["NoGroup"], "h3"), Ui.Card(rows, 6)));
        }
        return Scroll(panel);
    }

    private static Control Scroll(Control panel) => new ScrollViewer { Content = new Border { Child = panel, HorizontalAlignment = HorizontalAlignment.Center } };

    /// <summary>
    /// One item: a check box for the local user's own packing, the title and quantity, and who brings it. A person's
    /// chip toggles their packing, so one person can keep the list for everyone.
    /// </summary>
    private static Control Row(MainWindow w, PackingItem item, Entity? me)
    {
        var m = w.Model; var s = m.Strings; var task = item.Task;
        var mine = me is not null && item.IsFor(me.Id);
        CheckBox? check = null;
        if (mine)
        {
            check = new CheckBox { IsChecked = item.IsPackedBy(me!.Id), MinWidth = 0, Padding = new Thickness(0) };
            AutomationProperties.SetName(check, string.Format(s["MarkPacked"], task.Title));
            check.IsCheckedChanged += async (_, _) => await m.TogglePackedAsync(task.Id, me.Id);
        }
        var title = Ui.Line(task.Title, "title"); if (item.Done) { title.TextDecorations = TextDecorations.Strikethrough; title.Classes.Add("muted"); }
        var text = Ui.V(1, title, item.Quantity is { } quantity ? Ui.Line(quantity, "caption") : null);

        var people = new WrapPanel { ItemSpacing = 4, LineSpacing = 4, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        if (item.Unassigned)
        {
            var assign = Ui.Button(s["Assign"], "user", "chip"); assign.Classes.Add("warning"); assign.Click += async (_, _) => await m.AssignPackingItemAsync(task.Id);
            people.Children.Add(assign);
        }
        else
        {
            if (item.Everyone) people.Children.Add(Ui.Pill(string.Format(s["EveryonePacked"], item.Assignees.Count(a => item.IsPackedBy(a.Id)), item.Assignees.Count), "users", "Bg.Subtle", "Text.Muted"));
            foreach (var person in item.Assignees.Take(item.Everyone ? 6 : 12))
            {
                var packed = item.IsPackedBy(person.Id);
                var chip = new Button { Padding = new Thickness(3, 2, 8, 2), CornerRadius = new CornerRadius(12), Content = Ui.H(5, Ui.Avatar(person.Title, 20), Ui.Text(person.Title.Split(' ')[0], "caption"), packed ? Ui.Icon("check", 12, "Success") : null) }.Classed("chip");
                if (packed) chip.Classes.Add("selected");
                var name = string.Format(packed ? s["PackedBy"] : s["NotPackedBy"], person.Title);
                AutomationProperties.SetName(chip, name); ToolTip.SetTip(chip, name + ". " + s["ClickToToggle"]);
                chip.Click += async (_, _) => await m.TogglePackedAsync(task.Id, person.Id);
                people.Children.Add(chip);
            }
        }
        // The same actions in the "…" menu and on right-click.
        MenuItem[] Entries()
        {
            MenuItem Entry(string label, string icon, Func<Task> action) { var entry = new MenuItem { Header = label, Icon = Ui.Icon(icon, 15) }; entry.Click += async (_, _) => await action(); return entry; }
            return [Entry(s["Assign"], "users", () => m.AssignPackingItemAsync(task.Id)), Entry(s["GroupAndQuantity"], "package", () => m.DescribePackingItemAsync(task.Id)), Entry(s["Delete"], "trash-2", () => m.DeleteAsync(task.Id))];
        }
        var more = Ui.IconButton("ellipsis", s["More"], "small"); more.Flyout = new MenuFlyout { ItemsSource = Entries() };
        var grid = Ui.Columns("Auto,*,Auto,Auto", check?.Margin(0, 0, 10, 0), text, people.Margin(10, 0), more);
        var row = new Button { Content = grid, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch }.Classed("row").Named(task.Title).Bind(w, task);
        row.ContextMenu = new ContextMenu { ItemsSource = Entries() };
        return row;
    }
}
