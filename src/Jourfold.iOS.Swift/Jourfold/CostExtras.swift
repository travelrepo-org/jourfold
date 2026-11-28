import PhotosUI
import SwiftUI
import TravelRepoKit
import UniformTypeIdentifiers

/// What choosing some of the open items comes to in the trip's currency. TravelRepo works it out: the trip is copied
/// to a temporary folder, everything that was not chosen is marked as paid back there, and `settle_up` of the copy is
/// the answer. The copy never leaves the folder and is removed with the sheet.
final class PayBackCalculator {
    private let folder: URL
    private let session: TravelRepoSession
    private var before: [String: [String: JSONValue]] = [:]
    private var touched: Set<String> = []

    init(_ bridge: TravelRepoBridge) async throws {
        folder = FileManager.default.temporaryDirectory.appendingPathComponent("PayBack-\(UUID().uuidString)", isDirectory: true)
        let copy = folder.appendingPathComponent("trip", isDirectory: true)
        do {
            try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
            try await bridge.session.copyTrip(to: copy)
            session = try await .open(root: copy, recoveryRoot: folder.appendingPathComponent("recovery"), exchangeRates: TripLibrary.rates)
            for file in try await session.snapshot().entities {
                if let data = file.data.object, data["type"]?.string == "expense", let id = data["id"]?.string { before[id] = data }
            }
        } catch { try? FileManager.default.removeItem(at: folder); throw error }
    }

    deinit { try? FileManager.default.removeItem(at: folder) }

    /// `settled` are the items that were not chosen: an expense and who owes for it.
    func transfers(without settled: [(expense: String, debtor: UUID)], currency: String) async throws -> [CostDetails.Transfer] {
        if !touched.isEmpty {
            let edits = touched.reduce(into: [String: [String: JSONValue]?]()) { $0[$1] = before[$1] }
            try await session.apply(revision: try await session.revision(), edits: edits)
            touched = []
        }
        for item in settled {
            touched.insert(item.expense)
            _ = try await session.call("mark_paid", ["expense": .string(item.expense), "person": .string(TravelRepoBridge.key(item.debtor)), "paid": true])
        }
        return try await session.call("get_costs", ["in": .string(currency)], as: CostDetails.self).settle_up
    }
}

// MARK: Cash

/// Records that a payment was made with cash bought before: the rate of the cash machine, or what was received and
/// what the account was charged for it. TravelRepo's `set_cash` then counts it exactly.
struct CashFields: View {
    let currency: String, tripCurrency: String
    @Binding var cash: CashRate
    var body: some View {
        Picker("Rate", selection: $cash.way) {
            Text("The last one known").tag(CashRate.Way.known)
            Text("Per unit").tag(CashRate.Way.perUnit)
            Text("Received and charged").tag(CashRate.Way.machine)
        }
        switch cash.way {
        case .known: Text("The rate of the cash bought last for this trip, otherwise today's exchange rate.").font(.jf(.footnote)).foregroundStyle(Color.jfMuted)
        case .perUnit: TextField("\(currency) for 1 \(tripCurrency)", text: $cash.perUnit).decimalKeyboard()
        case .machine:
            TextField("Cash received in \(currency)", text: $cash.received).decimalKeyboard()
            TextField("Charged for it in \(tripCurrency), with fees", text: $cash.debited).decimalKeyboard()
        }
    }
}

struct CashRate: Equatable {
    enum Way: Hashable { case known, perUnit, machine }
    var way = Way.known, perUnit = "", received = "", debited = ""
    var complete: Bool { way == .known || (way == .perUnit ? !perUnit.isEmpty : !received.isEmpty && !debited.isEmpty) }
    func arguments(for expense: String) -> [String: JSONValue] {
        var a: [String: JSONValue] = ["expense": .string(expense)]
        switch way {
        case .known: break
        case .perUnit: a["per_unit"] = .string(perUnit)
        case .machine: a["received"] = .string(received); a["debited"] = .string(debited)
        }
        return a
    }
}

struct CashSheet: View {
    let tripID: UUID
    let expense: CostDetails.Expense
    @Environment(\.dismiss) private var dismiss
    @Environment(TripStore.self) private var store
    @State private var cash = CashRate()
    @State private var busy = false

