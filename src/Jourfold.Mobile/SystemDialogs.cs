namespace Jourfold.Mobile;

/// <summary>A menu for the system's own action sheet: its entries, the dangerous ones marked, and the way out.</summary>
public sealed record SystemMenu(string Title, string? Detail, IReadOnlyList<SystemMenuEntry> Entries, string Cancel);

/// <summary>An entry of a <see cref="SystemMenu"/>; a dangerous one, such as deleting, shows in red.</summary>
public sealed record SystemMenuEntry(string Label, bool Danger = false);

/// <summary>A question for the system's own alert: what is asked, more about it, the action and the way out.</summary>
public sealed record SystemQuestion(string Text, string? Detail, string Action, string Decline);

/// <summary>
/// The system's own action sheets and alerts, where the platform has them (iOS), for the app's long-press menus and
/// questions. Menus with a search field stay the app's own, since an action sheet has none.
/// </summary>
public static class SystemDialogs
{
    /// <summary>Whether a menu goes to the system's action sheet: not one to search in or one too long to read there.</summary>
    public static bool Fits(ActionMenu menu) => !menu.Searchable && menu.Actions.Count is > 0 and < 12;
}
