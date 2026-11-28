import Foundation

/// The sample trip "Spring in Japan" as in Jourfold's SampleTrip.cs, plus two small trips for the trip list.
/// Synthetic data only (AGENTS.md): Alex and Sam, the listed cities, EUR and JPY.
enum SampleData {
    static let tokyo = "Asia/Tokyo"
    static let berlin = "Europe/Berlin"

    static func day(_ y: Int, _ m: Int, _ d: Int) -> DateComponents { DateComponents(year: y, month: m, day: d) }

    static func at(_ y: Int, _ m: Int, _ d: Int, _ time: String, _ zone: String = tokyo) -> Date {
        let parts = time.split(separator: ":").map { Int($0)! }
        var c = Calendar(identifier: .gregorian); c.timeZone = TimeZone(identifier: zone)!
        return c.date(from: DateComponents(year: y, month: m, day: d, hour: parts[0], minute: parts[1]))!
    }

    /// Saturday 9 January 2027, 14:10 in Tokyo: the travellers have landed and the hotel check-in is next.
    static let demoNow = at(2027, 1, 9, "14:10")

    static func springInJapan() -> Trip {
        let start = 8 // January 2027
        func d(_ offset: Int) -> Int { start + offset }
        func exact(_ day: Int, _ from: String, _ endDay: Int, _ to: String, _ zone: String = tokyo, _ endZone: String? = nil) -> When {
            .exact(start: at(2027, 1, d(day), from, zone), startZone: zone, end: at(2027, 1, d(endDay), to, endZone ?? zone), endZone: endZone ?? zone)
        }

        let alex = Person(id: UUID.v7(), name: "Alex", isOrganizer: true)
        let sam = Person(id: UUID.v7(), name: "Sam")
        let both = [alex.id, sam.id]

        func place(_ name: String, _ lat: Double, _ lon: Double, _ zone: String, _ address: String) -> Place {
            Place(id: UUID.v7(), name: name, latitude: lat, longitude: lon, zone: zone, address: address)
        }
        let fra = place("Frankfurt Airport", 50.0379, 8.5622, berlin, "Frankfurt Airport, Frankfurt am Main, Germany")
        let hnd = place("Tokyo Haneda Airport", 35.5494, 139.7798, tokyo, "Haneda, Ota City, Tokyo, Japan")
        var gracery = place("Hotel Gracery Shinjuku", 35.6950, 139.7020, tokyo, "Kabukicho, Shinjuku City, Tokyo")
        gracery.localName = "ホテルグレイスリー新宿"; gracery.localAddress = "東京都新宿区歌舞伎町"
        let omoide = place("Omoide Yokocho", 35.6929, 139.6996, tokyo, "Nishishinjuku, Shinjuku City, Tokyo")
        let tsukiji = place("Tsukiji Outer Market", 35.6654, 139.7707, tokyo, "Tsukiji, Chuo City, Tokyo")
        let sensoji = place("Senso-ji", 35.7148, 139.7967, tokyo, "Asakusa, Taito City, Tokyo")
        let teamlab = place("teamLab Planets", 35.6491, 139.7898, tokyo, "Toyosu, Koto City, Tokyo")
        let ginza = place("Ginza", 35.6717, 139.7650, tokyo, "Ginza, Chuo City, Tokyo")
        let toshogu = place("Nikko Toshogu", 36.7581, 139.5989, tokyo, "Sannai, Nikko")
        let kegon = place("Kegon Falls", 36.7383, 139.5014, tokyo, "Chugushi, Nikko")
        let tokyoStation = place("Tokyo Station", 35.6812, 139.7671, tokyo, "Marunouchi, Chiyoda City, Tokyo")
        let kyotoStation = place("Kyoto Station", 34.9858, 135.7588, tokyo, "Shimogyo Ward, Kyoto")
        let ryokan = place("Ryokan in Gion", 35.0037, 135.7788, tokyo, "Gion, Higashiyama Ward, Kyoto")
        let inari = place("Fushimi Inari Taisha", 34.9671, 135.7727, tokyo, "Fushimi Ward, Kyoto")
        let bamboo = place("Arashiyama Bamboo Grove", 35.0170, 135.6713, tokyo, "Ukyo Ward, Kyoto")
        let nishiki = place("Nishiki Market", 35.0050, 135.7649, tokyo, "Nakagyo Ward, Kyoto")
        let naraPark = place("Nara Park", 34.6851, 135.8430, tokyo, "Nara Park, Nara")
        let kix = place("Kansai Airport", 34.4320, 135.2304, tokyo, "Izumisano, Osaka")
        let places = [fra, hnd, gracery, omoide, tsukiji, sensoji, teamlab, ginza, toshogu, kegon, tokyoStation, kyotoStation, ryokan, inari, bamboo, nishiki, naraPark, kix]

        var items: [PlanItem] = []
        @discardableResult
        func item(_ title: String, _ status: Status, _ kind: Kind, _ when: When, _ place: Place? = nil, people: [UUID]? = nil) -> UUID {
            let i = PlanItem(id: UUID.v7(), title: title, status: status, kind: kind, when: when, place: place?.id, participants: people ?? both)
            items.append(i); return i.id
        }
        func transport(_ title: String, _ status: Status, _ when: When, _ mode: TransportMode, _ from: Place, _ to: Place, _ carrier: String? = nil, _ number: String? = nil, seat: String? = nil) -> UUID {
            var i = PlanItem(id: UUID.v7(), title: title, status: status, kind: .transport, when: when, place: from.id, participants: both)
            i.transport = Transport(mode: mode, from: from.id, to: to.id, carrier: carrier, number: number, seat: seat)
            items.append(i); return i.id
        }
        func stay(_ title: String, _ status: Status, _ place: Place, _ from: Int, _ to: Int) -> UUID {
            var i = PlanItem(id: UUID.v7(), title: title, status: status, kind: .accommodation, when: exact(from, "15:00", to, "11:00"), place: place.id, participants: both)
            i.stay = Stay(place: place.id, room: "Twin room")
            items.append(i); return i.id
        }

        let outbound = transport("Flight to Tokyo", .confirmed, exact(0, "13:20", 1, "08:35", berlin, tokyo), .flight, fra, hnd, "Lufthansa", "LH 716", seat: "32A, 32C")
        let hotel = stay("Hotel Gracery Shinjuku", .confirmed, gracery, 1, 4)
        item("Explore Shinjuku", .planned, .sightseeing, exact(1, "16:00", 1, "18:00"), gracery)
        item("Dinner at Omoide Yokocho", .planned, .food, exact(1, "19:30", 1, "21:00"), omoide)
        item("Breakfast at Tsukiji", .planned, .food, exact(2, "08:30", 2, "10:00"), tsukiji)
        item("Senso-ji and Asakusa", .planned, .culture, exact(2, "11:00", 2, "13:00"), sensoji)
        let planets = item("teamLab Planets", .confirmed, .culture, exact(2, "15:30", 2, "17:30"), teamlab)
        item("Dinner in Ginza", .idea, .food, .approximate(start: at(2027, 1, d(2), "19:00"), end: at(2027, 1, d(2), "21:00"), zone: tokyo), ginza)
        let nikko = item("Day trip to Nikko", .planned, .nature, .unscheduled(minutes: nil))
        let trainOut = transport("Train to Nikko", .planned, exact(3, "08:10", 3, "10:00"), .train, tokyoStation, toshogu, "JR / Tobu")
        let shrine = item("Toshogu Shrine", .planned, .culture, exact(3, "10:30", 3, "12:30"), toshogu)
        let falls = item("Kegon Falls", .planned, .nature, exact(3, "13:30", 3, "15:00"), kegon)
        let trainBack = transport("Train back to Tokyo", .planned, exact(3, "16:30", 3, "18:20"), .train, toshogu, tokyoStation, "JR / Tobu")
        for child in [trainOut, shrine, falls, trainBack] { items[items.firstIndex { $0.id == child }!].parent = nikko }
        let shinkansen = transport("Shinkansen to Kyoto", .reserved, exact(4, "08:30", 4, "10:45"), .train, tokyoStation, kyotoStation, "JR Central", "Nozomi 215", seat: "Car 7, 12D-E")
        let stay2 = stay("Ryokan in Gion", .reserved, ryokan, 4, 7)
        item("Fushimi Inari gates", .planned, .sightseeing, .dayPart(day: day(2027, 1, d(4)), part: .afternoon, zone: tokyo), inari)
        let kaiseki = item("Kaiseki dinner", .confirmed, .food, exact(4, "19:00", 4, "21:00"), ryokan)
        item("Arashiyama bamboo grove", .planned, .nature, .dayPart(day: day(2027, 1, d(5)), part: .morning, zone: tokyo), bamboo)
        item("Lunch at Nishiki Market", .idea, .food, .approximate(start: at(2027, 1, d(5), "12:30"), end: at(2027, 1, d(5), "13:30"), zone: tokyo), nishiki)
        let tea = item("Tea ceremony", .confirmed, .culture, exact(5, "15:00", 5, "16:00"), ryokan, people: [sam.id])
        let deer = item("Nara deer park", .planned, .nature, .allDay(day: day(2027, 1, d(6)), zone: tokyo), naraPark)
        let homebound = transport("Flight home", .confirmed, exact(7, "11:15", 7, "17:05", tokyo, berlin), .flight, kix, fra, "Lufthansa", "LH 741")
        item("Onsen evening?", .idea, .freeTime, .unscheduled(minutes: 120))
        item("Sake tasting in Fushimi", .idea, .food, .unscheduled(minutes: 90))

        func edit(_ id: UUID, _ body: (inout PlanItem) -> Void) { body(&items[items.firstIndex { $0.id == id }!]) }
        edit(shinkansen) {
            $0.attachments = [Attachment(id: UUID.v7(), name: "Shinkansen to Kyoto.pkpass", kind: .walletPass)]
            $0.comments = [Comment(id: UUID.v7(), author: sam.id, body: "Window seats on the right side for a view of Mount Fuji.", date: demoNow.addingTimeInterval(-86400 * 3))]
        }
        edit(outbound) { $0.attachments = [Attachment(id: UUID.v7(), name: "Boarding pass LH 716.pkpass", kind: .walletPass)] }
        edit(planets) {
            $0.notes = "Entry slot 15:30. Wear shorts or trousers you can roll up: some rooms have knee-deep water.\n\n• Lockers at the entrance\n• Allow about two hours"
            $0.attachments = [Attachment(id: UUID.v7(), name: "teamLab Planets e-ticket.pdf", kind: .pdf), Attachment(id: UUID.v7(), name: "teamlab.art/e/planets", kind: .link)]
        }
        edit(deer) { $0.attachments = [Attachment(id: UUID.v7(), name: "Deer crackers.png", kind: .image)] }

        let flights = Booking(id: UUID.v7(), title: "Flights Frankfurt and Japan", provider: "Lufthansa", reference: "LH8KQ2", status: .confirmed, items: [outbound, homebound])
        let graceryBooking = Booking(id: UUID.v7(), title: "Hotel Gracery Shinjuku", provider: "Hotel Gracery", reference: "HG-20931", status: .confirmed, items: [hotel])
        let teamLabBooking = Booking(id: UUID.v7(), title: "teamLab Planets tickets", provider: "teamLab", reference: "TLP-55120", status: .confirmed, items: [planets])
        let ryokanBooking = Booking(id: UUID.v7(), title: "Ryokan in Gion", provider: "Gion Ryokan", reference: "GR-1187", status: .reserved, items: [stay2, kaiseki], freeCancellationUntil: at(2027, 1, 10, "23:59"))
        let teaBooking = Booking(id: UUID.v7(), title: "Tea ceremony", provider: "Camellia", reference: "CAM-302", status: .confirmed, items: [tea])
        let bookings = [flights, graceryBooking, teamLabBooking, ryokanBooking, teaBooking]
        for b in bookings { for i in b.items { edit(i) { $0.booking = b.id } } }

        func eur(_ v: String) -> Money { Money(value: Decimal(string: v)!, currency: "EUR") }
        func jpy(_ v: String) -> Money { Money(value: Decimal(string: v)!, currency: "JPY") }
        let before = at(2026, 12, 10, "12:00", berlin)
        let expenses = [
            Expense(id: UUID.v7(), title: "Flights", amount: eur("1749.80"), category: .transport, paidBy: alex.id, sharedBy: both, booking: flights.id, date: at(2026, 11, 2, "12:00", berlin), settled: Decimal(string: "874.90")!),
            Expense(id: UUID.v7(), title: "Hotel Gracery deposit", amount: jpy("46800"), converted: eur("285.48"), category: .stay, paidBy: alex.id, sharedBy: both, booking: graceryBooking.id, date: before),
            Expense(id: UUID.v7(), title: "teamLab tickets", amount: jpy("7600"), converted: eur("46.36"), category: .activities, paidBy: sam.id, sharedBy: both, booking: teamLabBooking.id, date: at(2026, 12, 27, "12:00", berlin)),
            Expense(id: UUID.v7(), title: "Ryokan in Gion", amount: jpy("96000"), category: .stay, sharedBy: both, estimated: true, booking: ryokanBooking.id),
            Expense(id: UUID.v7(), title: "Tea ceremony", amount: jpy("4400"), category: .activities, paidBy: sam.id, sharedBy: both, booking: teaBooking.id, date: at(2027, 1, 2, "12:00", berlin)),
            Expense(id: UUID.v7(), title: "Rail passes", amount: jpy("100000"), category: .transport, paidBy: sam.id, sharedBy: both, date: at(2026, 12, 20, "12:00", berlin)),
            Expense(id: UUID.v7(), title: "Food and snacks", amount: jpy("60000"), category: .food, sharedBy: both, estimated: true),
        ]
        let budgets = [
            Budget(id: UUID.v7(), title: "Trip budget", amount: eur("3500.00")),
            Budget(id: UUID.v7(), title: "Eating out", amount: eur("400.00"), category: .food),
        ]
        let tasks = [
            TripTask(id: UUID.v7(), title: "Check passport validity", done: true, due: at(2026, 11, 29, "12:00", berlin), assignee: alex.id),
            TripTask(id: UUID.v7(), title: "Exchange rail pass voucher", done: false, due: at(2027, 1, 9, "12:00"), assignee: sam.id, related: shinkansen),
            TripTask(id: UUID.v7(), title: "Rent pocket Wi-Fi", done: false, due: at(2027, 1, 1, "12:00", berlin), assignee: alex.id),
            TripTask(id: UUID.v7(), title: "Book kaiseki dinner", done: true, due: at(2026, 12, 18, "12:00", berlin), assignee: sam.id),
        ]
        let packing = PackingList(id: UUID.v7(), title: "Packing for Japan", items: [
            PackingItem(id: UUID.v7(), title: "Passport", group: "Documents", people: [], everyone: true, packedBy: [alex.id]),
            PackingItem(id: UUID.v7(), title: "Printed JR Pass exchange orders", group: "Documents", people: [sam.id], packedBy: [sam.id]),
            PackingItem(id: UUID.v7(), title: "Plug adapter type A", group: "Electronics", quantity: "2", people: [alex.id]),
            PackingItem(id: UUID.v7(), title: "Power bank", group: "Electronics", people: [alex.id, sam.id]),
            PackingItem(id: UUID.v7(), title: "Rain jacket", group: "Clothes", people: [], everyone: true),
            PackingItem(id: UUID.v7(), title: "Comfortable walking shoes", group: "Clothes", people: [], everyone: true, packedBy: [alex.id, sam.id]),
            PackingItem(id: UUID.v7(), title: "Snacks for the Shinkansen", group: "Food", people: []),
        ])
        let versions = [
            Version(id: UUID.v7(), title: "Kyoto days settled", author: "Sam", date: at(2027, 1, 3, "20:15", berlin)),
            Version(id: UUID.v7(), title: "Hotels booked", author: "Alex", date: at(2026, 12, 12, "09:40", berlin)),
            Version(id: UUID.v7(), title: "Create trip", author: "Alex", date: at(2026, 11, 1, "18:02", berlin)),
        ]
        return Trip(id: UUID.v7(), title: "Spring in Japan", start: day(2027, 1, 8), end: day(2027, 1, 15), zone: tokyo, currency: "EUR", colorHue: 0.93,
                    variant: "Main plan", people: [alex, sam], places: places, items: items, bookings: bookings, expenses: expenses, budgets: budgets,
                    tasks: tasks, packing: [packing], versions: versions,
                    notes: "• Rail pass voucher\n• Power adapter (type A)\n• Comfortable walking shoes\n• Small towel for the onsen")
    }

