import SwiftUI
import TravelRepoKit

/// A line of a day in TravelRepo's agenda: a plan, a check-in, a check-out or the rest of what began before.
struct DayEntry: Hashable { let item: UUID; let role: String; let start: Date?; let end: Date?; let steps: [UUID] }

extension Calendar {
    /// A day as a dictionary key. A calendar's own components carry more than year, month and day, so two that mean the
    /// same day can hash differently; this one equals what `dayComponents` reads from TravelRepo's dates.
    func ymd(_ date: Date) -> DateComponents {
        let c = dateComponents([.year, .month, .day], from: date)
        return DateComponents(year: c.year, month: c.month, day: c.day)
    }
}

extension Trip {
    /// The agenda's lines per day as last read; a day whose plans changed since falls back to the plans themselves.
    nonisolated(unsafe) static var agenda: [UUID: [DateComponents: [DayEntry]]] = [:]

    func lines(on day: Date) -> [DayEntry] {
        let shown = items(on: day)
        guard let known = Trip.agenda[id]?[calendar.ymd(day)] else {
            return shown.map { DayEntry(item: $0.id, role: "item", start: $0.when.start, end: $0.when.end, steps: []) }
        }
        let kept = known.filter { e in item(e.item).map { i in e.role != "item" && e.role != "check_in" || shown.contains { $0.id == i.id } } ?? false }
        let covered = Set(kept.flatMap { [$0.item] + $0.steps })
        // Moved or added since the last read.
        let new = shown.filter { !covered.contains($0.id) }.map { DayEntry(item: $0.id, role: "item", start: $0.when.start, end: $0.when.end, steps: []) }
        return kept + new
    }

    /// Where to go for a plan: a journey's destination, otherwise the plan's place.
    func target(_ i: PlanItem) -> Place? { place(i.transport?.to) ?? place(i.stay?.place ?? i.place) }

    /// The zone the travellers are in: where the last plan that began took place, or ends once it is over; before the
    /// first one, where that one starts. Not the phone's, which may still be on home time.
    func currentZone(_ now: Date) -> TimeZone {
        let plans = items.filter { $0.status != .cancelled && $0.status != .idea && $0.when.start != nil && $0.parent == nil }.sorted { $0.when.start! < $1.when.start! }
        guard let first = plans.first else { return calendar.timeZone }
        guard let last = plans.last(where: { $0.when.start! <= now }) else { return first.when.zone }
        return (last.when.end ?? last.when.start!) <= now ? last.when.endZone : last.when.zone
    }

    /// The day it is where the travellers are, as Today shows it.
    func today(_ now: Date) -> Date {
        var here = Calendar(identifier: .gregorian); here.timeZone = currentZone(now)
        return calendar.date(from: here.dateComponents([.year, .month, .day], from: now)) ?? calendar.startOfDay(for: now)
    }
}

extension Place {
    /// A place may have an address only; it is then found by that, never by the coordinates 0, 0.
    var hasPosition: Bool { latitude != 0 || longitude != 0 }
}

private struct Card<Content: View>: View {
    @ViewBuilder var content: Content
    var body: some View {
        content.padding(14).frame(maxWidth: .infinity, alignment: .leading).background(Color.jfSurface, in: RoundedRectangle(cornerRadius: 14, style: .continuous))
            .overlay(RoundedRectangle(cornerRadius: 14, style: .continuous).strokeBorder(Color.jfLine))
    }
}

/// Says that the trip has errors, which can keep changes from being saved.
struct TripErrors: View {
    let tripID: UUID
    @Environment(TripStore.self) private var store
    var body: some View {
        let errors = { _ = store.travelRepoReads; return (TravelRepoBridge.all[tripID]?.diagnostics ?? []).filter { $0.severity.lowercased() == "error" } }()
        if let why = store.syncProblem[tripID] ?? TravelRepoBridge.all[tripID]?.versionProblem {
            Card { Label { Text(why) } icon: { Image(systemName: "exclamationmark.icloud").foregroundStyle(Color.jfWarning) }.font(.jf(.callout)) }
        }
        if !errors.isEmpty {
            Card {
                VStack(alignment: .leading, spacing: 6) {
                    Label("This trip has errors", systemImage: "exclamationmark.triangle").font(.jf(.body, .semibold)).foregroundStyle(Color.jfDanger)
                    Text("Changes cannot be saved until they are repaired, best on a computer.").font(.jf(.footnote)).foregroundStyle(Color.jfMuted)
                    ForEach(Array(errors.prefix(3).enumerated()), id: \.offset) { _, e in Text(e.code).font(.system(.caption, design: .monospaced)).foregroundStyle(Color.jfMuted) }
                }
            }
        }
    }
}

