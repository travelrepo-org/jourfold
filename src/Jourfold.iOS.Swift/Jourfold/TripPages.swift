import SwiftUI
import TravelRepoKit

// Bookings, tasks, packing lists and budgets, as the .NET app's pages under More show them. They work on TravelRepo's
// entities and commands directly, so what this app's own model does not know (several assignees, no due date, a
// cancelled task) is neither hidden nor changed.

private func names(_ ids: JSONValue?, _ trip: Trip) -> [String] { (ids?.array ?? []).compactMap { trip.person($0.string.flatMap(UUID.init(uuidString:)))?.name } }

extension TravelRepoBridge {
    @discardableResult
    func create(_ type: String, _ data: [String: JSONValue]) async throws -> String? {
        let r = try await session.call("create_entity", ["type": .string(type), "data": .object(data)])
        return r["id"]?.string ?? r["entity"]?["id"]?.string
    }
    func patch(_ id: String, _ changes: [String: JSONValue]) async throws { _ = try await session.call("update_entity", ["id": .string(id), "changes": .object(changes)]) }
    /// The plans of a booking: those it lists and those that point to it.
    func plans(of booking: String) -> [UUID] {
        let listed = (entities[booking]?["items"]?.array ?? []).compactMap { $0.string.flatMap(UUID.init(uuidString:)) }
        let pointing = entities.values.filter { $0["components"]?["booking"]?["ref"]?.string == booking }.compactMap { $0["id"]?.string.flatMap(UUID.init(uuidString:)) }
        return listed + pointing.filter { !listed.contains($0) }
    }
}

// MARK: Bookings

struct BookingsView: View {
    let tripID: UUID
    @Environment(TripStore.self) private var store
    @Environment(AppNavigation.self) private var nav
    @State private var costs: CostDetails?
    @State private var showing: BookingID?

    struct BookingID: Identifiable { let id: String }
    private struct Row: Identifiable { let id: String; let data: [String: JSONValue]; let first: PlanItem? }

    var body: some View {
        let trip = store.trip(tripID)
        let bridge = { _ = store.travelRepoReads; return TravelRepoBridge.all[tripID] }()
        let rows = (bridge?.entities ?? [:]).filter { $0.value["type"]?.string == "booking" }.map { id, data in
            Row(id: id, data: data, first: bridge?.plans(of: id).compactMap { trip.item($0) }.min { ($0.when.start ?? .distantFuture) < ($1.when.start ?? .distantFuture) })
        }.sorted { ($0.first?.when.start ?? .distantFuture, $0.data["title"]?.string ?? "") < ($1.first?.when.start ?? .distantFuture, $1.data["title"]?.string ?? "") }
        JFList {
            if rows.isEmpty { Section { Text("No bookings yet. Add hotels, flights and tickets with their numbers.").foregroundStyle(Color.jfMuted) } }
            ForEach(["confirmed", "reserved", "pending", "idea", "completed", "cancelled"], id: \.self) { status in
                let group = rows.filter { ($0.data["status"]?.string ?? "pending") == status }
                if !group.isEmpty {
                    Section {
                        ForEach(group) { r in
                            Button { showing = .init(id: r.id) } label: { row(r, trip) }.buttonStyle(.plain)
                                .contextMenu { if let ref = r.data["reference"]?.string { Button { UIPasteboard.general.string = ref } label: { Label("Copy reference", systemImage: "doc.on.doc") } } }
                        }
                    } header: { Overline(Self.status(status)) }
                }
            }
        }
        .listStyle(.insetGrouped)
        .jfBackground()
        .navigationTitle("Bookings")
        .toolbar { ToolbarItem(placement: .topBarTrailing) { Button { nav.quickAddKind = .booking; nav.quickAdd = true } label: { Image(systemName: "plus") }.accessibilityLabel(Text("Add booking")) } }
        .task(id: store.travelRepoReads) { costs = try? await TravelRepoBridge.all[tripID]?.costDetails() }
        .sheet(item: $showing) { BookingSheet(tripID: tripID, bookingID: $0.id) }
    }

    static func status(_ s: String) -> LocalizedStringKey {
        switch s { case "confirmed": "Confirmed"; case "reserved": "Reserved"; case "idea": "Idea"; case "completed": "Completed"; case "cancelled": "Cancelled"; default: "Pending" }
    }

    /// The payments that belong to a booking, by the ids TravelRepo stores.
    static func payments(_ booking: String, _ costs: CostDetails?, _ bridge: TravelRepoBridge?) -> [CostDetails.Expense] {
        (costs?.expenses ?? []).filter { e in (bridge?.entities[e.id]?["related"]?.array ?? []).contains { $0["id"]?.string == booking } }
    }

    private func row(_ r: Row, _ trip: Trip) -> some View {
        HStack(alignment: .top, spacing: 12) {
            Image(systemName: r.first?.kind.symbol ?? "ticket").foregroundStyle(Color.jfAccent).frame(width: 28).accessibilityHidden(true)
            VStack(alignment: .leading, spacing: 2) {
                Text(r.data["title"]?.string ?? "").font(.jf(.body, .semibold)).foregroundStyle(Color.jfText)
                let place = trip.place(r.first?.stay?.place ?? r.first?.place ?? r.first?.transport?.to)?.name
                Text([r.first?.when.start.map { Fmt.shortDay($0, r.first!.when.zone) } ?? String(localized: "No date yet"), place].compactMap { $0 }.joined(separator: " · ")).font(.jf(.footnote)).foregroundStyle(Color.jfMuted)
                if let ref = r.data["reference"]?.string, !ref.isEmpty { Text("Reference \(ref)").font(.jf(.footnote)).foregroundStyle(Color.jfMuted) }
            }
            Spacer(minLength: 4)
            let sums = CostsView.sums(Self.payments(r.id, costs, TravelRepoBridge.all[tripID]))
            if !sums.isEmpty { Text(CostsView.text(sums)).font(.jf(.footnote, .semibold).monospacedDigit()).foregroundStyle(Color.jfText) }
        }
        .contentShape(Rectangle())
    }
}

