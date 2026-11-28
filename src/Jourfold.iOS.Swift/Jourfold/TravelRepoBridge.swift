import Foundation
import os
import TravelRepoKit

/// One trip in a TravelRepo trip folder on this device, read and written through TravelRepoKit (TravelRepo compiled
/// with NativeAOT). Everything the app shows comes from the folder: people, places, plans, bookings, costs, tasks,
/// packing, notes and versions (Git commits). The app's `Trip` is only a view of it; edits go back through `apply`
/// and keep the fields this app does not know. `TripLibrary` keeps one bridge per trip.
@MainActor
final class TravelRepoBridge {
    /// The open trips, by the trip's TravelRepo id.
    static var all: [UUID: TravelRepoBridge] = [:]
    static let log = Logger(subsystem: "dev.jourfold.app", category: "travelrepo")

    let session: TravelRepoSession
    let tripID: UUID
    let folder: URL
    let store: TripStore
    /// Entity data from the last read, by id, so edits keep fields this app does not know.
    private(set) var entities: [String: [String: JSONValue]] = [:]
    private(set) var revision = ""
    /// The trip as last read, to find what the app changed.
    private(set) var lastRead: Trip
    /// The weather by day, from TravelRepo's `fetch_weather`, and what went wrong fetching it.
    var weather: [DateComponents: DayWeather] = [:]
    var weatherProblems: [String] = []
    var weatherLoading = false
    private var weatherUpdate: Task<Void, Never>?
    /// The outlooks of each plan, and who the weather comes from.
    var itemWeather: [UUID: [(date: String?, weather: DayWeather)]] = [:]
    var weatherSources: [(text: String, url: String?)] = []
    /// What the weather means for each plan, from fetch_weather's items.
    var weatherConcerns: [UUID: [WeatherConcern]] = [:]
    /// travel.yaml as last read.
    private(set) var travel: [String: JSONValue] = [:]
    /// Resource files of the trip from the last read (documents and assets), by path.
    private(set) var resources: [String: Snapshot.Resource] = [:]
    /// Problems TravelRepo found in the trip, as the trip page lists them.
    private(set) var diagnostics: [Diagnostic] = []

    private init(session: TravelRepoSession, folder: URL, store: TripStore, tripID: UUID) {
        self.session = session; self.folder = folder; self.store = store; self.tripID = tripID
        lastRead = Trip.empty(id: tripID)
    }

    /// Opens the trip in `folder`, reads it and puts it into the store. Returns the trip's id.
    @discardableResult
    static func open(_ folder: URL, store: TripStore) async throws -> UUID {
        let session = try await TravelRepoSession.open(root: folder, recoveryRoot: TripLibrary.recovery(for: folder), culture: TripLibrary.culture, clientName: "Jourfold",
                                                        exchangeRates: TripLibrary.rates, weatherCache: TripLibrary.weatherCache(for: folder))
        try? await session.call("recover")
        let snapshot = try await session.snapshot()
        guard let id = snapshot.entities.first(where: { $0.path == "travel.yaml" })?.data["id"]?.string.flatMap(UUID.init(uuidString:)) else {
            throw AppError.message(String(localized: "This folder holds no trip."))
        }
        all[id]?.stopWatching()
        let bridge = TravelRepoBridge(session: session, folder: folder, store: store, tripID: id)
        all[id] = bridge
        try await bridge.read()
        _ = try? await session.watch(every: 5)
        Task { await bridge.updateWeather() }
        return id
    }

    /// Whether the trip has a GitHub remote, so syncing makes sense.
    var isShared: Bool { remotes.contains { $0.name == "origin" } }
    private(set) var remotes: [Remote] = []

    func stopWatching() { Task { _ = try? await session.watch(enabled: false) } }

    /// Writes an in-memory trip (a sample) into a new trip folder, as one transaction and a first version.
    static func seed(_ sample: Trip, into folder: URL, author: (name: String, email: String), message: String) async throws {
        let trip = try withVersion7Ids(sample)
        let language = TripLibrary.culture.hasPrefix("de") ? "de" : "en"
        try await TravelRepoSession.createTrip(root: folder, title: trip.title, language: language, timezone: trip.zone, gitName: author.name, gitEmail: author.email)
        let session = try await TravelRepoSession.open(root: folder, recoveryRoot: TripLibrary.recovery(for: folder), culture: TripLibrary.culture, clientName: "Jourfold")
        let snapshot = try await session.snapshot()
        var edits: [String: [String: JSONValue]?] = [:]
        var resources: [String: Data?] = [:]
        if let travel = snapshot.entities.first(where: { $0.path == "travel.yaml" })?.data.object, let id = travel["id"]?.string {
            edits[id] = tripData(trip, into: travel)
        }
        for person in trip.people { edits[key(person.id)] = personData(person, into: nil) }
        for place in trip.places { edits[key(place.id)] = placeData(place, into: nil) }
        for item in trip.items {
            var data = itemData(item, in: trip, into: nil)
            if !item.notes.isEmpty { resources[notesPath(item.id)] = Data(item.notes.utf8); data["content"] = contentWithNotes(item.id, into: data["content"]) }
            for attachment in item.attachments where attachment.kind == .link {
                data["content"] = .array((data["content"]?.array ?? []) + [["type": "link", "url": .string("https://" + attachment.name)]])
            }
            edits[key(item.id)] = data
            for comment in item.comments {
                edits[key(comment.id)] = ["id": .string(key(comment.id)), "type": "comment", "target": ["type": "schedule_item", "id": .string(key(item.id))], "author": .string(key(comment.author)),
                                          "created_at": .string(ISO8601DateFormatter().string(from: comment.date)), "body": .string(comment.body), "extensions": [:]]
            }
        }
        for booking in trip.bookings { edits[key(booking.id)] = bookingData(booking, into: nil) }
        for expense in trip.expenses { edits[key(expense.id)] = expenseData(expense, in: trip, into: nil) }
        for task in trip.tasks { edits[key(task.id)] = taskData(task, zone: trip.zone, into: nil) }
        for budget in trip.budgets { edits[key(budget.id)] = budgetData(budget, into: nil) }
        if !trip.notes.isEmpty { edits[notesID] = notesData(into: nil); resources[notesFile] = Data(trip.notes.utf8) }
        try await session.apply(revision: snapshot.revision, edits: edits, resources: resources)
        // Documents go through TravelRepo's import, which stores them by content and attaches them to the plan.
        for item in trip.items {
            for attachment in item.attachments where attachment.kind != .link {
                guard let made = SampleDocuments.make(attachment) else { continue }
                let base = attachment.name.replacingOccurrences(of: ".pkpass", with: "")
                let name = base.hasSuffix(".\(made.ext)") ? base : base + ".\(made.ext)"
                _ = try await session.importDocument(made.data, name: name, mediaType: made.mediaType, attachTo: key(item.id))
            }
        }
        try await writePacking(trip.packing, since: [], session: session)
        try await session.call("create_version", ["message": .string(message)])
    }

    /// Reads the whole trip from TravelRepo into the store. After a write, `wrote` is the trip that was written: if the
    /// app changed the trip again meanwhile, the store keeps that newer state for the next write instead of being
    /// reset to what was just read.
    func read() async throws {
        // What was typed a moment ago is written first, so reading does not take it back.
        if pending != nil { await flush() }
        try await readInTurn(after: nil)
    }

    private var reading: Task<Void, Error>?

    /// Reads run one after another: of two at once, the older state could otherwise come last and stay.
    private func readInTurn(after wrote: Trip?) async throws {
        let previous = reading
        let next = Task { @MainActor in
            _ = try? await previous?.value
            try await self.readNow(after: wrote)
        }
        reading = next
        try await next.value
    }

