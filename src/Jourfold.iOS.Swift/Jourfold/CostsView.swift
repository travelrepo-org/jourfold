import Charts
import SwiftUI
import TravelRepoKit

/// Costs as the .NET app shows them: what the trip comes to in its currency (exact or ≈ estimated), totals per
/// currency, who owes whom, what is still to pay, budgets, categories for everyone or for some, and every payment by
/// day with who paid for whom and what is still open.
struct CostsView: View {
    let tripID: UUID
    @Environment(TripStore.self) private var store
    @Environment(AppNavigation.self) private var nav
    @State private var costs: CostDetails?
    @State private var planned: (total: Decimal, estimated: Bool, rest: [CostDetails.Sum]) = (0, false, [])
    @State private var people: Set<UUID> = []
    @State private var query = ""
    @State private var export: String?
    @State private var showing: CostDetails.Expense?
    @State private var paying = false
    @State private var upgrade: [CostUpgradeSection.Change] = []
    @State private var budgeting: BudgetID?
    struct BudgetID: Identifiable { let id: String }
    @State private var confirmSettle = false
    @State private var problem: String?

    struct Balance: Identifiable, Hashable {
        var id: String { "\(from)-\(to)-\(currency ?? "")" }
        let from: UUID, to: UUID, amount: Decimal
        var currency: String? = nil
    }

    /// TravelRepo's costs of each trip as last read, for the line under More and the self-test.
    nonisolated(unsafe) static var costs: [UUID: Costs] = [:]
    nonisolated(unsafe) static var bridged: [UUID: [Balance]] = [:]
    static func balances(_ trip: Trip) -> [Balance] { bridged[trip.id] ?? [] }

    static func paidSummary(_ trip: Trip) -> String {
        let paid = (costs[trip.id]?.totals ?? []).filter { $0.paid > 0 }.map { Fmt.money($0.paid, $0.currency) }
        return paid.isEmpty ? String(localized: "Nothing paid yet") : String(localized: "\(paid.joined(separator: " + ")) paid")
    }

    var body: some View {
        let trip = store.trip(tripID)
        JFList {
            if let costs {
                if costs.expenses.isEmpty && costs.budgets.isEmpty && costs.budget == nil {
                    Section { Text("No costs yet. Add what was paid or is still to pay.").foregroundStyle(Color.jfMuted) }
                }
                if query.isEmpty {
                    CostUpgradeSection(tripID: tripID, changes: upgrade)
                    tripTotal(costs, trip)
                    if costs.totals.count > 1 { totals(costs) }
                    owes(costs, trip)
                    stillToPay(costs)
                    budgets(costs, trip)
                    categories(costs, trip)
                }
                payments(costs, trip)
                Section {} footer: { Footnote("Totals are per currency. Jourfold never rewrites amounts with current exchange rates.") }
            } else if let problem {
                Section { Text(problem).foregroundStyle(Color.jfDanger) }
            } else {
                Section { ProgressView() }
            }
        }
        .listStyle(.insetGrouped)
        .jfBackground()
        .navigationTitle("Costs")
        .searchable(text: $query, prompt: Text("Search: name, category, person, place"))
        .toolbar {
            ToolbarItem(placement: .topBarTrailing) {
                Button { nav.quickAddKind = .expense; nav.quickAdd = true } label: { Image(systemName: "plus") }.accessibilityLabel(Text("Add expense"))
            }
            if let export {
                ToolbarItem(placement: .topBarTrailing) { ShareLink(item: export) { Image(systemName: "square.and.arrow.up") }.accessibilityLabel(Text("Export costs")) }
            }
        }
        .task(id: "\(store.travelRepoReads)#\(people.map(\.uuidString).sorted())") { await load(trip) }
        .sheet(item: $showing) { e in ExpenseSheet(tripID: tripID, expenseID: e.id) }
        .sheet(item: $budgeting) { b in BudgetEditor(tripID: tripID, budgetID: b.id.isEmpty ? nil : b.id) }
        .sheet(isPresented: $paying) { PayBackSheet(tripID: tripID) }
        .confirmationDialog(Text("Mark everything as paid back?"), isPresented: $confirmSettle, titleVisibility: .visible) {
            Button("Mark all as paid back") { Task { await settleAll() } }
        } message: { Text("Do this once everyone has made the payments shown. Every open item of the trip is then marked as paid back today.") }
        .onAppear {
            // For screenshots: `-expense <title>` opens a payment, `-payback YES` the paying back, `-costsearch <text>` searches.
            if DebugTools.flag("payback") { paying = true }
            if let q = DebugTools.value("costsearch") { query = q }
        }
    }

