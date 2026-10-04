using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using NodaTime;
using TravelRepo.Core;
using TravelRepo.Git;
using TravelRepo.Repository;

namespace Jourfold.Application;

/// <summary>
/// Creates the clearly labelled sample trip offered on an empty library. The content is illustrative:
/// places are real, times, bookings and prices are invented. The manifest carries the
/// <c>org.jourfold.sample</c> extension so clients can label it.
/// </summary>
public static class SampleTrip
{
    public const string Extension = "org.jourfold.sample";

    /// <summary>The first Friday at least six weeks from <paramref name="today"/>.</summary>
    public static LocalDate SuggestedStart(LocalDate today)
    {
        var day = today.PlusWeeks(6);
        while (day.DayOfWeek != IsoDayOfWeek.Friday) day = day.PlusDays(1);
        return day;
    }

    public static bool IsSample(TripSnapshot trip) => trip.Manifest.Data["extensions"]?[Extension] is not null;

    public static async Task CreateAsync(string path, IGitBackend backend, LocalDate start, string authorName, string authorEmail, string? recoveryRoot = null, CancellationToken ct = default)
    {
        var repo = new TravelRepository(path, recoveryRoot);
        var manifest = Entity.CreateTrip("Spring in Japan", "en", "Asia/Tokyo");
        await repo.InitializeAsync(manifest, ct);
        var (edits, resources) = Build(manifest, start);
        await repo.ApplyAsync(await repo.ReadAsync(ct), edits, ct, resources);
        await new GitRepository(repo, backend).InitializeAsync(authorName, authorEmail, ct);
    }

