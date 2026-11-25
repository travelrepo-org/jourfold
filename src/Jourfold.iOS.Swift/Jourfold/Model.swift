import Foundation
import CoreLocation
import Observation

// What the screens show of a trip. The trip itself is TravelRepo's: `TravelRepoBridge` reads it into these values and
// writes changes back.

enum Kind: String, CaseIterable, Identifiable, Codable {
    case activity, transport, accommodation, food, sightseeing, culture, nature, shopping, nightlife, meeting, freeTime, other
    var id: String { rawValue }

    var symbol: String {
        switch self {
        case .activity: "figure.walk"
        case .transport: "tram.fill"
        case .accommodation: "bed.double.fill"
        case .food: "fork.knife"
        case .sightseeing: "camera.fill"
        case .culture: "building.columns.fill"
        case .nature: "leaf.fill"
        case .shopping: "bag.fill"
        case .nightlife: "moon.stars.fill"
        case .meeting: "person.2.fill"
        case .freeTime: "cup.and.saucer.fill"
        case .other: "circle.grid.2x2.fill"
        }
    }

    var label: LocalizedStringResource {
        switch self {
        case .activity: "Activity"
        case .transport: "Transport"
        case .accommodation: "Stay"
        case .food: "Food"
        case .sightseeing: "Sightseeing"
        case .culture: "Culture"
        case .nature: "Nature"
        case .shopping: "Shopping"
        case .nightlife: "Nightlife"
        case .meeting: "Meeting"
        case .freeTime: "Free time"
        case .other: "Other"
        }
    }

    /// Kinds offered when adding a plan item; transport and stays have their own entries in Quick Add.
    static var activityKinds: [Kind] { [.sightseeing, .culture, .nature, .food, .shopping, .nightlife, .meeting, .freeTime, .activity, .other] }
}

enum TransportMode: String, CaseIterable, Identifiable, Codable {
    case flight, train, bus, car, ferry, taxi, walk, bicycle, other
    var id: String { rawValue }
    var symbol: String {
        switch self {
        case .flight: "airplane"
        case .train: "tram.fill"
        case .bus: "bus.fill"
        case .car: "car.fill"
        case .ferry: "ferry.fill"
        case .taxi: "car.rear.fill"
        case .walk: "figure.walk"
        case .bicycle: "bicycle"
        case .other: "arrow.triangle.swap"
        }
    }
    var label: LocalizedStringResource {
        switch self {
        case .flight: "Flight"
        case .train: "Train"
        case .bus: "Bus"
        case .car: "Car"
        case .ferry: "Ferry"
        case .taxi: "Taxi"
        case .walk: "Walk"
        case .bicycle: "Bicycle"
        case .other: "Other"
        }
    }
}

enum Status: String, CaseIterable, Identifiable, Codable {
    case idea, planned, reserved, confirmed, done, cancelled
    var id: String { rawValue }
    var label: LocalizedStringResource {
        switch self {
        case .idea: "Idea"
        case .planned: "Planned"
        case .reserved: "Reserved"
        case .confirmed: "Confirmed"
        case .done: "Done"
        case .cancelled: "Cancelled"
        }
    }
    var symbol: String {
        switch self {
        case .idea: "lightbulb"
        case .planned: "circle.dashed"
        case .reserved: "clock"
        case .confirmed: "checkmark"
        case .done: "checkmark.circle"
        case .cancelled: "xmark"
        }
    }
}

enum DayPart: String, CaseIterable, Identifiable, Codable {
    case morning, afternoon, evening, night
    var id: String { rawValue }
    var label: LocalizedStringResource {
        switch self {
        case .morning: "Morning"
        case .afternoon: "Afternoon"
        case .evening: "Evening"
        case .night: "Night"
        }
    }
    var hours: (Int, Int) {
        switch self {
        case .morning: (8, 12)
        case .afternoon: (13, 17)
        case .evening: (18, 22)
        case .night: (22, 23)
        }
    }
}

/// When something happens, with the precision people actually plan with.
enum When: Equatable, Codable {
    case exact(start: Date, startZone: String, end: Date, endZone: String)
    case approximate(start: Date, end: Date, zone: String)
    case dayPart(day: DateComponents, part: DayPart, zone: String)
    case allDay(day: DateComponents, zone: String)
    case unscheduled(minutes: Int?)

    var zone: TimeZone {
        switch self {
        case .exact(_, let z, _, _), .approximate(_, _, let z), .dayPart(_, _, let z), .allDay(_, let z):
            TimeZone(identifier: z) ?? .current
        case .unscheduled: .current
        }
    }