struct BookingSheet: View {
    let tripID: UUID
    let bookingID: String
    @Environment(\.dismiss) private var dismiss
    @Environment(TripStore.self) private var store
    @Environment(AppNavigation.self) private var nav
    @State private var costs: CostDetails?
    @State private var editing = false
    @State private var deleting = false
    @State private var document: TripDocument?
    @State private var attaching = false
    @State private var showing: CostDetails.Expense?
    @State private var adding = false

    var body: some View {
        let bridge = { _ = store.travelRepoReads; return TravelRepoBridge.all[tripID] }()
        NavigationStack {
            JFList {
                if let trip = store.trips.first(where: { $0.id == tripID }), let data = bridge?.entities[bookingID] { content(data, trip, bridge) }
                else { ContentUnavailableView("This booking is gone", systemImage: "ticket") }
            }
            .listStyle(.insetGrouped)
            .jfBackground()
            .navigationTitle(bridge?.entities[bookingID]?["title"]?.string ?? "")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .topBarLeading) { Button(role: .close) { dismiss() } }
                ToolbarItem(placement: .topBarTrailing) { Button { editing = true } label: { Image(systemName: "pencil") }.accessibilityLabel(Text("Edit")) }
                ToolbarItem(placement: .topBarTrailing) { Button(role: .destructive) { deleting = true } label: { Image(systemName: "trash") }.accessibilityLabel(Text("Delete")) }
            }
            .task(id: store.travelRepoReads) { costs = try? await bridge?.costDetails() }
            .sheet(item: $document) { DocumentView(tripID: tripID, document: $0.id, name: $0.name) }
            .sheet(isPresented: $attaching) { if let id = UUID(uuidString: bookingID) { AddDocumentSheet(tripID: tripID, target: id) } }
            .sheet(isPresented: $editing) { BookingEditor(tripID: tripID, bookingID: bookingID) }
            .sheet(item: $showing) { ExpenseSheet(tripID: tripID, expenseID: $0.id) }
            .sheet(isPresented: $adding) { PaymentSheet(tripID: tripID, related: bookingID, title: bridge?.entities[bookingID]?["title"]?.string ?? "") }
            .deleteConfirmation(tripID: tripID, ids: deleting ? UUID(uuidString: bookingID).map { [$0] } : nil, title: bridge?.entities[bookingID]?["title"]?.string) {
                deleting = false
                if TravelRepoBridge.all[tripID]?.entities[bookingID] == nil { dismiss() }
            }
        }
    }

    @ViewBuilder
    private func content(_ data: [String: JSONValue], _ trip: Trip, _ bridge: TravelRepoBridge?) -> some View {
        Section {
            LabeledContent("Status") { Text(BookingsView.status(data["status"]?.string ?? "pending")) }
            if let ref = data["reference"]?.string, !ref.isEmpty {
                Button { UIPasteboard.general.string = ref; nav.toast = String(localized: "Copied") } label: {
                    LabeledContent("Reference") { Text(ref).font(.system(.body, design: .monospaced)) }
                }
                .accessibilityHint(Text("Copies the reference"))
            }
            if let provider = data["provider"]?["name"]?.string ?? data["provider"]?.string, !provider.isEmpty { LabeledContent("Provider", value: provider) }
            let people = names(data["travelers"], trip)
            if !people.isEmpty { LabeledContent("Travellers", value: people.joined(separator: ", ")) }
            if let until = TravelRepoBridge.when(["precision": "exact", "start": data["cancellation"]?["free_until"] ?? .null]), let date = until.start {
                Label("Free cancellation until \(Fmt.shortDay(date, until.zone)), \(Fmt.time(date, until.zone))", systemImage: "arrow.uturn.backward.circle").font(.jf(.footnote)).foregroundStyle(date < store.now ? Color.jfMuted : Color.jfSuccess)
            }
            ForEach([data["body"]?.string, data["notes"]?.string].compactMap { $0 }.filter { !$0.isEmpty }, id: \.self) { Text($0) }
        }
        let plans = (bridge?.plans(of: bookingID) ?? []).compactMap { trip.item($0) }
        Section {
            if plans.isEmpty { Text("Not linked to a plan yet.").foregroundStyle(Color.jfMuted) }
            ForEach(plans) { item in Button { dismiss(); nav.detail = item.id } label: { PlanRow(trip: trip, item: item, showDay: true) }.buttonStyle(.plain) }
            if let place = plans.lazy.compactMap({ trip.place($0.stay?.place ?? $0.place ?? $0.transport?.from) }).first { Button { Maps.navigate(to: place) } label: { Label("Navigate", systemImage: "location") } }
        } header: { Overline("Plans") }
        Section {
            let ids = bridge?.documentIDs(of: bookingID) ?? []
            ForEach((bridge?.documents() ?? []).filter { ids.contains($0.id) }) { d in
                Button { document = d } label: { Label { Text(d.name).foregroundStyle(Color.jfText) } icon: { Image(systemName: FileIcon.symbol(d.kind)).foregroundStyle(Color.jfAccent) } }
                    .swipeActions { Button(role: .destructive) { if let id = UUID(uuidString: bookingID) { Task { await bridge?.detach(d.id, from: id) } } } label: { Label("Remove", systemImage: "minus.circle") } }
            }
            Button { attaching = true } label: { Label("Add a file or scan", systemImage: "paperclip") }
        } header: { Overline("Tickets and files") }
        Section {
            let list = BookingsView.payments(bookingID, costs, bridge)
            if list.isEmpty { Text("No payments yet.").foregroundStyle(Color.jfMuted) }
            if let value = data["price"]?["value"]?.string, let code = data["price"]?["currency"]?.string, let amount = Decimal(string: value, locale: Locale(identifier: "en_US_POSIX")) {
                Text("Price from an older version, not counted: \(Fmt.money(amount, code))").font(.jf(.footnote)).foregroundStyle(Color.jfMuted)
            }
            ForEach(list) { e in Button { showing = e } label: { PaymentRow(expense: e, trip: trip, entity: bridge?.entities[e.id]) }.buttonStyle(.plain) }
            Button { adding = true } label: { Label("Add a payment", systemImage: "plus") }
        } header: { Overline("Payments") }
    }
}

