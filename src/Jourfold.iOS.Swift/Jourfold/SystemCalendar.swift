import EventKit
import SwiftUI
import TravelRepoKit

/// The trips in the iOS calendar, as in the .NET app: one calendar per trip, named after it, with the entries
/// TravelRepo's `calendar` command lists (plans with their times, places and status). Off until the person turns it
/// on in Settings; turning it off removes the calendars. Only entries that changed are written again.
@MainActor
enum SystemCalendar {
    private static let store = EKEventStore()
    static var on: Bool { UserDefaults.standard.bool(forKey: "calendar") }
    /// Why the calendar is not up to date, such as a missing permission.
    static var problem: String?

    /// Asks for access when needed. iOS has no read-only calendars of an app's own, so it needs full access.
    static func allowed(ask: Bool) async -> Bool {
        switch EKEventStore.authorizationStatus(for: .event) {
        case .fullAccess: return true
        case .notDetermined where ask: return (try? await store.requestFullAccessToEvents()) ?? false
        default: return false
        }
    }

    /// Writes every trip on the phone into its calendar, and removes calendars of trips that are gone.
    static func update(_ trips: [Trip]) async {
        guard on else { return }
        guard await allowed(ask: false) else { problem = String(localized: "Jourfold may not use the calendar. Allow it under Settings → Apps → Jourfold."); return }
        problem = nil
        var keep: Set<String> = []
        for trip in trips {
            guard let bridge = TravelRepoBridge.all[trip.id], let all = try? await entries(bridge) else { continue }
            do {
                // All in one, or one calendar per kind of plan; kinds without entries get none.
                for group in byKind ? groups : [""] {
                    let entries = group.isEmpty ? all : all.filter { Self.group($0.kind) == group }
                    guard group.isEmpty || !entries.isEmpty else { continue }
                    let calendar = try calendar(for: trip, group: group)
                    keep.insert(calendar.calendarIdentifier)
                    try write(entries, into: calendar, trip: trip)
                }
            } catch { problem = error.localizedDescription }
        }
        for (_, id) in known where !keep.contains(id) { if let c = store.calendar(withIdentifier: id) { try? store.removeCalendar(c, commit: true) } }
        known = known.filter { keep.contains($0.value) }
        TestReport.note("calendar written, \(keep.count) calendars, problem \(problem != nil)")
    }

    static var byKind: Bool { UserDefaults.standard.bool(forKey: "calendar.kinds") }
    static let groups = ["transport", "stays", "food", "plans"]
    private static func group(_ kind: String) -> String { kind == "transport" ? "transport" : kind == "accommodation" ? "stays" : kind == "food" ? "food" : "plans" }
    private static func groupName(_ group: String) -> String {
        switch group {
        case "transport": String(localized: "Journeys")
        case "stays": String(localized: "Stays")
        case "food": String(localized: "Food")
        default: String(localized: "Plans")
        }
    }

    /// The trip's entries with stays as check-in and check-out; cancelled plans are left out.
    static func entries(_ bridge: TravelRepoBridge) async throws -> [CalendarEntry] {
        nonisolated struct Entries: Decodable { let entries: [CalendarEntry] }
        return try await bridge.session.call("calendar", ["stays_as_check_in_out": true], as: Entries.self).entries.filter { $0.status != "cancelled" }
    }

    private static func title(_ e: CalendarEntry) -> String {
        switch e.role {
        case "check_in": String(localized: "Check-in: \(e.title)")
        case "check_out": String(localized: "Check-out: \(e.title)")
        default: e.title
        }
    }

    /// How many events the trip's calendar holds, for the self-test.
    static func events(of trip: Trip) -> Int {
        let calendars = known.filter { $0.key.hasPrefix(trip.id.uuidString) }.compactMap { store.calendar(withIdentifier: $0.value) }
        guard !calendars.isEmpty else { return 0 }
        // EventKit searches at most four years at a time.
        let start = (trip.calendar.date(from: trip.start) ?? Date()).addingTimeInterval(-86400 * 60)
        let predicate = store.predicateForEvents(withStart: start, end: start.addingTimeInterval(86400 * 365), calendars: calendars)
        return store.events(matching: predicate).count
    }

