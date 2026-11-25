import SwiftUI

/// The plan, one day at a time, chosen on a strip of days. Ideas without a time have their own section.
struct PlanView: View {
    let tripID: UUID
    @Environment(TripStore.self) private var store
    @Environment(AppNavigation.self) private var nav
    @State private var query = ""
    /// What TravelRepo's search found for `query` (plans and places, best first).
    @State private var found: (query: String, items: [UUID], places: [UUID])?
    @State private var mode = Mode.day
    @State private var editing: PlanItem?
    @State private var confirmDelete: PlanItem?
    @State private var showTimetable = false
    @Environment(\.dynamicTypeSize) private var size
    @State private var pulled = 0.0
    @State private var sheet: TripSheet?

    enum Mode: Hashable { case day, all, ideas }

    var body: some View {
        @Bindable var nav = nav
        let trip = store.trip(tripID)
        let days = trip.days
        let selected = min(max(nav.planDay, 0), max(days.count - 1, 0))
        NavigationStack {
            JFList {
                if query.isEmpty && mode != .all {
                    // The strip is the list's first row rather than a safe-area inset: as an inset it stayed invisible
                    // when the trip was opened from the trip list.
                    Section {
                        DayStrip(trip: trip, selected: $nav.planDay, now: store.now, showsSelection: mode == .day)
                            .listRowInsets(EdgeInsets())
                            .listRowBackground(Color.clear)
                            .listRowSeparator(.hidden)
                    }
                    .listSectionSpacing(0)
                }
                if !query.isEmpty {
                    searchResults(trip)
                } else {
                    switch mode {
                    case .day:
                        if days.indices.contains(selected) { daySection(trip, index: selected, day: days[selected]) }
                    case .all:
                        ForEach(Array(days.enumerated()), id: \.offset) { i, d in daySection(trip, index: i, day: d) }
                    case .ideas:
                        ideasSection(trip)
                    }
                }
            }
            .listStyle(.insetGrouped)
            .jfBackground()
            .onScrollGeometryChange(for: Double.self) { g in
                // How far the page is pulled past its end; a short day ends where the screen does.
                let end = max(g.contentSize.height + g.contentInsets.bottom, g.containerSize.height - g.contentInsets.top)
                return g.contentOffset.y + g.containerSize.height - end
            } action: { _, past in pulled = past }
            .onScrollPhaseChange { old, new in
                // Let go after pulling well past the end: the next day.
                if old == .interacting, new != .interacting, pulled > 110, mode == .day, query.isEmpty, selected + 1 < days.count { page(selected + 1) }
            }
            .navigationTitle("Plan")
            .searchable(text: $query, placement: .navigationBarDrawer(displayMode: .automatic), prompt: Text("Plans, places, bookings"))
            .task(id: query) { found = await TravelRepoBridge.all[tripID]?.search(query, trip: tripID) }
            .refreshable { await store.sync(tripID) }
            .tripToolbar(trip)
            .toolbar {
                ToolbarItem(placement: .topBarTrailing) {
                    Menu {
                        Picker(selection: $mode) {
                            Label("One day", systemImage: "calendar.day.timeline.left").tag(Mode.day)
                            Label("Whole trip", systemImage: "list.bullet").tag(Mode.all)
                            Label("Ideas", systemImage: "lightbulb").tag(Mode.ideas)
                        } label: { Text("Show") }
                        Divider()
                        Button { nav.planDay = trip.days.firstIndex(of: trip.calendar.startOfDay(for: store.now)) ?? 0; mode = .day } label: { Label("Go to today", systemImage: "sun.horizon") }
                        Button { showTimetable = true } label: { Label("Timetable", systemImage: "tablecells") }
                        Button {
                            Task {
                                guard await SystemCalendar.allowed(ask: true) else { store.problem = String(localized: "Jourfold may not use the calendar. Allow it under Settings → Apps → Jourfold."); return }
                                UserDefaults.standard.set(true, forKey: "calendar")
                                await SystemCalendar.update(store.trips)
                                nav.toast = String(localized: "The trips are in the Calendar app now")
                            }
                        } label: { Label("Show in the Calendar app", systemImage: "calendar.badge.plus") }
                    } label: { Image(systemName: "line.3.horizontal.decrease") }
                    .accessibilityLabel(Text("View options"))
                }
            }
            .sheet(item: $sheet) { $0.view(tripID) }
            .sheet(item: $editing) { item in ItemEditor(tripID: tripID, original: item) }
            .navigationDestination(isPresented: $showTimetable) { TimetableView(tripID: tripID, day: selected) }
            .onAppear {
                if nav.timetable { nav.timetable = false; showTimetable = true }
                if nav.ideas { nav.ideas = false; mode = .ideas }
            }
            .onChange(of: nav.planDay) { if mode == .ideas { withAnimation { mode = .day } } }
            .deleteConfirmation(tripID: tripID, ids: confirmDelete.map { [$0.id] }, title: confirmDelete?.title) { confirmDelete = nil }
        }
    }

