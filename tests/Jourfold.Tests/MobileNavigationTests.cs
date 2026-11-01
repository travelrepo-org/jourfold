using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Jourfold.Application;
using Jourfold.Mobile;
using Jourfold.Mobile.Controls;
using NodaTime;
using TravelRepo.Core;
using TravelRepo.Git;
using Xunit;
namespace Jourfold.Tests;

/// <summary>Back leads to where something was opened from: pages from Today, a booking from its stay, the editor from the details.</summary>
public sealed class MobileNavigationTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "jourfold-mobile-nav-" + Guid.NewGuid());
    public void Dispose() => TestFiles.Delete(root);

    private static readonly DateTimeZone Tokyo = DateTimeZoneProviders.Tzdb["Asia/Tokyo"];

    private async Task<(MobileSession Session, Window Window)> OpenAsync(LocalDate start)
    {
        var now = new LocalDateTime(2027, 5, 16, 10, 40).InZoneStrictly(Tokyo).ToInstant();
        var session = new MobileSession(new MobileHost(root, new GitCliBackend(), new FakeClock(now), () => Tokyo, "en", new MemorySecrets(), AppVersion: "1.2.3-preview.4"));
        await session.OpenAsync(await session.CreateSampleAsync(start));
        var window = new Window { Width = 412, Height = 915, Content = new AppShell(session) }; window.Show();
        Probe.Layout();
        return (session, window);
    }

    /// <summary>The system back, as Escape on a keyboard.</summary>
    private static void Back(Window window)
    {
        var shell = (AppShell)window.Content!;
        shell.RaiseEvent(new Avalonia.Input.KeyEventArgs { RoutedEvent = Avalonia.Input.InputElement.KeyDownEvent, Key = Avalonia.Input.Key.Escape, Source = shell });
        Probe.Layout();
    }
    private static Sheet Sheet(Window window) { Probe.Layout(); return window.GetVisualDescendants().OfType<Sheet>().First(); }
    private static bool Shows(Window window, string text) { Probe.Layout(); return Probe.All<TextBlock>(window).Any(t => t.Text == text && t.IsEffectivelyVisible); }

    [AvaloniaFact]
    public async Task BeforeTheTripPackingIsOnTopOfTodayAndBackReturnsThere()
    {
        var (session, window) = await OpenAsync(new LocalDate(2027, 6, 1));
        try
        {
            var buttons = Probe.All<Button>(window).Select(AutomationProperties.GetName).ToList();
            Assert.True(buttons.IndexOf("Packing lists") >= 0 && buttons.IndexOf("Packing lists") < buttons.IndexOf("Open the plan"));
            Assert.True(Shows(window, "Before the trip"));

            Probe.Click(window, "Packing lists"); Probe.Layout();
            Assert.Equal((MobileTab.More, "packing"), (session.Tab, session.Page));
            Assert.Equal("Today", session.BackLabel);
            Assert.Contains(Probe.All<Button>(window), b => AutomationProperties.GetName(b) == "Today");
            Back(window);
            Assert.Equal((MobileTab.Today, (string?)null), (session.Tab, session.Page));

            // From More, back still leads to More.
            session.ShowTab(MobileTab.More); session.ShowPage("packing"); Probe.Layout();
            Back(window);
            Assert.Equal((MobileTab.More, (string?)null), (session.Tab, session.Page));
        }
        finally { window.Close(); session.Dispose(); }
    }

    [AvaloniaFact]
    public async Task ABookingOpensFromItsStayAndBackReturnsWhileCloseCloses()
    {
        var (session, window) = await OpenAsync(new LocalDate(2027, 5, 14));
        try
        {
            var hotel = session.Trip!.Entities.Values.First(e => e.Type == "schedule_item" && TripCommands.BookingOf(e) is { } b && session.Trip!.Find(b)!.Title == "Hotel Gracery Shinjuku");
            var booking = session.Trip!.Find(TripCommands.BookingOf(hotel)!.Value)!;
            session.ShowDetails(hotel.Id); Probe.Layout();
            Assert.False(session.CanGoBackInDetails);
            Assert.DoesNotContain(Probe.All<Button>(Sheet(window)), b => AutomationProperties.GetName(b) == "Back");

            Probe.Click(Sheet(window), "Booking: " + booking.Title); Probe.Layout();
            Assert.Equal(booking.Id, session.OverlayItem);
            Assert.Contains(Probe.All<Button>(Sheet(window)), b => AutomationProperties.GetName(b) == hotel.Title);
            Probe.Click(Sheet(window), "Back"); Probe.Layout();
            Assert.Equal((Overlay.Details, (Guid?)hotel.Id), (session.Showing, session.OverlayItem));

            // The system back goes the same way; close closes, whatever came before.
            Probe.Click(Sheet(window), "Booking: " + booking.Title); Probe.Layout();
            Back(window);
            Assert.Equal(hotel.Id, session.OverlayItem);
            Probe.Click(Sheet(window), "Booking: " + booking.Title); Probe.Layout();
            Probe.Click(Sheet(window), "Close"); Probe.Layout();
            Assert.Equal(Overlay.None, session.Showing);

            // Editing the booking and cancelling returns to its details; opening anything from the plan starts afresh.
            session.ShowDetails(hotel.Id); session.FollowDetails(booking.Id); session.ShowEditor(booking.Id);
            session.CloseOverlay();
            Assert.Equal((Overlay.Details, (Guid?)booking.Id, true), (session.Showing, session.OverlayItem, session.CanGoBackInDetails));
            session.ShowDetails(booking.Id);
            Assert.False(session.CanGoBackInDetails);

            // Deleting the booking returns to the stay.
            session.ShowDetails(hotel.Id); session.FollowDetails(booking.Id);
            await session.DeleteAsync(booking.Id);
            Assert.Equal((Overlay.Details, (Guid?)hotel.Id), (session.Showing, session.OverlayItem));
        }
        finally { window.Close(); session.Dispose(); }
    }

    [AvaloniaFact]
    public async Task AboutIsAPageWithTheLicensesAndTheInstalledVersion()
    {
        var (session, window) = await OpenAsync(new LocalDate(2027, 5, 14));
        try
        {
            session.ShowTab(MobileTab.More); session.ShowPage("settings"); Probe.Layout();
            Assert.True(Shows(window, "1.2.3-preview.4"));
            Probe.Click(window, "About Jourfold"); Probe.Layout();
            Assert.Equal("about", session.Page);
            Assert.True(Shows(window, "Version 1.2.3-preview.4"));
            Probe.Click(window, "Read license"); Probe.Layout();
            Assert.Equal("license.jourfold", session.Page);
            Assert.Contains(Probe.All<SelectableTextBlock>(window), t => t.Text?.Contains("GNU GENERAL PUBLIC LICENSE", StringComparison.Ordinal) == true);
            Back(window);
            Assert.Equal("about", session.Page);
            Back(window);
            Assert.Equal("settings", session.Page);
            session.OpenPage("notices"); Probe.Layout();
            Assert.Contains(Probe.All<SelectableTextBlock>(window), t => t.Text?.Contains("Avalonia", StringComparison.Ordinal) == true);
        }
        finally { window.Close(); session.Dispose(); }
    }

    [Fact]
    public void RatesAreFetchedOnceInAWhileEvenWhenTheirDateIsAWeekendOld()
    {
        var file = Path.Combine(root, "rates.json"); Directory.CreateDirectory(root);
        var friday = new ExchangeRates(new DateOnly(2027, 5, 14), new Dictionary<string, decimal> { ["JPY"] = 160m });
        friday.Save(file);
        var sunday = new DateTime(2027, 5, 16, 9, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(file, sunday.AddHours(-2));

        var schedule = new RateSchedule();
        Assert.False(schedule.Begin(friday, file, nowUtc: sunday));
        Assert.True(schedule.Begin(friday, file, force: true, nowUtc: sunday));
        // A page drawn again right after an attempt does not fetch again, even when the file is old.
        File.SetLastWriteTimeUtc(file, sunday.AddDays(-3));
        Assert.False(schedule.Begin(friday, file, nowUtc: sunday.AddMinutes(1)));
        Assert.True(schedule.Begin(friday, file, nowUtc: sunday.AddMinutes(31)));
        Assert.True(new RateSchedule().Begin(null, file, nowUtc: sunday));
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
