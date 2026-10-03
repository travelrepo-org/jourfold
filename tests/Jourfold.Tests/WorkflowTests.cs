using Avalonia.VisualTree;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Threading;
using Jourfold.Application;
using Jourfold.Desktop;
using Jourfold.Infrastructure;
using NodaTime;
using TravelRepo.Core;
using TravelRepo.Git;
using TravelRepo.Repository;
using Xunit;
[assembly: AvaloniaTestApplication(typeof(Jourfold.Tests.TestBuilder))]
[assembly: CollectionBehavior(DisableTestParallelization = true)]
namespace Jourfold.Tests;

public static class TestBuilder
{
    public static AppBuilder BuildAvaloniaApp()
    { Environment.SetEnvironmentVariable("JOURFOLD_DATA_HOME", Path.Combine(Path.GetTempPath(), "jourfold-headless-" + Guid.NewGuid())); return AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }); }
}
public sealed class TestInteraction : IInteraction
{
    public Queue<string?> Answers { get; } = new();
    public List<string> Errors { get; } = [];
    public Task<TripDraft?> NewTripAsync() => Task.FromResult<TripDraft?>(new(Answers.Dequeue()!, "en", null, null, null, [], null));
    public Task<string?> PromptAsync(string title, string initial = "", bool multiline = false) => Task.FromResult(Answers.Count > 0 ? Answers.Dequeue() : initial);
    public Task<string?> ChooseAsync(string title, IReadOnlyList<Choice> choices) => Task.FromResult(Answers.Count > 0 ? Answers.Dequeue() : choices.FirstOrDefault()?.Id);
    public Task<bool> ConfirmAsync(string message) => Task.FromResult(true);
    public Task<string?> FolderAsync() => Task.FromResult(Answers.Dequeue());
    public Task<string?> FileAsync(bool save = false, string? extension = null) => Task.FromResult(Answers.Dequeue());
    public Task ShowAsync(string title, string message) { Errors.Add(message); return Task.CompletedTask; }
    public Task CompareAsync(TripSnapshot current, TripSnapshot incoming) => Task.CompletedTask;
    public Task CopyAsync(string value) => Task.CompletedTask;
    public Task<IReadOnlyDictionary<string, string>?> FormAsync(string title, IReadOnlyList<FormField> fields) => Task.FromResult<IReadOnlyDictionary<string, string>?>(fields.ToDictionary(f => f.Key, f => Answers.Count > 0 ? Answers.Dequeue() ?? "" : f.Value));
    public Task<IReadOnlyList<string>?> SelectManyAsync(string title, IReadOnlyList<Choice> choices, IReadOnlyList<string> selected) => Task.FromResult<IReadOnlyList<string>?>(Answers.Count > 0 ? new[] { Answers.Dequeue()! } : selected);
    public Task<JsonObject?> ScheduleAsync(JsonObject? initial, LocalDate date, string zone) => Task.FromResult(initial);
    public Task<ZonedTime?> ResolveTimeAsync(string local, string zone, string? offset = null) => Task.FromResult<ZonedTime?>(new(local, zone, offset));
    public Task SettingsAsync(MainViewModel model) => Task.CompletedTask;
    public void Open(string path) { }
}
public sealed class WorkflowTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "jourfold-flow-" + Guid.NewGuid());
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
    private async Task<Workspace> Create()
    {
        var path = Path.Combine(root, "trip"); var r = new TravelRepository(path); await r.InitializeAsync(Entity.CreateTrip("Tokyo journey", "en", "Asia/Tokyo")); await new GitRepository(r, new GitCliBackend()).InitializeAsync("Alex", "alex@example.invalid"); var w = new Workspace(path, new GitCliBackend()); await w.OpenAsync(); return w;
    }
    [Fact]
    public async Task EditsUndoRedoExternalChangeAndWriteOwnership()
    {
        using var w = await Create(); var p = Entity.Create("person", "Alex"); await w.EditAsync(p); Assert.Single(w.State.Trip.Entities); await w.UndoAsync(); Assert.Empty(w.State.Trip.Entities); await w.RedoAsync(); Assert.Single(w.State.Trip.Entities);
        Assert.Throws<DomainException>(() => new Workspace(w.Repository.Root, new GitCliBackend()));
        await File.WriteAllTextAsync(w.Repository.SafePath("travel.yaml"), "external: true\n" + await File.ReadAllTextAsync(w.Repository.SafePath("travel.yaml"))); Assert.True(await w.ReloadAsync()); Assert.False(w.CanUndo);
    }
    [Fact]
    public async Task ReleaseDomainWorkflow()
    {
        using var w = await Create(); var person = Entity.Create("person", "Alex"); var place = Entity.Create("place", "Tokyo Station"); await w.ApplyAsync([new(person.Id, person), new(place.Id, place)]);
        var flight = Entity.Create("schedule_item", "Flight to Tokyo"); flight.Data["time"] = new JsonObject { ["precision"] = "exact", ["start"] = new ZonedTime("2027-05-12T13:20:00", "Europe/Berlin").ToJson(), ["end"] = new ZonedTime("2027-05-13T08:35:00", "Asia/Tokyo").ToJson() }; await w.EditAsync(flight);
        var activity = Entity.Create("schedule_item", "Museum visit"); await w.EditAsync(activity); await w.ScheduleAsync(activity.Id, new ZonedTime("2027-05-14T10:00:00", "Asia/Tokyo"), Duration.FromHours(2));
        var task = Entity.Create("task", "Book museum"); var booking = Entity.Create("booking", "Museum tickets"); booking.Data["items"] = new JsonArray(activity.Id.ToString()); booking.Data["travelers"] = new JsonArray(person.Id.ToString()); await w.ApplyAsync([new(task.Id, task), new(booking.Id, booking)]);
        var image = Path.Combine(root, "image.png"); await File.WriteAllBytesAsync(image, Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+a5V8AAAAASUVORK5CYII=")); await w.Repository.ImportDocumentAsync(image, "image/png"); await w.ReloadAsync();
        var ticket = Path.Combine(root, "ticket.pdf"); PdfExport.Write(w.State.Trip, ticket, Path.Combine(AppContext.BaseDirectory, "fonts", "PlusJakartaSans-Regular.ttf")); await w.ImportDocumentAsync(ticket, "application/pdf");
        await w.Git.CreateVersionAsync("Plan Tokyo"); await w.Git.CreateVariantAsync("museum-first", "Museum first"); await w.ReloadAsync(); var changed = w.State.Trip.Find(activity.Id)!.Copy(); changed.Data["title"] = "Museum and garden"; await w.EditAsync(changed); await w.Git.CreateVersionAsync("Add garden"); await w.Git.SwitchAsync("main"); await w.ReloadAsync();
        var plan = await w.CompareMergeAsync("museum-first"); Assert.True(plan.CanApply); await w.ApplyMergeAsync("museum-first", plan); Assert.Equal("Museum and garden", w.State.Trip.Find(activity.Id)!.Title);
        await w.Git.SwitchAsync("museum-first"); await w.Git.UpdateVariantAsync(archive: true); await w.Git.SwitchAsync("main"); await w.ReloadAsync();
        var remote = Path.Combine(root, "remote.git"); Directory.CreateDirectory(remote); await new GitCliBackend().ExecuteAsync(remote, ["init", "--bare"]); await w.Git.AddRemoteAsync("backup", remote); await w.Git.PushAsync("backup"); await w.Git.FetchAsync("backup");
        var offline = await new TravelRepository(w.Repository.Root).ReadAsync(); using var store = new LocalStore(Path.Combine(root, "local")); store.Index(w.Repository.Root, offline.Trip); Assert.Contains(activity.Id, store.Search(w.Repository.Root, "garden"));
        var ics = TripExport.Ics(offline.Trip); Assert.Contains("20270512T112000Z", ics); Assert.Contains("Museum and garden", TripExport.Html(offline.Trip));
        var font = Path.Combine(AppContext.BaseDirectory, "fonts", "PlusJakartaSans-Regular.ttf"); var pdf = Path.Combine(root, "trip.pdf"); PdfExport.Write(offline.Trip, pdf, font); Assert.StartsWith("%PDF-", System.Text.Encoding.ASCII.GetString((await File.ReadAllBytesAsync(pdf))[..8]));
        if (Environment.GetEnvironmentVariable("JOURFOLD_CAPTURE_DIRECTORY") is { } capture) { Directory.CreateDirectory(capture); File.Copy(pdf, Path.Combine(capture, "trip.pdf"), true); await File.WriteAllTextAsync(Path.Combine(capture, "trip.ics"), ics); }
    }
    [AvaloniaFact]
    public async Task ShellLoadsAndTimetableDropWritesCanonicalTime()
    {
        using var w = await Create(); var path = w.Repository.Root; var activity = Entity.Create("schedule_item", "Aachen walk"); await w.EditAsync(activity); w.Dispose();
        var settings = (LocalStore)App.Services.GetService(typeof(LocalStore))!; settings.Set("textsize", "Normal"); settings.Set("highcontrast", "false");
        var window = new MainWindow(); window.Show(); await window.Model.OpenAsync(path); window.Model.StartDate = new LocalDate(2027, 5, 12); window.Model.Refresh(); Dispatcher.UIThread.RunJobs();
        Assert.True(window.Model.HasTrip); Assert.Single(window.Model.InboxItems);
        var planner = window.FindControl<ContentControl>("MainContent")!.Content as PlanningView; Assert.NotNull(planner);
        var point = planner!.TranslatePoint(new Point(120, 180), window)!.Value; var data = new DataTransfer(); data.Add(DataTransferItem.Create(PlanningView.EntityFormat, activity.Id.ToString()));
        window.DragDrop(point, RawDragEventType.DragEnter, data, DragDropEffects.Move, RawInputModifiers.None); window.DragDrop(point, RawDragEventType.Drop, data, DragDropEffects.Move, RawInputModifiers.None);
        for (var i = 0; i < 100 && window.Model.Workspace!.State.Trip.Find(activity.Id)!.Data["time"] is null; i++) await Task.Delay(20);
        Assert.NotNull(window.Model.Workspace!.State.Trip.Find(activity.Id)!.Data["time"]);
        for (var i = 0; i < 100 && window.Model.Busy; i++) await Task.Delay(20);
        window.Model.Selected = window.Model.Workspace.State.Trip.Find(activity.Id); Assert.NotEmpty(window.FindControl<StackPanel>("Inspector")!.Children);
        Dispatcher.UIThread.RunJobs();
        planner = (PlanningView)window.FindControl<ContentControl>("MainContent")!.Content!;
        var block = planner.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Tag, activity.Id)); var resizePoint = block.TranslatePoint(new Point(block.Bounds.Width / 2, block.Bounds.Height - 3), window)!.Value;
        var beforeEnd = ZonedTime.From(window.Model.Workspace.State.Trip.Find(activity.Id)!.Data["time"]!["end"]!).ToInstant();
        window.MouseDown(resizePoint, MouseButton.Left); window.MouseMove(resizePoint + new Point(0, 55)); window.MouseUp(resizePoint + new Point(0, 55), MouseButton.Left);
        for (var i = 0; i < 100 && ZonedTime.From(window.Model.Workspace.State.Trip.Find(activity.Id)!.Data["time"]!["end"]!).ToInstant() == beforeEnd; i++) await Task.Delay(20);
        Assert.Equal(beforeEnd + Duration.FromHours(1), ZonedTime.From(window.Model.Workspace.State.Trip.Find(activity.Id)!.Data["time"]!["end"]!).ToInstant());
        for (var i = 0; i < 100 && window.Model.Busy; i++) await Task.Delay(20);
        Dispatcher.UIThread.RunJobs(); planner = (PlanningView)window.FindControl<ContentControl>("MainContent")!.Content!; block = planner.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Tag, activity.Id)); var movePoint = block.TranslatePoint(new Point(20, 15), window)!.Value;
        var beforeStart = ZonedTime.From(window.Model.Workspace.State.Trip.Find(activity.Id)!.Data["time"]!["start"]!).ToInstant();
        window.MouseDown(movePoint, MouseButton.Left); window.MouseMove(movePoint + new Point(0, 55)); window.MouseUp(movePoint + new Point(0, 55), MouseButton.Left);
        for (var i = 0; i < 100 && ZonedTime.From(window.Model.Workspace.State.Trip.Find(activity.Id)!.Data["time"]!["start"]!).ToInstant() == beforeStart; i++) await Task.Delay(20);
        Assert.Equal(beforeStart + Duration.FromHours(1), ZonedTime.From(window.Model.Workspace.State.Trip.Find(activity.Id)!.Data["time"]!["start"]!).ToInstant());
        if (Environment.GetEnvironmentVariable("JOURFOLD_CAPTURE_DIRECTORY") is { } capture)
        {
            Directory.CreateDirectory(capture); Dispatcher.UIThread.RunJobs();
            using var frame = window.CaptureRenderedFrame(); frame?.Save(Path.Combine(capture, "planning.png"));
        }
        window.Close();
    }
    [AvaloniaTheory]
    [InlineData("en", "Light")]
    [InlineData("de", "Dark")]
    public void LocalizedShellSupportsThemes(string language, string theme)
    {
        var settings = (LocalStore)App.Services.GetService(typeof(LocalStore))!; settings.Set("language", language); settings.Set("theme", theme); var w = new MainWindow(); w.Show(); Assert.Equal(language, w.Model.Strings.Language); Assert.True(w.Model.IsLibrary); Assert.NotNull(w.FindControl<ContentControl>("MainContent")!.Content); w.Close();
        var en = new Localization("en"); var de = new Localization("de"); Assert.Equal(en.All.Keys.Order(), de.All.Keys.Order()); Assert.All(de.All.Values, value => Assert.False(string.IsNullOrWhiteSpace(value)));
    }
    [Fact]
    public async Task PluginContractComputesThroughPublicApi()
    { TravelRepo.Plugins.ITravelPlugin plugin = new Jourfold.ExamplePlugin.WalkingNotes(); Assert.Equal(1, plugin.Describe().ApiVersion); Assert.Equal(1000, (double)(await plugin.InvokeAsync("walking.duration", new JsonObject { ["distance_m"] = 1400 }))!["duration_seconds"]!, 8); }
    [Fact]
    public async Task PluginHostUsesVersionedRpcOutOfProcess()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Jourfold.sln"))) directory = directory.Parent;
        Assert.NotNull(directory);
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        var host = Path.Combine(directory!.FullName, "src", "Jourfold.PluginHost", "bin", configuration, "net10.0", "Jourfold.PluginHost.dll");
        var pluginDir = Path.Combine(directory.FullName, "samples", "Jourfold.ExamplePlugin", "bin", configuration, "net10.0");
        using var plugin = new PluginProcess(host, pluginDir, Environment.GetEnvironmentVariable("DOTNET_HOST_PATH"));
        Assert.Equal("org.travelrepo.walking", (await plugin.DescribeAsync()).Id);
        Assert.Equal(1000, (double)(await plugin.InvokeAsync("walking.duration", new JsonObject { ["distance_m"] = 1400 }))!["duration_seconds"]!, 8);
    }

    [AvaloniaFact]
    public async Task NewTripCommandAndEditorCreateRealOfflineContent()
    {
        using var store = new LocalStore(Path.Combine(root, "settings")); store.Set("tripRoot", Path.Combine(root, "trips"));
        var interaction = new TestInteraction(); interaction.Answers.Enqueue("Aachen weekend"); interaction.Answers.Enqueue(Path.Combine(root, "chosen-folder"));
        using var model = new MainViewModel(store, new GitCliBackend(), new OsSecretStore(), interaction);
        await model.NewTripCommand.ExecuteAsync(null); Assert.True(model.HasTrip); Assert.Empty(interaction.Errors);
        foreach (var type in new[] { "person", "place", "activity", "task", "booking", "expense", "collection", "note" }) { interaction.Answers.Enqueue("New " + type); await model.AddAsync(type); }
        Assert.Equal(8, model.Workspace!.State.Trip.Entities.Count);
        var activity = model.Workspace.State.Trip.Entities.Values.Single(e => e.Type == "schedule_item");
        await EditorModel.UpdateAsync(model, activity.Id, new EditorField("title", "Title"), "Explore Aachen");
        Assert.Equal("Explore Aachen", model.Workspace.State.Trip.Find(activity.Id)!.Title);
        await model.UndoCommand.ExecuteAsync(null); Assert.Equal("New activity", model.Workspace.State.Trip.Find(activity.Id)!.Title);
        Assert.Single(await model.Workspace.Git.HistoryAsync());
    }
    [AvaloniaFact]
    public async Task MapAcceptsRoundTrippedDecimalCoordinatesAndSharedSelection()
    {
        using var w = await Create(); var place = Entity.Create("place", "Aachen"); place.Data["location"] = new JsonObject { ["latitude"] = 50.7753, ["longitude"] = 6.0839 }; await w.EditAsync(place); var path = w.Repository.Root; w.Dispose();
        var window = new MainWindow(); window.Show(); await window.Model.OpenAsync(path); window.Model.Navigate("Map"); window.Model.Selected = window.Model.Workspace!.State.Trip.Find(place.Id); Dispatcher.UIThread.RunJobs();
        Assert.IsType<PlanningView>(window.FindControl<ContentControl>("MainContent")!.Content); Assert.Equal(place.Id, window.Model.Selected!.Id); window.Close();
    }
    [Fact]
    public async Task MergeTransfersNewBinaryResourcesAndPreservesPrimaryMetadata()
    {
        using var w = await Create(); var primary = w.State.Trip.Manifest.Data["variant"]!.ToJsonString(); await w.Git.CreateVariantAsync("files", "Travel files"); await w.ReloadAsync();
        var source = Path.Combine(root, "binary.pdf"); var bytes = new byte[] { 0, 255, 254, 128, 1, 0 }; await File.WriteAllBytesAsync(source, bytes); var document = await w.Repository.ImportDocumentAsync(source, "application/pdf"); await w.ReloadAsync(); await w.Git.CreateVersionAsync("Attach document");
        await w.Git.SwitchAsync("main"); await w.ReloadAsync(); Assert.Empty(w.State.Trip.Entities);
        var plan = await w.CompareMergeAsync("files"); await w.ApplyMergeAsync("files", plan);
        Assert.Equal(primary, w.State.Trip.Manifest.Data["variant"]!.ToJsonString()); Assert.Equal(bytes, await File.ReadAllBytesAsync(w.Repository.DocumentPath(w.State.Trip.Find(document.Id)!)));
        Assert.Equal(2, (await w.Git.HistoryAsync())[0].Parents.Split(' ').Length);
    }
    [Fact]
    public async Task StaleMergePlanDoesNotClaimToMergeNewerIncomingVersion()
    {
        using var w = await Create(); await w.Git.CreateVariantAsync("alternative", "Alternative"); await w.ReloadAsync(); var item = Entity.Create("task", "First change"); await w.EditAsync(item); await w.Git.CreateVersionAsync("Add task"); await w.Git.SwitchAsync("main"); await w.ReloadAsync(); var plan = await w.CompareMergeAsync("alternative");
        await w.Git.SwitchAsync("alternative"); await w.ReloadAsync(); var edit = w.State.Trip.Find(item.Id)!.Copy(); edit.Data["title"] = "Newer change"; await w.EditAsync(edit); await w.Git.CreateVersionAsync("Newer task"); await w.Git.SwitchAsync("main"); await w.ReloadAsync();
        Assert.Equal("merge.stale", (await Assert.ThrowsAsync<DomainException>(() => w.ApplyMergeAsync("alternative", plan))).Code); Assert.Empty(w.State.Trip.Entities);
    }

}
