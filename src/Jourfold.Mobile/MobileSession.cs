using Jourfold.Application;
using NodaTime;
using TravelRepo.Core;
using TravelRepo.Repository;

namespace Jourfold.Mobile;

/// <summary>A trip folder on this device, as the trip list shows it.</summary>
public sealed record TripCard(string Path, TripSnapshot Trip, TravelRepository Repository, LocalDate? Start, LocalDate? End, IReadOnlyList<string> Travellers, IReadOnlyList<string> Places);

/// <summary>The four places of the phone's bottom bar.</summary>
public enum MobileTab { Today, Plan, Map, More }

/// <summary>What is open over the page: an item's details, Quick Add, the editor for an item, a place, or what another app shared.</summary>
public enum Overlay { None, Details, QuickAdd, Editor, Place, Share }

/// <summary>A question that needs a yes before something is changed, such as deleting an item.</summary>
public sealed record Question(string Text, string Action, Func<Task> Run);

/// <summary>One entry of a long-press menu. It gets the window, for the clipboard and for handing links to other apps.</summary>
public sealed record MenuAction(string Label, string Icon, Func<Avalonia.Controls.TopLevel?, Task> Run, bool Danger = false);

/// <summary>A file shown in the viewer: a document of the trip or an attachment of an e-mail.</summary>
public sealed record ShownFile(string Name, string MediaType, byte[] Bytes);

/// <summary>The menu a long press opens, with what it is for as its title.</summary>
public sealed record ActionMenu(string Title, string? Detail, IReadOnlyList<MenuAction> Actions);

/// <summary>
/// What the phone app shows and where the user is: the trips on this device, the open trip with its day plan, the tab,
/// the day and the selected item. Views read it and render; every change raises <see cref="Changed"/>.
/// </summary>
public sealed partial class MobileSession : IDisposable
{
    public MobileHost Host { get; }
    public Localization Strings { get; }
    public IReadOnlyList<TripCard> Trips { get; private set; } = [];
    public Workspace? Workspace { get; private set; }
    public TripSnapshot? Trip => Workspace?.State.Trip;
    public Agenda Agenda { get; private set; } = new([], []);
    public MobileTab Tab { get; private set; } = MobileTab.Today;
    public LocalDate SelectedDate { get; private set; }
    public Guid? SelectedItem { get; private set; }
    public IReadOnlySet<Guid> ExpandedGroups => expanded;
    public event Action? Changed;
    private readonly HashSet<Guid> expanded = [];

    public MobileSettings Settings { get; }
    /// <summary>Text typed into fields on pages, by field, so it survives the page being drawn again.</summary>
    public Dictionary<string, string?> Drafts { get; } = [];
    /// <summary>A page under More, such as "bookings" or "sync"; null for More itself.</summary>
    public string? Page { get; private set; }
    public IReadOnlyList<TravelRepo.Git.VersionEntry> History { get; private set; } = [];
    /// <summary>Changes in the trip folder that are not in a version yet.</summary>
    public bool HasChanges { get; private set; }
    public DateTimeOffset? LastVersion { get; private set; }
    /// <summary>How long the trip has to be quiet before changes become a version on their own.</summary>
    public static readonly TimeSpan QuietTime = TimeSpan.FromMinutes(2);
    private readonly SynchronizationContext? context = SynchronizationContext.Current;
    private Timer? quiet;
    private readonly SemaphoreSlim saving = new(1, 1);
    private Task closing = Task.CompletedTask;

    public MobileSession(MobileHost host)
    {
        Host = host; Strings = new Localization(host.Language) { Device = host.Device };
        Directory.CreateDirectory(host.TripsRoot);
        Settings = MobileSettings.Load(Path.Combine(host.DataRoot, "settings.json"));
    }

    public Instant Now => Host.Clock.GetCurrentInstant();
    public DateTimeZone PhoneZone => Host.PhoneZone();
    public LocalDate PhoneToday => Now.InZone(PhoneZone).Date;

