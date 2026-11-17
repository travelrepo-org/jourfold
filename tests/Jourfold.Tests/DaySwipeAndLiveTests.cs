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
    public async Task OnThePhonesTimetableSwipesFollowOneAnotherAndThePlanIsNotDrawnAgainAtRest()
    {
        var (session, window) = await OpenAsync();
        try
        {
            session.Settings.Values["plan.day"] = "timetable"; session.ShowTab(MobileTab.Plan);
            var days = session.Agenda.Days; session.ShowDate(days[0].Date); Probe.Layout();
            // No date strip: the days' heads name them. The list keeps its strip.
            Assert.Empty(DateChips(window));
            var page = Probe.All<TextBlock>(window).First();

            // Letting go names the day the glide is heading for as the chosen day, before it rests.
            // At a finger's pace, so the days do not fly to the end of the trip.
            await PacedSwipe(window, 300, 180);
            var first = session.SelectedDate;
            Assert.True(first > days[0].Date);
            Assert.True(first < days[^1].Date, $"The first swipe went to the last day of {days.Count}, which leaves nothing to check.");
            await PacedSwipe(window, 300, 180);
            var second = session.SelectedDate;
            Assert.True(second > first, "The second swipe right after the first did not move on.");

            // The days come to rest and the plan stays as it is, so a finger can go on whenever it likes.
            await Task.Delay(TimeSpan.FromMilliseconds(1500)); Probe.Layout();
            Assert.True(page.IsAttachedToVisualTree(), "The plan was drawn again when the days came to rest.");
            Assert.Equal(second, session.SelectedDate);
            await PacedSwipe(window, 180, 300);
            Assert.True(session.SelectedDate < second);
            Assert.True(page.IsAttachedToVisualTree());

            // A short swipe that is still moving when the finger lets go goes on a day, however unhurried; a drag that
            // stops and turns back a little before letting go stays on the day.
            await Task.Delay(TimeSpan.FromMilliseconds(800));
            var before = session.SelectedDate;
            window.MouseDown(new Point(300, 600), MouseButton.Left);
            // About 0.45 px/ms: well under a flick, yet far enough above the least speed that a slow test machine, whose
            // delays stretch, still lets go while moving.
            for (var step = 1; step <= 10; step++) { await Task.Delay(20); window.MouseMove(new Point(300 - 9 * step, 602)); }
            window.MouseUp(new Point(210, 602), MouseButton.Left);
            Assert.True(session.SelectedDate > before, "An unhurried short swipe did not move on.");
            await Task.Delay(TimeSpan.FromMilliseconds(800));
            before = session.SelectedDate;
            window.MouseDown(new Point(300, 600), MouseButton.Left);
            for (var step = 1; step <= 8; step++) { await Task.Delay(15); window.MouseMove(new Point(300 - 10 * step, 602)); }
            for (var step = 1; step <= 8; step++) { await Task.Delay(15); window.MouseMove(new Point(220 + step, 602)); }
            window.MouseUp(new Point(228, 602), MouseButton.Left);
            Assert.Equal(before, session.SelectedDate);

            // Back to the list, the strip is there again on the chosen day.
            session.Settings.Values["plan.day"] = "list"; session.ShowDate(session.SelectedDate); Probe.Layout();
            Assert.Equal(session.SelectedDate, DateChips(window).Single(c => c.IsSelected).Tag);
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
        public List<Instant?> Woken { get; } = [];
        public void WakeAt(Instant? at) => Woken.Add(at);
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

            // The example is a train under way that arrives in a minute, half way there, with the minutes left for the
            // status bar and the train riding along the progress.
            var before = notices.Live.Count;
            Probe.Click(window, "Show an example");
            await Probe.Until(() => session.LiveExampleResult is not null);
            var example = notices.Live[before]!;
            Assert.Equal(session.Now + Duration.FromMinutes(1), example.Until);
            Assert.Equal(50, example.Progress(session.Now));
            Assert.Equal("Train to Aachen · arrives 10:41", example.Title);
            Assert.Equal(("1 min", "train-front"), (example.Chip, example.Icon));
            // The system takes it away when it arrives, also when the app is asleep by then; until then the app wakes
            // every few seconds to move it on.
            Assert.Equal(MobileSession.LiveExampleTime, example.Lasts);
            Assert.Equal(session.Now + NoticeRunner.ExampleStep, notices.Woken.Last());
            Probe.Layout();
            Assert.Contains(Probe.All<TextBlock>(window), t => t.Tag as string == "live.result" && t.Text!.Contains(result, StringComparison.Ordinal));
        }
        finally { window.Close(); session.Dispose(); }
    }

    [Fact]
    public void TheExampleMovesOnLikeAJourneyAndIsGoneWhenItArrives()
    {
        var started = new LocalDateTime(2027, 5, 16, 10, 40).InZoneStrictly(Tokyo).ToInstant();
        var settings = MobileSettings.Load(Path.Combine(root, "settings.json"));
        var english = new Localization("en"); var german = new Localization("de");
        Assert.Null(NoticeRunner.Example(settings, started, Tokyo, english));
        settings.Values[NoticeRunner.ExampleSetting] = started.ToUnixTimeMilliseconds() + "|/trips/aachen";
        var (first, folder) = NoticeRunner.Example(settings, started, Tokyo, english)!.Value;
        Assert.Equal(("/trips/aachen", 50), (folder, first.Progress(started)));
        // Half a minute on, the same train is three quarters there and its time to go away is shorter.
        var later = NoticeRunner.Example(settings, started + Duration.FromSeconds(30), Tokyo, german)!.Value.Live;
        Assert.Equal((75, "1 Min", "Zug nach Aachen · Ankunft 10:41"), (later.Progress(started + Duration.FromSeconds(30)), later.Chip, later.Title));
        Assert.Equal(TimeSpan.FromSeconds(30), later.Lasts);
        Assert.Null(NoticeRunner.Example(settings, started + Duration.FromMinutes(1), Tokyo, english));
    }

    [Fact]
    public void TheStatusBarCountsTheMinutesInTheLastHourAndTheNotificationIsDrawnAgainAsItMovesOn()
    {
        var now = new LocalDateTime(2027, 5, 16, 10, 0).InZoneStrictly(Tokyo).ToInstant();
        var english = new Localization("en");
        Assert.Equal("45 min", LiveNotice.ChipFor(now + Duration.FromMinutes(44) + Duration.FromSeconds(10), now, Tokyo, english));
        Assert.Equal("60 min", LiveNotice.ChipFor(now + Duration.FromHours(1), now, Tokyo, english));
        Assert.Equal("11:01", LiveNotice.ChipFor(now + Duration.FromMinutes(61), now, Tokyo, english));
        // A long flight moves on a step every five minutes at most, a short ride every minute; the last hour every minute.
        LiveNotice Ride(Duration took, Duration left) => new("", "", now - took + left, now + left, now + left, Guid.Empty, false);
        Assert.Equal(now + Duration.FromMinutes(5), Ride(Duration.FromHours(11), Duration.FromHours(5)).Redraw(now));
        Assert.Equal(now + Duration.FromMinutes(3), Ride(Duration.FromHours(5), Duration.FromHours(2)).Redraw(now));
        Assert.Equal(now + Duration.FromMinutes(1), Ride(Duration.FromHours(11), Duration.FromMinutes(50)).Redraw(now));
        Assert.Equal(now + Duration.FromMinutes(1), Ride(Duration.FromMinutes(70), Duration.FromMinutes(65)).Redraw(now));
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
