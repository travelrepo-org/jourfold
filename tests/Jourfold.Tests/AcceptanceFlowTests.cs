using Avalonia.VisualTree;
using System.Text;
using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
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

public sealed class AcceptanceFlowTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "jourfold-acceptance-" + Guid.NewGuid());
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
    private async Task<string> Create()
    {
        var repo = new TravelRepository(Path.Combine(root, "trip")); await repo.InitializeAsync(Entity.CreateTrip("Aachen", "de", "Europe/Berlin")); await new GitRepository(repo, new GitCliBackend()).InitializeAsync("Alex", "alex@example.invalid"); return repo.Root;
    }
    [Fact]
    public async Task AttachmentsAndMarkdownUndoAsCoherentCommands()
    {
        var path = await Create(); using var workspace = new Workspace(path, new GitCliBackend()); await workspace.OpenAsync();
        var source = Path.Combine(root, "ticket.pdf"); await File.WriteAllBytesAsync(source, Encoding.UTF8.GetBytes("%PDF-1.4\nfixture"));
        var doc = await workspace.ImportDocumentAsync(source, "application/pdf"); var blob = workspace.Repository.DocumentPath(doc); Assert.True(File.Exists(blob));
        await workspace.UndoAsync(); Assert.Null(workspace.State.Trip.Find(doc.Id)); Assert.False(File.Exists(blob)); await workspace.RedoAsync(); Assert.NotNull(workspace.State.Trip.Find(doc.Id)); Assert.True(File.Exists(blob));
        var note = Entity.Create("note", "Gate instructions"); note.Data["content"] = new JsonArray(new JsonObject { ["type"] = "markdown", ["file"] = "documents/gate.md" });
        await workspace.ApplyAsync([new(note.Id, note)], resources: [new("documents/gate.md", Encoding.UTF8.GetBytes("## Gate A12"))]);
        await workspace.UndoAsync(); Assert.False(workspace.State.Trip.Resources.ContainsKey("documents/gate.md")); await workspace.RedoAsync(); Assert.Contains("Gate A12", Encoding.UTF8.GetString(workspace.State.Trip.Resources["documents/gate.md"]));
        var scheduled = await workspace.PlanMaterialAsync(note.Id, new ZonedTime("2027-05-12T10:00:00", "Europe/Berlin"), Duration.FromMinutes(45));
        Assert.Equal(note.Id.ToString(), workspace.State.Trip.Find(scheduled)!.Data["content"]![0]!["entity"]!.ToString()); Assert.NotNull(workspace.State.Trip.Find(note.Id));
    }
    [AvaloniaFact]
    public async Task MalformedEntityOpenOffersReviewedRestoreWithoutDroppingUnknownData()
    {
        var path = await Create(); var repo = new TravelRepository(path); var p = Entity.Create("person", "Alex"); p.Data["extensions"]!["org.example.keep"] = "preserved";
        await repo.ApplyAsync(await repo.ReadAsync(), [new(p.Id, p)]); await new GitRepository(repo, new GitCliBackend()).CreateVersionAsync("Add Alex");
        var state = await repo.ReadAsync(); await File.WriteAllTextAsync(repo.SafePath(state.Files[p.Id].Path), "malformed: [");
        using var store = new LocalStore(Path.Combine(root, "local")); var interaction = new TestInteraction(); using var vm = new MainViewModel(store, new GitCliBackend(), new OsSecretStore(), interaction);
        await vm.OpenAsync(path); Assert.Single(interaction.Errors); Assert.DoesNotContain(vm.Workspace!.State.Diagnostics, d => d.Severity == Severity.Error); Assert.Equal("preserved", vm.Workspace.State.Trip.Find(p.Id)!.Data["extensions"]!["org.example.keep"]!.ToString());
        Assert.NotEmpty(Directory.GetFiles(repo.RecoveryRoot, "*.complete"));
    }
    [AvaloniaFact]
    public async Task IdentityTemplateMapsToOneTripLocalPersonAndInboxIncludesUnscheduledPrecision()
    {
        var path = await Create(); using var store = new LocalStore(Path.Combine(root, "local")); store.Set("identity.name", "Alex"); store.Set("identity.email", "alex@example.invalid");
        using var vm = new MainViewModel(store, new GitCliBackend(), new OsSecretStore(), new TestInteraction()); await vm.OpenAsync(path);
        await vm.AddIdentityCommand.ExecuteAsync(null); await vm.AddIdentityCommand.ExecuteAsync(null); Assert.Single(vm.Workspace!.State.Trip.Entities.Values, e => e.Type == "person");
        var activity = Entity.Create("schedule_item", "Maybe"); await vm.Workspace.EditAsync(activity); Assert.Contains(vm.InboxItems, e => e.Id == activity.Id);
        vm.InboxPinned = true; Assert.True(vm.InboxOpen); Assert.Equal("true", store.Get("inbox.pinned"));
    }
    [AvaloniaFact]
    public async Task AccommodationAndCommentsUseStructuredFieldsAndRenderTogether()
    {
        var path = await Create(); using var store = new LocalStore(Path.Combine(root, "local")); var interaction = new TestInteraction(); using var vm = new MainViewModel(store, new GitCliBackend(), new OsSecretStore(), interaction); await vm.OpenAsync(path);
        var person = Entity.Create("person", "Alex"); var place = Entity.Create("place", "Hotel Aachen"); await vm.Workspace!.ApplyAsync([new(person.Id, person), new(place.Id, place)]);
        interaction.Answers.Enqueue("Hotel stay"); await vm.AddAsync("accommodation"); var stay = vm.Selected!;
        interaction.Answers.Enqueue("2027-05-12T15:00:00"); interaction.Answers.Enqueue("Europe/Berlin"); await EditorModel.ComplexAsync(vm, stay.Id, EditorModel.Fields(stay).Single(f => f.Path.EndsWith("check_in", StringComparison.Ordinal)));
        await EditorModel.UpdateAsync(vm, stay.Id, new("components/accommodation/rooms", "Rooms", "list"), "Room 21, Room 22");
        interaction.Answers.Enqueue("Meet in the lobby"); await vm.AddAsync("comment");
        var comment = vm.Workspace.State.Trip.Entities.Values.Single(e => e.Type == "comment"); Assert.Equal(stay.Id.ToString(), comment.Data["target"]!["id"]!.ToString()); Assert.Equal(person.Id.ToString(), comment.Data["author"]!.ToString());
        var snapshot = vm.Workspace.State.Trip; Assert.DoesNotContain(vm.Workspace.State.Diagnostics, d => d.Severity == Severity.Error); Assert.Equal(2, ((JsonArray)snapshot.Find(stay.Id)!.Data["components"]!["accommodation"]!["rooms"]!).Count);
        vm.Dispose(); var window = new MainWindow(); window.Show(); await window.Model.OpenAsync(path); window.Model.Selected = window.Model.Workspace!.State.Trip.Find(stay.Id); Dispatcher.UIThread.RunJobs(); Assert.NotEmpty(Probe.Named<StackPanel>(window, "Inspector").Children);
        Probe.Click(window, window.Model.Strings["Comments"] + " · 1"); Dispatcher.UIThread.RunJobs(); Assert.Contains(Probe.All<SelectableTextBlock>(window), t => t.Text == "Meet in the lobby"); window.Close();
    }
    [AvaloniaFact]
    public async Task ComparisonProvidesTimetableAndStructuredTabsWithMarkdownChanges()
    {
        var path = await Create(); var snapshot = (await new TravelRepository(path).ReadAsync()).Trip; var activity = Entity.Create("schedule_item", "Museum"); activity.Data["time"] = new JsonObject { ["precision"] = "exact", ["start"] = new ZonedTime("2027-05-12T10:00:00", "Europe/Berlin").ToJson(), ["end"] = new ZonedTime("2027-05-12T11:00:00", "Europe/Berlin").ToJson() };
        var incoming = snapshot with { Entities = new Dictionary<Guid, Entity> { [activity.Id] = activity }, Resources = new Dictionary<string, byte[]> { ["documents/note.md"] = Encoding.UTF8.GetBytes("Museum **tickets**") } };
        var view = new ComparisonView(snapshot, incoming, new Localization("de")); var window = new Window { Width = 1000, Height = 700, Content = view }; window.Show(); Dispatcher.UIThread.RunJobs(); Assert.Equal(2, view.ItemCount); view.SelectedIndex = 1; Dispatcher.UIThread.RunJobs(); window.Close();
    }
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DivergentSyncOffersCancellableGraceAndRecordsARealMerge(bool cancel)
    {
        var path = await Create(); var backend = new GitCliBackend(); var local = new GitRepository(new TravelRepository(path), backend);
        var remote = Path.Combine(root, "remote.git"); Directory.CreateDirectory(remote); await backend.ExecuteAsync(remote, ["init", "--bare"]); await local.AddRemoteAsync("origin", remote); await local.PushAsync("origin");
        var otherPath = Path.Combine(root, "other"); await GitRepository.CloneAsync(backend, remote, otherPath); var other = new GitRepository(new TravelRepository(otherPath), backend); await backend.ExecuteAsync(otherPath, ["switch", "main"]); await other.SetIdentityAsync("Carla", "carla@example.invalid");
        var remotePerson = Entity.Create("person", "Carla"); await other.Repository.ApplyAsync(await other.Repository.ReadAsync(), [new(remotePerson.Id, remotePerson)]); await other.CreateVersionAsync("Add Carla"); await other.PushAsync("origin");
        var localPerson = Entity.Create("person", "Alex"); await local.Repository.ApplyAsync(await local.Repository.ReadAsync(), [new(localPerson.Id, localPerson)]); await local.CreateVersionAsync("Add Alex"); var head = await local.HeadAsync();
        using var store = new LocalStore(Path.Combine(root, "local")); var interaction = new TestInteraction(); using var vm = new MainViewModel(store, backend, new OsSecretStore(), interaction) { MergeGracePeriod = TimeSpan.FromMilliseconds(120) }; await vm.OpenAsync(path);
        if (cancel) vm.PropertyChanged += (_, args) => { if (args.PropertyName == nameof(vm.Notice) && vm.Notice.Length > 0) Dispatcher.UIThread.Post(() => vm.CancelMergeCommand.Execute(null)); };
        await vm.SyncCommand.ExecuteAsync(null); Assert.Empty(interaction.Errors); Assert.Empty(vm.Notice);
        if (cancel) { Assert.Equal(head, await local.HeadAsync()); Assert.Null(vm.Workspace!.State.Trip.Find(remotePerson.Id)); }
        else { Assert.NotNull(vm.Workspace!.State.Trip.Find(remotePerson.Id)); Assert.Equal(2, (await local.HistoryAsync())[0].Parents.Split(' ').Length); Assert.Contains("TravelRepo-Action: semantic-merge", (await local.HistoryAsync())[0].Message); }
    }

    [AvaloniaTheory]
    [InlineData("Light", "Normal", false)]
    [InlineData("Dark", "Larger", true)]
    public async Task ScaledAndHighContrastWorkspacesKeepPrimaryControlsReachable(string theme, string size, bool contrast)
    {
        var path = await Create(); var window = new MainWindow(); window.Model.Store.Set("theme", theme); window.Model.Store.Set("textsize", size); window.Model.Store.Set("highcontrast", contrast ? "true" : "false"); window.Show(); await window.Model.OpenAsync(path); Dispatcher.UIThread.RunJobs();
        var inbox = Probe.All<Button>(window).First(b => Avalonia.Automation.AutomationProperties.GetName(b)?.StartsWith(window.Model.Strings["Inbox"], StringComparison.Ordinal) == true);
        Assert.True(inbox.IsEffectivelyVisible); Assert.True(window.FindControl<Grid>("WorkspaceLayout")!.Height >= 580); Assert.NotNull(window.FindControl<ContentControl>("MainContent")!.Content);
        Assert.True(Probe.Button(window, window.Model.Strings["Add"]).IsEffectivelyVisible);
        if (contrast) Assert.Equal(Avalonia.Media.Brushes.Black, window.Resources["Bg.Surface"]); window.Close();
    }

    private sealed class MemorySecrets : TravelRepo.Providers.ISecretStore
    {
        public Task<string?> ReadAsync(string key, CancellationToken ct = default) => Task.FromResult<string?>(null);
        public Task WriteAsync(string key, string value, CancellationToken ct = default) => Task.CompletedTask;
        public Task DeleteAsync(string key, CancellationToken ct = default) => Task.CompletedTask;
    }
    private sealed class PublicRepositoryHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Assert.EndsWith("/repos/example/trip", request.RequestUri!.AbsoluteUri);
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent("{\"full_name\":\"example/trip\",\"clone_url\":\"https://github.com/example/trip.git\",\"html_url\":\"https://github.com/example/trip\",\"private\":false}") });
        }
    }
    [AvaloniaFact]
    public async Task PublicGitHubRemoteWarnsOnOrdinaryOpenAndCachesPrivacy()
    {
        var path = await Create(); await new GitRepository(new TravelRepository(path), new GitCliBackend()).AddRemoteAsync("origin", "ssh://git@github.com/example/trip.git");
        using var http = new HttpClient(new PublicRepositoryHandler()); using var store = new LocalStore(Path.Combine(root, "local"));
        var provider = new TravelRepo.Providers.GitHub.GitHubProvider(http, new MemorySecrets(), "app-client"); using var vm = new MainViewModel(store, new GitCliBackend(), new OsSecretStore(), new TestInteraction(), () => provider);
        await vm.OpenAsync(path); Assert.True(vm.HasPrivacyWarning); Assert.Equal("public", store.Get("privacy:example/trip")); Assert.Equal(vm.Strings["Invite"], vm.SharingLabel);
    }

    [Fact]
    public async Task ReschedulingFlightPreservesDestinationTimezone()
    {
        var path = await Create(); using var w = new Workspace(path, new GitCliBackend()); await w.OpenAsync(); var flight = Entity.Create("schedule_item", "Tokyo flight"); flight.Data["time"] = new JsonObject { ["precision"] = "exact", ["start"] = new ZonedTime("2027-05-12T13:00:00", "Europe/Berlin").ToJson(), ["end"] = new ZonedTime("2027-05-13T08:00:00", "Asia/Tokyo").ToJson() }; await w.EditAsync(flight);
        await w.ScheduleAsync(flight.Id, new ZonedTime("2027-05-12T14:00:00", "Europe/Berlin"), Duration.FromHours(12)); Assert.Equal("Asia/Tokyo", w.State.Trip.Find(flight.Id)!.Data["time"]!["end"]!["timezone"]!.ToString());
    }

    [AvaloniaTheory]
    [InlineData("UseCurrent", "Local", 1)]
    [InlineData("UseVariant", "Incoming", 1)]
    [InlineData("KeepBoth", "Local", 2)]
    [InlineData("EditResult", "Reviewed title", 1)]
    public async Task ConflictActionsProduceReviewedVersions(string choice, string title, int count)
    {
        var path = await Create(); var backend = new GitCliBackend(); var repo = new TravelRepository(path); var git = new GitRepository(repo, backend); var item = Entity.Create("schedule_item", "Original"); await repo.ApplyAsync(await repo.ReadAsync(), [new(item.Id, item)]); await git.CreateVersionAsync("Add activity");
        await git.CreateVariantAsync("alternative", "Alternative"); var incoming = item.Copy(); incoming.Data["title"] = "Incoming"; await repo.ApplyAsync(await repo.ReadAsync(), [new(item.Id, incoming)]); await git.CreateVersionAsync("Change title"); await git.SwitchAsync("main"); var current = item.Copy(); current.Data["title"] = "Local"; await repo.ApplyAsync(await repo.ReadAsync(), [new(item.Id, current)]); await git.CreateVersionAsync("Local title");
        using var store = new LocalStore(Path.Combine(root, "local")); var interaction = new TestInteraction(); interaction.Answers.Enqueue(choice); if (choice == "EditResult") interaction.Answers.Enqueue(title);
        using var vm = new MainViewModel(store, backend, new OsSecretStore(), interaction); await vm.OpenAsync(path); await vm.VariantActionAsync((await git.VariantsAsync()).Single(v => v.Branch == "alternative"), "Merge");
        Assert.Empty(interaction.Errors); Assert.Equal(title, vm.Workspace!.State.Trip.Find(item.Id)!.Title); Assert.Equal(count, vm.Workspace.State.Trip.Entities.Count); Assert.Equal("archived", (await git.VariantsAsync()).Single(v => v.Branch == "alternative").Manifest.Data["variant"]!["state"]!.ToString());
        Assert.Equal(2, (await git.HistoryAsync())[0].Parents.Split(' ').Length);
    }

    [AvaloniaFact]
    public async Task AutosaveKeepsKeyboardFocusInInspector()
    {
        var path = await Create(); var repo = new TravelRepository(path); var item = Entity.Create("schedule_item", "Original"); await repo.ApplyAsync(await repo.ReadAsync(), [new(item.Id, item)]);
        var window = new MainWindow(); window.Show(); await window.Model.OpenAsync(path); window.Model.Selected = window.Model.Workspace!.State.Trip.Find(item.Id); Dispatcher.UIThread.RunJobs();
        var title = Probe.Tagged<TextBox>(window.FindControl<Border>("InspectorHost")!, "title"); var tags = Probe.Tagged<TextBox>(window.FindControl<Border>("InspectorHost")!, "tags");
        title.Focus(); title.Text = "Keyboard edit"; tags.Focus();
        await Probe.Until(() => window.Model.Workspace.State.Trip.Find(item.Id)!.Title == "Keyboard edit");
        Dispatcher.UIThread.RunJobs(); await Task.Delay(50); Dispatcher.UIThread.RunJobs(); Assert.Equal("tags", (window.FocusManager!.GetFocusedElement() as Control)?.Tag); window.Close();
    }

    [AvaloniaFact]
    public async Task TimetableSplitsOvernightSharedItemsWithoutDuplicatingCanonicalEntities()
    {
        var path = await Create(); var repo = new TravelRepository(path);
        var alex = Entity.Create("person", "Alex"); var carla = Entity.Create("person", "Carla");
        var item = Entity.Create("schedule_item", "Night train"); item.Data["participants"] = new JsonArray(alex.Id.ToString(), carla.Id.ToString());
        item.Data["time"] = new JsonObject { ["precision"] = "exact", ["start"] = new ZonedTime("2027-05-12T23:00:00", "Europe/Berlin").ToJson(), ["end"] = new ZonedTime("2027-05-13T03:00:00", "Europe/Berlin").ToJson() };
        await repo.ApplyAsync(await repo.ReadAsync(), [new(alex.Id, alex), new(carla.Id, carla), new(item.Id, item)]);
        var window = new MainWindow(); window.Show(); await window.Model.OpenAsync(path); var vm = window.Model; vm.View = "Plan"; vm.InboxOpen = false; vm.SetLanes(false); vm.ShowDate(new LocalDate(2027, 5, 12)); Dispatcher.UIThread.RunJobs();
        Button[] Blocks() => Probe.All<Button>(window.FindControl<ContentControl>("MainContent")!).Where(b => Equals(b.Tag, item.Id)).ToArray();
        Assert.Equal(2, Blocks().Length);
        vm.SetLanes(true); vm.ShowDate(new LocalDate(2027, 5, 13)); Dispatcher.UIThread.RunJobs();
        var blocks = Blocks(); Assert.Equal(2, blocks.Length);
        Assert.NotEqual(Canvas.GetLeft(blocks[0]), Canvas.GetLeft(blocks[1])); Assert.Single(vm.Workspace!.State.Trip.Entities.Values, e => e.Type == "schedule_item"); vm.SetLanes(false); window.Close();
    }

    private sealed class AuthorizedTestSecrets : TravelRepo.Providers.ISecretStore
    {
        public Task<string?> ReadAsync(string key, CancellationToken ct = default) => Task.FromResult<string?>("unit-test-token");
        public Task WriteAsync(string key, string value, CancellationToken ct = default) => Task.CompletedTask;
        public Task DeleteAsync(string key, CancellationToken ct = default) => Task.CompletedTask;
    }
    private sealed class PublishRepositoryHandler(string remote) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Assert.Equal(HttpMethod.Post, request.Method); Assert.EndsWith("/user/repos", request.RequestUri!.AbsoluteUri);
            var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(ct))!; Assert.True((bool)body["private"]!);
            return new HttpResponseMessage(System.Net.HttpStatusCode.Created) { Content = new StringContent(new JsonObject { ["full_name"] = "test/published-trip", ["clone_url"] = remote, ["html_url"] = "https://github.com/test/published-trip", ["private"] = true }.ToJsonString()) };
        }
    }
    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AddingRemoteImmediatelyUpdatesLibraryEvenWhenInitialPushFails(bool reachable)
    {
        var path = await Create(); var backend = new GitCliBackend(); var remote = Path.Combine(root, "publish.git");
        if (reachable) { Directory.CreateDirectory(remote); Assert.Equal(0, (await backend.ExecuteAsync(remote, ["init", "--bare"])).ExitCode); }
        using var http = new HttpClient(new PublishRepositoryHandler(remote)); using var store = new LocalStore(Path.Combine(root, "local"));
        var interaction = new TestInteraction();
        using var vm = new MainViewModel(store, backend, new OsSecretStore(), interaction, () => new TravelRepo.Providers.GitHub.GitHubProvider(http, new AuthorizedTestSecrets(), "test-client"));
        await vm.OpenAsync(path); Assert.Equal("local", store.Recent().Single().State);
        await vm.PublishToGitHubAsync("published-trip");
        Assert.Equal("remote", store.Recent().Single().State); Assert.Equal("origin", store.Get("remote:" + path)); Assert.Equal(vm.Strings["Invite"], vm.SharingLabel);
        if (reachable) { Assert.Empty(interaction.Errors); Assert.Equal(await vm.Workspace!.Git.HeadAsync(), (await backend.ExecuteAsync(remote, ["rev-parse", "main"])).Output.Trim()); }
        else Assert.Single(interaction.Errors);
    }

}
