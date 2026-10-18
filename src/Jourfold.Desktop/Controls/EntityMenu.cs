using Avalonia.Controls;
using TravelRepo.Core;

namespace Jourfold.Desktop;

/// <summary>
/// The actions offered for one entity, shared by the right-click menu of every list, tile, map pin and timetable
/// block and by the inspector's menu, so the same item offers the same actions everywhere.
/// </summary>
public static class EntityMenu
{
    public sealed record Action(string Label, string Icon, System.Action Run);

    public static IReadOnlyList<Action?> Actions(MainWindow w, Entity e)
    {
        var m = w.Model; var s = m.Strings; var list = new List<Action?>();
        Entity? Current() => m.Workspace?.State.Trip.Find(e.Id);
        if (e.Type == "document") list.Add(new(s["OpenFile"], "external-link", () => InspectorView.OpenDocument(m, Current() ?? e)));
        if (e.Type == "place" && MapView.Coordinates(e) is not null) list.Add(new(s["ShowOnMap"], "map", () => { m.Selected = Current(); m.Navigate("Map"); }));
        if (e.Type == "schedule_item")
        {
            list.Add(new(s["EditTime"], "clock", () => { m.Selected = Current(); _ = m.ScheduleSelectedAsync(); }));
            if (e.Data["time"] is not null) list.Add(new(s["MoveToInbox"], "inbox", () => _ = m.MoveToInboxAsync(e.Id)));
        }
        if (e.Type is not ("person" or "comment" or "trip")) list.Add(new(s["Duplicate"], "copy", () => _ = m.DuplicateAsync(e.Id)));
        if (e.Type != "trip") { list.Add(null); list.Add(new(s["Delete"], "trash-2", () => _ = m.DeleteAsync(e.Id))); }
        return list;
    }

    /// <summary>A right-click menu with <see cref="Actions"/>, plus optional actions in front of them.</summary>
    public static ContextMenu Context(MainWindow w, Entity e, params Action[] extra)
    {
        var menu = new ContextMenu();
        foreach (var item in Build(w, e, extra)) menu.Items.Add(item);
        return menu;
    }

    public static MenuFlyout Flyout(MainWindow w, Entity e, params Action[] extra)
    {
        var menu = new MenuFlyout();
        foreach (var item in Build(w, e, extra)) menu.Items.Add(item);
        return menu;
    }

    private static IEnumerable<Control> Build(MainWindow w, Entity e, Action[] extra)
    {
        var actions = extra.Cast<Action?>().Concat(Actions(w, e)).ToArray();
        // Leading or doubled separators look broken when an entity offers few actions.
        Control? previous = null;
        foreach (var action in actions)
        {
            if (action is null) { if (previous is MenuItem) yield return previous = new Separator(); continue; }
            var item = new MenuItem { Header = action.Label, Icon = Ui.Icon(action.Icon, 15) }; item.Click += (_, _) => action.Run();
            yield return previous = item;
        }
    }

    /// <summary>
    /// Makes a button stand for an entity: it carries the entity ID as Tag, shows whether it is selected, selects it
    /// on click and offers the entity menu on right-click.
    /// </summary>
    public static T Bind<T>(this T button, MainWindow w, Entity e) where T : Button
    {
        var m = w.Model;
        button.Tag = e.Id;
        if (m.Selected?.Id == e.Id) button.Classes.Add("selected");
        button.Click += (_, _) => m.Selected = m.Workspace?.State.Trip.Find(e.Id);
        button.ContextMenu = Context(w, e);
        return button;
    }
}
