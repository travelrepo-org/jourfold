import SwiftUI
import TravelRepoKit

/// What there is to know about a payment: amount and day, who paid, its category and notes, what the card charged,
/// everyone's part, and who owes whom for it with a switch per person to mark it as paid back.
struct ExpenseSheet: View {
    let tripID: UUID
    let expenseID: String
    @Environment(\.dismiss) private var dismiss
    @Environment(TripStore.self) private var store
    @State private var expense: CostDetails.Expense?
    @State private var editing = false
    @State private var deleting = false
    @State private var charging = false
    @State private var cashing = false
    @State private var charge = ""
    @State private var busy = false

    var body: some View {
        NavigationStack {
            JFList {
                if let e = expense, let trip = store.trips.first(where: { $0.id == tripID }) { content(e, trip) } else { ProgressView() }
            }
            .listStyle(.insetGrouped)
            .jfBackground()
            .navigationTitle(expense?.title ?? "")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .topBarLeading) { Button(role: .close) { dismiss() } }
                ToolbarItem(placement: .topBarTrailing) { Button { editing = true } label: { Image(systemName: "pencil") }.accessibilityLabel(Text("Edit")).disabled(expense == nil) }
                ToolbarItem(placement: .topBarTrailing) { Button(role: .destructive) { deleting = true } label: { Image(systemName: "trash") }.accessibilityLabel(Text("Delete")) }
            }
            .sheet(isPresented: $cashing) { if let expense { CashSheet(tripID: tripID, expense: expense) } }
            .sheet(isPresented: $editing) { if let expense { ExpenseEditor(tripID: tripID, expense: expense) } }
            .deleteConfirmation(tripID: tripID, ids: deleting ? UUID(uuidString: expenseID).map { [$0] } : nil, title: expense?.title) {
                deleting = false
                if TravelRepoBridge.all[tripID]?.entities[expenseID] == nil { dismiss() }
            }
            .alert(Text("What was charged?"), isPresented: $charging) {
                TextField("Amount", text: $charge).decimalKeyboard()
                Button("Save") { Task { await saveCharge() } }
                Button("Cancel", role: .cancel) {}
            } message: { Text("In \(store.trips.first { $0.id == tripID }?.currency ?? ""), as on the card statement. Empty removes it.") }
        }
        .presentationDetents([.medium, .large])
        .task(id: store.travelRepoReads) { await load() }
    }

    private func load() async { expense = try? await TravelRepoBridge.all[tripID]?.costDetails().expenses.first { $0.id == expenseID } }

    @ViewBuilder
    private func content(_ e: CostDetails.Expense, _ trip: Trip) -> some View {
        let entity = TravelRepoBridge.all[tripID]?.entities[expenseID]
        let payers = (entity?["paid_by"]?.array ?? []).compactMap { trip.person($0.string.flatMap(UUID.init(uuidString:)))?.name }
        Section {
            LabeledContent("Amount") { Text(Fmt.money(e.value, e.code)).font(.jf(.headline).monospacedDigit()).foregroundStyle(Color.jfText) }
            if e.planned { LabeledContent("To pay") { Text(e.idea ? String(localized: "Idea, not counted") : e.due.map { String(localized: "due \(CostsView.day($0))") } ?? "–") } }
            else if let date = e.date { LabeledContent("Paid on", value: CostsView.day(date, weekday: true)) }
            LabeledContent("Paid by", value: payers.isEmpty ? "–" : payers.joined(separator: ", "))
            LabeledContent("Category", value: CostsView.categoryName(e.category))
            if !e.belongs_to.isEmpty { LabeledContent("Belongs to", value: e.belongs_to.joined(separator: ", ")) }
            if let note = entity?["body"]?.string, !note.isEmpty { Text(note) }
            if e.planned { Button { Task { await change { bridge, id in try await bridge.recordPayment(id) } } } label: { Label("Mark as paid", systemImage: "checkmark.circle") }.disabled(busy) }
        }
        if let state = ChargeState(e, trip, entity: entity) {
            Section {
                Label { Text(state.text(e, trip)) } icon: { Image(systemName: state.symbol).foregroundStyle(state.color) }
                if let c = e.charged { LabeledContent("Rate", value: "1 \(e.code) = \(c.rate.formatted(.number.precision(.fractionLength(0...6)))) \(c.currency)") }
                if state == .charged || state == .missing { Button { charge = e.charged.map { $0.amount.formatted(.number.grouping(.never)) } ?? ""; charging = true } label: { Label("Enter what was charged", systemImage: "creditcard") } }
                if e.code != trip.currency { Button { cashing = true } label: { Label(state == .cash ? "Change the cash rate" : "Paid with cash", systemImage: "banknote") } }
                if state == .cash { Button { Task { await change { bridge, id in _ = try await bridge.session.call("set_charged", ["expense": .string(TravelRepoBridge.key(id))]) } } } label: { Label("Not paid with cash", systemImage: "arrow.uturn.backward") } }
            } header: { Overline("In \(trip.currency)") }
        }
        if !e.split.parts.isEmpty {
            Section {
                ForEach(e.split.parts, id: \.person) { p in LabeledContent(p.person, value: Fmt.money(p.amount, e.code)) }
            } header: { Overline("Split") } footer: { if e.split.issues != nil { Text("Check split") } }
        }
        if !e.split.debts.isEmpty {
            Section {
                ForEach(Array(e.split.debts.enumerated()), id: \.offset) { i, d in
                    let flipped = d.open < 0
                    Toggle(isOn: Binding(get: { d.open == 0 }, set: { paid in
                        Task { await change { bridge, id in try await bridge.markPaidBack(id, person: try bridge.personNamed(d.from), paid) } }
                    })) {
                        VStack(alignment: .leading, spacing: 1) {
                            Text("\(flipped ? d.to : d.from) owes \(flipped ? d.from : d.to) · \(Fmt.money(d.open == 0 ? d.amount : abs(d.open), e.code))")
                            if d.open == 0, let on = d.paid_on {
                                let back = d.paid_in.map { " · " + String(localized: "as \(CostsView.approx($0.estimated == true) + Fmt.money($0.amount, $0.currency))") } ?? ""
                                Text(String(localized: "Paid back on \(CostsView.day(on))") + back).font(.jf(.footnote)).foregroundStyle(Color.jfMuted)
                            }
                        }
                    }
                    .disabled(busy)
                }
            } header: { Overline("Paid back") } footer: { Footnote("A switch counts for everything this person owes for this payment.") }
        }
        if let id = e.uuid { ReceiptSection(tripID: tripID, expense: id) }
    }

    private func change(_ action: @escaping (TravelRepoBridge, UUID) async throws -> Void) async {
        guard let bridge = TravelRepoBridge.all[tripID], let id = UUID(uuidString: expenseID) else { return }
        busy = true
        await bridge.changeCosts { try await action(bridge, id) }
        busy = false
    }

    private func saveCharge() async {
        await change { bridge, id in
            var arguments: [String: JSONValue] = ["expense": .string(TravelRepoBridge.key(id))]
            if !charge.trimmingCharacters(in: .whitespaces).isEmpty { arguments["amount"] = .string(charge); arguments["currency"] = .string(bridge.lastRead.currency) }
            _ = try await bridge.session.call("set_charged", arguments)
        }
    }
}

