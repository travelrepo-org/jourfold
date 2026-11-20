using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Jourfold.Mobile;
using Jourfold.Mobile.Controls;
using NodaTime;
using TravelRepo.Git;
using Xunit;
namespace Jourfold.Tests;

/// <summary>
/// The phone app on iOS feels like an iOS app: text follows Larger Text, and date and time fields open the system's
/// own pickers.
/// </summary>
public sealed class IosNativeFeelTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "jourfold-ios-feel-" + Guid.NewGuid());
    public void Dispose() { TestFiles.Delete(root); TextScale.Apply(new ResourceDictionary(), 1); }
    private static readonly DateTimeZone Tokyo = DateTimeZoneProviders.Tzdb["Asia/Tokyo"];

    private async Task<(MobileSession Session, Window Window)> OpenAsync(Func<MobileHost, MobileHost>? change = null)
    {
        var now = new LocalDateTime(2027, 5, 16, 10, 40).InZoneStrictly(Tokyo).ToInstant();
        var host = new MobileHost(root, new GitCliBackend(), new FakeClock(now), () => Tokyo, "en", new MemorySecrets());
        var session = new MobileSession(change?.Invoke(host) ?? host);
        await session.OpenAsync(await session.CreateSampleAsync(new LocalDate(2027, 5, 14)));
        var window = new Window { Width = 412, Height = 915, Content = new AppShell(session) }; window.Show();
        Probe.Layout();
        return (session, window);
    }

    [Fact]
    public void LargerTextScalesTheThemesSizesWithinBounds()
    {
        var resources = new ResourceDictionary();
        TextScale.Apply(resources, TextScale.FromIosCategory("UICTContentSizeCategoryXXXL"));
        Assert.Equal(23 / 17.0, TextScale.Factor, 3);
        Assert.Equal(Math.Round(15 * 23 / 17.0, 1), (double)resources["Size.Body"]!);
        Assert.Equal(Math.Round(13 * 23 / 17.0, 1), (double)resources["Size.Caption"]!);
        Assert.Equal((double)resources["Size.Body"]!, (double)resources["ControlContentThemeFontSize"]!);
        // The largest accessibility sizes stop where fixed parts still hold their text; the standard is unchanged.
        TextScale.Apply(resources, TextScale.FromIosCategory("UICTContentSizeCategoryAccessibilityXXXL"));
        Assert.Equal(TextScale.Largest, TextScale.Factor);
        TextScale.Apply(resources, TextScale.FromIosCategory("UICTContentSizeCategoryL"));
        Assert.Equal(15d, (double)resources["Size.Body"]!);
        Assert.Equal(1d, TextScale.FromIosCategory(null));
    }

    [AvaloniaFact]
    public async Task SizesSetInCodeFollowLargerTextWhenThePageIsDrawnAgain()
    {
        var (session, window) = await OpenAsync();
        try
        {
            session.ShowQuickAdd(); Probe.Layout();
            Assert.Equal(18, window.GetVisualDescendants().OfType<TextBox>().First(t => t.IsVisible).FontSize);
            session.CloseOverlay(); Probe.Layout();
            TextScale.Apply(new ResourceDictionary(), 1.3);
            session.ShowQuickAdd(); Probe.Layout();
            Assert.Equal(Math.Round(18 * 1.3, 1), window.GetVisualDescendants().OfType<TextBox>().First(t => t.IsVisible).FontSize);
        }
        finally { window.Close(); session.Dispose(); }
    }

    [AvaloniaFact]
    public async Task ATimeFieldOpensTheSystemsPickerAndTakesWhatWasChosen()
    {
        var asked = new List<(TimeSpan? Shown, int Steps)>();
        var answer = new PickedTime(true, new TimeSpan(14, 30, 0));
        var (session, window) = await OpenAsync(h => h.WithPickers(
            (shown, steps, _) => { asked.Add((shown, steps)); return Task.FromResult(answer); },
            (_, _) => Task.FromResult(new PickedDate(true, new DateTime(2027, 5, 20)))));
        try
        {
            session.ShowQuickAdd(); Probe.Layout();
            var start = window.GetVisualDescendants().OfType<TimePicker>().First(p => p.IsVisible);
            var button = start.GetVisualDescendants().OfType<Button>().First();
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, button)); Probe.Layout();
            await Probe.Until(() => start.SelectedTime is not null);
            Assert.Equal(new TimeSpan(14, 30, 0), start.SelectedTime);
            Assert.Equal(5, Assert.Single(asked).Steps);
            // Avalonia's own wheels did not open as well.
            Assert.DoesNotContain(start.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.Popup>(), p => p.IsOpen);
            // Cancelling keeps what the field had.
            answer = default;
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, button)); Probe.Layout();
            await Probe.Until(() => asked.Count == 2);
            Probe.Layout();
            Assert.Equal(new TimeSpan(14, 30, 0), start.SelectedTime);
            Assert.Equal(new TimeSpan(14, 30, 0), asked[1].Shown);
        }
        finally { window.Close(); session.Dispose(); }
    }

    [AvaloniaFact]
    public async Task WithoutSystemPickersTheFieldsKeepTheirOwnWheels()
    {
        var (session, window) = await OpenAsync();
        try
        {
            session.ShowQuickAdd(); Probe.Layout();
            var start = window.GetVisualDescendants().OfType<TimePicker>().First(p => p.IsVisible);
            var button = start.GetVisualDescendants().OfType<Button>().First();
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, button)); Probe.Layout();
            Assert.Contains(start.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.Popup>(), p => p.IsOpen);
        }
        finally { window.Close(); session.Dispose(); }
    }
    [AvaloniaFact]
    public async Task MenusAndQuestionsUseTheSystemsSheetsAndRunWhatWasChosen()
    {
        SystemMenu? shownMenu = null; SystemQuestion? asked = null;
        var (session, window) = await OpenAsync(h => h with
        {
            ShowMenu = m => { shownMenu = m; return Task.FromResult<int?>(1); },
            Ask = q => { asked = q; return Task.FromResult(true); }
        });
        try
        {
            var ran = new List<string>();
            session.ShowMenu(new ActionMenu("Senso-ji", null,
            [
                new MenuAction("Edit", "pencil", _ => { ran.Add("edit"); return Task.CompletedTask; }),
                new MenuAction("Delete", "trash", _ => { ran.Add("delete"); return Task.CompletedTask; }, Danger: true)
            ]));
            Probe.Layout();
            await Probe.Until(() => ran.Count == 1);
            Assert.Equal(["delete"], ran);
            Assert.Equal([new SystemMenuEntry("Edit"), new SystemMenuEntry("Delete", true)], shownMenu!.Entries);
            Assert.Equal("Cancel", shownMenu.Cancel);
            Assert.Null(session.Menu);
            // The app draws no menu of its own as well.
            Assert.DoesNotContain(Probe.All<Panel>(window), p => p.Classes.Contains("menu"));

            session.Confirm("Remove the trip from this phone?", "Remove", () => { ran.Add("removed"); return Task.CompletedTask; }, "It stays on GitHub.");
            Probe.Layout();
            await Probe.Until(() => ran.Count == 2);
            Assert.Equal(new SystemQuestion("Remove the trip from this phone?", "It stays on GitHub.", "Remove", "Cancel"), asked);
            Assert.Null(session.Asking);
        }
        finally { window.Close(); session.Dispose(); }
    }

    [AvaloniaFact]
    public async Task AMenuToSearchInStaysTheAppsOwn()
    {
        var called = false;
        var (session, window) = await OpenAsync(h => h with { ShowMenu = _ => { called = true; return Task.FromResult<int?>(null); } });
        try
        {
            session.ShowMenu(new ActionMenu("Link to", null, [new MenuAction("A", "x", _ => Task.CompletedTask)], Searchable: true));
            Probe.Layout();
            Assert.False(called);
            Assert.Contains(Probe.All<Panel>(window), p => p.Classes.Contains("menu"));
        }
        finally { window.Close(); session.Dispose(); }
    }

    [AvaloniaFact]
    public async Task WithIosBarTheDetailsFloatBelowTheTopAndReachTheEdgesAtIt()
    {
        var bar = new SearchBar();
        var (session, window) = await OpenAsync(h => h with { Bar = bar });
        try
        {
            var item = session.Trip!.Entities.Values.First(e => e.Type == "schedule_item");
            session.ShowDetails(item.Id); Probe.Layout();
            var sheet = Probe.All<Sheet>(window).Single();
            Assert.True(sheet.Floating);
            var panel = (Border)sheet.Children[1];
            Assert.Equal(new Thickness(Sheet.FloatingGap, 0, Sheet.FloatingGap, Sheet.FloatingGap), panel.Margin);
            Assert.Equal(new CornerRadius(Sheet.FloatingRadius), panel.CornerRadius);
            sheet.Show(2); Probe.Layout();
            Assert.Equal(default, panel.Margin);
            Assert.Equal(new CornerRadius(20, 20, 0, 0), panel.CornerRadius);
        }
        finally { window.Close(); session.Dispose(); }
    }

    [AvaloniaFact]
    public async Task WithoutIosBarSheetsKeepTheirEdges()
    {
        var (session, window) = await OpenAsync();
        try
        {
            session.ShowDetails(session.Trip!.Entities.Values.First(e => e.Type == "schedule_item").Id); Probe.Layout();
            var sheet = Probe.All<Sheet>(window).Single();
            Assert.False(sheet.Floating);
            Assert.Equal(default, ((Border)sheet.Children[1]).Margin);
        }
        finally { window.Close(); session.Dispose(); }
    }

    /// <summary>A bar like iOS 26's, with a place for the search set apart.</summary>
    private sealed class SearchBar : ISystemBar
    {
        public IReadOnlyList<SystemBarItem> Items { get; private set; } = [];
        public int Selected { get; private set; } = -1;
        public double Height => 98;
        public bool SearchTab => true;
        public event Action<int>? Tapped;
        public event Action? HeightChanged { add { } remove { } }
        public void Show(IReadOnlyList<SystemBarItem> items, int selected, Avalonia.Media.Color lightAccent, Avalonia.Media.Color darkAccent) { Items = items; Selected = selected; }
        public void Hide() { }
        public void Scrolled(double offset, double extent, double viewport) { }
        public void Tap(int index) { Tapped?.Invoke(index); Probe.Layout(); }
    }

    [AvaloniaFact]
    public async Task OnIos26TheSearchIsAPlaceOfTheBarInsteadOfAButtonAtTheTop()
    {
        var bar = new SearchBar();
        var (session, window) = await OpenAsync(h => h with { Bar = bar });
        try
        {
            Assert.Equal(["Today", "Plan", "Add", "Costs", "More", "Search"], bar.Items.Select(i => i.Label).ToArray());
            Assert.True(bar.Items[^1].Search);
            // The top keeps its way back, round as iOS draws it, and has no search of its own.
            Assert.DoesNotContain(Probe.All<Button>(window), b => Avalonia.Automation.AutomationProperties.GetName(b) == "Search the trip");
            Assert.Contains("round", Probe.All<Button>(window).First(b => Avalonia.Automation.AutomationProperties.GetName(b) == "Trips").Classes);
            bar.Tap(bar.Items.Count - 1); Probe.Layout();
            Assert.Equal("search", session.Page);
            Assert.Equal(bar.Items.Count - 1, bar.Selected);
            bar.Tap(0); Probe.Layout();
            Assert.Equal(MobileTab.Today, session.Tab);
            Assert.Equal(0, bar.Selected);
        }
        finally { window.Close(); session.Dispose(); }
    }

    [AvaloniaFact]
    public async Task WithoutThePlaceTheSearchStaysAtTheTop()
    {
        var (session, window) = await OpenAsync();
        try { Assert.Contains(Probe.All<Button>(window), b => Avalonia.Automation.AutomationProperties.GetName(b) == "Search the trip"); }
        finally { window.Close(); session.Dispose(); }
    }
}

internal static class PickerHosts
{
    public static MobileHost WithPickers(this MobileHost host, Func<TimeSpan?, int, string?, Task<PickedTime>> time, Func<DateTime?, string?, Task<PickedDate>> date) =>
        host with { PickTime = time, PickDate = date };
}