    public static (EntityEdit[] Edits, ResourceEdit[] Resources) Build(Entity manifest, LocalDate start)
    {
        var edits = new List<EntityEdit>(); var resources = new List<ResourceEdit>();
        string D(int day, string time) => start.PlusDays(day).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "T" + time + ":00";
        Entity Add(Entity e) { edits.Add(new(e.Id, e)); return e; }

        var alex = Add(Entity.Create("person", "Alex")); alex.Data["roles"] = new JsonArray("traveler", "organizer");
        var sam = Add(Entity.Create("person", "Sam")); sam.Data["roles"] = new JsonArray("traveler");
        var both = new[] { alex, sam };

        Entity Place(string name, double lat, double lon, string zone, string address)
        {
            var p = Add(Entity.Create("place", name));
            p.Data["location"] = new JsonObject { ["latitude"] = lat, ["longitude"] = lon };
            p.Data["timezone"] = zone; p.Data["address"] = new JsonObject { ["formatted"] = address };
            return p;
        }
        var fra = Place("Frankfurt Airport", 50.0379, 8.5622, "Europe/Berlin", "Frankfurt am Main, Germany");
        var hnd = Place("Tokyo Haneda Airport", 35.5494, 139.7798, "Asia/Tokyo", "Ota City, Tokyo, Japan");
        var gracery = Place("Hotel Gracery Shinjuku", 35.6950, 139.7020, "Asia/Tokyo", "Kabukicho, Shinjuku City, Tokyo");
        var omoide = Place("Omoide Yokocho", 35.6929, 139.6996, "Asia/Tokyo", "Nishishinjuku, Shinjuku City, Tokyo");
        var tsukiji = Place("Tsukiji Outer Market", 35.6654, 139.7707, "Asia/Tokyo", "Tsukiji, Chuo City, Tokyo");
        var sensoji = Place("Senso-ji", 35.7148, 139.7967, "Asia/Tokyo", "Asakusa, Taito City, Tokyo");
        var teamlab = Place("teamLab Planets", 35.6491, 139.7898, "Asia/Tokyo", "Toyosu, Koto City, Tokyo");
        var ginza = Place("Ginza", 35.6717, 139.7650, "Asia/Tokyo", "Chuo City, Tokyo");
        var toshogu = Place("Nikko Toshogu", 36.7581, 139.5989, "Asia/Tokyo", "Nikko, Tochigi");
        var kegon = Place("Kegon Falls", 36.7383, 139.5014, "Asia/Tokyo", "Chugushi, Nikko, Tochigi");
        var tokyoStation = Place("Tokyo Station", 35.6812, 139.7671, "Asia/Tokyo", "Marunouchi, Chiyoda City, Tokyo");
        var kyotoStation = Place("Kyoto Station", 34.9858, 135.7588, "Asia/Tokyo", "Shimogyo Ward, Kyoto");
        var ryokan = Place("Ryokan in Gion", 35.0037, 135.7788, "Asia/Tokyo", "Gion, Higashiyama Ward, Kyoto");
        var inari = Place("Fushimi Inari Taisha", 34.9671, 135.7727, "Asia/Tokyo", "Fushimi Ward, Kyoto");
        var bamboo = Place("Arashiyama Bamboo Grove", 35.0170, 135.6713, "Asia/Tokyo", "Ukyo Ward, Kyoto");
        var nishiki = Place("Nishiki Market", 35.0050, 135.7649, "Asia/Tokyo", "Nakagyo Ward, Kyoto");
        var nara = Place("Nara Park", 34.6851, 135.8430, "Asia/Tokyo", "Nara");
        var kix = Place("Kansai Airport", 34.4320, 135.2304, "Asia/Tokyo", "Izumisano, Osaka");

        JsonObject People(params Entity[] people) => new() { ["inherit"] = false, ["values"] = new JsonArray(people.Select(p => (JsonNode?)JsonValue.Create(p.Id.ToString())).ToArray()) };
        JsonObject Zoned(int day, string time, string zone = "Asia/Tokyo") => new ZonedTime(D(day, time), zone).ToJson();
        Entity Item(string title, string status, JsonObject? time, string? category = null, Entity? place = null, Entity[]? people = null)
        {
            var e = Add(Entity.Create("schedule_item", title)); e.Data["status"] = status; e.Data["time"] = time;
            if (category is not null) e.Data["category"] = category;
            if (place is not null) e.Data["default_place"] = place.Id.ToString();
            e.Data["participants"] = People(people ?? both);
            return e;
        }
        JsonObject Exact(int day, string from, int endDay, string to, string zone = "Asia/Tokyo", string? endZone = null) => new() { ["precision"] = "exact", ["start"] = Zoned(day, from, zone), ["end"] = Zoned(endDay, to, endZone ?? zone) };
        Entity Transport(string title, string status, JsonObject time, string type, Entity from, Entity to, string? carrier = null, string? number = null)
        {
            var e = Item(title, status, time);
            var t = new JsonObject { ["type"] = type, ["departure"] = new JsonObject { ["place"] = from.Id.ToString() }, ["arrival"] = new JsonObject { ["place"] = to.Id.ToString() } };
            if (carrier is not null) t["carrier"] = carrier; if (number is not null) t["flight_number"] = number;
            e.Data["components"]!["transport"] = t; return e;
        }
        Entity Stay(string title, string status, Entity place, int fromDay, int toDay)
        {
            var e = Item(title, status, Exact(fromDay, "15:00", toDay, "11:00"));
            e.Data["components"]!["accommodation"] = new JsonObject { ["place"] = place.Id.ToString(), ["check_in"] = Zoned(fromDay, "15:00"), ["check_out"] = Zoned(toDay, "11:00"), ["guests"] = new JsonArray(alex.Id.ToString(), sam.Id.ToString()), ["rooms"] = new JsonArray("Twin room") };
            return e;
        }

        var outbound = Transport("Flight to Tokyo", "confirmed", Exact(0, "13:20", 1, "08:35", "Europe/Berlin", "Asia/Tokyo"), "flight", fra, hnd, "Lufthansa", "LH 716");
        outbound.Data["components"]!["transport"]!["seat"] = "32A, 32C";
        var hotel = Stay("Hotel Gracery Shinjuku", "confirmed", gracery, 1, 4);
        Item("Explore Shinjuku", "planned", Exact(1, "16:00", 1, "18:00"), "sightseeing", gracery);
        Item("Dinner at Omoide Yokocho", "planned", Exact(1, "19:30", 1, "21:00"), "food", omoide);
        Item("Breakfast at Tsukiji", "planned", Exact(2, "08:30", 2, "10:00"), "food", tsukiji);
        Item("Senso-ji and Asakusa", "planned", Exact(2, "11:00", 2, "13:00"), "culture", sensoji);
        var planets = Item("teamLab Planets", "confirmed", Exact(2, "15:30", 2, "17:30"), "culture", teamlab);
        resources.Add(new("documents/teamlab-notes.md", Encoding.UTF8.GetBytes("Entry slot **15:30**. Wear shorts or trousers you can roll up: some rooms have knee-deep water.\n\n- Lockers at the entrance\n- Allow about two hours\n")));
        planets.Data["content"] = new JsonArray(new JsonObject { ["type"] = "markdown", ["file"] = "documents/teamlab-notes.md" }, new JsonObject { ["type"] = "link", ["url"] = "https://www.teamlab.art/e/planets/" });
        Item("Dinner in Ginza", "idea", new JsonObject { ["precision"] = "approximate", ["start"] = Zoned(2, "19:00"), ["end"] = Zoned(2, "21:00") }, "food", ginza);

        var trainOut = Transport("Train to Nikko", "planned", Exact(3, "08:10", 3, "10:00"), "train", tokyoStation, toshogu, "JR / Tobu");
        var shrine = Item("Toshogu Shrine", "planned", Exact(3, "10:30", 3, "12:30"), "culture", toshogu);
        var falls = Item("Kegon Falls", "planned", Exact(3, "13:30", 3, "15:00"), "nature", kegon);
        var trainBack = Transport("Train back to Tokyo", "planned", Exact(3, "16:30", 3, "18:20"), "train", toshogu, tokyoStation, "JR / Tobu");
        var nikko = Item("Day trip to Nikko", "planned", null, "nature");
        nikko.Data["children"] = new JsonArray(trainOut.Id.ToString(), shrine.Id.ToString(), falls.Id.ToString(), trainBack.Id.ToString());
        foreach (var child in new[] { trainOut, shrine, falls, trainBack }) child.Data["participants"] = new JsonObject { ["inherit"] = true, ["values"] = new JsonArray() };

        var shinkansen = Transport("Shinkansen to Kyoto", "reserved", Exact(4, "08:30", 4, "10:45"), "train", tokyoStation, kyotoStation, "JR Central", "Nozomi 215");
        shinkansen.Data["components"]!["transport"]!["seat"] = "Car 7, 12D-E";
        var stay2 = Stay("Ryokan in Gion", "reserved", ryokan, 4, 7);
        Item("Fushimi Inari gates", "planned", new JsonObject { ["precision"] = "day_part", ["date"] = start.PlusDays(4).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), ["day_part"] = "afternoon", ["timezone"] = "Asia/Tokyo" }, "sightseeing", inari);
        var kaiseki = Item("Kaiseki dinner", "confirmed", Exact(4, "19:00", 4, "21:00"), "food", ryokan);
        Item("Arashiyama bamboo grove", "planned", new JsonObject { ["precision"] = "day_part", ["date"] = start.PlusDays(5).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), ["day_part"] = "morning", ["timezone"] = "Asia/Tokyo" }, "nature", bamboo);
        Item("Lunch at Nishiki Market", "idea", new JsonObject { ["precision"] = "approximate", ["start"] = Zoned(5, "12:30"), ["end"] = Zoned(5, "13:30") }, "food", nishiki);
        var tea = Item("Tea ceremony", "confirmed", Exact(5, "15:00", 5, "16:00"), "culture", ryokan, [sam]);
        Item("Nara deer park", "planned", new JsonObject { ["precision"] = "all_day", ["date"] = start.PlusDays(6).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) }, "nature", nara);
        var homebound = Transport("Flight home", "confirmed", Exact(7, "11:15", 7, "17:05", "Asia/Tokyo", "Europe/Berlin"), "flight", kix, fra, "Lufthansa", "LH 741");

        var onsen = Item("Onsen evening?", "idea", null, "free_time"); onsen.Data["duration"] = "PT2H";
        var sake = Item("Sake tasting in Fushimi", "idea", null, "food"); sake.Data["duration"] = "PT1H30M";
        var packing = Add(Entity.Create("note", "Packing list")); packing.Data["body"] = "- Rail pass voucher\n- Power adapter (type A)\n- Comfortable walking shoes\n- Small towel for the onsen";

        Entity Money(string type, string title, string value, string currency, bool estimated = false, Entity? payer = null)
        {
            var e = Add(Entity.Create(type, title)); e.Data["amount"] = new JsonObject { ["value"] = value, ["currency"] = currency };
            if (type == "expense") { e.Data["estimated"] = estimated; if (payer is not null) e.Data["paid_by"] = new JsonArray(payer.Id.ToString()); }
            return e;
        }
        Entity Booking(string title, string provider, string reference, string status, string value, string currency, params Entity[] items)
        {
            var b = Add(Entity.Create("booking", title)); b.Data["provider"] = new JsonObject { ["name"] = provider }; b.Data["reference"] = reference; b.Data["status"] = status;
            b.Data["price"] = new JsonObject { ["value"] = value, ["currency"] = currency };
            b.Data["travelers"] = new JsonArray(alex.Id.ToString(), sam.Id.ToString());
            b.Data["items"] = new JsonArray(items.Select(i => (JsonNode?)JsonValue.Create(i.Id.ToString())).ToArray());
            foreach (var item in items) item.Data["components"]!["booking"] = new JsonObject { ["ref"] = b.Id.ToString() };
            return b;
        }
        var flights = Booking("Flights Frankfurt and Japan", "Lufthansa", "LH8KQ2", "confirmed", "1749.80", "EUR", outbound, homebound);
        Booking("Hotel Gracery Shinjuku", "Hotel Gracery", "HG-20931", "confirmed", "46800", "JPY", hotel);
        Booking("teamLab Planets tickets", "teamLab", "TLP-55120", "confirmed", "7600", "JPY", planets);
        Booking("Ryokan in Gion", "Gion Ryokan", "GR-1187", "reserved", "96000", "JPY", stay2, kaiseki);
        Booking("Tea ceremony", "Camellia", "CAM-302", "confirmed", "4400", "JPY", tea);

        var flightCost = Money("expense", "Flights", "1749.80", "EUR", payer: alex); flightCost.Data["related"] = new JsonArray(new JsonObject { ["type"] = "booking", ["id"] = flights.Id.ToString() });
        Money("expense", "Hotel Gracery deposit", "46800", "JPY", payer: alex);
        Money("expense", "teamLab tickets", "7600", "JPY", payer: sam);
        Money("expense", "Rail passes", "100000", "JPY", payer: sam);
        Money("expense", "Food and snacks", "60000", "JPY", estimated: true);
        Money("budget", "Trip budget", "3500.00", "EUR");
        Money("budget", "Spending money in Japan", "250000", "JPY");

        Entity Task(string title, string status, int dueOffset, Entity owner)
        {
            var t = Add(Entity.Create("task", title)); t.Data["status"] = status;
            t.Data["due"] = new JsonObject { ["date"] = start.PlusDays(dueOffset).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) };
            t.Data["assigned_to"] = new JsonArray(owner.Id.ToString()); return t;
        }
        Task("Check passport validity", "completed", -40, alex);
        var railTask = Task("Exchange rail pass voucher", "open", 1, sam); railTask.Data["related"] = new JsonArray(new JsonObject { ["type"] = "schedule_item", ["id"] = shinkansen.Id.ToString() });
        Task("Rent pocket Wi-Fi", "open", -7, alex);
        Task("Book kaiseki dinner", "completed", -21, sam);

        var restaurants = Add(Entity.Create("collection", "Food to try"));
        restaurants.Data["items"] = new JsonArray(omoide.Id.ToString(), tsukiji.Id.ToString(), nishiki.Id.ToString(), kaiseki.Id.ToString());

        var comment = Add(Entity.Create("comment", "Comment"));
        comment.Data.Remove("title"); comment.Data["target"] = new JsonObject { ["type"] = "schedule_item", ["id"] = shinkansen.Id.ToString() };
        comment.Data["author"] = sam.Id.ToString(); comment.Data["created_at"] = SystemClock.Instance.GetCurrentInstant().ToString();
        comment.Data["body"] = "Window seats on the right side for a view of Mount Fuji.";

        var updated = manifest.Copy();
        updated.Data["participants"] = new JsonArray(alex.Id.ToString(), sam.Id.ToString());
        updated.Data["dates"] = new JsonObject { ["start"] = start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), ["end"] = start.PlusDays(7).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) };
        updated.Data["extensions"]![Extension] = new JsonObject { ["version"] = 1 };
        updated.Data["variant"]!["title"] = "Main plan";
        edits.Insert(0, new(updated.Id, updated));
        return (edits.ToArray(), resources.ToArray());
    }
}
