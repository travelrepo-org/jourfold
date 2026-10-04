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
using TravelRepo.Providers.GitHub;

namespace Jourfold.Desktop;

/// <summary>
/// Window-level presentation state and application commands. Domain rules live in TravelRepo and
/// Jourfold.Application; this class orchestrates them and turns failures into friendly messages.
/// </summary>
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
    public TripSnapshot? Trip => Workspace?.State.Trip;
    public string TripTitle => Workspace?.State.Trip.Manifest.Title ?? Strings["YourTrips"];
    public string SharingLabel => Strings[shareKey];
    private string shareKey = "Publish";
    public string ShareKey => shareKey;
    public string VariantTitle => Workspace?.State.Trip.Manifest.Data["variant"]?["title"]?.ToString() ?? "";
    public string CurrentBranch { get; private set; } = "";
    /// <summary>Variants found at the last local refresh, for menus that must open instantly.</summary>
    public IReadOnlyList<Variant> KnownVariants { get; private set; } = [];
    public bool IsSample => Trip is { } trip && SampleTrip.IsSample(trip);

    /// <summary>Primary navigation keys. "List" and "Map" are alternative presentations of the plan.</summary>
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
    /// <summary>Localization key for the current synchronization state.</summary>
    [ObservableProperty] private string syncKey = "SyncLocalOnly";
    [ObservableProperty] private bool hasUncommitted;
    [ObservableProperty] private bool saving;
    public bool HasPrivacyWarning => PrivacyWarning.Length > 0;
    partial void OnPrivacyWarningChanged(string value) => OnPropertyChanged(nameof(HasPrivacyWarning));
    public LocalDate StartDate { get; set; } = SystemClock.Instance.GetCurrentInstant().InUtc().Date;
    public double Zoom { get; set; } = 1;
    public bool Lanes { get; set; }
    /// <summary>Number of day columns the timetable currently shows; used for paging.</summary>
    public int VisibleDays { get; set; } = 5;
    /// <summary>Raised when content must be re-rendered. Views rebuild from current state.</summary>
    public event EventHandler? ContentChanged;
    public event Action<string>? OpenRequested;
    /// <summary>Raised when only the selection changed.</summary>
    public event EventHandler? SelectionChanged;

    public MainViewModel(LocalStore store, IGitBackend backend, OsSecretStore secrets, IInteraction interaction, Func<GitHubProvider>? providerFactory = null)
    {
        Store = store; this.providerFactory = providerFactory; this.backend = backend; this.secrets = secrets; Interaction = interaction;
        Strings = new(store.Get("language") ?? CultureInfo.CurrentUICulture.TwoLetterISOLanguageName);
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(Strings.Language == "de" ? "de-DE" : "en-GB");
        InboxPinned = Store.Get("inbox.pinned") == "true"; InboxOpen = InboxPinned; Advanced = Store.Get("advanced") == "true";
        Lanes = Store.Get("timetable.lanes") == "true";
        Zoom = double.TryParse(Store.Get("timetable.zoom"), CultureInfo.InvariantCulture, out var zoom) ? Math.Clamp(zoom, .5, 2) : 1;
        foreach (var r in Store.Recent()) Recent.Add(r);
    }
    partial void OnViewChanged(string value) { if (HasTrip) Store.Set("lastView", value); Refresh(); }
    partial void OnSelectedChanged(Entity? value) => SelectionChanged?.Invoke(this, EventArgs.Empty);
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
    private async Task ExecuteAsync(Func<Task> action, bool edit, bool background = false)
    {
        if (operationScope.Value?.Active == true) { await action(); return; }
        await operations.WaitAsync(); var scope = new OperationScope(); operationScope.Value = scope;
        Editing = edit; Busy = true; if (edit && !background) Saving = true;
        try { await action(); }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is DomainException or IOException or UnauthorizedAccessException or HttpRequestException or FormatException or InvalidOperationException or System.Text.Json.JsonException)
        { Status = Strings["Error"]; await Interaction.ShowAsync(Strings["SomethingWentWrong"], Strings.Error(ex, Advanced)); }
        finally { Busy = false; Editing = false; if (!background) Saving = false; scope.Active = false; operationScope.Value = null; operations.Release(); }
    }

    public async Task OpenAsync(string path)
    {
        var next = new Workspace(path, backend);
        try { await next.OpenAsync(); if (next.State.Diagnostics.Any(d => d.Severity == Severity.Error)) throw new DomainException("repository.invalid", string.Join("\n", next.State.Diagnostics.Select(d => d.Path + ": " + d.Code))); }
        catch (DomainException ex)
        {
            try
            {
                await Interaction.ShowAsync(Strings["TripNeedsRepair"], Strings.Error(ex, Advanced));
                var target = await next.Git.SnapshotAsync("HEAD"); var repair = await next.Repository.PreviewRestoreAsync(target);
                if (!await Interaction.ConfirmAsync(Strings["Rollback"] + "\n\n" + string.Join('\n', repair.Changes.Select(c => c.Path)))) { next.Dispose(); return; }
                await next.Repository.ApplyRestoreAsync(repair); await next.OpenAsync();
            }
            catch { next.Dispose(); throw; }
        }
        catch { next.Dispose(); throw; }
        Workspace?.Dispose(); Workspace = next; next.Changed += (_, _) => { Refresh(); _ = UpdateLocalStateAsync(); };
        var remotes = await next.Git.RemotesAsync();
        Store.Remember(path, next.State.Trip, remotes.Count > 0 ? "remote" : "local");
        RefreshRecent();
        shareKey = remotes.Count == 0 ? "Publish" : remotes.Values.Any(IsGitHubUrl) ? "Invite" : "Share"; OnPropertyChanged(nameof(SharingLabel));
        Store.Index(path, next.State.Trip); _ = RefreshPrivacyAsync(remotes.Values); OnPropertyChanged(nameof(HasTrip)); OnPropertyChanged(nameof(IsLibrary)); OnPropertyChanged(nameof(IsSample));
        Selected = null;
        var preferred = Store.Get("openingView");
        View = preferred is { } p && p != "Remember" ? p : Store.Get("lastView") ?? "Plan";
        StartDate = TripStart(next.State.Trip);
        Refresh(); await UpdateLocalStateAsync(); _ = UpdateSyncStatusAsync();
    }

    /// <summary>The first day to show: the trip's first scheduled day, its start date, or today.</summary>
    public static LocalDate TripStart(TripSnapshot trip)
    {
        var (start, _) = TripQueries.EffectiveDates(trip);
        var zone = ScheduleQueries.TripZone(trip);
        var first = trip.Entities.Values.Where(e => e.Type == "schedule_item").Select(e => ScheduleQueries.Span(trip, e).Start).Where(s => s is not null).Min();
        if (first is { } instant) { var day = instant.InZone(zone).Date; return start is { } s && s < day ? s : day; }
        return start ?? SystemClock.Instance.GetCurrentInstant().InZone(zone).Date;
    }

    public void CloseTrip()
    {
        Workspace?.Dispose(); Workspace = null; Selected = null; Items.Clear(); InboxItems.Clear(); PrivacyWarning = ""; Notice = "";
        RefreshRecent(); OnPropertyChanged(nameof(HasTrip)); OnPropertyChanged(nameof(IsLibrary)); OnPropertyChanged(nameof(TripTitle)); OnPropertyChanged(nameof(IsSample));
        ContentChanged?.Invoke(this, EventArgs.Empty);
    }
    public void RefreshRecent() { Recent.Clear(); foreach (var r in Store.Recent()) Recent.Add(r); }

    public void Refresh()
    {
        OnPropertyChanged(nameof(TripTitle)); OnPropertyChanged(nameof(VariantTitle));
        if (Workspace is null) { ContentChanged?.Invoke(this, EventArgs.Empty); return; }
        var trip = Workspace.State.Trip;
        Diagnostics = Workspace.State.Diagnostics.Concat(Workspace.State.Diagnostics.Any(d => d.Severity == Severity.Error) ? [] : SemanticValidation.Validate(trip, Store.TravelEstimates(Workspace.Repository.Root)).Where(d => d.Code == "plan.travel_gap")).ToArray();
        var type = View switch { "Bookings" => "booking", "Costs" => "expense", "Tasks" => "task", "Files" => "document", "People" => "person", "Places" or "Map" => "place", "Collections" => "collection", _ => "schedule_item" };
        Items.Clear();
        foreach (var e in trip.Entities.Values.Where(e => e.Type == type || type == "expense" && e.Type == "budget").OrderBy(e => ScheduleQueries.Span(trip, e).Start ?? Instant.MaxValue).ThenBy(e => e.Title, StringComparer.CurrentCulture)) Items.Add(e);
        InboxItems.Clear(); foreach (var e in InboxCandidates(trip)) InboxItems.Add(e);
        if (Selected is not null) { var current = trip.Find(Selected.Id); if (!ReferenceEquals(current, Selected)) Selected = current; }
        Status = Workspace.State.Diagnostics.Any(d => d.Severity == Severity.Error) ? Strings["Error"] + ": " + string.Join("; ", Workspace.State.Diagnostics.Where(d => d.Severity == Severity.Error).Select(d => d.Code)) : Strings["Saved"];
        ContentChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Inbox: unscheduled schedule items, plus notes and documents not yet used by another entity.</summary>
    public static IEnumerable<Entity> InboxCandidates(TripSnapshot trip)
    {
        var used = trip.All.SelectMany(e => e.Data["content"] as JsonArray ?? []).Select(c => (c?["document"] ?? c?["entity"])?.ToString()).Where(id => id is not null).ToHashSet();
        var children = trip.Entities.Values.SelectMany(e => e.Data["children"] as JsonArray ?? []).Select(c => c?.ToString()).ToHashSet();
        foreach (var e in trip.Entities.Values.OrderByDescending(e => e.Id))
        {
            if (e.Type == "schedule_item" && !children.Contains(e.Id.ToString()) && ScheduleQueries.IsUnscheduled(trip, e)) yield return e;
            else if (e.Type is "note" && !used.Contains(e.Id.ToString())) yield return e;
            else if (e.Type is "document" && !used.Contains(e.Id.ToString()) && TripSummaries.ReferencesTo(trip, e.Id).Count == 0) yield return e;
        }
    }

    public void Navigate(string key) { if (key == View) { Refresh(); return; } View = key; }
    public void Select(Guid? id) => Selected = id is { } value ? Workspace?.State.Trip.Find(value) : null;

    /// <summary>Refresh the subtle autosave state: whether local edits exist that are not in a version yet.</summary>
    private async Task UpdateLocalStateAsync()
    {
        if (Workspace is not { } workspace) return;
        try { HasUncommitted = !string.IsNullOrEmpty(await workspace.Git.StatusAsync()); CurrentBranch = await workspace.Git.CurrentBranchAsync(); KnownVariants = await workspace.Git.VariantsAsync(); }
        catch (DomainException) { }
    }

    public void SetZoom(double zoom) { Zoom = Math.Clamp(zoom, .5, 2); Store.Set("timetable.zoom", Zoom.ToString(CultureInfo.InvariantCulture)); ContentChanged?.Invoke(this, EventArgs.Empty); }
    public void SetLanes(bool lanes) { Lanes = lanes; Store.Set("timetable.lanes", lanes ? "true" : "false"); ContentChanged?.Invoke(this, EventArgs.Empty); }
    public void ShowDate(LocalDate date) { StartDate = date; ContentChanged?.Invoke(this, EventArgs.Empty); }
    public void RaiseContentChanged() => ContentChanged?.Invoke(this, EventArgs.Empty);

    public static bool IsGitHubUrl(string url) => url.StartsWith("git@github.com:", StringComparison.Ordinal) || Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase);
    public static string GitHubName(string url) { var name = url.StartsWith("git@github.com:", StringComparison.Ordinal) ? url[15..] : new Uri(url).AbsolutePath.Trim('/'); return name.EndsWith(".git", StringComparison.Ordinal) ? name[..^4] : name; }
    private GitHubProvider GitHub() => providerFactory?.Invoke() ?? new(githubHttp, secrets, Store.Get("github.client") ?? "");

    public void Dispose() { Workspace?.Dispose(); mergeCancellation?.Cancel(); githubHttp.Dispose(); }
}
