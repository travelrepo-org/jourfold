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
    var start = new LocalDate(2027, 5, 14);
    var trip = Path.Combine(temporary, "Spring in Japan");
    await SampleTrip.CreateAsync(trip, new GitCliBackend(), start, "Alex", "alex@example.invalid", Path.Combine(temporary, "recovery"));
    var second = new TravelRepository(Path.Combine(temporary, "Weekend in Aachen"), Path.Combine(temporary, "recovery2"));
    var aachen = Entity.CreateTrip("Weekend in Aachen", "en", "Europe/Berlin"); aachen.Data["dates"] = new System.Text.Json.Nodes.JsonObject { ["start"] = "2027-09-17", ["end"] = "2027-09-19" };
    await second.InitializeAsync(aachen); await new GitRepository(second, new GitCliBackend()).InitializeAsync("Alex", "alex@example.invalid");

    using var session = HeadlessUnitTestSession.StartNew(typeof(ScreenshotApplication));
    await session.Dispatch(async () =>
    {
        var window = new MainWindow { Width = 1440, Height = 900 };
        try
        {
            window.Show();
            var m = window.Model;
            m.Store.Set("identity.name", "Alex"); m.Store.Set("identity.email", "alex@example.invalid");
            // Weather from a synthetic service, as seen the evening before the plan's first day. Nothing is sent anywhere.
            var weatherNow = new ZonedTime("2027-05-14T20:00:00", "Asia/Tokyo").ToInstant();
            m.WeatherFactory = () => new TravelRepo.Providers.Weather.WeatherService([new SyntheticWeather(weatherNow)], null, new TravelRepo.Providers.Weather.WeatherCache(Path.Combine(temporary, "weather")), clock: new FixedClock(weatherNow));
            m.Store.Set("weather.online", "true");
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

            // An outdoor activity with rain in the forecast; the inspector is scrolled to its weather.
            m.Selected = m.Workspace.State.Trip.Entities.Values.First(e => e.Type == "schedule_item" && e.Title == "Kegon Falls"); await Settle(300);
            window.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == m.Strings["Weather"].ToUpperInvariant() && t.FindAncestorOfType<StackPanel>() is { } p && p.GetVisualAncestors().OfType<StackPanel>().Any(a => a.Name == "Inspector")).BringIntoView();
            await Settle(300); await Capture("weather");

            m.Selected = m.Workspace.State.Trip.Entities.Values.First(e => e.Title == "Shinkansen to Kyoto");
            m.View = "List"; await Capture("list");
            m.View = "Map"; m.Selected = null; await Capture("map");
            m.View = "Costs"; await Capture("costs");
            m.View = "Packing"; m.PackingFilter = "all"; m.Selected = null; await Settle(300); await Capture("packing");
            // A booking confirmation e-mail with its ticket, attached to a booking and shown in the document window.
            {
                var booking = m.Workspace.State.Trip.Entities.Values.First(e => e.Type == "booking" && e.Title.Contains("Ryokan", StringComparison.Ordinal));
                var builder = new UglyToad.PdfPig.Writer.PdfDocumentBuilder(); var font = builder.AddStandard14Font(UglyToad.PdfPig.Fonts.Standard14Fonts.Standard14Font.Helvetica); var bold = builder.AddStandard14Font(UglyToad.PdfPig.Fonts.Standard14Fonts.Standard14Font.HelveticaBold);
                var ticket = builder.AddPage(UglyToad.PdfPig.Content.PageSize.A4);
                ticket.AddText("Ryokan in Gion", 26, new UglyToad.PdfPig.Core.PdfPoint(60, 760), bold);
                var lines = new[] { "Reservation RG-20270518", "Guests: Alex, Sam", "Check-in: Tuesday, 18 May 2027, from 15:00", "Check-out: Friday, 21 May 2027, by 10:00", "Room: Garden view, futon for two", "Dinner: kaiseki at 18:30 on the first evening", "", "Please show this confirmation at the front desk." };
                for (var i = 0; i < lines.Length; i++) ticket.AddText(lines[i], 13, new UglyToad.PdfPig.Core.PdfPoint(60, 710 - i * 24), font);
                var message = new MimeKit.MimeMessage { Subject = "Your stay at Ryokan in Gion is confirmed", Date = new DateTimeOffset(2027, 3, 2, 10, 15, 0, TimeSpan.FromHours(9)) };
                message.From.Add(new MimeKit.MailboxAddress("Ryokan in Gion", "stay@ryokan.example")); message.To.Add(new MimeKit.MailboxAddress("Alex", "alex@example.invalid"));
                var body = new MimeKit.BodyBuilder { HtmlBody = "<table width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" style=\"font-family:Georgia,serif;color:#3b2f2a\"><tr><td style=\"background:#7a2e2e;color:#ffffff;padding:18px 24px;font-size:20px\">Ryokan in Gion</td></tr>"
                    + "<tr><td style=\"padding:20px 24px;background:#fbf7f1\"><p>Dear Alex,</p><p>thank you for your reservation. We look forward to welcoming you and Sam.</p>"
                    + "<table cellpadding=\"6\" style=\"border-collapse:collapse;background:#ffffff;border:1px solid #e4d8c8\"><tr><td style=\"color:#8a7a6a\">Arrival</td><td><b>Tuesday, 18 May 2027</b>, check-in from 15:00</td></tr><tr><td style=\"color:#8a7a6a\">Departure</td><td><b>Friday, 21 May 2027</b>, check-out by 10:00</td></tr><tr><td style=\"color:#8a7a6a\">Room</td><td>Garden view, futon for two</td></tr><tr><td style=\"color:#8a7a6a\">Dinner</td><td>Kaiseki at 18:30 on the first evening</td></tr></table>"
                    + "<p><a href=\"https://ryokan.example/manage\" style=\"color:#7a2e2e\">Manage your reservation</a></p><p>Kind regards<br>Ryokan in Gion</p><img src=\"https://ryokan.example/open.gif\"></td></tr></table>" };
                body.Attachments.Add("confirmation-RG-20270518.pdf", builder.Build(), new MimeKit.ContentType("application", "pdf"));
                message.Body = body.ToMessageBody();
                var eml = Path.Combine(temporary, "ryokan-confirmation.eml"); using (var file = File.Create(eml)) message.WriteTo(file);
                await m.AddFilesAsync([eml], booking.Id); await Settle(300); window.FindControl<Panel>("ToastLayer")?.Children.Clear();
                m.View = "Bookings"; m.Selected = m.Workspace.State.Trip.Find(booking.Id); await Settle(300); await Capture("booking-confirmation");
                var document = TripCommands.AttachedDocuments(m.Workspace.State.Trip, m.Workspace.State.Trip.Find(booking.Id)!).First(d => d.Data["media_type"]?.ToString() == "message/rfc822");
                DocumentWindow.Show(window, document); var viewer = DocumentWindow.Current!; viewer.Width = 1100; viewer.Height = 820;
                async Task CaptureViewer(string name) { for (var i = 0; i < 6; i++) { await Task.Delay(100); Dispatcher.UIThread.RunJobs(); } using var frame = viewer.CaptureRenderedFrame() ?? throw new InvalidOperationException("No frame."); var path = Path.Combine(output, name + ".png"); frame.Save(path); Console.WriteLine(path); }
                await CaptureViewer("viewer-mail");
                viewer.GetVisualDescendants().OfType<Button>().First(b => Avalonia.Automation.AutomationProperties.GetName(b)?.StartsWith(m.Strings["OpenFile"] + ":", StringComparison.Ordinal) == true).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await Task.Delay(300); Dispatcher.UIThread.RunJobs();
                var search = viewer.GetVisualDescendants().OfType<TextBox>().First(); search.Text = "15:00";
                search.RaiseEvent(new Avalonia.Input.KeyEventArgs { RoutedEvent = Avalonia.Input.InputElement.KeyDownEvent, Key = Avalonia.Input.Key.Enter });
                await CaptureViewer("viewer-pdf"); viewer.Close(); await Settle();
            }
            if (all)
            {
                m.View = "Bookings"; m.Selected = m.Workspace.State.Trip.Entities.Values.First(e => e.Type == "booking"); await Capture("bookings");
                m.Selected = null;
                m.View = "Tasks"; await Capture("tasks");
                m.View = "Files"; await Capture("files");
                m.View = "People"; await Capture("people");
                m.View = "Places"; await Capture("places");
                m.Selected = m.Workspace.State.Trip.Entities.Values.First(e => e.Type == "place" && e.Data["location"] is not null); await Capture("place-details"); m.Selected = null;
                m.View = "Collections"; await Capture("collections");
                // A second timezone (home) beside the trip timezone.
                m.SetSecondZone("Europe/Berlin"); m.View = "Plan"; await Settle(300); await Capture("planning-second-zone");
                m.View = "List"; await Capture("list-second-zone"); m.SetSecondZone(null);
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
                // The last wizard step with publishing to GitHub chosen (synthetic account name).
                var wizard = m.Interaction.NewTripAsync(new NewTripDefaults(temporary, language, "Europe/Lisbon", "Alex", GitHubConnected: true, GitHubLogin: "alex-travels")); await Settle(300);
                var sheet = window.FindControl<Grid>("DialogLayer")!.Children.Last();
                sheet.GetVisualDescendants().OfType<TextBox>().First(t => t.Tag as string == "title").Text = "Lisbon in October";
                Click(m.Strings["Next"]); await Settle(); Click(m.Strings["Next"]); await Settle();
                sheet.GetVisualDescendants().OfType<RadioButton>().First(r => r.Tag as string == "share-github").IsChecked = true; await Settle(300);
                await Capture("new-trip-sharing"); Click(m.Strings["Cancel"]); await wizard;
                var fresh = new TravelRepository(Path.Combine(temporary, "Lisbon in October"), Path.Combine(temporary, "recovery3"));
                var lisbon = Entity.CreateTrip("Lisbon in October", "en", "Europe/Lisbon"); lisbon.Data["dates"] = new System.Text.Json.Nodes.JsonObject { ["start"] = "2027-10-08", ["end"] = "2027-10-12" };
                await fresh.InitializeAsync(lisbon); await new GitRepository(fresh, new GitCliBackend()).InitializeAsync("Alex", "alex@example.invalid");
                await m.OpenAsync(fresh.Root); m.View = "Plan"; await Capture("empty-plan"); m.CloseTrip(); await Settle();
                await m.OpenAsync(trip);
            }
            m.View = "Plan"; m.ShowDate(start.PlusDays(1));
            await Dialog(() => m.QuickAddCommand.ExecuteAsync(null), "quick-add", m.Strings["Cancel"]);
            if (all) await Dialog(() => m.AddAsync("transport"), "quick-add-transport", m.Strings["Cancel"]);
            // Show a neutral folder instead of the account name of the machine that renders the screenshots.
            m.Store.Set("trips.folder", "/home/alex/Documents/Jourfold");
            await Dialog(() => m.SettingsCommand.ExecuteAsync(null), "settings", m.Strings["Close"]);
            // The About window lists the example plugin as a loaded plugin, read from its build output when available.
            var samples = new DirectoryInfo(AppContext.BaseDirectory); while (samples is not null && !Directory.Exists(Path.Combine(samples.FullName, "samples", "Jourfold.ExamplePlugin"))) samples = samples.Parent;
            if (samples is not null)
            {
                var source = Path.Combine(samples.FullName, "samples", "Jourfold.ExamplePlugin");
                var built = Directory.Exists(Path.Combine(source, "bin")) ? Directory.GetFiles(Path.Combine(source, "bin"), "jourfold.plugin.json", SearchOption.AllDirectories).OrderByDescending(File.GetLastWriteTimeUtc).Select(Path.GetDirectoryName).FirstOrDefault() : null;
                Jourfold.Infrastructure.PluginCatalog.Remember(m.Store, built ?? source);
            }
            await Dialog(() => m.AboutCommand.ExecuteAsync(null), "about", m.Strings["Close"]);
            if (all)
            {
                var about = m.AboutCommand.ExecuteAsync(null); await Settle(400);
                Click(m.Strings["ViewNotices"]); await Settle(400); await Capture("about-notices"); Click(m.Strings["Close"]); await Settle();
                Click(m.Strings["Close"]); await about;
                // Settings with a connected GitHub account. The account and its answers are synthetic; nothing is sent.
                using var gitHubHttp = new HttpClient(new SyntheticGitHub());
                using var connectedStore = new Jourfold.Infrastructure.LocalStore(Path.Combine(temporary, "settings-github"));
                using var connected = new MainViewModel(connectedStore, new GitCliBackend(), new Jourfold.Infrastructure.OsSecretStore(), m.Interaction, () => new TravelRepo.Providers.GitHub.GitHubProvider(gitHubHttp, new SyntheticSecrets(), "screenshots"));
                connected.SetPreference("Language", language);
                var settings = ((Dialogs)m.Interaction).SettingsAsync(connected); await Settle(300);
                Click(connected.Strings["Providers"]); await Settle(800); await Capture("settings-github"); Click(connected.Strings["Close"]); await settings;
                var maps = m.SettingsCommand.ExecuteAsync(null); await Settle(300); Click(m.Strings["MapsAndPlaces"]); await Settle(300);
                window.FindControl<Grid>("DialogLayer")!.GetVisualDescendants().OfType<Expander>().First().IsExpanded = true; await Settle(300);
                await Capture("settings-maps"); Click(m.Strings["Close"]); await maps;
                var weatherSettings = m.SettingsCommand.ExecuteAsync(null); await Settle(300); Click(m.Strings["Weather"]); await Settle(300);
                await Capture("settings-weather"); Click(m.Strings["Close"]); await weatherSettings;
                // Deleting a stay offers to delete its place, which nothing else uses. Cancelled, so the trip is unchanged.
                var stay = m.Workspace.State.Trip.Entities.Values.First(e => e.Data["components"]?["accommodation"] is not null);
                await Dialog(() => m.DeleteAsync(stay.Id), "delete-linked", m.Strings["Cancel"]);
                // A Git failure with its explanation and technical details, using a made-up lock file problem.
                var failure = new TravelRepo.Git.GitCommandException("git.locked", "Another Git process is using the trip folder.", TravelRepo.Git.GitFailure.Locked, "git commit -F -", 128, "fatal: Unable to create '/home/alex/Documents/Jourfold/Japan/.git/index.lock': File exists.", []);
                await Dialog(async () =>
                {
                    var problem = new Problem(m.Strings["SomethingWentWrong"], m.Strings.Error(failure) + "\n\n" + m.Strings["problem.git.locked"], MainViewModel.ProblemDetails(failure), [new("lock", m.Strings["FixRemoveLock"], "wrench")]);
                    await m.Interaction.ProblemAsync(problem);
                }, "problem", m.Strings["Close"]);
                // Stays entered night by night, as people did before stays had their own band, and the offer to merge them.
                {
                    var hakone = Entity.Create("place", "Hakone ryokan"); var edits = new List<TravelRepo.Repository.EntityEdit> { new(hakone.Id, hakone) };
                    foreach (var date in new[] { "2027-05-19", "2027-05-20" })
                    {
                        var night = Entity.Create("schedule_item", "Night in Hakone"); night.Data["status"] = date == "2027-05-19" ? "idea" : "confirmed";
                        night.Data["time"] = new System.Text.Json.Nodes.JsonObject { ["precision"] = "day_part", ["date"] = date, ["day_part"] = "night", ["timezone"] = "Asia/Tokyo" };
                        night.Data["components"]!["accommodation"] = new System.Text.Json.Nodes.JsonObject { ["place"] = hakone.Id.ToString() };
                        edits.Add(new(night.Id, night));
                    }
                    await m.Workspace.ApplyAsync(edits.ToArray()); m.View = "Plan"; m.InboxOpen = false; m.Selected = null; m.ShowDate(new NodaTime.LocalDate(2027, 5, 17)); await Settle(300);
                    await Capture("stays-per-night");
                    await Dialog(() => m.MergeStaysCommand.ExecuteAsync(null), "merge-stays", m.Strings["MergeStaysConfirm"]); await Settle(300);
                    m.Selected = m.Workspace.State.Trip.Entities.Values.First(e => e.Title == "Night in Hakone"); await Capture("stays-merged");
                }
                // Accommodation details and the reminders, seen a few days before the trip with a fixed clock.
                {
                    var hotel = m.Workspace.State.Trip.Entities.Values.First(e => e.Title == "Hotel Gracery Shinjuku" && Stays.IsStay(e));
                    var details = hotel.Copy(); var stayData = (System.Text.Json.Nodes.JsonObject)details.Data["components"]!["accommodation"]!;
                    stayData["check_in_window"] = new System.Text.Json.Nodes.JsonObject { ["from"] = "15:00", ["until"] = "23:00" }; stayData["check_out_window"] = new System.Text.Json.Nodes.JsonObject { ["until"] = "11:00" };
                    stayData["access"] = "reception"; stayData["breakfast"] = "available";
                    stayData["cancellation"] = new System.Text.Json.Nodes.JsonObject { ["free_until"] = new ZonedTime("2027-05-12T23:59:00", "Asia/Tokyo").ToJson(), ["note"] = "Free until three days before arrival" };
                    m.DesktopNotifier = null; await m.Workspace.EditAsync(details); m.Clock = () => new ZonedTime("2027-05-11T09:00:00", "Asia/Tokyo").ToInstant(); await m.UpdateRemindersAsync();
                    m.View = "Plan"; m.ShowDate(new NodaTime.LocalDate(2027, 5, 15)); m.Selected = m.Workspace.State.Trip.Find(hotel.Id); await Settle(400);
                    // A taller window shows the whole accommodation section without scrolling.
                    window.FindControl<Panel>("ToastLayer")?.Children.Clear(); window.Height = 1500; await Settle(400); await Capture("stay-details"); window.Height = 900; await Settle(300);
                    var bellName = m.UrgentReminders > 0 ? string.Format(m.Strings["RemindersUrgent"], m.Reminders.Count, m.UrgentReminders) : string.Format(m.Strings["RemindersCount"], m.Reminders.Count);
                    var bell = window.GetVisualDescendants().OfType<Button>().First(b => Avalonia.Automation.AutomationProperties.GetName(b) == bellName);
                    bell.Flyout!.ShowAt(bell);
                    await Settle(400); await Capture("reminders"); bell.Flyout.Hide(); await Settle(200);
                }
                // "What's new" after an update from 0.3.1, and the full version history.
                await Dialog(() => m.Interaction.ReleaseNotesAsync(ReleaseNotes.Since("0.3.1", AppVersion.Version, true), highlightsOnly: true), "whats-new", m.Strings["LetsGo"]);
                await Dialog(() => m.Interaction.ReleaseNotesAsync(ReleaseNotes.All, highlightsOnly: false), "version-history", m.Strings["Close"]);
                // The assistant configuration with neutral paths instead of this machine's program and trip folders.
                var assistant = m.ConnectAssistantCommand.ExecuteAsync(null); await Settle(300);
                window.FindControl<Grid>("DialogLayer")!.GetVisualDescendants().OfType<TextBox>().First(t => t.Tag as string == "assistant-configuration").Text =
                    MainViewModel.AssistantConfiguration("/home/alex/.local/share/jourfold-app/Jourfold.Desktop", "/home/alex/Documents/Jourfold/" + m.TripTitle, m.TripTitle, false);
                await Settle(200); await Capture("ai-assistant"); Click(m.Strings["Close"]); await assistant;
            }
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

/// <summary>A signed-in GitHub account for screenshots, answered locally.</summary>
sealed class SyntheticGitHub : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent("{\"login\":\"alex-travels\",\"name\":\"Alex Example\",\"html_url\":\"https://github.com/alex-travels\"}") });
}