    private func readNow(after wrote: Trip?) async throws {
        let snapshot = try await session.snapshot()
        let agenda = try await session.agenda()
        // Whatever changed the trip, also a document or a place, becomes a version later.
        if !revision.isEmpty, revision != snapshot.revision { noteChange() }
        // New plans and places have weather too; what TravelRepo has already fetched it does not fetch again.
        if revision != snapshot.revision, Online.weatherOn, !Online.demo {
            weatherUpdate?.cancel()
            weatherUpdate = Task { try? await Task.sleep(for: .seconds(4)); if !Task.isCancelled { await self.updateWeather() } }
        }
        revision = snapshot.revision
        diagnostics = snapshot.diagnostics
        resources = Dictionary(snapshot.resources.map { ($0.path, $0) }, uniquingKeysWith: { a, _ in a })
        entities = [:]
        var travel: [String: JSONValue] = [:]
        for file in snapshot.entities {
            guard let data = file.data.object else { continue }
            if file.path == "travel.yaml" { travel = data } else if let id = data["id"]?.string { entities[id] = data }
        }
        self.travel = travel
        let known = lastRead
        func of(_ type: String) -> [[String: JSONValue]] { entities.values.filter { $0["type"]?.string == type } }
        func uuid(_ value: JSONValue?) -> UUID? { value?.string.flatMap(UUID.init(uuidString:)) }

        // Kinds, days, nights and the order of plans come from TravelRepo's agenda.
        var kinds: [String: Kind] = [:]; var agendaDays: [String: DateComponents] = [:]; var order: [String: Int] = [:]
        func collect(_ entry: Agenda.Entry) {
            kinds[entry.id] = Self.kind(entry.kind)
            if order[entry.id] == nil { order[entry.id] = order.count }
            if entry.role == "item" || entry.role == "check_in", let day = Self.dayComponents(entry.day) { agendaDays[entry.id] = agendaDays[entry.id] ?? day }
            entry.steps.forEach(collect)
        }
        agenda.days.flatMap(\.entries).forEach(collect); agenda.unscheduled.forEach(collect)
        let iso = ISO8601DateFormatter()
        Trip.agenda[tripID] = agenda.days.reduce(into: [:]) { result, day in
            guard let date = Self.dayComponents(day.date) else { return }
            result[date] = day.entries.compactMap { e in UUID(uuidString: e.id).map { DayEntry(item: $0, role: e.role, start: e.start.flatMap(iso.date(from:)), end: e.end.flatMap(iso.date(from:)), steps: e.steps.compactMap { UUID(uuidString: $0.id) }) } }
        }
        Trip.nightNumbers[tripID] = agenda.days.reduce(into: [:]) { result, day in
            if let date = Self.dayComponents(day.date), let night = day.nights.first { result[date] = (night.night, night.nights) }
        }
        let nights = agenda.days.reduce(into: [DateComponents: String]()) { result, day in
            if let date = Self.dayComponents(day.date), let night = day.nights.first { result[date] = night.stay }
        }

        let zone = travel["default_timezone"]?.string ?? known.zone
        var trip = Trip.empty(id: tripID)
        trip.title = travel["title"]?.string ?? ""
        trip.zone = zone
        trip.currency = travel["currency"]?.string ?? Self.defaultCurrency(travel, entities: entities)
        trip.variant = travel["variant"]?["title"]?.string ?? ""
        trip.colorHue = Self.hue(tripID)
        let first = agenda.days.first.flatMap { Self.dayComponents($0.date) }, last = agenda.days.last.flatMap { Self.dayComponents($0.date) }
        trip.start = Self.dayComponents(travel["dates"]?["start"]?.string) ?? first ?? Self.today(in: zone)
        trip.end = Self.dayComponents(travel["dates"]?["end"]?.string) ?? last ?? trip.start
        let participants = (travel["participants"]?.array ?? []).compactMap(uuid)
        trip.people = of("person").compactMap { data in
            guard let id = uuid(data["id"]) else { return nil }
            return Person(id: id, name: data["display_name"]?.string ?? "", isOrganizer: (data["roles"]?.array ?? []).contains { $0.string == "organizer" })
        }.sorted { a, b in (participants.firstIndex(of: a.id) ?? .max, a.name) < (participants.firstIndex(of: b.id) ?? .max, b.name) }
        trip.places = of("place").compactMap { data in
            guard let id = uuid(data["id"]) else { return nil }
            return Place(id: id, name: data["name"]?.string ?? "", latitude: data["location"]?["latitude"]?.number ?? 0, longitude: data["location"]?["longitude"]?.number ?? 0,
                         zone: data["timezone"]?.string ?? zone, address: data["address"]?["formatted"]?.string ?? "",
                         localName: data["local_name"]?.string, localAddress: data["address"]?["local"]?.string)
        }.sorted { $0.name.localizedStandardCompare($1.name) == .orderedAscending }
        var parents: [UUID: UUID] = [:]
        for data in of("schedule_item") {
            for child in data["children"]?.array ?? [] { if let c = uuid(child), let p = uuid(data["id"]) { parents[c] = p } }
        }
        let comments = of("comment").compactMap { data -> (UUID, Comment)? in
            guard let id = uuid(data["id"]), let target = uuid(data["target"]?["id"] ?? data["target"]) else { return nil }
            let date = ISO8601DateFormatter().date(from: data["created_at"]?.string ?? "") ?? .distantPast
            return (target, Comment(id: id, author: uuid(data["author"]) ?? id, body: data["body"]?.string ?? "", date: date))
        }
        var notesToRead: [(UUID, String)] = []
        trip.items = of("schedule_item").compactMap { data in
            guard let id = uuid(data["id"]) else { return nil }
            let old = known.item(id)
            var item = PlanItem(id: id, title: data["title"]?.string ?? "", status: Self.status(data["status"]?.string), kind: kinds[Self.key(id)] ?? old?.kind ?? .activity,
                                when: Self.when(data["time"], zone: zone) ?? .unscheduled(minutes: data["duration"]?.number.map { Int($0) }))
            item.parent = parents[id]
            item.place = uuid(data["default_place"])
            item.participants = (data["participants"]?.array ?? data["participants"]?["values"]?.array ?? []).compactMap(uuid)
            if data["participants"]?["inherit"]?.bool == true, let parent = parents[id], let p = entities[Self.key(parent)] {
                item.participants = (p["participants"]?.array ?? p["participants"]?["values"]?.array ?? []).compactMap(uuid)
            }
            let app = data["extensions"]?["jourfold"]
            if let transport = data["components"]?["transport"] {
                item.transport = Transport(mode: Self.mode(transport["type"]?.string), from: uuid(transport["departure"]?["place"]) ?? id, to: uuid(transport["arrival"]?["place"]) ?? id,
                                           carrier: transport["carrier"]?.string, number: transport["flight_number"]?.string ?? transport["number"]?.string, seat: app?["seat"]?.string)
            }
            if let a = data["components"]?["accommodation"], a.object != nil {
                // A stay keeps its place, or the plan's; its nights are those of check-in and check-out.
                item.stay = Stay(place: uuid(a["place"]) ?? item.place ?? id, room: app?["room"]?.string ?? (a["rooms"]?.array?.compactMap(\.string).joined(separator: ", ")).flatMap { $0.isEmpty ? nil : $0 })
                if a["check_in"] != nil, let span = Self.when(["precision": "exact", "start": a["check_in"] ?? .null, "end": a["check_out"] ?? a["check_in"] ?? .null]) { item.when = span }
            }
            item.booking = uuid(data["components"]?["booking"]?["ref"]) ?? uuid(data["components"]?["accommodation"]?["booking"])
            for part in data["content"]?.array ?? [] {
                switch part["type"]?.string {
                case "markdown", "text": if let file = part["file"]?.string { notesToRead.append((id, file)) }
                case "link": if let url = part["url"]?.string { item.attachments.append(Attachment(id: Self.linkID(id, url), name: url.replacingOccurrences(of: "https://", with: ""), kind: .link)) }
                default:
                    if let document = uuid(part["document"] ?? part["entity"]), let doc = entities[Self.key(document)], doc["type"]?.string == "document" {
                        item.attachments.append(Attachment(id: document, name: doc["name"]?.string ?? "", kind: Self.attachmentKind(doc["media_type"]?.string)))
                    } else if let reference = uuid(part["entity"]), let note = entities[Self.key(reference)] {
                        item.attachments.append(Attachment(id: reference, name: note["title"]?.string ?? "", kind: .note))
                    }
                }
            }
            item.comments = comments.filter { $0.0 == id }.map(\.1).sorted { $0.date < $1.date }
            return item
        }
        for (id, file) in notesToRead {
            if let text = try? await session.readResource(file), let i = trip.items.firstIndex(where: { $0.id == id }) {
                trip.items[i].notes = trip.items[i].notes.isEmpty ? String(decoding: text, as: UTF8.self) : trip.items[i].notes + "\n\n" + String(decoding: text, as: UTF8.self)
            }
        }
        trip.items.sort { (order[Self.key($0.id)] ?? .max, $0.when.start ?? .distantFuture, $0.title) < (order[Self.key($1.id)] ?? .max, $1.when.start ?? .distantFuture, $1.title) }
        trip.bookings = of("booking").compactMap { Self.booking($0, zone: zone) }.sorted { $0.title.localizedStandardCompare($1.title) == .orderedAscending }
        trip.expenses = of("expense").compactMap { Self.expense($0, currency: trip.currency, zone: zone) }.sorted { ($0.date ?? .distantFuture, $0.title) < ($1.date ?? .distantFuture, $1.title) }
        if let costs = try? await session.costs(currency: trip.currency) {
            CostsView.costs[tripID] = costs
            CostsView.bridged[tripID] = costs.settleUp.compactMap { t in
                guard let from = UUID(uuidString: t.from), let to = UUID(uuidString: t.to) else { return nil }
                var amount = t.amount, rounded = Decimal(); NSDecimalRound(&rounded, &amount, Fmt.minorUnits(t.currency), .plain)
                return CostsView.Balance(from: from, to: to, amount: rounded, currency: t.currency)
            }
        }
        // A task without a due date is never due; it must not look overdue.
        let fallbackDue = Date.distantFuture
        trip.tasks = of("task").filter { $0["components"]?["packing"] == nil }.compactMap { Self.task($0, people: trip.people, zone: zone, fallback: fallbackDue) }
            .sorted { ($0.due, $0.title) < ($1.due, $1.title) }
        trip.budgets = of("budget").compactMap { Self.budget($0, currency: trip.currency) }
        if entities[Self.notesID] != nil, let text = try? await session.readResource(Self.notesFile) { trip.notes = String(decoding: text, as: UTF8.self) }
        trip.packing = try await session.packingLists().map { list in
            PackingList(id: UUID(uuidString: list.id) ?? UUID.v7(), title: list.title, items: list.items.map { item in
                PackingItem(id: UUID(uuidString: item.id) ?? UUID.v7(), title: item.title, group: item.group ?? "", quantity: item.quantity,
                            people: item.assignees.compactMap(UUID.init(uuidString:)), everyone: item.everyone, packedBy: Set(item.packedBy.compactMap(UUID.init(uuidString:))))
            })
        }
        let versions = (try? await session.call("list_versions", ["limit": 30])["versions"]?.array) ?? []
        trip.versions = versions.map { v in
            Version(id: Self.stableID(v["commit"]?.string ?? v["id"]?.string ?? UUID.v7().uuidString), title: v["message"]?.string ?? "", author: v["author"]?.string ?? "",
                    date: ISO8601DateFormatter().date(from: v["date"]?.string ?? "") ?? .distantPast, commit: v["commit"]?.string ?? v["id"]?.string)
        }
        remotes = (try? await session.remotes()) ?? remotes
        store.me[tripID] = await me(login: store.account?.login)?.person
        lastRead = trip
        store.travelRepoReads += 1
        Trip.travelRepoDays[tripID] = Dictionary(uniqueKeysWithValues: trip.items.compactMap { item in agendaDays[Self.key(item.id)].map { (item.id, ($0, item.when)) } })
        Trip.travelRepoNights[tripID] = nights.compactMapValues { id in UUID(uuidString: id).flatMap { stay in trip.item(stay).map { (stay, $0.when) } } }
        if let i = store.trips.firstIndex(where: { $0.id == tripID }) {
            if let wrote, !Self.unchanged(store.trips[i], since: wrote) {
                // What came from elsewhere meanwhile is not in the store yet, and missing there must not mean deleted.
                lastRead.items.removeAll { wrote.item($0.id) == nil }
                lastRead.people.removeAll { wrote.person($0.id) == nil }
                lastRead.places.removeAll { wrote.place($0.id) == nil }
                lastRead.bookings.removeAll { wrote.booking($0.id) == nil }
                lastRead.expenses.removeAll { e in !wrote.expenses.contains { $0.id == e.id } }
                lastRead.tasks.removeAll { t in !wrote.tasks.contains { $0.id == t.id } }
                lastRead.budgets.removeAll { b in !wrote.budgets.contains { $0.id == b.id } }
                return
            }
            store.trips[i] = trip
        } else { store.trips.append(trip) }
        scheduleCalendar()
    }