struct BookingEditor: View {
    let tripID: UUID
    let bookingID: String
    @Environment(\.dismiss) private var dismiss
    @Environment(TripStore.self) private var store
    @State private var title = ""
    @State private var provider = ""
    @State private var reference = ""
    @State private var status = "pending"
    @State private var notes = ""
    @State private var people: Set<UUID> = []
    @State private var plans: Set<UUID> = []
    @State private var hasDeadline = false
    @State private var deadline = Date()
    @State private var start: [String: JSONValue]?
    @State private var startPlans: Set<UUID> = []
    @State private var startDeadline: (Bool, Date) = (false, Date())
    @State private var busy = false

    var body: some View {
        let trip = store.trips.first { $0.id == tripID }
        NavigationStack {
            JFForm {
                Section {
                    TextField("Title", text: $title)
                    TextField("Provider", text: $provider)
                    TextField("Booking reference", text: $reference).autocorrectionDisabled().textInputAutocapitalization(.characters)
                    Picker("Status", selection: $status) { ForEach(["idea", "pending", "reserved", "confirmed", "completed", "cancelled"], id: \.self) { Text(BookingsView.status($0)).tag($0) } }
                }
                Section {
                    OptionalDateRows(add: "Add a free cancellation deadline", title: "Free cancellation until", remove: "Remove the deadline", has: $hasDeadline, date: $deadline)
                }
                .environment(\.timeZone, trip?.calendar.timeZone ?? .current)
                if let trip {
                    Section { PeopleRow(title: "Travellers", people: trip.people, chosen: $people) }
                    JFSection("Plans") { PlanRows(trip: trip, chosen: $plans) }
                }
                JFSection("Notes") { TextField("Notes", text: $notes, axis: .vertical).lineLimit(2...8) }
            }
            .jfBackground()
            .navigationTitle("Edit booking")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) { Button(role: .cancel) { dismiss() } }
                ToolbarItem(placement: .confirmationAction) { Button(role: .confirm) { Task { await save() } }.disabled(busy || title.trimmingCharacters(in: .whitespaces).isEmpty) }
            }
            .onAppear(perform: load)
        }
    }

    private func load() {
        guard start == nil, let bridge = TravelRepoBridge.all[tripID], let d = bridge.entities[bookingID] else { return }
        start = d
        title = d["title"]?.string ?? ""; provider = d["provider"]?["name"]?.string ?? ""; reference = d["reference"]?.string ?? ""; status = d["status"]?.string ?? "pending"; notes = d["body"]?.string ?? ""
        people = Set((d["travelers"]?.array ?? []).compactMap { $0.string.flatMap(UUID.init(uuidString:)) })
        plans = Set(bridge.plans(of: bookingID)); startPlans = plans
        if let w = TravelRepoBridge.when(["precision": "exact", "start": d["cancellation"]?["free_until"] ?? .null])?.start { hasDeadline = true; deadline = w }
        startDeadline = (hasDeadline, deadline)
    }

    private func save() async {
        guard let bridge = TravelRepoBridge.all[tripID], let start, let trip = store.trips.first(where: { $0.id == tripID }) else { return }
        busy = true; defer { busy = false }
        var patch: [String: JSONValue] = [:]
        func text(_ key: String, _ value: String, _ before: String?) { if value != before ?? "" { patch[key] = value.isEmpty ? .null : .string(value) } }
        text("title", title, start["title"]?.string); text("reference", reference, start["reference"]?.string); text("body", notes, start["body"]?.string)
        if provider != start["provider"]?["name"]?.string ?? "" { patch["provider"] = ["name": .string(provider)] }
        if status != start["status"]?.string ?? "pending" { patch["status"] = .string(status) }
        if people != Set((start["travelers"]?.array ?? []).compactMap { $0.string.flatMap(UUID.init(uuidString:)) }) { patch["travelers"] = .array(trip.people.filter { people.contains($0.id) }.map { .string(TravelRepoBridge.key($0.id)) }) }
        if (hasDeadline, deadline) != startDeadline { patch["cancellation"] = ["free_until": hasDeadline ? TravelRepoBridge.zoned(deadline, trip.zone) : .null] }
        let ok = await bridge.changeCosts {
            if !patch.isEmpty { try await bridge.patch(bookingID, patch) }
            if plans != startPlans { _ = try await bridge.session.call("link_booking", ["booking": .string(bookingID), "items": .array(trip.items.filter { plans.contains($0.id) }.map { .string(TravelRepoBridge.key($0.id)) })]) }
        }
        if ok { dismiss() }
    }
}

