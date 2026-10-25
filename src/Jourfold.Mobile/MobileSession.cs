using Jourfold.Application;
using NodaTime;
using TravelRepo.Core;
using TravelRepo.Repository;

namespace Jourfold.Mobile;

/// <summary>A trip folder on this device, as the trip list shows it.</summary>
public sealed record TripCard(string Path, TripSnapshot Trip, TravelRepository Repository, LocalDate? Start, LocalDate? End, IReadOnlyList<string> Travellers, IReadOnlyList<string> Places);

/// <summary>The four places of the phone's bottom bar.</summary>
public enum MobileTab { Today, Plan, Map, More }

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

    public MobileSession(MobileHost host)
    {
        Host = host; Strings = new Localization(host.Language);
        Directory.CreateDirectory(host.TripsRoot);
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
                cards.Add(new(folder, trip, repository, start, end, trip.Entities.Values.Where(e => e.Type == "person").Select(p => p.Title).Order(StringComparer.CurrentCulture).ToArray(), TripSummaries.PlaceNames(trip)));
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
        Workspace = workspace; workspace.Changed += OnWorkspaceChanged;
        Agenda = TripAgenda.Build(workspace.State.Trip);
        Tab = MobileTab.Today; SelectedItem = null; expanded.Clear();
        SelectedDate = Agenda.Find(PhoneToday) is not null ? PhoneToday : Agenda.Days.FirstOrDefault()?.Date ?? PhoneToday;
        Changed?.Invoke();
    }

    /// <summary>Back to the trip list.</summary>
    public void Close()
    {
        if (Workspace is null) return;
        Workspace.Changed -= OnWorkspaceChanged; Workspace.Dispose(); Workspace = null;
        Agenda = new([], []); Changed?.Invoke();
    }

    private void OnWorkspaceChanged(object? sender, EventArgs e)
    {
        if (Workspace is null) return;
        Agenda = TripAgenda.Build(Workspace.State.Trip); Changed?.Invoke();
    }

    public void ShowTab(MobileTab tab) { Tab = tab; Changed?.Invoke(); }
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

    public void Dispose() => Close();
}
