import SwiftUI
import TravelRepoKit

extension TravelRepoBridge {
    /// Documents of an entity and what it names: its content, a stay's or a booking's own documents.
    func documentIDs(of id: String) -> [UUID] {
        guard let d = entities[id] else { return [] }
        let content = (d["content"]?.array ?? []).compactMap { $0["document"]?.string ?? $0["entity"]?.string }
        let own = (d["documents"]?.array ?? []) + (d["components"]?["accommodation"]?["documents"]?.array ?? [])
        return (content + own.compactMap(\.string)).filter { entities[$0]?["type"]?.string == "document" }.compactMap(UUID.init(uuidString:))
    }
    /// Entities of a type that point to this one in `related`.
    func related(to id: String, type: String) -> [(id: String, data: [String: JSONValue])] {
        entities.filter { $0.value["type"]?.string == type && ($0.value["related"]?.array ?? []).contains { $0["id"]?.string == id } }.map { ($0.key, $0.value) }.sorted { ($0.data["title"]?.string ?? "") < ($1.data["title"]?.string ?? "") }
    }
}

/// What a plan's details show beyond time and place, as in the .NET app: ticket, weather, changes to other journeys,
/// open tasks, its group or steps, its booking, payments, all its files and the status to choose.
struct PlanExtras: View {
    let trip: Trip
    let item: PlanItem
    @Environment(TripStore.self) private var store
    @Environment(AppNavigation.self) private var nav
    @State private var costs: CostDetails?
    @State private var changes: [(text: String, other: String)] = []
    @Binding var sheet: TripSheet?

    private var bridge: TravelRepoBridge? { _ = store.travelRepoReads; return TravelRepoBridge.all[trip.id] }
    private var key: String { TravelRepoBridge.key(item.id) }
    private var bookingKey: String? { item.booking.map(TravelRepoBridge.key) }
    private var documents: [TripDocument] {
        let ids = (bridge?.documentIDs(of: key) ?? []) + (bookingKey.flatMap { bridge?.documentIDs(of: $0) } ?? [])
        return (bridge?.documents() ?? []).filter { ids.contains($0.id) }
    }

    var body: some View {
        let data = bridge?.entities[key]
        Group {
            let tickets = documents.filter { $0.kind == .walletPass || $0.name.localizedCaseInsensitiveContains("ticket") || $0.name.localizedCaseInsensitiveContains("boarding") }
            if let first = tickets.first {
                Section {
                    if tickets.count == 1 { Button { sheet = .document(first) } label: { Label("Show ticket", systemImage: "ticket") } }
                    else { Menu { ForEach(tickets) { t in Button(t.name) { sheet = .document(t) } } } label: { Label("Show ticket", systemImage: "ticket") } }
                }
            }
            PlanWeather(tripID: trip.id, item: item.id)
            Section {
                Picker("Status", selection: Binding(get: { data?["status"]?.string ?? "planned" }, set: { s in change { try await $0.patch(key, ["status": .string(s)]) } })) {
                    ForEach(PlanEditor.statuses, id: \.0) { Text($0.1).tag($0.0) }
                }
            }
            if let t = data?["components"]?["transport"] { journey(t) }
            if let a = data?["components"]?["accommodation"], a.object != nil { stay(a) }
            if !changes.isEmpty {
                Section { ForEach(Array(changes.enumerated()), id: \.offset) { _, c in Label { VStack(alignment: .leading) { Text(c.text); Text(c.other).font(.jf(.footnote)).foregroundStyle(Color.jfMuted) } } icon: { Image(systemName: "arrow.triangle.swap") } } } header: { Overline("Changes") }
            }
            let tasks = (bridge?.related(to: key, type: "task") ?? []).filter { !["completed", "cancelled", "done"].contains($0.data["status"]?.string ?? "") }
            if !tasks.isEmpty {
                Section {
                    ForEach(tasks, id: \.id) { t in
                        HStack {
                            Button { change { try await $0.patch(t.id, ["status": "completed"]) } } label: { Image(systemName: "circle").font(.title3).foregroundStyle(Color.jfAccent) }.buttonStyle(.plain).accessibilityLabel(Text("Tick off \(t.data["title"]?.string ?? "")"))
                            Button { sheet = .task(t.id) } label: { Text(t.data["title"]?.string ?? "").foregroundStyle(Color.jfText).frame(maxWidth: .infinity, alignment: .leading).contentShape(Rectangle()) }.buttonStyle(.plain)
                        }
                    }
                } header: { Overline("Open tasks") }
            }
            steps
            if let bookingKey, let b = bridge?.entities[bookingKey] {
                Section {
                    Button { sheet = .booking(bookingKey) } label: {
                        VStack(alignment: .leading, spacing: 2) {
                            Text(b["title"]?.string ?? b["provider"]?["name"]?.string ?? "").font(.jf(.body, .semibold)).foregroundStyle(Color.jfText)
                            if let ref = b["reference"]?.string { Text(ref).font(.system(.callout, design: .monospaced)).foregroundStyle(Color.jfMuted) }
                        }
                    }
                    .contextMenu { if let ref = b["reference"]?.string { Button { UIPasteboard.general.string = ref } label: { Label("Copy reference", systemImage: "doc.on.doc") } } }
                } header: { Overline("Booking") }
            }
            let payments = (costs?.expenses ?? []).filter { e in (bridge?.entities[e.id]?["related"]?.array ?? []).contains { $0["id"]?.string == key || ($0["id"]?.string != nil && $0["id"]?.string == bookingKey) } }
            Section {
                ForEach(payments) { e in Button { sheet = .expense(e.id) } label: { PaymentRow(expense: e, trip: trip, entity: bridge?.entities[e.id]) }.buttonStyle(.plain) }
                Button { sheet = .payment(related: bookingKey ?? key, title: item.title) } label: { Label("Add a payment", systemImage: "plus") }
            } header: { Overline("Costs") }
            let shown = Set(item.attachments.map(\.id)), rest = documents.filter { !shown.contains($0.id) }
            if !rest.isEmpty {
                Section { ForEach(rest) { d in Button { sheet = .document(d) } label: { Label { Text(d.name).foregroundStyle(Color.jfText) } icon: { Image(systemName: FileIcon.symbol(d.kind)) } } } } header: { Overline("Files of the booking") }
            }
        }
        .task(id: store.travelRepoReads) { await load() }
    }

