using Foundation;
using Jourfold.Mobile;
using Jourfold.Mobile.Controls;
using UIKit;

namespace Jourfold.iOS;

/// <summary>
/// iOS's own date and time pickers for the app's date and time fields: wheels for a time, the calendar for a day, in a
/// sheet with Cancel and Done. VoiceOver, Larger Text and the system's language come with them. Times are read without
/// a zone, as the fields hold them, and in 24 hours as everywhere in Jourfold.
/// </summary>
internal static class IosPickers
{
    private static readonly NSTimeZone Utc = NSTimeZone.FromName("UTC")!;
    private static readonly DateTime Epoch = new(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static NSDate ToDate(DateTime utc) => NSDate.FromTimeIntervalSinceReferenceDate((utc - Epoch).TotalSeconds);
    private static DateTime FromDate(NSDate date) => Epoch.AddSeconds(date.SecondsSinceReferenceDate);

    /// <summary>The app's language, in a region that writes 24 hours.</summary>
    private static NSLocale Locale() => new(MobileApp.Host?.Language == "de" ? "de_DE" : "en_GB");

    public static async Task<PickedTime> PickTimeAsync(TimeSpan? shown, int minuteSteps, string? title)
    {
        var start = shown ?? Rounded(DateTime.Now.TimeOfDay, minuteSteps);
        var picker = new UIDatePicker
        {
            Mode = UIDatePickerMode.Time,
            PreferredDatePickerStyle = UIDatePickerStyle.Wheels,
            TimeZone = Utc,
            Locale = Locale(),
            // UIKit takes only steps that divide an hour.
            MinuteInterval = 60 % minuteSteps == 0 ? minuteSteps : 1,
        };
        picker.SetDate(ToDate(Epoch.Add(start)), false);
        var chosen = await PresentAsync(picker, title);
        return chosen ? new PickedTime(true, FromDate(picker.Date).TimeOfDay) : default;
    }

    public static async Task<PickedDate> PickDateAsync(DateTime? shown, string? title)
    {
        var picker = new UIDatePicker
        {
            Mode = UIDatePickerMode.Date,
            PreferredDatePickerStyle = UIDatePickerStyle.Inline,
            TimeZone = Utc,
            Locale = Locale(),
        };
        var day = (shown ?? DateTime.Today).Date;
        picker.SetDate(ToDate(DateTime.SpecifyKind(day, DateTimeKind.Utc)), false);
        var chosen = await PresentAsync(picker, title);
        return chosen ? new PickedDate(true, FromDate(picker.Date).Date) : default;
    }

    private static TimeSpan Rounded(TimeSpan time, int steps) => TimeSpan.FromMinutes(Math.Round(time.TotalMinutes / steps) * steps % (24 * 60));

    /// <summary>Shows the picker in a sheet; true when Done was tapped, false when cancelled or swiped away.</summary>
    private static Task<bool> PresentAsync(UIDatePicker picker, string? title)
    {
        var answer = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (IosSystem.Top() is not { } top) { answer.SetResult(false); return answer.Task; }
        var page = new PickerPage(picker, answer) { Title = title };
        var sheet = new UINavigationController(page);
        if (sheet.SheetPresentationController is { } presentation)
        {
            presentation.Detents = [UISheetPresentationControllerDetent.CreateMediumDetent()];
            presentation.PrefersGrabberVisible = true;
        }
        if (sheet.PresentationController is { } controller) controller.Delegate = new Dismissal(answer);
        if (Accent() is { } accent) { sheet.View!.TintColor = accent; sheet.NavigationBar.TintColor = accent; picker.TintColor = accent; }
        top.PresentViewController(sheet, true, null);
        return answer.Task;
    }

    /// <summary>The app's accent colour for the chosen day and the buttons, in light and dark.</summary>
    internal static UIColor? Accent()
    {
        static UIColor? Read(Avalonia.Styling.ThemeVariant theme) =>
            Avalonia.Application.Current?.TryGetResource("Accent", theme, out var value) == true && value is Avalonia.Media.ISolidColorBrush brush
                ? UIColor.FromRGB(brush.Color.R, brush.Color.G, brush.Color.B) : null;
        if (Read(Avalonia.Styling.ThemeVariant.Light) is not { } light || Read(Avalonia.Styling.ThemeVariant.Dark) is not { } dark) return null;
        return UIColor.FromDynamicProvider(traits => traits.UserInterfaceStyle == UIUserInterfaceStyle.Dark ? dark : light);
    }

    private sealed class PickerPage(UIDatePicker picker, TaskCompletionSource<bool> answer) : UIViewController
    {
        public override void ViewDidLoad()
        {
            base.ViewDidLoad();
            View!.BackgroundColor = UIColor.SystemBackground;
            NavigationItem.LeftBarButtonItem = new UIBarButtonItem(UIBarButtonSystemItem.Cancel, (_, _) => Close(false));
            // iOS 26 draws Done filled in the item's own tint, not the bar's.
            NavigationItem.RightBarButtonItem = new UIBarButtonItem(UIBarButtonSystemItem.Done, (_, _) => Close(true)) { TintColor = Accent() };
            picker.TranslatesAutoresizingMaskIntoConstraints = false;
            View.AddSubview(picker);
            var guide = View.SafeAreaLayoutGuide;
            NSLayoutConstraint.ActivateConstraints(
            [
                picker.TopAnchor.ConstraintEqualTo(guide.TopAnchor, 8),
                picker.LeadingAnchor.ConstraintEqualTo(guide.LeadingAnchor, 16),
                picker.TrailingAnchor.ConstraintEqualTo(guide.TrailingAnchor, -16),
                picker.BottomAnchor.ConstraintLessThanOrEqualTo(guide.BottomAnchor, -8),
            ]);
        }

        private void Close(bool chosen) => DismissViewController(true, () => answer.TrySetResult(chosen));
    }

    /// <summary>A sheet swiped down counts as cancelled.</summary>
    private sealed class Dismissal(TaskCompletionSource<bool> answer) : UIAdaptivePresentationControllerDelegate
    {
        public override void DidDismiss(UIPresentationController presentationController) => answer.TrySetResult(false);
    }
}
