using Jourfold.Application;
using TravelRepo.Core;

namespace Jourfold.Mobile;

/// <summary>
/// Places on the phone: choosing them for plan items, changing their name, address and position, finding addresses
/// online (only after the user turned on online maps) and the phone's own position (only when asked for).
/// </summary>
public sealed partial class MobileSession
{
    private HttpClient? mapsHttp;

    /// <summary>The item and slot a place opened from an item's details belongs to; a new place goes there.</summary>
    public (Guid Item, PlaceSlot Slot)? PlaceFor { get; private set; }

    /// <summary>The servers for map tiles and the address search, chosen as on the desktop.</summary>
    public MapProvider MapProvider => MapProviders.Current(Settings);

    /// <summary>Shared by tiles and the address search, so their rules on parallel requests hold together.</summary>
    public HttpClient MapsHttp => mapsHttp ??= Host.Http is null ? new HttpClient { Timeout = TimeSpan.FromSeconds(20) } : new HttpClient(Host.Http, disposeHandler: false);

    /// <summary>Opens a place: <paramref name="id"/> to change it, null for a new one. From an item, the place goes into <paramref name="slot"/> of it.</summary>
    public void ShowPlace(Guid? id, Guid? forItem = null, PlaceSlot slot = PlaceSlot.Place)
    {
        PlaceFor = forItem is { } item ? (item, slot) : null;
        Showing = Overlay.Place; OverlayItem = id; Changed?.Invoke();
    }

    /// <summary>Puts a place into a slot of an item, or takes it out without a choice.</summary>
    public async Task AssignPlaceAsync(Guid item, PlaceSlot slot, PlaceChoice? choice, CancellationToken ct = default)
    {
        var workspace = Workspace ?? throw new InvalidOperationException("No trip is open.");
        await workspace.ApplyAsync(PlaceEdits.Assign(workspace.State.Trip, item, slot, choice), ct); NoteChange();
        Changed?.Invoke();
    }

    /// <summary>
    /// Saves a place's name, address and position, or adds it when <paramref name="id"/> is null. A new place opened
    /// from an item's details becomes that item's place, and the details come back.
    /// </summary>
    public async Task<Guid> SavePlaceAsync(Guid? id, string name, string? address, double? latitude, double? longitude, CancellationToken ct = default)
    {
        var workspace = Workspace ?? throw new InvalidOperationException("No trip is open.");
        if (string.IsNullOrWhiteSpace(name)) throw new DomainException("mobile.title", Strings["EnterTitle"]);
        var place = id is { } existing ? workspace.State.Trip.Find(existing)?.Copy() ?? throw new DomainException("entity.missing", "Place not found.") : Entity.Create("place", name.Trim());
        PlaceEdits.Describe(place, name, address, latitude, longitude);
        if (id is null) await workspace.ApplyAsync([new(place.Id, place)], ct); else await workspace.EditAsync(place, ct);
        NoteChange();
        if (PlaceFor is { } target)
        {
            // A new place goes into the item; an existing one already is its place, perhaps inherited, and stays so.
            if (id is null) await workspace.ApplyAsync(PlaceEdits.Assign(workspace.State.Trip, target.Item, target.Slot, PlaceChoice.Existing(place.Id)), ct);
            Showing = Overlay.Details; OverlayItem = target.Item; PlaceFor = null;
        }
        else if (Showing == Overlay.Place) { Showing = Overlay.None; OverlayItem = null; }
        Changed?.Invoke();
        return place.Id;
    }

    /// <summary>Turns online maps and the address search on or off for this phone.</summary>
    public void SetOnlineMaps(bool on) { Settings.OnlineMaps = on; Settings.Save(); Changed?.Invoke(); }

    /// <summary>Finds places by name or address on the map provider's search server. Only with online maps on.</summary>
    public async Task<IReadOnlyList<PlaceResult>> SearchPlacesAsync(string query, CancellationToken ct = default)
    {
        if (!Settings.OnlineMaps) return [];
        return await new PlaceSearch(MapsHttp, MapProvider.SearchUrl).SearchAsync(query, Strings.Language, ct);
    }
}