    private static var calendarUpdate: Task<Void, Never>?
    /// Writes the calendars a moment after the trips were last read, so a burst of changes writes once.
    private func scheduleCalendar() {
        guard SystemCalendar.on else { return }
        Self.calendarUpdate?.cancel()
        let store = store
        Self.calendarUpdate = Task { @MainActor in
            try? await Task.sleep(for: .seconds(2))
            guard !Task.isCancelled else { return }
            await SystemCalendar.update(store.trips)
        }
    }

    /// Reads again when TravelRepo's revision moved, for example after a sync or a change from another app.
    func refreshIfChanged() async {
        await flush()
        guard let now = try? await session.revision(), now != revision else { return }
        try? await read()
    }

    /// The trip's currency when travel.yaml names none: the most used currency of its costs, else euros.
    private static func defaultCurrency(_ travel: [String: JSONValue], entities: [String: [String: JSONValue]]) -> String {
        let counts = entities.values.compactMap { $0["type"]?.string == "expense" ? $0["amount"]?["currency"]?.string : nil }.reduce(into: [String: Int]()) { $0[$1, default: 0] += 1 }
        return counts.max { $0.value < $1.value }?.key ?? "EUR"
    }

    /// A colour for the trip's badge that stays the same on every start.
    static func hue(_ id: UUID) -> Double {
        let bytes = withUnsafeBytes(of: id.uuid) { Array($0) }
        return Double(bytes.suffix(4).reduce(0) { ($0 << 8) | Int($1) } % 360) / 360
    }

    static func today(in zone: String) -> DateComponents {
        var c = Calendar(identifier: .gregorian); c.timeZone = TimeZone(identifier: zone) ?? .current
        return c.dateComponents([.year, .month, .day], from: Date())
    }

    static func notesPath(_ item: UUID) -> String { "documents/\(key(item))-notes.md" }

    static func isNote(_ part: JSONValue) -> Bool { ["markdown", "text"].contains(part["type"]?.string ?? "") && part["file"]?.string != nil }

    /// The item's content with its notes file first, keeping links and documents.
    static func contentWithNotes(_ item: UUID, into content: JSONValue?) -> JSONValue {
        // Text that stands in the content itself, without a file, stays.
        let rest = (content?.array ?? []).filter { !isNote($0) }
        return .array([["type": "markdown", "file": .string(notesPath(item))]] + rest)
    }

    static func attachmentKind(_ mediaType: String?) -> Attachment.Kind {
        switch mediaType ?? "" {
        case "application/vnd.apple.pkpass": .walletPass
        case "application/pdf": .pdf
        case let t where t.hasPrefix("image/"): .image
        case "message/rfc822": .mail
        default: .file
        }
    }

    /// An id for a thing TravelRepo names by text only, the same on every read.
    static func stableID(_ text: String) -> UUID {
        var bytes = [UInt8](repeating: 0, count: 16)
        for (i, b) in text.utf8.enumerated() { bytes[i % 16] = bytes[i % 16] &* 31 &+ b }
        return UUID(uuid: (bytes[0], bytes[1], bytes[2], bytes[3], bytes[4], bytes[5], bytes[6], bytes[7], bytes[8], bytes[9], bytes[10], bytes[11], bytes[12], bytes[13], bytes[14], bytes[15]))
    }
    static func linkID(_ item: UUID, _ url: String) -> UUID { stableID(item.uuidString + url) }

    private var writing: Task<Void, Never>?
    private var pending: Task<Void, Never>?

    /// Writes the store's trip a moment after the last change, so typing in a field makes one write, not one per key.
    func scheduleWrite() {
        pending?.cancel()
        pending = Task { @MainActor in
            try? await Task.sleep(for: .milliseconds(350))
            guard !Task.isCancelled, let trip = self.store.trips.first(where: { $0.id == self.tripID }) else { return }
            await self.write(trip)
        }
    }

    /// Writes what is still waiting, for example before a version or a sync.
    func flush() async {
        guard let waiting = pending else { await writing?.value; return }
        waiting.cancel(); pending = nil
        if let trip = store.trips.first(where: { $0.id == tripID }) { await write(trip) }
    }

    /// Writes what the app changed in the trip since the last read, as one transaction, and reads it again. Writes
    /// run one after another and each starts from the store's current trip, so quick edits do not trip over each
    /// other's revision; a change made elsewhere in the meantime still fails with repository.changed and is read again.
    func write(_ trip: Trip) async {
        let previous = writing
        let next = Task { @MainActor in
            await previous?.value
            await self.writeNow(self.store.trips.first { $0.id == self.tripID } ?? trip)
        }
        writing = next
        await next.value
    }

    /// An entity with only what the app changed: where the app's own writing of the old and the new state agree, the
    /// stored value stays as it is. The app's model is simpler than TravelRepo's (one payer, one place per end, its own
    /// cost groups), so writing a whole entity back would flatten what it cannot show.
    static func changed(_ stored: [String: JSONValue], _ before: [String: JSONValue], _ after: [String: JSONValue]) -> [String: JSONValue] {
        var out = stored
        for key in Set(before.keys).union(after.keys) where before[key] != after[key] {
            if case .object(let s)? = stored[key], case .object(let o)? = before[key], case .object(let n)? = after[key] { out[key] = .object(changed(s, o, n)) }
            else { out[key] = after[key] }
        }
        return out
    }

    private func changes<T>(of id: UUID, from before: T?, to after: T, write: (T, [String: JSONValue]?) -> [String: JSONValue]) -> [String: JSONValue] {
        guard let before, let stored = entities[Self.key(id)] else { return write(after, entities[Self.key(id)]) }
        return Self.changed(stored, write(before, stored), write(after, stored))
    }

