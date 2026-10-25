using Jourfold.Application;
using NodaTime;
using TravelRepo.Core;
using TravelRepo.Repository;

namespace Jourfold.Mobile;

/// <summary>A trip folder on this device, as the trip list shows it.</summary>
public sealed record TripCard(string Path, TripSnapshot Trip, TravelRepository Repository, LocalDate? Start, LocalDate? End, IReadOnlyList<string> Travellers, IReadOnlyList<string> Places);

/// <summary>The four places of the phone's bottom bar.</summary>
public enum MobileTab { Today, Plan, Map, More }

/// <summary>What is open over the page: an item's details, Quick Add, or the editor for an item.</summary>
public enum Overlay { None, Details, QuickAdd, Editor }

/// <summary>A question that needs a yes before something is changed, such as deleting an item.</summary>
public sealed record Question(string Text, string Action, Func<Task> Run);

/// <summary>
/// What the phone app shows and where the user is: the trips on this device, the open trip with its day plan, the tab,
/// the day and the selected item. Views read it and render; every change raises <see cref="Changed"/>.
/// </summary>
public sealed class MobileSession : IDisposable
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

    public MobileSession(MobileHost host)
    {
        Host = host; Strings = new Localization(host.Language);
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
        await workspace.OpenAsync(ct);
        if (Settings.Name is { Length: > 0 } name && Settings.Email is { Length: > 0 } email) await workspace.Git.SetIdentityAsync(name, email, ct);
        Workspace = workspace; workspace.Changed += OnWorkspaceChanged;
        Agenda = TripAgenda.Build(workspace.State.Trip);
        Tab = MobileTab.Today; Page = null; SelectedItem = null; expanded.Clear(); HasChanges = false;
        SelectedDate = Agenda.Find(PhoneToday) is not null ? PhoneToday : Agenda.Days.FirstOrDefault()?.Date ?? PhoneToday;
        Changed?.Invoke();
    }

    /// <summary>Back to the trip list. Open changes are saved as a version first.</summary>
    public void Close()
    {
        if (Workspace is null) return;
        var workspace = Workspace; workspace.Changed -= OnWorkspaceChanged; Workspace = null;
        if (HasChanges) _ = SaveAndDisposeAsync(workspace); else workspace.Dispose();
        Reset();
    }

    private void Reset()
    {
        quiet?.Dispose(); quiet = null; HasChanges = false; History = []; Page = null;
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
    }

    public void ShowTab(MobileTab tab) { Tab = tab; Page = null; Changed?.Invoke(); }

    /// <summary>Opens a page under More; the history is read when the sync page opens.</summary>
    public void ShowPage(string? page)
    {
        Tab = MobileTab.More; Page = page; Changed?.Invoke();
        if (page == "sync") _ = RefreshVersionsAsync();
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

    public async Task RefreshVersionsAsync(CancellationToken ct = default)
    {
        if (Workspace is not { } workspace) return;
        History = await workspace.Git.HistoryAsync(ct);
        if (History.Count > 0 && DateTimeOffset.TryParse(History[0].Timestamp, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var last)) LastVersion = last;
        HasChanges = !string.IsNullOrEmpty(await workspace.Git.StatusAsync(ct));
        Changed?.Invoke();
    }

    /// <summary>Saves the trip's current state as a version with the user's own message.</summary>
    public async Task CreateVersionAsync(string message, CancellationToken ct = default)
    {
        if (Workspace is not { } workspace) return;
        quiet?.Dispose(); quiet = null;
        await workspace.Git.CreateVersionAsync(message.Trim(), "user-version", ct);
        LastVersion = DateTimeOffset.Now;
        await RefreshVersionsAsync(ct);
    }

    /// <summary>
    /// Called after every change: once the trip has been quiet for <see cref="QuietTime"/>, the changes become a
    /// version on their own (specs/mobile/DECISIONS.md, 4). Leaving the app does the same right away.
    /// </summary>
    private void NoteChange()
    {
        HasChanges = true;
        quiet?.Dispose();
        quiet = new Timer(_ => Post(() => _ = SaveChangesAsync()), null, QuietTime, Timeout.InfiniteTimeSpan);
    }

    private void Post(Action action) { if (context is null) action(); else context.Post(_ => action(), null); }

    /// <summary>Turns open changes into a version. Nothing happens when there are none.</summary>
    public async Task SaveChangesAsync(CancellationToken ct = default)
    {
        quiet?.Dispose(); quiet = null;
        if (Workspace is not { } workspace) return;
        try
        {
            if (string.IsNullOrEmpty(await workspace.Git.StatusAsync(ct))) { HasChanges = false; return; }
            await workspace.Git.CreateVersionAsync(Strings["AutoVersion"], "auto-version", ct);
            LastVersion = DateTimeOffset.Now; HasChanges = false;
        }
        catch (DomainException) { HasChanges = true; }
        Changed?.Invoke();
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

    /// <summary>The agenda line of an item: a day's line, a step of a group or an unscheduled item.</summary>
    public AgendaEntry? Entry(Guid id) => Agenda.Days.SelectMany(d => d.Entries).SelectMany(e => e.Steps.Prepend(e)).Concat(Agenda.Unscheduled)
        .FirstOrDefault(e => e.Item.Id == id && e.Role == AgendaRole.Item);

    public void ShowDetails(Guid id) { Showing = Overlay.Details; OverlayItem = id; SelectedItem = id; Changed?.Invoke(); }
    public void ShowQuickAdd() { Showing = Overlay.QuickAdd; OverlayItem = null; Changed?.Invoke(); }
    public void ShowEditor(Guid id) { Showing = Overlay.Editor; OverlayItem = id; Changed?.Invoke(); }
    public void CloseOverlay() { if (Showing == Overlay.None && Asking is null) return; Showing = Overlay.None; OverlayItem = null; Asking = null; Changed?.Invoke(); }
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

    public Task SetStatusAsync(Guid id, string status, CancellationToken ct = default) => UpdateAsync(id, e => e.Data["status"] = status, ct);
    public Task SetTaskDoneAsync(Guid id, bool done, CancellationToken ct = default) => UpdateAsync(id, e => e.Data["status"] = done ? "completed" : "open", ct);

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        Showing = Overlay.None; OverlayItem = null; SelectedItem = null;
        await (Workspace ?? throw new InvalidOperationException("No trip is open.")).DeleteAsync(id, ct); NoteChange();
    }

    /// <summary>Saves the editor: title and notes, and the day and time when they changed.</summary>
    /// <param name="amount">For an expense, the new amount in its currency.</param>
    public async Task SaveEditAsync(Guid id, string title, string? body, LocalDate? day, LocalTime? start, Duration? length, decimal? amount = null, CancellationToken ct = default)
    {
        var workspace = Workspace ?? throw new InvalidOperationException("No trip is open.");
        var entry = Entry(id);
        await UpdateAsync(id, e =>
        {
            e.Data[e.Data.ContainsKey("name") && !e.Data.ContainsKey("title") ? "name" : "title"] = title.Trim();
            if (string.IsNullOrWhiteSpace(body)) e.Data.Remove("body"); else e.Data["body"] = body.Trim();
            if (amount is { } value && e.Data["amount"] is System.Text.Json.Nodes.JsonObject money) money["value"] = value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (day is { } date && start is null)
                e.Data["time"] = new System.Text.Json.Nodes.JsonObject { ["precision"] = "all_day", ["date"] = date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) };
        }, ct);
        if (day is { } d && start is { } t)
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
