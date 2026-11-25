#if JOURFOLD_DEBUG_TOOLS
import Foundation
import UIKit
import os
import TravelRepoKit

extension SelfTest {
    private static let bridgeLog = Logger(subsystem: "dev.jourfold.app", category: "selftest")

    /// The trip comes from TravelRepo; the usual checks run on it, then checks that
    /// edits and versions really reach the TravelRepo trip folder.
    @MainActor
    static func runTravelRepo(_ store: TripStore) async {
        var failures = 0
        func check(_ name: String, _ ok: Bool) {
            print(ok ? "PASS travelrepo: \(name)" : "FAIL travelrepo: \(name)"); if ok { bridgeLog.info("PASS travelrepo: \(name, privacy: .public)") } else { failures += 1; bridgeLog.error("FAIL travelrepo: \(name, privacy: .public)") }
        }
        guard let first = store.trips.first, first.title == "Spring in Japan", let bridge = TravelRepoBridge.all[first.id] else { check("attached", false); return }
        let trip = store.trips[0]
        let sample = SampleData.springInJapan()
        check("all plans read from TravelRepo", trip.items.count == sample.items.count && trip.people.count == sample.people.count && trip.places.count == sample.places.count)
        let flight = trip.items.first { $0.transport?.mode == .flight && $0.when.zone.identifier == "Europe/Berlin" }
        check("flight kind from TravelRepo is transport", flight?.kind == .transport)
        check("days and nights from TravelRepo's agenda", !(Trip.travelRepoDays[trip.id]?.isEmpty ?? true) && !(Trip.travelRepoNights[trip.id]?.isEmpty ?? true))
        let typo = await bridge.search("shinkanzen kyoto", trip: trip.id)
        check("search through TravelRepo forgives a typo", typo?.items.compactMap { trip.item($0)?.title }.first == "Shinkansen to Kyoto")
        check("flight zones survive the round trip", flight?.when.zone.identifier == "Europe/Berlin" && flight?.when.endZone.identifier == "Asia/Tokyo")
        let dinner = trip.items.first { $0.title == "Dinner at Omoide Yokocho" }
        check("dinner kind from TravelRepo's category is food", dinner?.kind == .food)
        check("bookings and expenses read from TravelRepo", trip.bookings.count == sample.bookings.count && trip.expenses.count == sample.expenses.count)
        check("who pays whom from TravelRepo's settle-up", CostsView.bridged[trip.id]?.count == 1)
        check("tasks, budgets and notes read from TravelRepo", trip.tasks.count == sample.tasks.count && trip.budgets.count == sample.budgets.count
              && trip.budgets.contains { $0.category == .food } && trip.notes == sample.notes && trip.openTasks == sample.openTasks)
        check("first version in Git", trip.versions.contains { $0.title == "Add the sample trip" })
        if let info = try? await TravelRepo.info() { check("TravelRepo \(info.version), ABI \(info.abiVersion)", info.abiVersion == TravelRepo.abiVersion) }

        // A plan added in the app reaches the trip folder, an edit keeps fields the app does not know, and a deletion
        // removes it again. A plan of its own, so the usual checks below find the sample as it was.
        let picnic = PlanItem(id: UUID(uuidString: "0196a3c1-7d2e-7a41-9b6c-3e5f7a8d9c42")!, title: "Picnic in Nara Park", status: .idea, kind: .nature, when: .unscheduled(minutes: 90))
        store.update(trip.id, "Add a picnic") { $0.items.append(picnic) }
        await bridge.write(store.trips[0])
        func saved() async -> [String: JSONValue]? {
            (try? await bridge.session.snapshot())?.entities.first { $0.data["id"]?.string == TravelRepoBridge.key(picnic.id) }?.data.object
        }
        check("new plan written to TravelRepo", await saved()?["title"]?.string == "Picnic in Nara Park")
        store.update(trip.id, "Reserve the picnic") { t in if let i = t.items.firstIndex(where: { $0.id == picnic.id }) { t.items[i].status = .reserved } }
        await bridge.write(store.trips[0])
        let reserved = await saved()
        check("status written to TravelRepo", reserved?["status"]?.string == "reserved")
        check("schema fields kept", reserved?["extensions"] != nil)
        _ = await bridge.createVersion("Try a picnic")
        check("version created through Git", store.trips[0].versions.first?.title == "Try a picnic")
        store.update(trip.id, "Remove the picnic") { $0.items.removeAll { $0.id == picnic.id } }
        await bridge.write(store.trips[0])
        check("deleted in TravelRepo", await saved() == nil)


        // A task ticked in the app and a changed note reach TravelRepo.
        if let open = store.trips[0].tasks.first(where: { !$0.done }) {
            store.update(trip.id, "Task") { t in if let i = t.tasks.firstIndex(where: { $0.id == open.id }) { t.tasks[i].done = true } }
            store.update(trip.id, "Notes") { t in t.notes += "\n• Coins for the omikuji" }
            await bridge.write(store.trips[0])
            let saved = try? await bridge.session.snapshot()
            check("task status written to TravelRepo", saved?.entities.first { $0.data["id"]?.string == TravelRepoBridge.key(open.id) }?.data["status"]?.string == "completed")
            let notes = (try? await bridge.session.readResource(TravelRepoBridge.notesFile)).map { String(decoding: $0, as: UTF8.self) } ?? ""
            check("notes written to TravelRepo", notes.hasSuffix("Coins for the omikuji"))
            store.update(trip.id, "Task") { t in if let i = t.tasks.firstIndex(where: { $0.id == open.id }) { t.tasks[i].done = false } }
            store.update(trip.id, "Notes") { t in t.notes = sample.notes }
            await bridge.write(store.trips[0])
        } else { check("an open task", false) }

        // Packing lists: read with every item and who packed what, and a tick in the app reaches TravelRepo.
        let lists = store.trips[0].packing
        check("packing lists read from TravelRepo", lists.count == sample.packing.count && lists.flatMap(\.items).count == sample.packing.flatMap(\.items).count
              && lists.flatMap(\.items).first { $0.title == "Comfortable walking shoes" }?.packedBy.count == 2)
        if let li = lists.indices.first, let ii = lists[li].items.firstIndex(where: { $0.title == "Power bank" }), let alex = store.trips[0].people.first?.id {
            store.update(trip.id, "Packed") { t in t.packing[li].items[ii].packedBy.insert(alex) }
            await bridge.write(store.trips[0])
            let packed = (try? await bridge.session.packingLists())?.flatMap(\.items).first { $0.title == "Power bank" }?.packedBy ?? []
            check("tick written to TravelRepo", packed.contains(TravelRepoBridge.key(alex)))
            store.update(trip.id, "Unpacked") { t in t.packing[li].items[ii].packedBy.remove(alex) }
            await bridge.write(store.trips[0])
            let unpacked = (try? await bridge.session.packingLists())?.flatMap(\.items).first { $0.title == "Power bank" }?.packedBy ?? ["?"]
            check("tick removed in TravelRepo", !unpacked.contains(TravelRepoBridge.key(alex)))
        } else { check("an item to tick", false) }

        // Sync with "Sam's phone" and merge through TravelRepo: Sam moved the tea ceremony half an hour later and invited
        // everyone, Alex moved it half an hour earlier here. Who goes merges by itself; the time is a conflict, and
        // taking Sam's answer wins.
        let teaBefore = store.trips[0].items.first { $0.title == "Tea ceremony" }?.when.start
        await store.sync(trip.id)
        if let merge = store.pendingMerge {
            check("sync finds the conflict on the time", !merge.parts.isEmpty && merge.parts.allSatisfy { $0.title == "Tea ceremony" })
            store.pendingMerge = nil
            await bridge.merge(merge, choices: merge.parts.map { _ in .theirs })
            let tea = store.trips[0].items.first { $0.title == "Tea ceremony" }
            check("merged: Sam's time and everyone goes", tea?.when.start.map { $0.timeIntervalSince(teaBefore ?? $0) } == 1800 && tea?.participants.count == store.trips[0].people.count)
            check("merge recorded in Git", store.trips[0].versions.first?.title == String(localized: "Take over changes from Sam"))
        } else { check("sync finds the conflict on the time", false) }

        // Who the user is: nobody at first, "That's me" on Alex, then TravelRepo recognises the login.
        let nobody = await bridge.me(login: nil)
        if let alex = store.trips[0].people.first(where: { $0.name == "Alex" })?.id, nobody?.person == nil {
            let claimed = await bridge.claim(alex, login: nil)
            check("That's me through TravelRepo", claimed?.person == alex)
            check("another account sees Alex as taken", await bridge.me(login: "sam-travels")?.taken.contains(alex) == true)
        } else { check("That's me through TravelRepo", false) }

        // Editing keeps what the app cannot show: a flight without a departure place, people inherited from the group,
        // and an expense that two people paid.
        let flightID = UUID.v7(), dinnerID = UUID.v7(), both = store.trips[0].people.map { JSONValue.string(TravelRepoBridge.key($0.id)) }
        let hnd = store.trips[0].places.first { $0.name == "Tokyo Haneda Airport" }!.id
        let flightData: [String: JSONValue] = ["id": .string(TravelRepoBridge.key(flightID)), "type": "schedule_item", "title": "Flight home", "status": "planned", "participants": ["inherit": true, "values": []], "extensions": [:],
                                               "components": ["transport": ["type": "flight", "arrival": ["place": .string(TravelRepoBridge.key(hnd))]]]]
        let dinnerData: [String: JSONValue] = ["id": .string(TravelRepoBridge.key(dinnerID)), "type": "expense", "title": "Dinner", "estimated": false, "amount": ["value": "60", "currency": "EUR"], "extensions": [:],
                                               "category": "insurance", "paid_by": .array(both), "split": ["method": "equal", "parts": .array(both.map { ["person": $0] })]]
        do {
            try await bridge.session.apply(revision: try await bridge.session.revision(), edits: [TravelRepoBridge.key(flightID): flightData, TravelRepoBridge.key(dinnerID): dinnerData])
            try await bridge.read()
            store.update(trip.id, "Rename") { t in
                if let i = t.items.firstIndex(where: { $0.id == flightID }) { t.items[i].title = "Flight back home" }
                if let i = t.expenses.firstIndex(where: { $0.id == dinnerID }) { t.expenses[i].title = "Dinner together" }
            }
            await bridge.flush()
            let f = bridge.entities[TravelRepoBridge.key(flightID)] ?? [:], d = bridge.entities[TravelRepoBridge.key(dinnerID)] ?? [:]
            check("edit keeps a missing departure and inherited people", f["title"]?.string == "Flight back home" && f["components"]?["transport"]?["departure"] == nil && f["participants"]?["inherit"]?.bool == true)
            check("edit keeps two payers and an unknown category", d["title"]?.string == "Dinner together" && d["paid_by"]?.array?.count == 2 && d["category"]?.string == "insurance")
            _ = try await bridge.session.deleteEntities([TravelRepoBridge.key(flightID), TravelRepoBridge.key(dinnerID)]); try await bridge.read()
        } catch { check("edit keeps what the app cannot show: \(error)", false) }

        // A weather warning for a plan becomes a notice on the morning of its day.
        if let tea = store.trips[0].items.first(where: { $0.title == "Tea ceremony" }), let start = tea.when.start {
            bridge.weatherConcerns = [tea.id: [WeatherConcern(kind: "rain", severe: false, value: 12)]]
            let notices = TravelNotices.upcoming(store.trips[0], now: start.addingTimeInterval(-86400), before: 30, days: 2)
            check("weather warning as a notice", notices.contains { $0.kind == .weather && $0.item == tea.id && $0.at <= start.addingTimeInterval(-2 * 3600) })
            bridge.weatherConcerns = [:]
        }

        // The plan's lines follow the agenda: the check-out of the first hotel is on its last day.
        do {
            let t = store.trips[0], hotel = t.items.first { $0.title == "Hotel Gracery Shinjuku" }
            let lines = t.days.indices.contains(4) ? t.lines(on: t.days[4]) : []
            check("check-out is a line of its day (\(lines.map(\.role)), days \(Trip.agenda[t.id]?.count ?? -1))", lines.contains { $0.item == hotel?.id && $0.role == "check_out" })
        }

        // Costs: days, who owes whom, paying back and taking it back, a planned payment that gets paid.
        if let c = try? await bridge.costDetails() {
            check("costs have days and people", c.expenses.contains { $0.date != nil } && c.expenses.contains { !$0.split.parts.isEmpty } && c.paid_in != nil)
            check("costs say who owes whom", !c.balances.isEmpty && !c.settle_up.isEmpty)
            let count = c.expenses.count
            if let e = c.expenses.first(where: { $0.split.debts.contains { $0.open > 0 } }), let id = e.uuid, let debtor = e.split.debts.first(where: { $0.open > 0 })?.from {
                await bridge.changeCosts { try await bridge.markPaidBack(id, person: try bridge.personNamed(debtor), true) }
                let after = try? await bridge.costDetails()
                check("paid back is a mark, not a new expense", after?.expenses.first { $0.id == e.id }?.paidBack == true && after?.expenses.count == count)
                await bridge.changeCosts { try await bridge.markPaidBack(id, person: try bridge.personNamed(debtor), false) }
                let again = try? await bridge.costDetails()
                check("the mark can be taken back", again?.expenses.first { $0.id == e.id }?.split.open == e.split.open)
            } else { check("an open debt", false) }
            if let p = c.expenses.first(where: { $0.planned }), let id = p.uuid {
                let stored = bridge.entities[p.id]
                await bridge.changeCosts { try await bridge.recordPayment(id) }
                let paid = try? await bridge.costDetails()
                check("a planned payment gets paid", paid?.expenses.first { $0.id == p.id }?.planned == false)
                if let stored { await bridge.changeCosts { try await bridge.session.apply(revision: try await bridge.session.revision(), edits: [p.id: stored]) } }
            } else { check("a planned payment", false) }
            let sam = store.trips[0].people.first { $0.name == "Sam" }!.id
            let mine = try? await bridge.costDetails(for: [sam])
            check("categories for one person are less than for everyone", (mine?.by_category.reduce(Decimal(0)) { $0 + $1.amount } ?? 0) < c.by_category.reduce(Decimal(0)) { $0 + $1.amount })
        } else { check("costs from get_costs", false) }

        // Signed in, a person that carries an e-mail is marked as another identity; it must still be selectable, stay
        // selected, and keep its identity unchanged.
        if let sam = store.trips[0].people.first(where: { $0.name == "Sam" })?.id, var data = bridge.entities[TravelRepoBridge.key(sam)] {
            data["identities"] = ["git": [["name": "Sam", "email": "sam@example.org"]]]
            do {
                try await bridge.session.apply(revision: try await bridge.session.revision(), edits: [TravelRepoBridge.key(sam): data]); try await bridge.read()
                check("own person with an e-mail is marked", await bridge.me(login: "sam-travels")?.taken.contains(sam) == true)
                check("and can be chosen all the same", await bridge.claim(sam, login: "sam-travels")?.person == sam)
                try await bridge.read()
                check("the choice stays and the identity is untouched", await bridge.me(login: "sam-travels")?.person == sam && bridge.entities[TravelRepoBridge.key(sam)]?["identities"] == data["identities"])
                check("not travelling along", await bridge.claimNobody()?.person == nil)
            } catch { check("own person with an e-mail: \(error)", false) }
        } else { check("own person with an e-mail", false) }

        await extras(store, bridge, check)

        // The calendar: one calendar for the trip, with an event per entry of TravelRepo's `calendar`.
        if await SystemCalendar.allowed(ask: false) {
            UserDefaults.standard.set(true, forKey: "calendar")
            await SystemCalendar.update([store.trips[0]])
            let entries = (try? await SystemCalendar.entries(bridge)) ?? []
            let events = SystemCalendar.events(of: store.trips[0])
            check("calendar of the trip with every entry (\(events)/\(entries.count)) \(SystemCalendar.problem ?? "")", events == entries.count && events > 0)
            UserDefaults.standard.set(true, forKey: "calendar.kinds")
            SystemCalendar.removeAll()
            await SystemCalendar.update([store.trips[0]])
            check("one calendar per kind with every entry", SystemCalendar.events(of: store.trips[0]) == entries.count)
            UserDefaults.standard.set(false, forKey: "calendar.kinds")
            SystemCalendar.removeAll()
            check("calendar removed again", SystemCalendar.events(of: store.trips[0]) == 0)
            UserDefaults.standard.set(false, forKey: "calendar")
        }

        // The trip library: a new trip, a clone from the demo's GitHub, going back to a version, removing a trip.
        let account = Account()
        if let created = try? await TripLibrary.create(title: "Weekend in Lyon", start: SampleData.day(2027, 5, 14), end: SampleData.day(2027, 5, 16), zone: "Europe/Paris",
                                                        currency: "EUR", traveller: "Alex", store: store, account: account) {
            let lyon = store.trips.first { $0.id == created }
            check("new trip in a folder of its own", lyon?.title == "Weekend in Lyon" && lyon?.people.map(\.name) == ["Alex"] && lyon?.zone == "Europe/Paris" && lyon?.versions.first?.title == "Start the trip")
            check("new trip knows who I am", await TravelRepoBridge.all[created]?.me(login: nil)?.person == lyon?.people.first?.id)
            // A plan, a version, another change, then back to the version.
            store.update(created, "Add") { $0.items.append(PlanItem(id: UUID.v7(), title: "Basilica of Fourvière", status: .planned, kind: .sightseeing, when: .unscheduled(minutes: 60))) }
            _ = await TravelRepoBridge.all[created]?.createVersion("With Fourvière")
            store.update(created, "Rename") { t in if let i = t.items.firstIndex(where: { $0.title == "Basilica of Fourvière" }) { t.items[i].title = "Fourvière" } }
            await TravelRepoBridge.all[created]?.flush()
            if let bridge = TravelRepoBridge.all[created], let version = store.trips.first(where: { $0.id == created })?.versions.first(where: { $0.title == "With Fourvière" }), let commit = version.commit,
               let preview = try? await bridge.session.restorePreview(commit: commit) {
                check("restore preview names the changed plan", preview.changes.count == 1)
                _ = try? await bridge.session.restoreApply(commit: commit, plan: preview.plan)
                try? await bridge.read()
                check("back to the version", store.trips.first { $0.id == created }?.items.first?.title == "Basilica of Fourvière")
            } else { check("restore preview", false) }
            try? TripLibrary.remove(created, store: store)
            check("the split's fields convert and add up", SplitConversion.convert([0, 0, 0], from: "equal", to: "amount", total: 100, digits: 2) == [Decimal(string: "33.34")!, Decimal(string: "33.33")!, Decimal(string: "33.33")!]
                  && SplitConversion.convert([1, 2], from: "share", to: "amount", total: 1000, digits: 0) == [334, 666] && SplitConversion.convert([25, 75], from: "amount", to: "percent", total: 100, digits: 2) == [25, 75]
                  && SplitConversion.convert([0, 0], from: "share", to: "amount", total: 10, digits: 2) == nil)
            check("barcodes of tickets", ["PKBarcodeFormatQR", "PKBarcodeFormatAztec", "PKBarcodeFormatPDF417", "PKBarcodeFormatCode128"].allSatisfy { BarcodeImage.make("M1ALEX/EXAMPLE FRAHND", format: $0, encoding: "iso-8859-1") != nil } && BarcodeImage.make("x", format: "other", encoding: "") == nil)
            let photo = UIGraphicsImageRenderer(size: .init(width: 3000, height: 2000)).image { _ in }
            check("a photo of a receipt is made smaller", CameraButton.jpeg(photo).flatMap(UIImage.init(data:))?.size.width == 1600)
            let berlin = Calendar(identifier: .gregorian).date(from: DateComponents(timeZone: TimeZone(identifier: "Europe/Berlin"), year: 2027, month: 1, day: 8, hour: 13, minute: 20))!
            check("the clock time stays when the zone changes", PlanEditor.sameClock(berlin, "Europe/Berlin", "Asia/Tokyo") == berlin.addingTimeInterval(-8 * 3600) && PlanEditor.sameClock(berlin, "Europe/Berlin", "Europe/Berlin") == berlin)
            check("amounts with a comma", "12,50 ".amountText == "12.50")
            check("newer versions", Updates.compare("0.17.0", "0.16.1") > 0 && Updates.compare("0.16.1", "0.16.1") == 0 && Updates.compare("0.9.0", "0.10.0") < 0 && Updates.compare("x", "0.1.0") < 0)
            check("links that only the maps service knows", ShortLink("https://maps.app.goo.gl/abc") != nil && ShortLink("https://maps.apple.com/?ll=35.0036,135.7788") == nil && ShortLink("https://example.com/x") == nil)
            check("maps apps get the position", Maps.others(Place(id: UUID(), name: "Nara Park", latitude: 34.6851, longitude: 135.843, zone: "Asia/Tokyo", address: "")).map(\.link.absoluteString) == ["comgooglemaps://?q=34.685100,135.843000", "waze://?ll=34.685100,135.843000&navigate=yes"])
            // What an update from the .NET app or an earlier preview relies on.
            check("a date as .NET writes it", DotNetApp.date("2026-11-20T10:11:12.1234567+00:00") == Date(timeIntervalSince1970: 1_795_169_472))
            let token = GitHubSignIn.Keychain.token
            let item: [String: Any] = [kSecClass as String: kSecClassGenericPassword, kSecAttrService as String: "dev.jourfold.app", kSecAttrAccount as String: "github.user-token"]
            GitHubSignIn.Keychain.token = nil
            SecItemAdd(item.merging([kSecValueData as String: Data("made-up".utf8), kSecAttrAccessible as String: kSecAttrAccessibleAfterFirstUnlockThisDeviceOnly]) { $1 } as CFDictionary, nil)
            check("the sign-in of the .NET app is found", GitHubSignIn.Keychain.token == "made-up")
            GitHubSignIn.Keychain.token = token
            let box = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString), was = box.appendingPathComponent("was"), will = box.appendingPathComponent("will")
            for f in [was.appendingPathComponent("a"), was.appendingPathComponent("b"), will.appendingPathComponent("b")] { try? FileManager.default.createDirectory(at: f, withIntermediateDirectories: true) }
            TripLibrary.move(was, into: will); TripLibrary.move(will, into: box.appendingPathComponent("Will")); TripLibrary.move(box.appendingPathComponent("Will"), into: will)
            check("earlier folders move without replacing any", FileManager.default.fileExists(atPath: will.appendingPathComponent("a").path) && (try? FileManager.default.contentsOfDirectory(atPath: was.path)) == ["b"])
            try? FileManager.default.removeItem(at: box)
            check("removed trip goes to the recovery folder", !store.trips.contains { $0.id == created } && FileManager.default.fileExists(atPath: TripLibrary.deleted.path))
        } else { check("new trip", false) }
        account.login = TravelRepoBridge.demoLogin
        if let repository = try? await GitHubTrips.repositories(demo: true).first(where: { $0.fullName.hasSuffix("montreal") }) {
            var steps: [Double] = []
            let cloned = try? await GitHubTrips.clone(repository, store: store, account: account) { steps.append($0) }
            let montreal = store.trips.first { $0.id == cloned }
            check("cloned trip opens with its plans", montreal?.title == "Autumn in Montreal" && (montreal?.items.count ?? 0) > 0 && montreal?.zone == "America/Toronto")
            check("cloned trip remembers where it came from", cloned.flatMap { TravelRepoBridge.all[$0]?.remotes.contains { $0.name == "origin" } } == true)
            check("cloned trip is found again", GitHubTrips.localTrip(repository, in: store) == cloned)
            if let cloned { try? TripLibrary.remove(cloned, store: store) }
        } else { check("demo repositories", false) }
        if let first = store.trips.first { check("sample stays first", first.title == "Spring in Japan") }

