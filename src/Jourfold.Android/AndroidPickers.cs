using Android.App;
using Jourfold.Mobile.Controls;

namespace Jourfold.Android;

/// <summary>
/// Android's own date and time pickers for the app's date and time fields: the clock face for a time, with the hours
/// 13 to 24 on the inner ring and then the minutes on a second face, and the calendar for a day. TalkBack, the
/// system's language and its look come with them. Times are read without a zone, as the fields hold them, and in 24
/// hours as everywhere in Jourfold. Any minute can be chosen; the clock face has no steps.
/// </summary>
internal static class AndroidPickers
{
    public static Task<PickedTime> PickTimeAsync(TimeSpan? shown, int minuteSteps, string? title)
    {
        var answer = new TaskCompletionSource<PickedTime>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (MainActivity.Current is not { } activity) { answer.SetResult(default); return answer.Task; }
        var start = shown ?? DateTime.Now.TimeOfDay;
        var dialog = new TimePickerDialog(activity, Resource.Style.Theme_Jourfold_Picker,
            (_, e) => answer.TrySetResult(new PickedTime(true, new TimeSpan(e.HourOfDay, e.Minute, 0))), start.Hours, start.Minutes, true);
        Show(dialog, title, () => answer.TrySetResult(default));
        return answer.Task;
    }

    public static Task<PickedDate> PickDateAsync(DateTime? shown, string? title)
    {
        var answer = new TaskCompletionSource<PickedDate>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (MainActivity.Current is not { } activity) { answer.SetResult(default); return answer.Task; }
        var day = (shown ?? DateTime.Today).Date;
        // Android counts months from 0.
        var dialog = new DatePickerDialog(activity, Resource.Style.Theme_Jourfold_Picker,
            (_, e) => answer.TrySetResult(new PickedDate(true, new DateTime(e.Year, e.Month + 1, e.DayOfMonth))), day.Year, day.Month - 1, day.Day);
        Show(dialog, title, () => answer.TrySetResult(default));
        return answer.Task;
    }

    /// <summary>
    /// Shows the dialog. Its own buttons answer through the listener first; closing it any other way, with Cancel, Back
    /// or a tap beside it, counts as cancelled.
    /// </summary>
    private static void Show(Dialog dialog, string? title, Action cancelled)
    {
        // The field's name for TalkBack, as the window's title; the dialog shows none above the clock or calendar.
        if (!string.IsNullOrEmpty(title)) dialog.Window?.SetTitle(title);
        dialog.DismissEvent += (_, _) => cancelled();
        dialog.Show();
    }
}