    private func load() async {
        costs = try? await bridge?.costDetails()
        let all = (try? await bridge?.session.call("list_connections"))?["connections"]?.array ?? []
        changes = all.compactMap { c in
            let onward = c["arriving"]?["id"]?.string == key
            guard onward || c["departing"]?["id"]?.string == key else { return nil }
            let time = Fmt.duration((c["layover_minutes"]?.number ?? 0) * 60), place = c["place"]?.string
            return (place.map { String(localized: "\(time) to change at \($0)") } ?? String(localized: "\(time) to change"),
                    onward ? String(localized: "On to \(c["departing"]?["title"]?.string ?? "")") : String(localized: "From \(c["arriving"]?["title"]?.string ?? "")"))
        }
    }

    private func change(_ action: @escaping (TravelRepoBridge) async throws -> Void) {
        guard let bridge = TravelRepoBridge.all[trip.id] else { return }
        Task { await bridge.changeCosts { try await action(bridge) } }
    }

    @ViewBuilder
    private func journey(_ t: JSONValue) -> some View {
        func place(_ v: JSONValue?) -> String? { trip.place(v?["place"]?.string.flatMap(UUID.init(uuidString:)))?.name }
        func more(_ v: JSONValue?) -> String { [v?["terminal"]?.string.map { String(localized: "Terminal \($0)") }, v?["gate"]?.string.map { String(localized: "Gate \($0)") }, v?["platform"]?.string.map { String(localized: "Platform \($0)") }].compactMap { $0 }.joined(separator: " · ") }
        return Section {
            row("From", place(t["departure"]), more(t["departure"]))
            row("To", place(t["arrival"]), more(t["arrival"]))
            if let s = item.when.start, let e = item.when.end, e > s { LabeledContent("Travel time") { Text(Fmt.duration(e.timeIntervalSince(s))) } }
            if let c = t["carrier"]?.string { LabeledContent("Carrier", value: c) }
            if let n = t["flight_number"]?.string { LabeledContent("Number", value: n).contextMenu { Button { UIPasteboard.general.string = n } label: { Label("Copy", systemImage: "doc.on.doc") } } }
            if let seat = t["seat"]?.string ?? item.transport?.seat { LabeledContent("Seats", value: seat) }
        } header: { Overline("Journey") }
    }

    @ViewBuilder private func row(_ label: LocalizedStringKey, _ value: String?, _ detail: String) -> some View {
        if value != nil || !detail.isEmpty { LabeledContent { VStack(alignment: .trailing) { Text(value ?? "–"); if !detail.isEmpty { Text(detail).font(.jf(.footnote)) } } } label: { Text(label) } }
    }

