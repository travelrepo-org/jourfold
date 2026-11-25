import SwiftUI
import TravelRepoKit

/// What a plan is: an activity, a journey or a stay. Each has its own fields, as in the .NET app's form.
enum PlanShape: String { case activity, journey, stay }

/// Adds or changes a plan, a journey or a stay: time with the precision it was planned with, zones of departure and
/// arrival, places, number, terminal, gate and seat, nights and hours of a stay, the booking number, who goes and
/// notes. An existing plan keeps everything the form did not change; TravelRepo writes it in one step.
struct PlanEditor: View {
    let tripID: UUID
    /// The plan to change, or nil for a new one.
    var itemID: UUID?
    @State var shape: PlanShape = .activity
    /// A group the new plan becomes a step of.
    var into: UUID? = nil
    var done: (() -> Void)? = nil
    @Environment(\.dismiss) private var dismiss
    @Environment(TripStore.self) private var store

    enum Precision: String, CaseIterable { case exact, approximate, day_part, all_day, none, window
        var label: LocalizedStringKey { switch self { case .exact: "Fixed time"; case .approximate: "About"; case .day_part: "Part of the day"; case .all_day: "All day"; case .none: "No time yet"; case .window: "In a window" } } }
    enum Who: String { case group, everyone, chosen }

    private struct Form: Equatable {
        var title = "", status = "planned", category = "", precision = Precision.exact.rawValue
        var start = Date(), end = Date(), startZone = "", endZone = "", hasEnd = true, part = "morning"
        var place: UUID?, from: UUID?, to: UUID?
        var mode = "train", carrier = "", number = "", depTerminal = "", depGate = "", depPlatform = "", arrTerminal = "", seat = ""
        var rooms = "", access = "", windowFrom = "", windowUntil = "", latestOut = "", reference = ""
        var who = Who.everyone.rawValue, people: Set<UUID> = [], notes = ""
    }
    @State private var f = Form()
    @State private var start = Form()
    @State private var loaded = false
    /// Once what the form starts with has been drawn, so only the person's own changes move other fields.
    @State private var ready = false
    @State private var busy = false
    @State private var newPlace: WritableKeyPath<Form, UUID?>?
    @State private var problem: String?

    private var bridge: TravelRepoBridge? { TravelRepoBridge.all[tripID] }
    private var entity: [String: JSONValue]? { itemID.flatMap { bridge?.entities[TravelRepoBridge.key($0)] } }
    private var isStep: Bool { into != nil || (itemID.flatMap { store.trips.first { $0.id == tripID }?.item($0)?.parent } != nil) }