    var body: some View {
        let tripCurrency = store.trips.first { $0.id == tripID }?.currency ?? "EUR"
        NavigationStack {
            JFForm {
                Section { LabeledContent("Amount", value: Fmt.money(expense.value, expense.code)); CashFields(currency: expense.code, tripCurrency: tripCurrency, cash: $cash) }
                    footer: { Footnote("No card will charge this payment, so it counts at the rate the cash cost.") }
            }
            .jfBackground()
            .navigationTitle("Paid with cash")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) { Button(role: .cancel) { dismiss() } }
                ToolbarItem(placement: .confirmationAction) { Button(role: .confirm) { Task { await save() } }.disabled(busy || !cash.complete) }
            }
        }
        .presentationDetents([.medium, .large])
    }

    private func save() async {
        guard let bridge = TravelRepoBridge.all[tripID] else { return }
        busy = true; defer { busy = false }
        if await bridge.changeCosts({ _ = try await bridge.session.call("set_cash", cash.arguments(for: expense.id)) }) { dismiss() }
    }
}

// MARK: Categories

/// A cost category: the standard ones, the trip's own ones, or a new one typed in.
struct CategoryField: View {
    let tripID: UUID
    @Binding var category: String
    /// What an empty category means here.
    var empty: LocalizedStringKey? = nil
    @State private var own: [String] = []
    @State private var typing = false
    @State private var text = ""
    @Environment(TripStore.self) private var store

    var body: some View {
        let extra = own + (category.isEmpty || own.contains(category) || CostsView.category(category) != nil ? [] : [category])
        Picker("Category", selection: Binding(get: { typing ? "\u{1}" : category }, set: { new in
            typing = new == "\u{1}"; if !typing { category = new } else { text = "" }
        })) {
            if let empty { Text(empty).tag("") }
            ForEach(TravelRepoBridge.categories, id: \.1) { c, key in Label { Text(c.label) } icon: { Image(systemName: c.symbol) }.tag(key) }
            ForEach(extra, id: \.self) { Label($0, systemImage: "tag.fill").tag($0) }
            Label("Own category…", systemImage: "plus").tag("\u{1}")
        }
        .task(id: store.travelRepoReads) {
            own = ((try? await TravelRepoBridge.all[tripID]?.session.call("vocabulary"))?["custom_expense_categories"]?.array ?? []).compactMap(\.string)
        }
        if typing {
            TextField("Name of the category", text: $text).onChange(of: text) { _, t in category = t.trimmingCharacters(in: .whitespaces) }
        }
    }
}

// MARK: Receipts

/// The receipts of a payment: photos and files in the trip, attached to the payment.
struct ReceiptSection: View {
    let tripID: UUID
    let expense: UUID
    @Environment(TripStore.self) private var store
    @State private var photo: PhotosPickerItem?
    @State private var importing = false
    @State private var open: Doc?
    @State private var busy = false
    struct Doc: Identifiable { let id: UUID, name: String }

    static func documents(_ tripID: UUID, _ expense: UUID) -> [Doc] {
        guard let bridge = TravelRepoBridge.all[tripID] else { return [] }
        return (bridge.entities[TravelRepoBridge.key(expense)]?["content"]?.array ?? []).compactMap { c in
            guard let key = c["document"]?.string ?? c["entity"]?.string, let id = UUID(uuidString: key), let d = bridge.entities[key], d["type"]?.string == "document" else { return nil }
            return Doc(id: id, name: d["name"]?.string ?? String(localized: "Receipt"))
        }
    }

    var body: some View {
        let _ = store.travelRepoReads
        Section {
            ForEach(Self.documents(tripID, expense)) { d in
                Button { open = d } label: { Label(d.name, systemImage: "doc.text") }
                    .swipeActions { Button(role: .destructive) { Task { await TravelRepoBridge.all[tripID]?.detach(d.id, from: expense) } } label: { Label("Remove", systemImage: "minus.circle") } }
            }
            PhotosPicker(selection: $photo, matching: .images) { Label("Add a photo", systemImage: "photo") }.disabled(busy)
                .onChange(of: photo) { _, item in
                    guard let item else { return }
                    photo = nil
                    Task {
                        busy = true; defer { busy = false }
                        guard let data = try? await item.loadTransferable(type: Data.self), let jpeg = UIImage(data: data)?.jpegData(compressionQuality: 0.8) else {
                            store.problem = String(localized: "The photo could not be read."); return
                        }
                        do { try await TravelRepoBridge.all[tripID]?.importFile(jpeg, name: String(localized: "Receipt") + ".jpg", mediaType: "image/jpeg", attachTo: expense) }
                        catch { store.problem = TravelRepoBridge.message(error) }
                    }
                }
            CameraButton(title: "Take a photo") { data in
                Task {
                    busy = true; defer { busy = false }
                    do { try await TravelRepoBridge.all[tripID]?.importFile(data, name: String(localized: "Receipt") + ".jpg", mediaType: "image/jpeg", attachTo: expense) }
                    catch { store.problem = TravelRepoBridge.message(error) }
                }
            }
            Button { importing = true } label: { Label("Choose a file", systemImage: "folder") }.disabled(busy)
                .importFile(isPresented: $importing, tripID: tripID, plan: expense)
                .sheet(item: $open) { DocumentView(tripID: tripID, document: $0.id, name: $0.name) }
        } header: { Overline("Receipts") }
    }
}