    @ViewBuilder
    private func daySection(_ trip: Trip, index: Int, day: Date) -> some View {
        let items = trip.items(on: day)
        Section {
            if items.isEmpty, trip.lines(on: day).isEmpty {
                ContentUnavailableView { Label("Nothing planned", systemImage: "calendar") } description: { Text("Add a plan or pull an idea into this day.") } actions: {
                    Button("Add a plan") { nav.quickAddKind = .item; nav.quickAdd = true }.buttonStyle(.bordered)
                }
            }
            if let w = DayWeather.of(trip, day) {
                Label { Text("\(w.high)° / \(w.low)° · \(String(localized: w.summary))\(w.kind == "climate" ? "" : " · \(w.rain) %")") } icon: { Image.weather(w.symbol) }
                    .font(.jf(.footnote)).foregroundStyle(Color.jfMuted)
            }
            if mode == .day { WeatherProblem(tripID: trip.id) }
            ForEach(trip.lines(on: day), id: \.self) { line in
                if let item = trip.item(line.item) {
                    switch line.role {
                    case "item", "check_in":
                        row(trip, item)
                        // A group's steps unfold below it.
                        if !line.steps.isEmpty {
                            DisclosureGroup { ForEach(line.steps, id: \.self) { s in if let step = trip.item(s) { row(trip, step) } } } label: { Text("\(line.steps.count) steps").font(.jf(.footnote, .semibold)).foregroundStyle(Color.jfMuted) }
                        }
                    default:
                        Button { nav.detail = item.id } label: {
                            Label { Text(line.role == "check_out" ? "Check-out · \(item.title)" : "Continues · \(item.title)") + Text(verbatim: (line.role == "check_out" ? item.when.end : line.end).map { " · " + Fmt.time($0, item.when.endZone) } ?? "") } icon: { Image(systemName: line.role == "check_out" ? "door.left.hand.open" : "arrow.turn.down.right") }
                                .font(.jf(.subheadline)).foregroundStyle(Color.jfMuted)
                        }
                    }
                }
            }
            if let stay = trip.stay(on: day) {
                Label { Text("Night at \(stay.title)") + Text(verbatim: trip.nightText(on: day).map { " · " + $0 } ?? "") } icon: { Image(systemName: "moon.zzz.fill") }
                    .font(.jf(.footnote, .medium)).foregroundStyle(Color.jfMuted)
                    .listRowBackground(Color.jfSubtle)
            }
            // Ideas without a day close every day, to pull one in.
            if mode == .day, !trip.ideas.isEmpty {
                DisclosureGroup { ForEach(trip.ideas) { row(trip, $0) } } label: { Label("Ideas (\(trip.ideas.count))", systemImage: "lightbulb").font(.jf(.footnote, .semibold)).foregroundStyle(Color.jfMuted) }
            }
            if mode == .day, index + 1 < trip.days.count {
                Button { page(index + 1) } label: { Label("Next day · \(Fmt.shortDay(trip.days[index + 1], trip.calendar.timeZone))", systemImage: "arrow.down") }.font(.jf(.footnote, .semibold))
            }
        } header: {
            FlexStack(spacing: 4) {
                Text(Fmt.day(day, trip.calendar.timeZone)).font(.jf(.headline)).foregroundStyle(Color.jfText)
                if !size.isAccessibilitySize { Spacer() }
                HStack(spacing: 4) {
                if mode == .day { Button { page(index - 1) } label: { Image(systemName: "chevron.left").frame(minWidth: 40, minHeight: 44).contentShape(Rectangle()) }.disabled(index == 0).accessibilityLabel(Text("Previous day")) }
                Text("Day \(index + 1)").font(.jf(.footnote, .semibold)).foregroundStyle(Color.jfMuted)
                if mode == .day { Button { page(index + 1) } label: { Image(systemName: "chevron.right").frame(minWidth: 40, minHeight: 44).contentShape(Rectangle()) }.disabled(index + 1 >= trip.days.count).accessibilityLabel(Text("Next day")) }
                if !items.isEmpty { ShareLink(item: dayText(trip, index, day, items)) { Image(systemName: "square.and.arrow.up").font(.footnote).frame(minWidth: 40, minHeight: 44).contentShape(Rectangle()) }.accessibilityLabel(Text("Share this day")) }
                }
            }
            .textCase(nil)
        }
    }