    private func writeNow(_ trip: Trip) async {
        var edits: [String: [String: JSONValue]?] = [:]
        var deleted: [String] = []
        var resources: [String: Data?] = [:]
        if trip.title != lastRead.title || trip.start != lastRead.start || trip.end != lastRead.end || trip.zone != lastRead.zone || trip.currency != lastRead.currency
            || trip.people.map(\.id) != lastRead.people.map(\.id) {
            edits[Self.key(tripID)] = Self.changed(travel, Self.tripData(lastRead, into: travel), Self.tripData(trip, into: travel))
        }
        for person in trip.people where lastRead.person(person.id) != person { edits[Self.key(person.id)] = changes(of: person.id, from: lastRead.person(person.id), to: person) { Self.personData($0, into: $1) } }
        for person in lastRead.people where trip.person(person.id) == nil { deleted.append(Self.key(person.id)) }
        for place in trip.places where lastRead.place(place.id) != place { edits[Self.key(place.id)] = changes(of: place.id, from: lastRead.place(place.id), to: place) { Self.placeData($0, into: $1) } }
        for place in lastRead.places where trip.place(place.id) == nil { deleted.append(Self.key(place.id)) }
        // A group is written when its steps changed, because the group lists them.
        let changedParents = Set(trip.items.map(\.id).filter { id in trip.children(of: id).map(\.id) != lastRead.children(of: id).map(\.id) })
        for item in trip.items where lastRead.item(item.id).map({ !Self.same($0, item) }) ?? true || changedParents.contains(item.id) {
            let before = lastRead.item(item.id)
            let stored = entities[Self.key(item.id)]
            var data = Self.itemData(item, in: trip, into: stored)
            if let before, let stored { data = Self.changed(stored, Self.itemData(before, in: lastRead, into: stored), data) }
            if item.notes != (before?.notes ?? "") {
                let file = (data["content"]?.array ?? []).first(where: Self.isNote)?["file"]?.string ?? Self.notesPath(item.id)
                // Several notes are shown and edited as one, so the others go into the first.
                for other in (data["content"]?.array ?? []).filter(Self.isNote).compactMap({ $0["file"]?.string }) where other != file {
                    data["content"] = (data["content"]?.array).map { .array($0.filter { $0["file"]?.string != other }) }
                    if !entities.contains(where: { $0.key != Self.key(item.id) && ($0.value["content"]?.array ?? []).contains { $0["file"]?.string == other } }) { resources[other] = .some(nil) }
                }
                if item.notes.isEmpty {
                    data["content"] = .array((data["content"]?.array ?? []).filter { $0["file"]?.string != file }); resources[file] = .some(nil)
                } else {
                    resources[file] = Data(item.notes.utf8)
                    if !(data["content"]?.array ?? []).contains(where: { $0["file"]?.string == file }) { data["content"] = Self.contentWithNotes(item.id, into: data["content"]) }
                }
            }
            // Links the app removed; documents are removed with the document itself.
            let links = Set(item.attachments.filter { $0.kind == .link }.map(\.id))
            data["content"] = (data["content"]?.array).map { parts in .array(parts.filter { $0["type"]?.string != "link" || links.contains(Self.linkID(item.id, $0["url"]?.string ?? "")) }) }
            if data["content"]?.array?.isEmpty == true { data["content"] = nil }
            edits[Self.key(item.id)] = data
            for comment in item.comments where !(before?.comments.contains { $0.id == comment.id } ?? false) {
                edits[Self.key(comment.id)] = ["id": .string(Self.key(comment.id)), "type": "comment", "target": ["type": "schedule_item", "id": .string(Self.key(item.id))], "author": .string(Self.key(comment.author)),
                                               "created_at": .string(ISO8601DateFormatter().string(from: comment.date)), "body": .string(comment.body), "extensions": [:]]
            }
        }
        for item in lastRead.items where trip.item(item.id) == nil { deleted.append(Self.key(item.id)) }
        for booking in trip.bookings where lastRead.booking(booking.id) != booking { edits[Self.key(booking.id)] = changes(of: booking.id, from: lastRead.booking(booking.id), to: booking) { Self.bookingData($0, zone: trip.zone, into: $1) } }
        for booking in lastRead.bookings where trip.booking(booking.id) == nil { deleted.append(Self.key(booking.id)) }
        for expense in trip.expenses where lastRead.expenses.first(where: { $0.id == expense.id }) != expense {
            edits[Self.key(expense.id)] = changes(of: expense.id, from: lastRead.expenses.first { $0.id == expense.id }, to: expense) { Self.expenseData($0, in: trip, into: $1) }
        }
        for expense in lastRead.expenses where !trip.expenses.contains(where: { $0.id == expense.id }) { deleted.append(Self.key(expense.id)) }
        for task in trip.tasks where lastRead.tasks.first(where: { $0.id == task.id }) != task { edits[Self.key(task.id)] = changes(of: task.id, from: lastRead.tasks.first { $0.id == task.id }, to: task) { Self.taskData($0, zone: trip.zone, into: $1) } }
        for task in lastRead.tasks where !trip.tasks.contains(where: { $0.id == task.id }) { deleted.append(Self.key(task.id)) }
        for budget in trip.budgets where lastRead.budgets.first(where: { $0.id == budget.id }) != budget { edits[Self.key(budget.id)] = changes(of: budget.id, from: lastRead.budgets.first { $0.id == budget.id }, to: budget) { Self.budgetData($0, into: $1) } }
        for budget in lastRead.budgets where !trip.budgets.contains(where: { $0.id == budget.id }) { deleted.append(Self.key(budget.id)) }
        if trip.notes != lastRead.notes {
            resources[Self.notesFile] = Data(trip.notes.utf8)
            if entities[Self.notesID] == nil { edits[Self.notesID] = Self.notesData(into: nil) }
        }
        let packingChanged = trip.packing != lastRead.packing
        guard !edits.isEmpty || !resources.isEmpty || packingChanged || !deleted.isEmpty else { return }
        do {
            if !edits.isEmpty || !resources.isEmpty { try await session.apply(revision: revision, edits: edits, resources: resources) }
            // Deleting goes through TravelRepo, which also removes references to what is deleted (a booking's plans,
            // a comment's target), so the trip stays valid.
            if !deleted.isEmpty { try await session.call("delete_entities", ["ids": .array(deleted.map { .string($0) })]) }
            if packingChanged { try await Self.writePacking(trip.packing, since: lastRead.packing, session: session) }
            try await readInTurn(after: trip)
            noteChange()
        } catch {
            Self.log.error("write failed: \(String(describing: error))")
            store.problem = Self.message(error)
            try? await readInTurn(after: nil)
        }
    }

    enum AppError: LocalizedError { case message(String); var errorDescription: String? { if case .message(let m) = self { m } else { nil } } }

    /// What to tell the person when TravelRepo refused something.
    static func message(_ error: Error) -> String {
        if let e = error as? TravelRepoError {
            if e.code == "time.gap" { return String(localized: "That day, the clocks skip this time (daylight saving). Choose another time.") }
            if e.code == "time.ambiguous" { return String(localized: "That day, this time happens twice (the clocks go back). Choose another time, or say which one in the plan's details.") }
            if e.code == "repository.changed" { return String(localized: "The trip changed elsewhere in the meantime. It shows the latest state now.") }
            if e.code.hasPrefix("schema.") || e.message.contains("schema.invalid") || e.message.hasPrefix("{") { return String(localized: "TravelRepo did not accept the change, because the trip would no longer be valid. Nothing was saved.") + " (\(e.code))" }
            return e.message
        }
        return error.localizedDescription
    }

    // MARK: Who the user is

    /// The user in this trip and the people another account already uses: by the GitHub login when signed in,
    /// else by the e-mail address of this phone's versions. The demo signs in as the made-up alex-travels.
    struct Me: Equatable { var person: UUID?; var taken: Set<UUID> }

    static let demoLogin = "alex-travels"
    private static var demo: Bool { DebugTools.flag("demo") || DebugTools.flag("selftest") }

    /// Which person the user is here is this phone's own choice (as in the .NET app); TravelRepo's `me` only suggests
    /// one. `taken` are people with another login or e-mail, which may still be the user's: a note, never a lock.
    private var choiceKey: String { "me-\(tripID.uuidString)" }

    func me(login: String?) async -> Me? {
        let account = login ?? (Self.demo ? Self.demoLogin : nil)
        guard let found = try? await session.me(githubLogin: account, gitEmail: TripLibrary.ownEmail) else { return nil }
        var person = found.person.flatMap(UUID.init(uuidString:))
        let choice = UserDefaults.standard.string(forKey: choiceKey)
        if choice == "none" { person = nil }
        else if let chosen = choice.flatMap(UUID.init(uuidString:)), entities[Self.key(chosen)] != nil { person = chosen }
        return Me(person: person, taken: Set(found.travelers.filter(\.claimedBySomeoneElse).compactMap { UUID(uuidString: $0.id) }))
    }

    /// "That's me": remembered on this phone. The user's login and e-mail go onto the person only when it has no
    /// others, so nobody's identity is mixed up; new versions are signed with the e-mail the trip knows for them.
    func claim(_ person: UUID, login: String?, email given: String? = nil) async -> Me? {
        let email = given ?? TripLibrary.ownEmail
        let account = login ?? (Self.demo ? Self.demoLogin : nil)
        let taken = await me(login: login)?.taken.contains(person) ?? true
        UserDefaults.standard.set(person.uuidString, forKey: choiceKey)
        // Holds at once, also when storing it in the trip fails below.
        store.me[tripID] = person
        let name = entities[Self.key(person)]?["display_name"]?.string
        do {
            if !taken, account != nil || email != nil {
                try await session.claimPerson(Self.key(person), githubLogin: account, gitName: name, gitEmail: email)
            }
            try await read()
            let known = entities[Self.key(person)]?["identities"]?["git"]?.array?.first?["email"]?.string
            if let email = given ?? known ?? TripLibrary.ownEmail, let name { try await session.setIdentity(name: name, email: email) }
        } catch { store.problem = Self.message(error) }
        return await me(login: login)
    }