/// Changes a payment: only what was changed is written, through TravelRepo's own commands. The currency stays once
/// something was charged or paid back, because those amounts depend on it.
struct ExpenseEditor: View {
    let tripID: UUID
    let expense: CostDetails.Expense
    @Environment(\.dismiss) private var dismiss
    @Environment(TripStore.self) private var store
    @State private var title = ""
    @State private var amount = ""
    @State private var currency = ""
    @State private var category = ""
    @State private var hasDay = false
    @State private var day = Date()
    @State private var payers: Set<UUID> = []
    @State private var method = "equal"
    @State private var sharing: Set<UUID> = []
    @State private var values: [UUID: String] = [:]
    @State private var start: Snapshot?
    @State private var planned = false
    @State private var hasDue = false
    @State private var due = Date()
    @State private var belongsTo = ""
    @State private var notes = ""
    @State private var before: (planned: Bool, due: String?, belongsTo: String, notes: String) = (false, nil, "", "")
    @State private var busy = false

    private struct Snapshot: Equatable { var payers: Set<UUID>, method: String, sharing: Set<UUID>, values: [UUID: String], day: String? }
    private var now: Snapshot { .init(payers: payers, method: method, sharing: sharing, values: method == "equal" ? [:] : values, day: hasDay ? iso(day) : nil) }
    private var utc: Calendar { var c = Calendar(identifier: .gregorian); c.timeZone = TimeZone(identifier: "UTC")!; return c }
    private func iso(_ d: Date) -> String { let c = utc.dateComponents([.year, .month, .day], from: d); return String(format: "%04d-%02d-%02d", c.year!, c.month!, c.day!) }