    private func page(_ day: Int) {
        withAnimation(.snappy) { nav.planDay = day }
        UISelectionFeedbackGenerator().selectionChanged()
    }

    @ViewBuilder
    private func ideasSection(_ trip: Trip) -> some View {
        Section {
            ForEach(trip.ideas) { row(trip, $0) }
            let maybe = trip.items.filter { $0.status == .idea && $0.when.isScheduled }
            ForEach(maybe) { row(trip, $0) }
        } header: { Overline("Ideas and maybes") } footer: {
            Footnote("Ideas have no fixed time yet. Drag one onto a day above, or hold it and choose a day.")
        }
    }

    private func searchResults(_ trip: Trip) -> some View {
        SearchResults(trip: trip, query: query, show: { hit in
            query = ""
            let day = (hit.day ?? trip.item(UUID(uuidString: hit.id)).flatMap { $0.when.start.map { trip.calendar.startOfDay(for: $0) } }.map { let c = trip.calendar.dateComponents([.year, .month, .day], from: $0); return String(format: "%04d-%02d-%02d", c.year!, c.month!, c.day!) })
            @MainActor func toDay() {
                if let day, let i = trip.days.firstIndex(where: { let c = trip.calendar.dateComponents([.year, .month, .day], from: $0); return String(format: "%04d-%02d-%02d", c.year!, c.month!, c.day!) == day }) { nav.planDay = i; mode = .day }
                else { mode = .ideas }
            }
            switch hit.kind {
            case "booking": nav.go(to: .bookings)
            case "cost": nav.go(to: .costs)
            case "place": nav.go(to: .places)
            case "task": nav.go(to: .tasks)
            case "packing": nav.go(to: .packing)
            case "file": nav.go(to: .files)
            default: toDay()
            }
        }, sheet: $sheet)
    }