    /// For `-mecheck`: what stands in the way of choosing oneself, without anything of the trip's content.
    func meCheck(_ login: String?) async -> String {
        let choice = UserDefaults.standard.string(forKey: choiceKey)
        var parts = ["people \(lastRead.people.count)", "choice \(choice == nil ? "-" : choice == "none" ? "none" : "person") known \(choice.map { entities[$0.lowercased()] != nil } ?? false)",
                     "store.me \(store.me[tripID] != nil)", "seen \(WelcomeSheet.seen(tripID))", "shared \(isShared)"]
        do {
            let m = try await session.me(githubLogin: login, gitEmail: TripLibrary.ownEmail)
            parts.append("me person \(m.person != nil) travelers \(m.travelers.count) taken \(m.travelers.filter(\.claimedBySomeoneElse).count)")
        } catch { parts.append("me error \(String(describing: error).prefix(160))") }
        parts.append("diagnostics \(Dictionary(grouping: diagnostics, by: \.code).mapValues(\.count))")
        return parts.joined(separator: ", ")
    }

    /// Finds out again who the user is, when the account changed.
    func recognise(_ login: String?) async {
        if let me = await me(login: login) { store.me[tripID] = me.person }
    }

    /// The user is not one of the people in this trip.
    func claimNobody() async -> Me? {
        UserDefaults.standard.set("none", forKey: choiceKey)
        store.me[tripID] = nil
        return await me(login: store.account?.login)
    }

    // MARK: Search

    /// TravelRepo's search (TripSearch): every word as the start of a word, one typo allowed in longer words, accents
    /// ignored, best first. Plans and places, as the plan's search field shows them; nil when this trip is not bridged.
    func search(_ query: String, trip: UUID) async -> (query: String, items: [UUID], places: [UUID])? {
        guard trip == tripID, !query.trimmingCharacters(in: .whitespaces).isEmpty, let hits = try? await session.find(query, kinds: ["plan", "place"]) else { return nil }
        return (query, hits.filter { $0.kind == "plan" }.compactMap { UUID(uuidString: $0.id) }, hits.filter { $0.kind == "place" }.compactMap { UUID(uuidString: $0.id) })
    }

    // MARK: Sync and merge

    /// The conflicts of the merge that `sync` found, by the part of the merge sheet that shows them.
    private var conflicts: [UUID: MergePreview.Conflict] = [:]

    /// "Sam's phone": a clone of this trip in the app's container, where Sam moved the tea ceremony half an hour later
    /// and invited everyone, while Alex moved it here half an hour earlier. Stands in for a GitHub remote, so syncing and merging
    /// work offline in the simulator through TravelRepo's real sync and merge.
    private func prepareDemoRemote(_ trip: Trip) async throws -> URL {
        let sam = folder.deletingLastPathComponent().appendingPathComponent("sam-phone")
        if FileManager.default.fileExists(atPath: sam.appendingPathComponent("travel.yaml").path) { return sam }
        try await TravelRepo.call("clone", ["url": .string(folder.path), "destination": .string(sam.path)], as: JSONValue.self)
        let phone = try await TravelRepoSession.open(root: sam, recoveryRoot: folder.deletingLastPathComponent().appendingPathComponent("recovery-sam"))
        try await phone.setIdentity(name: "Sam", email: "sam@example.invalid")
        let snapshot = try await phone.snapshot()
        guard let tea = trip.items.first(where: { $0.title == "Tea ceremony" }),
              case .object(var data)? = snapshot.entities.first(where: { $0.data["id"]?.string == Self.key(tea.id) })?.data,
              case .exact(let start, let zone, let end, let endZone) = tea.when else { return sam }
        data["time"] = Self.time(.exact(start: start.addingTimeInterval(1800), startZone: zone, end: end.addingTimeInterval(1800), endZone: endZone))
        data["participants"] = .array(trip.people.map { .string(Self.key($0.id)) })
        try await phone.apply(revision: snapshot.revision, edits: [Self.key(tea.id): data])
        try await phone.call("create_version", ["message": "Tea ceremony half an hour later, for both of us"])
        // Here, at the same time: Alex moves it half an hour earlier.
        store.update(tripID, "Tea ceremony") { t in
            if let i = t.items.firstIndex(where: { $0.id == tea.id }) { t.items[i].when = .exact(start: start.addingTimeInterval(-1800), startZone: zone, end: end.addingTimeInterval(-1800), endZone: endZone) }
        }
        await write(store.trips.first { $0.id == tripID } ?? trip)
        try await session.call("create_version", ["message": "Tea ceremony half an hour earlier"])
        return sam
    }

    /// Syncs through TravelRepo: with GitHub (`origin`) when the trip is shared there; in the demo (`-demo YES`) with
    /// "Sam's phone". When both sides changed the same thing, returns the conflicts for the merge sheet.
    func sync(_ trip: Trip, quiet: Bool = false) async -> MergeConflict? {
        await flush()
        do {
            let remotes = try await session.remotes()
            let remote: String, from: String
            if remotes.contains(where: { $0.name == "origin" }) { remote = "origin"; from = "GitHub" }
            else if DebugTools.flag("demo"), trip.people.contains(where: { $0.name == "Sam" }) {
                let sam = try await prepareDemoRemote(trip)
                if !remotes.contains(where: { $0.name == "sam" }) { try await session.call("add_remote", ["name": "sam", "url": .string(sam.path)]) }
                remote = "sam"; from = "Sam"
            } else {
                if !quiet { store.problem = String(localized: "This trip is only on this phone. Share it on GitHub to sync it with others.") }
                return nil
            }
            // Only versions are sent: open changes become one first, or the trip would only look synced.
            guard await saveChanges() else { store.syncProblem[tripID] = versionProblem; return nil }
            let state = try await session.sync(remote: remote)
            Self.log.info("sync with \(remote, privacy: .public): \(state, privacy: .public)")
            if state == "local_changes" { store.syncProblem[tripID] = String(localized: "Something was changed while syncing. It is sent with the next sync."); try await read(); return nil }
            store.syncProblem[tripID] = nil
            guard state == "diverged" else { try await read(); store.synced(tripID); return nil }
            let branch = "\(remote)/\(try await session.syncStatus(remote: remote).branch)"
            let preview = try await session.mergePreview(branch: branch)
            if preview.conflicts.isEmpty {
                try await session.mergeApply(branch: branch, resolutions: [:], message: String(localized: "Take over changes from \(from)"))
                try await read()
                try await send(remote)
                try await read(); store.synced(tripID); return nil
            }
            let current = store.trips.first { $0.id == tripID } ?? trip
            conflicts = [:]
            let parts = preview.conflicts.map { conflict -> MergeConflict.Part in
                let id = UUID(uuidString: conflict.entity)
                let title = current.item(id)?.title ?? current.person(id)?.name ?? current.place(id)?.name ?? current.booking(id)?.title
                    ?? current.expenses.first { $0.id == id }?.title ?? (entities[conflict.entity]?["title"]?.string) ?? conflict.entity
                let part = MergeConflict.Part(title: title, field: Self.field(conflict.path), mine: Self.show(conflict.current, in: current), theirs: Self.show(conflict.incoming, in: current))
                conflicts[part.id] = conflict
                return part
            }
            syncRemote = remote
            return MergeConflict(tripID: tripID, from: from, parts: parts, branch: branch)
        } catch {
            Self.log.error("sync failed: \(String(describing: error))")
            store.syncProblem[tripID] = Self.message(error)
            if !quiet { store.problem = Self.message(error) }
            return nil
        }
    }

    private var syncRemote = "origin"

    /// Sends a merge that was just made; anything but a finished sync is an error, so it is never taken for done.
    private func send(_ remote: String) async throws {
        let state = try await session.sync(remote: remote)
        guard state == "synced" else { throw AppError.message(String(localized: "The changes are combined on this phone, but not sent yet. Sync again.")) }
    }

    /// Why open changes could not become a version, such as errors in the trip; nil when the last try worked.
    private(set) var versionProblem: String?
    private var saving: Task<Bool, Never>?
    private var quietTimer: Task<Void, Never>?

    /// Turns open changes into a version, as the .NET app does. False when they could not become one.
    @discardableResult
    func saveChanges() async -> Bool {
        quietTimer?.cancel(); quietTimer = nil
        if let saving { return await saving.value }
        let task = Task { @MainActor () -> Bool in
            await flush()
            do {
                guard try await session.syncStatus(remote: nil).hasLocalChanges else { versionProblem = nil; return true }
                try await session.call("create_version", ["message": .string(String(localized: "Changes on the phone"))])
                versionProblem = nil
                store.changes[tripID] = []
                try await read()
                return true
            } catch { versionProblem = Self.message(error); return false }
        }
        saving = task
        let done = await task.value
        saving = nil
        return done
    }

    /// Once the trip has been quiet for a moment, its changes become a version and go to GitHub.
    func noteChange() {
        quietTimer?.cancel()
        guard !DebugTools.flag("selftest") else { return }
        quietTimer = Task { @MainActor in
            try? await Task.sleep(for: .seconds(120))
            guard !Task.isCancelled else { return }
            quietTimer = nil
            await store.autoSave(tripID)
        }
    }