    private func load(_ trip: Trip) async {
        guard let bridge = TravelRepoBridge.all[tripID] else { problem = String(localized: "The trip is not open."); return }
        do {
            let loaded = try await bridge.costDetails(for: people)
            costs = loaded; problem = nil; upgrade = await CostUpgradeSection.load(tripID)
            // Still to pay in the trip's currency, with TravelRepo's conversion where the rates allow it.
            var total = Decimal(0), estimated = false, rest: [CostDetails.Sum] = []
            let open = Dictionary(grouping: loaded.expenses.filter { $0.planned && !$0.idea }, by: \.code).mapValues { $0.reduce(Decimal(0)) { $0 + $1.value } }
            for (code, amount) in open.sorted(by: { $0.key < $1.key }) {
                if code == trip.currency { total += amount; continue }
                let text = NSDecimalNumber(decimal: amount).stringValue
                if let v = try? await bridge.session.call("convert_currency", ["amount": .string(text), "from": .string(code), "to": .string(trip.currency)])["result"]?.number { total += Decimal(v).rounded(trip.currency); estimated = true }
                else { rest.append(.init(amount: text, currency: code)) }
            }
            planned = (total, estimated, rest)
            export = try? await bridge.session.export("costs", format: "text")
            if let title = DebugTools.value("expense"), showing == nil { showing = loaded.expenses.first { $0.title.hasPrefix(title) }; UserDefaults.standard.removeObject(forKey: "expense") }
        } catch { problem = TravelRepoBridge.message(error) }
    }

    private func settleAll() async {
        guard let costs, let bridge = TravelRepoBridge.all[tripID] else { return }
        await bridge.changeCosts {
            for e in costs.expenses where e.split.debts.contains(where: { $0.open != 0 }) { if let id = e.uuid { try await bridge.markPaidBack(id, person: nil, true) } }
        }
    }

    // MARK: The trip in its currency

    @ViewBuilder
    private func tripTotal(_ c: CostDetails, _ trip: Trip) -> some View {
        if let paid = c.paid_in, !c.expenses.isEmpty || c.budget != nil {
            let ideas = Self.sums(c.expenses.filter { $0.planned && $0.idea })
            Section {
                VStack(alignment: .leading, spacing: 6) {
                    Overline("In \(paid.currency)")
                    Text(Self.approx(paid.estimated > 0) + Fmt.money(paid.total, paid.currency)).font(.jf(.title, .bold).monospacedDigit())
                    if let each = perPersonAndDay(c, trip) { Label(each, systemImage: "person.2").font(.jf(.footnote)).foregroundStyle(Color.jfMuted) }
                    if !paid.not_converted.isEmpty { note("Not converted yet: \(Self.text(paid.not_converted))") }
                    if paid.without_charged_amount > 0 {
                        Label { Text("\(paid.without_charged_amount) without a charged amount, estimated or left out") } icon: { Image(systemName: "creditcard.trianglebadge.exclamationmark").foregroundStyle(Color.jfWarning) }
                            .font(.jf(.footnote)).foregroundStyle(Color.jfMuted)
                    }
                    if planned.total != 0 || !planned.rest.isEmpty {
                        let parts = [planned.total != 0 ? Self.approx(planned.estimated) + Fmt.money(planned.total, trip.currency) : nil, planned.rest.isEmpty ? nil : Self.text(planned.rest)].compactMap { $0 }
                        note("\(parts.joined(separator: " + ")) still to pay")
                    }
                    if !ideas.isEmpty { note("Ideas, not counted: \(Self.text(ideas))") }
                    if let b = c.budget { BudgetBar(used: b.spent + b.planned, amount: b.amount, left: b.left, currency: b.currency, approximate: b.estimated == true) }
                    if paid.estimated > 0, let date = Rates.date {
                        note("≈ estimated with the European Central Bank's reference rates of \(date.formatted(date: .abbreviated, time: .omitted)), because no charged amount was entered.")
                    }
                }
                .padding(.vertical, 4)
            }
        }
    }