    private func row(_ trip: Trip, _ item: PlanItem, showDay: Bool = false) -> some View {
        Button { nav.detail = item.id } label: { PlanRow(trip: trip, item: item, showDay: showDay) }
            .buttonStyle(.plain)
            .listRowInsets(EdgeInsets(top: 10, leading: 12, bottom: 10, trailing: 14))
            .draggable(item.id.uuidString)
            .contextMenu {
                Button { nav.detail = item.id } label: { Label("Open", systemImage: "arrow.up.right.square") }
                Button { editing = item } label: { Label("Edit", systemImage: "pencil") }
                // A stay's nights are changed in the stay itself.
                if item.stay == nil {
                    Menu {
                        ForEach(Array(trip.days.enumerated()), id: \.offset) { i, d in
                            Button(Fmt.shortDay(d, trip.calendar.timeZone)) { PlanMoves.move(store, tripID, item.id, to: i) }
                        }
                        Divider()
                        Button { PlanMoves.makeIdea(store, tripID, item.id) } label: { Label("Back to ideas", systemImage: "lightbulb") }
                    } label: { Label("Move to day", systemImage: "calendar.badge.clock") }
                }
                if let b = trip.booking(item.booking), !b.reference.isEmpty {
                    Button { UIPasteboard.general.string = b.reference; nav.toast = String(localized: "Copied") } label: { Label("Copy \(b.reference)", systemImage: "doc.on.doc") }
                }
                Menu {
                    ForEach(Status.allCases) { s in
                        Button { store.update(tripID, "Status of \(item.title)") { t in t.items[t.items.firstIndex { $0.id == item.id }!].status = s } } label: {
                            Label { Text(s.label) } icon: { Image(systemName: item.status == s ? "checkmark" : s.symbol) }
                        }
                    }
                } label: { Label("Status", systemImage: "flag") }
                if item.stay == nil { Button { PlanMoves.duplicate(store, tripID, item.id) } label: { Label("Duplicate", systemImage: "plus.square.on.square") } }
                if let place = trip.target(item) {
                    Button { Maps.navigate(to: place) } label: { Label("Navigate", systemImage: "location") }
                }
                ShareLink(item: shareText(trip, item)) { Label("Share", systemImage: "square.and.arrow.up") }
                Divider()
                Button(role: .destructive) { confirmDelete = item } label: { Label("Delete", systemImage: "trash") }
            } preview: {
                ItemPreview(trip: trip, item: item)
            }
            .swipeActions(edge: .trailing) {
                Button(role: .destructive) { confirmDelete = item } label: { Label("Delete", systemImage: "trash") }
                Button { editing = item } label: { Label("Edit", systemImage: "pencil") }.tint(.jfAccent)
            }
            .swipeActions(edge: .leading) {
                if item.status != .confirmed {
                    Button { store.update(tripID, "Confirmed \(item.title)") { t in t.items[t.items.firstIndex { $0.id == item.id }!].status = .confirmed } } label: { Label("Confirm", systemImage: "checkmark") }.tint(.jfSuccess)
                }
            }
    }

    private func dayText(_ trip: Trip, _ index: Int, _ day: Date, _ items: [PlanItem]) -> String {
        ([trip.title + " · " + Fmt.day(day, trip.calendar.timeZone)] + items.map { Fmt.when($0.when) + "  " + $0.title + (trip.target($0).map { " (" + $0.name + ")" } ?? "") }).joined(separator: "\n")
    }

    private func shareText(_ trip: Trip, _ item: PlanItem) -> String {
        var s = item.title
        if let start = item.when.start { s += "\n" + Fmt.day(start, item.when.zone) + " · " + Fmt.when(item.when) }
        if let p = trip.place(item.place) { s += "\n" + p.address }
        return s
    }
}

/// One plan in a list: the kind's colour bar, time, title, where, and status.
struct PlanRow: View {
    let trip: Trip
    let item: PlanItem
    var showDay = false
    @Environment(\.dynamicTypeSize) private var typeSize

