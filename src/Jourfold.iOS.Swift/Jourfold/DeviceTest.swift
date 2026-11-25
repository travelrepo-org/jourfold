#if JOURFOLD_DEBUG_TOOLS
import Foundation
import TravelRepoKit
import UIKit

/// `-devicetest YES`: does on a device what a person would, in a trip named "Test" that is never shared, and says
/// what worked. Other trips are only read; a shared one is synced when there is nothing of its own to send.
@MainActor
enum DeviceTest {
    static func run(_ store: TripStore, _ account: Account?) async {
        func say(_ name: String, _ ok: Bool, _ detail: String = "") { print("REPORT \(ok ? "PASS" : "FAIL") \(name) \(detail)") }
        let zone = "Pacific/Auckland"
        var here = Calendar(identifier: .gregorian); here.timeZone = TimeZone(identifier: zone)!
        let now = Date()
        func day(_ d: Date) -> DateComponents { here.dateComponents([.year, .month, .day], from: d) }
        do {
            if !store.trips.contains(where: { $0.title == "Test" }) {
                _ = try await TripLibrary.create(title: "Test", start: day(now), end: day(now.addingTimeInterval(2 * 86400)), zone: zone, currency: "EUR", traveller: "Alex", store: store, account: nil)
            }
            guard let trip = store.trips.first(where: { $0.title == "Test" }), let bridge = TravelRepoBridge.all[trip.id], !bridge.isShared else { say("trip", false); return }
            say("trip made", true)
            for person in trip.people.prefix(1) where store.me[trip.id] == nil { store.me[trip.id] = person.id; UserDefaults.standard.set(person.id.uuidString, forKey: "me-\(trip.id.uuidString)") }

            // A journey in 20 minutes, with its two places.
            let from = Place(id: UUID.v7(), name: "Britomart", latitude: -36.8443, longitude: 174.7680, zone: zone, address: "Britomart, Auckland")
            let to = Place(id: UUID.v7(), name: "Auckland Airport", latitude: -37.0082, longitude: 174.7850, zone: zone, address: "Auckland Airport, Auckland")
            // `-later YES`: nothing more today, to see the widget say so.
            let start = DebugTools.flag("later") ? here.date(bySettingHour: 14, minute: 0, second: 0, of: now.addingTimeInterval(86400))! : now.addingTimeInterval(20 * 60), end = start.addingTimeInterval(45 * 60)
            var journey = PlanItem(id: UUID.v7(), title: "Train to the airport", status: .confirmed, kind: .transport, when: .exact(start: start, startZone: zone, end: end, endZone: zone))
            journey.transport = Transport(mode: .train, from: from.id, to: to.id)
            let night = here.date(bySettingHour: 10, minute: 0, second: 0, of: now.addingTimeInterval(86400))!
            var stay = PlanItem(id: UUID.v7(), title: "Hotel with a long name at the harbour in Auckland", status: .confirmed, kind: .accommodation, when: .exact(start: now.addingTimeInterval(-20 * 3600), startZone: zone, end: night, endZone: zone))
            stay.stay = Stay(place: from.id)
            store.update(trip.id, "Test") { t in t.places += [from, to]; t.items += [journey, stay] }
            await bridge.flush(); try await bridge.read()
            let read = store.trips.first { $0.id == trip.id }?.items.first { $0.title == journey.title }
            say("journey kept its time", read?.when.start.map { abs($0.timeIntervalSince(start)) < 60 } ?? false, "zone \(read?.when.zone.identifier ?? "-")")

            // Cash and a receipt.
            var cash = CashRate(way: .perUnit); cash.perUnit = "160"
            let made = try await bridge.session.call("add_expense", ["title": "Test lunch", "amount": "1600", "currency": "JPY", "paid_by": [.string(TravelRepoBridge.key(trip.people[0].id))]])
            guard let id = made["id"]?.string ?? made["entity"]?["id"]?.string, let uuid = UUID(uuidString: id) else { say("expense", false); return }
            _ = try await bridge.session.call("set_cash", cash.arguments(for: id))
            let jpeg = UIGraphicsImageRenderer(size: CGSize(width: 200, height: 300)).jpegData(withCompressionQuality: 0.8) { c in UIColor.white.setFill(); c.fill(CGRect(x: 0, y: 0, width: 200, height: 300)) }
            try await bridge.importFile(jpeg, name: "Receipt.jpg", mediaType: "image/jpeg", attachTo: uuid)
            try await bridge.read()
            say("cash 1600 JPY at 160", try await bridge.costDetails().expenses.first { $0.id == id }?.charged?.amount == 10)
            _ = try await bridge.session.call("set_budget", ["title": "Test budget", "amount": "100"])
            say("receipt", ReceiptSection.documents(trip.id, uuid).count == 1)
            let calculator = try await PayBackCalculator(bridge)
            say("paying back in a copy", (try await calculator.transfers(without: [], currency: "EUR")).isEmpty)

            // What the share extension would leave, through the store this build shares with it.
            let file = SharedInbox.Entry(text: "Test.pdf", url: nil, trip: "Test", tripID: trip.id, plan: journey.title, planID: journey.id, files: ["Test.pdf"])
            let kept = SharedInbox.putFile(Data("%PDF-1.4 test".utf8), name: "Test.pdf", entry: file.id)
            SharedInbox.put(file)
            SharedInbox.put(.init(text: "Example page", url: "https://example.org/", trip: "Test", tripID: trip.id, plan: nil))
            SharedInbox.put(.init(text: "Viewpoint", url: "https://maps.apple.com/place?name=Test%20Viewpoint&coordinate=-36.8500,174.7700", trip: "Test", tripID: trip.id, plan: nil, newPlace: true))
            say("shared store keeps a file", kept && SharedInbox.pending().count == 3)

            LiveJourney.start(store.trips.first { $0.id == trip.id } ?? trip, read ?? journey)
            say("live activity running", LiveJourney.isRunning(read ?? journey))
            _ = await bridge.createVersion("Test"); try await bridge.read()
            say("version", store.trips.first { $0.id == trip.id }?.versions.contains { $0.title == "Test" } ?? false)
            say("Today in Auckland", store.trips.first { $0.id == trip.id }.map { $0.calendar.ymd($0.today(now)) == day(now) } ?? false)
            say("notices for the journey", !TravelNotices.upcoming(store.trips.first { $0.id == trip.id } ?? trip, now: now, before: 15).isEmpty)

            for t in store.trips where t.title != "Test" {
                guard let b = TravelRepoBridge.all[t.id], b.isShared else { continue }
                let s = try await b.session.syncStatus(remote: "origin")
                guard !s.hasLocalChanges, s.ahead == 0 else { say("sync skipped, it has changes of its own", true); continue }
                let before = store.lastSync[t.id]
                await store.sync(t.id)
                say("sync with GitHub", store.syncProblem[t.id] == nil && store.lastSync[t.id] != before)
            }
        } catch { say("stopped: \(String(describing: error).prefix(200))", false) }
    }
}
#endif
