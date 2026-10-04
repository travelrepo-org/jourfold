using NodaTime;
using TravelRepo.Core;

namespace Jourfold.Desktop;

public partial class MainViewModel
{
    /// <summary>
    /// An optional second timezone for the open trip, shown beside the trip timezone in the timetable and the list,
    /// for example the time at home. It is a view preference stored on this computer, not trip data.
    /// </summary>
    public string? SecondZone =>
        Workspace is not null && Store.Get("zone2:" + Workspace.Repository.Root) is { Length: > 0 } id && DateTimeZoneProviders.Tzdb.GetZoneOrNull(id) is not null && id != ScheduleQueries.TripZone(Workspace.State.Trip).Id ? id : null;

    public void SetSecondZone(string? zone)
    {
        if (Workspace is null) return;
        Store.Set("zone2:" + Workspace.Repository.Root, zone ?? "");
        OnPropertyChanged(nameof(SecondZone)); ContentChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Timezones worth offering as the second one: those the trip already uses (departure points, places, stays),
    /// most frequent first, then this computer's timezone. The trip timezone itself is left out.
    /// </summary>
    public IReadOnlyList<string> SecondZoneSuggestions()
    {
        if (Workspace is null) return [];
        var trip = Workspace.State.Trip; var primary = ScheduleQueries.TripZone(trip).Id;
        var used = trip.Entities.Values.SelectMany(e => new[] { e.Data["time"]?["start"]?["timezone"]?.ToString(), e.Data["time"]?["end"]?["timezone"]?.ToString(), e.Data["timezone"]?.ToString() })
            .OfType<string>().Where(z => z.Length > 0 && z != primary && DateTimeZoneProviders.Tzdb.GetZoneOrNull(z) is not null)
            .GroupBy(z => z).OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal).Select(g => g.Key);
        var local = DateTimeZoneProviders.Tzdb.GetSystemDefault().Id;
        return used.Append(local).Where(z => z != primary).Distinct().Take(6).ToArray();
    }
}
