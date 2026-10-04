using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Jourfold.Application;
using Jourfold.Desktop;
using Jourfold.Infrastructure;
using NodaTime;
using TravelRepo.Core;
using TravelRepo.Git;
using TravelRepo.Repository;
using Xunit;
namespace Jourfold.Tests;

public sealed class UsabilityTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "jourfold-usability-" + Guid.NewGuid());
    public void Dispose() => TestFiles.Delete(root);
    private static MainWindow Window()
    {
        var w = new MainWindow(); w.Model.SetPreference("Language", "en"); w.Model.SetPreference("TextSize", "Normal"); w.Model.SetPreference("HighContrast", "false");
        w.Model.SetPreference("OpenView", "Plan"); w.Model.SetPreference("Density", "Comfortable"); w.Model.SetPreference("Theme", "Light"); w.Model.SetLanes(false);
        w.Show(); Probe.Layout(); return w;
    }
    private async Task Open(MainWindow w)
    {
        var repository = new TravelRepository(Path.Combine(root, "trip")); await repository.InitializeAsync(Entity.CreateTrip("UI test", "en", "Europe/Berlin"));
        await new GitRepository(repository, new GitCliBackend()).InitializeAsync("Test", "test@example.invalid"); await w.Model.OpenAsync(repository.Root);
        w.Model.Navigate("Plan"); w.Model.InboxOpen = false; w.Model.ShowDate(new LocalDate(2027, 5, 14)); Probe.Layout();
    }
    private static Control Sheet(MainWindow w) => Probe.Sheet(w);

    [AvaloniaFact]
    public async Task WizardTakesKeyboardFocusAndEscapeCancelsWithoutANativeWindow()
    {
        var w = Window(); try
        {
            var task = w.Model.Interaction.NewTripAsync(new NewTripDefaults(root, "en", "Europe/Berlin", null)); Probe.Layout();
            Assert.Equal("title", (w.FocusManager!.GetFocusedElement() as Control)?.Tag);
            w.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None); w.KeyReleaseQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            Assert.Null(await task.WaitAsync(TimeSpan.FromSeconds(3))); Assert.Empty(w.FindControl<Grid>("DialogLayer")!.Children);
            Assert.True(w.FindControl<LayoutTransformControl>("RootLayout")!.IsEnabled);
        }
        finally { w.Close(); }
    }

    [AvaloniaFact]
    public async Task QuickAddSwitchesTypesAndTogglesWithoutLosingTheTitle()
    {
        var w = Window(); try
        {
            await Open(w);
            var task = w.Model.QuickAddCommand.ExecuteAsync(null); Probe.Layout();
            var s = w.Model.Strings;
            // Type first, then fields: every type and every toggle rebuilds the form and must keep what was typed.
            foreach (var kind in new[] { "food", "transport", "accommodation", "booking", "task", "note", "place", "person", "expense", "collection", "sightseeing", "activity" })
            {
                Probe.Click(Sheet(w), s["quick." + kind]); Probe.Layout();
                if (kind == "food") Probe.Tagged<TextBox>(Sheet(w), "title").Text = "Dinner with friends";
                Assert.Equal("Dinner with friends", Probe.Tagged<TextBox>(Sheet(w), "title").Text);
            }
            CheckBox Toggle(string label) => Probe.All<CheckBox>(Sheet(w)).Single(c => c.Content as string == label);
            Toggle(s["AtATime"]).IsChecked = false; Probe.Layout(); Toggle(s["AtATime"]).IsChecked = true; Probe.Layout();
            Toggle(s["JustAnIdea"]).IsChecked = true; Probe.Layout();
            Assert.False(Probe.Tagged<TimePicker>(Sheet(w), "startTime").IsEnabled);
            Probe.Click(Sheet(w), s["Add"]); await task.WaitAsync(TimeSpan.FromSeconds(5));
            var added = w.Model.Workspace!.State.Trip.Entities.Values.Single(e => e.Title == "Dinner with friends");
            Assert.Equal("schedule_item", added.Type); Assert.Null(added.Data["time"]);
        }
        finally { w.Close(); }
    }

    [AvaloniaFact]
    public async Task WizardOffersToPublishANewTripToGitHub()
    {
        var w = Window(); try
        {
            var task = w.Model.Interaction.NewTripAsync(new NewTripDefaults(root, "en", "Europe/Berlin", "Alex", GitHubConnected: true, GitHubLogin: "alex")); Probe.Layout();
            Probe.Tagged<TextBox>(Sheet(w), "title").Text = "Lisbon in October";
            Probe.Click(Sheet(w), "Next"); Probe.Layout(); Probe.Click(Sheet(w), "Next"); Probe.Layout();
            Assert.True(Probe.Tagged<RadioButton>(Sheet(w), "share-local").IsChecked);
            Assert.False(Probe.Tagged<TextBox>(Sheet(w), "repository").IsEffectivelyVisible);
            Probe.Tagged<RadioButton>(Sheet(w), "share-github").IsChecked = true; Probe.Layout();
            var repository = Probe.Tagged<TextBox>(Sheet(w), "repository");
            Assert.True(repository.IsEffectivelyVisible); Assert.Equal("lisbon-in-october", repository.Text);
            Assert.Contains(Probe.All<TextBlock>(Sheet(w)), t => t.Text == "Creates a private repository in the account @alex.");
            Probe.Click(Sheet(w), "Create trip");
            var draft = await task.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.Equal("lisbon-in-october", draft!.GitHubRepository); Assert.Null(draft.RemoteUrl);
        }
        finally { w.Close(); }
    }

    [AvaloniaFact]
    public async Task WizardNeedsOnlyATitleAndCollectsPeopleAndDates()
    {
        var w = Window(); try
        {
            var task = w.Model.Interaction.NewTripAsync(new NewTripDefaults(root, "en", "Europe/Berlin", "Alex")); Probe.Layout();
            Probe.Click(Sheet(w), "Next"); Probe.Layout(); Assert.False(task.IsCompleted);
            Probe.Tagged<TextBox>(Sheet(w), "title").Text = "Aachen";
            Probe.Tagged<CalendarDatePicker>(Sheet(w), "start").SelectedDate = new DateTime(2027, 5, 14); Probe.Layout();
            Assert.Equal(new DateTime(2027, 5, 17), Probe.Tagged<CalendarDatePicker>(Sheet(w), "end").SelectedDate);
            Probe.Click(Sheet(w), "Next"); Probe.Layout();
            Assert.Equal("Alex", Probe.Tagged<TextBox>(Sheet(w), "me").Text);
            var person = Probe.Tagged<TextBox>(Sheet(w), "person"); person.Text = "Carla"; Probe.Click(Sheet(w), "Add person"); Probe.Layout();
            Probe.Click(Sheet(w), "Next"); Probe.Layout(); Probe.Click(Sheet(w), "Create trip");
            var draft = await task.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.NotNull(draft); Assert.Equal("Aachen", draft.Title); Assert.Equal("2027-05-14", draft.Start); Assert.Equal("2027-05-17", draft.End); Assert.Equal(["Carla"], draft.Participants); Assert.Equal("Alex", draft.MyName);
            Assert.Equal(Path.Combine(root, "Aachen"), draft.Destination);
        }
        finally { w.Close(); }
    }

    [AvaloniaFact]
    public async Task InboxClickSelectsWithoutStartingDragAndBlocksHaveContextActions()
    {
        var w = Window(); try
        {
            await Open(w); var item = Entity.Create("schedule_item", "Idea"); await w.Model.Workspace!.EditAsync(item); w.Model.InboxOpen = true; Probe.Layout();
            var card = Probe.All<Button>(w.FindControl<Border>("InboxHost")!).Single(b => b.Classes.Contains("card"));
            var point = card.TranslatePoint(new Point(25, 12), w)!.Value;
            w.MouseDown(point, MouseButton.Left); w.MouseUp(point, MouseButton.Left); Probe.Layout();
            Assert.Equal(item.Id, w.Model.Selected!.Id); Assert.False(w.Model.Busy); Assert.Empty(w.FindControl<Grid>("DialogLayer")!.Children);
            await w.Model.Workspace.ScheduleAsync(item.Id, new ZonedTime("2027-05-14T10:00:00", "Europe/Berlin"), Duration.FromHours(1)); Probe.Layout();
            var block = Probe.All<Button>(w.FindControl<ContentControl>("MainContent")!).Single(b => Equals(b.Tag, item.Id));
            w.Model.Selected = null; Probe.Layout(); block = Probe.All<Button>(w.FindControl<ContentControl>("MainContent")!).Single(b => Equals(b.Tag, item.Id));
            var center = block.TranslatePoint(new Point(block.Bounds.Width / 2, 12), w)!.Value; w.MouseDown(center, MouseButton.Left); w.MouseUp(center, MouseButton.Left); Probe.Layout();
            Assert.Equal(item.Id, w.Model.Selected?.Id); Assert.Equal("10:00", ZonedTime.From(w.Model.Workspace.State.Trip.Find(item.Id)!.Data["time"]!["start"]!).Local[11..16]);
            block = Probe.All<Button>(w.FindControl<ContentControl>("MainContent")!).Single(b => Equals(b.Tag, item.Id));
            var actions = block.ContextMenu!.Items.OfType<MenuItem>().Select(i => i.Header as string).ToArray();
            Assert.Contains("Delete", actions); Assert.Contains("Change time", actions); Assert.Contains("Duplicate", actions);
            Assert.NotNull(ToolTip.GetTip(block)); Assert.Contains("Idea", Avalonia.Automation.AutomationProperties.GetName(block));
        }
        finally { w.Close(); }
    }

    [AvaloniaFact]
    public async Task DeleteIsImmediateAndUndoable()
    {
        var w = Window(); try
        {
            await Open(w); var item = Entity.Create("task", "Pack"); await w.Model.Workspace!.EditAsync(item);
            await w.Model.DeleteAsync(item.Id); Assert.Null(w.Model.Workspace.State.Trip.Find(item.Id));
            Assert.Empty(w.FindControl<Grid>("DialogLayer")!.Children); Assert.NotEmpty(w.FindControl<StackPanel>("ToastLayer")!.Children);
            await w.Model.UndoCommand.ExecuteAsync(null); Assert.NotNull(w.Model.Workspace.State.Trip.Find(item.Id));
        }
        finally { w.Close(); }
    }

    [AvaloniaFact]
    public async Task ScheduleEditorValidatesRangeAndStoresCanonicalValues()
    {
        var w = Window(); try
        {
            var task = w.Model.Interaction.ScheduleAsync(null, new LocalDate(2027, 5, 14), "Europe/Berlin"); Probe.Layout();
            Probe.Tagged<TimePicker>(Sheet(w), "startTime").SelectedTime = TimeSpan.FromHours(23); Probe.Tagged<TimePicker>(Sheet(w), "endTime").SelectedTime = TimeSpan.FromHours(1);
            Probe.Click(Sheet(w), "Save"); await Task.Delay(30); Probe.Layout(); Assert.False(task.IsCompleted);
            Probe.Tagged<CalendarDatePicker>(Sheet(w), "endDate").SelectedDate = new DateTime(2027, 5, 15); Probe.Click(Sheet(w), "Save");
            var time = await task.WaitAsync(TimeSpan.FromSeconds(3)); Assert.NotNull(time);
            Assert.Equal("2027-05-14T23:00:00", time["start"]!["local"]!.ToString()); Assert.Equal(Duration.FromHours(2), ZonedTime.From(time["end"]!).ToInstant() - ZonedTime.From(time["start"]!).ToInstant());
        }
        finally { w.Close(); }
    }

    [AvaloniaFact]
    public async Task DurationPresetsSetTheEndFromTheStart()
    {
        var w = Window(); try
        {
            var task = w.Model.Interaction.ScheduleAsync(null, new LocalDate(2027, 5, 14), "Europe/Berlin"); Probe.Layout();
            Probe.Tagged<TimePicker>(Sheet(w), "startTime").SelectedTime = TimeSpan.FromHours(14);
            Probe.Click(Sheet(w), Formats.Duration(Duration.FromMinutes(90), w.Model.Strings)); Probe.Click(Sheet(w), "Save");
            var time = await task.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.Equal("2027-05-14T15:30:00", time!["end"]!["local"]!.ToString());
        }
        finally { w.Close(); }
    }

    [AvaloniaFact]
    public async Task DayPartEditorPreservesItsOwnTimezone()
    {
        var w = Window(); try
        {
            var initial = new JsonObject { ["precision"] = "day_part", ["date"] = "2027-05-14", ["day_part"] = "morning", ["timezone"] = "Asia/Tokyo" };
            var task = w.Model.Interaction.ScheduleAsync(initial, new LocalDate(2027, 5, 14), "Europe/Berlin"); Probe.Layout();
            var zone = Probe.Tagged<SearchChoice>(Sheet(w), "dayZone"); Assert.True(zone.IsEffectivelyVisible); Assert.Equal("Asia/Tokyo", zone.Value);
            Probe.Click(Sheet(w), "Evening"); Probe.Click(Sheet(w), "Save"); var result = await task.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.Equal("Asia/Tokyo", result!["timezone"]!.ToString()); Assert.Equal("evening", result["day_part"]!.ToString());
        }
        finally { w.Close(); }
    }

    [AvaloniaFact]
    public async Task QuickAddTakesDepartureAndArrivalInLocalTimeOfEachPlace()
    {
        var w = Window(); try
        {
            await Open(w);
            var frankfurt = Entity.Create("place", "Frankfurt Airport"); frankfurt.Data["timezone"] = "Europe/Berlin";
            var haneda = Entity.Create("place", "Haneda Airport"); haneda.Data["timezone"] = "Asia/Tokyo";
            await w.Model.Workspace!.ApplyAsync([new(frankfurt.Id, frankfurt), new(haneda.Id, haneda)]);
            var task = w.Model.AddAsync("transport"); Probe.Layout(); var sheet = Sheet(w); var s = w.Model.Strings;
            Probe.Tagged<TextBox>(sheet, "title").Text = "Flight to Tokyo";
            var places = Probe.All<AutoCompleteBox>(sheet).ToArray(); places[0].Text = "Frankfurt Airport"; places[1].Text = "Haneda Airport"; Probe.Layout();
            SearchChoice Zone(string tag) => Probe.All<SearchChoice>(Sheet(w)).Single(z => z.Tag as string == tag);
            // The timezones follow the chosen places.
            await Probe.Until(() => Zone("departureZone").Value == "Europe/Berlin" && Zone("arrivalZone").Value == "Asia/Tokyo");
            Probe.All<CalendarDatePicker>(Sheet(w)).First().SelectedDate = new DateTime(2027, 5, 12);
            Probe.Tagged<TimePicker>(Sheet(w), "startTime").SelectedTime = new TimeSpan(13, 20, 0);
            Probe.Tagged<CalendarDatePicker>(Sheet(w), "arrivalDate").SelectedDate = new DateTime(2027, 5, 12);
            Probe.Tagged<TimePicker>(Sheet(w), "arrivalTime").SelectedTime = new TimeSpan(8, 35, 0); Probe.Layout();
            Assert.Contains(Probe.All<TextBlock>(Sheet(w)), t => t.Text == s["ArrivalBeforeDeparture"]);
            Probe.Click(Sheet(w), s["Add"]); Probe.Layout(); Assert.False(task.IsCompleted);

            Probe.Tagged<CalendarDatePicker>(Sheet(w), "arrivalDate").SelectedDate = new DateTime(2027, 5, 13); Probe.Layout();
            Assert.Contains(Probe.All<TextBlock>(Sheet(w)), t => t.Text == string.Format(s["TravelTime"], "12 h 15 min"));
            Probe.Click(Sheet(w), s["Add"]); await task.WaitAsync(TimeSpan.FromSeconds(5));
            var flight = w.Model.Workspace.State.Trip.Entities.Values.Single(e => e.Title == "Flight to Tokyo");
            Assert.Equal("2027-05-12T13:20:00", flight.Data["time"]!["start"]!["local"]!.ToString()); Assert.Equal("Europe/Berlin", flight.Data["time"]!["start"]!["timezone"]!.ToString());
            Assert.Equal("2027-05-13T08:35:00", flight.Data["time"]!["end"]!["local"]!.ToString()); Assert.Equal("Asia/Tokyo", flight.Data["time"]!["end"]!["timezone"]!.ToString());
        }
        finally { w.Close(); }
    }

    [AvaloniaFact]
    public async Task SecondTimezoneShowsInTimetableAndList()
    {
        var w = Window(); try
        {
            await Open(w); var m = w.Model;
            var visit = Entity.Create("schedule_item", "Cathedral"); await m.Workspace!.EditAsync(visit);
            await m.Workspace.ScheduleAsync(visit.Id, new ZonedTime("2027-05-14T10:00:00", "Europe/Berlin"), Duration.FromHours(1));
            var flight = Entity.Create("schedule_item", "Flight"); flight.Data["time"] = new JsonObject { ["precision"] = "exact", ["start"] = new ZonedTime("2027-05-15T13:20:00", "Europe/Berlin").ToJson(), ["end"] = new ZonedTime("2027-05-16T08:35:00", "Asia/Tokyo").ToJson() };
            await m.Workspace.EditAsync(flight); m.Refresh();
            Assert.Null(m.SecondZone); Assert.Equal("Asia/Tokyo", m.SecondZoneSuggestions()[0]);

            m.SetSecondZone("Asia/Tokyo"); m.ShowDate(new LocalDate(2027, 5, 14)); Probe.Layout();
            var texts = Probe.All<TextBlock>(w.FindControl<ContentControl>("MainContent")!).Select(t => t.Text).ToList();
            Assert.Contains("Tokyo", texts); Assert.Contains("17:00", texts); // 10:00 in Berlin (CEST) is 17:00 in Tokyo.
            m.View = "List"; Probe.Layout();
            Assert.Contains(Probe.All<TextBlock>(w.FindControl<ContentControl>("MainContent")!), t => t.Text == "Tokyo 17:00");

            // The choice belongs to this trip on this computer and can be switched off again.
            var root = m.Workspace.Repository.Root; m.CloseTrip(); Assert.Null(m.SecondZone); await m.OpenAsync(root); Assert.Equal("Asia/Tokyo", m.SecondZone);
            m.SetSecondZone(null); Assert.Null(m.SecondZone);
        }
        finally { w.Close(); }
    }

    [AvaloniaFact]
    public async Task TimetableArrowsMoveByOneDayOrByTheVisibleDays()
    {
        var w = Window(); try
        {
            await Open(w); var m = w.Model; var s = m.Strings; var step = Math.Max(1, m.VisibleDays);
            Probe.Click(w, s["NextDay"]); Probe.Layout(); Assert.Equal(new LocalDate(2027, 5, 15), m.StartDate);
            Probe.Click(w, s["PreviousDay"]); Probe.Layout(); Assert.Equal(new LocalDate(2027, 5, 14), m.StartDate);
            Probe.Click(w, s["NextDays"]); Probe.Layout(); Assert.Equal(new LocalDate(2027, 5, 14).PlusDays(step), m.StartDate);
            Probe.Click(w, s["PreviousDays"]); Probe.Layout(); Assert.Equal(new LocalDate(2027, 5, 14), m.StartDate);
        }
        finally { w.Close(); }
    }

    [AvaloniaFact]
    public async Task NewTimeKeepsTheTimetableInPlaceWhileTheItemStaysVisible()
    {
        var w = Window(); try
        {
            await Open(w); var m = w.Model;
            var visit = Entity.Create("schedule_item", "Cathedral"); await m.Workspace!.EditAsync(visit);
            async Task MoveTo(string local)
            {
                await m.Workspace!.ScheduleAsync(visit.Id, new ZonedTime(local, "Europe/Berlin"), Duration.FromHours(1));
                m.KeepInView(visit.Id); m.Refresh(); Probe.Layout(); await Task.Delay(50); Probe.Layout();
            }
            ScrollViewer Scroll() => Probe.All<ScrollViewer>(w.FindControl<ContentControl>("MainContent")!).First();
            await MoveTo("2027-05-15T10:00:00");
            var offset = Scroll().Offset.Y; Assert.True(offset > 0);

            // Still visible: neither the days nor the scroll position change.
            await MoveTo("2027-05-15T11:00:00");
            Assert.Equal(new LocalDate(2027, 5, 14), m.StartDate); Assert.Equal(offset, Scroll().Offset.Y);

            // Later in the evening: scrolled down just to the item, same days.
            await MoveTo("2027-05-15T22:00:00");
            Assert.Equal(new LocalDate(2027, 5, 14), m.StartDate); Assert.True(Scroll().Offset.Y > offset);
            var block = Probe.All<Button>(Scroll()).First(b => Equals(b.Tag, visit.Id));
            var top = Canvas.GetTop(block) - Scroll().Offset.Y; Assert.InRange(top, 0, Scroll().Viewport.Height - block.Bounds.Height);

            // Another week: the timetable moves to that day.
            await MoveTo("2027-06-10T10:00:00");
            Assert.Equal(new LocalDate(2027, 6, 10), m.StartDate);
        }
        finally { w.Close(); }
    }

    [AvaloniaFact]
    public async Task AssistantDialogShowsAConfigurationForTheOpenTrip()
    {
        var w = Window(); try
        {
            await Open(w); var m = w.Model;
            var shown = m.ConnectAssistantCommand.ExecuteAsync(null); Probe.Layout();
            var sheet = Sheet(w); var box = Probe.Tagged<TextBox>(sheet, "assistant-configuration");
            var server = JsonNode.Parse(box.Text!)!["mcpServers"]!["jourfold-ui-test"]!;
            Assert.Equal(MainViewModel.AssistantCommand, server["command"]!.ToString());
            Assert.Equal(["--mcp", m.Workspace!.Repository.Root], server["args"]!.AsArray().Select(a => a!.ToString()));
            Probe.All<CheckBox>(sheet).Single().IsChecked = true; Probe.Layout();
            Assert.Equal("--read-only", JsonNode.Parse(box.Text!)!["mcpServers"]!["jourfold-ui-test"]!["args"]![2]!.ToString());
            Probe.Click(sheet, m.Strings["Close"]); await shown.WaitAsync(TimeSpan.FromSeconds(3));
        }
        finally { w.Close(); }
    }

    [Fact]
    public void AssistantServerNamesAreShortAndPlain()
    {
        Assert.Equal("jourfold-spring-in-japan", MainViewModel.AssistantServerName("Spring in Japan"));
        Assert.Equal("jourfold-zurich-geneve-2027", MainViewModel.AssistantServerName("Zürich & Genève 2027"));
        Assert.Equal("jourfold-trip", MainViewModel.AssistantServerName("東京"));
    }

    [AvaloniaFact]
    public async Task QuickAddActivityUsesThePlaceTimezone()
    {
        var w = Window(); try
        {
            await Open(w);
            var museum = Entity.Create("place", "Tokyo National Museum"); museum.Data["timezone"] = "Asia/Tokyo";
            await w.Model.Workspace!.ApplyAsync([new(museum.Id, museum)]);
            var task = w.Model.AddAsync("sightseeing"); Probe.Layout(); var s = w.Model.Strings;
            Probe.Tagged<TextBox>(Sheet(w), "title").Text = "Museum";
            Probe.All<AutoCompleteBox>(Sheet(w)).First().Text = "Tokyo National Museum"; Probe.Layout();
            await Probe.Until(() => Probe.All<TextBlock>(Sheet(w)).Any(t => t.Text?.StartsWith(string.Format(s["TimezoneIs"], "Tokyo · Japan"), StringComparison.Ordinal) == true));
            Assert.False(Probe.All<SearchChoice>(Sheet(w)).Single(z => z.Tag as string == "zone").IsVisible);
            Probe.Click(Sheet(w), s["Change"]); Probe.Layout();
            Assert.True(Probe.All<SearchChoice>(Sheet(w)).Single(z => z.Tag as string == "zone").IsVisible);
            Probe.Tagged<TimePicker>(Sheet(w), "startTime").SelectedTime = new TimeSpan(10, 0, 0);
            Probe.Click(Sheet(w), s["Add"]); await task.WaitAsync(TimeSpan.FromSeconds(5));
            var visit = w.Model.Workspace.State.Trip.Entities.Values.Single(e => e.Title == "Museum");
            Assert.Equal("Asia/Tokyo", visit.Data["time"]!["start"]!["timezone"]!.ToString()); Assert.EndsWith("T10:00:00", visit.Data["time"]!["start"]!["local"]!.ToString());
        }
        finally { w.Close(); }
    }

    [AvaloniaFact]
    public async Task QuickAddCreatesTransportWithNewPlacesInOneUndoableStep()
    {
        var w = Window(); try
        {
            await Open(w); var count = w.Model.Workspace!.State.Trip.Entities.Count;
            var task = w.Model.AddAsync("transport"); Probe.Layout();
            Probe.Tagged<TextBox>(Sheet(w), "title").Text = "Train to Cologne";
            Probe.Click(Sheet(w), w.Model.Strings["train"]);
            var places = Probe.All<AutoCompleteBox>(Sheet(w)).ToArray(); places[0].Text = "Aachen Hbf"; places[1].Text = "Köln Hbf";
            Probe.Click(Sheet(w), "Add"); await task;
            var trip = w.Model.Workspace.State.Trip;
            var train = trip.Entities.Values.Single(e => e.Title == "Train to Cologne");
            Assert.Equal("train", train.Data["components"]!["transport"]!["type"]!.ToString());
            var route = ScheduleQueries.Route(trip, train); Assert.Equal("Aachen Hbf", route.From!.Title); Assert.Equal("Köln Hbf", route.To!.Title);
            Assert.Equal(count + 3, trip.Entities.Count); Assert.Equal(train.Id, w.Model.Selected!.Id);
            await w.Model.UndoCommand.ExecuteAsync(null); Assert.Equal(count, w.Model.Workspace.State.Trip.Entities.Count);
        }
        finally { w.Close(); }
    }

    [AvaloniaFact]
    public async Task SettingsApplyDirectlyAndLanguageRefreshesDerivedShareLabel()
    {
        var w = Window(); try
        {
            w.Model.SetPreference("Language", "de"); Assert.Equal("Teilen", w.Model.SharingLabel);
            w.Model.SetPreference("Language", "en"); Assert.Equal("Share", w.Model.SharingLabel);
            var task = w.Model.SettingsCommand.ExecuteAsync(null); Probe.Layout(); Probe.Click(Sheet(w), "Appearance"); Probe.Layout();
            Probe.Click(Sheet(w), "Dark");
            Assert.Equal("Dark", w.Model.Store.Get("theme")); Assert.False(task.IsCompleted);
            Probe.Click(Sheet(w), "Maps and places"); Probe.Layout(); Assert.False(w.Model.OnlineMaps);
            Probe.All<ToggleSwitch>(Sheet(w)).First().IsChecked = true; Assert.True(w.Model.OnlineMaps);
            Probe.Click(Sheet(w), "Close"); await task; Assert.NotNull(w.Icon);
        }
        finally { w.Model.SetOnlineMaps(false); w.Close(); }
    }

    [AvaloniaFact]
    public async Task DraggingEmptyTimetablePrefillsRangeAndCreatesOneUndoableActivity()
    {
        var w = Window(); try
        {
            await Open(w); var interaction = new List<QuickAddRequest>();
            var planner = Assert.IsType<TimetableView>(w.FindControl<ContentControl>("MainContent")!.Content);
            var canvas = Probe.All<Canvas>(planner).First(c => c.Height > 1000);
            var a = canvas.TranslatePoint(new Point(120, 10 + 9 * 54 + 2), w)!.Value; var b = canvas.TranslatePoint(new Point(120, 10 + 11 * 54 - 2), w)!.Value;
            w.MouseDown(a, MouseButton.Left); w.MouseMove(b); w.MouseUp(b, MouseButton.Left); Probe.Layout();
            await Probe.Until(() => w.FindControl<Grid>("DialogLayer")!.Children.Count > 0);
            Assert.Equal(TimeSpan.FromHours(9), Probe.Tagged<TimePicker>(Sheet(w), "startTime").SelectedTime);
            Probe.Tagged<TextBox>(Sheet(w), "title").Text = "Morning walk"; Probe.Click(Sheet(w), "Add");
            await Probe.Until(() => !w.Model.Busy && w.Model.Workspace!.State.Trip.Entities.Count == 1);
            var item = w.Model.Workspace!.State.Trip.Entities.Values.Single();
            Assert.Equal("Morning walk", w.Model.Selected!.Title); Assert.Equal("2027-05-14T09:00:00", item.Data["time"]!["start"]!["local"]!.ToString()); Assert.Equal(Duration.FromHours(2), ScheduleQueries.Span(w.Model.Workspace.State.Trip, item).Length);
            await w.Model.UndoCommand.ExecuteAsync(null); Assert.Empty(w.Model.Workspace.State.Trip.Entities);
        }
        finally { w.Close(); }
    }

    [AvaloniaFact]
    public async Task SampleTripOpensWithAllViewsPopulated()
    {
        var w = Window(); try
        {
            w.Model.Store.Set("trips.folder", root);
            await w.Model.OpenSampleCommand.ExecuteAsync(null); await Probe.Until(() => w.Model.HasTrip);
            Assert.True(w.Model.IsSample); Assert.DoesNotContain(w.Model.Diagnostics, d => d.Severity == Severity.Error);
            foreach (var view in new[] { "Plan", "List", "Map", "Bookings", "Costs", "Tasks", "Files", "People", "Places", "Collections", "History", "Variants" })
            { w.Model.Navigate(view); Probe.Layout(); Assert.NotNull(w.FindControl<ContentControl>("MainContent")!.Content); }
            Assert.Contains(w.Model.InboxItems, e => e.Title == "Onsen evening?");
        }
        finally { w.Close(); }
    }

    [AvaloniaFact]
    public void SearchChoicesRejectUnknownValuesAndKeepCodesOutOfUserInput()
    {
        var control = new SearchChoice(FieldOptions.Timezones, "Europe/Berlin", "Timezone"); Assert.Equal("Europe/Berlin", control.Value); Assert.StartsWith("Berlin · Germany", control.Text);
        // People search by country, city, region note or offset, not by IANA ID.
        foreach (var query in new[] { "China", "Shanghai", "Beijing", "UTC+8" }) Assert.Contains(FieldOptions.Timezones, c => c.Id == "Asia/Shanghai" && c.Label.Contains(query, StringComparison.CurrentCultureIgnoreCase));
        Assert.Contains(FieldOptions.Timezones, c => c.Id == "PRC" && c.Label.Contains("Asia/Shanghai"));
        Assert.Contains(FieldOptions.Timezones, c => c.Id == "Pacific/Auckland" && c.Label.Contains("New Zealand") && c.Label.Contains("UTC+12/+13"));
        var host = new Window { Content = control }; host.Show(); Probe.Layout(); Assert.True(control.Input.Bounds.Height > 20); host.Close();
        control.Text = "not-a-timezone"; Assert.Null(control.Value);
        Assert.Contains(FieldOptions.Languages, c => c.Id == "de" && c.Label.Contains("Deutsch"));
    }

    [Fact]
    public void PortableDesktopRegistrationUsesCorrectIdentityAndIcon()
    {
        if (!OperatingSystem.IsLinux()) return;
        var app = Path.Combine(root, "app with spaces"); Directory.CreateDirectory(Path.Combine(app, "branding")); File.WriteAllText(Path.Combine(app, "Jourfold.Desktop"), "fixture"); File.WriteAllBytes(Path.Combine(app, "branding", "icon-256.png"), [1, 2, 3]);
        var data = Path.Combine(root, "data"); var launcher = DesktopIntegration.InstallLinuxLauncher(app, data); var text = File.ReadAllText(launcher);
        Assert.Contains("StartupWMClass=Jourfold", text); Assert.Contains("Exec=\"" + Path.Combine(app, "Jourfold.Desktop") + "\" %u", text); Assert.True(File.Exists(Path.Combine(data, "icons/hicolor/256x256/apps/jourfold.png")));
    }

    [Fact]
    public void QuickAddBuildsValidEntitiesForEveryKind()
    {
        var manifest = Entity.CreateTrip("Test", "en", "Europe/Berlin"); var trip = new TripSnapshot(manifest, new Dictionary<Guid, Entity>());
        foreach (var kind in new[] { "activity", "food", "sightseeing", "transport", "accommodation", "task", "note", "booking", "place", "person", "expense", "budget", "collection" })
        {
            var draft = new QuickAddDraft(kind, "Item " + kind) { Date = new LocalDate(2027, 5, 14), Start = new LocalTime(10, 0), Amount = 12.5m, Currency = "EUR", Place = PlaceChoice.Create("Somewhere"), From = PlaceChoice.Create("A"), To = PlaceChoice.Create("B") };
            var (main, edits) = QuickAdd.Build(draft, trip);
            var entities = edits.Where(e => e.Value is not null && e.Value.Type != "trip").ToDictionary(e => e.Id, e => e.Value!);
            var next = new TripSnapshot(edits.FirstOrDefault(e => e.Value?.Type == "trip")?.Value ?? manifest, entities);
            Assert.Empty(next.All.SelectMany(TravelRepo.Serialization.SchemaValidation.Validate));
            Assert.DoesNotContain(SemanticValidation.Validate(next), d => d.Severity == Severity.Error);
            Assert.Equal("Item " + kind, main.Title);
        }
    }
}
