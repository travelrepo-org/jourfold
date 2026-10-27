using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Jourfold.Mobile;
using NodaTime;
using TravelRepo.Git;

// Renders the phone and tablet app with the sample trip at a fixed moment: Sunday 16 May 2027, 10:40 in Tokyo, as in
// the mobile design spec. Usage: dotnet run --project eng/MobileScreenshots -- <output-directory> [--lang de]
var options = args.Where(a => a.StartsWith("--", StringComparison.Ordinal)).ToArray();
var output = Path.GetFullPath(args.FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal) && a is not ("de" or "en")) ?? "artifacts/mobile-screenshots");
var language = args.SkipWhile(a => a != "--lang").Skip(1).FirstOrDefault() ?? "en";
Directory.CreateDirectory(output);
var temporary = Path.Combine(Path.GetTempPath(), "jourfold-mobile-screenshots-" + Guid.NewGuid());
Environment.SetEnvironmentVariable("GIT_CONFIG_GLOBAL", Path.Combine(temporary, "gitconfig"));
Environment.SetEnvironmentVariable("GIT_CONFIG_NOSYSTEM", "1");
Directory.CreateDirectory(temporary); File.WriteAllText(Path.Combine(temporary, "gitconfig"), "");
var tokyo = DateTimeZoneProviders.Tzdb["Asia/Tokyo"];
var now = new LocalDateTime(2027, 5, 16, 10, 40).InZoneStrictly(tokyo).ToInstant();
MobileApp.Host = new MobileHost(temporary, new GitCliBackend(), new FixedClock(now), () => tokyo, language, new NoSecrets());

try
{
    var session = HeadlessUnitTestSession.StartNew(typeof(MobileScreenshotApp));
    await session.Dispatch(async () =>
    {
        var s = MobileApp.Session!;
        async Task Capture(string name, double width = 412, double height = 915, ThemeVariant? theme = null)
        {
            Avalonia.Application.Current!.RequestedThemeVariant = theme ?? ThemeVariant.Light;
            var window = new Window { Width = width, Height = height, Content = new AppShell(s) };
            window.Show();
            for (var i = 0; i < 6; i++) { await Task.Delay(60); Dispatcher.UIThread.RunJobs(); }
            using var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("No frame.");
            var path = Path.Combine(output, name + ".png"); frame.Save(path, PngBitmapEncoderOptions.Default); Console.WriteLine(path);
            window.Close();
        }
        await Capture("welcome");
        await s.CreateSampleAsync(new LocalDate(2027, 5, 14));
        await Capture("trips");
        await s.OpenAsync(s.Trips[0].Path);
        await Capture("today");
        await Capture("today-dark", theme: ThemeVariant.Dark);
        s.ShowTab(MobileTab.Plan); s.ShowDate(new LocalDate(2027, 5, 16));
        await Capture("plan");
        var teamLab = s.Agenda.Find(new LocalDate(2027, 5, 16))!.Entries.First(e => e.Item.Title.StartsWith("teamLab", StringComparison.Ordinal));
        s.Select(teamLab.Item.Id);
        await Capture("plan-selected");
        s.ShowDetails(teamLab.Item.Id);
        await Capture("detail-teamlab");
        var shinkansen = s.Agenda.Find(new LocalDate(2027, 5, 18))!.Entries.First(e => e.Item.Title.StartsWith("Shinkansen", StringComparison.Ordinal));
        s.ShowDate(new LocalDate(2027, 5, 18)); s.ShowDetails(shinkansen.Item.Id);
        await Capture("detail-transport");
        await Capture("tablet-detail", 1194, 834);
        s.CloseOverlay(); s.ShowDate(new LocalDate(2027, 5, 16)); s.ShowQuickAdd();
        await Capture("quick-add");
        s.CloseOverlay();
        s.ShowDate(new LocalDate(2027, 5, 17));
        foreach (var group in s.Agenda.Find(new LocalDate(2027, 5, 17))!.Entries.Where(e => e.IsGroup)) s.ToggleGroup(group.Item.Id);
        await Capture("plan-nested");
        s.ShowDate(new LocalDate(2027, 5, 14));
        await Capture("plan-flight");
        s.ShowTab(MobileTab.Map); s.ShowDate(new LocalDate(2027, 5, 16));
        await Capture("map");
        s.ShowTab(MobileTab.More);
        await Capture("more");
        s.ShowPage("bookings");
        s.ToggleGroup(s.Trip!.Entities.Values.First(e => e.Type == "booking" && e.Title.StartsWith("teamLab", StringComparison.Ordinal)).Id);
        await Capture("bookings");
        s.ShowPage("tasks");
        await Capture("tasks");
        s.ShowPage("costs");
        s.ToggleGroup(s.Trip!.Entities.Values.First(e => e.Type == "expense" && e.Title == "Flights").Id);
        await Capture("costs");
        s.ShowPage("travellers");
        await Capture("travellers");
        s.ShowPage("sync"); await s.RefreshVersionsAsync();
        await Capture("sync");
        s.ShowPage("settings");
        await Capture("settings");
        s.ShowTab(MobileTab.Plan);
        await Capture("tablet-plan", 1194, 834);
        // A trip that has not started yet: how it begins.
        s.Close(); await s.OpenAsync(await s.CreateSampleAsync(new LocalDate(2027, 6, 4)));
        await Capture("today-before");
        return true;
    }, CancellationToken.None);
    // Dispatch resumes on the session's own thread, and disposing the session there would wait for itself.
    await Task.Run(session.Dispose).ConfigureAwait(false);
}
finally
{
    try { Directory.Delete(temporary, recursive: true); } catch (IOException) { }
}

public sealed class MobileScreenshotApp
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<MobileApp>()
        .UseSkia().UseHarfBuzz().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}

sealed class FixedClock(Instant now) : IClock { public Instant GetCurrentInstant() => now; }

/// <summary>Screenshots never sign in.</summary>
sealed class NoSecrets : TravelRepo.Providers.ISecretStore
{
    public Task<string?> ReadAsync(string key, CancellationToken ct = default) => Task.FromResult<string?>(null);
    public Task WriteAsync(string key, string value, CancellationToken ct = default) => Task.CompletedTask;
    public Task DeleteAsync(string key, CancellationToken ct = default) => Task.CompletedTask;
}