/// Payments still to pay that are due within two weeks or overdue, soonest first, opening the costs.
struct PaymentsDue: View {
    let trip: Trip
    let now: Date
    @Environment(AppNavigation.self) private var nav
    @Environment(TripStore.self) private var store
    @State private var due: [CostDetails.Expense] = []
    var body: some View {
        Group {
            if !due.isEmpty {
                VStack(alignment: .leading, spacing: 10) {
                    Overline("Payments due")
                    Button { nav.tab = .more; nav.morePath = [.costs] } label: {
                        Card {
                            VStack(alignment: .leading, spacing: 8) {
                                ForEach(due) { e in
                                    HStack(spacing: 12) {
                                        Image(systemName: "creditcard").foregroundStyle(Color.jfWarning).frame(width: 28).accessibilityHidden(true)
                                        VStack(alignment: .leading, spacing: 2) {
                                            Text(e.title).font(.jf(.body, .semibold)).foregroundStyle(Color.jfText)
                                            Text("\(Fmt.money(e.value, e.code)) · due \(CostsView.day(e.due ?? ""))").font(.jf(.footnote)).foregroundStyle(Color.jfMuted)
                                        }
                                    }
                                }
                            }
                        }
                    }
                    .buttonStyle(.plain)
                }
            }
        }
        .task(id: store.travelRepoReads) {
            let c = trip.calendar.dateComponents([.year, .month, .day], from: now.addingTimeInterval(14 * 86400)), until = String(format: "%04d-%02d-%02d", c.year ?? 0, c.month ?? 1, c.day ?? 1)
            due = ((try? await TravelRepoBridge.all[trip.id]?.costDetails())?.expenses ?? []).filter { $0.planned && !$0.idea && ($0.due ?? "9999") <= until }.sorted { ($0.due ?? "") < ($1.due ?? "") }
        }
    }
}