    /// Merges with the choices from the merge sheet: mine keeps this phone's value, theirs takes the other one, both
    /// keeps this one and adds the other as a copy. Then sends the result.
    func merge(_ merge: MergeConflict, choices: [MergeConflict.Choice]) async {
        let branch = merge.branch
        var resolutions: [String: JSONValue] = [:]; var keepBoth: [String] = []
        for (part, choice) in zip(merge.parts, choices) {
            guard let conflict = conflicts[part.id] else { continue }
            switch choice {
            case .mine: resolutions[conflict.key] = conflict.current ?? .null
            case .theirs: resolutions[conflict.key] = conflict.incoming ?? .null
            case .both: keepBoth.append(conflict.entity)
            }
        }
        do {
            try await session.mergeApply(branch: branch, resolutions: resolutions, keepBoth: keepBoth, message: String(localized: "Take over changes from \(merge.from)"))
            try await read()
            try await send(syncRemote)
            try await read()
            store.synced(tripID)
        } catch { Self.log.error("merge failed: \(String(describing: error))"); store.syncProblem[tripID] = Self.message(error); store.problem = Self.message(error) }
    }

    private static func field(_ path: String) -> LocalizedStringResource {
        switch path.split(separator: "/").first.map(String.init) ?? "" {
        case "time": "Time"
        case "participants": "Who goes"
        case "title": "Title"
        case "status": "Status"
        default: LocalizedStringResource(stringLiteral: path)
        }
    }

    private static func show(_ value: JSONValue?, in trip: Trip) -> String {
        guard let value else { return "–" }
        if let people = (value.array ?? value["values"]?.array)?.compactMap({ UUID(uuidString: $0.string ?? "") }), !people.isEmpty || value.array != nil {
            return people.compactMap { trip.person($0)?.name }.joined(separator: ", ")
        }
        if let when = when(value), let start = when.start, let end = when.end {
            let f = DateFormatter(); f.timeZone = when.zone; f.dateFormat = "HH:mm"
            return f.string(from: start) + "–" + f.string(from: end)
        }
        return value.string ?? value.number.map { String($0) } ?? String(decoding: (try? JSONEncoder().encode(value)) ?? Data(), as: UTF8.self)
    }

    func createVersion(_ message: String) async -> Bool {
        await flush()
        do { try await session.call("create_version", ["message": .string(message)]); try await read(); return true }
        catch { Self.log.error("version failed: \(String(describing: error))"); store.problem = Self.message(error); return false }
    }

    // MARK: The app's model ↔ TravelRepo entities

    static func key(_ id: UUID) -> String { id.uuidString.lowercased() }

    /// Whether the app changed people, places or plans of `current` since `since`.
    private static func unchanged(_ current: Trip, since: Trip) -> Bool {
        current.people == since.people && current.places == since.places && current.bookings == since.bookings && current.expenses == since.expenses && current.packing == since.packing
            && current.tasks == since.tasks && current.budgets == since.budgets && current.notes == since.notes && current.items.count == since.items.count
            && current.title == since.title && current.start == since.start && current.end == since.end && current.zone == since.zone && current.currency == since.currency
            && zip(current.items, since.items).allSatisfy { $0.id == $1.id && same($0, $1) }
    }

    private static func same(_ a: PlanItem, _ b: PlanItem) -> Bool {
        a == b && a.participants == b.participants && a.transport == b.transport && a.stay == b.stay && a.parent == b.parent && a.booking == b.booking
            && a.comments == b.comments && a.attachments == b.attachments
    }

    static func personData(_ person: Person, into existing: [String: JSONValue]?) -> [String: JSONValue] {
        var data = existing ?? ["id": .string(key(person.id)), "type": "person", "extensions": [:]]
        data["display_name"] = .string(person.name)
        var roles = (data["roles"]?.array ?? []).filter { $0.string != "organizer" }
        if person.isOrganizer { roles.insert("organizer", at: 0) }
        data["roles"] = roles.isEmpty ? nil : .array(roles)
        return data
    }

    /// travel.yaml: title, dates, main zone, currency and the people who travel.
    static func tripData(_ trip: Trip, into existing: [String: JSONValue]) -> [String: JSONValue] {
        var data = existing
        data["title"] = .string(trip.title)
        data["dates"] = ["start": dateText(trip.start), "end": dateText(trip.end)]
        data["default_timezone"] = .string(trip.zone)
        data["currency"] = .string(trip.currency)
        data["participants"] = .array(trip.people.map { .string(key($0.id)) })
        return data
    }

    static func placeData(_ place: Place, into existing: [String: JSONValue]?) -> [String: JSONValue] {
        var data = existing ?? ["id": .string(key(place.id)), "type": "place", "extensions": [:]]
        data["name"] = .string(place.name)
        data["timezone"] = .string(place.zone)
        data["location"] = place.hasPosition ? ["latitude": .number(place.latitude), "longitude": .number(place.longitude)] : nil
        data["local_name"] = place.localName.map { .string($0) }
        var address = data["address"]?.object ?? [:]
        address["formatted"] = place.address.isEmpty ? nil : .string(place.address)
        address["local"] = place.localAddress.map { .string($0) }
        data["address"] = address.isEmpty ? nil : .object(address)
        return data
    }

    static func itemData(_ item: PlanItem, in trip: Trip, into existing: [String: JSONValue]?) -> [String: JSONValue] {
        var data = existing ?? ["id": .string(key(item.id)), "type": "schedule_item", "extensions": [:]]
        data["title"] = .string(item.title)
        data["status"] = .string(item.status == .done ? "completed" : item.status.rawValue)
        data["time"] = time(item.when)
        // TravelRepo derives the kind from the inheritable category (and from transport and accommodation components).
        if item.kind != .transport, item.kind != .accommodation, item.kind != .activity || data["category"] != nil {
            data["category"] = .string(item.kind == .freeTime ? "free_time" : item.kind.rawValue)
        }
        data["participants"] = .array(item.participants.map { .string(key($0)) })
        var components = data["components"]?.object ?? [:]
        if let transport = item.transport {
            var t = components["transport"]?.object ?? [:]
            t["type"] = .string(transport.mode == .walk ? "walking" : transport.mode.rawValue)
            t["departure"] = .object((t["departure"]?.object ?? [:]).merging(["place": .string(key(transport.from))]) { $1 })
            t["arrival"] = .object((t["arrival"]?.object ?? [:]).merging(["place": .string(key(transport.to))]) { $1 })
            if let carrier = transport.carrier { t["carrier"] = .string(carrier) }
            if transport.mode == .flight, let number = transport.number { t["flight_number"] = .string(number) }
            components["transport"] = .object(t)
        } else { components["transport"] = nil }
        if let stay = item.stay {
            components["accommodation"] = .object((components["accommodation"]?.object ?? [:]).merging(["place": .string(key(stay.place))]) { $1 })
        } else { components["accommodation"] = nil }
        if let booking = item.booking { components["booking"] = .object((components["booking"]?.object ?? [:]).merging(["ref": .string(key(booking))]) { $1 }) }
        else { components["booking"] = nil }
        data["components"] = components.isEmpty ? nil : .object(components)
        data["default_place"] = item.place.map { .string(key($0)) }
        // A seat and a room are details of this app, kept under its own extension.
        var extensions = data["extensions"]?.object ?? [:], app = extensions["jourfold"]?.object ?? [:]
        app["seat"] = item.transport?.seat.map { .string($0) }
        app["room"] = item.stay?.room.map { .string($0) }
        extensions["jourfold"] = app.isEmpty ? nil : .object(app)
        data["extensions"] = .object(extensions)
        let children = trip.children(of: item.id)
        data["children"] = children.isEmpty && data["children"] == nil ? nil : .array(children.map { .string(key($0.id)) })
        return data
    }

    // MARK: Tasks, budgets and the trip's notes

    /// The trip's notes are one note entity with a Markdown file, at a fixed id so every start finds it.
    static let notesID = "01a1fe00-0000-7000-8000-000000000001"
    static let notesFile = "documents/\(notesID).md"

    static func notesData(into existing: [String: JSONValue]?) -> [String: JSONValue] {
        var data = existing ?? ["id": .string(notesID), "type": "note", "extensions": [:]]
        data["title"] = "Trip notes"
        data["content"] = [["type": "markdown", "file": .string(notesFile)]]
        return data
    }

    static func taskData(_ task: TripTask, zone: String, into existing: [String: JSONValue]?) -> [String: JSONValue] {
        var data = existing ?? ["id": .string(key(task.id)), "type": "task", "extensions": [:]]
        data["title"] = .string(task.title)
        data["status"] = task.done ? "completed" : (data["status"]?.string == "in_progress" ? "in_progress" : "open")
        data["assigned_to"] = [.string(key(task.assignee))]
        var c = Calendar(identifier: .gregorian); c.timeZone = TimeZone(identifier: zone) ?? .current
        if task.due == .distantFuture { data["due"] = nil }
        else { data["due"] = .object((data["due"]?.object ?? [:]).merging(["date": dateText(c.dateComponents([.year, .month, .day], from: task.due))]) { $1 }) }
        data["related"] = task.related.map { [["type": "schedule_item", "id": .string(key($0))]] }
        return data
    }