    var endZone: TimeZone {
        if case .exact(_, _, _, let z) = self { return TimeZone(identifier: z) ?? .current }
        return zone
    }

    /// The moment it starts, for sorting and for "next".
    var start: Date? {
        switch self {
        case .exact(let s, _, _, _), .approximate(let s, _, _): s
        case .dayPart(let d, let p, let z): Self.date(d, hour: p.hours.0, zone: z)
        case .allDay(let d, let z): Self.date(d, hour: 0, zone: z)
        case .unscheduled: nil
        }
    }

    var end: Date? {
        switch self {
        case .exact(_, _, let e, _), .approximate(_, let e, _): e
        case .dayPart(let d, let p, let z): Self.date(d, hour: p.hours.1, zone: z)
        case .allDay(let d, let z): Self.date(d, hour: 24, zone: z)
        case .unscheduled: nil
        }
    }

    var isScheduled: Bool { if case .unscheduled = self { false } else { true } }

    static func date(_ day: DateComponents, hour: Int, zone: String) -> Date? {
        var cal = Calendar(identifier: .gregorian)
        cal.timeZone = TimeZone(identifier: zone) ?? .current
        var c = day; c.hour = hour; c.minute = 0
        return cal.date(from: c)
    }
}

struct Person: Identifiable, Hashable, Codable {
    let id: UUID
    var name: String
    var isOrganizer = false
    var initials: String { String(name.prefix(1)) }
}

struct Place: Identifiable, Hashable, Codable {
    let id: UUID
    var name: String
    var latitude: Double
    var longitude: Double
    var zone: String
    var address: String
    var localName: String? = nil
    var localAddress: String? = nil
    var coordinate: CLLocationCoordinate2D { .init(latitude: latitude, longitude: longitude) }
    /// The town, for headings: the last part of the address that is not a country.
    var city: String {
        let parts = address.components(separatedBy: ", ")
        let countries: Set<String> = ["Germany", "Japan", "France", "New Zealand", "Canada"]
        return parts.last(where: { !countries.contains($0) }) ?? address
    }
}

struct Transport: Hashable, Codable {
    var mode: TransportMode
    var from: UUID
    var to: UUID
    var carrier: String? = nil
    var number: String? = nil
    var seat: String? = nil
}

struct Stay: Hashable, Codable {
    var place: UUID
    var room: String? = nil
}

struct Attachment: Identifiable, Hashable, Codable {
    enum Kind: String, Codable { case walletPass, pdf, image, note, link, mail, file }
    let id: UUID
    var name: String
    var kind: Kind
    var text: String? = nil
}

struct Comment: Identifiable, Hashable, Codable {
    let id: UUID
    var author: UUID
    var body: String
    var date: Date
}

struct PlanItem: Identifiable, Hashable, Codable {
    let id: UUID
    var title: String
    var status: Status
    var kind: Kind
    var when: When
    var place: UUID? = nil
    var participants: [UUID] = []
    var transport: Transport? = nil
    var stay: Stay? = nil
    var booking: UUID? = nil
    var parent: UUID? = nil
    var notes: String = ""
    var attachments: [Attachment] = []
    var comments: [Comment] = []

    static func == (a: PlanItem, b: PlanItem) -> Bool { a.id == b.id && a.title == b.title && a.status == b.status && a.when == b.when && a.notes == b.notes && a.kind == b.kind && a.place == b.place }
    func hash(into h: inout Hasher) { h.combine(id) }
}

struct Booking: Identifiable, Hashable, Codable {
    let id: UUID
    var title: String
    var provider: String
    var reference: String
    var status: Status
    var items: [UUID]
    var freeCancellationUntil: Date? = nil
}

struct Money: Hashable, Codable {
    var value: Decimal
    var currency: String
}

enum CostCategory: String, CaseIterable, Identifiable, Codable {
    case transport, stay, food, activities, shopping, other
    var id: String { rawValue }
    var label: LocalizedStringResource {
        switch self {
        case .transport: "Transport"
        case .stay: "Stay"
        case .food: "Food"
        case .activities: "Activities"
        case .shopping: "Shopping"
        case .other: "Other"
        }
    }
    var symbol: String {
        switch self {
        case .transport: "tram.fill"
        case .stay: "bed.double.fill"
        case .food: "fork.knife"
        case .activities: "ticket.fill"
        case .shopping: "bag.fill"
        case .other: "square.grid.2x2.fill"
        }
    }
}

