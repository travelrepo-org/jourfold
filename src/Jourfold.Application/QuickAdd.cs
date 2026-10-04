using System.Globalization;
using System.Text.Json.Nodes;
using NodaTime;
using TravelRepo.Core;
using TravelRepo.Repository;

namespace Jourfold.Application;

/// <summary>A place chosen in a form: an existing place, a new place created by name, or nothing.</summary>
public sealed record PlaceChoice(Guid? Id, string? NewName = null, double? Latitude = null, double? Longitude = null, string? Address = null)
{
    public static PlaceChoice? Existing(Guid id) => new(id);
    public static PlaceChoice? Create(string name) => string.IsNullOrWhiteSpace(name) ? null : new(null, name.Trim());
}

/// <summary>What the user entered in Quick Add. Kind values: activity, food, sightseeing, transport, accommodation, task, note, booking, place, person, expense, budget, collection.</summary>
public sealed record QuickAddDraft(string Kind, string Title)
{
    public LocalDate? Date { get; init; }
    public LocalTime? Start { get; init; }
    public Duration? Length { get; init; }
    /// <summary>Timezone of <see cref="Date"/> and <see cref="Start"/>; for transport, the departure timezone.</summary>
    public string? Timezone { get; init; }
    /// <summary>Transport arrival in local time at the destination. When set, it replaces <see cref="Length"/>.</summary>
    public LocalDate? ArrivalDate { get; init; }
    public LocalTime? ArrivalTime { get; init; }
    public string? ArrivalTimezone { get; init; }
    public string? TransportType { get; init; }
    public PlaceChoice? Place { get; init; }
    public PlaceChoice? From { get; init; }
    public PlaceChoice? To { get; init; }
    public int Nights { get; init; } = 1;
    public decimal? Amount { get; init; }
    public string? Currency { get; init; }
    public string? Body { get; init; }
    public string? Reference { get; init; }
    public IReadOnlyList<Guid> People { get; init; } = [];
}

/// <summary>Builds canonical entities for Quick Add. The result is applied as one undoable repository command.</summary>
public static class QuickAdd
{
    public static readonly IReadOnlyList<string> ScheduleKinds = ["activity", "food", "sightseeing", "transport", "accommodation"];