    var body: some View {
        let trip = store.trips.first { $0.id == tripID }
        JFForm {
            if let trip {
                Section {
                    TextField(shape == .stay ? "Name of the hotel or flat" : shape == .journey ? "Title, e.g. Train to Nara" : "What are you planning?", text: $f.title)
                    if shape == .activity { Picker("Kind", selection: $f.category) { Text("Not set").tag(""); ForEach(Self.categories, id: \.0) { key, kind in Label { Text(kind.label) } icon: { Image(systemName: kind.symbol) }.tag(key) } } }
                    if shape == .journey { Picker("By", selection: $f.mode) { ForEach(Self.modes, id: \.0) { key, label, symbol in Label { Text(label) } icon: { Image(systemName: symbol) }.tag(key) } } }
                    Picker("Status", selection: $f.status) { ForEach(Self.statuses, id: \.0) { Text($0.1).tag($0.0) } }
                }
                timeFields(trip)
                if shape == .journey {
                    Section {
                        placePicker("From", \.from, trip)
                        placePicker("To", \.to, trip)
                        TextField("Carrier", text: $f.carrier)
                        TextField("Number", text: $f.number).textInputAutocapitalization(.characters).autocorrectionDisabled()
                        TextField("Seats", text: $f.seat)
                        TextField("Departure terminal", text: $f.depTerminal)
                        TextField("Departure gate", text: $f.depGate)
                        TextField("Departure platform", text: $f.depPlatform)
                        TextField("Arrival terminal", text: $f.arrTerminal)
                    } header: { Overline("Journey") }
                } else {
                    Section { placePicker("Place", \.place, trip) }
                }
                if shape == .stay {
                    Section {
                        TextField("Rooms, separated by commas", text: $f.rooms)
                        TextField("How guests get in", text: $f.access)
                        TextField("Check-in from, such as 15:00", text: $f.windowFrom).keyboardType(.numbersAndPunctuation)
                        TextField("Check-in until, such as 22:00", text: $f.windowUntil).keyboardType(.numbersAndPunctuation)
                        TextField("Latest check-out, such as 11:00", text: $f.latestOut).keyboardType(.numbersAndPunctuation)
                    } header: { Overline("Stay") } footer: { Footnote("Never write key box or door codes into the trip.") }
                }
                Section { TextField("Booking reference", text: $f.reference).textInputAutocapitalization(.characters).autocorrectionDisabled() } footer: { if booking == nil { Text("A number makes a booking for this plan.") } }
                Section {
                    if shape != .stay {
                        Picker("Who goes", selection: $f.who) { if isStep { Text("As the group").tag(Who.group.rawValue) }; Text("Everyone").tag(Who.everyone.rawValue); Text("Some").tag(Who.chosen.rawValue) }.pickerStyle(.segmented)
                    }
                    if shape == .stay || f.who == Who.chosen.rawValue {
                        ForEach(trip.people) { p in Toggle(isOn: Binding(get: { f.people.contains(p.id) }, set: { if $0 { f.people.insert(p.id) } else { f.people.remove(p.id) } })) { HStack { PersonAvatar(person: p, size: 24).accessibilityHidden(true); Text(p.name) } } }
                    }
                } header: { Overline(shape == .stay ? "Guests" : "Who goes") }
                JFSection("Notes") { TextField("Notes", text: $f.notes, axis: .vertical).lineLimit(2...10) }
                if let problem { Section { Text(problem).foregroundStyle(Color.jfDanger) } }
            }
        }
        .jfBackground()
        .scrollDismissesKeyboard(.interactively)
        .navigationTitle(itemID != nil ? "Edit plan" : shape == .journey ? "Journey" : shape == .stay ? "Stay" : "Plan")
        .navigationBarTitleDisplayMode(.inline)
        .toolbar {
            if itemID != nil || done == nil { ToolbarItem(placement: .cancellationAction) { Button(role: .cancel) { dismiss() } } }
            ToolbarItem(placement: .confirmationAction) { Button(role: .confirm) { Task { await save() } }.disabled(busy || f.title.trimmingCharacters(in: .whitespaces).isEmpty) }
        }
        .sheet(isPresented: Binding(get: { newPlace != nil }, set: { if !$0 { newPlace = nil } })) {
            let before = Set(trip?.places.map(\.id) ?? []), path = newPlace
            PlaceEditor(tripID: tripID, placeID: nil).onDisappear {
                if let path, let added = store.trips.first(where: { $0.id == tripID })?.places.first(where: { !before.contains($0.id) }) { f[keyPath: path] = added.id }
            }
        }
        .onAppear(perform: load)
        .onChange(of: f.from) { old, new in if ready, itemID == nil, let z = trip?.place(new)?.zone { f.startZone = z } }
        .onChange(of: f.to) { old, new in if ready, itemID == nil, let z = trip?.place(new)?.zone { f.endZone = z } }
        .onChange(of: f.start) { old, new in if ready, f.end < new || (itemID == nil && f.end.timeIntervalSince(old) > 0) { f.end = f.end.addingTimeInterval(new.timeIntervalSince(old)) } }
    }

    // MARK: Time