/// Before the trip: how long until it starts, what to get ready, and its first journey, stay and activity.
struct BeforeTrip: View {
    let trip: Trip
    let now: Date
    @Environment(AppNavigation.self) private var nav
    @Environment(TripStore.self) private var store
    var body: some View {
        let days = trip.calendar.dateComponents([.day], from: trip.today(now), to: trip.calendar.date(from: trip.start) ?? now).day ?? 0
        let planned = trip.items.filter { $0.when.isScheduled && $0.status != .idea && $0.status != .cancelled }.sorted { ($0.when.start ?? .distantFuture) < ($1.when.start ?? .distantFuture) }
        let firsts: [(LocalizedStringKey, PlanItem?)] = [("First journey", planned.first { $0.transport != nil }), ("First stay", planned.first { $0.stay != nil }), ("First activity", planned.first { $0.transport == nil && $0.stay == nil })]
        VStack(alignment: .leading, spacing: 18) {
            Text(days == 1 ? "The trip starts tomorrow." : days > 1 ? "The trip starts in \(days) days." : "No dates yet.").font(.jf(.title2, .bold)).padding(.top, 8)
            VStack(alignment: .leading, spacing: 10) {
                Overline("Before the trip")
                Card {
                    VStack(spacing: 12) {
                        row("backpack", "Packing lists", trip.packing.isEmpty ? Text("No packing lists yet.") : trip.toPack > 0 ? Text("\(trip.toPack) things still to pack") : Text("Everything is packed"), .packing)
                        if trip.openTasks > 0 { row("checklist", "Tasks", Text("\(trip.openTasks) open"), .tasks) }
                    }
                }
            }
            PaymentsDue(trip: trip, now: now)
            if firsts.contains(where: { $0.1 != nil }) {
                Overline("How it starts")
                ForEach(Array(firsts.enumerated()), id: \.offset) { _, f in
                    if let item = f.1 {
                        Button { nav.detail = item.id } label: { Card { VStack(alignment: .leading, spacing: 6) { Text(f.0).font(.jf(.caption, .semibold)).foregroundStyle(Color.jfMuted); PlanRow(trip: trip, item: item, showDay: true) } } }.buttonStyle(.plain)
                    }
                }
            }
            Button { nav.tab = .plan } label: { Label("Open the plan", systemImage: "list.bullet") }.buttonStyle(.bordered)
        }
    }
    private func row(_ symbol: String, _ title: LocalizedStringKey, _ detail: Text, _ page: MorePage) -> some View {
        Button { nav.tab = .more; nav.morePath = [page] } label: {
            HStack(spacing: 12) {
                Image(systemName: symbol).foregroundStyle(Color.jfAccent).frame(width: 28).accessibilityHidden(true)
                VStack(alignment: .leading, spacing: 1) { Text(title).font(.jf(.body, .semibold)).foregroundStyle(Color.jfText); detail.font(.jf(.footnote)).foregroundStyle(Color.jfMuted) }
                Spacer(); Image(systemName: "chevron.right").font(.footnote).foregroundStyle(Color.jfSubtleText).accessibilityHidden(true)
            }
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
    }
}

/// After the trip: the trip in numbers, what it cost, and paying back when someone still owes someone.
struct TripRecap: View {
    let trip: Trip
    @Environment(TripStore.self) private var store
    @State private var costs: CostDetails?
    @State private var paying = false
    var body: some View {
        VStack(alignment: .leading, spacing: 16) {
            Text("Welcome back.").font(.jf(.title2, .bold)).padding(.top, 8)
            HStack(spacing: 12) {
                stat("\(trip.days.count)", Text("days")); stat("\(trip.places.count)", Text("places")); stat("\(trip.items.filter { $0.when.isScheduled }.count)", Text("plans"))
            }
            if let paid = costs?.paid_in, paid.total > 0 {
                let rest = paid.not_converted.isEmpty ? "" : " + " + CostsView.text(paid.not_converted)
                Text("Spent together: \(CostsView.approx(paid.estimated > 0) + Fmt.money(paid.total, paid.currency) + rest)").font(.jf(.callout)).foregroundStyle(Color.jfMuted)
            }
            if costs?.balances.isEmpty == false {
                Text("Not everything is paid back yet.").font(.jf(.callout)).foregroundStyle(Color.jfMuted)
                Button { paying = true } label: { Label("Pay back…", systemImage: "wallet.bifold") }.buttonStyle(.bordered)
            }
        }
        .task(id: store.travelRepoReads) { costs = try? await TravelRepoBridge.all[trip.id]?.costDetails() }
        .sheet(isPresented: $paying) { PayBackSheet(tripID: trip.id) }
    }
    private func stat(_ value: String, _ label: Text) -> some View {
        Card { VStack(alignment: .leading, spacing: 2) { Text(value).font(.jf(.title, .bold).monospacedDigit()).foregroundStyle(Color.jfAccent); label.font(.jf(.footnote)).foregroundStyle(Color.jfMuted) } }.accessibilityElement(children: .combine)
    }
}

// MARK: Search over everything

/// Results of TravelRepo's search over the whole trip, grouped by kind; each opens what it found.
struct SearchResults: View {
    let trip: Trip
    let query: String
    @Environment(AppNavigation.self) private var nav
    @State private var hits: [SearchHit]?
    @State private var kind = ""
    @State private var byDay = false
    @AppStorage("search.notes") private var notes = true
    /// Goes to the page the hit is on, such as its day in the plan.
    var show: ((SearchHit) -> Void)? = nil
    @Binding var sheet: TripSheet?

    static let kinds: [(String, LocalizedStringKey, String)] = [("plan", "Plans", "calendar"), ("booking", "Bookings", "ticket"), ("cost", "Costs", "wallet.bifold"), ("place", "Places", "mappin.circle"), ("task", "Tasks", "checklist"), ("packing", "Packing lists", "backpack"), ("note", "Notes", "note.text"), ("file", "Files", "paperclip")]