sealed class SyntheticSecrets : TravelRepo.Providers.ISecretStore
{
    public Task<string?> ReadAsync(string key, CancellationToken ct = default) => Task.FromResult<string?>("synthetic-token");
    public Task WriteAsync(string key, string value, CancellationToken ct = default) => Task.CompletedTask;
    public Task DeleteAsync(string key, CancellationToken ct = default) => Task.CompletedTask;
}

sealed class FixedClock(Instant now) : IClock { public Instant GetCurrentInstant() => now; }

/// <summary>
/// Invented forecasts for the sample trip: mostly fine in Tokyo and Kyoto, clouds on the second day and rain in Nikko on
/// the third day from noon, so the Kegon Falls visit gets a warning.
/// </summary>
sealed class SyntheticWeather(Instant now) : TravelRepo.Providers.Weather.IWeatherProvider
{
    public string Id => "open-meteo";
    public string Name => "Open-Meteo";
    public TravelRepo.Providers.Weather.WeatherAttribution Attribution { get; } = new("Weather data by Open-Meteo.com", "https://open-meteo.com/", "CC BY 4.0");
    public bool RequiresKey => false;
    public int HorizonDays => 16;
    public bool Covers(TravelRepo.Providers.Weather.GeoPoint point) => true;
    public Task<IReadOnlyList<TravelRepo.Providers.Weather.WeatherForecast?>> ForecastAsync(IReadOnlyList<TravelRepo.Providers.Weather.GeoPoint> points, CancellationToken ct = default)
    {
        var zone = DateTimeZoneProviders.Tzdb["Asia/Tokyo"]; var first = zone.AtStartOfDay(new LocalDate(2027, 5, 14)).ToInstant();
        TravelRepo.Providers.Weather.WeatherHour Hour(TravelRepo.Providers.Weather.GeoPoint p, int index)
        {
            var at = first + Duration.FromHours(index); var local = at.InZone(zone); var day = local.Date.Day; var hour = local.Hour;
            var mountains = p.Latitude > 36.5; var kyoto = p.Latitude < 35.2;
            var warmth = Math.Sin((hour - 9) / 24.0 * 2 * Math.PI) * 4;
            var temperature = Math.Round((mountains ? 13 : kyoto ? 20 : 19) + warmth + (day % 3) * 0.6, 1);
            var rain = mountains && day == 17 && hour is >= 12 and < 17;
            var condition = rain ? TravelRepo.Providers.Weather.WeatherCondition.Rain : day == 16 || mountains ? TravelRepo.Providers.Weather.WeatherCondition.Cloudy : hour is >= 12 and < 18 && day % 2 == 1 ? TravelRepo.Providers.Weather.WeatherCondition.PartlyCloudy : TravelRepo.Providers.Weather.WeatherCondition.Clear;
            return new(at, 1, temperature, rain ? 80 : day == 16 ? 30 : 5, rain ? 2.4 : 0, 12, rain ? 38 : 22, condition);
        }
        return Task.FromResult<IReadOnlyList<TravelRepo.Providers.Weather.WeatherForecast?>>(points.Select(p => (TravelRepo.Providers.Weather.WeatherForecast?)new TravelRepo.Providers.Weather.WeatherForecast(Id, p, now, now + Duration.FromDays(30), Enumerable.Range(0, 16 * 24).Select(i => Hour(p, i)).ToArray())).ToArray());
    }
}
