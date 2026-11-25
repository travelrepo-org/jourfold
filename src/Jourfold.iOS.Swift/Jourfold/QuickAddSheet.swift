import TravelRepoKit
import PhotosUI
import SwiftUI

enum QuickAddKind: String, Hashable, CaseIterable, Identifiable {
    case item, transport, stay, expense, booking, task, packing, note, document, budget
    var id: String { rawValue }
    var label: LocalizedStringResource {
        switch self {
        case .item: "Plan"
        case .transport: "Journey"
        case .stay: "Stay"
        case .expense: "Expense"
        case .booking: "Booking"
        case .task: "Task"
        case .packing: "Packing item"
        case .note: "Note"
        case .document: "File or scan"
        case .budget: "Budget"
        }
    }
    var symbol: String {
        switch self {
        case .item: "calendar.badge.plus"
        case .transport: "tram.fill"
        case .stay: "bed.double.fill"
        case .expense: "creditcard.fill"
        case .booking: "ticket.fill"
        case .task: "checklist"
        case .packing: "backpack.fill"
        case .note: "note.text"
        case .document: "doc.viewfinder"
        case .budget: "chart.bar.fill"
        }
    }
    var tint: Kind {
        switch self {
        case .item: .sightseeing
        case .transport: .transport
        case .stay: .accommodation
        case .expense: .food
        case .booking: .nightlife
        case .task: .meeting
        case .packing: .nature
        case .note: .other
        case .document: .culture
        case .budget: .shopping
        }
    }
}

/// The centre "+": choose what to add, then a short form for it.
struct QuickAddSheet: View {
    let tripID: UUID
    @Environment(AppNavigation.self) private var nav
    @Environment(\.dismiss) private var dismiss
    @State private var path: [QuickAddKind] = []
    @Environment(\.dynamicTypeSize) private var size
    @State private var detent: PresentationDetent = .medium

    var body: some View {
        NavigationStack(path: $path) {
            ScrollView {
                LazyVGrid(columns: [GridItem(.adaptive(minimum: size.isAccessibilitySize ? 300 : 150), spacing: 12)], spacing: 12) {
                    ForEach(nav.quickAddInto == nil ? QuickAddKind.allCases : [.item, .transport]) { k in
                        NavigationLink(value: k) {
                            HStack(spacing: 10) {
                                Image(systemName: k.symbol).font(.system(size: 16, weight: .semibold))
                                    .foregroundStyle(k.tint.colors.fg)
                                    .frame(width: 34, height: 34)
                                    .background(k.tint.colors.bg, in: RoundedRectangle(cornerRadius: 9, style: .continuous))
                                Text(k.label).font(.jf(.callout, .semibold)).foregroundStyle(Color.jfText).multilineTextAlignment(.leading)
                                    .fixedSize(horizontal: false, vertical: true)
                                Spacer(minLength: 0)
                            }
                            .frame(maxWidth: .infinity, minHeight: 44, alignment: .leading)
                            .padding(10)
                            .background(Color.jfSurface, in: RoundedRectangle(cornerRadius: 14, style: .continuous))
                            .overlay(RoundedRectangle(cornerRadius: 14, style: .continuous).strokeBorder(Color.jfLine))
                        }
                        .buttonStyle(.plain)
                    }
                }
                .padding(16)
            }
            .background(Color.jfApp.ignoresSafeArea())
            .navigationTitle("Add to trip")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar { ToolbarItem(placement: .topBarLeading) { Button(role: .close) { dismiss() } } }
            .navigationDestination(for: QuickAddKind.self) { k in
                Group {
                    switch k {
                    case .item: PlanEditor(tripID: tripID, shape: .activity, into: nav.quickAddInto, done: { dismiss() })
                    case .transport: PlanEditor(tripID: tripID, shape: .journey, into: nav.quickAddInto, done: { dismiss() })
                    case .stay: PlanEditor(tripID: tripID, shape: .stay, done: { dismiss() })
                    default: QuickAddForm(tripID: tripID, kind: k) { dismiss() }
                    }
                }
                .onAppear { detent = .large }
            }
        }
        .presentationDetents([.medium, .large], selection: $detent)
        .onAppear {
            if let k = nav.quickAddKind { path = [k]; detent = .large }
        }
    }
}