/// How an expense is divided between the people who share it.
enum Split: Hashable, Codable {
    case equal
    case amounts([UUID: Decimal])
    case shares([UUID: Int])
    case percent([UUID: Decimal])
}

struct Expense: Identifiable, Hashable, Codable {
    let id: UUID
    var title: String
    var amount: Money
    var converted: Money? = nil
    var category: CostCategory
    var paidBy: UUID? = nil
    var sharedBy: [UUID]
    var estimated = false
    var booking: UUID? = nil
    var date: Date? = nil
    var settled: Decimal = 0
    var split: Split = .equal
    /// The plan this expense belongs to, when it is not a booking's.
    var item: UUID? = nil
}

struct Budget: Identifiable, Hashable, Codable {
    let id: UUID
    var title: String
    var amount: Money
    var category: CostCategory? = nil
}

struct TripTask: Identifiable, Hashable, Codable {
    let id: UUID
    var title: String
    var done: Bool
    var due: Date
    var assignee: UUID
    var related: UUID? = nil
}

struct PackingItem: Identifiable, Hashable, Codable {
    let id: UUID
    var title: String
    var group: String
    var quantity: String? = nil
    /// Who brings it. Empty and not `everyone` means nobody has taken it yet.
    var people: [UUID]
    var everyone = false
    var packedBy: Set<UUID> = []
}

struct PackingList: Identifiable, Hashable, Codable {
    let id: UUID
    var title: String
    var items: [PackingItem]
}

struct Version: Identifiable, Hashable, Codable {
    let id: UUID
    var title: String
    var author: String
    var date: Date
    /// The Git commit in TravelRepo.
    var commit: String? = nil
}

struct Trip: Identifiable, Hashable, Codable {
    /// The app's own id for the trip; it stays the same when the trip is read from TravelRepo.
    var id: UUID
    var title: String
    var start: DateComponents
    var end: DateComponents
    var zone: String
    var currency: String
    var colorHue: Double
    var variant: String
    var people: [Person]
    var places: [Place]
    var items: [PlanItem]
    var bookings: [Booking]
    var expenses: [Expense]
    var budgets: [Budget]
    var tasks: [TripTask]
    var packing: [PackingList]
    var versions: [Version]
    var notes: String

    static func == (a: Trip, b: Trip) -> Bool { a.id == b.id }
    func hash(into h: inout Hasher) { h.combine(id) }

    func place(_ id: UUID?) -> Place? { places.first { $0.id == id } }
    func person(_ id: UUID?) -> Person? { people.first { $0.id == id } }
    func booking(_ id: UUID?) -> Booking? { bookings.first { $0.id == id } }
    func item(_ id: UUID?) -> PlanItem? { items.first { $0.id == id } }

    var calendar: Calendar { var c = Calendar(identifier: .gregorian); c.timeZone = TimeZone(identifier: zone) ?? .current; return c }

    /// Every day of the trip as a date at local midnight in the trip's main zone.
    var days: [Date] {
        guard let s = calendar.date(from: start), let e = calendar.date(from: end) else { return [] }
        var out: [Date] = []; var d = s
        while d <= e { out.append(d); d = calendar.date(byAdding: .day, value: 1, to: d)! }
        return out
    }

    /// The day TravelRepo's agenda puts each plan on, and the stay of each night, with the
    /// time the plan had then. A plan whose time changed since uses the local rule until TravelRepo is read again.
    nonisolated(unsafe) static var travelRepoDays: [UUID: [UUID: (day: DateComponents, when: When)]] = [:]
    /// Which night of how many, by day, from TravelRepo's agenda.
    nonisolated(unsafe) static var nightNumbers: [UUID: [DateComponents: (night: Int, of: Int)]] = [:]
    nonisolated(unsafe) static var travelRepoNights: [UUID: [DateComponents: (stay: UUID, when: When)]] = [:]

    /// Items on a day, by where they happen: an item belongs to the day it starts in its own zone.
    func items(on day: Date) -> [PlanItem] {
        let dayComps = calendar.ymd(day)
        let fromTravelRepo = Trip.travelRepoDays[id]
        return items.filter { item in
            guard item.status != .cancelled, let s = item.when.start else { return false }
            if let known = fromTravelRepo?[item.id], known.when == item.when { return known.day == dayComps }
            var c = Calendar(identifier: .gregorian); c.timeZone = item.when.zone
            return c.ymd(s) == dayComps
        }
        .sorted { ($0.when.start ?? .distantFuture) < ($1.when.start ?? .distantFuture) }
    }