    @ViewBuilder
    private func timeFields(_ trip: Trip) -> some View {
        let sz = TimeZone(identifier: f.startZone) ?? trip.calendar.timeZone, ez = TimeZone(identifier: f.endZone) ?? sz
        Section {
            if shape == .activity {
                Picker("When", selection: $f.precision) { ForEach(Precision.allCases.filter { $0 != .window || start.precision == $0.rawValue }, id: \.rawValue) { Text($0.label).tag($0.rawValue) } }
            }
            switch Precision(rawValue: f.precision) ?? .exact {
            case .none: EmptyView()
            case .day_part:
                DatePicker("Day", selection: $f.start, displayedComponents: .date).environment(\.timeZone, sz)
                Picker("Part of the day", selection: $f.part) { Text("Morning").tag("morning"); Text("Afternoon").tag("afternoon"); Text("Evening").tag("evening"); Text("Night").tag("night") }
            case .all_day: DatePicker("Day", selection: $f.start, displayedComponents: .date).environment(\.timeZone, sz)
            default:
                DatePicker(shape == .stay ? "Check-in" : shape == .journey ? "Departs" : "Starts", selection: $f.start).environment(\.timeZone, sz)
                if shape != .stay { zonePicker("Time zone", $f.startZone, trip) }
                if shape == .activity { Toggle("With an end", isOn: $f.hasEnd) }
                if f.hasEnd {
                    DatePicker(shape == .stay ? "Check-out" : shape == .journey ? "Arrives" : "Ends", selection: $f.end).environment(\.timeZone, ez)
                    if shape == .journey { zonePicker("Arrival time zone", $f.endZone, trip) }
                }
            }
        } header: { Overline("When") } footer: {
            if shape == .journey, f.hasEnd, f.end > f.start { Text("Travel time \(Fmt.duration(f.end.timeIntervalSince(f.start)))") }
            else if f.precision == Precision.none.rawValue { Text("Saved without a time. Move it into a day later.") }
        }
    }

    private func zonePicker(_ label: LocalizedStringKey, _ zone: Binding<String>, _ trip: Trip) -> some View {
        // The places' zones and those the trip already uses come first.
        let used = [zone.wrappedValue, trip.place(f.from)?.zone, trip.place(f.to)?.zone, trip.place(f.place)?.zone, trip.zone].compactMap { $0 } + trip.items.flatMap { [$0.when.zone.identifier, $0.when.endZone.identifier] }
        let all = (used + TimeZonePicker.common(including: trip.zone)).reduce(into: [String]()) { if !$1.isEmpty, !$0.contains($1) { $0.append($1) } }
        return Picker(label, selection: zone) { ForEach(all, id: \.self) { Text(TimeZonePicker.name($0)).tag($0) } }
    }

    private func placePicker(_ label: LocalizedStringKey, _ path: WritableKeyPath<Form, UUID?>, _ trip: Trip) -> some View {
        Group {
            Picker(label, selection: Binding(get: { f[keyPath: path] }, set: { f[keyPath: path] = $0 })) { Text("None").tag(UUID?.none); ForEach(trip.places) { Text($0.name).tag(UUID?.some($0.id)) } }.pickerStyle(.navigationLink)
            Button { newPlace = path } label: { Label("New place", systemImage: "mappin.and.ellipse") }
        }
    }

    // MARK: Values

    static let categories: [(String, Kind)] = [("activity", .activity), ("sightseeing", .sightseeing), ("food", .food), ("culture", .culture), ("nature", .nature), ("shopping", .shopping), ("nightlife", .nightlife), ("meeting", .meeting), ("free_time", .freeTime), ("other", .other)]
    static let modes: [(String, LocalizedStringKey, String)] = [("flight", "Flight", "airplane"), ("train", "Train", "tram.fill"), ("bus", "Bus", "bus.fill"), ("car", "Car", "car.fill"), ("taxi", "Taxi", "car.side.fill"), ("rideshare", "Ride share", "car.side.fill"), ("ferry", "Ferry", "ferry.fill"), ("ship", "Ship", "ferry.fill"), ("bicycle", "Bicycle", "bicycle"), ("walking", "Walk", "figure.walk"), ("other", "Other", "arrow.triangle.swap")]
    static let statuses: [(String, LocalizedStringKey)] = [("idea", "Idea"), ("planned", "Planned"), ("reserved", "Reserved"), ("confirmed", "Confirmed"), ("completed", "Done"), ("cancelled", "Cancelled")]

    private var booking: String? { entity?["components"]?["booking"]?["ref"]?.string ?? entity?["components"]?["accommodation"]?["booking"]?.string }