    /// <summary>Reads the trip folders on this device. Folders that are not trips are left out.</summary>
    public async Task LoadTripsAsync(CancellationToken ct = default)
    {
        var cards = new List<TripCard>();
        foreach (var folder in Directory.GetDirectories(Host.TripsRoot).Order(StringComparer.Ordinal))
        {
            if (!File.Exists(Path.Combine(folder, "travel.yaml"))) continue;
            try
            {
                var repository = new TravelRepository(folder, Path.Combine(Host.RecoveryRoot, Path.GetFileName(folder)));
                var trip = (await repository.ReadAsync(ct)).Trip;
                var (start, end) = TripQueries.EffectiveDates(trip);
                cards.Add(new(folder, trip, repository, start, end, trip.Entities.Values.Where(e => e.Type == "person").Select(p => p.Title).Order(StringComparer.CurrentCulture).ToArray(), TripSummaries.Destinations(trip) is { Count: > 0 } towns ? towns : TripSummaries.PlaceNames(trip)));
            }
            catch (DomainException) { }
        }
        Trips = cards.OrderByDescending(c => c.Start ?? LocalDate.MinIsoValue).ToArray();
        Changed?.Invoke();
        // A trip added or removed adds or removes its calendar.
        UpdateCalendarSoon();
    }

    /// <summary>Creates the "Spring in Japan" sample trip, starting on the next suitable Friday.</summary>
    public async Task<string> CreateSampleAsync(LocalDate? start = null, CancellationToken ct = default)
    {
        var path = Path.Combine(Host.TripsRoot, "Spring in Japan");
        for (var i = 2; Directory.Exists(path); i++) path = Path.Combine(Host.TripsRoot, "Spring in Japan " + i);
        await SampleTrip.CreateAsync(path, Host.Git, start ?? SampleTrip.SuggestedStart(PhoneToday), "Alex", "alex@example.invalid", Path.Combine(Host.RecoveryRoot, Path.GetFileName(path)), ct);
        await LoadTripsAsync(ct);
        return path;
    }

    public async Task OpenAsync(string path, CancellationToken ct = default)
    {
        Close();
        var workspace = new Workspace(path, Host.Git);
        // Not while the system's "Sync now" is moving this trip forward.
        await SystemCalendars.TripGate.WaitAsync(ct);
        try { await workspace.OpenAsync(ct); }
        finally { SystemCalendars.TripGate.Release(); }
        if (Settings.Name is { Length: > 0 } name && Settings.Email is { Length: > 0 } email) await workspace.Git.SetIdentityAsync(name, email, ct);
        Workspace = workspace; workspace.Changed += OnWorkspaceChanged;
        Agenda = TripAgenda.Build(workspace.State.Trip);
        Tab = MobileTab.Today; Page = null; SelectedItem = null; expanded.Clear(); HasChanges = false;
        SelectedDate = Agenda.Find(PhoneToday) is not null ? PhoneToday : Agenda.Days.FirstOrDefault()?.Date ?? PhoneToday;
        await RefreshRemoteAsync(ct);
        // Changes left from the last time, for example when Android stopped the app, become a version soon.
        if (!string.IsNullOrEmpty(await workspace.Git.StatusAsync(ct))) NoteChange();
        // Something shared before a trip was chosen goes into this one.
        if (Incoming is not null) Showing = Overlay.Share;
        Changed?.Invoke();
        // Others may have changed the trip since it was last open here.
        if (GitHubLogin is not null && Remote is not null) _ = SyncAsync(quiet: true, CancellationToken.None);
    }

    /// <summary>Back to the trip list. Open changes are saved as a version first.</summary>
    public void Close()
    {
        if (Workspace is null) return;
        var workspace = Workspace; workspace.Changed -= OnWorkspaceChanged; Workspace = null;
        if (HasChanges) closing = SaveAndDisposeAsync(workspace); else workspace.Dispose();
        Reset();
    }

    private void Reset()
    {
        quiet?.Dispose(); quiet = null; HasChanges = false; History = []; Page = null; Remote = null; SyncProblem = null; PendingMerge = null; Drafts.Remove("version");
        viewing.Clear(); FileProblem = null; TaxiCard = null;
        Agenda = new([], []); Showing = Overlay.None; OverlayItem = null; Asking = null; Changed?.Invoke();
    }

    private async Task SaveAndDisposeAsync(Workspace workspace)
    {
        try
        {
            if (!string.IsNullOrEmpty(await workspace.Git.StatusAsync()))
                await workspace.Git.CreateVersionAsync(Strings["AutoVersion"], "auto-version");
        }
        catch (DomainException) { }
        finally { workspace.Dispose(); }
    }

    private void OnWorkspaceChanged(object? sender, EventArgs e)
    {
        if (Workspace is null) return;
        Agenda = TripAgenda.Build(Workspace.State.Trip); Changed?.Invoke();
        UpdateCalendarSoon();
    }

