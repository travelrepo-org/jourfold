using Jourfold.Mobile;
using UIKit;

namespace Jourfold.iOS;

/// <summary>
/// The app's long-press menus as iOS action sheets and its questions as iOS alerts: VoiceOver, Larger Text, the red of
/// a dangerous entry and Cancel at the bottom come from the system.
/// </summary>
internal static class IosDialogs
{
    public static Task<int?> ShowMenuAsync(SystemMenu menu)
    {
        var answer = new TaskCompletionSource<int?>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (IosSystem.Top() is not { } top) { answer.SetResult(null); return answer.Task; }
        var sheet = UIAlertController.Create(menu.Title, menu.Detail, UIAlertControllerStyle.ActionSheet);
        for (var i = 0; i < menu.Entries.Count; i++)
        {
            var index = i; var entry = menu.Entries[i];
            sheet.AddAction(UIAlertAction.Create(entry.Label, entry.Danger ? UIAlertActionStyle.Destructive : UIAlertActionStyle.Default, _ => answer.TrySetResult(index)));
        }
        sheet.AddAction(UIAlertAction.Create(menu.Cancel, UIAlertActionStyle.Cancel, _ => answer.TrySetResult(null)));
        // On an iPad the sheet is a popover; a tap beside it counts as Cancel, as UIKit runs the cancel action then.
        IosSystem.Anchor(sheet, top);
        Present(sheet, top);
        return answer.Task;
    }

    public static Task<bool> AskAsync(SystemQuestion question)
    {
        var answer = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (IosSystem.Top() is not { } top) { answer.SetResult(false); return answer.Task; }
        var alert = UIAlertController.Create(question.Text, question.Detail, UIAlertControllerStyle.Alert);
        alert.AddAction(UIAlertAction.Create(question.Decline, UIAlertActionStyle.Cancel, _ => answer.TrySetResult(false)));
        var yes = UIAlertAction.Create(question.Action, UIAlertActionStyle.Default, _ => answer.TrySetResult(true));
        alert.AddAction(yes);
        alert.PreferredAction = yes;
        Present(alert, top);
        return answer.Task;
    }

    private static void Present(UIAlertController controller, UIViewController top)
    {
        if (IosPickers.Accent() is { } accent && controller.View is { } view) view.TintColor = accent;
        top.PresentViewController(controller, true, null);
    }
}