/// Currencies people reach for on this trip: the trip's, the local one and the ones used lately.
enum RecentCurrencies {
    static let key = "converter.recent"
    static var chosen: [String] { (UserDefaults.standard.string(forKey: key) ?? "").split(separator: ",").map(String.init) }
    /// Keeps a currency the trip does not offer by itself; the latest four of them.
    static func remember(_ code: String, _ trip: Trip) {
        guard code.count == 3, !own(trip).contains(code) else { return }
        UserDefaults.standard.set(([code] + chosen.filter { $0 != code }).prefix(4).joined(separator: ","), forKey: key)
    }
    static func list(_ trip: Trip) -> [String] {
        var out = own(trip)
        for c in chosen + ["USD", "NZD", "CAD"] where !out.contains(c) { out.append(c) }
        return out
    }
    private static func own(_ trip: Trip) -> [String] {
        var out = [trip.currency]
        // The currency of the trip's main zone, and those its costs are in.
        if let region = ["Asia/Tokyo": "JPY", "Pacific/Auckland": "NZD", "America/Toronto": "CAD", "America/Vancouver": "CAD", "America/St_Johns": "CAD"][trip.zone], !out.contains(region) { out.append(region) }
        if let own = Locale.current.currency?.identifier, !out.contains(own) { out.append(own) }
        for e in trip.expenses.reversed() where !out.contains(e.amount.currency) { out.append(e.amount.currency) }
        return out
    }
}

struct QuickAddForm: View {
    let tripID: UUID
    let kind: QuickAddKind
    let done: () -> Void
    @Environment(TripStore.self) private var store
    @State private var title = ""
    @State private var amount = ""
    @State private var currency = ""
    @State private var otherCurrency = ""
    @State private var planned = false
    @State private var hasDay = true
    @State private var day = Date()
    @State private var category = ""
    @State private var payers: Set<UUID> = []
    @State private var people: Set<UUID> = []
    @State private var chosen = false
    @State private var method = "equal"
    @State private var values: [UUID: String] = [:]
    @State private var belongsTo = ""
    @State private var charged = ""
    @State private var withCash = false
    @State private var cash = CashRate()
    @State private var provider = ""
    @State private var reference = ""
    @State private var status = "confirmed"
    @State private var freeCancellation = false
    @State private var linked: Set<UUID> = []
    @State private var group = ""
    @State private var quantity = ""
    @State private var list: UUID?
    @State private var everyone = false
    @State private var loaded = false
    @State private var busy = false
    @State private var receipt: Data?
    @State private var receiptPhoto: PhotosPickerItem?
    @FocusState private var focused: Bool

    var body: some View {
        let trip = store.trips.first { $0.id == tripID }
        Group {
            if kind == .budget { BudgetEditor(tripID: tripID, budgetID: nil, embedded: done) }
            else if let trip { form(trip) }
        }
    }

    private func form(_ trip: Trip) -> some View {
        JFForm {
            switch kind {
            case .expense: expenseFields(trip)
            case .booking: bookingFields(trip)
            case .task: taskFields(trip)
            case .packing: packingFields(trip)
            case .note: Section { TextField("Write a note for the trip", text: $title, axis: .vertical).lineLimit(5...12).focused($focused) }
            default: DocumentAddSection(tripID: tripID, done: done)
            }
        }
        .jfBackground()
        .environment(\.timeZone, trip.calendar.timeZone)
        .scrollDismissesKeyboard(.interactively)
        .navigationTitle(String(localized: kind.label))
        .navigationBarTitleDisplayMode(.inline)
        .toolbar { if kind != .document { ToolbarItem(placement: .confirmationAction) { Button(role: .confirm) { Task { await add(trip) } }.disabled(busy || !canAdd) } } }
        .onAppear {
            guard !loaded else { return }
            loaded = true; focused = !DebugTools.flag("prefill")
            currency = trip.currency; list = trip.packing.first?.id
            if let me = store.me[tripID] { payers = [me]; if kind == .packing || kind == .task { people = [me] } }
            if kind == .expense || kind == .booking { people = Set(trip.people.map(\.id)) }
            day = store.now
        }
    }