    static func weekendInParis() -> Trip {
        let paris = "Europe/Paris"
        let alex = Person(id: UUID.v7(), name: "Alex", isOrganizer: true)
        let louvre = Place(id: UUID.v7(), name: "Louvre", latitude: 48.8606, longitude: 2.3376, zone: paris, address: "Rue de Rivoli, Paris, France")
        let gare = Place(id: UUID.v7(), name: "Gare de Lyon", latitude: 48.8443, longitude: 2.3744, zone: paris, address: "Place Louis-Armand, Paris, France")
        let fhbf = Place(id: UUID.v7(), name: "Frankfurt Hauptbahnhof", latitude: 50.1071, longitude: 8.6638, zone: berlin, address: "Am Hauptbahnhof, Frankfurt am Main, Germany")
        var train = PlanItem(id: UUID.v7(), title: "ICE to Paris", status: .confirmed, kind: .transport,
                             when: .exact(start: at(2027, 4, 23, "16:58", berlin), startZone: berlin, end: at(2027, 4, 23, "20:47", paris), endZone: paris), place: fhbf.id, participants: [alex.id])
        train.transport = Transport(mode: .train, from: fhbf.id, to: gare.id, carrier: "DB / SNCF", number: "ICE 9558")
        let museum = PlanItem(id: UUID.v7(), title: "Louvre in the morning", status: .planned, kind: .culture, when: .dayPart(day: day(2027, 4, 24), part: .morning, zone: paris), place: louvre.id, participants: [alex.id])
        return Trip(id: UUID.v7(), title: "Weekend in Paris", start: day(2027, 4, 23), end: day(2027, 4, 25), zone: paris, currency: "EUR", colorHue: 0.58, variant: "Main plan",
                    people: [alex], places: [louvre, gare, fhbf], items: [train, museum], bookings: [], expenses: [], budgets: [], tasks: [], packing: [], versions: [], notes: "")
    }

