using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Jourfold.Application;
using Jourfold.Mobile;
using NodaTime;
using TravelRepo.Git;
using Xunit;
namespace Jourfold.Tests;

/// <summary>
/// The system's own tab bar, as on iPhone: it shows the places the shell decides, marks the one the user is at, and
/// only reports taps back. A platform without one keeps the bottom bar the shell draws.
/// </summary>
public sealed class MobileSystemBarTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "jourfold-mobile-bar-" + Guid.NewGuid());
    public void Dispose() => TestFiles.Delete(root);

    private static readonly DateTimeZone Tokyo = DateTimeZoneProviders.Tzdb["Asia/Tokyo"];

    /// <summary>Records what the shell asks the bar to show, and taps it like a finger would.</summary>
    private sealed class FakeBar : ISystemBar
    {
        public IReadOnlyList<SystemBarItem>? Items { get; private set; }
        public int Selected { get; private set; } = -1;
        public bool Visible { get; private set; }
        public double Covers { get; set; } = 98;
        public double Height => Visible ? Covers : 0;
        public double Offset { get; private set; }
        public event Action<int>? Tapped;
        public event Action? HeightChanged;

        public void Show(IReadOnlyList<SystemBarItem> items, int selected, Avalonia.Media.Color lightAccent, Avalonia.Media.Color darkAccent)
        {
            var appeared = !Visible;
            Items = items; Selected = selected; Visible = true;
            if (appeared) HeightChanged?.Invoke();
        }

        public void Hide() { Visible = false; HeightChanged?.Invoke(); }
        public void Scrolled(double offset, double extent, double viewport) => Offset = offset;
        public void Tap(int index) { Tapped?.Invoke(index); Probe.Layout(); }
    }

    private async Task<(MobileSession Session, Window Window, FakeBar Bar)> OpenAsync(string language = "en", double width = 412)
    {
        var bar = new FakeBar();
        var now = new LocalDateTime(2027, 5, 16, 10, 40).InZoneStrictly(Tokyo).ToInstant();
        var session = new MobileSession(new MobileHost(root, new GitCliBackend(), new FakeClock(now), () => Tokyo, language, new MemorySecrets()) { Bar = bar });
        await session.OpenAsync(await session.CreateSampleAsync(new LocalDate(2027, 5, 14)));
        var window = new Window { Width = width, Height = 915, Content = new AppShell(session) }; window.Show();
        Probe.Layout();
        return (session, window, bar);
    }

    private static string[] Labels(FakeBar bar) => bar.Items!.Select(i => i.Label).ToArray();

    [AvaloniaFact]
    public async Task OnPhonesTheSystemBarReplacesTheDrawnOneWithTheSamePlaces()
    {
        var (session, window, bar) = await OpenAsync();
        try
        {
            Assert.True(bar.Visible);
            Assert.Equal(["Today", "Plan", "Add", "Costs", "More"], Labels(bar));
            Assert.Equal([false, false, true, false, false], bar.Items!.Select(i => i.Action).ToArray());
            Assert.Equal(["calendar", "list", "plus", "wallet", "ellipsis"], bar.Items!.Select(i => i.Icon).ToArray());
            Assert.Equal(0, bar.Selected);
            // The shell's own bar is not drawn as well.
            Assert.DoesNotContain(Probe.All<Button>(window), b => b.Classes.Contains("tab") || b.Classes.Contains("plus"));
            // The page scrolls on beneath the bar and ends with room for it.
            var scroll = Probe.All<ScrollViewer>(window).First(s => s.VerticalScrollBarVisibility != Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled);
            Assert.True(scroll.Padding.Bottom >= bar.Covers);
        }
        finally { window.Close(); session.Dispose(); }
    }

    [AvaloniaFact]
    public async Task TapsGoWhereTheDrawnBarGoesAndTheMarkFollowsTheShell()
    {
        var (session, window, bar) = await OpenAsync();
        try
        {
            bar.Tap(1);
            Assert.Equal(MobileTab.Plan, session.Tab);
            Assert.Equal(1, bar.Selected);

            // The third place is a page under More, marked as its own place.
            bar.Tap(3);
            Assert.Equal((MobileTab.More, "costs"), (session.Tab, session.Page));
            Assert.Equal(3, bar.Selected);

            // Another page under More marks More.
            session.ShowPage("packing"); Probe.Layout();
            Assert.Equal(4, bar.Selected);

            // The plus opens Quick Add, ready for an expense on the costs, and the sheet covers the bar.
            bar.Tap(3); bar.Tap(2);
            Assert.Equal(Overlay.QuickAdd, session.Showing);
            Assert.False(bar.Visible);
            session.CloseOverlay(); Probe.Layout();
            Assert.True(bar.Visible);
            Assert.Equal(3, bar.Selected);
        }
        finally { window.Close(); session.Dispose(); }
    }

    [AvaloniaFact]
    public async Task TheOneDayTimetableEndsAboveTheBarWithItsNightsBand()
    {
        var (session, window, bar) = await OpenAsync();
        try
        {
            session.Settings.Values["plan.day"] = "timetable";
            bar.Tap(1);
            // The whole timetable, the nights band below its grid included, ends above the bar; the grid itself gets
            // no extra room, or the band would lie under the bar where it cannot be tapped.
            var area = Probe.All<Control>(window).Single(c => c.Classes.Contains(AppShell.AboveBar));
            Assert.Equal(bar.Covers, area.Margin.Bottom);
            Assert.All(Probe.All<ScrollViewer>(area), s => Assert.True(s.Padding.Bottom < bar.Covers));
        }
        finally { window.Close(); session.Dispose(); }
    }

    [AvaloniaFact]
    public async Task GermanLabelsAreShort()
    {
        var (session, window, bar) = await OpenAsync("de");
        try { Assert.Equal(["Heute", "Plan", "Hinzufügen", "Kosten", "Mehr"], Labels(bar)); }
        finally { window.Close(); session.Dispose(); }
    }

    [AvaloniaFact]
    public async Task WiderScreensKeepTheRailAndTheTripListHasNoBar()
    {
        var (session, window, bar) = await OpenAsync(width: 1024);
        try
        {
            Assert.False(bar.Visible);
            Assert.Contains(Probe.All<Button>(window), b => b.Classes.Contains("tab"));

            window.Width = 412; Probe.Layout();
            Assert.True(bar.Visible);
            session.Close(); Probe.Layout();
            Assert.False(bar.Visible);
        }
        finally { window.Close(); session.Dispose(); }
    }

    /// <summary>
    /// Every page on the phone, scrolled to its end, leaves nothing to tap under the bar that floats over it: what
    /// scrolls ends with room for the bar, and what stays put, such as the timetable's nights band, ends above it.
    /// A page that forgets this fails here.
    /// </summary>
    [AvaloniaFact]
    public async Task NothingToTapStaysUnderTheBarOnAnyPage()
    {
        var (session, window, bar) = await OpenAsync();
        try
        {
            var pages = new (string Name, Action Show)[]
            {
                ("today", () => session.ShowTab(MobileTab.Today)),
                ("plan list", () => session.ShowTab(MobileTab.Plan)),
                ("plan timetable", () => { session.Settings.Values["plan.day"] = "timetable"; session.ShowTab(MobileTab.Today); session.ShowTab(MobileTab.Plan); }),
                ("map", () => session.ShowTab(MobileTab.Map)),
                ("more", () => session.ShowTab(MobileTab.More)),
            }.Concat(new[] { "trip", "bookings", "tasks", "packing", "converter", "places", "files", "costs", "search", "pay", "travellers", "sync", "settings", "about" }
                .Select(page => (page, (Action)(() => { session.ShowTab(MobileTab.More); session.ShowPage(page); })))).ToArray();
            foreach (var (name, show) in pages)
            {
                show(); Probe.Layout();
                Assert.True(bar.Visible, name + ": the bar is shown");
                foreach (var scroll in Probe.All<ScrollViewer>(window))
                    scroll.Offset = new Avalonia.Vector(scroll.Offset.X, Math.Max(0, scroll.Extent.Height - scroll.Viewport.Height));
                Probe.Layout();
                var top = window.Bounds.Height - bar.Height;
                foreach (var control in Probe.All<Control>(window).Where(c => c is Button or TextBox or Avalonia.Controls.Primitives.ToggleButton or Slider or ComboBox or DatePicker && c.IsEffectivelyVisible
                    // A scroll bar's own arrows run the page's full height; the page scrolls under the bar on purpose.
                    && !Avalonia.VisualTree.VisualExtensions.GetVisualAncestors(c).Any(a => a is Avalonia.Controls.Primitives.ScrollBar)))
                    if (Shown(control, window) is { } shown)
                        Assert.True(shown.Bottom <= top + 0.5, $"{name}: {Avalonia.Automation.AutomationProperties.GetName(control)} ({control.GetType().Name}) ends at {shown.Bottom}, under the bar from {top}");
            }
        }
        finally { window.Close(); session.Dispose(); }
    }

    /// <summary>The part of a control on screen, cut by whatever around it clips, such as a scrolling page; null when none.</summary>
    private static Avalonia.Rect? Shown(Control control, Window window)
    {
        if (Avalonia.VisualExtensions.TransformToVisual(control, window) is not { } toWindow) return null;
        var rect = new Avalonia.Rect(control.Bounds.Size).TransformToAABB(toWindow);
        foreach (var around in Avalonia.VisualTree.VisualExtensions.GetVisualAncestors(control).OfType<Avalonia.Visual>())
            if (around.ClipToBounds && Avalonia.VisualExtensions.TransformToVisual(around, window) is { } clip)
                rect = rect.Intersect(new Avalonia.Rect(around.Bounds.Size).TransformToAABB(clip));
        return rect.Width > 0.5 && rect.Height > 0.5 ? rect : null;
    }
}