    private var value: Decimal? { Decimal(string: amount.replacingOccurrences(of: ",", with: "."), locale: Locale(identifier: "en_US_POSIX")) }
    private var code: String { let o = otherCurrency.uppercased(); return o.count == 3 ? o : currency }
    private var canAdd: Bool {
        guard !title.trimmingCharacters(in: .whitespaces).isEmpty else { return false }
        return kind != .expense || (value != nil && !people.isEmpty && splitComplete && (!withCash || cash.complete))
    }
    private func numbers() -> [Decimal]? {
        let list = people.map { values[$0].flatMap { Decimal(string: $0.replacingOccurrences(of: ",", with: "."), locale: Locale(identifier: "en_US_POSIX")) } }
        return list.contains { $0 == nil } ? nil : list.compactMap { $0 }
    }
    private var splitComplete: Bool {
        guard method != "equal" else { return true }
        guard let n = numbers() else { return false }
        return method == "amount" ? n.reduce(0, +) == (value ?? -1) : method == "percent" ? n.reduce(0, +) == 100 : n.contains { $0 > 0 }
    }
    /// What is still to divide, or too much.
    private var rest: String? {
        guard method == "amount" || method == "percent", let n = numbers(), let target = method == "percent" ? 100 : value else { return nil }
        let sum = n.reduce(0, +), left = target - sum
        func f(_ x: Decimal) -> String { method == "percent" ? x.formatted() + " %" : Fmt.money(x, code) }
        return left > 0 ? String(localized: "\(f(sum)) of \(f(target)), \(f(left)) left") : String(localized: "\(f(sum)) of \(f(target)), \(f(-left)) too much")
    }
    private func iso(_ d: Date, _ trip: Trip) -> String { let c = trip.calendar.dateComponents([.year, .month, .day], from: d); return String(format: "%04d-%02d-%02d", c.year!, c.month!, c.day!) }
    private func ids(_ set: Set<UUID>, _ trip: Trip) -> JSONValue { .array(trip.people.filter { set.contains($0.id) }.map { .string(TravelRepoBridge.key($0.id)) }) }
    private func toggles(_ trip: Trip, _ set: Binding<Set<UUID>>) -> some View {
        ForEach(trip.people) { p in Toggle(isOn: Binding(get: { set.wrappedValue.contains(p.id) }, set: { if $0 { set.wrappedValue.insert(p.id) } else { set.wrappedValue.remove(p.id) } })) { HStack { PersonAvatar(person: p, size: 24).accessibilityHidden(true); Text(p.name) } } }
    }