    static func newZealandSummer() -> Trip {
        let nz = "Pacific/Auckland"
        let alex = Person(id: UUID.v7(), name: "Alex", isOrganizer: true)
        let sam = Person(id: UUID.v7(), name: "Sam")
        let picton = Place(id: UUID.v7(), name: "Picton Ferry Terminal", latitude: -41.2873, longitude: 174.0059, zone: nz, address: "Auckland Street, Picton, New Zealand")
        let wellington = Place(id: UUID.v7(), name: "Wellington Ferry Terminal", latitude: -41.2706, longitude: 174.7860, zone: nz, address: "Aotea Quay, Wellington, New Zealand")
        var ferry = PlanItem(id: UUID.v7(), title: "Ferry to Wellington", status: .confirmed, kind: .transport,
                             when: .exact(start: at(2026, 2, 14, "13:15", nz), startZone: nz, end: at(2026, 2, 14, "16:45", nz), endZone: nz), place: picton.id, participants: [alex.id, sam.id])
        ferry.transport = Transport(mode: .ferry, from: picton.id, to: wellington.id, carrier: "Interislander")
        return Trip(id: UUID.v7(), title: "Summer in New Zealand", start: day(2026, 2, 6), end: day(2026, 2, 20), zone: nz, currency: "NZD", colorHue: 0.36, variant: "Main plan",
                    people: [alex, sam], places: [picton, wellington], items: [ferry], bookings: [], expenses: [], budgets: [], tasks: [], packing: [], versions: [], notes: "")
    }