    /// Stays that cover the night after `day`, for the "where you sleep" line.
    /// "Night 2 of 3", or nothing when there is only one or it is not known.
    func nightText(on day: Date) -> String? {
        guard let n = Trip.nightNumbers[id]?[calendar.ymd(day)], n.of > 1 else { return nil }
        return String(localized: "Night \(n.night) of \(n.of)")
    }

    func stay(on day: Date) -> PlanItem? {
        if let nights = Trip.travelRepoNights[id], let known = nights[calendar.ymd(day)],
           let stay = item(known.stay), stay.when == known.when, stay.status != .cancelled { return stay }
        let night = day.addingTimeInterval(22 * 3600)
        return items.first { $0.stay != nil && $0.status != .cancelled && ($0.when.start ?? .distantFuture) <= night && ($0.when.end ?? .distantPast) > night }
    }

    var ideas: [PlanItem] { items.filter { item in !item.when.isScheduled && item.parent == nil && !items.contains { $0.parent == item.id } } }

    func children(of id: UUID) -> [PlanItem] { items.filter { $0.parent == id }.sorted { ($0.when.start ?? .distantFuture) < ($1.when.start ?? .distantFuture) } }

    var openTasks: Int { tasks.filter { !$0.done }.count }

    var toPack: Int {
        packing.flatMap(\.items).reduce(0) { sum, item in
            let who = item.everyone ? people.map(\.id) : item.people
            return sum + (who.isEmpty ? 1 : who.filter { !item.packedBy.contains($0) }.count)
        }
    }

    var peopleText: String { ListFormatter.localizedString(byJoining: people.map(\.name)) }

    /// A trip with nothing in it yet, before TravelRepo is read.
    static func empty(id: UUID) -> Trip {
        let today = TravelRepoBridge.today(in: TimeZone.current.identifier)
        return Trip(id: id, title: "", start: today, end: today, zone: TimeZone.current.identifier, currency: "EUR", colorHue: TravelRepoBridge.hue(id), variant: "",
                    people: [], places: [], items: [], bookings: [], expenses: [], budgets: [], tasks: [], packing: [], versions: [], notes: "")
    }
}

/// What the person using the app has changed on this phone since the last version.
struct Change: Identifiable, Hashable {
    let id = UUID.v7()
    var text: String
}

@Observable
final class TripStore {
    /// Every change to a trip, by `update` or by a form's binding, is written to its TravelRepo folder shortly after.
    var trips: [Trip] { didSet { for trip in trips { TravelRepoBridge.all[trip.id]?.scheduleWrite() } } }
    var changes: [UUID: [Change]] = [:]
    var lastSync: [UUID: Date] = [:]
    var isSyncing = false
    /// Counts reads from TravelRepo, so views that ask TravelRepo themselves ask again.
    var travelRepoReads = 0
    /// Changes from someone else that touch the same thing as changes on this phone; the person chooses.
    var pendingMerge: MergeConflict?
    /// Something TravelRepo refused or could not do, shown once as a message.
    var problem: String?
    /// Who the person using the phone is in each trip, as TravelRepo's `me` found them.
    var me: [UUID: UUID] = [:]
    /// The GitHub account, for TravelRepo's `me`.
    weak var account: Account?
    /// While the trips on the device are opened.
    var loading: Bool
    /// Trip folders that could not be read, by folder name.
    var unreadable: [String] = []

    /// The real time, or with `-demo YES` the sample's moment (9 January 2027, 14:10 in Tokyo), so Today has
    /// something to show in screenshots.
    var now: Date

    init() {
        let d = UserDefaults.standard
        trips = []; loading = true
        lastSync = Dictionary(uniqueKeysWithValues: (d.dictionary(forKey: "lastSync") as? [String: Date] ?? [:]).compactMap { k, v in UUID(uuidString: k).map { ($0, v) } })
        now = DebugTools.flag("demo") ? SampleData.demoNow : Date()
    }

    /// The trip, or an empty one in the moment a view still shows a trip that was just removed.
    func trip(_ id: UUID) -> Trip { trips.first { $0.id == id } ?? Trip.empty(id: id) }