    private func note(_ text: LocalizedStringKey) -> some View { Text(text).font(.jf(.footnote)).foregroundStyle(Color.jfMuted) }

    /// Paid and still to pay, divided among the travellers and the days; nothing when some of it is not converted.
    private func perPersonAndDay(_ c: CostDetails, _ trip: Trip) -> String? {
        guard let paid = c.paid_in, paid.not_converted.isEmpty, planned.rest.isEmpty, !trip.people.isEmpty, !trip.days.isEmpty, paid.total + planned.total > 0 else { return nil }
        let each = ((paid.total + planned.total) / Decimal(trip.people.count) / Decimal(trip.days.count)).rounded(trip.currency)
        return String(localized: "\(Self.approx(paid.estimated > 0 || planned.estimated) + Fmt.money(each, trip.currency)) per person and day")
    }

    private func totals(_ c: CostDetails) -> some View {
        Section {
            ForEach(c.totals, id: \.currency) { t in
                LabeledContent {
                    VStack(alignment: .trailing, spacing: 1) {
                        Text(Fmt.money(t.paid, t.currency)).font(.jf(.body, .semibold).monospacedDigit()).foregroundStyle(Color.jfText)
                        if t.estimated > 0 { Text("\(Fmt.money(t.estimated, t.currency)) still to pay").font(.jf(.caption).monospacedDigit()) }
                    }
                } label: { Text(t.currency) }
            }
        } header: { Overline("Paid per currency") }
    }

    // MARK: Who owes whom

    @ViewBuilder
    private func owes(_ c: CostDetails, _ trip: Trip) -> some View {
        if c.expenses.contains(where: { !$0.split.debts.isEmpty }) || !c.needs_attention.isEmpty {
            Section {
                ForEach(c.needs_attention, id: \.id) { p in
                    Label { VStack(alignment: .leading) { Text(p.title); Text("Check split").font(.jf(.footnote)).foregroundStyle(Color.jfMuted) } } icon: { Image(systemName: "exclamationmark.triangle").foregroundStyle(Color.jfWarning) }
                }
                if c.balances.isEmpty {
                    Label("Everyone is even. Nothing is open.", systemImage: "checkmark.circle").foregroundStyle(Color.jfSuccess)
                } else {
                    ForEach(Array(c.balances.enumerated()), id: \.offset) { _, b in
                        HStack(spacing: 10) {
                            person(b.person, trip)
                            VStack(alignment: .leading, spacing: 1) {
                                Text(named(b.person, trip)).font(.jf(.body, .medium))
                                let money = Self.approx(b.estimated == true) + Fmt.money(abs(b.net), b.currency)
                                Text(b.net > 0 ? "gets \(money)" : "owes \(money)").font(.jf(.footnote).monospacedDigit()).foregroundStyle(b.net > 0 ? Color.jfSuccess : Color.jfWarning)
                            }
                        }
                    }
                    Text("The fewest payments that settle everything:").font(.jf(.footnote)).foregroundStyle(Color.jfMuted)
                    ForEach(Array(c.settle_up.enumerated()), id: \.offset) { _, t in
                        HStack(spacing: 8) {
                            person(t.from, trip)
                            Image(systemName: "arrow.right").font(.footnote).foregroundStyle(Color.jfSubtleText).accessibilityHidden(true)
                            person(t.to, trip)
                            Text("\(named(t.from, trip)) pays \(named(t.to, trip))")
                            Spacer()
                            Text(Self.approx(t.estimated == true) + Fmt.money(t.amount, t.currency)).font(.jf(.body, .semibold).monospacedDigit())
                        }
                    }
                    if c.settle_up.contains(where: { $0.currency != trip.currency }) {
                        note("Some expenses have no amount charged in \(trip.currency) and no exchange rate yet, so they stay in their own currency.")
                    }
                    Button { paying = true } label: { Label("Pay back…", systemImage: "wallet.bifold") }
                    Button { confirmSettle = true } label: { Label("Mark all as paid back", systemImage: "checkmark.circle") }
                }
            } header: { Overline("Who owes whom") }
        }
    }

