import EventKit
import Foundation
import UserNotifications

/// What the .NET app (`src/Jourfold.iOS`) left on this device under the same bundle ID. Both keep trips, recovery data
/// and the GitHub token in the same places (`trips`, `recovery/<folder>`, the Keychain item `github.user-token`), so
/// those need no step. Its settings file is read once; it stays as it is, so going back to the .NET app loses nothing.
@MainActor
enum DotNetApp {
    nonisolated struct Settings: Decodable {
        var Name: String?
        var Email: String?
        var GitHubClientId: String?
        var GitHubLogin: String?
        var LastSync: [String: String]?
        var SecondZones: [String: String]?
        var OnlineMaps: Bool?
        var Values: [String: String]?
    }
    private nonisolated struct Kept: Decodable { var Id: String? }

    private static let doneKey = "dotnet.settings", tripsKey = "dotnet.trips"
    static var file: URL { TripLibrary.support.appendingPathComponent("settings.json") }
    private static var read: Settings? { (try? Data(contentsOf: file)).flatMap { try? JSONDecoder().decode(Settings.self, from: $0) } }

    /// Settings of the phone, before the trips open.
    static func takeSettings(_ d: UserDefaults = .standard) {
        guard !d.bool(forKey: doneKey) else { return }
        // Unreadable, such as before the first unlock: the next start tries again.
        guard let s = read else { if !FileManager.default.fileExists(atPath: file.path) { d.set(true, forKey: doneKey); d.set(true, forKey: tripsKey) }; return }
        func set(_ key: String, _ value: Any?) { if let value, d.object(forKey: key) == nil { d.set(value, forKey: key) } }
        func text(_ value: String?) -> String? { value.flatMap { $0.trimmingCharacters(in: .whitespaces).isEmpty ? nil : $0 } }
        let values = s.Values ?? [:]
        set("authorName", text(s.Name)); set("authorEmail", text(s.Email)); set("githubClientID", text(s.GitHubClientId)); set(GitHubSignIn.loginKey, text(s.GitHubLogin))
        if s.OnlineMaps == true { set("maps.online", "on") }
        for key in ["weather.online", "rates.online"] { if let v = values[key], ["on", "off"].contains(v) { set(key, v) } }
        if let third = values["bar.third"], ThirdPlace(rawValue: third) != nil { set("bar.third", third) }
        if let filter = values["packing.filter"], ["all", "unassigned", "mine"].contains(filter) { set("packing.filter", filter) }
        if let v = values["search.notes"] { set("search.notes", v == "on") }
        // Off until turned on in the .NET app, so a missing value is a no.
        set("notices", values["notices"] == "on"); if values["notices"] != nil { set("notices.asked", true) }
        set("calendar", values["calendar"] == "trips" || values["calendar"] == "kinds"); set("calendar.kinds", values["calendar"] == "kinds")
        if let recent = text(values["converter.recent"]) { set(RecentCurrencies.key, recent) }
        if let v = values["updates.check"] { set("updates.check", v) }
        removeCalendars()
        let center = UNUserNotificationCenter.current()
        Task {
            // The app plans its own in their place.
            let old = await center.pendingNotificationRequests().map(\.identifier).filter { $0.hasPrefix("jourfold.") }
            center.removePendingNotificationRequests(withIdentifiers: old)
        }
        // Map tiles and weather of the .NET app; both can be downloaded again.
        try? FileManager.default.removeItem(at: TripLibrary.support.appendingPathComponent("cache", isDirectory: true))
        d.set(true, forKey: doneKey)
    }

    /// What it knew about each trip, by folder name, once the trips are open: who the user is, and the last sync.
    static func takeTrips(_ store: TripStore, _ d: UserDefaults = .standard) {
        guard !d.bool(forKey: tripsKey), let s = read else { return }
        var synced = d.dictionary(forKey: "lastSync") as? [String: Date] ?? [:]
        for (id, bridge) in TravelRepoBridge.all {
            let folder = bridge.folder.lastPathComponent, key = "me-\(id.uuidString)"
            if d.string(forKey: key) == nil, let choice = s.Values?["me:" + folder] {
                if choice == "none" { d.set("none", forKey: key) } else if let person = UUID(uuidString: choice) { d.set(person.uuidString, forKey: key) }
            }
            if s.Values?["me:" + folder] != nil || s.Values?["welcome:" + folder] != nil { WelcomeSheet.markSeen(id) }
            if let zone = s.SecondZones?[folder], TimeZone(identifier: zone) != nil, d.string(forKey: SecondZone.key(id)) == nil { d.set(zone, forKey: SecondZone.key(id)) }
            if synced[id.uuidString] == nil, let date = date(s.LastSync?[folder]) { synced[id.uuidString] = date; store.lastSync[id] = date }
        }
        d.set(synced, forKey: "lastSync")
        // A trip that did not open this time gets its answers when it does.
        if store.unreadable.isEmpty { d.set(true, forKey: tripsKey) }
    }

    /// .NET writes seven digits of a second, which ISO8601DateFormatter does not read.
    static func date(_ text: String?) -> Date? {
        text.flatMap { try? Date($0.replacing(/\.\d+/, with: ""), strategy: .iso8601) }
    }

    /// Its calendars, which hold nothing but its entries: the app writes its own, so nothing is there twice.
    private static func removeCalendars() {
        guard EKEventStore.authorizationStatus(for: .event) == .fullAccess,
              let kept = (try? Data(contentsOf: TripLibrary.support.appendingPathComponent("calendars.json"))).flatMap({ try? JSONDecoder().decode([String: Kept].self, from: $0) }) else { return }
        let events = EKEventStore()
        for id in kept.values.compactMap(\.Id) { if let calendar = events.calendar(withIdentifier: id) { try? events.removeCalendar(calendar, commit: true) } }
    }
}