/// A payment for a booking or plan, paid or still to pay.
struct PaymentSheet: View {
    let tripID: UUID
    let related: String
    @State var title: String
    @Environment(\.dismiss) private var dismiss
    @Environment(TripStore.self) private var store
    @State private var amount = ""
    @State private var currency = ""
    @State private var planned = false
    @State private var hasDue = false
    @State private var due = Date()
    @State private var payer: UUID?
    @State private var busy = false

    var body: some View {
        let trip = store.trips.first { $0.id == tripID }
        NavigationStack {
            JFForm {
                Section {
                    TextField("Title", text: $title)
                    HStack {
                        TextField("Amount", text: $amount).decimalKeyboard()
                        Picker("Currency", selection: $currency) { ForEach(trip.map(RecentCurrencies.list) ?? ["EUR"], id: \.self) { Text($0) } }.labelsHidden()
                    }
                    Toggle("Still to pay", isOn: $planned)
                    if planned { Toggle("Due date", isOn: $hasDue); if hasDue { DatePicker("Due", selection: $due, displayedComponents: .date) } }
                    else { Picker("Paid by", selection: $payer) { Text("Nobody yet").tag(UUID?.none); ForEach(trip?.people ?? []) { Text($0.name).tag(UUID?.some($0.id)) } } }
                } footer: { Footnote("Shared equally by the people of the booking. You can change that in the payment.") }
            }
            .jfBackground()
            .navigationTitle("Add a payment")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) { Button(role: .cancel) { dismiss() } }
                ToolbarItem(placement: .confirmationAction) { Button(role: .confirm) { Task { await save() } }.disabled(busy || amount.isEmpty || title.isEmpty) }
            }
            .onAppear { currency = trip?.currency ?? "EUR"; payer = store.me[tripID] }
        }
        .presentationDetents([.medium, .large])
    }

    private func save() async {
        guard let bridge = TravelRepoBridge.all[tripID] else { return }
        busy = true; defer { busy = false }
        var a: [String: JSONValue] = ["title": .string(title), "amount": .string(amount), "currency": .string(currency), "belongs_to": .string(related), "estimated": .bool(planned)]
        if !planned, let payer { a["paid_by"] = [.string(TravelRepoBridge.key(payer))] }
        if planned, hasDue { let c = Calendar.current.dateComponents([.year, .month, .day], from: due); a["due"] = .string(String(format: "%04d-%02d-%02d", c.year!, c.month!, c.day!)) }
        if await bridge.changeCosts({ _ = try await bridge.session.call("add_expense", a) }) { dismiss() }
    }
}

// MARK: Tasks

struct TasksView: View {
    let tripID: UUID
    @Environment(TripStore.self) private var store
    @Environment(AppNavigation.self) private var nav
    @State private var editing: TaskID?
    @State private var deleting: (id: UUID, title: String)?
    struct TaskID: Identifiable { let id: String }
    private struct Row: Identifiable { let id: String; let data: [String: JSONValue]; let due: String?
        var done: Bool { ["completed", "cancelled", "done"].contains(data["status"]?.string ?? "") } }

    var body: some View {
        let trip = store.trip(tripID)
        let bridge = { _ = store.travelRepoReads; return TravelRepoBridge.all[tripID] }()
        let c = TravelRepoBridge.today(in: trip.zone), today = String(format: "%04d-%02d-%02d", c.year ?? 0, c.month ?? 1, c.day ?? 1)
        let rows = (bridge?.entities ?? [:]).filter { $0.value["type"]?.string == "task" && $0.value["components"]?["packing"] == nil }
            .map { Row(id: $0.key, data: $0.value, due: $0.value["due"]?["date"]?.string) }.sorted { ($0.due ?? "9999", $0.data["title"]?.string ?? "") < ($1.due ?? "9999", $1.data["title"]?.string ?? "") }
        let late = rows.filter { !$0.done && ($0.due ?? "9999") < today }
        JFList {
            if rows.isEmpty { Section { Text("No tasks yet. Add what still needs doing before the trip.").foregroundStyle(Color.jfMuted) } }
            section("Overdue", late, trip, true)
            section("Still open", rows.filter { r in !r.done && !late.contains { $0.id == r.id } }, trip, false)
            section("Done", rows.filter(\.done), trip, false)
        }
        .listStyle(.insetGrouped)
        .jfBackground()
        .navigationTitle("Tasks")
        .toolbar { ToolbarItem(placement: .topBarTrailing) { Button { nav.quickAddKind = .task; nav.quickAdd = true } label: { Image(systemName: "plus") }.accessibilityLabel(Text("Add task")) } }
        .sheet(item: $editing) { TaskEditor(tripID: tripID, taskID: $0.id) }
        .deleteConfirmation(tripID: tripID, ids: deleting.map { [$0.id] }, title: deleting?.title) { deleting = nil }
    }