    var body: some View {
        let trip = store.trips.first { $0.id == tripID }
        NavigationStack {
            JFForm {
                Section {
                    TextField("Title", text: $title)
                    HStack {
                        TextField("Amount", text: $amount).decimalKeyboard()
                        Picker("Currency", selection: $currency) { ForEach(Array(Set(["EUR", "JPY", "NZD", "CAD", "USD", expense.code, trip?.currency ?? "EUR"])).sorted(), id: \.self) { Text($0) } }.labelsHidden()
                            .disabled(expense.charged != nil || expense.split.debts.contains { $0.paid != 0 })
                    }
                    CategoryField(tripID: tripID, category: $category)
                    Toggle("Still to pay", isOn: $planned.animation())
                    if planned {
                        Toggle("Due date", isOn: $hasDue)
                        if hasDue { DatePicker("Due", selection: $due, displayedComponents: .date).environment(\.timeZone, utc.timeZone) }
                    }
                    if !planned { Toggle("Its own day", isOn: $hasDay) }
                    if !planned, hasDay { DatePicker("Paid on", selection: $day, displayedComponents: .date).environment(\.timeZone, utc.timeZone) }
                }
                Section {
                    Picker("Belongs to", selection: $belongsTo) {
                        Text("Nothing").tag("")
                        Section("Bookings") { ForEach(trip?.bookings ?? []) { Text($0.title).tag(TravelRepoBridge.key($0.id)) } }
                        Section("Plans") { ForEach(trip?.items ?? []) { Text($0.title).tag(TravelRepoBridge.key($0.id)) } }
                    }
                    .pickerStyle(.navigationLink)
                    TextField("Notes", text: $notes, axis: .vertical).lineLimit(1...6)
                }
                JFSection("Paid by") { ForEach(trip?.people ?? []) { p in toggle(p, $payers) } }
                JFSection("Split") {
                    Picker("Method", selection: $method.converting($values, people: (trip?.people ?? []).map(\.id).filter(sharing.contains), total: number, code: currency)) { Text("Equal").tag("equal"); Text("Amounts").tag("amount"); Text("Shares").tag("share"); Text("Percent").tag("percent") }
                    ForEach(trip?.people ?? []) { p in
                        toggle(p, $sharing)
                        if method != "equal", sharing.contains(p.id) { TextField("Value", text: Binding(get: { values[p.id] ?? "" }, set: { values[p.id] = $0 })).decimalKeyboard() }
                    }
                }
            }
            .jfBackground()
            .navigationTitle("Edit payment")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) { Button(role: .cancel) { dismiss() } }
                ToolbarItem(placement: .confirmationAction) { Button(role: .confirm) { Task { await save() } }.disabled(busy || title.trimmingCharacters(in: .whitespaces).isEmpty || number == nil) }
            }
            .onAppear(perform: load)
        }
    }

    private func toggle(_ p: Person, _ set: Binding<Set<UUID>>) -> some View {
        Toggle(isOn: Binding(get: { set.wrappedValue.contains(p.id) }, set: { if $0 { set.wrappedValue.insert(p.id) } else { set.wrappedValue.remove(p.id) } })) { HStack { PersonAvatar(person: p, size: 24).accessibilityHidden(true); Text(p.name) } }
    }

    private var number: Decimal? { Decimal(string: amount.replacingOccurrences(of: ",", with: "."), locale: Locale(identifier: "en_US_POSIX")) }

    private func load() {
        guard start == nil, let trip = store.trips.first(where: { $0.id == tripID }), let data = TravelRepoBridge.all[tripID]?.entities[expense.id] else { return }
        title = expense.title; amount = expense.value.formatted(.number.grouping(.never)); currency = expense.code; category = expense.category
        if let own = data["date"]?.string, let d = TravelRepoBridge.dayComponents(own), let date = utc.date(from: d) { hasDay = true; day = date }
        else if let d = expense.date.flatMap({ TravelRepoBridge.dayComponents($0) }), let date = utc.date(from: d) { day = date }
        planned = expense.planned
        if let d = expense.due.flatMap({ TravelRepoBridge.dayComponents($0) }), let date = utc.date(from: d) { hasDue = true; due = date }
        belongsTo = (data["related"]?.array ?? []).compactMap { $0["id"]?.string }.first { id in trip.bookings.contains { TravelRepoBridge.key($0.id) == id } || trip.items.contains { TravelRepoBridge.key($0.id) == id } } ?? ""
        notes = data["body"]?.string ?? ""
        before = (planned, hasDue ? iso(due) : nil, belongsTo, notes)
        payers = Set((data["paid_by"]?.array ?? []).compactMap { $0.string.flatMap(UUID.init(uuidString:)) })
        method = expense.split.method ?? "equal"
        for p in expense.split.parts {
            guard let id = trip.people.first(where: { $0.name == p.person })?.id else { continue }
            sharing.insert(id); if let v = p.value, method != "equal" { values[id] = v.formatted(.number.grouping(.never)) }
        }
        start = now
    }

    private func save() async {
        guard let bridge = TravelRepoBridge.all[tripID], let id = expense.uuid, let number, let start, let trip = store.trips.first(where: { $0.id == tripID }) else { return }
        busy = true; defer { busy = false }
        var patch: [String: JSONValue] = [:]
        if title != expense.title { patch["title"] = .string(title.trimmingCharacters(in: .whitespaces)) }
        if number != expense.value || currency != expense.code { patch["amount"] = ["value": .string(NSDecimalNumber(decimal: number.rounded(currency)).stringValue), "currency": .string(currency)] }
        if category != expense.category, !category.isEmpty { patch["category"] = .string(category) }
        if planned != before.planned { patch["estimated"] = .bool(planned) }
        let newDue = planned && hasDue ? iso(due) : nil
        if newDue != before.due { patch["due"] = newDue.map { ["date": .string($0)] } ?? .null }
        if notes != before.notes { patch["body"] = notes.isEmpty ? .null : .string(notes) }
        if belongsTo != before.belongsTo {
            var links = (bridge.entities[expense.id]?["related"]?.array ?? []).filter { $0["id"]?.string != before.belongsTo }
            if !belongsTo.isEmpty, let type = bridge.entities[belongsTo]?["type"]?.string { links.insert(["id": .string(belongsTo), "type": .string(type)], at: 0) }
            patch["related"] = links.isEmpty ? .null : .array(links)
        }
        if now.day != start.day { patch["date"] = now.day.map { .string($0) } ?? .null }
        if payers != start.payers { patch["paid_by"] = .array(trip.people.filter { payers.contains($0.id) }.map { .string(TravelRepoBridge.key($0.id)) }) }
        let people = trip.people.filter { sharing.contains($0.id) }
        let splitChanged = method != start.method || sharing != start.sharing || now.values != start.values
        let ok = await bridge.changeCosts {
            if !patch.isEmpty { _ = try await bridge.session.call("update_entity", ["id": .string(TravelRepoBridge.key(id)), "changes": .object(patch)]) }
            if splitChanged {
                var arguments: [String: JSONValue] = ["expense": .string(TravelRepoBridge.key(id)), "method": .string(method), "people": .array(people.map { .string(TravelRepoBridge.key($0.id)) })]
                if method != "equal" { arguments["values"] = .array(people.map { .string(values[$0.id] ?? "0") }) }
                _ = try await bridge.session.call("split_expense", arguments)
            }
        }
        if ok { dismiss() }
    }
}

