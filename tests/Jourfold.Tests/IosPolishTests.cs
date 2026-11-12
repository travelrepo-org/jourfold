using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Jourfold.Mobile;
using Jourfold.Mobile.Views;
using Jourfold.UI;
using Jourfold.UI.Controls;
using NodaTime;
using TravelRepo.Git;
using Xunit;
namespace Jourfold.Tests;

/// <summary>Details found while trying the app on an iPhone and an iPad.</summary>
public sealed class IosPolishTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "jourfold-ios-" + Guid.NewGuid());
    public void Dispose() => TestFiles.Delete(root);
    private static readonly DateTimeZone Tokyo = DateTimeZoneProviders.Tzdb["Asia/Tokyo"];

    private async Task<MobileSession> Sample(string language = "en")
    {
        var now = new LocalDateTime(2027, 5, 16, 10, 40).InZoneStrictly(Tokyo).ToInstant();
        var session = new MobileSession(new MobileHost(root, new GitCliBackend(), new FakeClock(now), () => Tokyo, language, new MemorySecrets()));
        await session.OpenAsync(await session.CreateSampleAsync(new LocalDate(2027, 6, 4)));
        return session;
    }

    [AvaloniaFact]
    public async Task OnAnIPadKeyboardCommandVPastesInsteadOfTypingV()
    {
        var before = AppShell.AppleKeys; AppShell.AppleKeys = true;
        var session = await Sample();
        var window = new Window { Width = 1024, Height = 768, Content = new AppShell(session) }; window.Show();
        try
        {
            session.ShowQuickAdd(); Probe.Layout();
            var title = window.GetVisualDescendants().OfType<TextBox>().First(t => t.IsVisible);
            title.Focus(); Probe.Layout();
            await Avalonia.Input.Platform.ClipboardExtensions.SetTextAsync(window.Clipboard!, "Wanaka");
            window.KeyPress(Key.V, RawInputModifiers.Meta, PhysicalKey.V, "v");
            await Probe.Until(() => title.Text == "Wanaka");
            window.KeyPress(Key.A, RawInputModifiers.Meta, PhysicalKey.A, "a");
            Assert.Equal("Wanaka", title.SelectedText);
            window.KeyPress(Key.X, RawInputModifiers.Meta, PhysicalKey.X, "x");
            await Probe.Until(() => string.IsNullOrEmpty(title.Text));
            window.KeyPress(Key.Z, RawInputModifiers.Meta, PhysicalKey.Z, "z");
            await Probe.Until(() => title.Text == "Wanaka");
        }
        finally { AppShell.AppleKeys = before; window.Close(); session.Dispose(); }
    }

    [Fact]
    public void TimeRangesAndDurationsStayOnOneLine()
    {
        var english = new Localization("en"); var german = new Localization("de");
        Assert.DoesNotContain(' ', Formats.Duration(NodaTime.Duration.FromMinutes(675), english));
        Assert.Equal("11 Std. 15 Min.", Formats.Duration(NodaTime.Duration.FromMinutes(675), german));
        Assert.Equal("16:00⁠–⁠18:00", new ClockText("16:00", "18:00").Primary);
    }

    [AvaloniaFact]
    public async Task TheTimelineNamesTheTownALineStartsInRatherThanTheZone()
    {
        var session = await Sample();
        try
        {
            session.ShowDate(new LocalDate(2027, 6, 4));
            var window = new Window { Width = 412, Height = 915, Content = TimetableView.Build(session, 412) }; window.Show();
            var texts = window.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
            Assert.Contains("Frankfurt am Main", texts);
            Assert.DoesNotContain("Berlin", texts);
            window.Close();
        }
        finally { session.Dispose(); }
    }

    [AvaloniaFact]
    public void ARouteAcrossTheWorldFitsTheNarrowMapOfAPhone()
    {
        // Frankfurt to Tokyo in the part of a phone's map that the controls leave free.
        var map = new MapSurface(null); var window = new Window { Width = 348, Height = 300, Content = map }; window.Show(); Probe.Layout();
        (double, double) frankfurt = (50.03, 8.57), tokyo = (35.55, 139.78);
        map.Fit([frankfurt, tokyo], new Size(348, 300));
        foreach (var at in new[] { map.ToScreen(frankfurt), map.ToScreen(tokyo) }) Assert.InRange(at.X, 16, 332);
        window.Close();
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