    @ViewBuilder
    private func section(_ title: LocalizedStringKey, _ rows: [Row], _ trip: Trip, _ late: Bool) -> some View {
        if !rows.isEmpty {
            Section {
                ForEach(rows) { r in
                    HStack(spacing: 12) {
                        Button { Task { await toggle(r) } } label: {
                            Image(systemName: r.data["status"]?.string == "cancelled" ? "xmark.circle" : r.done ? "checkmark.circle.fill" : "circle").font(.title3).foregroundStyle(r.done ? Color.jfSuccess : Color.jfAccent).frame(minWidth: 44, minHeight: 44).contentShape(Rectangle())
                        }
                        .buttonStyle(.plain).accessibilityLabel(r.done ? Text("Reopen \(r.data["title"]?.string ?? "")") : Text("Tick off \(r.data["title"]?.string ?? "")"))
                        Button { editing = .init(id: r.id) } label: {
                            VStack(alignment: .leading, spacing: 2) {
                                Text(r.data["title"]?.string ?? "").font(.jf(.body, .medium)).strikethrough(r.done).foregroundStyle(r.done ? Color.jfMuted : Color.jfText)
                                let related = (r.data["related"]?.array ?? []).compactMap { trip.item($0["id"]?.string.flatMap(UUID.init(uuidString:)))?.title }.first
                                let detail = [r.due.map { CostsView.day($0, weekday: true) }, names(r.data["assigned_to"], trip).joined(separator: ", "), related].compactMap { $0 }.filter { !$0.isEmpty }.joined(separator: " · ")
                                if !detail.isEmpty { HStack(spacing: 4) { if late { Image(systemName: "exclamationmark.triangle").accessibilityLabel(Text("Overdue")) }; Text(detail) }.font(.jf(.footnote)).foregroundStyle(late ? Color.jfDanger : Color.jfMuted) }
                            }
                            .frame(maxWidth: .infinity, alignment: .leading).contentShape(Rectangle())
                        }
                        .buttonStyle(.plain)
                    }
                    .swipeActions { Button(role: .destructive) { if let id = UUID(uuidString: r.id) { deleting = (id, r.data["title"]?.string ?? "") } } label: { Label("Delete", systemImage: "trash") } }
                }
            } header: { Overline(title) }
        }
    }

    private func toggle(_ r: Row) async {
        guard let bridge = TravelRepoBridge.all[tripID] else { return }
        UIImpactFeedbackGenerator(style: .light).impactOccurred()
        await bridge.changeCosts { try await bridge.patch(r.id, ["status": .string(r.done ? "open" : "completed")]) }
    }
}

struct TaskEditor: View {
    let tripID: UUID
    let taskID: String
    @State private var body_ = ""
    @Environment(\.dismiss) private var dismiss
    @Environment(TripStore.self) private var store
    @State private var title = ""
    @State private var hasDue = false
    @State private var due = Date()
    @State private var people: Set<UUID> = []
    @State private var start: [String: JSONValue]?
    @State private var busy = false
    private var utc: Calendar { var c = Calendar(identifier: .gregorian); c.timeZone = TimeZone(identifier: "UTC")!; return c }
    private var dueText: String? { guard hasDue else { return nil }; let c = utc.dateComponents([.year, .month, .day], from: due); return String(format: "%04d-%02d-%02d", c.year!, c.month!, c.day!) }

    var body: some View {
        let trip = store.trips.first { $0.id == tripID }
        NavigationStack {
            JFForm {
                Section { TextField("Title", text: $title, axis: .vertical) }
                Section { OptionalDateRows(add: "Add a due date", title: "Due", remove: "Remove the due date", has: $hasDue, date: $due, parts: .date) }.environment(\.timeZone, utc.timeZone)
                JFSection("Notes") { TextField("Notes", text: $body_, axis: .vertical).lineLimit(2...8) }
                Section { PeopleRow(title: "For", people: trip?.people ?? [], chosen: $people) }
            }
            .jfBackground()
            .navigationTitle("Edit task")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) { Button(role: .cancel) { dismiss() } }
                ToolbarItem(placement: .confirmationAction) { Button(role: .confirm) { Task { await save() } }.disabled(busy || title.trimmingCharacters(in: .whitespaces).isEmpty) }
            }
            .onAppear {
                guard start == nil, let d = TravelRepoBridge.all[tripID]?.entities[taskID] else { return }
                start = d; title = d["title"]?.string ?? ""; body_ = d["body"]?.string ?? ""
                people = Set((d["assigned_to"]?.array ?? []).compactMap { $0.string.flatMap(UUID.init(uuidString:)) })
                if let day = d["due"]?["date"]?.string, let c = TravelRepoBridge.dayComponents(day), let date = utc.date(from: c) { hasDue = true; due = date }
            }
        }
        .presentationDetents([.medium, .large])
    }

    private func save() async {
        guard let bridge = TravelRepoBridge.all[tripID], let start, let trip = store.trips.first(where: { $0.id == tripID }) else { return }
        busy = true; defer { busy = false }
        var patch: [String: JSONValue] = [:]
        if title != start["title"]?.string { patch["title"] = .string(title) }
        if body_ != start["body"]?.string ?? "" { patch["body"] = body_.isEmpty ? .null : .string(body_) }
        if dueText != start["due"]?["date"]?.string { patch["due"] = dueText.map { ["date": .string($0)] } ?? .null }
        if people != Set((start["assigned_to"]?.array ?? []).compactMap { $0.string.flatMap(UUID.init(uuidString:)) }) { patch["assigned_to"] = .array(trip.people.filter { people.contains($0.id) }.map { .string(TravelRepoBridge.key($0.id)) }) }
        if patch.isEmpty { dismiss(); return }
        if await bridge.changeCosts({ try await bridge.patch(taskID, patch) }) { dismiss() }
    }
}

// MARK: Packing lists

/// What the user brings, to tick off; what others bring, to see; what nobody brings yet, to take on with a tap.
struct PackingView: View {
    let tripID: UUID
    @Environment(TripStore.self) private var store
    @AppStorage("packing.filter") private var filter = "mine"
    @State private var drafts: [UUID: String] = [:]
    @State private var newList = ""
    @State private var editing: PackingItem?
    @State private var renaming: PackingList?
    @State private var name = ""
    @State private var deleting: PackingList?