    private func named(_ name: String, _ trip: Trip) -> String { trip.people.first { $0.id == store.me[tripID] }?.name == name ? String(localized: "\(name) (You)") : name }
    @ViewBuilder private func person(_ name: String, _ trip: Trip) -> some View { if let p = trip.people.first(where: { $0.name == name }) { PersonAvatar(person: p, size: 26) } }

    // MARK: Still to pay, budgets, categories

    @ViewBuilder
    private func stillToPay(_ c: CostDetails) -> some View {
        let open = c.expenses.filter(\.planned).sorted { ($0.idea ? 1 : 0, $0.due ?? "9999", $0.title) < ($1.idea ? 1 : 0, $1.due ?? "9999", $1.title) }
        if !open.isEmpty {
            Section {
                ForEach(open) { e in
                    HStack {
                        Button { showing = e } label: {
                            VStack(alignment: .leading, spacing: 1) {
                                Text(e.title).font(.jf(.body, .medium)).foregroundStyle(Color.jfText)
                                Text([Fmt.money(e.value, e.code), e.idea ? String(localized: "Idea, not counted") : e.due.map { String(localized: "due \(Self.day($0))") }].compactMap { $0 }.joined(separator: " · "))
                                    .font(.jf(.footnote)).foregroundStyle(Color.jfMuted)
                            }
                        }
                        .buttonStyle(.plain)
                        Spacer()
                        Button { Task { await record(e) } } label: {
                            // Not a Label: a list gives its icon a column and the title no width, and the button grows tall.
                            HStack(spacing: 4) { Image(systemName: "checkmark.circle"); Text("Paid") }.font(.jf(.footnote, .semibold)).lineLimit(1)
                        }
                            .buttonStyle(.bordered).fixedSize().accessibilityLabel(Text("Mark \(e.title) as paid"))
                    }
                }
            } header: { Overline("Still to pay") }
        }
    }

    private func record(_ e: CostDetails.Expense) async {
        guard let id = e.uuid, let bridge = TravelRepoBridge.all[tripID] else { return }
        await bridge.changeCosts { try await bridge.recordPayment(id) }
    }

    @ViewBuilder
    private func budgets(_ c: CostDetails, _ trip: Trip) -> some View {
        let wholeTrip = (TravelRepoBridge.all[tripID]?.entities ?? [:]).filter { $0.value["type"]?.string == "budget" && !c.budgets.map(\.id).contains($0.key) }
        if !c.expenses.isEmpty || !c.budgets.isEmpty || !wholeTrip.isEmpty {
            Section {
                ForEach(wholeTrip.keys.sorted(), id: \.self) { id in
                    Button { budgeting = .init(id: id) } label: { LabeledContent { Text((wholeTrip[id]?["amount"]?["value"]?.string).flatMap { Decimal(string: $0, locale: Locale(identifier: "en_US_POSIX")) }.map { Fmt.money($0, wholeTrip[id]?["amount"]?["currency"]?.string ?? trip.currency) } ?? "") } label: { Text("\(wholeTrip[id]?["title"]?.string ?? "") · Whole trip") } }
                }
                ForEach(c.budgets, id: \.id) { b in
                    Button { budgeting = .init(id: b.id) } label: { VStack(alignment: .leading, spacing: 4) {
                        Text([b.title, b.category.map { Self.categoryName($0) }, b.person, b.collection].compactMap { $0 }.joined(separator: " · ")).font(.jf(.body, .medium))
                        BudgetBar(used: b.spent + b.planned, amount: b.amount, left: b.left, currency: b.currency, approximate: b.estimated == true)
                    } }.buttonStyle(.plain)
                }
                Button { budgeting = .init(id: "") } label: { Label("Add a budget", systemImage: "plus") }
            } header: { Overline("Budgets") }
        }
    }

