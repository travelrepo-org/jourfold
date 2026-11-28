import Foundation

/// What other apps share with Jourfold, left by the share extension until the app takes it. The app also leaves the
/// trips' names, plans and bookings there, so the extension can offer them without the trip data.
enum SharedInbox {
    struct Entry: Codable, Identifiable, Hashable {
        var id = UUID()
        var text: String
        var url: String?
        var trip: String
        /// The trip's id, as two trips can have the same title.
        var tripID: UUID? = nil
        /// The plan's title to add it to, or nil for a note on the trip.
        var plan: String?
        var planID: UUID? = nil
        /// The booking to add it to.
        var booking: UUID? = nil
        /// A shared map link that becomes a place of the trip.
        var newPlace: Bool? = nil
        /// Files that came along, imported as the trip's documents.
        var files: [String] = []
        var date: Date? = nil
    }

    struct Target: Codable, Hashable { var id: UUID; var title: String }
    struct TripSummary: Codable, Hashable {
        var title: String
        var plans: [String]
        var id: UUID? = nil
        var planTargets: [Target]? = nil
        var bookings: [Target]? = nil
    }

    // MARK: Files

    private static func key(_ entry: UUID, _ name: String) -> String { "file/\(entry.uuidString)/\(name)" }

    /// False when the file could not be kept, for example because it is too large for a build without an app group.
    static func putFile(_ data: Data, name: String, entry: UUID) -> Bool {
        if SharedStore.usesKeychain, data.count > SharedStore.keychainFileLimit { return false }
        return SharedStore.set(key(entry, name), data)
    }
    static func file(_ name: String, of entry: Entry) -> Data? { SharedStore.get(key(entry.id, name)) }

    /// Removes an entry's files once the app took them, or when sharing was cancelled.
    static func clean(_ entry: UUID) { SharedStore.keys("file/\(entry.uuidString)/").forEach(SharedStore.remove) }
    static func clean(_ entry: Entry) { clean(entry.id) }

    // MARK: Entries

    static func put(_ entry: Entry) {
        var entry = entry
        if entry.date == nil { entry.date = Date() }
        if let data = try? JSONEncoder().encode(entry) { SharedStore.set("inbox/\(entry.id.uuidString)", data) }
    }

    static func pending() -> [Entry] {
        SharedStore.keys("inbox/").compactMap { SharedStore.get($0).flatMap { try? JSONDecoder().decode(Entry.self, from: $0) } }
            .sorted { ($0.date ?? .distantPast) < ($1.date ?? .distantPast) }
    }

    /// Takes everything that waits, so each share is added once; `put` gives back one that could not be added.
    static func take() -> [Entry] {
        let all = pending()
        all.forEach { SharedStore.remove("inbox/\($0.id.uuidString)") }
        return all
    }

    // MARK: For the extension and the widget

    static func publish(_ trips: [TripSummary]) { store("trips", trips) }
    static func trips() -> [TripSummary] { SharedStore.get("trips").flatMap { try? JSONDecoder().decode([TripSummary].self, from: $0) } ?? [] }

    /// The next plans of all trips, which the app leaves for the Home Screen widget.
    struct UpcomingPlan: Codable, Hashable {
        var title: String
        var trip: String
        var start: Date
        var end: Date
        var zone: String
        var place: String
        var symbol: String
        var tripID: UUID? = nil
        var itemID: UUID? = nil
        /// `check_in` or `check_out` of a stay.
        var role: String? = nil
        /// Instead of a clock time, such as "All day".
        var when: String? = nil
    }

    /// What is still on today where it happens, and tomorrow's check-outs. What began earlier and goes on is today's too.
    static func today(_ plans: [UpcomingPlan], at: Date) -> (today: [UpcomingPlan], leaving: [UpcomingPlan]) {
        func day(_ p: UpcomingPlan, _ shift: Int) -> Bool {
            var c = Calendar(identifier: .gregorian); c.timeZone = TimeZone(identifier: p.zone) ?? .current
            guard let d = c.date(byAdding: .day, value: shift, to: at) else { return false }
            return c.isDate(p.start, inSameDayAs: d) || (shift == 0 && p.start < at)
        }
        let open = plans.filter { $0.end > at }
        return (open.filter { day($0, 0) }, open.filter { $0.role == "check_out" && !day($0, 0) && day($0, 1) })
    }