    var body: some View {
        Group {
            if let hits {
                if hits.isEmpty { ContentUnavailableView.search(text: query); Toggle("Also search notes", isOn: $notes) } else { filters(hits) }
                let wanted = hits.filter { kind.isEmpty || $0.kind == kind }
                ForEach(groups(wanted), id: \.id) { g in
                        Section {
                            ForEach(g.hits, id: \.id) { h in
                                let symbol = Self.kinds.first { $0.0 == h.kind }?.2 ?? "doc"
                                if h.kind == "plan", let item = trip.item(UUID(uuidString: h.id)) { Button { nav.detail = item.id } label: { PlanRow(trip: trip, item: item, showDay: true) }.buttonStyle(.plain).swipeActions { jump(h) }.contextMenu { jump(h) } }
                                else {
                                    Button { open(h) } label: {
                                        Label {
                                            VStack(alignment: .leading, spacing: 1) {
                                                Text(h.title).foregroundStyle(Color.jfText)
                                                let detail = [h.day.map { CostsView.day($0, weekday: true) }, h.matches.first { $0.text != h.title }?.text].compactMap { $0 }.joined(separator: " · ")
                                                if !detail.isEmpty { Text(detail).font(.jf(.footnote)).foregroundStyle(Color.jfMuted).lineLimit(2) }
                                            }
                                        } icon: { Image(systemName: symbol) }
                                    }
                                    .swipeActions { jump(h) }.contextMenu { jump(h) }
                                }
                            }
                        } header: { if let title = g.title { Overline(title) } else { Text(g.day) } }
                }
            } else { ProgressView() }
        }
        .task(id: "\(notes)\(query)") {
            try? await Task.sleep(for: .milliseconds(200))
            guard !Task.isCancelled else { return }
            hits = (try? await TravelRepoBridge.all[trip.id]?.session.find(query, notes: notes, limit: 100)) ?? []
        }
    }

    private struct Group_: Identifiable { let id: String; let title: LocalizedStringKey?; let day: String; let hits: [SearchHit] }

    private func groups(_ hits: [SearchHit]) -> [Group_] {
        // TravelRepo's order is best first.
        let best = hits.count > 1 && kind.isEmpty ? hits.first : nil
        let top = best.map { [Group_(id: "best", title: "Best match", day: "", hits: [$0])] } ?? []
        let hits = best == nil ? hits : Array(hits.dropFirst())
        return top + rest(hits)
    }

    private func rest(_ hits: [SearchHit]) -> [Group_] {
        guard byDay else { return Self.kinds.compactMap { k, title, _ in let f = hits.filter { $0.kind == k }; return f.isEmpty ? nil : Group_(id: k, title: title, day: "", hits: f) } }
        return Dictionary(grouping: hits) { $0.day ?? "" }.sorted { ($0.key.isEmpty ? "9999" : $0.key) < ($1.key.isEmpty ? "9999" : $1.key) }.map {
            Group_(id: "d" + $0.key, title: $0.key.isEmpty ? "Without a day" : nil, day: $0.key.isEmpty ? "" : CostsView.day($0.key, weekday: true), hits: $0.value.sorted { ($0.start ?? "") < ($1.start ?? "") })
        }
    }

    private func filters(_ hits: [SearchHit]) -> some View {
        Section {
            Picker("Show", selection: $kind) {
                Text("Everything (\(hits.count))").tag("")
                ForEach(Self.kinds.filter { k in hits.contains { $0.kind == k.0 } }, id: \.0) { k, title, symbol in Label { Text(title) + Text(verbatim: " (\(hits.filter { $0.kind == k }.count))") } icon: { Image(systemName: symbol) }.tag(k) }
            }
            Toggle("Group by day", isOn: $byDay)
            Toggle("Also search notes", isOn: $notes)
        }
        .onChange(of: query) { if !kind.isEmpty, !hits.contains(where: { $0.kind == kind }) { kind = "" } }
    }

    @ViewBuilder private func jump(_ h: SearchHit) -> some View {
        if let show { Button { show(h) } label: { Label("Show where it is", systemImage: "arrow.turn.up.right") }.tint(.jfAccent) }
    }

    private func open(_ h: SearchHit) {
        let id = UUID(uuidString: h.id)
        switch h.kind {
        case "booking": sheet = .booking(h.id)
        case "cost": sheet = .expense(h.id)
        case "task": sheet = .task(h.id)
        case "file": sheet = TravelRepoBridge.all[trip.id]?.documents().first { $0.id == id }.map { .document($0) }
        case "note": if trip.item(id) != nil { nav.detail = id } else { sheet = id.map { .note($0) } }
        case "packing": sheet = trip.packing.flatMap(\.items).first { $0.id == id }.map { .packing($0) }
        case "place": sheet = id.map { .place($0) }
        default: nav.detail = id
        }
    }
}