/// Paying back: whose open items, each to choose, and what the choice comes to per currency after what two people owe
/// each other is set off. Confirming marks the chosen items as paid back.
struct PayBackSheet: View {
    let tripID: UUID
    @Environment(\.dismiss) private var dismiss
    @Environment(TripStore.self) private var store
    @State private var costs: CostDetails?
    @State private var who = ""
    @State private var chosen: Set<String> = []
    @State private var busy = false
    @State private var calculator: PayBackCalculator?
    @State private var together: [CostDetails.Transfer]?
    @State private var working = false

    /// What one person owes for one payment; the switch of `mark_paid` covers all of it.
    private struct Item: Identifiable { let expense: CostDetails.Expense; let debtor: String; let debts: [CostDetails.Debt]; var id: String { expense.id + debtor } }

    private var items: [Item] {
        (costs?.expenses ?? []).flatMap { e in
            Dictionary(grouping: e.split.debts.filter { $0.open != 0 }, by: \.from).map { Item(expense: e, debtor: $0.key, debts: $0.value) }
        }.filter { i in i.debtor == who || i.debts.contains { $0.to == who } }.sorted { ($0.expense.date ?? "", $0.expense.title) < ($1.expense.date ?? "", $1.expense.title) }
    }

    /// Net payments between pairs, per currency.
    private var result: [(from: String, to: String, amount: Decimal, currency: String)] {
        var net: [String: (a: String, b: String, code: String, sum: Decimal)] = [:]
        for i in items where chosen.contains(i.id) {
            for d in i.debts {
                let (a, b, sign) = d.from < d.to ? (d.from, d.to, Decimal(1)) : (d.to, d.from, Decimal(-1))
                net["\(a)|\(b)|\(i.expense.code)", default: (a, b, i.expense.code, 0)].sum += sign * d.open
            }
        }
        return net.values.filter { $0.sum != 0 }.map { $0.sum > 0 ? ($0.a, $0.b, $0.sum, $0.code) : ($0.b, $0.a, -$0.sum, $0.code) }.sorted { ($0.currency, $0.from) < ($1.currency, $1.from) }
    }