    @ViewBuilder
    private func categories(_ c: CostDetails, _ trip: Trip) -> some View {
        if c.expenses.contains(where: { !$0.planned }) {
            Section {
                ScrollView(.horizontal, showsIndicators: false) {
                    HStack {
                        chip("Everyone", people.isEmpty) { people = [] }
                        ForEach(trip.people) { p in chip(LocalizedStringKey(p.name), people.contains(p.id)) { if !people.insert(p.id).inserted { people.remove(p.id) } } }
                    }
                }
                if c.by_category.isEmpty { Text("Nothing paid for them yet.").foregroundStyle(Color.jfMuted) }
                ForEach(Array(Set(c.by_category.map(\.currency))).sorted { ($0 == trip.currency ? 0 : 1, $0) < ($1 == trip.currency ? 0 : 1, $1) }, id: \.self) { code in
                    let rows = c.by_category.filter { $0.currency == code }
                    let sum = rows.reduce(Decimal(0)) { $0 + $1.amount }
                    if code != trip.currency { note("Not converted yet, in \(code):") }
                    LabeledContent { Text(Self.approx(rows.contains { $0.estimated == true }) + Fmt.money(sum, code)).font(.jf(.body, .semibold).monospacedDigit()).foregroundStyle(Color.jfText) } label: { Text("Total").font(.jf(.body, .semibold)) }
                    ForEach(rows, id: \.category) { r in
                        let part = sum == 0 ? 0 : NSDecimalNumber(decimal: r.amount / sum).doubleValue
                        HStack(spacing: 12) {
                            Image(systemName: Self.categorySymbol(r.category)).foregroundStyle(Color.jfMuted).frame(width: 24).accessibilityHidden(true)
                            VStack(alignment: .leading, spacing: 3) {
                                Text(Self.categoryName(r.category))
                                Text("\(Self.approx(r.estimated == true))\(Fmt.money(r.amount, code)) · \(part.formatted(.percent.precision(.fractionLength(0))))").font(.jf(.footnote).monospacedDigit()).foregroundStyle(Color.jfMuted)
                                ProgressView(value: min(max(part, 0), 1)).tint(Color.jfAccent).accessibilityHidden(true)
                            }
                        }
                    }
                }
            } header: { Overline("By category") }
        }
    }

    private func chip(_ title: LocalizedStringKey, _ on: Bool, _ action: @escaping () -> Void) -> some View {
        Button(action: action) {
            Text(title).font(.jf(.subheadline, .semibold)).padding(.horizontal, 14).padding(.vertical, 8)
                .foregroundStyle(on ? Color.jfOnAccent : Color.jfText).background(on ? Color.jfAccent : Color.jfAccentSoft, in: Capsule())
        }
        .buttonStyle(.plain).accessibilityAddTraits(on ? .isSelected : [])
    }

    // MARK: Every payment, by day

    @ViewBuilder
    private func payments(_ c: CostDetails, _ trip: Trip) -> some View {
        let found = c.expenses.filter { matches($0, trip) }
        let dated = Dictionary(grouping: found.filter { $0.date != nil }) { $0.date! }
        if found.isEmpty, !query.isEmpty { Section { Text("No payment fits “\(query)”.").foregroundStyle(Color.jfMuted) } }
        ForEach(dated.keys.sorted(by: >), id: \.self) { day in
            let rows = dated[day]!.sorted { $0.title < $1.title }
            Section { rowsFor(rows, trip) } header: {
                HStack {
                    Text(Self.day(day, weekday: true)).font(.jf(.subheadline, .semibold))
                    Spacer()
                    let paid = Self.sums(rows.filter { !$0.planned })
                    if !paid.isEmpty { Text(Self.text(paid)).font(.jf(.subheadline).monospacedDigit()) }
                }
                .textCase(nil).foregroundStyle(Color.jfMuted)
            }
        }
        let undated = found.filter { $0.date == nil }.sorted { $0.title < $1.title }
        if !undated.isEmpty { Section { rowsFor(undated, trip) } header: { Overline("Not linked to a day") } }
    }

    private func rowsFor(_ rows: [CostDetails.Expense], _ trip: Trip) -> some View {
        ForEach(rows) { e in Button { showing = e } label: { PaymentRow(expense: e, trip: trip, entity: TravelRepoBridge.all[tripID]?.entities[e.id]) }.buttonStyle(.plain) }
    }

