using NodaTime;
using TravelRepo.Core;

namespace Jourfold.Mobile;

/// <summary>
/// The optional second time zone (design spec: second time rail), chosen per trip and kept on this phone. It is shown
/// quieter next to every time in Today and Plan, and hidden where it shows the same time as the first.
/// </summary>
public sealed partial class MobileSession
{
    private string? TripFolder => Workspace is { } w ? Path.GetFileName(w.Repository.Root) : null;

    /// <summary>The second time zone of the open trip, or null when the user has not chosen one.</summary>
    public DateTimeZone? SecondZone => TripFolder is { } folder && Settings.SecondZones.TryGetValue(folder, out var id) ? DateTimeZoneProviders.Tzdb.GetZoneOrNull(id) : null;

    /// <summary>The zones that make sense as a second time: the phone's, the trip's and those of its items, by first use.</summary>
    public IReadOnlyList<DateTimeZone> ZoneChoices
    {
        get
        {
            var ids = new List<string?> { PhoneZone.Id, Trip is { } trip ? ScheduleQueries.TripZone(trip).Id : null };
            ids.AddRange(Agenda.Days.SelectMany(d => d.Entries).SelectMany(e => new[] { e.Zone, e.EndZone }));
            return ids.OfType<string>().Distinct().Select(id => DateTimeZoneProviders.Tzdb.GetZoneOrNull(id)).OfType<DateTimeZone>().ToArray();
        }
    }

    /// <summary>Chooses the second time zone for the open trip, or turns it off.</summary>
    public void SetSecondZone(string? id)
    {
        if (TripFolder is not { } folder) return;
        if (id is null) Settings.SecondZones.Remove(folder); else Settings.SecondZones[folder] = id;
        Settings.Save(); Menu = null; Changed?.Invoke();
    }
}