    var body: some View {
        let trip = store.trips.first { $0.id == tripID }
        NavigationStack {
            JFList {
                Section {
                    Picker("Whose open items", selection: $who) { ForEach(trip?.people ?? []) { Text($0.name).tag($0.name) } }
                } footer: { Footnote("Choose the items to settle now. What two people owe each other is set off into one payment.") }
                Section {
                    if costs != nil, items.isEmpty { Text("\(who) has nothing open.").foregroundStyle(Color.jfMuted) }
                    ForEach(items) { i in
                        Toggle(isOn: Binding(get: { chosen.contains(i.id) }, set: { if $0 { chosen.insert(i.id) } else { chosen.remove(i.id) } })) {
                            VStack(alignment: .leading, spacing: 1) {
                                Text(i.expense.title)
                                ForEach(Array(i.debts.enumerated()), id: \.offset) { _, d in
                                    Text("\(d.open < 0 ? d.to : d.from) owes \(d.open < 0 ? d.from : d.to) · \(Fmt.money(abs(d.open), i.expense.code))").font(.jf(.footnote).monospacedDigit()).foregroundStyle(Color.jfMuted)
                                }
                            }
                        }
                    }
                } header: { Overline("Open items") }
                Section {
                    if result.isEmpty { Text("Nothing chosen yet.").foregroundStyle(Color.jfMuted) }
                    if let together, !chosen.isEmpty {
                        ForEach(Array(together.enumerated()), id: \.offset) { _, r in
                            LabeledContent { Text(CostsView.approx(r.estimated == true) + Fmt.money(r.amount, r.currency)).font(.jf(.body, .semibold).monospacedDigit()).foregroundStyle(Color.jfText) } label: { Text("\(r.from) pays \(r.to)") }
                        }
                        if together.isEmpty { Text("Nothing to pay: the items cancel out.").foregroundStyle(Color.jfMuted) }
                    } else if working, !chosen.isEmpty { ProgressView() }
                    else { ForEach(Array(result.enumerated()), id: \.offset) { _, r in
                        LabeledContent { Text(Fmt.money(r.amount, r.currency)).font(.jf(.body, .semibold).monospacedDigit()).foregroundStyle(Color.jfText) } label: { Text("\(r.from) pays \(r.to)") }
                    } }
                    Button { Task { await confirm() } } label: { Label("Mark as paid back", systemImage: "checkmark.circle") }.disabled(chosen.isEmpty || busy)
                } header: { Overline("That comes to") } footer: { Footnote(together == nil ? "Shown in the currencies of the payments." : "In the trip's currency. ≈ is estimated with exchange rates, because no charged amount was entered.") }
            }
            .listStyle(.insetGrouped)
            .jfBackground()
            .navigationTitle("Pay back")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar { ToolbarItem(placement: .topBarLeading) { Button(role: .close) { dismiss() } } }
            .task(id: store.travelRepoReads) { await load(trip) }
            .onChange(of: who) { chosen = Set(items.map(\.id)) }
            .task(id: "\(chosen.sorted())#\(store.travelRepoReads)") { await calculate(trip) }
        }
    }