    var body: some View {
        let trip = store.trip(tripID), me = store.me[tripID]
        let shown = me == nil && filter == "mine" ? "all" : filter
        JFList {
            Section {
                if me == nil, !trip.people.isEmpty {
                    NavigationLink(value: MorePage.travellers) { Label("Choose who you are to tick off your own things", systemImage: "person.crop.circle.badge.questionmark") }
                }
                if !trip.packing.isEmpty {
                    Picker("Show", selection: Binding(get: { shown }, set: { filter = $0 })) {
                        if me != nil { Text("Mine").tag("mine") }
                        Text("All").tag("all")
                        let open = trip.packing.flatMap(\.items).filter { !$0.everyone && $0.people.isEmpty }.count
                        Text(open > 0 ? "Nobody yet (\(open))" : "Nobody yet").tag("unassigned")
                    }
                    .pickerStyle(.segmented)
                } else { Text("No packing lists yet.").foregroundStyle(Color.jfMuted) }
            }
            ForEach(trip.packing) { list in
                let items = list.items.filter { i in shown == "mine" ? (i.everyone || i.people.contains(me!)) : shown == "unassigned" ? (!i.everyone && i.people.isEmpty) : true }
                let groups = items.reduce(into: [String]()) { if !$0.contains($1.group) { $0.append($1.group) } }
                Section {
                    if items.isEmpty { Text(list.items.isEmpty ? "Nothing on this list yet. Add the first item below." : "Nothing here.").foregroundStyle(Color.jfMuted) }
                    ForEach(groups, id: \.self) { g in
                        if groups.count > 1 || !g.isEmpty { Text(g.isEmpty ? String(localized: "No group") : g).font(.jf(.subheadline, .semibold)).foregroundStyle(Color.jfMuted) }
                        ForEach(items.filter { $0.group == g }) { row($0, trip, me) }
                    }
                    // Above the field, where the keyboard does not hide it.
                    if !(drafts[list.id] ?? "").isEmpty {
                        PackingLineHints(text: Binding(get: { drafts[list.id] ?? "" }, set: { drafts[list.id] = $0 }), trip: trip) { add(list, me, newPeople: true) }
                    }
                    HStack {
                        TextField("2x Charger #Electronics @all", text: Binding(get: { drafts[list.id] ?? "" }, set: { drafts[list.id] = $0 })).submitLabel(.done).onSubmit { add(list, me) }
                        Button { add(list, me) } label: { Image(systemName: "plus.circle.fill").font(.title3) }.buttonStyle(.plain).foregroundStyle(Color.jfAccent)
                            .disabled(!PackingLine(drafts[list.id] ?? "", people: trip.people).canAdd).accessibilityLabel(Text("Add an item"))
                    }
                    .accessibilityElement(children: .contain)
                } header: {
                    HStack {
                        let done = list.items.filter { Self.done($0, trip) }.count
                        Text("\(list.title) · \(done) of \(list.items.count)").font(.jf(.subheadline, .semibold))
                        Spacer()
                        Menu {
                            Button { name = list.title; renaming = list } label: { Label("Rename", systemImage: "pencil") }
                            Button(role: .destructive) { deleting = list } label: { Label("Delete list", systemImage: "trash") }
                        } label: { Image(systemName: "ellipsis.circle").frame(minWidth: 44, minHeight: 44).contentShape(Rectangle()) }.accessibilityLabel(Text("Edit \(list.title)"))
                    }
                    .textCase(nil)
                }
            }
            Section {
                HStack {
                    TextField("New packing list", text: $newList).submitLabel(.done).onSubmit(create)
                    Button(action: create) { Image(systemName: "plus.circle.fill").font(.title3) }.disabled(newList.trimmingCharacters(in: .whitespaces).isEmpty).accessibilityLabel(Text("Add"))
                }
            }
        }
        .listStyle(.insetGrouped)
        .jfBackground()
        .scrollDismissesKeyboard(.interactively)
        .navigationTitle("Packing lists")
        .sheet(item: $editing) { PackingItemEditor(tripID: tripID, item: $0) }
        .alert(Text("Rename"), isPresented: Binding(get: { renaming != nil }, set: { if !$0 { renaming = nil } })) {
            TextField("Name", text: $name)
            Button("Save") { if let list = renaming { call("rename_packing_list", ["list": .string(TravelRepoBridge.key(list.id)), "title": .string(name)]) } }
            Button("Cancel", role: .cancel) {}
        }
        .confirmationDialog(Text("Delete “\(deleting?.title ?? "")”?"), isPresented: Binding(get: { deleting != nil }, set: { if !$0 { deleting = nil } }), titleVisibility: .visible, presenting: deleting) { list in
            Button("Delete list", role: .destructive) { call("delete_packing_list", ["list": .string(TravelRepoBridge.key(list.id))]) }
        } message: { list in Text("Its \(list.items.count) items go with it. Items that are also on another list stay there.") }
    }

    /// Packed by everyone who brings it.
    static func done(_ i: PackingItem, _ trip: Trip) -> Bool {
        let who = i.everyone ? trip.people.map(\.id) : i.people
        return !who.isEmpty && who.allSatisfy(i.packedBy.contains)
    }

    private func call(_ command: String, _ arguments: [String: JSONValue]) {
        guard let bridge = TravelRepoBridge.all[tripID] else { return }
        Task { await bridge.changeCosts { _ = try await bridge.session.call(command, arguments) } }
    }
    private func create() { call("create_packing_list", ["title": .string(newList.trimmingCharacters(in: .whitespaces))]); newList = "" }