    /// Removes all of Jourfold's calendars.
    static func removeAll() {
        for (_, id) in known { if let c = store.calendar(withIdentifier: id) { try? store.removeCalendar(c, commit: true) } }
        known = [:]; written = [:]
    }

    /// Calendar ids by trip, and what was written per entry (a fingerprint and the event's id), kept on this phone.
    private static var known: [String: String] {
        get { UserDefaults.standard.dictionary(forKey: "calendar.ids") as? [String: String] ?? [:] }
        set { UserDefaults.standard.set(newValue, forKey: "calendar.ids") }
    }
    private static var written: [String: [String]] {
        get { UserDefaults.standard.dictionary(forKey: "calendar.entries") as? [String: [String]] ?? [:] }
        set { UserDefaults.standard.set(newValue, forKey: "calendar.entries") }
    }

    private static func calendar(for trip: Trip, group: String) throws -> EKCalendar {
        let key = trip.id.uuidString + (group.isEmpty ? "" : "/" + group)
        let name = group.isEmpty ? trip.title : trip.title + " · " + groupName(group)
        if let id = known[key], let existing = store.calendar(withIdentifier: id) {
            if existing.title != name { existing.title = name; try store.saveCalendar(existing, commit: true) }
            return existing
        }
        let calendar = EKCalendar(for: .event, eventStore: store)
        calendar.title = name
        let kind: Kind? = group == "transport" ? .transport : group == "stays" ? .accommodation : group == "food" ? .food : group == "plans" ? .sightseeing : nil
        calendar.cgColor = UIColor(kind?.colors.bar ?? Color(hue: trip.colorHue, saturation: 0.55, brightness: 0.7)).cgColor
        // On the phone itself when it has local calendars, else in the default account (such as iCloud).
        calendar.source = store.sources.first { $0.sourceType == .local } ?? store.defaultCalendarForNewEvents?.source
        try store.saveCalendar(calendar, commit: true)
        known[key] = calendar.calendarIdentifier
        return calendar
    }

    private static func write(_ entries: [CalendarEntry], into calendar: EKCalendar, trip: Trip) throws {
        var done = written
        let iso = ISO8601DateFormatter()
        var seen: Set<String> = []
        for entry in entries {
            let mark = "\(calendar.calendarIdentifier)|\(entry.uid)"
            seen.insert(mark)
            let print = [title(entry), entry.start ?? "", entry.end ?? "", entry.date ?? "", entry.location ?? "", entry.description ?? "", entry.status ?? ""].joined(separator: "|")
            if let previous = done[mark], previous.first == print, previous.count > 1, store.event(withIdentifier: previous[1]) != nil { continue }
            let event = done[mark].flatMap { $0.count > 1 ? store.event(withIdentifier: $0[1]) : nil } ?? EKEvent(eventStore: store)
            event.calendar = calendar
            event.title = title(entry)
            // Ideas and what is not booked yet are not certain.
            event.availability = entry.status == "idea" || entry.status == "planned" ? (calendar.supportedEventAvailabilities.contains(.tentative) ? .tentative : .notSupported) : (calendar.supportedEventAvailabilities.contains(.busy) ? .busy : .notSupported)
            event.location = entry.location
            event.notes = entry.description
            if entry.allDay, let date = entry.date, let day = TravelRepoBridge.dayComponents(date) {
                var c = Calendar(identifier: .gregorian); c.timeZone = TimeZone(identifier: entry.zone ?? trip.zone) ?? .current
                event.isAllDay = true
                event.startDate = c.date(from: day); event.endDate = event.startDate
                event.timeZone = nil
            } else if let start = entry.start.flatMap(iso.date(from:)) {
                event.isAllDay = false
                event.startDate = start
                event.endDate = entry.end.flatMap(iso.date(from:)) ?? start
                event.timeZone = entry.zone.flatMap(TimeZone.init(identifier:))
            } else { continue }
            try store.save(event, span: .thisEvent, commit: false)
            done[mark] = [print, event.eventIdentifier]
        }
        for (mark, value) in done where mark.hasPrefix(calendar.calendarIdentifier + "|") && !seen.contains(mark) {
            if value.count > 1, let event = store.event(withIdentifier: value[1]) { try? store.remove(event, span: .thisEvent, commit: false) }
            done[mark] = nil
        }
        try store.commit()
        written = done
    }
}