    var body: some View {
        let c = item.kind.colors
        HStack(alignment: .top, spacing: 12) {
            RoundedRectangle(cornerRadius: 2).fill(c.bar).frame(width: 4).padding(.vertical, 2)
            VStack(alignment: .leading, spacing: 4) {
                HStack(alignment: .firstTextBaseline, spacing: 8) {
                    Image(systemName: item.transport?.mode.symbol ?? item.kind.symbol).font(.system(size: 14, weight: .semibold)).foregroundStyle(c.fg)
                        .accessibilityLabel(Text(item.kind.label))
                    Text(item.title).font(.jf(.body, .semibold)).foregroundStyle(Color.jfText)
                    Spacer(minLength: 4)
                    if item.status != .planned && !typeSize.isAccessibilitySize { StatusPill(status: item.status) }
                }
                if item.status != .planned && typeSize.isAccessibilitySize { StatusPill(status: item.status) }
                Text(line).font(.jf(.subheadline)).foregroundStyle(Color.jfMuted)
                if let parent = trip.item(item.parent) {
                    Label(parent.title, systemImage: "rectangle.stack").font(.jf(.caption, .medium)).foregroundStyle(Color.jfSubtleText)
                }
                if let note = zoneNote { Text(note).font(.jf(.caption)).foregroundStyle(Color.jfSubtleText) }
            }
        }
        .contentShape(Rectangle())
        .accessibilityElement(children: .combine)
    }

    private var line: String {
        var parts: [String] = []
        if showDay, let s = item.when.start { parts.append(Fmt.shortDay(s, item.when.zone)) }
        if item.stay != nil, let st = item.when.start, let en = item.when.end {
            parts.append(String(localized: "Check-in \(Fmt.time(st, item.when.zone))"))
            parts.append(String(localized: "until \(Fmt.shortDay(en, item.when.zone))"))
        } else {
            parts.append(Fmt.when(item.when))
        }
        if let t = item.transport, let from = trip.place(t.from), let to = trip.place(t.to) {
            parts.append("\(from.name) → \(to.name)")
            if let n = t.number { parts.append(n) }
        } else if let p = trip.place(item.stay?.place ?? item.place), p.name != item.title {
            parts.append(p.name)
        }
        if let b = trip.booking(item.booking) { parts.append(b.reference) }
        return parts.joined(separator: " · ")
    }

    /// The time in the second zone chosen in Settings, when it differs from the plan's.
    private var zoneNote: String? {
        guard let s = item.when.start, case .exact = item.when else { return nil }
        guard let here = SecondZone.zone(SecondZone.choice(trip.id)) else { return nil }
        guard here.secondsFromGMT(for: s) != item.when.zone.secondsFromGMT(for: s) else { return nil }
        return "\(Fmt.city(here)): \(Fmt.time(s, here))"
    }
}

/// The second time zone shown next to a plan's own: none, the phone's or a chosen one.
enum SecondZone {
    static let key = "secondZone", phone = "phone"
    static func key(_ trip: UUID) -> String { "secondZone-\(trip.uuidString)" }
    /// Told about every choice, so plans on screen show it at once.
    @Observable final class Changes { var count = 0 }
    static let changes = Changes()
    static func choose(_ zone: String, _ trip: UUID) { UserDefaults.standard.set(zone, forKey: key(trip)); changes.count += 1 }
    static func choice(_ trip: UUID) -> String { _ = changes.count; return UserDefaults.standard.string(forKey: key(trip)) ?? UserDefaults.standard.string(forKey: key) ?? phone }
    static func zone(_ choice: String) -> TimeZone? { choice == phone ? .current : TimeZone(identifier: choice) }
}

struct ItemPreview: View {
    let trip: Trip
    let item: PlanItem
    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            PlanRow(trip: trip, item: item, showDay: true)
            if !item.notes.isEmpty { Text(item.notes).font(.jf(.footnote)).foregroundStyle(Color.jfMuted).lineLimit(4) }
        }
        .padding(16)
        .frame(width: 340, alignment: .leading)
        .background(Color.jfSurface)
    }
}

/// The strip of days at the top of the plan: weekday, date, and a dot for days with plans.
struct DayStrip: View {
    let trip: Trip
    @Binding var selected: Int
    let now: Date
    var showsSelection = true
    @Environment(TripStore.self) private var store
    @State private var dropTarget: Int?
    @ScaledMetric(relativeTo: .title3) private var chipWidth: CGFloat = 50
    @ScaledMetric(relativeTo: .title3) private var chipHeight: CGFloat = 66