    private func matches(_ e: CostDetails.Expense, _ trip: Trip) -> Bool {
        let words = query.split(separator: " ")
        guard !words.isEmpty else { return true }
        let payers = (TravelRepoBridge.all[tripID]?.entities[e.id]?["paid_by"]?.array ?? []).compactMap { trip.person($0.string.flatMap(UUID.init(uuidString:)))?.name }
        let text = ([e.title, Self.categoryName(e.category), e.code] + e.belongs_to + payers + e.split.parts.map(\.person)).joined(separator: " ")
        return words.allSatisfy { text.localizedStandardContains($0) }
    }

    // MARK: Words

    static func approx(_ estimated: Bool) -> String { estimated ? "≈ " : "" }
    static func sums(_ list: [CostDetails.Expense]) -> [CostDetails.Sum] {
        Dictionary(grouping: list, by: \.code).map { code, rows in .init(amount: NSDecimalNumber(decimal: rows.reduce(Decimal(0)) { $0 + $1.value }).stringValue, currency: code) }.sorted { $0.currency < $1.currency }
    }
    static func text(_ sums: [CostDetails.Sum]) -> String {
        sums.map { Fmt.money(Decimal(string: $0.amount, locale: Locale(identifier: "en_US_POSIX")) ?? 0, $0.currency) }.joined(separator: " + ")
    }
    static func day(_ iso: String, weekday: Bool = false) -> String {
        guard let d = TravelRepoBridge.dayComponents(iso) else { return iso }
        var c = Calendar(identifier: .gregorian); c.timeZone = TimeZone(identifier: "UTC")!
        guard let date = c.date(from: d) else { return iso }
        return weekday ? Fmt.shortDay(date, c.timeZone) + " \(d.year ?? 0)" : Fmt.day(date, c.timeZone, weekday: false) + " \(d.year ?? 0)"
    }
    static func category(_ name: String) -> CostCategory? { TravelRepoBridge.categories.first { $0.1 == name }?.0 }
    static func categoryName(_ name: String) -> String { category(name).map { String(localized: $0.label) } ?? name }
    static func categorySymbol(_ name: String) -> String { category(name)?.symbol ?? "tag.fill" }
}

extension Decimal {
    /// Rounded to the currency's smallest unit, away from zero at the half, as TravelRepo rounds.
    func rounded(_ currency: String) -> Decimal { var v = self, r = Decimal(); NSDecimalRound(&r, &v, Fmt.minorUnits(currency), .plain); return r }
}

struct BudgetBar: View {
    let used: Decimal, amount: Decimal, left: Decimal
    let currency: String
    let approximate: Bool
    var body: some View {
        let ratio = amount <= 0 ? 1 : min(max(NSDecimalNumber(decimal: used / amount).doubleValue, 0), 1)
        let a = CostsView.approx(approximate)
        VStack(alignment: .leading, spacing: 4) {
            ProgressView(value: ratio).tint(left < 0 ? Color.jfDanger : ratio > 0.85 ? Color.jfWarning : Color.jfAccent)
            Label {
                Text("\(a + Fmt.money(used, currency)) of \(Fmt.money(amount, currency))") + Text(verbatim: " · ")
                    + (left >= 0 ? Text("\(a + Fmt.money(left, currency)) left") : Text("\(a + Fmt.money(-left, currency)) over budget"))
            } icon: { Image(systemName: left < 0 ? "exclamationmark.triangle" : "chart.bar") }
            .font(.jf(.footnote)).foregroundStyle(left < 0 ? Color.jfDanger : Color.jfMuted)
        }
        .accessibilityElement(children: .combine)
    }
}

