using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Jourfold.Application;
using Jourfold.Desktop;
using NodaTime;
using TravelRepo.Core;
using TravelRepo.Git;
using TravelRepo.Repository;

// Renders the real application with synthetic local data. Usage:
//   dotnet run --project eng/Screenshots -- <output-directory> [--all] [--lang de]
var options = args.Where(a => a.StartsWith("--", StringComparison.Ordinal)).ToArray();
var output = Path.GetFullPath(args.FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal) && !Array.Exists(options, o => o == a) && a is not ("de" or "en")) ?? "docs/screenshots");
var all = options.Contains("--all");
var language = args.SkipWhile(a => a != "--lang").Skip(1).FirstOrDefault() ?? "en";
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
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.GetCultureInfo(language == "de" ? "de-DE" : "en-GB");
CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.DefaultThreadCurrentCulture;

try
{
    // Synthetic sample data: real place names, invented times, bookings and prices.
    Console.Error.WriteLine("creating sample");
    var start = new LocalDate(2027, 5, 14);
    var trip = Path.Combine(temporary, "Spring in Japan");
    await SampleTrip.CreateAsync(trip, new GitCliBackend(), start, "Alex", "alex@example.invalid", Path.Combine(temporary, "recovery"));
    var second = new TravelRepository(Path.Combine(temporary, "Weekend in Aachen"), Path.Combine(temporary, "recovery2"));
    var aachen = Entity.CreateTrip("Weekend in Aachen", "en", "Europe/Berlin"); aachen.Data["dates"] = new System.Text.Json.Nodes.JsonObject { ["start"] = "2027-09-17", ["end"] = "2027-09-19" };
    await second.InitializeAsync(aachen); await new GitRepository(second, new GitCliBackend()).InitializeAsync("Alex", "alex@example.invalid");

    Console.Error.WriteLine("starting session");
    using var session = HeadlessUnitTestSession.StartNew(typeof(ScreenshotApplication));
    await session.Dispatch(async () =>
    {
        Console.Error.WriteLine("dispatch");
        MainWindow window;
        try { window = new MainWindow { Width = 1440, Height = 900 }; }
        catch (Exception ex) { Console.Error.WriteLine(ex); throw; }
        try
        {
            Console.Error.WriteLine("show"); window.Show(); Console.Error.WriteLine("shown");
            var m = window.Model;
            m.Store.Set("identity.name", "Alex"); m.Store.Set("identity.email", "alex@example.invalid");
            if (language == "de") m.SetPreference("Language", "de");
            async Task Settle(int ms = 150) { using (window.CaptureRenderedFrame()) { } for (var i = 0; i < 4; i++) { await Task.Delay(ms / 4); Dispatcher.UIThread.RunJobs(); } }
            async Task Capture(string name) { await Settle(); using var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("No frame."); var path = Path.Combine(output, name + ".png"); frame.Save(path); Console.WriteLine(path); }
            void Click(string label)
            {
                var button = window.FindControl<Grid>("DialogLayer")!.GetVisualDescendants().OfType<Button>().LastOrDefault(b => Avalonia.Automation.AutomationProperties.GetName(b) == label || b.Content as string == label)
                    ?? throw new InvalidOperationException("Button not found: " + label);
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }
            async Task Dialog(Func<Task> open, string name, string close)
            {
                var layer = window.FindControl<Grid>("DialogLayer")!; var before = layer.Children.Count;
                var task = open();
                for (var i = 0; i < 60 && layer.Children.Count <= before; i++) await Settle(100);
                await Capture(name); Click(close); await Settle(); await task;
            }
            void Theme(string theme) { m.SetPreference("Theme", theme); }

            Theme("Light");
            if (all) await Capture("library-empty");
            m.Store.Remember(second.Root, (await second.ReadAsync()).Trip, "local");
            await m.OpenAsync(trip); await Settle();
            m.CloseTrip(); await Settle(300);
            await Capture("library");

            await m.OpenAsync(trip);
            m.View = "Plan"; m.ShowDate(start.PlusDays(1)); m.InboxOpen = true;
            m.Selected = m.Workspace!.State.Trip.Entities.Values.First(e => e.Type == "schedule_item" && e.Title == "teamLab Planets");
            await Settle(300);
            await Capture("planning-light");
            Theme("Dark"); await Settle(300); await Capture("planning-dark"); Theme("Light");
            m.InboxOpen = false;

            m.Selected = m.Workspace.State.Trip.Entities.Values.First(e => e.Title == "Shinkansen to Kyoto");
            m.View = "List"; await Capture("list");
            m.View = "Map"; m.Selected = null; await Capture("map");
            m.View = "Costs"; await Capture("costs");
            if (all)
            {
                m.View = "Bookings"; m.Selected = m.Workspace.State.Trip.Entities.Values.First(e => e.Type == "booking"); await Capture("bookings");
                m.Selected = null;
                m.View = "Tasks"; await Capture("tasks");
                m.View = "Files"; await Capture("files");
                m.View = "People"; await Capture("people");
                m.View = "Places"; await Capture("places");
                m.View = "Collections"; await Capture("collections");
                m.View = "Plan"; m.Lanes = true; m.RaiseContentChanged(); await Capture("lanes"); m.Lanes = false;
                Theme("Dark"); m.View = "Map"; await Capture("map-dark"); m.View = "Costs"; await Capture("costs-dark"); Theme("Light");
                m.View = "Plan"; m.ShowDate(start.PlusDays(3));
                m.Selected = m.Workspace.State.Trip.Entities.Values.First(e => e.Title == "Day trip to Nikko"); await Capture("nested");
                m.Selected = m.Workspace.State.Trip.Manifest; await Capture("trip-details");
                await Dialog(() => m.SearchCommand.ExecuteAsync(null), "palette", m.Strings["Close"]);
                await Dialog(() => m.Interaction.ScheduleAsync(m.Workspace.State.Trip.Entities.Values.First(e => e.Title == "Flight to Tokyo").Data["time"] as System.Text.Json.Nodes.JsonObject, start, "Asia/Tokyo"), "date-time", m.Strings["Cancel"]);
                await Dialog(() => m.ShareCommand.ExecuteAsync(null), "share", m.Strings["Close"]);
                var item = m.Workspace.State.Trip.Entities.Values.First(e => e.Title == "Explore Shinjuku").Copy(); item.Data["status"] = "confirmed"; await m.Workspace.EditAsync(item);
                await Dialog(() => m.CreateVersionCommand.ExecuteAsync(null), "create-version", m.Strings["Cancel"]);
                // Variants: an alternative plan that changes the same activity, to show comparison and conflict resolution.
                var git = m.Workspace.Git; await git.CreateVersionAsync("Confirm Shinjuku walk");
                await git.CreateVariantAsync("variants/slower-kyoto", "Slower Kyoto"); await m.Workspace.ReloadAsync();
                var tea = m.Workspace.State.Trip.Entities.Values.First(e => e.Title == "Tea ceremony").Copy(); tea.Data["title"] = "Tea ceremony in Uji"; await m.Workspace.EditAsync(tea);
                var extra = Entity.Create("schedule_item", "Philosopher's Path"); extra.Data["status"] = "planned"; extra.Data["category"] = "nature";
                extra.Data["time"] = new System.Text.Json.Nodes.JsonObject { ["precision"] = "exact", ["start"] = new ZonedTime(start.PlusDays(5).ToString("yyyy-MM-dd", null) + "T10:00:00", "Asia/Tokyo").ToJson(), ["end"] = new ZonedTime(start.PlusDays(5).ToString("yyyy-MM-dd", null) + "T11:30:00", "Asia/Tokyo").ToJson() };
                await m.Workspace.EditAsync(extra); await git.CreateVersionAsync("Slower days in Kyoto");
                await git.SwitchAsync("main"); await m.Workspace.ReloadAsync();
                tea = m.Workspace.State.Trip.Entities.Values.First(e => e.Title == "Tea ceremony").Copy(); tea.Data["title"] = "Tea ceremony at the ryokan"; await m.Workspace.EditAsync(tea); await git.CreateVersionAsync("Move tea ceremony to the ryokan");
                m.Refresh(); m.View = "Variants"; await Settle(800); await Capture("variants");
                var slower = (await git.VariantsAsync()).First(v => v.Branch == "variants/slower-kyoto");
                await Dialog(() => m.VariantActionAsync(slower, "Compare"), "compare", m.Strings["Close"]);
                await Dialog(() => m.VariantActionAsync(slower, "Merge"), "merge-conflicts", m.Strings["Cancel"]);
                m.View = "History"; await Settle(800); await Capture("history");
                Jourfold.Infrastructure.PdfExport.Write(m.Workspace.State.Trip, Path.Combine(output, "itinerary.pdf"), Path.Combine(AppContext.BaseDirectory, "fonts", "PlusJakartaSans-Regular.ttf"), m.ExportLabels(), CultureInfo.CurrentCulture);
                File.WriteAllText(Path.Combine(output, "itinerary.html"), TripExport.Html(m.Workspace.State.Trip, m.ExportLabels(), CultureInfo.CurrentCulture));
                m.Selected = null; m.CloseTrip(); await Settle();
                await Dialog(() => m.NewTripCommand.ExecuteAsync(null), "new-trip", m.Strings["Cancel"]);
                await m.OpenAsync(trip);
            }
            m.View = "Plan"; m.ShowDate(start.PlusDays(1));
            await Dialog(() => m.QuickAddCommand.ExecuteAsync(null), "quick-add", m.Strings["Cancel"]);
            await Dialog(() => m.SettingsCommand.ExecuteAsync(null), "settings", m.Strings["Close"]);
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); throw; }
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
    try { Directory.Delete(temporary, recursive: true); } catch (IOException) { }
}

public static class ScreenshotApplication
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