    public static (Entity Main, EntityEdit[] Edits) Build(QuickAddDraft draft, TripSnapshot trip)
    {
        if (string.IsNullOrWhiteSpace(draft.Title)) throw new DomainException("quickadd.title", "Enter a title.");
        var edits = new List<EntityEdit>();
        var zone = draft.Timezone ?? trip.Manifest.Data["default_timezone"]?.ToString() ?? "Etc/UTC";
        string? Place(PlaceChoice? choice)
        {
            if (choice is null) return null;
            if (choice.Id is { } id) return id.ToString();
            if (string.IsNullOrWhiteSpace(choice.NewName)) return null;
            var existing = trip.Entities.Values.FirstOrDefault(e => e.Type == "place" && string.Equals(e.Title, choice.NewName, StringComparison.CurrentCultureIgnoreCase));
            if (existing is not null) return existing.Id.ToString();
            var created = edits.Select(e => e.Value).FirstOrDefault(e => e?.Type == "place" && string.Equals(e.Title, choice.NewName, StringComparison.CurrentCultureIgnoreCase));
            if (created is not null) return created.Id.ToString();
            var place = Entity.Create("place", choice.NewName!.Trim());
            if (choice.Latitude is { } lat && choice.Longitude is { } lon) place.Data["location"] = new JsonObject { ["latitude"] = lat, ["longitude"] = lon };
            if (!string.IsNullOrWhiteSpace(choice.Address)) place.Data["address"] = new JsonObject { ["formatted"] = choice.Address };
            edits.Add(new(place.Id, place)); return place.Id.ToString();
        }
        JsonObject Participants() => new() { ["inherit"] = false, ["values"] = new JsonArray(draft.People.Select(p => (JsonNode?)JsonValue.Create(p.ToString())).ToArray()) };
        JsonNode Money() => new JsonObject { ["value"] = (draft.Amount ?? 0m).ToString("0.00", CultureInfo.InvariantCulture), ["currency"] = draft.Currency ?? "EUR" };

        Entity main;
        switch (draft.Kind)
        {
            case "activity" or "food" or "sightseeing" or "transport" or "accommodation":
                main = Entity.Create("schedule_item", draft.Title.Trim());
                if (draft.Kind is "food" or "sightseeing") main.Data["category"] = draft.Kind;
                if (draft.People.Count > 0) main.Data["participants"] = Participants();
                if (draft.Kind == "transport")
                {
                    var transport = new JsonObject { ["type"] = draft.TransportType ?? "other" };
                    if (Place(draft.From) is { } from) transport["departure"] = new JsonObject { ["place"] = from };
                    if (Place(draft.To) is { } to) transport["arrival"] = new JsonObject { ["place"] = to };
                    main.Data["components"]!["transport"] = transport;
                }
                else if (draft.Kind == "accommodation")
                {
                    var accommodation = new JsonObject();
                    if (Place(draft.Place) is { } stay) accommodation["place"] = stay;
                    if (draft.Date is { } arrival)
                    {
                        var checkIn = new ZonedTime(arrival.At(draft.Start ?? new LocalTime(15, 0)).ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture), zone);
                        var checkOut = new ZonedTime(arrival.PlusDays(Math.Max(1, draft.Nights)).At(new LocalTime(11, 0)).ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture), zone);
                        accommodation["check_in"] = checkIn.ToJson(); accommodation["check_out"] = checkOut.ToJson();
                        main.Data["time"] = new JsonObject { ["precision"] = "exact", ["start"] = checkIn.ToJson(), ["end"] = checkOut.ToJson() };
                    }
                    main.Data["components"]!["accommodation"] = accommodation;
                }
                else if (Place(draft.Place) is { } place) main.Data["default_place"] = place;
                if (draft.Kind != "accommodation" && draft.Date is { } day && draft.Start is { } start)
                {
                    var begin = new ZonedTime(day.At(start).ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture), zone);
                    var instant = begin.ToInstant();
                    ZonedTime end;
                    if (draft.Kind == "transport" && draft.ArrivalDate is { } arrivalDay && draft.ArrivalTime is { } arrivalTime)
                    {
                        // Tickets give departure and arrival in local time at each end.
                        end = new ZonedTime(arrivalDay.At(arrivalTime).ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture), draft.ArrivalTimezone ?? zone);
                        if (end.ToInstant() <= instant) throw new DomainException("quickadd.arrival", "The arrival must be after the departure.");
                    }
                    else
                    {
                        var endZone = draft.ArrivalTimezone ?? (draft.Kind == "transport" && draft.To?.Id is { } arrivalId && trip.Find(arrivalId)?.Data["timezone"]?.ToString() is { Length: > 0 } arrivalZone ? arrivalZone : zone);
                        end = ZonedTime.At(instant + (draft.Length ?? Duration.FromHours(1)), endZone);
                    }
                    main.Data["time"] = new JsonObject { ["precision"] = "exact", ["start"] = begin.ToJson(), ["end"] = end.ToJson() };
                    main.Data["status"] = "planned";
                }
                else if (draft.Kind != "accommodation" && draft.Date is { } onlyDay)
                {
                    main.Data["time"] = new JsonObject { ["precision"] = "all_day", ["date"] = onlyDay.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) };
                    main.Data["status"] = "planned";
                }
                if (draft.Length is { } length && draft.Start is null) main.Data["duration"] = NodaTime.Text.PeriodPattern.NormalizingIso.Format(Period.FromMinutes((long)length.TotalMinutes));
                if (draft.Kind == "accommodation" && draft.Date is not null) main.Data["status"] = "planned";
                break;
            case "task":
                main = Entity.Create("task", draft.Title.Trim());
                if (draft.Date is { } due) main.Data["due"] = new JsonObject { ["date"] = due.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) };
                if (draft.People.Count > 0) main.Data["assigned_to"] = new JsonArray(draft.People.Select(p => (JsonNode?)JsonValue.Create(p.ToString())).ToArray());
                break;
            case "note":
                main = Entity.Create("note", draft.Title.Trim());
                if (!string.IsNullOrWhiteSpace(draft.Body)) main.Data["body"] = draft.Body.Trim();
                break;
            case "booking":
                main = Entity.Create("booking", draft.Title.Trim()); main.Data["status"] = "pending";
                if (!string.IsNullOrWhiteSpace(draft.Reference)) main.Data["reference"] = draft.Reference.Trim();
                if (draft.Amount is not null) main.Data["price"] = Money();
                if (draft.People.Count > 0) main.Data["travelers"] = new JsonArray(draft.People.Select(p => (JsonNode?)JsonValue.Create(p.ToString())).ToArray());
                break;
            case "place":
                var choice = draft.Place ?? new PlaceChoice(null, draft.Title);
                main = Entity.Create("place", draft.Title.Trim());
                if (choice.Latitude is { } lat && choice.Longitude is { } lon) main.Data["location"] = new JsonObject { ["latitude"] = lat, ["longitude"] = lon };
                if (!string.IsNullOrWhiteSpace(choice.Address)) main.Data["address"] = new JsonObject { ["formatted"] = choice.Address };
                break;
            case "person":
                main = Entity.Create("person", draft.Title.Trim()); main.Data["roles"] = new JsonArray("traveler");
                var manifest = trip.Manifest.Copy(); var participants = manifest.Data["participants"] as JsonArray ?? [];
                manifest.Data["participants"] = participants; participants.Add(main.Id.ToString()); edits.Add(new(manifest.Id, manifest));
                break;
            case "expense" or "budget":
                main = Entity.Create(draft.Kind, draft.Title.Trim()); main.Data["amount"] = Money();
                if (draft.Kind == "expense") { main.Data["estimated"] = false; if (draft.People.Count > 0) main.Data["paid_by"] = new JsonArray(draft.People.Select(p => (JsonNode?)JsonValue.Create(p.ToString())).ToArray()); }
                break;
            case "collection":
                main = Entity.Create("collection", draft.Title.Trim()); main.Data["items"] = new JsonArray();
                break;
            default: throw new DomainException("quickadd.kind", "Unknown item type.");
        }
        edits.Insert(0, new(main.Id, main));
        return (main, edits.ToArray());
    }
}