    /// Sam moved the tea ceremony and joined Alex there on their phone, while this phone changed the same plan.
    /// The forecast the app downloaded on the demo day: Tokyo and Kyoto in January, dry and cold.
    static func weather(_ trip: Trip, _ day: Date) -> DayWeather? {
        guard trip.title == "Spring in Japan", let i = trip.days.firstIndex(of: day) else { return nil }
        let all: [DayWeather] = [
            .init(symbol: "sun.max.fill", high: 6, low: -1, rain: 0, summary: "Sunny"),
            .init(symbol: "sun.max.fill", high: 10, low: 2, rain: 0, summary: "Sunny"),
            .init(symbol: "cloud.sun.fill", high: 9, low: 3, rain: 10, summary: "Partly cloudy"),
            .init(symbol: "cloud.snow.fill", high: 3, low: -4, rain: 60, summary: "Snow in the mountains"),
            .init(symbol: "cloud.fill", high: 8, low: 1, rain: 20, summary: "Cloudy"),
            .init(symbol: "cloud.rain.fill", high: 7, low: 2, rain: 70, summary: "Rain in the afternoon"),
            .init(symbol: "sun.max.fill", high: 9, low: 0, rain: 0, summary: "Sunny"),
            .init(symbol: "cloud.sun.fill", high: 10, low: 3, rain: 10, summary: "Partly cloudy"),
        ]
        return all.indices.contains(i) ? all[i] : nil
    }