    static func publishUpcoming(_ plans: [UpcomingPlan]) { store("upcoming", plans) }
    static func upcoming() -> [UpcomingPlan] { SharedStore.get("upcoming").flatMap { try? JSONDecoder().decode([UpcomingPlan].self, from: $0) } ?? [] }

    /// What a trip has cost and what is left of its budgets, as TravelRepo worked it out, for the costs widget.
    struct CostSummary: Codable, Hashable {
        struct Budget: Codable, Hashable { var title: String; var amount: Decimal; var used: Decimal; var left: Decimal; var estimated: Bool }
        var tripID: UUID
        var trip: String
        var currency: String
        var start: Date
        var end: Date
        var paid: Decimal
        var paidEstimated: Bool
        var planned: Decimal
        var budget: Budget?
        var budgets: [Budget]
        /// What the user gets (above zero) or owes, when the app knows who they are.
        var balance: Decimal?
        var balanceEstimated: Bool
    }

    static func publishCosts(_ costs: [CostSummary]) { store("costs", costs) }
    static func costs() -> [CostSummary] { SharedStore.get("costs").flatMap { try? JSONDecoder().decode([CostSummary].self, from: $0) } ?? [] }

    private static func store(_ key: String, _ value: some Encodable) {
        let encoder = JSONEncoder(); encoder.outputFormatting = .sortedKeys
        guard let data = try? encoder.encode(value), data != SharedStore.get(key) else { return }
        SharedStore.set(key, data)
    }
}

/// A map link as it is shared: the place's name and position when the link has them.
struct MapLink {
    var name: String?
    var latitude: Double?, longitude: Double?

    init?(_ url: String?) {
        guard let url, let parts = URLComponents(string: url), let host = parts.host?.lowercased() else { return nil }
        let maps = host == "maps.apple.com" || host == "maps.apple" || host == "maps.app.goo.gl" || host.hasPrefix("maps.google.")
            || (host.hasPrefix("www.google.") && parts.path.hasPrefix("/maps")) || host.hasSuffix("openstreetmap.org") || parts.scheme == "geo"
        guard maps else { return nil }
        let q = parts.queryItems ?? []
        func value(_ names: String...) -> String? { q.first { names.contains($0.name) && $0.value?.isEmpty == false }?.value }
        func pair(_ text: String?) -> (Double, Double)? {
            guard let m = text?.firstMatch(of: /(-?\d{1,2}(?:\.\d+)?)\s*,\s*(-?\d{1,3}\.\d+)/), let a = Double(m.1), let b = Double(m.2), abs(a) <= 90, abs(b) <= 180 else { return nil }
            return (a, b)
        }
        name = value("name", "q", "query").flatMap { pair($0) == nil ? $0 : nil }
        if name == nil, let r = parts.path.range(of: "/maps/place/") { name = parts.path[r.upperBound...].split(separator: "/").first.map { $0.replacingOccurrences(of: "+", with: " ") }.flatMap { pair($0) == nil ? $0 : nil } }
        // A Google link holds the pin after !3d…!4d…; the "@" part is only where the map was.
        if let m = url.firstMatch(of: /!3d(-?\d+\.\d+)!4d(-?\d+\.\d+)/), let a = Double(m.1), let b = Double(m.2) { (latitude, longitude) = (a, b) }
        else if let lat = value("mlat").flatMap(Double.init), let lon = value("mlon").flatMap(Double.init) { (latitude, longitude) = (lat, lon) }
        else if let p = pair(value("coordinate", "ll", "q", "query", "sll")) ?? pair(parts.path) { (latitude, longitude) = p }
    }
}