    private func load() {
        guard !loaded, let trip = store.trips.first(where: { $0.id == tripID }) else { return }
        defer { start = f; loaded = true; Task { try? await Task.sleep(for: .milliseconds(200)); ready = true } }
        f.startZone = trip.zone; f.endZone = trip.zone
        f.start = ItemEditor.defaultStart(tripID); f.end = f.start.addingTimeInterval(7200)
        if shape == .stay {
            var c = trip.calendar; c.timeZone = trip.calendar.timeZone
            f.start = c.date(bySettingHour: 15, minute: 0, second: 0, of: f.start) ?? f.start
            f.end = c.date(byAdding: .day, value: 1, to: f.start).flatMap { c.date(bySettingHour: 10, minute: 0, second: 0, of: $0) } ?? f.end
            f.status = "reserved"
        }
        if into != nil { f.who = Who.group.rawValue }
        guard let d = entity else { f.people = Set(trip.people.map(\.id)); return }
        func uuid(_ v: JSONValue?) -> UUID? { v?.string.flatMap(UUID.init(uuidString:)) }
        func people(_ v: JSONValue?) -> Set<UUID> { Set((v?.array ?? []).compactMap(uuid)) }
        let t = d["components"]?["transport"], a = d["components"]?["accommodation"]
        shape = a != nil ? .stay : t != nil ? .journey : .activity
        f.title = d["title"]?.string ?? ""; f.status = d["status"]?.string ?? "planned"; f.category = d["category"]?.string ?? ""
        f.place = uuid(a?["place"]) ?? uuid(d["default_place"])
        f.from = uuid(t?["departure"]?["place"]); f.to = uuid(t?["arrival"]?["place"])
        f.mode = t?["type"]?.string ?? "train"; f.carrier = t?["carrier"]?.string ?? ""; f.number = t?["flight_number"]?.string ?? ""
        f.seat = t?["seat"]?.string ?? d["extensions"]?["jourfold"]?["seat"]?.string ?? ""
        f.depTerminal = t?["departure"]?["terminal"]?.string ?? ""; f.depGate = t?["departure"]?["gate"]?.string ?? ""; f.depPlatform = t?["departure"]?["platform"]?.string ?? ""; f.arrTerminal = t?["arrival"]?["terminal"]?.string ?? ""
        f.rooms = (a?["rooms"]?.array ?? []).compactMap(\.string).joined(separator: ", "); f.access = a?["access"]?.string ?? ""
        f.windowFrom = a?["check_in_window"]?["from"]?.string ?? ""; f.windowUntil = a?["check_in_window"]?["until"]?.string ?? ""; f.latestOut = a?["check_out_window"]?["until"]?.string ?? ""
        f.reference = booking.flatMap { bridge?.entities[$0]?["reference"]?.string } ?? ""
        let p = d["participants"]
        if shape == .stay { f.people = people(a?["guests"]) }
        else if p?["inherit"]?.bool == true { f.who = Who.group.rawValue }
        else {
            f.people = people(p?.array != nil ? p : p?["values"])
            f.who = f.people.isEmpty || f.people == Set(trip.people.map(\.id)) ? Who.everyone.rawValue : Who.chosen.rawValue
        }
        f.notes = itemID.flatMap { trip.item($0)?.notes } ?? ""
        let time = d["time"], precision = time?["precision"]?.string ?? "none"
        f.precision = shape == .activity ? precision : "exact"
        f.part = time?["day_part"]?.string ?? "morning"
        let from = a?["check_in"] ?? time?["start"] ?? time?["earliest"], to = a?["check_out"] ?? time?["end"] ?? time?["latest"]
        if let w = Self.instant(from) { f.start = w.0; f.startZone = w.1 } else if let day = time?["date"]?.string { f.startZone = time?["timezone"]?.string ?? trip.zone; f.start = Self.noon(day, f.startZone) ?? f.start }
        if let w = Self.instant(to) { f.end = w.0; f.endZone = w.1; f.hasEnd = true } else { f.endZone = f.startZone; f.hasEnd = false; f.end = f.start.addingTimeInterval(3600) }
    }

    static func instant(_ v: JSONValue?) -> (Date, String)? {
        guard let text = v?["local"]?.string, let zone = v?["timezone"]?.string else { return nil }
        let d = DateFormatter(); d.locale = Locale(identifier: "en_US_POSIX"); d.timeZone = TimeZone(identifier: zone); d.dateFormat = "yyyy-MM-dd'T'HH:mm:ss"
        return d.date(from: text).map { ($0, zone) }
    }
    private static func noon(_ day: String, _ zone: String) -> Date? { instant(["local": .string(day + "T12:00:00"), "timezone": .string(zone)])?.0 }
    private func day(_ date: Date, _ zone: String) -> String { String((TravelRepoBridge.zoned(date, zone)["local"]?.string ?? "").prefix(10)) }