/// Whether what a payment comes to in the trip's currency is certain: charged, cash or in that currency already, or
/// only estimated.
enum ChargeState {
    case exact, charged, cash, missing
    init?(_ e: CostDetails.Expense, _ trip: Trip, entity: [String: JSONValue]?) {
        if e.planned || e.value == 0 { return nil }
        if entity?["converted"]?["source"]?.string == "cash" { self = .cash }
        else if e.charged != nil { self = .charged }
        else { self = e.code == trip.currency ? .exact : .missing }
    }
    var symbol: String { switch self { case .cash: "banknote"; case .missing: "creditcard.trianglebadge.exclamationmark"; default: "creditcard" } }
    var color: Color { if case .missing = self { .jfWarning } else { .jfSuccess } }
    func text(_ e: CostDetails.Expense, _ trip: Trip) -> String {
        switch self {
        case .exact: String(localized: "Exact: paid in \(e.code)")
        case .charged: String(localized: "Exact: \(e.charged.map { Fmt.money($0.amount, $0.currency) } ?? "") was charged")
        case .cash: String(localized: "Cash, counted as \(e.charged.map { Fmt.money($0.amount, $0.currency) } ?? "")")
        case .missing: String(localized: "No amount charged in \(trip.currency) yet")
        }
    }
}

/// One payment: day, category, what it belongs to, who paid → for whom, amount, whether it is certain in the trip's
/// currency, and whether it is paid back.
struct PaymentRow: View {
    let expense: CostDetails.Expense
    let trip: Trip
    let entity: [String: JSONValue]?

    var body: some View {
        let e = expense, state = ChargeState(e, trip, entity: entity)
        let payers = (entity?["paid_by"]?.array ?? []).compactMap { trip.person($0.string.flatMap(UUID.init(uuidString:))) }
        let names = Set(e.split.parts.map(\.person))
        HStack(alignment: .top, spacing: 12) {
            Image(systemName: CostsView.categorySymbol(e.category)).font(.system(size: 14, weight: .semibold)).foregroundStyle(Color.jfAccentSoftText)
                .frame(width: 32, height: 32).background(Color.jfAccentSoft, in: RoundedRectangle(cornerRadius: 8, style: .continuous))
                .accessibilityLabel(Text(CostsView.categoryName(e.category)))
            VStack(alignment: .leading, spacing: 4) {
                Text(e.title).font(.jf(.body, .semibold)).foregroundStyle(Color.jfText)
                if let first = e.belongs_to.first { Text(first).font(.jf(.footnote)).foregroundStyle(Color.jfMuted) }
                HStack(spacing: 5) {
                    if payers.isEmpty { Image(systemName: "questionmark.circle.dashed").foregroundStyle(e.planned ? Color.jfSubtleText : Color.jfWarning).accessibilityLabel(Text("Nobody entered as payer")) }
                    ForEach(payers) { PersonAvatar(person: $0, size: 22) }
                    Image(systemName: "arrow.right").font(.caption).foregroundStyle(Color.jfSubtleText).accessibilityLabel(Text("for"))
                    if names.count >= 2, names == Set(trip.people.map(\.name)) { Text("Everyone").font(.jf(.caption, .semibold)).foregroundStyle(Color.jfMuted) }
                    else if names.isEmpty { Image(systemName: "questionmark.circle.dashed").foregroundStyle(Color.jfWarning).accessibilityLabel(Text("nobody yet")) }
                    else { ForEach(trip.people.filter { names.contains($0.name) }) { PersonAvatar(person: $0, size: 22) } }
                }
            }
            Spacer(minLength: 4)
            VStack(alignment: .trailing, spacing: 3) {
                HStack(spacing: 4) {
                    if let state { Image(systemName: state.symbol).font(.caption).foregroundStyle(state.color).accessibilityLabel(Text(state.text(e, trip))) }
                    Text(Fmt.money(e.value, e.code)).font(.jf(.body, .semibold).monospacedDigit()).foregroundStyle(Color.jfText)
                }
                if let charged = e.charged { Text(Fmt.money(charged.amount, charged.currency)).font(.jf(.caption).monospacedDigit()).foregroundStyle(Color.jfMuted) }
                if e.planned { pill("To pay", "clock", .jfWarning) }
                else if e.paidBack { pill("Settled", "checkmark.circle", .jfSuccess) }
                else if let open = e.split.open, open != 0 { pill("\(Fmt.money(abs(open), e.code)) open", "clock", .jfWarning) }
            }
        }
        .contentShape(Rectangle())
        .accessibilityElement(children: .combine)
    }

    private func pill(_ text: LocalizedStringKey, _ symbol: String, _ color: Color) -> some View {
        HStack(spacing: 3) { Image(systemName: symbol); Text(text) }.font(.jf(.caption, .semibold)).foregroundStyle(color)
    }
}
