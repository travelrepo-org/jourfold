using System.Globalization;
using System.Text.Json.Nodes;
using TravelRepo.Core;
using TravelRepo.Repository;

namespace Jourfold.Application;

/// <summary>Which place of a plan item: its own place (or a stay's), or where a journey starts or ends.</summary>
public enum PlaceSlot { Place, From, To }

/// <summary>
/// Choosing places for plan items and changing places themselves, as the phone's forms do. A journey keeps its places
/// in <c>components.transport.departure</c> and <c>arrival</c>, a stay in <c>components.accommodation.place</c>, and
/// anything else in <c>default_place</c>, which items inside a group can inherit.
/// </summary>
public static class PlaceEdits
{
    /// <summary>The place in a slot of the item, inherited ones included.</summary>
    public static Entity? Current(TripSnapshot trip, Entity item, PlaceSlot slot) => slot switch
    {
        PlaceSlot.From => ScheduleQueries.Route(trip, item).From,
        PlaceSlot.To => ScheduleQueries.Route(trip, item).To,
        _ => ScheduleQueries.PrimaryPlace(trip, item)
    };

    /// <summary>The slots a form offers for the item: From and To for a journey, one place otherwise.</summary>
    public static IReadOnlyList<PlaceSlot> Slots(Entity item) =>
        item.Data["components"]?["transport"] is JsonObject ? [PlaceSlot.From, PlaceSlot.To] : [PlaceSlot.Place];

    /// <summary>
    /// The ID a choice stands for: an existing place, one already created in <paramref name="edits"/>, a place of the
    /// trip with the same name, or a new place that is added to <paramref name="edits"/>. Null for no choice.
    /// </summary>
    public static string? Resolve(TripSnapshot trip, PlaceChoice? choice, List<EntityEdit> edits)
    {
        if (choice is null) return null;
        if (choice.Id is { } id) return id.ToString();
        if (string.IsNullOrWhiteSpace(choice.NewName)) return null;
        bool Named(Entity? e) => e?.Type == "place" && string.Equals(e.Title, choice.NewName.Trim(), StringComparison.CurrentCultureIgnoreCase);
        if (trip.Entities.Values.FirstOrDefault(Named) is { } existing) return existing.Id.ToString();
        if (edits.Select(e => e.Value).FirstOrDefault(Named) is { } created) return created.Id.ToString();
        var place = Entity.Create("place", choice.NewName.Trim());
        Describe(place, null, choice.Address, choice.Latitude, choice.Longitude);
        edits.Add(new(place.Id, place)); return place.Id.ToString();
    }

    /// <summary>
    /// Edits that put the chosen place into a slot of the item, creating the place first when it is new. Without a
    /// choice the item's own place is removed; an inherited one then shows again.
    /// </summary>
    public static EntityEdit[] Assign(TripSnapshot trip, Guid itemId, PlaceSlot slot, PlaceChoice? choice)
    {
        var item = trip.Find(itemId)?.Copy() ?? throw new DomainException("entity.missing", "Item not found.");
        var edits = new List<EntityEdit>();
        var id = Resolve(trip, choice, edits);
        var components = item.Data["components"] as JsonObject;
        if (slot is PlaceSlot.From or PlaceSlot.To)
        {
            if (components?["transport"] is not JsonObject transport) throw new DomainException("place.slot", "Only journeys have a start and an end.");
            var key = slot == PlaceSlot.From ? "departure" : "arrival";
            var end = transport[key] as JsonObject ?? new JsonObject();
            if (id is null) end.Remove("place"); else end["place"] = id;
            if (end.Count == 0) transport.Remove(key); else transport[key] = end.DeepClone();
        }
        else if (components?["accommodation"] is JsonObject stay)
        {
            if (id is null) stay.Remove("place"); else stay["place"] = id;
        }
        else if (id is null) item.Data.Remove("default_place");
        else item.Data["default_place"] = id;
        edits.Add(new(item.Id, item));
        return edits.ToArray();
    }

    /// <summary>
    /// Sets a place's name, address and position. An empty address or a missing position removes it; the other
    /// address parts and the time zone stay.
    /// </summary>
    public static void Describe(Entity place, string? name, string? address, double? latitude, double? longitude)
    {
        if (!string.IsNullOrWhiteSpace(name)) place.Data[place.Data.ContainsKey("name") && !place.Data.ContainsKey("title") ? "name" : "title"] = name.Trim();
        var postal = place.Data["address"] as JsonObject;
        if (!string.IsNullOrWhiteSpace(address)) { postal ??= new JsonObject(); postal["formatted"] = address.Trim(); place.Data["address"] = postal.DeepClone(); }
        else if (postal is not null) { postal.Remove("formatted"); if (postal.Count == 0) place.Data.Remove("address"); else place.Data["address"] = postal.DeepClone(); }
        if (latitude is { } lat && longitude is { } lon && Coordinates.IsLatitude(lat) && Coordinates.IsLongitude(lon))
        {
            var location = place.Data["location"] as JsonObject ?? new JsonObject();
            location["latitude"] = Math.Round(lat, 6); location["longitude"] = Math.Round(lon, 6);
            place.Data["location"] = location.DeepClone();
        }
        else place.Data.Remove("location");
    }

    /// <summary>A position written for people, such as "-37.0093, 174.7841".</summary>
    public static string Format(double latitude, double longitude) =>
        latitude.ToString("0.######", CultureInfo.InvariantCulture) + ", " + longitude.ToString("0.######", CultureInfo.InvariantCulture);
}