    /// The plan's time; every key of the other forms is taken out, because a patch merges.
    private func time() -> JSONValue {
        if f.precision == "none" { return .null }
        var t: [String: JSONValue] = ["precision": .string(f.precision)]
        for k in ["start", "end", "earliest", "latest", "date", "day_part", "timezone"] { t[k] = .null }
        switch f.precision {
        case "day_part": t["date"] = .string(day(f.start, f.startZone)); t["day_part"] = .string(f.part); t["timezone"] = .string(f.startZone)
        case "all_day": t["date"] = .string(day(f.start, f.startZone)); t["timezone"] = .string(f.startZone)
        case "window": t["earliest"] = TravelRepoBridge.zoned(f.start, f.startZone); t["latest"] = TravelRepoBridge.zoned(f.hasEnd ? f.end : f.start, f.endZone)
        default:
            t["start"] = TravelRepoBridge.zoned(f.start, f.startZone)
            if f.hasEnd { t["end"] = TravelRepoBridge.zoned(f.end, shape == .journey ? f.endZone : f.startZone) }
        }
        return .object(t)
    }

    /// Without the nulls a patch uses to remove fields.
    private static func pruned(_ v: JSONValue) -> JSONValue {
        if case .object(let o) = v { return .object(o.filter { $0.value != .null }.mapValues(pruned)) }
        return v
    }