// MARK: Costs in an older form

/// Offers to bring costs that older versions saved up to date, naming what changes. TravelRepo works out the changes.
struct CostUpgradeSection: View {
    let tripID: UUID
    @Environment(TripStore.self) private var store
    /// Loaded by the costs page: an empty part of a list never runs a task of its own.
    let changes: [Change]
    @State private var busy = false

    nonisolated struct Change: Decodable {
        nonisolated struct Sum: Decodable { let amount: Decimal?; let value: Decimal?; let currency: String
            init(from decoder: Decoder) throws {
                let c = try decoder.container(keyedBy: Key.self)
                func number(_ k: Key) -> Decimal? { (try? c.decode(String.self, forKey: k)).flatMap { Decimal(string: $0, locale: Locale(identifier: "en_US_POSIX")) } ?? (try? c.decode(Decimal.self, forKey: k)) }
                amount = number(.amount); value = number(.value); currency = try c.decode(String.self, forKey: .currency)
            }
            enum Key: CodingKey { case amount, value, currency }
        }
        let kind: String, title: String
        let amount: Sum?, other: Sum?
    }
    private nonisolated struct Preview: Decodable { let needed: Bool; let changes: [Change] }

    static func load(_ tripID: UUID) async -> [Change] {
        let p = try? await TravelRepoBridge.all[tripID]?.session.call("cost_upgrade_preview", fallback, as: Preview.self)
        return p?.needed == true ? p!.changes : []
    }

    /// The currency for a trip that has none yet: that of the phone's region.
    private static var fallback: [String: JSONValue] { Locale.current.currency.map { ["fallback_currency": .string($0.identifier)] } ?? [:] }

    static func lines(_ changes: [Change]) -> [String] {
        func money(_ s: Change.Sum?) -> String { s.map { Fmt.money($0.amount ?? $0.value ?? 0, $0.currency) } ?? "" }
        func count(_ kind: String) -> Int { changes.filter { $0.kind == kind }.count }
        var lines = changes.filter { $0.kind == "trip_currency" }.prefix(1).map { String(localized: "The trip counts in \($0.title). You can change that in the trip's details.") }
        if count("shares_named") > 0 { lines.append(String(localized: "Expenses that name the travellers who share them, so someone who joins later does not share them by accident: \(count("shares_named"))")) }
        if count("price_covered") > 0 { lines.append(String(localized: "Booking prices that are covered by their payments and removed: \(count("price_covered"))")) }
        if count("budget_scope") > 0 { lines.append(String(localized: "Budgets that lose a setting that only repeated their limits: \(count("budget_scope"))")) }
        for c in changes {
            switch c.kind {
            case "price_to_planned": lines.append(String(localized: "\(c.title): the price of \(money(c.amount)) becomes a payment still to pay."))
            case "price_rest_planned": lines.append(String(localized: "\(c.title): \(money(c.other)) is paid; the rest, \(money(c.amount)), becomes a payment still to pay."))
            case "price_exceeded": lines.append(String(localized: "\(c.title): the payments of \(money(c.other)) are more than the price of \(money(c.amount)), for example with fees; the price is removed."))
            default: break
            }
        }
        for c in changes where c.kind == "price_kept" { lines.append(String(localized: "\(c.title): the price of \(money(c.amount)) stays, because its payments are in another currency. Look at it in the booking.")) }
        return lines
    }

    var body: some View {
        Group {
            if !changes.isEmpty {
                Section {
                    Text("This trip has costs in the form older versions of Jourfold saved. Jourfold can bring them up to date. Nothing that was paid, owed or paid back changes:").font(.jf(.footnote))
                    ForEach(Self.lines(changes), id: \.self) { Text(verbatim: "• \($0)").font(.jf(.footnote)).foregroundStyle(Color.jfMuted) }
                    Button { Task { await apply() } } label: { Label("Update costs", systemImage: "arrow.up.circle") }.disabled(busy)
                } header: { Overline("Update the costs of this trip?") }
            }
        }
    }

    private func apply() async {
        guard let bridge = TravelRepoBridge.all[tripID] else { return }
        busy = true; defer { busy = false }
        var a = Self.fallback; a["message"] = .string(String(localized: "Costs brought up to date"))
        await bridge.changeCosts { _ = try await bridge.session.call("cost_upgrade_apply", a) }
    }
}