    @ViewBuilder private func expenseFields(_ trip: Trip) -> some View {
        Section {
            TextField("Amount", text: $amount).decimalKeyboard().font(.jf(.title2, .semibold)).focused($focused)
            Picker("Currency", selection: $currency) { ForEach(RecentCurrencies.list(trip), id: \.self) { Text($0) } }
            TextField("Other currency, such as CAD", text: $otherCurrency).textInputAutocapitalization(.characters).autocorrectionDisabled()
            TextField("What for?", text: $title)
            CategoryField(tripID: tripID, category: $category, empty: "From the plan or title")
        }
        Section {
            Toggle("Still to pay", isOn: $planned.animation())
            Toggle(planned ? "Due date" : "Day", isOn: $hasDay)
            if hasDay { DatePicker(planned ? "Due" : "Paid on", selection: $day, displayedComponents: .date) }
            if !planned, code != trip.currency {
                Toggle("Paid with cash", isOn: $withCash.animation())
                if withCash { CashFields(currency: code, tripCurrency: trip.currency, cash: $cash) }
                else { TextField("Charged in \(trip.currency), if you know it", text: $charged).decimalKeyboard() }
            }
        }
        Section { toggles(trip, $payers) } header: { Overline("Paid by") }
        Section {
            toggles(trip, $people)
            Picker("Method", selection: $method.converting($values, people: trip.people.map(\.id).filter(people.contains), total: value, code: code)) { Text("Equal").tag("equal"); Text("Amounts").tag("amount"); Text("Shares").tag("share"); Text("Percent").tag("percent") }
            if method != "equal" {
                ForEach(trip.people.filter { people.contains($0.id) }) { p in
                    HStack { Text(p.name); Spacer(); TextField("0", text: Binding(get: { values[p.id] ?? "" }, set: { values[p.id] = $0 })).decimalKeyboard().multilineTextAlignment(.trailing).frame(maxWidth: 110) }
                }
                if !splitComplete { Label("The split does not add up yet.", systemImage: "exclamationmark.triangle").font(.jf(.footnote)).foregroundStyle(Color.jfWarning); if let rest { Text(rest).font(.jf(.footnote).monospacedDigit()).foregroundStyle(Color.jfMuted) } }
            }
        } header: { Overline("Shared by") }
        Section {
            Picker("Belongs to", selection: $belongsTo) {
                Text("Nothing").tag("")
                Section("Bookings") { ForEach(trip.bookings) { Text($0.title).tag(TravelRepoBridge.key($0.id)) } }
                Section("Plans") { ForEach(trip.items) { Text($0.title).tag(TravelRepoBridge.key($0.id)) } }
            }
            .pickerStyle(.navigationLink)
        }
        Section {
            PhotosPicker(selection: $receiptPhoto, matching: .images) { Label(receipt == nil ? "Add a photo of the receipt" : "Receipt added", systemImage: receipt == nil ? "photo" : "checkmark") }
                .onChange(of: receiptPhoto) { _, item in
                    guard let item else { return }
                    Task {
                        receipt = (try? await item.loadTransferable(type: Data.self)).flatMap { UIImage(data: $0)?.jpegData(compressionQuality: 0.8) }
                        if receipt == nil { store.problem = String(localized: "The photo could not be read.") }
                    }
                }
            if receipt == nil { CameraButton(title: "Photograph the receipt") { receipt = $0 } }
            if receipt != nil { Button("Remove the receipt", role: .destructive) { receipt = nil; receiptPhoto = nil } }
        }
    }

    @ViewBuilder private func bookingFields(_ trip: Trip) -> some View {
        Section {
            TextField("What is booked?", text: $title).focused($focused)
            TextField("Provider", text: $provider)
            TextField("Booking reference", text: $reference).textInputAutocapitalization(.characters).autocorrectionDisabled()
            Picker("Status", selection: $status) { ForEach(["idea", "pending", "reserved", "confirmed", "cancelled"], id: \.self) { Text(BookingsView.status($0)).tag($0) } }
        }
        Section {
            TextField("Amount still to pay", text: $amount).decimalKeyboard()
            if !amount.isEmpty { Picker("Currency", selection: $currency) { ForEach(RecentCurrencies.list(trip), id: \.self) { Text($0) } } }
            OptionalDateRows(add: "Add a free cancellation deadline", title: "Free cancellation until", remove: "Remove the deadline", has: $freeCancellation, date: $day)
        } footer: { Footnote("A booking has no price of its own; what it costs are its payments.") }
        Section { PeopleRow(title: "Travellers", people: trip.people, chosen: $people) }
        Section { PlanRows(trip: trip, chosen: $linked) } header: { Overline("Plans it covers") }
    }

    @ViewBuilder private func taskFields(_ trip: Trip) -> some View {
        Section {
            TextField("What needs doing?", text: $title).focused($focused)
            OptionalDateRows(add: "Add a due date", title: "Due", remove: "Remove the due date", has: $hasDay, date: $day, parts: .date)
        }
        Section { PeopleRow(title: "For", people: trip.people, chosen: $people) }
    }

    @ViewBuilder private func packingFields(_ trip: Trip) -> some View {
        Section {
            TextField("What to pack?", text: $title).focused($focused)
            TextField("Group", text: $group)
            TextField("Quantity", text: $quantity)
            if trip.packing.count > 1 { Picker("List", selection: $list) { ForEach(trip.packing) { Text($0.title).tag(UUID?.some($0.id)) } } }
        }
        Section { Toggle("Everyone brings their own", isOn: $everyone); if !everyone { PeopleRow(title: "People", people: trip.people, chosen: $people) } } header: { Overline("Who brings it") }
    }