    public void ShowTab(MobileTab tab) { Tab = tab; Page = null; Changed?.Invoke(); }

    /// <summary>Opens a page under More. The sync page appears once its versions are read, so it is drawn only once.</summary>
    public void ShowPage(string? page)
    {
        Tab = MobileTab.More; Page = page;
        if (page == "sync" && Workspace is not null) _ = RefreshVersionsAsync(force: true);
        else Changed?.Invoke();
    }

    /// <summary>Saves name and email; versions made from now on carry them.</summary>
    public async Task SaveSettingsAsync(string? name, string? email, CancellationToken ct = default)
    {
        Settings.Name = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
        Settings.Email = string.IsNullOrWhiteSpace(email) ? null : email.Trim();
        Settings.Save();
        if (Workspace is { } workspace && Settings.Name is { } n && Settings.Email is { } e) await workspace.Git.SetIdentityAsync(n, e, ct);
        Changed?.Invoke();
    }

    /// <summary>Reads the versions and whether there are open changes; the page is drawn again only if they differ.</summary>
    public async Task RefreshVersionsAsync(CancellationToken ct = default) => await RefreshVersionsAsync(false, ct);

    private async Task RefreshVersionsAsync(bool force, CancellationToken ct = default)
    {
        if (Workspace is not { } workspace) return;
        var before = (History.FirstOrDefault()?.Commit, HasChanges);
        History = await workspace.Git.HistoryAsync(ct);
        if (History.Count > 0 && DateTimeOffset.TryParse(History[0].Timestamp, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var last)) LastVersion = last;
        HasChanges = !string.IsNullOrEmpty(await workspace.Git.StatusAsync(ct));
        if (force || before != (History.FirstOrDefault()?.Commit, HasChanges)) Changed?.Invoke();
    }

    /// <summary>Saves the trip's current state as a version with the user's own message.</summary>
    public async Task CreateVersionAsync(string message, CancellationToken ct = default)
    {
        if (Workspace is not { } workspace) return;
        quiet?.Dispose(); quiet = null;
        await workspace.Git.CreateVersionAsync(message.Trim(), "user-version", ct);
        Drafts.Remove("version");
        LastVersion = DateTimeOffset.Now;
        await RefreshVersionsAsync(force: true, ct);
    }

    /// <summary>
    /// Called after every change: once the trip has been quiet for <see cref="QuietTime"/>, the changes become a
    /// version on their own (specs/mobile/DECISIONS.md, 4). Leaving the app does the same right away.
    /// </summary>
    private void NoteChange()
    {
        HasChanges = true;
        quiet?.Dispose();
        quiet = new Timer(_ => Post(() => _ = AutoSaveAsync()), null, QuietTime, Timeout.InfiniteTimeSpan);
    }

    private void Post(Action action) { if (context is null) action(); else context.Post(_ => action(), null); }

    /// <summary>
    /// Turns open changes into a version and, when signed in, syncs the trip. The platform app calls it when the app
    /// goes to the background.
    /// </summary>
    public async Task AutoSaveAsync(CancellationToken ct = default)
    {
        await SaveChangesAsync(ct);
        if (GitHubLogin is not null) await SyncAsync(quiet: true, ct);
    }

    /// <summary>Turns open changes into a version. Nothing happens when there are none.</summary>
    public async Task SaveChangesAsync(CancellationToken ct = default)
    {
        quiet?.Dispose(); quiet = null;
        if (Workspace is not { } workspace) return;
        // The timer, leaving the app and sync can ask at the same moment; one version is enough.
        await saving.WaitAsync(ct);
        try
        {
            if (string.IsNullOrEmpty(await workspace.Git.StatusAsync(ct))) { HasChanges = false; return; }
            await workspace.Git.CreateVersionAsync(Strings["AutoVersion"], "auto-version", ct);
            LastVersion = DateTimeOffset.Now; HasChanges = false;
            if (Page == "sync") History = await workspace.Git.HistoryAsync(ct);
        }
        catch (DomainException) { HasChanges = true; }
        finally { saving.Release(); Changed?.Invoke(); }
    }

