using System.Globalization;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Jourfold.Desktop;
using NodaTime;
using TravelRepo.Core;
using TravelRepo.Git;
using TravelRepo.Repository;

var output = Path.GetFullPath(args.SingleOrDefault() ?? "docs/screenshots");
Directory.CreateDirectory(output);
var temporary = Path.Combine(Path.GetTempPath(), "jourfold-screenshots-" + Guid.NewGuid());
Directory.CreateDirectory(temporary);
Environment.SetEnvironmentVariable("JOURFOLD_DATA_HOME", Path.Combine(temporary, "settings"));
if (OperatingSystem.IsLinux())
{
    Environment.SetEnvironmentVariable("XDG_DATA_HOME", Path.Combine(temporary, "data"));
    Environment.SetEnvironmentVariable("DBUS_SESSION_BUS_ADDRESS", "unix:path=" + Path.Combine(temporary, "no-session-bus"));
}
Environment.SetEnvironmentVariable("GIT_CONFIG_GLOBAL", Path.Combine(temporary, "gitconfig"));
Environment.SetEnvironmentVariable("GIT_CONFIG_NOSYSTEM", "1");
File.WriteAllText(Path.Combine(temporary, "gitconfig"), "");
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.GetCultureInfo("en-US");
CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.GetCultureInfo("en-US");

try
{
    var (repository, selection) = await Fixture.CreateAsync(temporary);
    using var session = HeadlessUnitTestSession.StartNew(typeof(ScreenshotApplication));
    await session.Dispatch(async () =>
    {
        var window = new MainWindow { Width = 1360, Height = 960 };
        try
        {
            window.Show();
            await window.Model.OpenAsync(repository.Root);
            window.Model.StartDate = new LocalDate(2027, 5, 14);
            window.Model.InboxOpen = true;
            window.Model.Selected = window.Model.Workspace!.State.Trip.Find(selection);
            foreach (var theme in new[] { "Light", "Dark" })
            {
                window.Model.Store.Set("theme", theme);
                window.Model.Refresh();
                using (window.CaptureRenderedFrame()) { }
                await Task.Delay(100);
                Dispatcher.UIThread.RunJobs();
                var planner = (PlanningView)window.FindControl<ContentControl>("MainContent")!.Content!;
                planner.GetVisualDescendants().OfType<ScrollViewer>().First().Offset = new Vector(0, 8 * 55);
                await Task.Delay(100);
                Dispatcher.UIThread.RunJobs();
                using var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("No rendered frame.");
                var path = Path.Combine(output, $"planning-{theme.ToLowerInvariant()}.png");
                frame.Save(path);
                Console.WriteLine(path);
            }
            window.Model.SetPreference("Theme", "Light");
            var settings = window.Model.SettingsCommand.ExecuteAsync(null);
            await CaptureDialog("settings.png", "Close"); await settings;
            var schedule = window.Model.Interaction.ScheduleAsync(window.Model.Selected!.Data["time"] as JsonObject, window.Model.StartDate, "Europe/Berlin");
            await CaptureDialog("date-time.png", "Cancel"); await schedule;
            async Task CaptureDialog(string name, string close)
            {
                using (window.CaptureRenderedFrame()) { }
                await Task.Delay(120); Dispatcher.UIThread.RunJobs();
                using var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("No rendered dialog."); frame.Save(Path.Combine(output, name));
                var layer = window.FindControl<Grid>("DialogLayer")!;
                layer.GetVisualDescendants().OfType<Button>().Single(b => b.Content?.ToString() == close).RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            }
        }
        finally
        {
            window.Close();
            (App.Services as IDisposable)?.Dispose();
        }
        return true;
    }, CancellationToken.None);
}
finally
{
    Directory.Delete(temporary, recursive: true);
}

public static class ScreenshotApplication
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}

internal static class Fixture
{
    // Synthetic documentation data. These are ideas, not bookings or travel recommendations.
    public static async Task<(TravelRepository Repository, Guid Selection)> CreateAsync(string temporary)
    {
        var repository = new TravelRepository(Path.Combine(temporary, "trip"), Path.Combine(temporary, "recovery"));
        var manifest = Entity.CreateTrip("A weekend in Aachen", "en", "Europe/Berlin");
        await repository.InitializeAsync(manifest);
        var people = new[] { Entity.Create("person", "Alex"), Entity.Create("person", "Carla") };
        manifest.Data["participants"] = new JsonArray(people.Select(p => JsonValue.Create(p.Id.ToString())).ToArray());
        var edits = new List<EntityEdit> { new(manifest.Id, manifest) };
        edits.AddRange(people.Select(p => new EntityEdit(p.Id, p)));
        Guid selected = default;
        foreach (var (title, day, start, end) in new[]
        {
            ("Arrive in Aachen", 14, "09:00", "10:30"),
            ("Cathedral and old town", 14, "11:00", "13:00"),
            ("Coffee and Printen", 14, "14:00", "15:30"),
            ("Explore the neighbourhood", 14, "16:00", "17:30"),
            ("Breakfast together", 15, "09:00", "10:30"),
            ("Centre Charlemagne", 15, "11:00", "13:00"),
            ("Picnic in the park", 15, "14:00", "15:30"),
            ("Time to wander", 15, "16:00", "17:30"),
            ("A slow Sunday brunch", 16, "09:30", "11:00"),
            ("Walk on the Lousberg", 16, "11:30", "13:30"),
            ("Train home", 16, "15:00", "17:00")
        })
        {
            var item = Entity.Create("schedule_item", title);
            item.Data["status"] = "planned";
            item.Data["time"] = new JsonObject
            {
                ["precision"] = "exact",
                ["start"] = new ZonedTime($"2027-05-{day}T{start}:00", "Europe/Berlin").ToJson(),
                ["end"] = new ZonedTime($"2027-05-{day}T{end}:00", "Europe/Berlin").ToJson()
            };
            item.Data["participants"] = new JsonObject
            {
                ["inherit"] = false,
                ["values"] = new JsonArray(people.Select(p => JsonValue.Create(p.Id.ToString())).ToArray())
            };
            edits.Add(new(item.Id, item));
            if (title == "Cathedral and old town") selected = item.Id;
        }
        foreach (var title in new[] { "Thermal baths?", "Find a dinner spot" })
        {
            var item = Entity.Create("schedule_item", title);
            edits.Add(new(item.Id, item));
        }
        await repository.ApplyAsync(await repository.ReadAsync(), edits);
        await new GitRepository(repository, new GitCliBackend()).InitializeAsync("Screenshot fixture", "screenshots@example.invalid");
        return (repository, selected);
    }
}