    private func load(_ trip: Trip?) async {
        costs = try? await TravelRepoBridge.all[tripID]?.costDetails(); calculator = nil
        if who.isEmpty { who = (trip?.person(store.me[tripID]) ?? trip?.people.first)?.name ?? "" }
        if chosen.isEmpty { chosen = Set(items.map(\.id)) } else { chosen.formIntersection(items.map(\.id)) }
    }

    /// The choice in the trip's currency; per currency when that cannot be worked out.
    private func calculate(_ trip: Trip?) async {
        guard let bridge = TravelRepoBridge.all[tripID], let trip, let costs, !chosen.isEmpty, !working, !busy else { return }
        working = true; defer { working = false }
        var done: Set<String>?
        // A tap while TravelRepo calculates counts in the next round.
        while done != chosen, !busy {
            let now = chosen
            do {
                if calculator == nil { await bridge.flush(); calculator = try await PayBackCalculator(bridge) }
                var settled: [(expense: String, debtor: UUID)] = []
                for e in costs.expenses {
                    for debtor in Set(e.split.debts.filter { $0.open != 0 }.map(\.from)) where !now.contains(e.id + debtor) { settled.append((e.id, try bridge.personNamed(debtor))) }
                }
                together = try await calculator?.transfers(without: settled, currency: trip.currency)
            } catch { together = nil; TravelRepoBridge.log.error("pay back: \(error)") }
            done = now
        }
    }

    private func confirm() async {
        guard let bridge = TravelRepoBridge.all[tripID] else { return }
        busy = true; defer { busy = false }
        let paid = items.filter { chosen.contains($0.id) }
        let ok = await bridge.changeCosts {
            for i in paid { if let id = i.expense.uuid { try await bridge.markPaidBack(id, person: try bridge.personNamed(i.debtor), true) } }
        }
        if ok { dismiss() }
    }
}