    /// A new item is the user's own; its page changes who brings it.
    private func add(_ list: PackingList, _ me: UUID?, newPeople: Bool = false) {
        guard let bridge = TravelRepoBridge.all[tripID], let text = drafts[list.id] else { return }
        let line = PackingLine(text, people: store.trip(tripID).people)
        guard line.canAdd || (newPeople && !line.title.isEmpty && line.ambiguous.isEmpty) else { return }
        if newPeople {
            let names = line.unknown.reduce(into: [String]()) { if !$0.contains($1) { $0.append($1) } }
            store.update(tripID, String(localized: "Add \(names.joined(separator: ", "))")) { t in t.people += names.map { Person(id: UUID.v7(), name: $0) } }
        }
        drafts[list.id] = ""
        Task {
            await bridge.flush()
            let ready = PackingLine(text, people: store.trip(tripID).people)
            guard ready.canAdd else { drafts[list.id] = text; return }
            var a: [String: JSONValue] = ["list": .string(TravelRepoBridge.key(list.id)), "title": .string(ready.title), "everyone": .bool(ready.everyone)]
            if let g = ready.group { a["group"] = .string(g) }
            if let q = ready.quantity { a["quantity"] = .string(q) }
            let people = ready.people.isEmpty && !ready.everyone ? (me.map { [$0] } ?? []) : ready.people
            if !people.isEmpty, !ready.everyone { a["people"] = .array(people.map { .string(TravelRepoBridge.key($0)) }) }
            if !(await bridge.changeCosts { _ = try await bridge.session.call("add_packing_item", a) }) { drafts[list.id] = text }
        }
    }

    private func row(_ item: PackingItem, _ trip: Trip, _ me: UUID?) -> some View {
        let mine = me.map { item.everyone || item.people.contains($0) } ?? false, nobody = !item.everyone && item.people.isEmpty
        let packed = me.map { item.packedBy.contains($0) } ?? false
        return HStack(spacing: 12) {
            if mine, let me {
                Button { UIImpactFeedbackGenerator(style: .light).impactOccurred(); call("set_packed", ["item": .string(TravelRepoBridge.key(item.id)), "person": .string(TravelRepoBridge.key(me)), "packed": .bool(!packed)]) } label: {
                    Image(systemName: packed ? "checkmark.square.fill" : "square").font(.title3).foregroundStyle(packed ? Color.jfSuccess : Color.jfAccent).frame(minWidth: 44, minHeight: 44).contentShape(Rectangle())
                }
                .buttonStyle(.plain).accessibilityLabel(packed ? Text("Not packed: \(item.title)") : Text("Packed: \(item.title)"))
            } else { Image(systemName: Self.done(item, trip) ? "checkmark.circle" : "circle").foregroundStyle(Self.done(item, trip) ? Color.jfSuccess : Color.jfSubtleText).accessibilityHidden(true) }
            Button { editing = item } label: {
                VStack(alignment: .leading, spacing: 2) {
                    Text(item.title).foregroundStyle(Color.jfText)
                    let who = item.everyone ? String(localized: "Everyone brings their own") : nobody ? String(localized: "Nobody brings it yet") : item.people.compactMap { id in trip.person(id).map { $0.name + (item.packedBy.contains(id) ? " ✓" : "") } }.joined(separator: ", ")
                    Text([item.quantity, who].compactMap { $0 }.joined(separator: " · ")).font(.jf(.footnote)).foregroundStyle(nobody ? Color.jfWarning : Color.jfMuted)
                }
                .frame(maxWidth: .infinity, alignment: .leading).contentShape(Rectangle())
            }
            .buttonStyle(.plain)
            if nobody, let me { Button("I'll take it") { call("assign_packing_item", ["item": .string(TravelRepoBridge.key(item.id)), "people": [.string(TravelRepoBridge.key(me))]]) }.buttonStyle(.bordered).controlSize(.small) }
        }
    }
}

struct PackingItemEditor: View {
    let tripID: UUID
    @State var item: PackingItem
    @Environment(\.dismiss) private var dismiss
    @Environment(TripStore.self) private var store
    @State private var before: PackingItem?
    @State private var list: UUID?
    @State private var startList: UUID?
    @State private var deleting = false
    @State private var busy = false