    /// Trips under way and coming up first, the soonest first; past trips after them, the latest first.
    func sortTrips() {
        func past(_ t: Trip) -> Bool { (t.calendar.date(from: t.end).map { $0.addingTimeInterval(86400) } ?? .distantFuture) < now }
        func start(_ t: Trip) -> Date { t.calendar.date(from: t.start) ?? .distantFuture }
        trips.sort { a, b in
            if past(a) != past(b) { return !past(a) }
            return past(a) ? (start(a), a.title) > (start(b), b.title) : (start(a), a.title) < (start(b), b.title)
        }
    }

    func update(_ tripID: UUID, _ change: String, _ body: (inout Trip) -> Void) {
        guard let i = trips.firstIndex(where: { $0.id == tripID }) else { return }
        body(&trips[i])
        if !change.isEmpty { changes[tripID, default: []].append(Change(text: change)) }
    }

    func saveVersion(_ tripID: UUID, title: String) {
        guard let bridge = TravelRepoBridge.all[tripID] else { return }
        Task { @MainActor in if await bridge.createVersion(title) { changes[tripID] = [] } }
    }

    /// Why the last sync of a trip did not finish; nil when it did.
    var syncProblem: [UUID: String] = [:]

    func synced(_ tripID: UUID) {
        lastSync[tripID] = Date()
        var kept = UserDefaults.standard.dictionary(forKey: "lastSync") as? [String: Date] ?? [:]
        kept[tripID.uuidString] = Date(); UserDefaults.standard.set(kept, forKey: "lastSync")
    }

    /// Open changes become a version and, when the trip is shared and someone is signed in, go to GitHub.
    func autoSave(_ tripID: UUID) async {
        guard let bridge = TravelRepoBridge.all[tripID], !DebugTools.flag("demo") else { return }
        await bridge.saveChanges()
        if bridge.isShared, account?.login != nil, account?.isDemo == false { await sync(tripID, quiet: true) }
    }

    func sync(_ tripID: UUID, quiet: Bool = false) async {
        guard let bridge = TravelRepoBridge.all[tripID], let trip = trips.first(where: { $0.id == tripID }), !isSyncing else { return }
        isSyncing = true
        if let conflict = await bridge.sync(trip, quiet: quiet) { pendingMerge = conflict }
        else if pendingMerge?.tripID == tripID { pendingMerge = nil }
        isSyncing = false
        TestReport.note("sync \(quiet ? "quiet" : "asked") problem \(syncProblem[tripID] != nil) merge \(pendingMerge != nil)")
    }

    func resolve(_ choices: [MergeConflict.Choice]) {
        guard let merge = pendingMerge, let bridge = TravelRepoBridge.all[merge.tripID] else { return }
        pendingMerge = nil
        Task { @MainActor in await bridge.merge(merge, choices: choices) }
    }
}

struct MergeConflict: Identifiable {
    enum Choice: Hashable { case mine, theirs, both }
    struct Part: Identifiable {
        let id = UUID.v7()
        var title: String
        var field: LocalizedStringResource
        var mine: String
        var theirs: String
    }
    let id = UUID.v7()
    var tripID: UUID
    var from: String
    var parts: [Part]
    /// The branch TravelRepo merges.
    var branch: String
}

/// Weather the app has downloaded for a day and place. `TravelRepoBridge` fetches it through TravelRepo, once the person agreed.
struct DayWeather {
    var symbol: String
    var high: Int
    var low: Int
    var rain: Int
    var summary: LocalizedStringResource
    /// `forecast`, `trend` (a tendency one to two weeks ahead) or `climate` (what is usual on that day).
    var kind = "forecast"
    var fetched: Date? = nil
    /// For the hours of a plan rather than a whole day.
    var hourly = false
}

extension UUID {
    /// A version 7 UUID (time-ordered), as TravelRepo requires for every entity id.
    static func v7() -> UUID {
        var bytes = (0..<16).map { _ in UInt8.random(in: 0...255) }
        let milliseconds = UInt64(Date().timeIntervalSince1970 * 1000)
        for i in 0..<6 { bytes[i] = UInt8((milliseconds >> (8 * (5 - UInt64(i)))) & 0xFF) }
        bytes[6] = 0x70 | (bytes[6] & 0x0F); bytes[8] = 0x80 | (bytes[8] & 0x3F)
        return UUID(uuid: (bytes[0], bytes[1], bytes[2], bytes[3], bytes[4], bytes[5], bytes[6], bytes[7], bytes[8], bytes[9], bytes[10], bytes[11], bytes[12], bytes[13], bytes[14], bytes[15]))
    }
}
