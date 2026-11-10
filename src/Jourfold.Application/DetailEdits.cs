using System.Globalization;
using System.Text.Json.Nodes;
using NodaTime;
using TravelRepo.Core;
using TravelRepo.Repository;

namespace Jourfold.Application;

/// <summary>Who a plan is for: everyone (it names nobody), the same people as its group, or the people chosen.</summary>
public enum PeopleChoice { Everyone, AsGroup, Chosen }

/// <summary>
/// Changes to an item's details that both clients make the same way: fields by path, a journey's arrival in its own
/// time zone, a stay's nights and hours, and the booking number, which lives on the booking.
/// </summary>
public static class DetailEdits
{
    /// <summary>The value at a path such as <c>components/transport/flight_number</c>; null when any part is missing.</summary>
    public static JsonNode? Get(Entity e, string path)
    {
        JsonNode? n = e.Data;
        foreach (var k in path.Split('/')) n = n is JsonObject o ? o[k] : null;
        return n;
    }

    /// <summary>Sets the value at a path, creating the objects on the way; null removes it.</summary>
    public static void Set(Entity e, string path, JsonNode? value)
    {
        var keys = path.Split('/'); JsonObject n = e.Data;
        foreach (var k in keys[..^1]) { if (n[k] is not JsonObject) n[k] = new JsonObject(); n = (JsonObject)n[k]!; }
        if (value is null) n.Remove(keys[^1]); else n[keys[^1]] = value.DeepClone();
    }

    /// <summary>Sets a text field, trimmed; empty text removes it.</summary>
    public static void SetText(Entity e, string path, string? text) => Set(e, path, string.IsNullOrWhiteSpace(text) ? null : JsonValue.Create(text.Trim()));