    private func add(_ trip: Trip) async {
        guard let bridge = TravelRepoBridge.all[tripID] else { return }
        busy = true; defer { busy = false }
        let name = title.trimmingCharacters(in: .whitespaces)
        if kind == .note {
            store.update(tripID, "Added a note") { t in t.notes += (t.notes.isEmpty ? "" : "\n\n") + name }
            await bridge.flush(); done(); return
        }
        var created = false
        let ok = await bridge.changeCosts {
            switch kind {
            case .expense:
                var a: [String: JSONValue] = ["title": .string(name), "amount": .string(amount), "currency": .string(code), "estimated": .bool(planned), "paid_by": ids(payers, trip)]
                if !belongsTo.isEmpty { a["belongs_to"] = .string(belongsTo) }
                if !category.isEmpty { a["category"] = .string(category) }
                if hasDay { a[planned ? "due" : "date"] = .string(iso(day, trip)) }
                let r = try await bridge.session.call("add_expense", a)
                created = true
                guard let id = r["id"]?.string ?? r["entity"]?["id"]?.string else { return }
                // Equal for everyone is what TravelRepo does by itself, with the booking's or plan's people.
                if method != "equal" || people != Set(trip.people.map(\.id)) {
                    let who = trip.people.filter { people.contains($0.id) }
                    var s: [String: JSONValue] = ["expense": .string(id), "method": .string(method), "people": ids(people, trip)]
                    if method != "equal" { s["values"] = .array(who.map { .string(values[$0.id] ?? "0") }) }
                    _ = try await bridge.session.call("split_expense", s)
                }
                if let receipt { _ = try await bridge.session.importDocument(receipt, name: String(localized: "Receipt") + ".jpg", mediaType: "image/jpeg", attachTo: id) }
                if !planned, code != trip.currency, withCash { _ = try await bridge.session.call("set_cash", cash.arguments(for: id)) }
                else if !planned, code != trip.currency, !charged.isEmpty { _ = try await bridge.session.call("set_charged", ["expense": .string(id), "amount": .string(charged), "currency": .string(trip.currency)]) }
            case .booking:
                var d: [String: JSONValue] = ["title": .string(name), "status": .string(status), "travelers": ids(people, trip)]
                if !provider.isEmpty { d["provider"] = ["name": .string(provider)] }
                if !reference.isEmpty { d["reference"] = .string(reference) }
                if freeCancellation { d["cancellation"] = ["free_until": TravelRepoBridge.zoned(day, trip.zone)] }
                let r = try await bridge.session.call("create_entity", ["type": "booking", "data": .object(d)])
                created = true
                guard let id = r["id"]?.string ?? r["entity"]?["id"]?.string else { return }
                if !linked.isEmpty { _ = try await bridge.session.call("link_booking", ["booking": .string(id), "items": .array(linked.map { .string(TravelRepoBridge.key($0)) })]) }
                if !amount.isEmpty { _ = try await bridge.session.call("add_expense", ["title": .string(name), "amount": .string(amount), "currency": .string(currency), "estimated": true, "belongs_to": .string(id)]) }
            case .task:
                var d: [String: JSONValue] = ["title": .string(name), "status": "open", "assigned_to": ids(people, trip)]
                if hasDay { d["due"] = ["date": .string(iso(day, trip))] }
                try await bridge.create("task", d)
            case .packing:
                var target = list.map(TravelRepoBridge.key)
                if target == nil { target = try await bridge.session.call("create_packing_list", ["title": .string(String(localized: "Packing list"))])["id"]?.string }
                guard let target else { return }
                var a: [String: JSONValue] = ["list": .string(target), "title": .string(name), "everyone": .bool(everyone), "people": everyone ? [] : ids(people, trip)]
                if !group.isEmpty { a["group"] = .string(group) }
                if !quantity.isEmpty { a["quantity"] = .string(quantity) }
                _ = try await bridge.session.call("add_packing_item", a)
            default: break
            }
        }
        // A part that failed after the entry was made is shown; adding again would make it twice.
        if ok || created { UINotificationFeedbackGenerator().notificationOccurred(ok ? .success : .warning); done() }
    }
}