    static func task(_ data: [String: JSONValue], people: [Person], zone: String, fallback: Date) -> TripTask? {
        guard let id = UUID(uuidString: data["id"]?.string ?? "") else { return nil }
        var task = TripTask(id: id, title: "", done: false, due: fallback, assignee: people.first?.id ?? id)
        task.title = data["title"]?.string ?? task.title
        task.done = data["status"]?.string == "completed"
        task.assignee = (data["assigned_to"]?.array?.first?.string).flatMap(UUID.init(uuidString:)) ?? task.assignee
        if case .allDay(let day, _)? = when(["precision": "all_day", "date": data["due"]?["date"] ?? .null, "timezone": .string(zone)]) {
            var c = Calendar(identifier: .gregorian); c.timeZone = TimeZone(identifier: zone) ?? .current
            var noon = day; noon.hour = 12; task.due = c.date(from: noon) ?? task.due
        }
        task.related = (data["related"]?.array?.first?["id"]?.string).flatMap(UUID.init(uuidString:))
        return task
    }

    static func budgetData(_ budget: Budget, into existing: [String: JSONValue]?) -> [String: JSONValue] {
        var data = existing ?? ["id": .string(key(budget.id)), "type": "budget", "extensions": [:]]
        data["title"] = .string(budget.title)
        data["amount"] = ["value": decimalText(budget.amount.value), "currency": .string(budget.amount.currency)]
        // A budget's limits say what it covers; TravelRepo no longer writes the `scope` that repeated them.
        data["scope"] = nil
        // A category of the trip's own, which this model does not know, stays.
        if existing.flatMap({ Self.budget($0, currency: budget.amount.currency) })?.category != budget.category || existing == nil {
            data["category"] = budget.category.map { category in .string(categories.first { $0.0 == category }?.1 ?? "other") }
        }
        return data
    }

    static func budget(_ data: [String: JSONValue], currency: String) -> Budget? {
        guard let id = UUID(uuidString: data["id"]?.string ?? "") else { return nil }
        var budget = Budget(id: id, title: "", amount: Money(value: 0, currency: currency))
        budget.title = data["title"]?.string ?? budget.title
        if let value = decimal(data["amount"]?["value"]), let currency = data["amount"]?["currency"]?.string { budget.amount = Money(value: value, currency: currency) }
        budget.category = data["category"]?.string.flatMap { key in categories.first { $0.1 == key }?.0 }
        return budget
    }

    /// Writes packing lists through TravelRepo's own packing commands, so the app does not need to know how TravelRepo
    /// stores them (collections of tasks). New lists and items get TravelRepo's ids, which the next read brings back.
    static func writePacking(_ lists: [PackingList], since old: [PackingList], session: TravelRepoSession) async throws {
        func ids(_ people: some Sequence<UUID>) -> JSONValue { .array(people.map { .string(key($0)) }) }
        for list in old where !lists.contains(where: { $0.id == list.id }) {
            try await session.call("delete_packing_list", ["list": .string(key(list.id))])
        }
        for list in lists {
            let before = old.first { $0.id == list.id }
            var listID = key(list.id)
            if before == nil { listID = try await session.call("create_packing_list", ["title": .string(list.title)])["id"]?.string ?? listID }
            else if before?.title != list.title { try await session.call("rename_packing_list", ["list": .string(listID), "title": .string(list.title)]) }
            for item in before?.items ?? [] where !list.items.contains(where: { $0.id == item.id }) {
                try await session.call("delete_entity", ["id": .string(key(item.id))])
            }
            for item in list.items {
                var itemID = key(item.id)
                let previous = before?.items.first { $0.id == item.id }
                if previous == nil {
                    var arguments: [String: JSONValue] = ["list": .string(listID), "title": .string(item.title), "group": .string(item.group), "people": ids(item.people), "everyone": .bool(item.everyone)]
                    if let quantity = item.quantity { arguments["quantity"] = .string(quantity) }
                    itemID = try await session.call("add_packing_item", arguments)["id"]?.string ?? itemID
                } else if let previous {
                    if previous.title != item.title || previous.group != item.group || previous.quantity != item.quantity {
                        var arguments: [String: JSONValue] = ["item": .string(itemID), "title": .string(item.title), "group": .string(item.group)]
                        if let quantity = item.quantity { arguments["quantity"] = .string(quantity) }
                        try await session.call("update_packing_item", arguments)
                    }
                    if previous.people != item.people || previous.everyone != item.everyone {
                        try await session.call("assign_packing_item", ["item": .string(itemID), "people": ids(item.people), "everyone": .bool(item.everyone)])
                    }
                }
                let packedBefore = previous?.packedBy ?? []
                for person in item.packedBy.subtracting(packedBefore) { try await session.call("set_packed", ["item": .string(itemID), "person": .string(key(person)), "packed": true]) }
                for person in packedBefore.subtracting(item.packedBy) { try await session.call("set_packed", ["item": .string(itemID), "person": .string(key(person)), "packed": false]) }
            }
        }
    }

    private func ordered<T: Identifiable>(_ values: [T], like order: [T]) -> [T] where T.ID == UUID {
        let index = Dictionary(uniqueKeysWithValues: order.enumerated().map { ($1.id, $0) })
        return values.sorted { (index[$0.id] ?? .max) < (index[$1.id] ?? .max) }
    }

    private static func decimalText(_ value: Decimal) -> JSONValue { .string(NSDecimalNumber(decimal: value).stringValue) }
    private static func decimal(_ value: JSONValue?) -> Decimal? { value?.string.flatMap { Decimal(string: $0, locale: Locale(identifier: "en_US_POSIX")) } }

    static func bookingStatus(_ status: Status) -> String {
        switch status { case .idea: "idea"; case .planned: "pending"; case .reserved: "reserved"; case .confirmed: "confirmed"; case .done: "completed"; case .cancelled: "cancelled" }
    }

    static func bookingData(_ booking: Booking, zone: String = "UTC", into existing: [String: JSONValue]?) -> [String: JSONValue] {
        var data = existing ?? ["id": .string(key(booking.id)), "type": "booking", "extensions": [:]]
        data["title"] = .string(booking.title)
        data["provider"] = .object((data["provider"]?.object ?? [:]).merging(["name": .string(booking.provider)]) { $1 })
        data["reference"] = .string(booking.reference)
        data["status"] = .string(bookingStatus(booking.status))
        data["items"] = .array(booking.items.map { .string(key($0)) })
        if let until = booking.freeCancellationUntil {
            data["cancellation"] = .object((data["cancellation"]?.object ?? [:]).merging(["free_until": local(until, zone)]) { $1 })
        } else if var cancellation = data["cancellation"]?.object { cancellation["free_until"] = nil; data["cancellation"] = .object(cancellation) }
        return data
    }

    static func booking(_ data: [String: JSONValue], zone: String) -> Booking? {
        guard let id = UUID(uuidString: data["id"]?.string ?? "") else { return nil }
        var booking = Booking(id: id, title: "", provider: "", reference: "", status: .planned, items: [])
        booking.title = data["title"]?.string ?? booking.title
        booking.provider = data["provider"]?["name"]?.string ?? booking.provider
        booking.reference = data["reference"]?.string ?? booking.reference
        booking.status = ["pending": .planned, "completed": .done][data["status"]?.string ?? ""] ?? Status(rawValue: data["status"]?.string ?? "") ?? booking.status
        booking.items = (data["items"]?.array ?? []).compactMap { UUID(uuidString: $0.string ?? "") }
        booking.freeCancellationUntil = when(["precision": "exact", "start": data["cancellation"]?["free_until"] ?? .null])?.start
        return booking
    }

    /// The app's cost categories and TravelRepo's standard ones (spec section 34.2).
    static let categories: [(CostCategory, String)] = [(.transport, "transport"), (.stay, "accommodation"), (.food, "food"), (.activities, "activity"), (.shopping, "souvenirs"), (.other, "other")]

    static func expenseData(_ e: Expense, in trip: Trip, into existing: [String: JSONValue]?) -> [String: JSONValue] {
        var data = existing ?? ["id": .string(key(e.id)), "type": "expense", "extensions": [:]]
        data["title"] = .string(e.title)
        data["amount"] = ["value": decimalText(e.amount.value), "currency": .string(e.amount.currency)]
        data["estimated"] = .bool(e.estimated)
        data["category"] = .string(categories.first { $0.0 == e.category }?.1 ?? "other")
        if let date = e.date {
            var c = Calendar(identifier: .gregorian); c.timeZone = TimeZone(identifier: trip.zone) ?? .current
            data["date"] = dateText(c.dateComponents([.year, .month, .day], from: date))
        } else { data["date"] = nil }
        data["paid_by"] = e.paidBy.map { .array([.string(key($0))]) }
        let parts: [JSONValue]
        let method: String
        switch e.split {
        case .equal: method = "equal"; parts = e.sharedBy.map { ["person": .string(key($0))] }
        case .amounts(let a): method = "amount"; parts = e.sharedBy.map { ["person": .string(key($0)), "value": decimalText(a[$0] ?? 0)] }
        case .shares(let s): method = "share"; parts = e.sharedBy.map { ["person": .string(key($0)), "value": .string(String(s[$0] ?? 0))] }
        case .percent(let p): method = "percent"; parts = e.sharedBy.map { ["person": .string(key($0)), "value": decimalText(p[$0] ?? 0)] }
        }
        data["split"] = .object((data["split"]?.object ?? [:]).merging(["method": .string(method), "parts": .array(parts)]) { $1 })
        if e.settled > 0, let payer = e.paidBy, let other = e.sharedBy.first(where: { $0 != payer }) {
            data["settled"] = [["person": .string(key(other)), "to": .string(key(payer)), "value": decimalText(e.settled)]]
        } else { data["settled"] = nil }
        if let booking = e.booking { data["related"] = [["type": "booking", "id": .string(key(booking))]] }
        else if let item = e.item { data["related"] = [["type": "schedule_item", "id": .string(key(item))]] }
        else { data["related"] = nil }
        if let converted = e.converted, e.amount.value != 0 {
            var rate = converted.value / e.amount.value, rounded = Decimal(); NSDecimalRound(&rounded, &rate, 6, .plain)
            var c = Calendar(identifier: .gregorian); c.timeZone = TimeZone(identifier: trip.zone) ?? .current
            data["converted"] = ["value": decimalText(converted.value), "currency": .string(converted.currency), "rate": decimalText(rounded),
                                 "rate_date": dateText(c.dateComponents([.year, .month, .day], from: e.date ?? Date())), "source": "card"]
        } else { data["converted"] = nil }
        return data
    }

