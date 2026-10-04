using System.Globalization;
using System.Text.Json.Nodes;
using NodaTime;
using TravelRepo.Core;
namespace Jourfold.Desktop;

public sealed record EditorField(string Path, string Label, string Kind = "text", string[]? Choices = null, string? ReferenceType = null);
public static class EditorModel
{
    public static IReadOnlyList<EditorField> Fields(Entity e)
    {
        var f = new List<EditorField> { new(e.Type == "person" ? "display_name" : e.Type is "place" or "document" ? "name" : "title", "Title") };
        switch (e.Type)
        {
            case "trip": f.AddRange([new("language", "Language", "language"), new("dates/start", "Start", "date"), new("dates/end", "End", "date"), new("default_timezone", "Timezone", "timezone"), new("participants", "Participants", "refs", ReferenceType: "person"), new("cover/document", "Cover", "ref", ReferenceType: "document")]); break;
            case "schedule_item":
                f.AddRange([new("status", "Status", "choice", ["idea", "planned", "reserved", "confirmed", "completed", "cancelled"]), new("participants/values", "Participants", "refs", ReferenceType: "person"), new("participants/inherit", "Inherit", "bool"), new("children", "Children", "refs", ReferenceType: "schedule_item"), new("default_place", "DefaultPlace", "ref", ReferenceType: "place"), new("tags", "Tags", "list"), new("duration", "DurationMinutes", "duration"), new("category", "Category")]);
                f.Add(new("time", "Schedule", "schedule"));
                if (e.Data["components"]?["transport"] is not null) f.AddRange([new("components/transport/type", "transport", "choice", ["flight", "train", "bus", "car", "taxi", "rideshare", "ferry", "ship", "bicycle", "walking", "other"]), new("components/transport/carrier", "Carrier"), new("components/transport/flight_number", "Number"), new("components/transport/departure/place", "Departure", "ref", ReferenceType: "place"), new("components/transport/arrival/place", "Arrival", "ref", ReferenceType: "place"), new("components/transport/departure/terminal", "Terminal"), new("components/transport/departure/gate", "Gate"), new("components/transport/seat", "Seat"), new("components/transport/distance_m", "Distance", "distance"), new("components/transport/stops", "Items", "stops", ReferenceType: "place")]);
                if (e.Data["components"]?["accommodation"] is not null) f.AddRange([new("components/accommodation/place", "Place", "ref", ReferenceType: "place"), new("components/accommodation/guests", "Guests", "refs", ReferenceType: "person"), new("components/accommodation/rooms", "Rooms", "list"), new("components/accommodation/check_in", "CheckIn", "zoned"), new("components/accommodation/check_out", "CheckOut", "zoned"), new("components/accommodation/booking", "Booking", "ref", ReferenceType: "booking"), new("components/accommodation/documents", "Documents", "refs", ReferenceType: "document")]);
                f.Add(new("components/booking/ref", "Booking", "ref", ReferenceType: "booking")); break;
            case "person": f.AddRange([new("roles", "Roles", "list"), new("avatar/document", "Files", "ref", ReferenceType: "document"), new("identities/git", "LocalIdentity", "identities"), new("identities/github", "GitHub", "github")]); break;
            case "place": f.AddRange([new("address/formatted", "Address"), new("timezone", "Timezone", "timezone"), new("location", "Map", "coordinates")]); break;
            case "booking": f.AddRange([new("provider/name", "Provider"), new("reference", "Reference"), new("status", "Status", "choice", ["idea", "pending", "reserved", "confirmed", "cancelled", "completed"]), new("travelers", "Travelers", "refs", ReferenceType: "person"), new("items", "Items", "refs", ReferenceType: "schedule_item"), new("documents", "Documents", "refs", ReferenceType: "document"), new("price", "Amount", "money")]); break;
            case "task": f.AddRange([new("status", "Status", "choice", ["open", "in_progress", "completed", "cancelled"]), new("assigned_to", "Assignees", "refs", ReferenceType: "person"), new("due/date", "Due", "date"), new("related", "Related", "related")]); break;
            case "expense": case "budget": f.Add(new("amount", "Amount", "money")); if (e.Type == "expense") f.AddRange([new("estimated", "Estimated", "bool"), new("paid_by", "Payer", "refs", ReferenceType: "person"), new("related", "Related", "related")]); break;
            case "collection": f.Add(new("items", "Items", "refs")); break;
            case "comment": f.AddRange([new("target", "Target", "target"), new("created_at", "Created", "readonly"), new("body", "Body", "multiline"), new("author", "Author", "ref", ReferenceType: "person"), new("mentions", "Mention", "refs", ReferenceType: "person")]); break;
            case "document": f.AddRange([new("caption", "Caption"), new("tags", "Tags", "list")]); break;
            case "note": f.Add(new("body", "Body", "multiline")); break;
        }
        return f;
    }
    public static JsonNode? Get(Entity e, string path)
    { JsonNode? n = e.Data; foreach (var k in path.Split('/')) n = n is JsonObject o ? o[k] : null; return n; }
    public static void Set(Entity e, string path, JsonNode? value)
    {
        var keys = path.Split('/'); JsonObject n = e.Data;
        foreach (var k in keys[..^1]) { if (n[k] is not JsonObject) n[k] = new JsonObject(); n = (JsonObject)n[k]!; }
        if (value is null) n.Remove(keys[^1]); else n[keys[^1]] = value.DeepClone();
    }
    public static async Task UpdateAsync(MainViewModel vm, Guid id, EditorField field, string value)
    {
        if (vm.Workspace?.State.Trip.Find(id) is not { } existing) return;
        var e = existing.Copy(); JsonNode? node = string.IsNullOrEmpty(value) ? null : JsonValue.Create(value);
        if (field.Kind == "distance") node = JsonValue.Create(DisplayValues.ParseDistance(value, vm.Store.Get("units") == "Imperial", CultureInfo.InvariantCulture));
        if (field.Kind == "bool") node = JsonValue.Create(value == "true");
        if (field.Kind == "list") node = new JsonArray(value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(s => (JsonNode?)JsonValue.Create(s)).ToArray());
        Set(e, field.Path, node);
        if (field.Path.StartsWith("participants/", StringComparison.Ordinal)) { e.Data["participants"]!["inherit"] ??= false; e.Data["participants"]!["values"] ??= new JsonArray(); }
        await vm.Workspace.EditAsync(e);
    }
    public static async Task ComplexAsync(MainViewModel vm, Guid id, EditorField field)
    {
        if (vm.Workspace?.State.Trip.Find(id) is not { } existing) return; var e = existing.Copy(); var strings = vm.Strings;
        if (field.Kind == "refs")
        {
            var current = ((field.Path == "participants/values" ? TripQueries.Resolve(vm.Workspace.State.Trip, id, "participants").Value : Get(e, field.Path)) as JsonArray)?.Select(n => n!.ToString()).ToArray() ?? [];
            var selected = await vm.Interaction.SelectManyAsync(strings[field.Label], vm.Workspace.State.Trip.Entities.Values.Where(x => x.Id != id && (field.ReferenceType is null || x.Type == field.ReferenceType)).Select(x => new Choice(x.Id.ToString(), x.Title)).ToArray(), current);
            if (selected is null) return; Set(e, field.Path, new JsonArray(selected.Select(v => JsonValue.Create(v)).ToArray()));
            if (field.Path.StartsWith("participants/", StringComparison.Ordinal)) e.Data["participants"]!["inherit"] = false;
        }
        if (field.Kind is "ref" or "stops" or "related" or "target")
        {
            var choice = await vm.Interaction.ChooseAsync(strings[field.Label], vm.Workspace.State.Trip.Entities.Values.Where(x => x.Id != id && (field.ReferenceType is null || x.Type == field.ReferenceType)).Select(x => new Choice(x.Id.ToString(), x.Title)).ToArray()); if (choice is null) return;
            if (field.Kind is "target" or "related") { var target = vm.Workspace.State.Trip.Find(Guid.Parse(choice))!; var reference = new JsonObject { ["id"] = choice, ["type"] = target.Type }; if (field.Kind == "target") Set(e, field.Path, reference); else { var values = Get(e, field.Path)?.DeepClone() as JsonArray ?? []; var index = values.Select((v, i) => (v, i)).FirstOrDefault(p => p.v?["id"]?.ToString() == choice, (null, -1)).i; if (index >= 0) values.RemoveAt(index); else values.Add(reference); Set(e, field.Path, values); } }
            else if (field.Kind == "ref") Set(e, field.Path, JsonValue.Create(choice));
            else if (field.Kind == "stops") { var stops = Get(e, field.Path)?.DeepClone() as JsonArray ?? []; stops.Add(new JsonObject { ["place"] = choice }); Set(e, field.Path, stops); }

            if (field.Path.StartsWith("participants/", StringComparison.Ordinal)) e.Data["participants"]!["inherit"] ??= false;
        }
        if (field.Kind is "date" or "timezone" or "language" or "duration" or "distance")
        {
            var initial = Get(e, field.Path)?.ToString() ?? "";
            if (field.Kind == "distance" && double.TryParse(initial, CultureInfo.InvariantCulture, out var meters)) initial = (meters / (vm.Store.Get("units") == "Imperial" ? 1609.344 : 1000)).ToString(CultureInfo.InvariantCulture);
            var result = await vm.Interaction.FormAsync(strings[field.Label], [new("value", strings[field.Label] + (field.Kind == "distance" ? vm.Store.Get("units") == "Imperial" ? " (mi)" : " (km)" : ""), field.Kind == "distance" ? "number" : field.Kind, initial)]);
            if (result is null) return;
            await UpdateAsync(vm, id, field, result["value"]); return;
        }
        if (field.Kind == "zoned")
        {
            var current = Get(e, field.Path);
            var result = await vm.Interaction.FormAsync(strings[field.Label], [new("local", strings[field.Label], "datetime", current?["local"]?.ToString() ?? vm.StartDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "T15:00:00", Required: true), new("zone", strings["Timezone"], "timezone", current?["timezone"]?.ToString() ?? vm.Workspace.State.Trip.Manifest.Data["default_timezone"]?.ToString() ?? "Etc/UTC", Required: true)]);
            if (result is null) return;
            var value = await vm.Interaction.ResolveTimeAsync(result["local"], result["zone"], current?["offset"]?.ToString());
            if (value is null) return; value.ToInstant(); Set(e, field.Path, value.ToJson());
        }
        if (field.Kind == "money")
        {
            var result = await vm.Interaction.FormAsync(strings[field.Label], [new("amount", strings["Amount"], "number", Get(e, field.Path)?["value"]?.ToString() ?? "0.00", Required: true), new("currency", strings["Currency"], "choice", Get(e, field.Path)?["currency"]?.ToString() ?? "EUR", CultureInfo.GetCultures(CultureTypes.SpecificCultures).Select(c => new RegionInfo(c.Name)).DistinctBy(r => r.ISOCurrencySymbol).OrderBy(r => r.ISOCurrencySymbol).Select(r => new Choice(r.ISOCurrencySymbol, r.ISOCurrencySymbol + " · " + r.CurrencyEnglishName)).ToArray(), true)]);
            if (result is null) return; Set(e, field.Path, new JsonObject { ["value"] = result["amount"], ["currency"] = result["currency"] });
        }
        if (field.Kind == "coordinates")
        {
            var result = await vm.Interaction.FormAsync(strings[field.Label], [new("latitude", strings["Latitude"], "text", Get(e, field.Path)?["latitude"]?.ToString() ?? "", Required: true), new("longitude", strings["Longitude"], "text", Get(e, field.Path)?["longitude"]?.ToString() ?? "", Required: true)]);
            if (result is null) return;
            if (!Jourfold.Application.Coordinates.TryParse(result["latitude"], out var lat) || !Jourfold.Application.Coordinates.IsLatitude(lat)) { await vm.Interaction.ShowAsync(strings[field.Label], strings["LatitudeRange"]); return; }
            if (!Jourfold.Application.Coordinates.TryParse(result["longitude"], out var lon) || !Jourfold.Application.Coordinates.IsLongitude(lon)) { await vm.Interaction.ShowAsync(strings[field.Label], strings["LongitudeRange"]); return; }
            Set(e, field.Path, new JsonObject { ["latitude"] = Math.Round(lat, 6), ["longitude"] = Math.Round(lon, 6) });
        }
        if (field.Kind == "identities")
        { var name = await vm.Interaction.PromptAsync(strings["Name"]); var email = await vm.Interaction.PromptAsync(strings["Email"]); if (name is null || email is null) return; var a = Get(e, field.Path)?.DeepClone() as JsonArray ?? []; a.Add(new JsonObject { ["name"] = name, ["email"] = email }); Set(e, field.Path, a); }
        if (field.Kind == "github") { var name = await vm.Interaction.PromptAsync(strings["Username"]); if (name is null) return; var a = Get(e, field.Path)?.DeepClone() as JsonArray ?? []; a.Add(new JsonObject { ["username"] = name }); Set(e, field.Path, a); }
        await vm.Workspace.EditAsync(e);
    }
    public static async Task TimeAsync(MainViewModel vm)
    {
        if (vm.Workspace is null || vm.Selected is null) return;
        var id = vm.Selected.Id;
        var time = await vm.Interaction.ScheduleAsync(vm.Selected.Data["time"] as JsonObject, vm.StartDate, vm.Workspace.State.Trip.Manifest.Data["default_timezone"]?.ToString() ?? "Etc/UTC");
        if (time is null) return;
        var e = vm.Workspace.State.Trip.Find(id)!.Copy(); e.Data["time"] = time["precision"]?.ToString() == "unscheduled" ? null : time;
        await vm.Workspace.EditAsync(e);
    }
}
