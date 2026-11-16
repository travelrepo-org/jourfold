using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Jourfold.Mobile;
using Jourfold.Mobile.Views;
using Jourfold.UI;
using NodaTime;
using TravelRepo.Git;
using Xunit;
namespace Jourfold.Tests;

/// <summary>
/// Swiping through the days twice in a row, in the list and on the phone's timetable, and the travel day's
/// notification as a Live Update: what the settings say about it and the example to try it with.
/// </summary>
public sealed class DaySwipeAndLiveTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "jourfold-swipe-" + Guid.NewGuid());
    public void Dispose() => TestFiles.Delete(root);

    private static readonly DateTimeZone Tokyo = DateTimeZoneProviders.Tzdb["Asia/Tokyo"];

    private async Task<(MobileSession Session, Window Window)> OpenAsync(ISystemNotices? notices = null)
    {
        var now = new LocalDateTime(2027, 5, 16, 10, 40).InZoneStrictly(Tokyo).ToInstant();
        var session = new MobileSession(new MobileHost(root, new GitCliBackend(), new FakeClock(now), () => Tokyo, "en", new MemorySecrets(), Notices: notices));
        await session.OpenAsync(await session.CreateSampleAsync(new LocalDate(2027, 5, 14)));
        var window = new Window { Width = 412, Height = 915, Content = new AppShell(session) }; window.Show();
        Probe.Layout();
        return (session, window);
    }

    /// <summary>A sideways drag over the day, let go at the end.</summary>
    private static void Swipe(Window window, double from, double to, double y = 600)
    {
        window.MouseDown(new Point(from, y), MouseButton.Left);
        for (var step = 1; step <= 10; step++) window.MouseMove(new Point(from + (to - from) * step / 10, y + 2));
        window.MouseUp(new Point(to, y + 2), MouseButton.Left);
    }

    /// <summary>
    /// A sideways drag at a finger's pace, a move every ten milliseconds, so the speed it is let go with does not depend
    /// on how fast the machine running the test sends the moves.
    /// </summary>
    private static async Task PacedSwipe(Window window, double from, double to, double y = 600)
    {
        window.MouseDown(new Point(from, y), MouseButton.Left);
        for (var step = 1; step <= 10; step++) { await Task.Delay(10); window.MouseMove(new Point(from + (to - from) * step / 10, y + 2)); }
        window.MouseUp(new Point(to, y + 2), MouseButton.Left);
    }

    private static ChoiceButton[] DateChips(Window window) => Probe.All<ChoiceButton>(window).Where(c => c.Classes.Contains("date")).ToArray();

    [AvaloniaFact]
    public async Task InTheListTwoSwipesInARowMoveTwoDays()
    {
        var (session, window) = await OpenAsync();
        try
        {
            session.ShowTab(MobileTab.Plan);
            var days = session.Agenda.Days; session.ShowDate(days[1].Date); Probe.Layout();
            // The next day is there as soon as the finger lets go, without waiting for the day to slide out.
            Swipe(window, 350, 60);
            Assert.Equal(days[2].Date, session.SelectedDate);
            Probe.Layout();
            Assert.Equal(days[2].Date, DateChips(window).Single(c => c.IsSelected).Tag);
            // So a second swipe right after it counts too.
            Swipe(window, 350, 60); Probe.Layout();
            Assert.Equal(days[3].Date, session.SelectedDate);
            Swipe(window, 60, 350); Probe.Layout();
            Swipe(window, 60, 350); Probe.Layout();
            Assert.Equal(days[1].Date, session.SelectedDate);
            // A short drag still goes nowhere.
            Swipe(window, 200, 180); Probe.Layout();
            Assert.Equal(days[1].Date, session.SelectedDate);
            await Task.CompletedTask;
        }
        finally { window.Close(); session.Dispose(); }
    }

    [AvaloniaFact]
    public async Task OnThePhonesTimetableTheDateStripFollowsAtOnceAndTheNextSwipeNeedsNoRest()
    {
        var (session, window) = await OpenAsync();
        try
        {
            session.Settings.Values["plan.day"] = "timetable"; session.ShowTab(MobileTab.Plan);
            var days = session.Agenda.Days; session.ShowDate(days[0].Date); Probe.Layout();
            var chips = DateChips(window);
            Assert.Equal(days[0].Date, chips.Single(c => c.IsSelected).Tag);

            // Letting go names the day the glide is heading for, on the strip and as the chosen day, before it rests.
            // At a finger's pace, so the days do not fly to the end of the trip.
            await PacedSwipe(window, 300, 180);
            var first = session.SelectedDate;
            Assert.True(first > days[0].Date);
            Assert.True(first < days[^1].Date, $"The first swipe went to the last day of {days.Count}, which leaves nothing to check.");
            Assert.Equal(first, chips.Single(c => c.IsSelected).Tag);
            // The plan is not drawn again in between: the strip is the same one, so the finger can go on at once.
            await PacedSwipe(window, 300, 180);
            var second = session.SelectedDate;
            Assert.True(second > first, "The second swipe right after the first did not move on.");
            Assert.Equal(second, chips.Single(c => c.IsSelected).Tag);
            Assert.Same(chips[0], DateChips(window)[0]);
            Assert.True(chips[0].IsAttachedToVisualTree());

            // Once the days have rested a moment, the plan is drawn again around the day they stopped at.
            await Probe.Until(() => !chips[0].IsAttachedToVisualTree(), 3000);
            Assert.Equal(second, session.SelectedDate);
            Assert.Equal(second, DateChips(window).Single(c => c.IsSelected).Tag);

            // A finger that takes hold of the days while they still glide keeps the plan from being drawn again under it,
            // however long it holds them.
            chips = DateChips(window);
            Swipe(window, 150, 350);
            window.MouseDown(new Point(150, 600), MouseButton.Left);
            for (var step = 1; step <= 10; step++) window.MouseMove(new Point(150 + 20 * step, 602));
            await Task.Delay(PlanView.RedrawAfterRest + TimeSpan.FromMilliseconds(700)); Probe.Layout();
            Assert.True(chips[0].IsAttachedToVisualTree(), "The plan was drawn again under the finger.");
            window.MouseUp(new Point(350, 602), MouseButton.Left);
            Assert.True(session.SelectedDate < second);
        }
        finally { window.Close(); session.Dispose(); }
    }

    private sealed class LiveNotices(LiveSupport live, bool? promoted) : ISystemNotices
    {
        public List<LiveNotice?> Live { get; } = [];
        public int OpenedLiveSettings { get; private set; }
        public Task<bool> AllowAsync(bool ask, CancellationToken ct = default) => Task.FromResult(true);
        public bool OnTime => true;
        public void AskOnTime() { }
        public void OpenSettings() { }
        public void Show(TravelNotice notice, string tripFolder) { }
        public void ShowLive(LiveNotice? notice, string? tripFolder) => Live.Add(notice);
        public void WakeAt(Instant? at) { }
        public void SetTicketShortcuts(IReadOnlyList<TicketShortcut> tickets) { }
        LiveSupport ISystemNotices.Live => live;
        public void OpenLiveSettings() => OpenedLiveSettings++;
        public bool? LivePromoted => Live.LastOrDefault() is null ? null : promoted;
        public bool LiveByMakerOnly { get; init; }
    }

    [AvaloniaTheory]
    [InlineData(LiveSupport.On, true, "Allowed. On a travel day", "Android marked the example as a Live Update.")]
    [InlineData(LiveSupport.On, false, "Allowed. On a travel day", "but Android did not make it a Live Update.")]
    [InlineData(LiveSupport.Off, false, "Turned off for Jourfold in the system settings.", "but Android did not make it a Live Update.")]
    [InlineData(LiveSupport.None, null, "This Android version has no Live Updates.", "The example is in the notifications for a minute.")]
    public async Task TheSettingsSayWhetherTheTravelDayIsALiveUpdateAndShowAnExample(LiveSupport support, bool? promoted, string state, string result)
    {
        var notices = new LiveNotices(support, promoted);
        var (session, window) = await OpenAsync(notices);
        try
        {
            session.ShowPage("settings"); Probe.Layout();
            // Only with the reminders on is there anything to say about them.
            Assert.DoesNotContain(Probe.All<TextBlock>(window), t => t.Text == "Live Update in the status bar");
            await session.SetNoticesAsync(true); Probe.Layout();
            Assert.Contains(Probe.All<TextBlock>(window), t => t.Text == "Live Update in the status bar");
            Assert.Contains(Probe.All<TextBlock>(window), t => t.Text!.StartsWith(state, StringComparison.Ordinal));
            // Turned off in the system: a way to allow it, and only then.
            var allow = Probe.All<Button>(window).FirstOrDefault(b => Avalonia.Automation.AutomationProperties.GetName(b) == "Allow in the system settings");
            Assert.Equal(support == LiveSupport.Off, allow is not null);
            if (allow is not null) { Probe.Click(window, "Allow in the system settings"); Assert.Equal(1, notices.OpenedLiveSettings); }

            // The example is a journey under way that arrives in 45 minutes, so it has a time for the status bar and progress.
            var before = notices.Live.Count;
            Probe.Click(window, "Show an example");
            await Probe.Until(() => session.LiveExampleResult is not null);
            var example = notices.Live[before]!;
            Assert.Equal(session.Now + Duration.FromMinutes(45), example.Until);
            Assert.True(example.From < session.Now);
            Assert.Equal("Example journey · arrives 11:25", example.Title);
            // The system takes it away after a minute, also when the app is asleep by then.
            Assert.Equal(MobileSession.LiveExampleTime, example.Lasts);
            Probe.Layout();
            Assert.Contains(Probe.All<TextBlock>(window), t => t.Tag as string == "live.result" && t.Text!.Contains(result, StringComparison.Ordinal));
        }
        finally { window.Close(); session.Dispose(); }
    }

    [AvaloniaFact]
    public async Task OnASamsungPhoneTheSettingsSayWhereToLetJourfoldsLiveUpdatesThrough()
    {
        var notices = new LiveNotices(LiveSupport.On, true) { LiveByMakerOnly = true };
        var (session, window) = await OpenAsync(notices);
        try
        {
            session.ShowPage("settings"); await session.SetNoticesAsync(true); Probe.Layout();
            var hint = Assert.Single(Probe.All<TextBlock>(window), t => t.Tag as string == "live.maker");
            Assert.Contains("Live notifications for all apps", hint.Text, StringComparison.Ordinal);
            Assert.Contains("developer options", hint.Text, StringComparison.Ordinal);
        }
        finally { window.Close(); session.Dispose(); }
    }
}