    static func expense(_ data: [String: JSONValue], currency: String, zone: String) -> Expense? {
        guard let id = UUID(uuidString: data["id"]?.string ?? "") else { return nil }
        var e = Expense(id: id, title: "", amount: Money(value: 0, currency: currency), category: .other, sharedBy: [])
        e.title = data["title"]?.string ?? e.title
        if let value = decimal(data["amount"]?["value"]), let currency = data["amount"]?["currency"]?.string { e.amount = Money(value: value, currency: currency) }
        e.estimated = data["estimated"]?.bool ?? e.estimated
        if let category = data["category"]?.string { e.category = categories.first { $0.1 == category }?.0 ?? .other }
        e.paidBy = (data["paid_by"]?.array?.first?.string).flatMap(UUID.init(uuidString:))
        let parts = data["split"]?["parts"]?.array ?? []
        let people = parts.compactMap { UUID(uuidString: $0["person"]?.string ?? "") }
        if !people.isEmpty { e.sharedBy = people }
        func values<V>(_ convert: (JSONValue?) -> V?) -> [UUID: V] {
            Dictionary(uniqueKeysWithValues: parts.compactMap { p in UUID(uuidString: p["person"]?.string ?? "").flatMap { id in convert(p["value"]).map { (id, $0) } } })
        }
        switch data["split"]?["method"]?.string {
        case "amount": e.split = .amounts(values(decimal))
        case "share": e.split = .shares(values { $0?.string.flatMap { Int($0) } })
        case "percent": e.split = .percent(values(decimal))
        default: e.split = .equal
        }
        e.settled = (data["settled"]?.array ?? []).filter { $0["to"]?.string == e.paidBy.map(key) }.compactMap { decimal($0["value"]) }.reduce(0, +)
        let related = data["related"]?.array?.first
        e.booking = related?["type"]?.string == "booking" ? UUID(uuidString: related?["id"]?.string ?? "") : nil
        e.item = related?["type"]?.string == "schedule_item" ? UUID(uuidString: related?["id"]?.string ?? "") : nil
        if let value = decimal(data["converted"]?["value"]), let currency = data["converted"]?["currency"]?.string { e.converted = Money(value: value, currency: currency) } else { e.converted = nil }
        if let date = (e.estimated ? data["due"] ?? data["date"] : data["date"])?.string, case .allDay(let day, _)? = when(["precision": "all_day", "date": .string(date), "timezone": .string(zone)]) {
            var c = Calendar(identifier: .gregorian); c.timeZone = TimeZone(identifier: zone) ?? .current
            var noon = day; noon.hour = 12
            e.date = c.date(from: noon)
        } else { e.date = nil }
        return e
    }

    static func zoned(_ date: Date, _ zone: String) -> JSONValue { local(date, zone) }

    private static func local(_ date: Date, _ zone: String) -> JSONValue {
        let f = DateFormatter(); f.locale = Locale(identifier: "en_US_POSIX"); f.timeZone = TimeZone(identifier: zone); f.dateFormat = "yyyy-MM-dd'T'HH:mm:ss"
        return ["local": .string(f.string(from: date)), "timezone": .string(zone)]
    }

    private static func dateText(_ day: DateComponents) -> JSONValue {
        .string(String(format: "%04d-%02d-%02d", day.year ?? 0, day.month ?? 1, day.day ?? 1))
    }

    static func time(_ when: When) -> JSONValue? {
        switch when {
        case .exact(let start, let startZone, let end, let endZone): ["precision": "exact", "start": local(start, startZone), "end": local(end, endZone)]
        case .approximate(let start, let end, let zone): ["precision": "approximate", "start": local(start, zone), "end": local(end, zone)]
        case .dayPart(let day, let part, let zone): ["precision": "day_part", "date": dateText(day), "day_part": .string(part.rawValue), "timezone": .string(zone)]
        case .allDay(let day, let zone): ["precision": "all_day", "date": dateText(day), "timezone": .string(zone)]
        case .unscheduled: nil
        }
    }

    static func when(_ time: JSONValue?, zone tripZone: String = "UTC") -> When? {
        guard let time, let precision = time["precision"]?.string else { return nil }
        func instant(_ value: JSONValue?) -> (Date, String)? {
            guard let text = value?["local"]?.string, let zone = value?["timezone"]?.string else { return nil }
            let f = DateFormatter(); f.locale = Locale(identifier: "en_US_POSIX"); f.timeZone = TimeZone(identifier: zone); f.dateFormat = "yyyy-MM-dd'T'HH:mm:ss"
            return f.date(from: text).map { ($0, zone) }
        }
        func day(_ value: JSONValue?) -> DateComponents? {
            let parts = (value?.string ?? "").split(separator: "-").compactMap { Int($0) }
            return parts.count == 3 ? DateComponents(year: parts[0], month: parts[1], day: parts[2]) : nil
        }
        switch precision {
        case "exact":
            guard let (start, zone) = instant(time["start"]) else { return nil }
            let (end, endZone) = instant(time["end"]) ?? (start, zone)
            return .exact(start: start, startZone: zone, end: end, endZone: endZone)
        case "approximate", "window":
            guard let (start, zone) = instant(time["start"] ?? time["earliest"]) else { return nil }
            return .approximate(start: start, end: instant(time["end"] ?? time["latest"])?.0 ?? start, zone: zone)
        case "day_part":
            guard let d = day(time["date"]) else { return nil }
            return .dayPart(day: d, part: DayPart(rawValue: time["day_part"]?.string ?? "") ?? .morning, zone: time["timezone"]?.string ?? tripZone)
        case "all_day":
            guard let d = day(time["date"]) else { return nil }
            return .allDay(day: d, zone: time["timezone"]?.string ?? tripZone)
        default: return nil
        }
    }

    static func dayComponents(_ text: String?) -> DateComponents? {
        let parts = (text ?? "").split(separator: "-").compactMap { Int($0) }
        return parts.count == 3 ? DateComponents(year: parts[0], month: parts[1], day: parts[2]) : nil
    }

    static func status(_ text: String?) -> Status { text == "completed" ? .done : Status(rawValue: text ?? "") ?? .planned }

    static func kind(_ text: String) -> Kind { text == "free_time" ? .freeTime : Kind(rawValue: text) ?? .other }

    static func mode(_ text: String?) -> TransportMode {
        switch text { case "walking": .walk; case "rideshare": .taxi; case "ship": .ferry; default: TransportMode(rawValue: text ?? "") ?? .other }
    }

    /// The sample with every id replaced by a version 7 UUID, as TravelRepo requires; references stay consistent.
    static func withVersion7Ids(_ trip: Trip) throws -> Trip {
        var text = String(decoding: try JSONEncoder().encode(trip), as: UTF8.self)
        let pattern = try NSRegularExpression(pattern: "[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}")
        var map: [String: String] = [:]
        var milliseconds = UInt64(Date().timeIntervalSince1970 * 1000)
        for match in pattern.matches(in: text, range: NSRange(text.startIndex..., in: text)) {
            let old = String(text[Range(match.range, in: text)!])
            if map[old] == nil { milliseconds += 1; map[old] = version7(milliseconds) }
        }
        for (old, new) in map { text = text.replacingOccurrences(of: old, with: new) }
        return try JSONDecoder().decode(Trip.self, from: Data(text.utf8))
    }

    private static func version7(_ milliseconds: UInt64) -> String {
        var bytes = (0..<16).map { _ in UInt8.random(in: 0...255) }
        for i in 0..<6 { bytes[i] = UInt8((milliseconds >> (8 * (5 - UInt64(i)))) & 0xFF) }
        bytes[6] = 0x70 | (bytes[6] & 0x0F); bytes[8] = 0x80 | (bytes[8] & 0x3F)
        let hex = bytes.map { String(format: "%02X", $0) }.joined()
        let c = Array(hex)
        return String(c[0..<8]) + "-" + String(c[8..<12]) + "-" + String(c[12..<16]) + "-" + String(c[16..<20]) + "-" + String(c[20..<32])
    }
}
