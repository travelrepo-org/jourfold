using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Jourfold.Desktop;
using Jourfold.Mobile;
using NodaTime;
using TravelRepo.Git;
using Xunit;

namespace Jourfold.Tests;

/// <summary>
/// A row or card that opens its item holds buttons of its own: a check box, a person's chip, "…", Copy, Navigate. A
/// click on one of them reaches the row too, and must not open the item there.
/// </summary>
public sealed class NestedButtonTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "jourfold-nested-" + Guid.NewGuid());
    public void Dispose() => TestFiles.Delete(root);

    /// <summary>Every button inside another button, with the button around it.</summary>
    private static (Button Outer, Button Inner)[] Nested(Visual root) =>
        [.. Probe.All<Button>(root).Select(inner => (Outer: inner.GetVisualAncestors().OfType<Button>().FirstOrDefault()!, Inner: inner)).Where(p => p.Outer is not null)];

    /// <summary>A click on <paramref name="inner"/>, which rises through the row around it.</summary>
    private static void Click(Button inner) => inner.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static string Name(Button b) => AutomationProperties.GetName(b) ?? b.Content as string ?? b.GetType().Name;

    [AvaloniaFact]
    public async Task OnTheDesktopAButtonInARowDoesNotOpenTheRow()
    {
        var w = new MainWindow(); w.Model.SetPreference("Language", "en"); w.Show(); Probe.Layout();
        try
        {
            var m = w.Model; m.Store.Set("trips.folder", root);
            await m.OpenSampleCommand.ExecuteAsync(null); await Probe.Until(() => m.HasTrip); m.InboxOpen = false;
            var pairs = 0;
            foreach (var view in new[] { "Plan", "List", "Map", "Bookings", "Costs", "Tasks", "Packing", "Files", "People", "Places", "Collections", "History", "Variants" })
            {
                m.Navigate(view); m.Selected = null; Probe.Layout();
                // A click may change the item and draw the view again, so the buttons are looked up afresh each time.
                var content = w.FindControl<ContentControl>("MainContent")!;
                for (var i = 0; Nested(content) is var nested && i < nested.Length; i++)
                {
                    var (outer, inner) = nested[i];
                    Click(inner); Probe.Layout();
                    // "Show on map" picks the place itself, on the map.
                    Assert.True(m.Selected is null || m.View != view, $"{view}: “{Name(inner)}” opened “{Name(outer)}”");
                    m.Navigate(view); m.Selected = null; Probe.Layout();
                    // "Who brings it?" asks in a sheet; it is closed again.
                    for (var layer = w.FindControl<Grid>("DialogLayer")!; layer.Children.Count > 0; Probe.Layout())
                        Probe.Click(layer.Children[^1], "Close");
                    pairs++;
                }
                // The row itself still opens its item.
                if (Nested(content) is [(var row, _), ..]) { row.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Assert.NotNull(m.Selected); m.Selected = null; }
            }
            Assert.True(pairs > 20); // packing, tasks, bookings and places have such rows in the sample
        }
        finally { w.Close(); }
    }

    [AvaloniaFact]
    public async Task OnThePhoneAButtonOnACardDoesNotTapTheCard()
    {
        var tokyo = DateTimeZoneProviders.Tzdb["Asia/Tokyo"];
        var now = new LocalDateTime(2027, 5, 16, 10, 40).InZoneStrictly(tokyo).ToInstant();
        var session = new MobileSession(new MobileHost(root, new GitCliBackend(), new FakeClock(now), () => tokyo, "en", new MemorySecrets(), AppVersion: "1.2.3"));
        await session.OpenAsync(await session.CreateSampleAsync(new LocalDate(2027, 5, 14)));
        var window = new Window { Width = 412, Height = 915, Content = new AppShell(session) }; window.Show(); Probe.Layout();
        try
        {
            var pairs = 0;
            void Check(string where)
            {
                Probe.Layout();
                for (var i = 0; Nested(window) is var nested && i < nested.Length; i++)
                {
                    var (outer, inner) = nested[i];
                    var before = (session.Tab, session.Page, session.Showing, session.SelectedItem);
                    Click(inner); Probe.Layout();
                    Assert.True(before == (session.Tab, session.Page, session.Showing, session.SelectedItem), $"{where}: “{Name(inner)}” acted as “{Name(outer)}”");
                    pairs++;
                }
            }
            foreach (var tab in Enum.GetValues<MobileTab>()) { session.ShowTab(tab); Check(tab.ToString()); }
            foreach (var page in new[] { "trip", "bookings", "tasks", "packing", "converter", "places", "files", "costs", "travellers", "sync", "settings", "about" })
            { session.ShowTab(MobileTab.More); session.ShowPage(page); Check(page); }
            Assert.True(pairs > 0); // the open card on the plan has Navigate
        }
        finally { window.Close(); }
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