    public void ShowDate(LocalDate date) { SelectedDate = date; SelectedItem = null; Changed?.Invoke(); }
    public void Select(Guid? item) { SelectedItem = SelectedItem == item ? null : item; Changed?.Invoke(); }
    public void ToggleGroup(Guid group) { if (!expanded.Remove(group)) expanded.Add(group); Changed?.Invoke(); }

    /// <summary>Moves the plan one day back or forward within the trip's days.</summary>
    public void StepDay(int direction)
    {
        var days = Agenda.Days; var index = days.ToList().FindIndex(d => d.Date == SelectedDate);
        var next = index + direction;
        if (next >= 0 && next < days.Count) ShowDate(days[next].Date);
    }

    public Overlay Showing { get; private set; }
    public Guid? OverlayItem { get; private set; }
    public Question? Asking { get; private set; }
    /// <summary>The long-press menu that is open, if any.</summary>
    public ActionMenu? Menu { get; private set; }
    /// <summary>The files open in the viewer, the one shown last; an attachment opened from an e-mail goes on top.</summary>
    public IReadOnlyList<ShownFile> Viewing => viewing;
    private readonly List<ShownFile> viewing = [];

    /// <summary>The agenda line of an item: a day's line, a step of a group or an unscheduled item.</summary>
    public AgendaEntry? Entry(Guid id) => Agenda.Days.SelectMany(d => d.Entries).SelectMany(e => e.Steps.Prepend(e)).Concat(Agenda.Unscheduled)
        .FirstOrDefault(e => e.Item.Id == id && e.Role == AgendaRole.Item);

    public void ShowDetails(Guid id) { Showing = Overlay.Details; OverlayItem = id; SelectedItem = id; Changed?.Invoke(); }
    public void ShowQuickAdd() { Showing = Overlay.QuickAdd; OverlayItem = null; Changed?.Invoke(); }
    public void ShowEditor(Guid id) { Showing = Overlay.Editor; OverlayItem = id; Changed?.Invoke(); }
    /// <summary>Closes what is on top: a question or menu first, otherwise the sheet. A place opened from an item's details goes back to them.</summary>
    public void CloseOverlay()
    {
        if (Asking is not null || Menu is not null) { Asking = null; Menu = null; }
        else if (Showing == Overlay.Place && PlaceFor is { } from) { Showing = Overlay.Details; OverlayItem = from.Item; PlaceFor = null; }
        else if (Showing == Overlay.Share) { Incoming = null; Showing = Overlay.None; }
        else if (Showing != Overlay.None) { Showing = Overlay.None; OverlayItem = null; PlaceFor = null; }
        else return;
        Changed?.Invoke();
    }
    public void ShowMenu(ActionMenu menu) { Menu = menu; Changed?.Invoke(); }