    var body: some View {
        let zone = trip.calendar.timeZone
        let today = trip.calendar.startOfDay(for: now)
        ScrollViewReader { proxy in
            ScrollView(.horizontal, showsIndicators: false) {
                HStack(spacing: 8) {
                    ForEach(Array(trip.days.enumerated()), id: \.offset) { i, d in
                        let isSel = i == selected && showsSelection
                        let count = trip.items(on: d).count
                        Button {
                            withAnimation(.snappy) { selected = i }
                            UISelectionFeedbackGenerator().selectionChanged()
                        } label: {
                            VStack(spacing: 2) {
                                Text(Fmt.weekdayShort(d, zone)).font(.jf(.caption, .semibold))
                                Text(Fmt.dayNumber(d, zone)).font(.jf(.title3, .bold).monospacedDigit())
                                HStack(spacing: 2) {
                                    ForEach(0..<min(count, 3), id: \.self) { _ in Circle().frame(width: 4, height: 4) }
                                }
                                .frame(height: 5)
                                .opacity(count == 0 ? 0 : 1)
                            }
                            .frame(minWidth: chipWidth, minHeight: chipHeight)
                            .padding(.horizontal, 2)
                            .foregroundStyle(isSel ? Color.jfOnAccent : (d == today ? Color.jfAccent : Color.jfText))
                            .background {
                                RoundedRectangle(cornerRadius: 14, style: .continuous)
                                    .fill(isSel ? Color.jfAccent : Color.jfSurface)
                                    .strokeBorder(d == today && !isSel ? Color.jfAccent : Color.jfLine, lineWidth: d == today && !isSel ? 1.5 : 1)
                            }
                        }
                        .buttonStyle(.plain)
                        .id(i)
                        .dropDestination(for: String.self) { ids, _ in
                            guard let id = ids.first.flatMap(UUID.init(uuidString:)) else { return false }
                            PlanMoves.move(store, trip.id, id, to: i)
                            withAnimation(.snappy) { selected = i }
                            return true
                        } isTargeted: { on in withAnimation(.snappy) { dropTarget = on ? i : (dropTarget == i ? nil : dropTarget) } }
                        .scaleEffect(dropTarget == i ? 1.1 : 1)
                        .accessibilityLabel(Text("\(Fmt.day(d, zone)), plans: \(count)"))
                        .accessibilityAddTraits(isSel ? .isSelected : [])
                    }
                }
                .padding(.horizontal, 16)
                .padding(.vertical, 8)
            }
            // Scrolls once the strip has its width: when the trip opens while the list fades out, the strip appears
            // before it is laid out, and scrolling then would leave the chips off to the side.
            .task { try? await Task.sleep(for: .milliseconds(80)); proxy.scrollTo(selected, anchor: .center) }
            .onChange(of: selected) { _, v in withAnimation { proxy.scrollTo(v, anchor: .center) } }
        }
        .background(Color.jfApp.opacity(0.001))
    }
}

