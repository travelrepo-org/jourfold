using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Jourfold.Mobile;
using NodaTime;
using TravelRepo.Git;
using Xunit;

namespace Jourfold.Tests;

/// <summary>The app on a tablet: the sidebar's button, the pages under More, the timetable's days.</summary>
public sealed class TabletLayoutTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "jourfold-tablet-" + Guid.NewGuid());
    public void Dispose() => TestFiles.Delete(root);

    private async Task<(MobileSession Session, Window Window)> OpenAsync(double width, double height)
    {
        var tokyo = DateTimeZoneProviders.Tzdb["Asia/Tokyo"];
        var now = new LocalDateTime(2027, 5, 16, 10, 40).InZoneStrictly(tokyo).ToInstant();
        var session = new MobileSession(new MobileHost(root, new GitCliBackend(), new FakeClock(now), () => tokyo, "en", new MemorySecrets(), AppVersion: "1.2.3"));
        await session.OpenAsync(await session.CreateSampleAsync(new LocalDate(2027, 6, 1)));
        var window = new Window { Width = width, Height = height, Content = new AppShell(session) }; window.Show(); Probe.Layout();
        return (session, window);
    }

    private static Point Where(Window window, Control control) => control.TranslatePoint(new Point(0, 0), window)!.Value;

    [AvaloniaFact]
    public async Task TheSidebarFoldsAndOpensFromTheSameCorner()
    {
        var (session, window) = await OpenAsync(1400, 900);
        try
        {
            var fold = Probe.Button(window, "Fold the sidebar"); var at = Where(window, fold);
            Assert.True(at.X < 40 && at.Y + fold.Bounds.Height > 900 - 24, $"fold at {at}");
            Probe.Click(window, "Fold the sidebar"); Probe.Layout();
            Assert.Equal("collapsed", session.Settings.Values["sidebar"]);
            Assert.Equal(at, Where(window, Probe.Button(window, "Show the sidebar")));
            Probe.Click(window, "Show the sidebar"); Probe.Layout();
            Assert.Equal(at, Where(window, Probe.Button(window, "Fold the sidebar")));
        }
        finally { window.Close(); session.Dispose(); }
    }

    [AvaloniaFact]
    public async Task EveryPageUnderMoreHasTheSameColumnSoTheWayBackStaysPut()
    {
        var (session, window) = await OpenAsync(1400, 900);
        try
        {
            var lefts = new List<double>();
            foreach (var page in new[] { "bookings", "travellers", "tasks", "converter" })
            {
                session.ShowTab(MobileTab.More); session.ShowPage(page); Probe.Layout();
                var column = Probe.All<Border>(window).First(b => b.MaxWidth == 760);
                Assert.Equal(760, column.Bounds.Width);
                lefts.Add(Where(window, Probe.Button(column, "More")).X);
            }
            Assert.Single(lefts.Distinct());
        }
        finally { window.Close(); session.Dispose(); }
    }

    [AvaloniaFact]
    public async Task ASwipeOnTheTimetableMovesByTheDaysShown()
    {
        var (session, window) = await OpenAsync(900, 900);
        try
        {
            session.Settings.Values["plan.mode"] = "timetable"; session.ShowTab(MobileTab.Plan);
            var days = session.Agenda.Days; session.ShowDate(days[0].Date); Probe.Layout();
            var shown = Mobile.Views.TimetableView.Columns(900 - 88);
            Assert.True(days.Count > shown);

            void Swipe(double from, double to)
            {
                var y = 600.0;
                window.MouseDown(new Point(from, y), MouseButton.Left);
                for (var step = 1; step <= 10; step++) window.MouseMove(new Point(from + (to - from) * step / 10, y + 2));
                window.MouseUp(new Point(to, y + 2), MouseButton.Left);
            }
            Swipe(800, 300);
            await Probe.Until(() => session.SelectedDate == days[shown].Date);
            // Back again, and no further than the first day.
            Swipe(300, 800); await Probe.Until(() => session.SelectedDate == days[0].Date);
            Swipe(300, 800); Probe.Layout(); await Task.Delay(300); Probe.Layout();
            Assert.Equal(days[0].Date, session.SelectedDate);
            // A short drag goes nowhere.
            Swipe(500, 470); await Task.Delay(300); Probe.Layout();
            Assert.Equal(days[0].Date, session.SelectedDate);
        }
        finally { window.Close(); session.Dispose(); }
    }

    private sealed class FakeClock(Instant now) : IClock { public Instant GetCurrentInstant() => now; }

    private sealed class MemorySecrets : TravelRepo.Providers.ISecretStore
    {
        private readonly Dictionary<string, string> values = [];
        public Task<string?> ReadAsync(string key, CancellationToken ct = default) => Task.FromResult(values.GetValueOrDefault(key));
        public Task WriteAsync(string key, string value, CancellationToken ct = default) { values[key] = value; return Task.CompletedTask; }
        public Task DeleteAsync(string key, CancellationToken ct = default) { values.Remove(key); return Task.CompletedTask; }
    }
}
