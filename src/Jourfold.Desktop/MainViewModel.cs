using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Jourfold.Application;
using Jourfold.Infrastructure;
using NodaTime;
using NodaTime.Text;
using TravelRepo.Core;
using TravelRepo.Git;
using TravelRepo.Merge;
using TravelRepo.Providers.GitHub;
using TravelRepo.Repository;
namespace Jourfold.Desktop;

public partial class MainViewModel : ObservableObject, IDisposable
{
    public LocalStore Store { get; }
    public IInteraction Interaction { get; }
    public Localization Strings { get; private set; }
    private readonly IGitBackend backend;
    private readonly OsSecretStore secrets;
    private readonly HttpClient githubHttp = new();
    private readonly Func<GitHubProvider>? providerFactory;
    public Workspace? Workspace { get; private set; }
    public IReadOnlyList<Diagnostic> Diagnostics { get; private set; } = [];
    public bool HasTrip => Workspace is not null;
    public bool IsLibrary => !HasTrip;
    public string TripTitle => Workspace?.State.Trip.Manifest.Title ?? Strings["Recent"];
    public string SharingLabel => Strings[shareKey];
    private string shareKey = "Publish";
    public string VariantTitle => Workspace?.State.Trip.Manifest.Data["variant"]?["title"]?.ToString() ?? "";
    public IReadOnlyList<string> Navigation { get; } = ["Plan", "Map", "List", "Bookings", "Costs", "Tasks", "Files", "People", "Places", "Collections", "History", "Variants"];
    public ObservableCollection<Entity> Items { get; } = new();
    public ObservableCollection<Entity> InboxItems { get; } = new();
    public ObservableCollection<RecentTrip> Recent { get; } = new();
    [ObservableProperty] private Entity? selected;
    [ObservableProperty] private string view = "Plan";
    [ObservableProperty] private string status = "";
    [ObservableProperty] private bool inboxOpen;
    [ObservableProperty] private bool inboxPinned;
    [ObservableProperty] private bool advanced;
    [ObservableProperty] private string notice = "";
    [ObservableProperty] private bool busy;
    [ObservableProperty] private string privacyWarning = "";
    public bool HasPrivacyWarning => PrivacyWarning.Length > 0;
    partial void OnPrivacyWarningChanged(string value) => OnPropertyChanged(nameof(HasPrivacyWarning));
    public LocalDate StartDate { get; set; } = SystemClock.Instance.GetCurrentInstant().InUtc().Date;
    public double Zoom { get; set; } = 1;
    public bool Lanes { get; set; }
    public event EventHandler? ContentChanged;
    public event Action<string>? OpenRequested;
    public MainViewModel(LocalStore store, IGitBackend backend, OsSecretStore secrets, IInteraction interaction, Func<GitHubProvider>? providerFactory = null)
    {
        Store = store; this.providerFactory = providerFactory; this.backend = backend; this.secrets = secrets; Interaction = interaction; Strings = new(store.Get("language") ?? CultureInfo.CurrentUICulture.TwoLetterISOLanguageName);
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(Strings.Language == "de" ? "de-DE" : "en-US");
        InboxPinned = Store.Get("inbox.pinned") == "true"; InboxOpen = InboxPinned; Advanced = Store.Get("advanced") == "true"; foreach (var r in Store.Recent()) Recent.Add(r);
    }
    partial void OnViewChanged(string value) { Store.Set("lastView", value); Refresh(); }
    partial void OnSelectedChanged(Entity? value) { }
    partial void OnInboxOpenChanged(bool value) => ContentChanged?.Invoke(this, EventArgs.Empty);
    partial void OnInboxPinnedChanged(bool value) { Store.Set("inbox.pinned", value ? "true" : "false"); if (value) InboxOpen = true; }
    partial void OnAdvancedChanged(bool value) => Store.Set("advanced", value ? "true" : "false");
    private readonly SemaphoreSlim operations = new(1, 1);
    private sealed class OperationScope { public bool Active = true; }
    private readonly AsyncLocal<OperationScope?> operationScope = new();
    [ObservableProperty] private bool editing;
    public bool CanInteract => !Busy || Editing;
    partial void OnBusyChanged(bool value) => OnPropertyChanged(nameof(CanInteract));
    partial void OnEditingChanged(bool value) => OnPropertyChanged(nameof(CanInteract));
    public Task RunAsync(Func<Task> action) => ExecuteAsync(action, false);
    public Task RunEditAsync(Func<Task> action) => ExecuteAsync(action, true);
    private async Task ExecuteAsync(Func<Task> action, bool edit)
    {
        if (operationScope.Value?.Active == true) { await action(); return; }
        await operations.WaitAsync(); var scope = new OperationScope(); operationScope.Value = scope;
        Editing = edit; Busy = true;
        try { await action(); }
        catch (Exception ex) when (ex is DomainException or IOException or UnauthorizedAccessException or HttpRequestException or FormatException or InvalidOperationException or System.Text.Json.JsonException)
        { Status = Strings["Error"]; await Interaction.ShowAsync(Strings["Error"], Strings.Error(ex, Advanced)); }
        finally { Busy = false; Editing = false; scope.Active = false; operationScope.Value = null; operations.Release(); }
    }
    public async Task OpenAsync(string path)
    {
        var next = new Workspace(path, backend);
        try { await next.OpenAsync(); if (next.State.Diagnostics.Any(d => d.Severity == Severity.Error)) throw new DomainException("repository.invalid", string.Join("\n", next.State.Diagnostics.Select(d => d.Path + ": " + d.Code))); }
        catch (DomainException ex)
        {
            try
            {
                await Interaction.ShowAsync(Strings["Error"], Strings.Error(ex, Advanced));
                var target = await next.Git.SnapshotAsync("HEAD"); var repair = await next.Repository.PreviewRestoreAsync(target);
                if (!await Interaction.ConfirmAsync(Strings["Rollback"] + "\n" + string.Join('\n', repair.Changes.Select(c => c.Path)))) { next.Dispose(); return; }
                await next.Repository.ApplyRestoreAsync(repair); await next.OpenAsync();
            }
            catch { next.Dispose(); throw; }
        }
        catch { next.Dispose(); throw; }
        Workspace?.Dispose(); Workspace = next; next.Changed += (_, _) => Refresh();
        Store.Remember(path, next.State.Trip, (await next.Git.RemotesAsync()).Count > 0 ? "remote" : "local");
        var remotes = await next.Git.RemotesAsync(); shareKey = remotes.Count == 0 ? "Publish" : remotes.Values.Any(IsGitHubUrl) ? "Invite" : "Share"; OnPropertyChanged(nameof(SharingLabel));
        Store.Index(path, next.State.Trip); _ = RefreshPrivacyAsync(remotes.Values); OnPropertyChanged(nameof(HasTrip)); OnPropertyChanged(nameof(IsLibrary));
        View = Store.Get("openingView") is { } preferred && preferred != "Remember" ? preferred : Store.Get("lastView") ?? "Plan";
        var first = next.State.Trip.Entities.Values.Select(e => e.Data["time"]?["start"]?["local"]?.ToString() ?? e.Data["time"]?["date"]?.ToString()).Where(s => s is not null).Order().FirstOrDefault();
        if (first?.Length >= 10 && LocalDatePattern.Iso.Parse(first[..10]).TryGetValue(default, out var date)) StartDate = date;
        Refresh(); Status = string.IsNullOrEmpty(await next.Git.StatusAsync()) ? Strings["Saved"] : Strings["Changed"];
    }
    public void Refresh()
    {
        OnPropertyChanged(nameof(TripTitle)); OnPropertyChanged(nameof(VariantTitle));
        if (Workspace is null) return;
        Diagnostics = Workspace.State.Diagnostics.Concat(Workspace.State.Diagnostics.Any(d => d.Severity == Severity.Error) ? [] : SemanticValidation.Validate(Workspace.State.Trip, Store.TravelEstimates(Workspace.Repository.Root)).Where(d => d.Code == "plan.travel_gap")).ToArray();
        var entities = Workspace.State.Trip.Entities.Values;
        var type = View switch { "Bookings" => "booking", "Costs" => "expense", "Tasks" => "task", "Files" => "document", "People" => "person", "Places" => "place", "Collections" => "collection", "Map" => "place", _ => "schedule_item" };
        Items.Clear(); foreach (var e in entities.Where(e => View == "List" || e.Type == type).OrderBy(e => e.Data["time"]?["start"]?["local"]?.ToString() ?? "~").ThenBy(e => e.Title)) Items.Add(e);
        InboxItems.Clear(); foreach (var e in entities.Where(e => e.Type is "note" or "document" && !entities.Any(other => other.Id != e.Id && other.Data["content"] is JsonArray content && content.Any(c => (c?["document"] ?? c?["entity"])?.ToString() == e.Id.ToString())) || e.Type == "schedule_item" && (e.Data["time"] is null || e.Data["time"]?["precision"]?.ToString() == "unscheduled"))) InboxItems.Add(e);
        if (Selected is not null) Selected = Workspace.State.Trip.Find(Selected.Id);
        Status = Workspace.State.Diagnostics.Any(d => d.Severity == Severity.Error) ? Strings["Error"] + ": " + string.Join("; ", Workspace.State.Diagnostics.Where(d => d.Severity == Severity.Error).Select(d => d.Code)) : Strings["Saved"]; ContentChanged?.Invoke(this, EventArgs.Empty);
    }
    [RelayCommand]
    private Task NewTrip() => RunAsync(async () =>
    {
        var draft = await Interaction.NewTripAsync(); if (draft is null) return; var title = draft.Title;
        var path = draft.Destination ?? await Interaction.FolderAsync(); if (path is null) return;
        if (Directory.Exists(path) && Directory.EnumerateFileSystemEntries(path).Any()) throw new DomainException("folder.not_empty", Strings["EmptyFolderRequired"]);
        var repo = new TravelRepository(path); var manifest = Entity.CreateTrip(title, draft.Language, draft.Timezone);
        if (draft.Start is not null || draft.End is not null) manifest.Data["dates"] = new JsonObject { ["start"] = draft.Start, ["end"] = draft.End };
        manifest.Data["variant"]!["title"] = Strings["CurrentPlan"];
        var semantic = SemanticValidation.Validate(new(manifest, new Dictionary<Guid, Entity>())); if (semantic.Any(d => d.Severity == Severity.Error)) throw new DomainException("trip.invalid", string.Join("; ", semantic.Select(d => d.Message)));
        await repo.InitializeAsync(manifest);
        if (draft.Participants.Length > 0)
        {
            var people = draft.Participants.Select(name => Entity.Create("person", name)).ToArray(); var state = await repo.ReadAsync(); var updated = manifest.Copy(); updated.Data["participants"] = new JsonArray(people.Select(p => (JsonNode?)JsonValue.Create(p.Id.ToString())).ToArray());
            await repo.ApplyAsync(state, people.Select(p => new EntityEdit(p.Id, p)).Append(new(updated.Id, updated)).ToArray());
        }
        await new GitRepository(repo, backend).InitializeAsync(Store.Get("identity.name") ?? Environment.UserName, Store.Get("identity.email") ?? "traveler@localhost");
        if (draft.RemoteUrl is not null) { await new GitRepository(repo, backend).AddRemoteAsync("origin", draft.RemoteUrl); Store.Set("remote:" + path, "origin"); }
        if (OpenRequested is not null) OpenRequested(path); else await OpenAsync(path);
    });
    [RelayCommand] private Task OpenTrip() => RunAsync(async () => { var path = await Interaction.FolderAsync(); if (path is not null) { if (OpenRequested is not null) OpenRequested(path); else await OpenAsync(path); } });
    [RelayCommand] private Task Clone() => RunAsync(async () => { var url = await Interaction.PromptAsync(Strings["RemoteUrl"]); if (url is null) return; var folder = await Interaction.FolderAsync(); if (folder is null) return; await GitRepository.CloneAsync(backend, url, folder); OpenRequested?.Invoke(folder); });
    public void OpenRecent(RecentTrip recent) => OpenRequested?.Invoke(recent.Path);
    public void Navigate(string key) { View = key; }
    [RelayCommand]
    private Task QuickAdd() => RunAsync(async () =>
    {
        if (Workspace is null) return;
        var type = await Interaction.ChooseAsync(Strings["QuickAdd"], new[] { "activity", "transport", "accommodation", "task", "note", "booking", "place", "person", "expense", "collection", "comment", "budget" }.Select(k => new Choice(k, Strings[k])).ToArray());
        if (type is not null) await AddAsync(type);
    });
    public async Task AddAsync(string kind)
    {
        if (Workspace is null) return;
        var title = await Interaction.PromptAsync(Strings["Title"]); if (string.IsNullOrWhiteSpace(title)) return;
        var type = kind is "activity" or "transport" or "accommodation" ? "schedule_item" : kind; var e = Entity.Create(type, title);
        if (kind == "transport") { var transport = await Interaction.ChooseAsync(Strings["transport"], new[] { "flight", "train", "bus", "car", "taxi", "rideshare", "ferry", "ship", "bicycle", "walking", "other" }.Select(k => new Choice(k, Strings[k])).ToArray()); if (transport is null) return; e.Data["components"]!["transport"] = new JsonObject { ["type"] = transport }; }
        if (kind == "accommodation") e.Data["components"]!["accommodation"] = new JsonObject();
        if (type is "expense" or "budget") { e.Data[type == "expense" ? "amount" : "amount"] = new JsonObject { ["value"] = "0.00", ["currency"] = "EUR" }; if (type == "expense") e.Data["estimated"] = true; }
        if (type == "collection") e.Data["items"] = new JsonArray();
        if (type == "comment")
        {
            if (Selected is null) return;
            var author = await Interaction.ChooseAsync(Strings["Author"], Workspace.State.Trip.Entities.Values.Where(x => x.Type == "person").Select(x => new Choice(x.Id.ToString(), x.Title)).ToArray()); if (author is null) return;
            e.Data["author"] = author; e.Data["target"] = new JsonObject { ["id"] = Selected.Id.ToString(), ["type"] = Selected.Type }; e.Data["body"] = title; e.Data["created_at"] = SystemClock.Instance.GetCurrentInstant().ToString();
        }
        await Workspace.EditAsync(e); Selected = e; if (type != "schedule_item" && View is "Plan" or "Map") View = type switch { "person" => "People", "place" => "Places", "booking" => "Bookings", "task" => "Tasks", "expense" or "budget" => "Costs", "collection" => "Collections", "document" => "Files", _ => "List" };
    }
    [RelayCommand]
    private Task AddIdentity() => RunAsync(async () =>
    {
        if (Workspace is null) return;
        var name = Store.Get("identity.name"); var email = Store.Get("identity.email");
        if (name is null || email is null) { name = await Interaction.PromptAsync(Strings["Name"], Environment.UserName); if (name is null) return; email = await Interaction.PromptAsync(Strings["Email"]); if (string.IsNullOrWhiteSpace(email)) return; Store.Set("identity.name", name); Store.Set("identity.email", email); }
        var person = Workspace.State.Trip.Entities.Values.FirstOrDefault(p => p.Type == "person" && p.Data["identities"]?["git"] is JsonArray identities && identities.Any(i => string.Equals(i?["email"]?.ToString(), email, StringComparison.OrdinalIgnoreCase)))?.Copy() ?? Entity.Create("person", name);
        person.Data["identities"] ??= new JsonObject(); person.Data["identities"]!["git"] ??= new JsonArray(new JsonObject { ["name"] = name, ["email"] = email });
        var manifest = Workspace.State.Trip.Manifest.Copy(); var participants = manifest.Data["participants"] as JsonArray ?? []; if (manifest.Data["participants"] is null) manifest.Data["participants"] = participants; if (!participants.Any(p => p?.ToString() == person.Id.ToString())) participants.Add(person.Id.ToString());
        await Workspace.ApplyAsync([new(person.Id, person), new(manifest.Id, manifest)]); await Workspace.Git.SetIdentityAsync(name, email); Selected = person;
    });
    [RelayCommand] private Task Undo() => RunAsync(async () => { if (Workspace is not null) await Workspace.UndoAsync(); });
    [RelayCommand] private Task Redo() => RunAsync(async () => { if (Workspace is not null) await Workspace.RedoAsync(); });
    [RelayCommand] private Task Delete() => RunAsync(async () => { if (Workspace is not null && Selected is not null && await Interaction.ConfirmAsync(Strings["DeleteConfirm"])) await Workspace.DeleteAsync(Selected.Id); });
    [RelayCommand]
    private Task CreateVersion() => RunAsync(async () =>
    {
        if (Workspace is null) return; var baseline = await Workspace.Git.SnapshotAsync("HEAD"); var changes = SemanticMerge.Diff(baseline, Workspace.State.Trip);
        var summary = string.Join('\n', changes.Select(c => (Workspace.State.Trip.Find(c.Entity)?.Title ?? baseline.Find(c.Entity)?.Title) + ": " + c.Path));
        await Interaction.ShowAsync(Strings["Review"], summary);
        var ids = changes.Select(c => c.Entity).Distinct().ToArray();
        var suggestion = Strings["VersionSuggestion"];
        if (ids.Length == 1 && ids[0] != Guid.Empty) { var before = baseline.Find(ids[0]); var after = Workspace.State.Trip.Find(ids[0]); suggestion = string.Format(Strings[before is null ? "VersionAdd" : after is null ? "VersionRemove" : "VersionUpdate"], (after ?? before)!.Title); }
        else if (ids.Length > 1) suggestion = string.Format(Strings["VersionMany"], ids.Length);
        var message = await Interaction.PromptAsync(Strings["VersionMessage"], suggestion); if (message is null) return;
        await Workspace.Git.CreateVersionAsync(message); Status = Strings["Saved"];
    });
    [RelayCommand] private Task NewVariant() => RunAsync(async () => { if (Workspace is null) return; var title = await Interaction.PromptAsync(Strings["VariantTitle"]); if (title is null) return; await Workspace.Git.CreateVariantAsync("variants/" + Guid.CreateVersion7(), title); await Workspace.ReloadAsync(); Refresh(); });
    public Task VariantActionAsync(Variant variant, string action) => RunAsync(async () =>
    {
        if (Workspace is null) return;
        if (action == "ShareVariant") { await ShareVariantAsync(variant); return; }
        if (action == "Switch") { await Workspace.Git.SwitchAsync(variant.Branch); await Workspace.ReloadAsync(); Refresh(); return; }
        if (action == "Compare") { var other = await Workspace.Git.SnapshotAsync(variant.Branch); await Interaction.CompareAsync(Workspace.State.Trip, other); return; }
        if (action == "Merge") { await MergeAsync(variant.Branch); return; }
        var current = await Workspace.Git.CurrentBranchAsync();
        await Workspace.Git.SwitchAsync(variant.Branch);
        if (action == "Archive") await Workspace.Git.UpdateVariantAsync(archive: true);
        if (action == "Rename") { var title = await Interaction.PromptAsync(Strings["VariantTitle"], variant.Manifest.Data["variant"]!["title"]!.ToString()); if (title is not null) await Workspace.Git.UpdateVariantAsync(title); }
        if (current != variant.Branch) await Workspace.Git.SwitchAsync(current);
        await Workspace.ReloadAsync(); Refresh();
    });
    private async Task MergeAsync(string branch)
    {
        if (Workspace is null) return; var baseline = await Workspace.Git.SnapshotAsync(await Workspace.Git.MergeBaseAsync(branch)); var incoming = await Workspace.Git.SnapshotAsync(branch); var resolutions = new Dictionary<string, JsonNode?>(); var duplicates = new Dictionary<Guid, Entity>(); var keptBoth = new HashSet<Guid>();
        var plan = SemanticMerge.Plan(baseline, Workspace.State.Trip, incoming);
        foreach (var conflict in plan.Conflicts)
        {
            if (keptBoth.Contains(conflict.Entity)) { resolutions[conflict.Entity + conflict.Path] = conflict.Current?.DeepClone(); continue; }
            var title = Workspace.State.Trip.Find(conflict.Entity)?.Title ?? incoming.Find(conflict.Entity)?.Title ?? Strings["Files"];
            var description = conflict.Kind == ConflictKind.Binary ? Strings["BinaryConflict"] : Strings["UseCurrent"] + ": " + DescribeValue(conflict.Current, Workspace.State.Trip) + "\n" + Strings["UseVariant"] + ": " + DescribeValue(conflict.Incoming, incoming);
            var actions = conflict.Entity == Guid.Empty && conflict.Kind == ConflictKind.Binary ? new[] { "UseCurrent", "UseVariant" } : conflict.Entity != Guid.Empty && incoming.Find(conflict.Entity) is { Type: not "trip" } ? new[] { "UseCurrent", "UseVariant", "KeepBoth", "EditResult" } : new[] { "UseCurrent", "UseVariant", "EditResult" };
            var choice = await Interaction.ChooseAsync(title + "\n" + description, actions.Select(k => new Choice(k, Strings[k])).ToArray());
            if (choice is null) return;
            if (choice == "KeepBoth")
            {
                keptBoth.Add(conflict.Entity); foreach (var duplicate in TripCommands.DuplicateSubtree(incoming, conflict.Entity)) duplicates[duplicate.Id] = duplicate;
                resolutions[conflict.Entity + conflict.Path] = conflict.Current?.DeepClone(); continue;
            }
            if (choice == "EditResult") { var text = await Interaction.PromptAsync(Strings["EditResult"], conflict.Current?.ToString() ?? "", true); if (text is null) return; resolutions[conflict.Entity + conflict.Path] = conflict.Current is JsonObject or JsonArray || conflict.Incoming is JsonObject or JsonArray ? JsonNode.Parse(text) : JsonValue.Create(text); }
            else resolutions[conflict.Entity + conflict.Path] = (choice == "UseCurrent" ? conflict.Current : conflict.Incoming)?.DeepClone();
        }
        plan = SemanticMerge.Plan(baseline, Workspace.State.Trip, incoming, resolutions);
        if (duplicates.Count > 0) { var all = plan.Result.Entities.ToDictionary(p => p.Key, p => p.Value); foreach (var duplicate in duplicates) all[duplicate.Key] = duplicate.Value; plan = plan with { Result = plan.Result with { Entities = all } }; }
        await Workspace.ApplyMergeAsync(branch, plan);
        var current = await Workspace.Git.CurrentBranchAsync();
        if ((await Workspace.Git.VariantsAsync()).Any(v => v.Branch == branch && !v.IsRemote && v.Manifest.Data["variant"]?["role"]?.ToString() == "variant")) { await Workspace.Git.SwitchAsync(branch); await Workspace.Git.UpdateVariantAsync(archive: true); await Workspace.Git.SwitchAsync(current); await Workspace.ReloadAsync(); }
        Refresh(); Status = Strings["Merged"];
    }
    private string DescribeValue(JsonNode? value, TripSnapshot trip)
    {
        if (value is null) return Strings["Removed"];
        if (value is JsonArray array) return string.Join(", ", array.Select(v => DescribeValue(v, trip)));
        if (value is JsonObject obj) return string.Join("; ", obj.Where(p => p.Key is not ("id" or "extensions")).Select(p => p.Key + ": " + DescribeValue(p.Value, trip)));
        return Guid.TryParse(value.ToString(), out var id) && trip.Find(id) is { } entity ? entity.Title : value.ToString();
    }
    [RelayCommand]
    private Task AddFile() => RunAsync(async () =>
    {
        if (Workspace is null) return; var path = await Interaction.FileAsync(); if (path is null) return;
        var large = new FileInfo(path).Length > 25 * 1024 * 1024; if (large && !await Interaction.ConfirmAsync(Strings["LargeFile"])) return;
        var media = Path.GetExtension(path).ToLowerInvariant() switch { ".png" => "image/png", ".jpg" or ".jpeg" => "image/jpeg", ".pdf" => "application/pdf", _ => "application/octet-stream" };
        var doc = await Workspace.ImportDocumentAsync(path, media, large); View = "Files"; Selected = doc;
    });
    [RelayCommand]
    private Task Export() => RunAsync(async () =>
    {
        if (Workspace is null) return; var format = await Interaction.ChooseAsync(Strings["ExportFormat"], [new("pdf", Strings["PDF"]), new("ics", Strings["ICS"]), new("html", Strings["HTML"])]); if (format is null) return;
        var path = await Interaction.FileAsync(true, format); if (path is null) return;
        if (format == "pdf") PdfExport.Write(Workspace.State.Trip, path, Path.Combine(AppContext.BaseDirectory, "fonts", "PlusJakartaSans-Regular.ttf"));
        else await File.WriteAllTextAsync(path, format == "ics" ? TripExport.Ics(Workspace.State.Trip) : TripExport.Html(Workspace.State.Trip));
        Status = path;
    });
    [RelayCommand]
    private Task AddRemote() => RunAsync(async () =>
    {
        if (Workspace is null) return; var name = await Interaction.PromptAsync(Strings["Remote"], "origin"); if (name is null) return; var url = await Interaction.PromptAsync(Strings["RemoteUrl"]); if (url is null) return;
        await Workspace.Git.AddRemoteAsync(name, url); await RememberRemoteAsync(name, IsGitHubUrl(url));
    });
    private async Task RememberRemoteAsync(string name, bool github)
    {
        if (Workspace is null) return;
        Store.Set("remote:" + Workspace.Repository.Root, name);
        Store.Remember(Workspace.Repository.Root, Workspace.State.Trip, "remote");
        shareKey = github ? "Invite" : "Share"; OnPropertyChanged(nameof(SharingLabel));
        _ = RefreshPrivacyAsync((await Workspace.Git.RemotesAsync()).Values);
    }
    private async Task<string?> PreferredRemote()
    {
        if (Workspace is null) return null; var remotes = await Workspace.Git.RemotesAsync(); var preferred = Store.Get("remote:" + Workspace.Repository.Root); if (preferred is not null && remotes.ContainsKey(preferred)) return preferred;
        if (remotes.Count == 0) return null;
        preferred = remotes.Count == 1 ? remotes.Keys.First() : await Interaction.ChooseAsync(Strings["Remote"], remotes.Select(p => new Choice(p.Key, p.Key)).ToArray());
        if (preferred is not null) Store.Set("remote:" + Workspace.Repository.Root, preferred); return preferred;
    }
    public TimeSpan MergeGracePeriod { get; init; } = TimeSpan.FromSeconds(10);
    [RelayCommand]
    private Task Sync() => RunAsync(() => SynchronizeAsync(false));
    private async Task SynchronizeAsync(bool background)
    {
        if (Workspace is null) return;
        var remotes = await Workspace.Git.RemotesAsync();
        var remote = background ? Store.Get("remote:" + Workspace.Repository.Root) ?? (remotes.Count == 1 ? remotes.Keys.First() : null) : await PreferredRemote();
        if (remote is null) { if (!background) await Interaction.ShowAsync(Strings["Sync"], Strings["AddRemote"]); return; }
        var state = await Workspace.Git.SyncAsync(remote); await Workspace.ReloadAsync(); Refresh();
        if (state == SyncState.Diverged)
        {
            var branch = "refs/remotes/" + remote + "/" + await Workspace.Git.CurrentBranchAsync(); var plan = await Workspace.CompareMergeAsync(branch);
            if (plan.CanApply)
            {
                Notice = Strings["MergeNotice"]; mergeCancellation?.Dispose(); mergeCancellation = new();
                try { await Task.Delay(MergeGracePeriod, mergeCancellation.Token); await Workspace.ApplyMergeAsync(branch, plan); await Workspace.Git.PushAsync(remote); Refresh(); Status = Strings["Merged"]; }
                catch (OperationCanceledException) { Status = Strings["MergeCancelled"]; }
                finally { Notice = ""; }
            }
            else if (background) Status = Strings["SyncConflict"];
            else if (plan.Conflicts.Count > 0) await MergeAsync(branch);
            else await Interaction.ShowAsync(Strings["Review"], string.Join('\n', plan.Diagnostics.Select(d => Strings.Diagnostic(d.Code))));
            return;
        }
        Status = state == SyncState.LocalChanges ? Strings["Changed"] : Strings["Saved"]; Store.Set("sync:" + Workspace.Repository.Root, state == SyncState.LocalChanges ? "Changed" : "Saved");
    }
    private CancellationTokenSource? mergeCancellation;
    [RelayCommand] private void CancelMerge() { mergeCancellation?.Cancel(); Notice = ""; }
    [RelayCommand] private async Task ReviewMerge() { mergeCancellation?.Cancel(); if (Workspace is not null && await PreferredRemote() is { } remote) { var plan = await Workspace.CompareMergeAsync("refs/remotes/" + remote + "/" + await Workspace.Git.CurrentBranchAsync()); await Interaction.ShowAsync(Strings["Review"], string.Join('\n', SemanticMerge.Diff(Workspace.State.Trip, plan.Result).Select(c => c.Path))); } }
    private async Task RefreshPrivacyAsync(IEnumerable<string> remotes)
    {
        PrivacyWarning = ""; var workspace = Workspace;
        foreach (var url in remotes.Where(IsGitHubUrl))
        {
            var name = GitHubName(url);
            if (Store.Get("privacy:" + name) == "public") PrivacyWarning = Strings["PublicWarning"];
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5)); var repository = await GitHub().InspectAsync(name, timeout.Token);
                if (Workspace != workspace) return; Store.Set("privacy:" + name, repository.IsPrivate ? "private" : "public"); if (!repository.IsPrivate) PrivacyWarning = Strings["PublicWarning"];
            }
            catch (Exception ex) when (ex is DomainException or HttpRequestException or OperationCanceledException) { }
        }
    }
    private static bool IsGitHubUrl(string url) => url.StartsWith("git@github.com:", StringComparison.Ordinal) || Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase);
    private static string GitHubName(string url) { var name = url.StartsWith("git@github.com:", StringComparison.Ordinal) ? url[15..] : new Uri(url).AbsolutePath.Trim('/'); return name.EndsWith(".git", StringComparison.Ordinal) ? name[..^4] : name; }
    private GitHubProvider GitHub() => providerFactory?.Invoke() ?? new(githubHttp, secrets, Store.Get("github.client") ?? "");
    [RelayCommand]
    private Task ConnectGitHub() => RunAsync(async () =>
    {
        if (Store.Get("github.client") is null) { var id = await Interaction.PromptAsync(Strings["GitHubClient"]); if (id is null) return; Store.Set("github.client", id); }
        var provider = GitHub(); var code = await provider.BeginAsync(); Interaction.Open(code.VerificationUri); await Interaction.ShowAsync(Strings["GitHubCode"], code.UserCode); await provider.CompleteAsync(code); if (secrets.SessionOnly) await Interaction.ShowAsync(Strings["ConnectGitHub"], Strings["SessionOnly"]);
    });
    [RelayCommand]
    private Task DiscoverGitHub() => RunAsync(async () =>
    {
        var repos = await GitHub().DiscoverAsync(); var choice = await Interaction.ChooseAsync(Strings["DiscoverGitHub"], repos.Select(r => new Choice(r.FullName, r.FullName)).ToArray()); if (choice is null) return; var repo = repos.Single(r => r.FullName == choice);
        if (!repo.IsPrivate && !await Interaction.ConfirmAsync(Strings["PublicWarning"])) return;
        var folder = await Interaction.FolderAsync(); if (folder is null) return; await GitRepository.CloneAsync(backend, repo.CloneUrl, folder); OpenRequested?.Invoke(folder);
    });
    public async Task ShareVariantAsync(Variant variant)
    {
        if (Workspace is null || await PreferredRemote() is not { } remote) { await Interaction.ShowAsync(Strings["ShareVariant"], Strings["PublishFirst"]); return; }
        var url = (await Workspace.Git.RemotesAsync())[remote];
        var branch = variant.IsRemote ? variant.Branch[(variant.Branch.IndexOf('/') + 1)..] : variant.Branch;
        var text = Strings["ShareNotice"] + "\n\n" + url + "\n" + variant.Manifest.Data["variant"]?["title"] + "\n" + new ShareLink(url, branch).ToUri();
        await Interaction.ShowAsync(Strings["ShareVariant"], text); await Interaction.CopyAsync(text);
    }
    public Task OpenLinkAsync(string link) => RunAsync(async () =>
    {
        var context = ShareLink.Parse(link);
        if (!await Interaction.ConfirmAsync(Strings["CloneLink"] + "\n" + context.Remote)) return;
        var folder = await Interaction.FolderAsync(); if (folder is null) return;
        await GitRepository.CloneAsync(backend, context.Remote, folder);
        if (context.Branch is not null)
        {
            var git = new GitRepository(new TravelRepository(folder), backend); var variants = await git.VariantsAsync();
            var variant = variants.FirstOrDefault(v => v.Branch == context.Branch) ?? variants.FirstOrDefault(v => v.Branch == "origin/" + context.Branch);
            if (variant is null) throw new DomainException("share.variant", "The shared variant is not available on this remote.");
            await git.SwitchAsync(variant.Branch);
        }
        if (OpenRequested is not null) OpenRequested(folder); else await OpenAsync(folder);
    });
    [RelayCommand]
    private Task Share() => RunAsync(async () =>
    {
        if (Workspace is null) return; var remote = await PreferredRemote();
        if (remote is null)
        {
            var name = await Interaction.PromptAsync(Strings["PrivateName"]); if (name is null) return; var created = await GitHub().CreateAsync(name); await Workspace.Git.AddRemoteAsync("origin", created.CloneUrl); await RememberRemoteAsync("origin", true); await Workspace.Git.PushAsync("origin"); return;
        }
        var url = (await Workspace.Git.RemotesAsync())[remote];
        if (IsGitHubUrl(url))
        {
            var name = GitHubName(url);
            var repo = await GitHub().InspectAsync(name); if (!repo.IsPrivate && !await Interaction.ConfirmAsync(Strings["PublicWarning"])) return;
            var user = await Interaction.PromptAsync(Strings["Username"]); if (user is not null) await GitHub().InviteAsync(name, user);
        }
        else { GitRepository.ValidateRemote(url); var text = Strings["ShareNotice"] + "\n\n" + url + "\n" + new ShareLink(url, await Workspace.Git.CurrentBranchAsync()).ToUri(); await Interaction.ShowAsync(Strings["Share"], text); await Interaction.CopyAsync(text); }
    });
    [RelayCommand]
    private Task Search() => RunAsync(async () =>
    {
        var query = await Interaction.PromptAsync(Strings["Search"]); if (query is null) return;
        var choices = Navigation.Where(k => Strings[k].Contains(query, StringComparison.CurrentCultureIgnoreCase)).Select(k => new Choice("view:" + k, Strings[k])).ToList();
        choices.AddRange(new[] { "QuickAdd", "Version", "NewVariant", "Export", "Sync", "Undo", "Redo" }.Where(k => Strings[k].Contains(query, StringComparison.CurrentCultureIgnoreCase)).Select(k => new Choice("command:" + k, Strings[k])));
        if (Workspace is not null) { Store.Index(Workspace.Repository.Root, Workspace.State.Trip); choices.AddRange(Store.Search(Workspace.Repository.Root, query).Select(id => new Choice(id.ToString(), Workspace.State.Trip.Find(id)?.Title ?? ""))); choices.AddRange((await Workspace.Git.VariantsAsync()).Where(v => v.Manifest.Data["variant"]!["title"]!.ToString().Contains(query, StringComparison.CurrentCultureIgnoreCase)).Select(v => new Choice("variant:" + v.Branch, v.Manifest.Data["variant"]!["title"]!.ToString()))); }
        var selected = await Interaction.ChooseAsync(Strings["SearchTitle"], choices); if (selected is null) return;
        if (selected.StartsWith("view:", StringComparison.Ordinal)) View = selected[5..];
        else if (selected.StartsWith("variant:", StringComparison.Ordinal) && Workspace is not null) { await Workspace.Git.SwitchAsync(selected[8..]); await Workspace.ReloadAsync(); Refresh(); }
        else if (selected.StartsWith("command:", StringComparison.Ordinal)) { Busy = false; var command = selected[8..] switch { "QuickAdd" => QuickAddCommand, "Version" => CreateVersionCommand, "NewVariant" => NewVariantCommand, "Export" => ExportCommand, "Sync" => SyncCommand, "Undo" => UndoCommand, _ => RedoCommand }; await command.ExecuteAsync(null); }
        else if (Guid.TryParse(selected, out var id)) { View = "List"; Selected = Workspace?.State.Trip.Find(id); }
    });
    [RelayCommand]
    private Task Settings() => Interaction.SettingsAsync(this);
    public Task SettingsActionAsync(string key) => RunAsync(async () =>
    {
        string[]? choices = key switch { "Theme" => ["Light", "Dark", "System"], "Density" => ["Comfortable", "Compact"], "Language" => ["en", "de"], "OpenView" => ["Remember", "Plan", "Map", "List"], "Units" => ["Metric", "Imperial"], "TextSize" => ["Normal", "Large", "Larger"], _ => null };
        if (choices is not null) { var value = await Interaction.ChooseAsync(Strings[key], choices.Select(k => new Choice(k, Strings[k])).ToArray()); if (value is null) return; SetPreference(key, value); return; }
        if (key == "HighContrast") { Store.Set("highcontrast", Store.Get("highcontrast") == "true" ? "false" : "true"); ContentChanged?.Invoke(this, EventArgs.Empty); }
        if (key == "Advanced") { Advanced = !Advanced; ContentChanged?.Invoke(this, EventArgs.Empty); }
        if (key == "LocalIdentity") { var name = await Interaction.PromptAsync(Strings["Name"], Store.Get("identity.name") ?? Environment.UserName); var email = await Interaction.PromptAsync(Strings["Email"], Store.Get("identity.email") ?? "traveler@localhost"); if (name is not null && email is not null) { Store.Set("identity.name", name); Store.Set("identity.email", email); if (Workspace is not null) await Workspace.Git.SetIdentityAsync(name, email); } }
        if (key == "PreferredRemote" && Workspace is not null) { var remotes = await Workspace.Git.RemotesAsync(); var name = await Interaction.ChooseAsync(Strings["PreferredRemote"], remotes.Keys.Select(k => new Choice(k, k)).ToArray()); if (name is not null) Store.Set("remote:" + Workspace.Repository.Root, name); }
        if (key == "Diagnostics" && Workspace is not null)
        {
            var remotes = await Workspace.Git.RemotesAsync();
            string SafeUrl(string url) => Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" ? new UriBuilder(uri) { UserName = "", Password = "", Query = "", Fragment = "" }.Uri.ToString() : url;
            var extensions = Workspace.State.Trip.All.SelectMany(e => (e.Data["extensions"] as JsonObject ?? []).Select(p => p.Key).Concat((e.Data["components"] as JsonObject ?? []).Where(p => p.Key.Contains('.')).Select(p => p.Key))).Distinct();
            await Interaction.ShowAsync(Strings["Diagnostics"], Workspace.Repository.Root + "\n" + await Workspace.Git.CurrentBranchAsync() + "\n" + await Workspace.Git.HeadAsync() + "\n" + await Workspace.Git.StatusAsync() + "\n" + string.Join('\n', remotes.Select(r => r.Key + ": " + SafeUrl(r.Value))) + "\n" + string.Join('\n', Workspace.State.Diagnostics.Select(d => d.Code + ": " + d.Message)) + "\n" + Strings["Extensions"] + ": " + string.Join(", ", extensions) + "\nGitHub: " + (Store.Get("github.client") is null ? Strings["NotConfigured"] : Strings["Configured"]) + "\n" + Strings["Plugins"] + ": " + (Store.Get("plugin.last") ?? Strings["NotConfigured"]));
        }
        if (key == "Repair" && Workspace is not null) { await Workspace.Repository.RecoverAsync(); var plan = await Workspace.Repository.PreviewRestoreAsync(await Workspace.Git.SnapshotAsync("HEAD")); if (await Interaction.ConfirmAsync(Strings["Rollback"] + "\n" + string.Join('\n', plan.Changes.Select(c => c.Path)))) { await Workspace.Repository.ApplyRestoreAsync(plan); await Workspace.ReloadAsync(); Refresh(); } }
        if (key == "Plugins")
        {
            if (!await Interaction.ConfirmAsync(Strings["PluginTrust"])) return; var folder = await Interaction.FolderAsync(); if (folder is null) return;
            var host = Path.Combine(AppContext.BaseDirectory, "PluginHost", OperatingSystem.IsWindows() ? "Jourfold.PluginHost.exe" : "Jourfold.PluginHost");
            using var plugin = new PluginProcess(host, folder); var description = await plugin.DescribeAsync(); Store.Set("plugin.last", description.Id + " API " + description.ApiVersion);
            var component = await Interaction.ChooseAsync(Strings["Plugins"], description.Components.Select(c => new Choice(c.Type, c.Title)).ToArray()); if (component is null || Workspace is null || Selected is null) return;
            var definition = description.Components.Single(c => c.Type == component); var data = new JsonObject();
            foreach (var field in definition.Fields) { var value = field.Choices is not null ? await Interaction.ChooseAsync(field.Label, field.Choices.Select(v => new Choice(v, v)).ToArray()) : await Interaction.PromptAsync(field.Label); if (value is null) return; data[field.Key] = field.Kind == "number" ? JsonValue.Create(double.Parse(value, CultureInfo.InvariantCulture)) : JsonValue.Create(value); }
            var entity = Selected.Copy(); if (entity.Data["components"] is not JsonObject) entity.Data["components"] = new JsonObject(); entity.Data["components"]![component] = data; await Workspace.EditAsync(entity);
        }
        if (key == "Report") { var report = await Interaction.PromptAsync(Strings["ReportInfo"], "Jourfold 0.1.0\n" + System.Runtime.InteropServices.RuntimeInformation.OSDescription + "\n.NET " + Environment.Version, true); if (report is not null) await Interaction.CopyAsync(report); }
        if (key is "ConnectGitHub" or "DiscoverGitHub" or "AddRemote") { Busy = false; await (key == "ConnectGitHub" ? ConnectGitHubCommand : key == "DiscoverGitHub" ? DiscoverGitHubCommand : AddRemoteCommand).ExecuteAsync(null); }
    });
    public void SetPreference(string key, string value)
    {
        Store.Set(key == "OpenView" ? "openingView" : key.ToLowerInvariant(), value);
        if (key == "Language")
        {
            Strings = new(value); CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(value == "de" ? "de-DE" : "en-US");
            OnPropertyChanged(nameof(Strings)); OnPropertyChanged(nameof(SharingLabel)); OnPropertyChanged(nameof(TripTitle));
        }
        if (key == "Advanced") Advanced = value == "true";
        ContentChanged?.Invoke(this, EventArgs.Empty);
    }
    [RelayCommand]
    private Task SaveAs() => RunAsync(async () =>
    {
        if (Workspace is null) return;
        var destination = await Interaction.FolderAsync(); if (destination is null) return;
        await Workspace.Git.CopyWorkingTreeAsync(destination);
        if (OpenRequested is not null) OpenRequested(destination); else await OpenAsync(destination);
    });
    public Task AddScheduledAsync(ZonedTime start, Duration duration, Guid? participant = null) => RunAsync(async () =>
    {
        if (Workspace is null) return;
        var draft = new JsonObject { ["precision"] = "exact", ["start"] = start.ToJson(), ["end"] = ZonedTime.At(start.ToInstant() + duration, start.Timezone).ToJson() };
        var title = await Interaction.PromptAsync(Strings["NewActivity"]); if (string.IsNullOrWhiteSpace(title)) return;
        var time = await Interaction.ScheduleAsync(draft, StartDate, start.Timezone); if (time is null) return;
        var item = Entity.Create("schedule_item", title); item.Data["time"] = time["precision"]?.ToString() == "unscheduled" ? null : time;
        if (participant is { } person) item.Data["participants"] = new JsonObject { ["inherit"] = false, ["values"] = new JsonArray(person.ToString()) };
        await Workspace.EditAsync(item); Selected = item; Refresh();
    });
    private int syncTicks;
    public Task PollAsync() => Busy || Workspace is null ? Task.CompletedTask : ExecuteAsync(PollCoreAsync, true);
    private async Task PollCoreAsync()
    {
        if (Workspace is null) return;
        try
        {
            if (await Workspace.ReloadAsync()) { Refresh(); Status = Strings["External"]; }
            if (++syncTicks >= 20)
            {
                syncTicks = 0;
                var remotes = await Workspace.Git.RemotesAsync();
                var remote = Store.Get("remote:" + Workspace.Repository.Root) ?? (remotes.Count == 1 ? remotes.Keys.First() : null);
                if (remote is not null)
                {
                    Busy = true;
                    try { await SynchronizeAsync(true); }
                    catch (Exception ex) when (ex is DomainException or HttpRequestException or IOException or OperationCanceledException) { Status = Strings["SyncFailed"]; }
                    finally { Busy = false; }
                }
            }
        }
        catch (Exception ex) when (ex is DomainException or IOException) { Status = Strings["Error"] + ": " + Strings.Error(ex, Advanced); }
    }
    public void Dispose() { Workspace?.Dispose(); mergeCancellation?.Cancel(); githubHttp.Dispose(); }
}