    private func stay(_ a: JSONValue) -> some View {
        Section {
            if let s = item.when.start { LabeledContent("Check-in") { Text("\(Fmt.shortDay(s, item.when.zone)), \(Fmt.time(s, item.when.zone))") } }
            if let e = item.when.end { LabeledContent("Check-out") { Text("\(Fmt.shortDay(e, item.when.zone)), \(Fmt.time(e, item.when.zone))") } }
            if let s = item.when.start, let e = item.when.end {
                let c = { var c = Calendar(identifier: .gregorian); c.timeZone = item.when.zone; return c }()
                LabeledContent("Nights") { Text("\(c.dateComponents([.day], from: c.startOfDay(for: s), to: c.startOfDay(for: e)).day ?? 0)") }
            }
            let hours = [a["check_in_window"]?["from"]?.string, a["check_in_window"]?["until"]?.string].compactMap { $0 }
            if !hours.isEmpty { LabeledContent("Check-in hours", value: hours.joined(separator: "–")) }
            if let latest = a["check_out_window"]?["until"]?.string { LabeledContent("Latest check-out", value: latest) }
            if let room = item.stay?.room { LabeledContent("Room", value: room) }
            if let access = a["access"]?.string { LabeledContent("How guests get in", value: ["reception": String(localized: "Reception"), "self": String(localized: "Self check-in"), "host": String(localized: "Host")][access] ?? access) }
            if let note = a["arrival_notes"]?.string { Text(note) }
        } header: { Overline("Stay") }
    }

    // MARK: Groups

    @ViewBuilder
    private var steps: some View {
        if item.stay == nil {
            let children = trip.children(of: item.id)
            Section {
                if let parent = trip.item(item.parent) {
                    Button { nav.detail = parent.id } label: { Label("Step of \(parent.title)", systemImage: "square.stack.3d.up") }
                    Button { change { _ = try await $0.session.call("remove_from_group", ["item": .string(key)]) } } label: { Label("Take out of the group", systemImage: "rectangle.portrait.and.arrow.right") }
                } else {
                    ForEach(children) { c in Button { nav.detail = c.id } label: { PlanRow(trip: trip, item: c) }.buttonStyle(.plain) }
                    Button { let id = item.id; nav.detail = nil; DispatchQueue.main.asyncAfter(deadline: .now() + 0.5) { nav.quickAddInto = id; nav.quickAddKind = nil; nav.quickAdd = true } } label: { Label("New step", systemImage: "plus") }
                    let candidates = trip.items.filter { $0.id != item.id && $0.parent == nil && $0.stay == nil && trip.children(of: $0.id).isEmpty }
                    if !candidates.isEmpty {
                        Menu { ForEach(candidates) { c in Button(c.title) { change { _ = try await $0.session.call("add_to_group", ["group": .string(key), "item": .string(TravelRepoBridge.key(c.id))]) } } } } label: { Label("Take in a plan", systemImage: "square.stack.3d.down.right") }
                    }
                    let groups = children.isEmpty ? trip.items.filter { g in g.id != item.id && !trip.children(of: g.id).isEmpty } : []
                    if !groups.isEmpty {
                        Menu { ForEach(groups) { g in Button(g.title) { change { _ = try await $0.session.call("add_to_group", ["group": .string(TravelRepoBridge.key(g.id)), "item": .string(key)]) } } } } label: { Label("Join a group", systemImage: "square.stack.3d.up") }
                    }
                }
            } header: { Overline(children.isEmpty ? "Group" : "Steps") }
        }
    }
}

/// A note that a plan refers to, with its text.
struct NoteSheet: View {
    let tripID: UUID
    let noteID: UUID
    @Environment(\.dismiss) private var dismiss
    @State private var text = ""
    var body: some View {
        let d = TravelRepoBridge.all[tripID]?.entities[TravelRepoBridge.key(noteID)]
        NavigationStack {
            ScrollView { Text(text).textSelection(.enabled).frame(maxWidth: .infinity, alignment: .leading).padding() }
                .jfBackground().navigationTitle(d?["title"]?.string ?? "").navigationBarTitleDisplayMode(.inline)
                .toolbar { ToolbarItem(placement: .topBarLeading) { Button(role: .close) { dismiss() } } }
        }
        .task {
            var parts = [d?["body"]?.string].compactMap { $0 }
            for p in d?["content"]?.array ?? [] { if let f = p["file"]?.string, let data = try? await TravelRepoBridge.all[tripID]?.session.readResource(f) { parts.append(String(decoding: data, as: UTF8.self)) } }
            text = parts.joined(separator: "\n\n")
        }
    }
}