        // The usual checks, now on the trip read from TravelRepo.
        await run(store)
        print("travelrepo self-test done, \(failures) failures, \(SelfTest.residentMegabytes()) MB in memory")
        bridgeLog.info("travelrepo self-test done, \(failures, privacy: .public) failures")
    }
}

extension SelfTest {
    /// Packing lines, map links, the shared store, cash, receipts, the trip's currency and paying back some items.
    static func extras(_ store: TripStore, _ bridge: TravelRepoBridge, _ check: (String, Bool) -> Void) async {
        let people = [Person(id: UUID.v7(), name: "Alex Example"), Person(id: UUID.v7(), name: "Sam"), Person(id: UUID.v7(), name: "Alex Sample")]
        let line = PackingLine("2x Charger #Electronic_things @sam @All", people: people)
        check("packing line", line.title == "Charger" && line.quantity == "2" && line.group == "Electronic things" && line.people == [people[1].id] && line.everyone && line.canAdd)
        let odd = PackingLine("Tent @alex @kim", people: people)
        check("packing line refuses unclear names", odd.ambiguous == ["alex"] && odd.unknown == ["kim"] && !odd.canAdd)
        check("packing line takes a full name", PackingLine("Tent @Alex_Sample x3", people: people).people == [people[2].id])
        check("packing suggestions", PackingLine.suggestions("Tent @al", people: people, groups: []).map(\.insert) == ["@all", "@Alex_Example", "@Alex_Sample"] && PackingLine.accept("Tent @al", "@all") == "Tent @all ")
        let apple = MapLink("https://maps.apple.com/place?name=Nara%20Park&coordinate=34.6851,135.8430")
        check("map link with a position", apple?.name == "Nara Park" && apple?.latitude == 34.6851 && apple?.longitude == 135.8430)
        let google = MapLink("https://www.google.com/maps/place/Nara+Park/@34.68,135.80,15z/data=!3d34.6851!4d135.8430")
        check("map link of Google", google?.name == "Nara Park" && google?.longitude == 135.8430)
        check("no map link", MapLink("https://example.org/maps") == nil)
        let entry = SharedInbox.Entry(text: "Self-test", url: nil, trip: "Nowhere", plan: nil, files: ["a.txt"])
        check("shared file is kept", SharedInbox.putFile(Data("a".utf8), name: "a.txt", entry: entry.id))
        SharedInbox.put(entry)
        check("shared entry comes back with its file", SharedInbox.pending().contains { $0.id == entry.id } && SharedInbox.file("a.txt", of: entry) == Data("a".utf8))
        SharedStore.remove("inbox/\(entry.id.uuidString)"); SharedInbox.clean(entry)
        check("shared entry is gone", !SharedInbox.pending().contains { $0.id == entry.id } && SharedInbox.file("a.txt", of: entry) == nil)
        let map = OfflineMap.project([.init(latitude: -36.85, longitude: 174.76), .init(latitude: 49.28, longitude: -123.12)], [], into: CGSize(width: 400, height: 400)).stops
        check("map across the date line", map.count == 2 && map[0].x < map[1].x && map[0].y > map[1].y && map.allSatisfy { (0...400).contains($0.x) && (0...400).contains($0.y) })

        // Today follows the place, whatever zone the phone is on.
        var away = Trip.empty(id: UUID.v7()); away.zone = "Pacific/Auckland"; away.start = SampleData.day(2027, 2, 1); away.end = SampleData.day(2027, 2, 10)
        func at(_ y: Int, _ m: Int, _ d: Int, _ h: Int, _ zone: String) -> Date { var c = Calendar(identifier: .gregorian); c.timeZone = TimeZone(identifier: zone)!; return c.date(from: DateComponents(year: y, month: m, day: d, hour: h))! }
        away.items = [PlanItem(id: UUID.v7(), title: "Flight to Auckland", status: .confirmed, kind: .transport, when: .exact(start: at(2027, 2, 1, 10, "Europe/Berlin"), startZone: "Europe/Berlin", end: at(2027, 2, 2, 20, "Pacific/Auckland"), endZone: "Pacific/Auckland")),
                      PlanItem(id: UUID.v7(), title: "Harbour walk", status: .planned, kind: .nature, when: .exact(start: at(2027, 2, 3, 9, "Pacific/Auckland"), startZone: "Pacific/Auckland", end: at(2027, 2, 3, 11, "Pacific/Auckland"), endZone: "Pacific/Auckland"))]
        check("before the flight in the zone it leaves from", away.currentZone(at(2027, 2, 1, 8, "Europe/Berlin")).identifier == "Europe/Berlin")
        let morning = at(2027, 2, 4, 8, "Pacific/Auckland")
        check("in Auckland it is Auckland's day while Germany has the day before", away.currentZone(morning).identifier == "Pacific/Auckland" && away.calendar.ymd(away.today(morning)) == away.calendar.ymd(morning))

        // The widget's day, where the plans are: 22:00 in Auckland is the morning of the same day in Germany.
        func plan(_ t: String, _ from: Date, _ to: Date, _ zone: String, _ role: String? = nil) -> SharedInbox.UpcomingPlan { .init(title: t, trip: "T", start: from, end: to, zone: zone, place: "", symbol: "", role: role) }
        let nz = "Pacific/Auckland", late = at(2027, 2, 3, 22, nz)
        let widget = SharedInbox.today([plan("over", at(2027, 2, 3, 9, nz), at(2027, 2, 3, 10, nz), nz), plan("night bus", at(2027, 2, 3, 21, nz), at(2027, 2, 4, 5, nz), nz), plan("bar", at(2027, 2, 3, 23, nz), at(2027, 2, 4, 1, nz), nz),
                                        plan("hotel", at(2027, 2, 4, 10, nz), at(2027, 2, 4, 10, nz), nz, "check_out"), plan("walk", at(2027, 2, 4, 12, nz), at(2027, 2, 4, 13, nz), nz), plan("later hotel", at(2027, 2, 5, 10, nz), at(2027, 2, 5, 10, nz), nz, "check_out")], at: late)
        check("widget of today and tomorrow's check-out", widget.today.map(\.title) == ["night bus", "bar"] && widget.leaving.map(\.title) == ["hotel"])
        let feed = WidgetFeed.plans(store)
        check("widget feed has stays as check-in and check-out", feed.contains { $0.role == "check_in" } && feed.contains { $0.role == "check_out" })

        let revision = bridge.revision
        do {
            let made = try await bridge.session.call("add_expense", ["title": "Self-test lunch", "amount": "3200", "currency": "JPY", "paid_by": [.string(TravelRepoBridge.key(bridge.lastRead.people[0].id))]])
            guard let id = made["id"]?.string ?? made["entity"]?["id"]?.string, let uuid = UUID(uuidString: id) else { check("expense for the checks", false); return }
            var cash = CashRate(way: .perUnit); cash.perUnit = "160"
            _ = try await bridge.session.call("set_cash", cash.arguments(for: id))
            try await bridge.read()
            check("cash at 160 yen per euro", (try await bridge.costDetails()).expenses.first { $0.id == id }?.charged?.amount == 20 && bridge.entities[id]?["converted"]?["source"]?.string == "cash")
            cash = CashRate(way: .machine); cash.received = "32000"; cash.debited = "210"
            _ = try await bridge.session.call("set_cash", cash.arguments(for: id))
            check("cash from the machine's receipt", (try await bridge.costDetails()).expenses.first { $0.id == id }?.charged?.amount == 21)
            try await bridge.importFile(Data([0xFF, 0xD8, 0xFF, 0xE0]), name: "Receipt.jpg", mediaType: "image/jpeg", attachTo: uuid)
            check("receipt on an expense", ReceiptSection.documents(bridge.tripID, uuid).map(\.name) == ["Receipt.jpg"])
            try await bridge.patch(id, ["category": "Street food"])
            check("own category", (try await bridge.session.call("vocabulary"))["custom_expense_categories"]?.array?.contains { $0.string == "Street food" } == true)
            // Only this lunch, everything else settled in the copy: the others owe the payer their part of 21 euros.
            let costs = try await bridge.costDetails()
            var settled: [(expense: String, debtor: UUID)] = []
            for e in costs.expenses where e.id != id { for d in Set(e.split.debts.filter { $0.open != 0 }.map(\.from)) { settled.append((e.id, try bridge.personNamed(d))) } }
            let calculator = try await PayBackCalculator(bridge)
            let one = try await calculator.transfers(without: settled, currency: "EUR")
            check("pay back one item in euros \(one.map { "\($0.from) \($0.to) \($0.amount)" })", one.count == 1 && one[0].amount == Decimal(string: "10.5") && one[0].currency == "EUR")
            let all = try await calculator.transfers(without: [], currency: "EUR")
            check("pay back everything is the settle-up", all.map(\.amount) == costs.settle_up.map(\.amount))
            check("paying back was only tried in the copy", (try await bridge.costDetails()).settle_up.map(\.amount) == costs.settle_up.map(\.amount))
            _ = try await bridge.session.call("set_trip_currency", ["currency": "CAD"])
            try await bridge.read()
            check("trip currency set", bridge.lastRead.currency == "CAD")
            _ = try await bridge.session.call("set_trip_currency", ["currency": "EUR"])
            try await bridge.read()
            check("costs as before in euros again", (try await bridge.costDetails()).settle_up.map(\.amount) == costs.settle_up.map(\.amount))
            nonisolated struct Preview: Decodable { let needed: Bool; let changes: [CostUpgradeSection.Change] }
            // As older versions of Jourfold wrote budgets.
            if let budget = bridge.entities.first(where: { $0.value["type"]?.string == "budget" })?.key { try await bridge.patch(budget, ["scope": "trip"]) }
            let upgrade = try await bridge.session.call("cost_upgrade_preview", as: Preview.self)
            let lines = CostUpgradeSection.lines(upgrade.changes)
            check("costs in an older form are offered: \(lines)", upgrade.needed == !lines.isEmpty)
            if upgrade.needed {
                let before = try await bridge.costDetails()
                _ = try await bridge.session.call("cost_upgrade_apply", ["message": "Costs brought up to date"])
                let again = try await bridge.session.call("cost_upgrade_preview", as: Preview.self), after = try await bridge.costDetails()
                check("costs brought up to date", !again.needed)
                check("the upgrade changes no debts \(before.settle_up.map(\.amount)) \(after.settle_up.map(\.amount)) earlier \(costs.settle_up.map(\.amount))", after.settle_up.map(\.amount) == before.settle_up.map(\.amount))
            }
            for key in [id] + ReceiptSection.documents(bridge.tripID, uuid).map({ TravelRepoBridge.key($0.id) }) { _ = try await bridge.session.call("delete_entity", ["id": .string(key)]) }
            try await bridge.read()
        } catch { check("costs extras from \(revision.prefix(8)): \(error)", false) }
    }

    /// The app's resident memory in megabytes, printed by the self-tests on a device.
    nonisolated static func residentMegabytes() -> Int {
        var info = mach_task_basic_info()
        var count = mach_msg_type_number_t(MemoryLayout<mach_task_basic_info>.size / MemoryLayout<natural_t>.size)
        let result = withUnsafeMutablePointer(to: &info) { $0.withMemoryRebound(to: integer_t.self, capacity: Int(count)) { task_info(mach_task_self_, task_flavor_t(MACH_TASK_BASIC_INFO), $0, &count) } }
        return result == KERN_SUCCESS ? Int(info.resident_size / 1_048_576) : -1
    }
}
#endif
