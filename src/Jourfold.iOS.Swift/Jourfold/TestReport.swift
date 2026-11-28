import EventKit
import Foundation
import Security
import UIKit
import UserNotifications

/// For testing on a device: `-testreport YES` prints what the app and its extensions did as counts, states and times,
/// then ends. Nothing of a trip's content is printed, except from a trip named "Test", which is made for this.
/// `-testlog on` (or off) keeps a short list of events for it on the device.
enum TestReport {
    nonisolated static var on: Bool { DebugTools.inBuild && UserDefaults.standard.bool(forKey: "testlog.on") }

    static func applyArguments() {
        switch DebugTools.value("testlog") {
        case "on": UserDefaults.standard.set(true, forKey: "testlog.on")
        case "off": UserDefaults.standard.set(false, forKey: "testlog.on"); UserDefaults.standard.removeObject(forKey: "testlog.events")
        default: break
        }
    }

    /// An event without content, such as "share taken, 1 file".
    nonisolated static func note(_ event: String) {
        guard on else { return }
        var all = UserDefaults.standard.stringArray(forKey: "testlog.events") ?? []
        all.append("\(Date().formatted(.iso8601)) \(event)")
        UserDefaults.standard.set(Array(all.suffix(120)), forKey: "testlog.events")
    }

    #if JOURFOLD_DEBUG_TOOLS
    @MainActor
    static func run(_ store: TripStore, _ account: Account?) async {
        let center = UNUserNotificationCenter.current()
        let settings = await center.notificationSettings()
        print("REPORT at \(Date().formatted(.iso8601)) zone \(TimeZone.current.identifier) language \(Locale.current.language.languageCode?.identifier ?? "?")")
        print("REPORT account \(account?.login != nil) trips \(store.trips.count) unreadable \(store.unreadable.count) voiceover \(UIAccessibility.isVoiceOverRunning)")
        for (i, t) in store.trips.enumerated() {
            guard let b = TravelRepoBridge.all[t.id] else { continue }
            while b.weatherLoading { try? await Task.sleep(for: .milliseconds(300)) }
            let weatherDone = await b.updateWeather()
            let status = try? await b.session.syncStatus(remote: b.isShared ? "origin" : nil)
            print("REPORT trip \(i)\(t.title == "Test" ? " Test" : "") shared \(b.isShared) me \(store.me[t.id] != nil) people \(t.people.count) plans \(t.items.count) places \(t.places.count) withPosition \(t.places.filter(\.hasPosition).count) bookings \(t.bookings.count) expenses \(t.expenses.count) tasks \(t.tasks.count) done \(t.tasks.filter(\.done).count) files \(b.documents().count) notes \(t.notes.count) versions \(t.versions.count) errors \(b.diagnostics.count) zoneNow \(t.currentZone(Date()).identifier)")
            print("REPORT trip \(i) sync state \(status?.state ?? "-") ahead \(status?.ahead ?? -1) behind \(status?.behind ?? -1) localChanges \(status?.hasLocalChanges ?? false) last \(store.lastSync[t.id].map { $0.formatted(.iso8601) } ?? "-") problem \(store.syncProblem[t.id] != nil) weatherDays \(b.weather.count) weatherProblems \(b.weatherProblems.count) weatherOn \(Online.weatherOn) done \(weatherDone) trend \(b.weather.values.filter { $0.kind == "trend" }.count) climate \(b.weather.values.filter { $0.kind == "climate" }.count) tripDays \(t.days.count) raw \((try? await b.session.fetchWeather())?["days"]?.array?.count ?? -1) plansWithWeather \(b.itemWeather.values.filter { !$0.isEmpty }.count) forecast \(b.weather.values.filter { $0.kind == "forecast" }.count)")
            print("REPORT trip \(i) notices planned next 7 days \(TravelNotices.upcoming(t, now: Date(), before: 60).count) calendar events \(SystemCalendar.events(of: t))")
            if t.title == "Test" {
                for p in t.places { print("REPORT Test place '\(p.name)' \(p.latitude),\(p.longitude)") }
                for item in t.items { print("REPORT Test plan '\(item.title)' start \(item.when.start.map { $0.formatted(.iso8601) } ?? "-") zone \(item.when.zone.identifier) files \(item.attachments.count)") }
                for e in t.expenses { print("REPORT Test expense '\(e.title)' \(e.amount.value) \(e.amount.currency) receipts \(ReceiptSection.documents(t.id, e.id).count)") }
                for d in b.documents() { print("REPORT Test file '\(d.name)' \(d.mediaType)") }
            }
        }
        let pending = await center.pendingNotificationRequests(), delivered = await center.deliveredNotifications()
        let late = store.trips.flatMap { t in TravelNotices.upcoming(t, now: Date(), before: UserDefaults.standard.object(forKey: "notifyBefore") as? Int ?? 60, days: 7).compactMap { n in pending.first { $0.identifier == "\(t.id.uuidString)/\(n.id)" }.flatMap { ($0.trigger as? UNCalendarNotificationTrigger)?.nextTriggerDate() }.map { abs($0.timeIntervalSince(n.at)) } } }
        print("REPORT notices off by at most \(Int(late.max() ?? -1)) s, matched \(late.count) of \(pending.count)")
        let next = pending.compactMap { ($0.trigger as? UNCalendarNotificationTrigger)?.nextTriggerDate() }.sorted().prefix(3).map { $0.formatted(.iso8601) }
        print("REPORT notifications allowed \(settings.authorizationStatus == .authorized) sound \(settings.soundSetting == .enabled) pending \(pending.count) own \(pending.filter { $0.identifier.hasPrefix("remind-") }.count) next \(next) delivered \(delivered.count)")
        print("REPORT calendar access \(EKEventStore.authorizationStatus(for: .event) == .fullAccess) on \(SystemCalendar.on) perKind \(SystemCalendar.byKind) problem \(SystemCalendar.problem != nil)")
        func time(_ key: String) -> String { SharedStore.get(key).flatMap { String(data: $0, encoding: .utf8) } ?? "never" }
        print("REPORT shared store \(SharedStore.usesKeychain ? "keychain" : "group") widget read \(time("seen/widget")) share sheet opened \(time("seen/share")) inbox \(SharedInbox.pending().count) upcoming \(SharedInbox.upcoming().count) trips offered \(SharedInbox.trips().count) costs \(SharedInbox.costs().count) widget set to \((time("seen/widget-config")).split(separator: " ").first.map(String.init) ?? "-") one trip \(UUID(uuidString: time("seen/widget-config").split(separator: " ").last.map(String.init) ?? "") != nil)")
        for e in UserDefaults.standard.stringArray(forKey: "testlog.events") ?? [] { print("REPORT event \(e)") }
        print("REPORT end")
    }
    #endif
}