    /// <summary>Opens a document of the trip in the viewer. Its file is read from the trip folder.</summary>
    public void ShowDocument(Entity document)
    {
        if (Workspace is not { } workspace) return;
        byte[] bytes;
        try { bytes = File.ReadAllBytes(workspace.Repository.DocumentPath(document)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DomainException)
        {
            // The viewer still opens, to say what is missing where the file would be.
            viewing.Add(new(document.Title, "application/octet-stream", [])); FileProblem = Strings["FileMissing"]; Changed?.Invoke(); return;
        }
        ShowFile(new(document.Title, document.Data["media_type"]?.ToString() ?? "application/octet-stream", bytes));
    }

    public void ShowFile(ShownFile file) { viewing.Add(file); FileProblem = null; Bright(); Changed?.Invoke(); }

    /// <summary>A ticket on screen, a pass or a picture, is shown at full brightness so a scanner can read it.</summary>
    private void Bright() => Host.Brightness?.Invoke(viewing.LastOrDefault() is { } shown && (Tickets.IsTicket(shown.MediaType) && shown.MediaType != "application/pdf"));

    /// <summary>One step back in the viewer: to the e-mail an attachment came from, or out of the viewer.</summary>
    public void CloseFile() { if (viewing.Count > 0) viewing.RemoveAt(viewing.Count - 1); FileProblem = null; Bright(); Changed?.Invoke(); }

    /// <summary>A short message about something that did not work, shown until the next step.</summary>
    public string? FileProblem { get; private set; }

    /// <summary>
    /// Hands a file to another app on the phone, such as a PDF viewer. The copy it gets is the only one outside the
    /// trip folder and is replaced by the next.
    /// </summary>
    public async Task<bool> OpenElsewhereAsync(ShownFile file)
    {
        if (Host.OpenFile is not { } open) return false;
        if (Directory.Exists(Host.SharedRoot)) Directory.Delete(Host.SharedRoot, recursive: true);
        Directory.CreateDirectory(Host.SharedRoot);
        var name = TravelRepo.Documents.Mail.SafeName(Path.HasExtension(file.Name) ? file.Name : file.Name + TravelRepo.Documents.MediaTypes.Extension(file.MediaType));
        var path = Path.Combine(Host.SharedRoot, name);
        await File.WriteAllBytesAsync(path, file.Bytes);
        if (await open(path, file.MediaType)) return true;
        FileProblem = Strings["NoAppForFile"]; Changed?.Invoke();
        return false;
    }

    /// <summary>Closes the menu and runs the chosen entry; an entry may open another menu, such as the days to move to.</summary>
    public async Task ChooseAsync(MenuAction action, Avalonia.Controls.TopLevel? top)
    {
        Menu = null; Changed?.Invoke();
        await action.Run(top);
    }
    public void Confirm(string text, string action, Func<Task> run) { Asking = new(text, action, run); Changed?.Invoke(); }
    public async Task AnswerAsync(bool yes)
    {
        var question = Asking; Asking = null; Changed?.Invoke();
        if (yes && question is not null) await question.Run();
    }

    /// <summary>Adds what Quick Add collected, with the same rules as the desktop; an idea stays tentative.</summary>
    public async Task AddAsync(QuickAddDraft draft, bool idea, CancellationToken ct = default)
    {
        var workspace = Workspace ?? throw new InvalidOperationException("No trip is open.");
        var (main, edits) = QuickAdd.Build(draft, workspace.State.Trip);
        if (idea && main.Type == "schedule_item") main.Data["status"] = "idea";
        await workspace.ApplyAsync(edits, ct); NoteChange();
        Showing = Overlay.None; OverlayItem = null;
        if (draft.Date is { } day && Agenda.Find(day) is not null) SelectedDate = day;
        Changed?.Invoke();
    }

    private async Task UpdateAsync(Guid id, Action<Entity> change, CancellationToken ct = default)
    {
        var workspace = Workspace ?? throw new InvalidOperationException("No trip is open.");
        var copy = workspace.State.Trip.Find(id)?.Copy() ?? throw new DomainException("entity.missing", "Item not found.");
        change(copy); await workspace.EditAsync(copy, ct); NoteChange();
    }

    /// <summary>Saves edits the editor built from the open trip, such as an item and its booking, as one change.</summary>
    public async Task SaveDetailsAsync(IReadOnlyList<EntityEdit> edits, CancellationToken ct = default)
    {
        var workspace = Workspace ?? throw new InvalidOperationException("No trip is open.");
        if (edits.Count == 0) return;
        await workspace.ApplyAsync([.. edits], ct); NoteChange();
    }

    public Task SetStatusAsync(Guid id, string status, CancellationToken ct = default) => UpdateAsync(id, e => e.Data["status"] = status, ct);
    public Task SetTaskDoneAsync(Guid id, bool done, CancellationToken ct = default) => UpdateAsync(id, e => e.Data["status"] = done ? "completed" : "open", ct);

    /// <summary>Adds a copy of an item and what it contains, named as a copy, on the same day.</summary>
    public async Task DuplicateAsync(Guid id, CancellationToken ct = default)
    {
        var workspace = Workspace ?? throw new InvalidOperationException("No trip is open.");
        var copies = TripCommands.DuplicateSubtree(workspace.State.Trip, id);
        var root = copies[0]; root.Data[root.Data.ContainsKey("name") && !root.Data.ContainsKey("title") ? "name" : "title"] = string.Format(System.Globalization.CultureInfo.CurrentCulture, Strings["CopyOf"], root.Title);
        await workspace.ApplyAsync(copies.Select(c => new EntityEdit(c.Id, c)).ToArray(), ct); NoteChange();
    }

    /// <summary>Moves an item to another day at the same local time; an item without a time keeps its kind of day.</summary>
    public async Task MoveToDayAsync(Guid id, LocalDate day, CancellationToken ct = default)
    {
        var workspace = Workspace ?? throw new InvalidOperationException("No trip is open.");
        var entry = Entry(id);
        if (entry is { IsTimed: true, Start: { } start, Zone: { } zoneId } && DateTimeZoneProviders.Tzdb.GetZoneOrNull(zoneId) is { } zone)
        {
            var length = entry.End is { } end && end > start ? end - start : Duration.FromHours(1);
            NoteChange();
            await workspace.ScheduleAsync(id, new ZonedTime(day.At(start.InZone(zone).TimeOfDay).ToString("yyyy-MM-dd'T'HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture), zoneId), length, ct);
        }
        else await UpdateAsync(id, e =>
        {
            var date = day.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
            if (e.Data["time"] is System.Text.Json.Nodes.JsonObject time && time.ContainsKey("date")) time["date"] = date;
            else e.Data["time"] = new System.Text.Json.Nodes.JsonObject { ["precision"] = "all_day", ["date"] = date };
        }, ct);
        if (Agenda.Find(day) is not null) SelectedDate = day;
        Changed?.Invoke();
    }

    /// <summary>
    /// Removes a trip from this phone. A trip shared on GitHub stays there and can be fetched again; one that was only
    /// here is gone, which the question before says.
    /// </summary>
    public async Task DeleteTripAsync(string path, CancellationToken ct = default)
    {
        if (Workspace is { } open && Path.GetFullPath(open.Repository.Root) == Path.GetFullPath(path)) Close();
        // A trip that was just left may still be saving its last version.
        await closing;
        await Task.Run(() =>
        {
            foreach (var folder in new[] { path, Path.Combine(Host.RecoveryRoot, Path.GetFileName(path)) }.Where(Directory.Exists))
            {
                // Git keeps its object files read-only; they have to be writable to be deleted.
                foreach (var file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)) File.SetAttributes(file, FileAttributes.Normal);
                Directory.Delete(folder, recursive: true);
            }
        }, ct);
        if (Settings.LastSync.Remove(Path.GetFileName(path))) Settings.Save();
        await LoadTripsAsync(ct);
    }

    /// <summary>Whether a trip folder has a copy on GitHub or elsewhere to come back from.</summary>
    public async Task<bool> IsSharedAsync(string path, CancellationToken ct = default)
    {
        try { return (await new TravelRepo.Git.GitRepository(new TravelRepository(path), Host.Git).RemotesAsync(ct)).Count > 0; }
        catch (DomainException) { return false; }
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        Showing = Overlay.None; OverlayItem = null; SelectedItem = null;
        await (Workspace ?? throw new InvalidOperationException("No trip is open.")).DeleteAsync(id, ct); NoteChange();
    }

    /// <summary>Saves the editor: title and notes, and the day and time when they changed.</summary>
    /// <param name="amount">For an expense, the new amount in its currency.</param>
    /// <param name="task">For a task, <paramref name="day"/> is when it is due; null clears it.</param>
    public async Task SaveEditAsync(Guid id, string title, string? body, LocalDate? day, LocalTime? start, Duration? length, decimal? amount = null, bool task = false, CancellationToken ct = default)
    {
        var workspace = Workspace ?? throw new InvalidOperationException("No trip is open.");
        var entry = Entry(id);
        await UpdateAsync(id, e =>
        {
            e.Data[e.Data.ContainsKey("name") && !e.Data.ContainsKey("title") ? "name" : "title"] = title.Trim();
            if (string.IsNullOrWhiteSpace(body)) e.Data.Remove("body"); else e.Data["body"] = body.Trim();
            if (amount is { } value && e.Data["amount"] is System.Text.Json.Nodes.JsonObject money) money["value"] = value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (task)
            {
                if (day is { } due) e.Data["due"] = new System.Text.Json.Nodes.JsonObject { ["date"] = due.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) };
                else e.Data.Remove("due");
                return;
            }
            if (day is { } date && start is null)
                e.Data["time"] = new System.Text.Json.Nodes.JsonObject { ["precision"] = "all_day", ["date"] = date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) };
        }, ct);
        if (!task && day is { } d && start is { } t)
        {
            var zone = entry?.Zone ?? ScheduleQueries.TripZone(workspace.State.Trip).Id;
            var previous = entry is { Start: { } a, End: { } b } && b > a ? b - a : Duration.FromHours(1);
            NoteChange();
            await workspace.ScheduleAsync(id, new ZonedTime(d.At(t).ToString("yyyy-MM-dd'T'HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture), zone), length ?? previous, ct);
        }
        Showing = Overlay.Details; Changed?.Invoke();
    }

    public void Dispose() => Close();
}