/// Moving plans between days and back to the ideas, from the context menu or by dragging onto the strip of days.
enum PlanMoves {
    /// Moves a plan to another day of the trip. A plan with a time keeps its local time and length through
    /// TravelRepo's `move_to_day`, which refuses a local time that the clock skips or repeats that day (daylight
    /// saving) instead of guessing. An idea lands in the afternoon of the day.
    @discardableResult
    static func move(_ store: TripStore, _ tripID: UUID, _ itemID: UUID, to dayIndex: Int) -> Task<Void, Never>? {
        // Dragged onto a day: a stay keeps its nights.
        if store.trips.first(where: { $0.id == tripID })?.item(itemID)?.stay != nil { store.problem = String(localized: "The nights of a stay are changed in the stay itself."); return nil }
        if let bridge = TravelRepoBridge.all[tripID], let trip = store.trips.first(where: { $0.id == tripID }), let item = trip.item(itemID), item.when.isScheduled,
           trip.days.indices.contains(dayIndex) {
            let day = trip.calendar.dateComponents([.year, .month, .day], from: trip.days[dayIndex])
            let text = String(format: "%04d-%02d-%02d", day.year ?? 0, day.month ?? 1, day.day ?? 1)
            let task = Task {
                await bridge.flush()
                do { _ = try await bridge.session.moveToDay(TravelRepoBridge.key(itemID), day: text); try await bridge.read() }
                catch { store.problem = TravelRepoBridge.message(error) }
                store.changes[tripID, default: []].append(Change(text: String(localized: "Moved \(item.title)")))
            }
            UIImpactFeedbackGenerator(style: .medium).impactOccurred()
            return task
        }
        defer { UIImpactFeedbackGenerator(style: .medium).impactOccurred() }
        store.update(tripID, "Moved") { t in
            guard let i = t.items.firstIndex(where: { $0.id == itemID }), t.days.indices.contains(dayIndex) else { return }
            let item = t.items[i]
            let cal = t.calendar
            let day = t.days[dayIndex]
            switch item.when {
            case .exact(let s, let sz, let e, let ez):
                // Same local time on the new day.
                var c = Calendar(identifier: .gregorian); c.timeZone = TimeZone(identifier: sz)!
                let shift = c.dateComponents([.day], from: c.startOfDay(for: s), to: c.startOfDay(for: day.addingTimeInterval(12 * 3600))).day ?? 0
                t.items[i].when = .exact(start: c.date(byAdding: .day, value: shift, to: s)!, startZone: sz, end: c.date(byAdding: .day, value: shift, to: e)!, endZone: ez)
            case .approximate(let s, let e, let z):
                var c = Calendar(identifier: .gregorian); c.timeZone = TimeZone(identifier: z)!
                let shift = c.dateComponents([.day], from: c.startOfDay(for: s), to: c.startOfDay(for: day.addingTimeInterval(12 * 3600))).day ?? 0
                t.items[i].when = .approximate(start: c.date(byAdding: .day, value: shift, to: s)!, end: c.date(byAdding: .day, value: shift, to: e)!, zone: z)
            case .dayPart(_, let p, let z):
                t.items[i].when = .dayPart(day: cal.dateComponents([.year, .month, .day], from: day), part: p, zone: z)
            case .allDay(_, let z):
                t.items[i].when = .allDay(day: cal.dateComponents([.year, .month, .day], from: day), zone: z)
            case .unscheduled:
                // An idea lands in the afternoon of that day; the person can give it a time later.
                t.items[i].when = .dayPart(day: cal.dateComponents([.year, .month, .day], from: day), part: .afternoon, zone: t.zone)
                if t.items[i].status == .idea { t.items[i].status = .planned }
            }
        }
        return nil
    }

    /// A copy of the plan with everything nested in it, through TravelRepo's `duplicate`.
    static func duplicate(_ store: TripStore, _ tripID: UUID, _ itemID: UUID) {
        guard let bridge = TravelRepoBridge.all[tripID] else { return }
        Task {
            await bridge.flush()
            do { _ = try await bridge.session.duplicate(TravelRepoBridge.key(itemID)); try await bridge.read() } catch { store.problem = TravelRepoBridge.message(error) }
        }
    }

    static func makeIdea(_ store: TripStore, _ tripID: UUID, _ itemID: UUID) {
        store.update(tripID, "Idea") { t in
            guard let i = t.items.firstIndex(where: { $0.id == itemID }) else { return }
            let minutes = t.items[i].when.start.flatMap { s in t.items[i].when.end.map { Int($0.timeIntervalSince(s) / 60) } }
            t.items[i].when = .unscheduled(minutes: minutes)
            t.items[i].status = .idea
        }
    }
}
