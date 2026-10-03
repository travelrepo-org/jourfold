using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
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
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
    private static Control Sheet(MainWindow w) => (Control)w.FindControl<Grid>("DialogLayer")!.Children.Last();
    private static IEnumerable<T> Controls<T>(Control c) where T : Control => c.GetVisualDescendants().OfType<T>();
    private static void Click(Control c, string label) => Controls<Button>(c).Single(b => b.Content?.ToString() == label).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static void Layout() => Dispatcher.UIThread.RunJobs();
    private static MainWindow Window()
    {
        var w = new MainWindow(); w.Model.SetPreference("Language", "en"); w.Model.SetPreference("TextSize", "Normal"); w.Model.SetPreference("HighContrast", "false");
        w.Model.SetPreference("OpenView", "Plan"); w.Model.SetPreference("Density", "Comfortable"); w.Model.SetPreference("Theme", "Light");
        w.Show(); Layout(); return w;
    }
    private async Task Open(MainWindow w)
    {
        var repository = new TravelRepository(Path.Combine(root, "trip")); await repository.InitializeAsync(Entity.CreateTrip("UI test", "en", "Europe/Berlin"));
        await new GitRepository(repository, new GitCliBackend()).InitializeAsync("Test", "test@example.invalid"); await w.Model.OpenAsync(repository.Root); w.Model.Navigate("Plan"); w.Model.StartDate = new LocalDate(2027, 5, 14); w.Model.Refresh(); Layout();
    }
    [AvaloniaFact]
    public async Task FormTakesKeyboardFocusAndEscapeCancelsWithoutANativeWindow()
    {
        var w = Window(); try
        {
            var task = w.Model.Interaction.NewTripAsync(); Layout();
            Assert.IsType<TextBox>(w.FocusManager!.GetFocusedElement());
            w.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None); w.KeyReleaseQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            Assert.Null(await task.WaitAsync(TimeSpan.FromSeconds(3))); Assert.Empty(w.FindControl<Grid>("DialogLayer")!.Children);
        }
        finally { w.Close(); }
    }
    [AvaloniaFact]
    public async Task InboxClickSelectsWithoutStartingDragAndCalendarHasContextActions()
    {
        var w = Window(); try
        {
            await Open(w); var item = Entity.Create("schedule_item", "Idea"); await w.Model.Workspace!.EditAsync(item); w.Model.InboxOpen = true; Layout();
            var inbox = w.FindControl<ListBox>("InboxList")!; var row = Controls<ListBoxItem>(inbox).Single(); var point = row.TranslatePoint(new Point(25, 12), w)!.Value;
            w.MouseDown(point, MouseButton.Left); w.MouseUp(point, MouseButton.Left); Layout();
            Assert.Equal(item.Id, w.Model.Selected!.Id); Assert.False(w.Model.Busy); Assert.Empty(w.FindControl<Grid>("DialogLayer")!.Children);
            await w.Model.Workspace.ScheduleAsync(item.Id, new ZonedTime("2027-05-14T03:00:00", "Europe/Berlin"), Duration.FromHours(1)); Layout();
            var planner = (PlanningView)w.FindControl<ContentControl>("MainContent")!.Content!;
            var block = Controls<Button>(planner).Single(b => Equals(b.Tag, item.Id)); Assert.NotNull(block.ContextMenu);
            Assert.Equal("Delete", block.ContextMenu!.Items.Cast<MenuItem>().Single().Header);
            Assert.NotNull(ToolTip.GetTip(block));
        }
        finally { w.Close(); }
    }
    [AvaloniaFact]
    public async Task NewTripFormUsesCalendarAndParticipantListWithCancellation()
    {
        var w = Window(); try
        {
            var task = w.Model.Interaction.NewTripAsync(); Layout(); var sheet = Sheet(w);
            Controls<TextBox>(sheet).Single(t => t.Tag as string == "title").Text = "Aachen";
            var destination = Path.Combine(root, "selected-trip"); Directory.CreateDirectory(destination);
            Controls<TextBox>(sheet).Single(t => t.Tag as string == "destination").Text = destination;
            Controls<Expander>(sheet).Single().IsExpanded = true; Layout();
            Assert.Empty(Controls<ListBox>(sheet).Single().Items);
            Click(sheet, "+ Add participant"); Layout(); Controls<TextBox>(Sheet(w)).Single(t => t.Tag as string == "name").Text = "Cancelled"; Click(Sheet(w), "Cancel"); Layout();
            Assert.Empty(Controls<ListBox>(sheet).Single().Items);
            Click(sheet, "+ Add participant"); Layout(); Controls<TextBox>(Sheet(w)).Single(t => t.Tag as string == "name").Text = "Alex"; Click(Sheet(w), "Save"); await Task.Delay(20); Layout();
            Assert.Single(Controls<ListBox>(sheet).Single().Items);
            Controls<CalendarDatePicker>(sheet).Single(t => t.Tag as string == "start").SelectedDate = new DateTime(2027, 5, 14);
            Click(sheet, "New trip"); var draft = await task.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.NotNull(draft); Assert.Equal(destination, draft.Destination); Assert.Equal("2027-05-14", draft.Start); Assert.Equal(new[] { "Alex" }, draft.Participants);
            Assert.Empty(w.FindControl<Grid>("DialogLayer")!.Children); Assert.True(w.FindControl<LayoutTransformControl>("RootLayout")!.IsEnabled);
        }
        finally { w.Close(); }
    }
    [AvaloniaFact]
    public async Task SchedulePickersValidateRangeAndStoreCanonicalValues()
    {
        var w = Window(); try
        {
            var task = w.Model.Interaction.ScheduleAsync(null, new LocalDate(2027, 5, 14), "Europe/Berlin"); Layout();
            var start = (LocalDateTimeEditor)Controls<FormControl>(Sheet(w)).Single(f => f.Field.Key == "start").Editor;
            var end = (LocalDateTimeEditor)Controls<FormControl>(Sheet(w)).Single(f => f.Field.Key == "end").Editor;
            start.Time.SelectedTime = TimeSpan.FromHours(23); end.Time.SelectedTime = TimeSpan.FromHours(1);
            Click(Sheet(w), "Save"); await Task.Delay(20); Assert.False(task.IsCompleted);
            end.Date.SelectedDate = new DateTime(2027, 5, 15);
            Assert.True(Controls<FormControl>(Sheet(w)).Where(f => f.IsVisible).All(f => f.IsValid), string.Join(";", Controls<FormControl>(Sheet(w)).Select(f => f.Field.Key + "=" + f.Value))); Click(Sheet(w), "Save");
            var time = await task.WaitAsync(TimeSpan.FromSeconds(3)); Assert.NotNull(time);
            Assert.Equal("2027-05-14T23:00:00", time["start"]!["local"]!.ToString()); Assert.Equal(Duration.FromHours(2), ZonedTime.From(time["end"]!).ToInstant() - ZonedTime.From(time["start"]!).ToInstant());
            Assert.DoesNotContain(Controls<TextBox>(w), t => t.Tag as string == "time/start/local");
        }
        finally { w.Close(); }
    }
    [AvaloniaFact]
    public async Task DayPartEditorPreservesItsOwnTimezone()
    {
        var w = Window(); try
        {
            var initial = new JsonObject { ["precision"] = "day_part", ["date"] = "2027-05-14", ["day_part"] = "morning", ["timezone"] = "Asia/Tokyo" };
            var task = w.Model.Interaction.ScheduleAsync(initial, new LocalDate(2027, 5, 14), "Europe/Berlin"); Layout();
            var zone = Controls<FormControl>(Sheet(w)).Single(f => f.Field.Key == "dayZone"); Assert.True(zone.IsVisible); Assert.Equal("Asia/Tokyo", zone.Value);
            Click(Sheet(w), "Save"); var result = await task.WaitAsync(TimeSpan.FromSeconds(3)); Assert.Equal("Asia/Tokyo", result!["timezone"]!.ToString()); Assert.Equal("morning", result["day_part"]!.ToString());
        }
        finally { w.Close(); }
    }
    [AvaloniaFact]
    public async Task ParticipantAssignmentIsExplicitAndCancelDoesNotChangeSelection()
    {
        var w = Window(); try
        {
            var options = new[] { new Choice("alex", "Alex"), new Choice("carla", "Carla") };
            var task = w.Model.Interaction.SelectManyAsync("Participants", options, ["alex"]); Layout();
            var left = Controls<ListBox>(Sheet(w)).Single(l => l.Tag as string == "available"); var right = Controls<ListBox>(Sheet(w)).Single(l => l.Tag as string == "assigned");
            Assert.Equal("Alex", right.Items.Cast<Choice>().Single().Label); left.SelectedIndex = 0; Click(Sheet(w), "Add →");
            Assert.Equal(2, right.ItemCount); Click(Sheet(w), "Save"); Assert.Equal(new[] { "alex", "carla" }, await task);
            task = w.Model.Interaction.SelectManyAsync("Participants", options, ["alex"]); Layout();
            right = Controls<ListBox>(Sheet(w)).Single(l => l.Tag as string == "assigned"); right.SelectedIndex = 0; Click(Sheet(w), "← Remove"); Click(Sheet(w), "Cancel"); Assert.Null(await task);
        }
        finally { w.Close(); }
    }
    [AvaloniaFact]
    public async Task SettingsApplyDirectlyAndLanguageRefreshesDerivedShareLabel()
    {
        var w = Window(); try
        {
            w.Model.SetPreference("Language", "de"); Assert.Equal("Veröffentlichen", w.Model.SharingLabel);
            w.Model.SetPreference("Language", "en"); Assert.Equal("Publish", w.Model.SharingLabel);
            var task = w.Model.SettingsCommand.ExecuteAsync(null); Layout(); Click(Sheet(w), "Appearance"); Layout();
            var theme = Controls<ComboBox>(Sheet(w)).Single(c => c.Tag as string == "Theme"); theme.SelectedItem = theme.Items.Cast<Choice>().Single(c => c.Id == "Dark");
            Assert.Equal("Dark", w.Model.Store.Get("theme")); Assert.False(task.IsCompleted); Assert.DoesNotContain(Controls<Button>(Sheet(w)), b => b.Content?.ToString() == "Continue");
            Click(Sheet(w), "Close"); await task; Assert.NotNull(w.Icon);
        }
        finally { w.Close(); }
    }
    [AvaloniaFact]
    public async Task DraggingEmptyTimetablePrefillsRangeAndCreatesOneUndoableActivity()
    {
        var w = Window(); try
        {
            await Open(w); var planner = (PlanningView)w.FindControl<ContentControl>("MainContent")!.Content!;
            var canvas = (Canvas)((Grid)planner.Content!).Children.OfType<ScrollViewer>().Single().Content!; var a = canvas.TranslatePoint(new Point(120, 260), w)!.Value; var b = a + new Point(0, 110);
            w.MouseDown(a, MouseButton.Left); w.MouseMove(b); w.MouseUp(b, MouseButton.Left); Layout();
            Controls<TextBox>(Sheet(w)).Single().Text = "Morning walk"; Click(Sheet(w), "Continue"); await Task.Delay(20); Layout();
            var start = (LocalDateTimeEditor)Controls<FormControl>(Sheet(w)).Single(f => f.Field.Key == "start").Editor;
            var end = (LocalDateTimeEditor)Controls<FormControl>(Sheet(w)).Single(f => f.Field.Key == "end").Editor;
            Assert.Equal(TimeSpan.FromHours(4), start.Time.SelectedTime); Assert.Equal(TimeSpan.FromHours(6), end.Time.SelectedTime);
            Click(Sheet(w), "Save");
            for (var i = 0; i < 150 && (w.Model.Busy || w.Model.Workspace!.State.Trip.Entities.Count == 0); i++) await Task.Delay(20);
            Assert.Single(w.Model.Workspace!.State.Trip.Entities); Assert.Equal("Morning walk", w.Model.Selected!.Title);
            await w.Model.UndoCommand.ExecuteAsync(null); Assert.Empty(w.Model.Workspace.State.Trip.Entities);
        }
        finally { w.Close(); }
    }
    [AvaloniaFact]
    public void SearchChoicesRejectUnknownValuesAndKeepCodesOutOfUserInput()
    {
        var control = new SearchChoice(FieldOptions.Timezones, "Europe/Berlin", "Timezone"); Assert.Equal("Europe/Berlin", control.Value); Assert.Contains(" / ", control.Text);
        var host = new Window { Content = control }; host.Show(); Layout(); Assert.True(control.Input.Bounds.Height > 20); host.Close();
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
}