    var body: some View {
        let trip = store.trips.first { $0.id == tripID }
        NavigationStack {
            JFForm {
                Section {
                    TextField("Title", text: $item.title)
                    TextField("Group", text: $item.group)
                    let used = Array(Set((trip?.packing ?? []).flatMap(\.items).map(\.group).filter { !$0.isEmpty })).sorted()
                    if !used.isEmpty { ScrollView(.horizontal, showsIndicators: false) { HStack { ForEach(used, id: \.self) { g in Button(g) { item.group = g }.buttonStyle(.bordered).controlSize(.small) } } } }
                    TextField("Quantity", text: Binding(get: { item.quantity ?? "" }, set: { item.quantity = $0.isEmpty ? nil : $0 }))
                    if (trip?.packing.count ?? 0) > 1 { Picker("List", selection: $list) { ForEach(trip?.packing ?? []) { Text($0.title).tag(UUID?.some($0.id)) } } }
                }
                Section {
                    Toggle("Everyone brings their own", isOn: $item.everyone)
                    if !item.everyone { PeopleRow(title: "People", people: trip?.people ?? [], chosen: Binding(get: { Set(item.people) }, set: { new in item.people = (trip?.people ?? []).map(\.id).filter(new.contains) })) }
                } header: { Overline("Who brings it") }
                let who = item.everyone ? (trip?.people.map(\.id) ?? []) : item.people
                if !who.isEmpty {
                    Section { PeopleRow(title: "Packed by", people: (trip?.people ?? []).filter { who.contains($0.id) }, chosen: $item.packedBy) }
                }
                Section { Button("Delete", role: .destructive) { deleting = true } }
            }
            .jfBackground()
            .navigationTitle(item.title)
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) { Button(role: .cancel) { dismiss() } }
                ToolbarItem(placement: .confirmationAction) { Button(role: .confirm) { Task { await save() } }.disabled(busy || item.title.trimmingCharacters(in: .whitespaces).isEmpty) }
            }
            .onAppear { if before == nil { before = item; list = trip?.packing.first { $0.items.contains { $0.id == item.id } }?.id; startList = list } }
            .deleteConfirmation(tripID: tripID, ids: deleting ? [item.id] : nil, title: item.title) { deleting = false; if TravelRepoBridge.all[tripID]?.entities[TravelRepoBridge.key(item.id)] == nil { dismiss() } }
        }
    }

    private func save() async {
        guard let bridge = TravelRepoBridge.all[tripID], let before else { return }
        busy = true; defer { busy = false }
        let id = JSONValue.string(TravelRepoBridge.key(item.id))
        let ok = await bridge.changeCosts {
            if item.title != before.title || item.group != before.group || item.quantity != before.quantity || list != startList {
                var a: [String: JSONValue] = ["item": id, "title": .string(item.title), "group": .string(item.group), "quantity": .string(item.quantity ?? "")]
                if list != startList, let list { a["list"] = .string(TravelRepoBridge.key(list)) }
                _ = try await bridge.session.call("update_packing_item", a)
            }
            if item.people != before.people || item.everyone != before.everyone { _ = try await bridge.session.call("assign_packing_item", ["item": id, "people": .array(item.people.map { .string(TravelRepoBridge.key($0)) }), "everyone": .bool(item.everyone)]) }
            for p in item.packedBy.symmetricDifference(before.packedBy) { _ = try await bridge.session.call("set_packed", ["item": id, "person": .string(TravelRepoBridge.key(p)), "packed": .bool(item.packedBy.contains(p))]) }
        }
        if ok { dismiss() }
    }
}

// MARK: Budgets

struct BudgetEditor: View {
    let tripID: UUID
    /// The budget to change, or nil for a new one.
    let budgetID: String?
    /// In Quick Add, which has a navigation of its own.
    var embedded: (() -> Void)? = nil
    @Environment(\.dismiss) private var dismiss
    @Environment(TripStore.self) private var store
    @State private var title = ""
    @State private var amount = ""
    @State private var category = ""
    @State private var person: UUID?
    @State private var collection = ""
    @State private var deleting = false
    @State private var loaded = false
    @State private var busy = false

    var body: some View {
        let trip = store.trips.first { $0.id == tripID }
        if embedded != nil { form(trip) } else { NavigationStack { form(trip) }.presentationDetents([.medium, .large]) }
    }

    private func form(_ trip: Trip?) -> some View {
            JFForm {
                Section { TextField("Title", text: $title); TextField("Amount in \(trip?.currency ?? "")", text: $amount).decimalKeyboard() }
                Section {
                    CategoryField(tripID: tripID, category: $category, empty: "All")
                    Picker("Person", selection: $person) { Text("Everyone").tag(UUID?.none); ForEach(trip?.people ?? []) { Text($0.name).tag(UUID?.some($0.id)) } }
                    let lists = Set((trip?.packing ?? []).map { TravelRepoBridge.key($0.id) })
                    let collections = (TravelRepoBridge.all[tripID]?.entities ?? [:]).filter { $0.value["type"]?.string == "collection" && !lists.contains($0.key) }.sorted { ($0.value["title"]?.string ?? "") < ($1.value["title"]?.string ?? "") }
                    if !collections.isEmpty { Picker("Collection", selection: $collection) { Text("All").tag(""); ForEach(collections, id: \.key) { Text($0.value["title"]?.string ?? "").tag($0.key) } } }
                } footer: { Footnote("Without a limit, it is part of the budget for the whole trip.") }
                if budgetID != nil { Section { Button("Delete", role: .destructive) { deleting = true } } }
            }
            .jfBackground()
            .navigationTitle(budgetID == nil ? "New budget" : "Edit budget")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                if embedded == nil { ToolbarItem(placement: .cancellationAction) { Button(role: .cancel) { dismiss() } } }
                ToolbarItem(placement: .confirmationAction) { Button(role: .confirm) { Task { await save() } }.disabled(busy || title.isEmpty || amount.isEmpty) }
            }
            .onAppear {
                guard !loaded, let budgetID, let d = TravelRepoBridge.all[tripID]?.entities[budgetID] else { loaded = true; return }
                loaded = true; title = d["title"]?.string ?? ""; amount = d["amount"]?["value"]?.string ?? ""; category = d["category"]?.string ?? ""
                person = (d["person"]?.string).flatMap(UUID.init(uuidString:)); collection = d["collection"]?.string ?? ""
            }
            .deleteConfirmation(tripID: tripID, ids: deleting ? budgetID.flatMap(UUID.init(uuidString:)).map { [$0] } : nil, title: title) { deleting = false; if let budgetID, TravelRepoBridge.all[tripID]?.entities[budgetID] == nil { dismiss() } }
    }

    private func save() async {
        guard let bridge = TravelRepoBridge.all[tripID] else { return }
        busy = true; defer { busy = false }
        var a: [String: JSONValue] = ["title": .string(title), "amount": .string(amount)]
        if let budgetID { a["id"] = .string(budgetID) }
        if !collection.isEmpty { a["collection"] = .string(collection) }
        if !category.isEmpty { a["category"] = .string(category) }
        if let person { a["person"] = .string(TravelRepoBridge.key(person)) }
        if await bridge.changeCosts({ _ = try await bridge.session.call("set_budget", a) }) { if let embedded { embedded() } else { dismiss() } }
    }
}
