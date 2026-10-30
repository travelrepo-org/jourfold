using System.Globalization;
using System.Text.Json.Nodes;
using TravelRepo.Core;

namespace Jourfold.UI;

/// <summary>How a merge conflict is described to the user: which field, and each side's value in words.</summary>
public static class MergeText
{
    public static string Field(string path, Localization strings)
    {
        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return strings["WholeItem"];
        if (parts[0] == "resources") return strings["Content"];
        var key = parts[0] == "components" && parts.Length > 1 ? parts[1] : parts[0];
        return strings["field." + key] is var label && label != "field." + key ? label : key.Replace('_', ' ');
    }

    public static string Value(JsonNode? value, TripSnapshot trip, Localization strings)
    {
        if (value is null) return strings["Removed"];
        if (value is JsonArray array) return array.Count == 0 ? strings["Empty"] : string.Join(", ", array.Select(v => Value(v, trip, strings)));
        if (value is JsonObject obj)
        {
            if (obj["local"] is { } local && obj["timezone"] is { } zone && NodaTime.Text.LocalDateTimePattern.ExtendedIso.Parse(local.ToString()).TryGetValue(default, out var wall)) return wall.ToString("g", CultureInfo.CurrentCulture) + " (" + Formats.ZoneName(zone.ToString()) + ")";
            if (TripSummaries.Money(obj) is { } money) return Formats.Money(money.Amount, money.Currency);
            return string.Join("; ", obj.Where(p => p.Key is not ("id" or "extensions") && p.Value is not null).Select(p => Field(p.Key, strings) + ": " + Value(p.Value, trip, strings)));
        }
        return Guid.TryParse(value.ToString(), out var id) && trip.Find(id) is { } entity ? entity.Title : strings.Optional(value.ToString());
    }
}