    private func save() async {
        guard let bridge, let trip = store.trips.first(where: { $0.id == tripID }) else { return }
        if f.hasEnd, shape != .activity || ["exact", "approximate", "window"].contains(f.precision), f.end < f.start { problem = String(localized: "The end is before the start."); return }
        for t in [f.windowFrom, f.windowUntil, f.latestOut] where !t.isEmpty && t.range(of: "^([01][0-9]|2[0-3]):[0-5][0-9]$", options: .regularExpression) == nil { problem = String(localized: "Write hours like 15:00."); return }
        problem = nil; busy = true; defer { busy = false }
        let new = itemID == nil
        var p: [String: JSONValue] = [:]
        func ids(_ set: Set<UUID>) -> JSONValue { .array(trip.people.filter { set.contains($0.id) }.map { .string(TravelRepoBridge.key($0.id)) }) }
        func ref(_ id: UUID?) -> JSONValue { id.map { .string(TravelRepoBridge.key($0)) } ?? .null }
        func text(_ s: String) -> JSONValue { s.trimmingCharacters(in: .whitespaces).isEmpty ? .null : .string(s.trimmingCharacters(in: .whitespaces)) }
        func changed<V: Equatable>(_ path: KeyPath<Form, V>) -> Bool { new || f[keyPath: path] != start[keyPath: path] }
        if changed(\.title) { p["title"] = text(f.title) }
        if changed(\.status) { p["status"] = .string(f.status) }
        if shape == .activity, changed(\.category) { p["category"] = text(f.category) }
        let timeChanged = new || f.precision != start.precision || f.start != start.start || f.end != start.end || f.startZone != start.startZone || f.endZone != start.endZone || f.hasEnd != start.hasEnd || f.part != start.part
        if timeChanged, shape != .stay || new { p["time"] = time() }
        if shape == .activity, changed(\.place) { p["default_place"] = ref(f.place) }
        if shape != .stay, changed(\.who) || (f.who == Who.chosen.rawValue && changed(\.people)) {
            p["participants"] = f.who == Who.group.rawValue ? ["inherit": true, "values": []] : ["inherit": false, "values": ids(f.who == Who.everyone.rawValue ? Set(trip.people.map(\.id)) : f.people)]
        }
        var c: [String: JSONValue] = [:]
        if shape == .journey {
            var t: [String: JSONValue] = [:], dep: [String: JSONValue] = [:], arr: [String: JSONValue] = [:]
            if changed(\.mode) { t["type"] = .string(f.mode) }
            if changed(\.carrier) { t["carrier"] = text(f.carrier) }
            if changed(\.number) { t["flight_number"] = text(f.number) }
            if changed(\.seat) { t["seat"] = text(f.seat) }
            if changed(\.from) { dep["place"] = ref(f.from) }
            if changed(\.depTerminal) { dep["terminal"] = text(f.depTerminal) }
            if changed(\.depGate) { dep["gate"] = text(f.depGate) }
            if changed(\.depPlatform) { dep["platform"] = text(f.depPlatform) }
            if changed(\.to) { arr["place"] = ref(f.to) }
            if changed(\.arrTerminal) { arr["terminal"] = text(f.arrTerminal) }
            if !dep.isEmpty { t["departure"] = .object(dep) }
            if !arr.isEmpty { t["arrival"] = .object(arr) }
            if !t.isEmpty { c["transport"] = .object(t) }
        }
        if shape == .stay {
            var a: [String: JSONValue] = [:]
            if changed(\.place) { a["place"] = ref(f.place) }
            if changed(\.start) { a["check_in"] = TravelRepoBridge.zoned(f.start, f.startZone) }
            if changed(\.end) { a["check_out"] = TravelRepoBridge.zoned(f.end, f.startZone) }
            if changed(\.people) { a["guests"] = ids(f.people) }
            if changed(\.rooms) { let r = f.rooms.split(separator: ",").map { $0.trimmingCharacters(in: .whitespaces) }.filter { !$0.isEmpty }; a["rooms"] = r.isEmpty ? .null : .array(r.map { .string($0) }) }
            if changed(\.access) { a["access"] = text(f.access) }
            if changed(\.windowFrom) || changed(\.windowUntil) { a["check_in_window"] = f.windowFrom.isEmpty && f.windowUntil.isEmpty ? .null : ["from": text(f.windowFrom), "until": text(f.windowUntil)] }
            if changed(\.latestOut) { a["check_out_window"] = f.latestOut.isEmpty ? .null : ["until": text(f.latestOut)] }
            if !a.isEmpty { c["accommodation"] = .object(a) }
        }
        if !c.isEmpty { p["components"] = .object(c) }
        var made: String?
        let ok = await bridge.changeCosts {
            var id = itemID.map(TravelRepoBridge.key)
            if let id { if !p.isEmpty { try await bridge.patch(id, p) } }
            else {
                let result = try await bridge.session.call("create_entity", ["type": "schedule_item", "data": Self.pruned(.object(p))])
                id = result["id"]?.string ?? result["entity"]?["id"]?.string
                made = id
                if let into, let id { _ = try await bridge.session.call("add_to_group", ["group": .string(TravelRepoBridge.key(into)), "item": .string(id)]) }
            }
            guard let id else { return }
            if changed(\.reference), !(new && f.reference.isEmpty) {
                if let booking { try await bridge.patch(booking, ["reference": text(f.reference)]) }
                else if !f.reference.isEmpty {
                    let b = try await bridge.session.call("create_entity", ["type": "booking", "data": ["title": .string(f.title), "reference": .string(f.reference), "status": "confirmed"]])
                    if let b = b["id"]?.string ?? b["entity"]?["id"]?.string { _ = try await bridge.session.call("link_booking", ["booking": .string(b), "items": [.string(id)]]) }
                }
            }
        }
        guard ok || made != nil else { return }
        // Notes are a Markdown file of the plan; the bridge writes them and keeps its other content.
        if changed(\.notes), !(new && f.notes.isEmpty), let id = (itemID ?? made.flatMap(UUID.init(uuidString:))) {
            store.update(tripID, "Notes") { t in if let i = t.items.firstIndex(where: { $0.id == id }) { t.items[i].notes = f.notes } }
            await bridge.flush()
        }
        UINotificationFeedbackGenerator().notificationOccurred(ok ? .success : .warning)
        if ok { if let done { done() } else { dismiss() } }
    }
}

/// The form for an existing plan in a sheet of its own.
struct ItemEditor: View {
    let tripID: UUID
    let original: PlanItem
    var body: some View { NavigationStack { PlanEditor(tripID: tripID, itemID: original.id) } }

    /// For a plan without a time: 10:00 on the trip's first day that is not over yet, or on the day the plan shows.
    @MainActor static func defaultStart(_ tripID: UUID) -> Date {
        guard let store = TravelRepoBridge.all[tripID]?.store, let trip = store.trips.first(where: { $0.id == tripID }) else { return Date() }
        let day = trip.days.first { $0.addingTimeInterval(86400) > store.now } ?? trip.days.first ?? Date()
        return trip.calendar.date(byAdding: .hour, value: 10, to: day) ?? day
    }
}