    /// <summary>
    /// A journey arriving at a local time in its own zone, as tickets print it. The departure stays as it is.
    /// </summary>
    public static Entity WithArrival(Entity item, LocalDate day, LocalTime time, string zone)
    {
        if (item.Data["time"]?["start"] is not JsonObject start) throw new DomainException("time.start", "Give the departure a time first.");
        var end = new ZonedTime(day.At(time).ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture), zone);
        if (end.ToInstant() <= ZonedTime.From(start).ToInstant()) throw new DomainException("quickadd.arrival", "The arrival must be after the departure.");
        var copy = item.Copy(); copy.Data["time"]!["end"] = end.ToJson();
        return copy;
    }

    /// <summary>
    /// A stay from its first night for <paramref name="nights"/> nights. A missing hour takes TravelRepo's default and is
    /// marked as not fixed.
    /// </summary>
    public static Entity WithStay(Entity item, LocalDate firstNight, int nights, LocalTime? checkIn, LocalTime? checkOut, string zone)
    {
        if (nights < 1) throw new DomainException("stay.nights", "A stay needs at least one night.");
        return Stays.WithSpan(item, Stays.At(firstNight, checkIn ?? Stays.DefaultCheckIn, zone), Stays.At(firstNight.PlusDays(nights), checkOut ?? Stays.DefaultCheckOut, zone),
            checkInApproximate: checkIn is null, checkOutApproximate: checkOut is null);
    }

    /// <summary>A stay's check-in or check-out hours; without either end the window is removed.</summary>
    public static void SetWindow(Entity stay, string name, LocalTime? from, LocalTime? until) =>
        Set(stay, "components/accommodation/" + name, from is null && until is null ? null : new StayWindow(from, until).ToJson());

    /// <summary>
    /// Sets the booking number of an item. It lives on the item's booking; without one, a booking with the item's title
    /// is added and linked. An empty number clears it on the booking and adds nothing.
    /// </summary>
    public static IReadOnlyList<EntityEdit> WithBookingReference(TripSnapshot trip, Entity item, string? reference)
    {
        var linked = item.Data["components"]?["booking"]?["ref"] ?? item.Data["components"]?["accommodation"]?["booking"];
        if (Guid.TryParse(linked?.ToString(), out var id) && trip.Find(id) is { Type: "booking" } booking)
        {
            var copy = booking.Copy(); SetText(copy, "reference", reference);
            return [new(copy.Id, copy)];
        }
        if (string.IsNullOrWhiteSpace(reference)) return [];
        var (added, edits) = QuickAdd.Build(new QuickAddDraft("booking", item.Title) { Reference = reference }, trip);
        var linkedItem = item.Copy(); Set(linkedItem, "components/booking/ref", JsonValue.Create(added.Id.ToString()));
        return [.. edits, new(linkedItem.Id, linkedItem)];
    }

    /// <summary>The people listed at a path such as <c>paid_by</c> or <c>components/accommodation/guests</c>.</summary>
    public static IReadOnlyList<Guid> People(Entity e, string path) =>
        (Get(e, path) as JsonArray ?? []).Select(n => Guid.TryParse(n?.ToString(), out var id) ? id : Guid.Empty).Where(id => id != Guid.Empty).Distinct().ToArray();

    /// <summary>Lists people at a path; nobody removes the field.</summary>
    public static void SetPeople(Entity e, string path, IEnumerable<Guid> people)
    {
        var ids = people.Distinct().Select(p => (JsonNode?)JsonValue.Create(p.ToString())).ToArray();
        Set(e, path, ids.Length == 0 ? null : new JsonArray(ids));
    }

    /// <summary>How a plan's own participants are set, and the people it names itself.</summary>
    public static (PeopleChoice Choice, IReadOnlyList<Guid> People) Participants(TripSnapshot trip, Entity item)
    {
        var inGroup = TripQueries.Parent(trip, item.Id) is not null;
        if (item.Data["participants"] is not JsonObject own) return (inGroup ? PeopleChoice.AsGroup : PeopleChoice.Everyone, []);
        if (own["inherit"]?.ToString() == "true" && inGroup) return (PeopleChoice.AsGroup, []);
        var people = People(item, "participants/values");
        return (people.Count == 0 ? PeopleChoice.Everyone : PeopleChoice.Chosen, people);
    }

    /// <summary>
    /// Sets who a plan is for. Everyone is no participants: at the top of the plan the field goes, in a group the plan
    /// stops taking the group's people. Without anyone chosen, a choice of people means everyone.
    /// </summary>
    public static void SetParticipants(TripSnapshot trip, Entity item, PeopleChoice choice, IEnumerable<Guid> people)
    {
        var chosen = people.Distinct().Select(p => (JsonNode?)JsonValue.Create(p.ToString())).ToArray();
        var inGroup = TripQueries.Parent(trip, item.Id) is not null;
        if (choice == PeopleChoice.Chosen && chosen.Length == 0) choice = PeopleChoice.Everyone;
        if (choice == PeopleChoice.AsGroup && !inGroup) choice = PeopleChoice.Everyone;
        item.Data.Remove("participants");
        if (choice == PeopleChoice.AsGroup) item.Data["participants"] = new JsonObject { ["inherit"] = true, ["values"] = new JsonArray() };
        else if (choice == PeopleChoice.Chosen) item.Data["participants"] = new JsonObject { ["inherit"] = false, ["values"] = new JsonArray(chosen) };
        else if (inGroup) item.Data["participants"] = new JsonObject { ["inherit"] = false, ["values"] = new JsonArray() };
    }

    /// <summary>An amount in a currency at a path such as <c>price</c>; no amount removes it.</summary>
    public static void SetMoney(Entity e, string path, decimal? amount, string currency)
    {
        if (amount is { } value && (currency.Length != 3 || !currency.All(char.IsAsciiLetterUpper))) throw new DomainException("money.currency", "A currency is a three-letter code such as EUR.");
        // With the currency's own decimals: 3600 JPY, 12.50 EUR.
        Set(e, path, amount is { } v ? new JsonObject { ["value"] = TravelRepo.Core.Expenses.Format(v, currency), ["currency"] = currency } : null);
    }

    /// <summary>
    /// Until when a booking can be cancelled for free, in a zone, keeping the note beside it; null removes the date.
    /// </summary>
    public static void SetFreeCancellation(Entity booking, LocalDateTime? until, string zone)
    {
        if (until is not { } local) { Set(booking, "cancellation/free_until", null); if (booking.Data["cancellation"] is JsonObject { Count: 0 }) booking.Data.Remove("cancellation"); return; }
        var time = new ZonedTime(local.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture), zone);
        time.ToInstant();
        Set(booking, "cancellation/free_until", time.ToJson());
    }
}