    static func autumnInMontreal() -> Trip {
        let mtl = "America/Toronto"
        let alex = Person(id: UUID.v7(), name: "Alex", isOrganizer: true)
        let sam = Person(id: UUID.v7(), name: "Sam")
        let oldPort = Place(id: UUID.v7(), name: "Old Port of Montreal", latitude: 45.5075, longitude: -73.5536, zone: mtl, address: "Rue de la Commune, Montreal, Canada")
        let walk = PlanItem(id: UUID.v7(), title: "Walk through the Old Port", status: .planned, kind: .sightseeing, when: .dayPart(day: day(2027, 10, 9), part: .afternoon, zone: mtl), place: oldPort.id, participants: [alex.id, sam.id])
        return Trip(id: UUID.v7(), title: "Autumn in Montreal", start: day(2027, 10, 8), end: day(2027, 10, 12), zone: mtl, currency: "CAD", colorHue: 0.06, variant: "Main plan",
                    people: [alex, sam], places: [oldPort], items: [walk], bookings: [], expenses: [], budgets: [], tasks: [], packing: [], versions: [], notes: "")
    }

    /// Sentences for what changed since a version of the sample trip.
    static func changes(since version: Version, in trip: Trip) -> [String] {
        guard trip.title == "Spring in Japan" else { return [] }
        switch version.title {
        case "Kyoto days settled":
            return [String(localized: "Added the idea “Sake tasting in Fushimi”"), String(localized: "Tea ceremony: only Sam goes")]
        case "Hotels booked":
            return [String(localized: "Added the idea “Sake tasting in Fushimi”"), String(localized: "Tea ceremony: only Sam goes"),
                    String(localized: "Arashiyama bamboo grove moved to the morning"), String(localized: "Added Kaiseki dinner")]
        default:
            return [String(localized: "Added 24 plans, 18 places and 5 bookings")]
        }
    }
}
