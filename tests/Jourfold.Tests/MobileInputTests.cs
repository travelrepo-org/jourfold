using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Jourfold.Mobile;
using Jourfold.Mobile.Controls;
using NodaTime;
using TravelRepo.Core;
using TravelRepo.Git;
using Xunit;

namespace Jourfold.Tests;

/// <summary>
/// Touch and typing on the phone and tablet: amounts as people type them, the long press, the pickers' words in
/// German, what the device is called, and pages that keep their place without flashing their top when drawn again.
/// </summary>
public sealed class MobileInputTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "jourfold-mobile-input-" + Guid.NewGuid());
    public void Dispose() => TestFiles.Delete(root);

    private static readonly DateTimeZone Tokyo = DateTimeZoneProviders.Tzdb["Asia/Tokyo"];

    private async Task<(MobileSession Session, Window Window)> OpenAsync(string language = "en", double width = 412, double height = 915, LocalDate? start = null)
    {
        var now = new LocalDateTime(2027, 5, 16, 10, 40).InZoneStrictly(Tokyo).ToInstant();
        var session = new MobileSession(new MobileHost(root, new GitCliBackend(), new FakeClock(now), () => Tokyo, language, new MemorySecrets()));
        await session.OpenAsync(await session.CreateSampleAsync(start ?? new LocalDate(2027, 5, 14)));
        var window = new Window { Width = width, Height = height, Content = new AppShell(session) }; window.Show();
        Probe.Layout();
        return (session, window);
    }

    private static Sheet Form(Window window) { Probe.Layout(); return window.GetVisualDescendants().OfType<Sheet>().First(); }
    private static T Field<T>(Window window, string name) where T : Control => Probe.All<T>(Form(window)).First(c => AutomationProperties.GetName(c) == name);

    [AvaloniaTheory]
    [InlineData("1.234", "1234.00")]
    [InlineData("1.234,56", "1234.56")]
    [InlineData("1,234.56", "1234.56")]
    [InlineData("12,50", "12.50")]
    [InlineData("12.50", "12.50")]
    public async Task AnAmountIsReadAsPeopleTypeIt(string typed, string saved)
    {
        // A phone set to German, as Android sets the app's culture.
        var culture = System.Globalization.CultureInfo.CurrentCulture;
        System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo("de-DE");
        var (session, window) = await OpenAsync("de");
        try
        {
            session.ShowQuickAdd("expense");
            Field<TextBox>(window, "Titel").Text = "Taxi";
            Field<TextBox>(window, "Betrag").Text = typed;
            Assert.Equal("0,00", Field<TextBox>(window, "Betrag").PlaceholderText);
            Probe.Click(Form(window), "Hinzufügen");
            await Probe.Until(() => session.Trip!.Entities.Values.Any(e => e.Title == "Taxi"));
            Assert.Equal(saved, session.Trip!.Entities.Values.Single(e => e.Title == "Taxi").Data["amount"]!["value"]!.ToString());
        }
        finally { window.Close(); session.Dispose(); System.Globalization.CultureInfo.CurrentCulture = culture; }
    }

    [AvaloniaFact]
    public async Task TheConverterReadsThousandsToo()
    {
        Directory.CreateDirectory(root);
        new ExchangeRates(new DateOnly(2027, 5, 14), new Dictionary<string, decimal> { ["JPY"] = 160m, ["USD"] = 1.1m }).Save(Path.Combine(root, "rates.json"));
        var (session, window) = await OpenAsync();
        try
        {
            session.Settings.Values["rates.online"] = "on";
            session.Drafts["converter.from"] = "JPY"; session.Drafts["converter.to"] = "EUR";
            session.ShowTab(MobileTab.More); session.ShowPage("converter"); Probe.Layout();
            var amount = Probe.All<TextBox>(window).First(b => AutomationProperties.GetName(b) == "Amount");
            amount.Text = "1.600"; Probe.Layout();
            Assert.Contains(Probe.All<TextBlock>(window), t => t.Text is { } text && text.Contains("10.00", StringComparison.Ordinal));
        }
        finally { window.Close(); session.Dispose(); }
    }

    [AvaloniaFact]
    public async Task TimeFieldsSayHourAndMinuteInGerman()
    {
        var (session, window) = await OpenAsync("de");
        try
        {
            session.ShowQuickAdd(); Probe.Layout();
            var picker = Probe.All<TimePicker>(Form(window)).First(p => p.SelectedTime is null && p.IsEffectivelyVisible);
            var words = Probe.All<TextBlock>(picker).Select(t => t.Text).ToArray();
            Assert.Contains("Stunde", words); Assert.Contains("Minute", words);
            Assert.DoesNotContain("hour", words);
        }
        finally { window.Close(); session.Dispose(); }
    }

    [Fact]
    public void ThePickersWordsComeFromTheStrings()
    {
        var resources = new ResourceDictionary();
        new Localization("de").Pickers(resources);
        Assert.Equal(("Stunde", "Minute", "Tag", "Monat", "Jahr"), ((string)resources["StringTimePickerHourText"]!, (string)resources["StringTimePickerMinuteText"]!,
            (string)resources["StringDatePickerDayText"]!, (string)resources["StringDatePickerMonthText"]!, (string)resources["StringDatePickerYearText"]!));
        new Localization("en").Pickers(resources);
        Assert.Equal("hour", resources["StringTimePickerHourText"]);
    }

    [Theory]
    [InlineData(false, true, false, 411, "phone")]
    // A tablet with a SIM, or an emulator, that can make calls.
    [InlineData(false, true, false, 900, "tablet")]
    [InlineData(false, false, false, 900, "tablet")]
    // An unfolded foldable is still a phone, as its maker calls it.
    [InlineData(false, true, true, 673, "phone")]
    // A Wi-Fi tablet that is small.
    [InlineData(false, false, false, 540, "tablet")]
    [InlineData(true, false, false, 800, "device")]
    public void AnAndroidDeviceIsCalledWhatItIs(bool computer, bool calls, bool folds, double shortestSide, string expected) =>
        Assert.Equal(expected, MobileHost.AndroidDevice(computer, calls, folds, shortestSide));

    private sealed class FakeClock(Instant now) : IClock { public Instant GetCurrentInstant() => now; }

    private sealed class MemorySecrets : TravelRepo.Providers.ISecretStore
    {
        private readonly Dictionary<string, string> values = [];
        public Task<string?> ReadAsync(string key, CancellationToken ct = default) => Task.FromResult(values.GetValueOrDefault(key));
        public Task WriteAsync(string key, string value, CancellationToken ct = default) { values[key] = value; return Task.CompletedTask; }
        public Task DeleteAsync(string key, CancellationToken ct = default) { values.Remove(key); return Task.CompletedTask; }
    }
}
