import SwiftUI
import MapKit

extension View {
    /// The trip's toolbar: back to the trip list on the left, sync state on the right.
    func tripToolbar(_ trip: Trip) -> some View { modifier(TripToolbar(trip: trip)) }
}

struct TripToolbar: ViewModifier {
    let trip: Trip
    @Environment(AppNavigation.self) private var nav
    @Environment(TripStore.self) private var store

    func body(content: Content) -> some View {
        content.toolbar {
            ToolbarItem(placement: .topBarLeading) {
                Button { nav.openTrip = nil } label: { Image(systemName: "chevron.backward").frame(minWidth: 44, minHeight: 44).contentShape(Rectangle()) }
                    .accessibilityLabel(Text("Trips"))
            }
            ToolbarItem(placement: .topBarTrailing) {
                let changes = store.changes[trip.id]?.count ?? 0
                Button { nav.tab = .more; nav.morePath = [.sync] } label: {
                    if store.isSyncing { ProgressView() }
                    else { Image(systemName: changes > 0 ? "arrow.triangle.2.circlepath.circle.fill" : "checkmark.icloud") }
                }
                .accessibilityLabel(changes > 0 ? Text("\(changes) changes not saved as a version") : Text("Synced"))
            }
        }
    }
}

struct TodayView: View {
    let tripID: UUID
    @Environment(TripStore.self) private var store
    @Environment(AppNavigation.self) private var nav
    @Environment(\.openURL) private var openURL

    var body: some View {
        let trip = store.trip(tripID)
        let today = trip.today(store.now)
        let dayIndex = trip.days.firstIndex(of: today)
        NavigationStack {
            ScrollView {
                VStack(alignment: .leading, spacing: 22) {
                    Text(subtitle(trip, dayIndex: dayIndex, today: today))
                        .font(.jf(.subheadline, .medium)).foregroundStyle(Color.jfMuted)
                        .padding(.bottom, -8)
                    if dayIndex != nil, let clocks = clocks(trip) {
                        Label(clocks, systemImage: "clock.badge.exclamationmark").font(.jf(.footnote)).foregroundStyle(Color.jfMuted)
                    }
                    TripErrors(tripID: trip.id)
                    if store.me[trip.id] == nil, !trip.people.isEmpty, WelcomeSheet.seen(trip.id), UserDefaults.standard.string(forKey: "me-\(trip.id.uuidString)") == nil, !DebugTools.flag("demo") {
                        Button { nav.welcome = trip.id } label: { Label("Choose who you are, so reminders cover your own plans", systemImage: "person.crop.circle.badge.questionmark").font(.jf(.callout)) }
                    }
                    if let dayIndex {
                        let items = trip.items(on: today)
                        let next = items.first { ($0.when.end ?? .distantPast) > store.now && $0.status != .idea }
                        if let merge = store.pendingMerge {
                            Button { nav.tab = .more; nav.morePath = [.sync, .merge] } label: {
                                Label("\(merge.from) changed the same plans. Choose what to keep.", systemImage: "arrow.triangle.merge")
                                    .font(.jf(.callout, .semibold)).foregroundStyle(Color.jfWarning)
                                    .frame(maxWidth: .infinity, alignment: .leading)
                                    .padding(14)
                                    .background(Color.jfWarning.opacity(0.12), in: RoundedRectangle(cornerRadius: 14, style: .continuous))
                            }
                            .buttonStyle(.plain)
                        }
                        if trip.days.count > 1, dayIndex == 0 || dayIndex == trip.days.count - 1 {
                            Label(dayIndex == 0 ? "Today it begins. Have a wonderful trip!" : "The last day. Make the most of it!", systemImage: dayIndex == 0 ? "party.popper" : "sunset").font(.jf(.body, .semibold))
                        }
                        if let next { NextCard(trip: trip, item: next, now: store.now) }
                        if let w = DayWeather.of(trip, today) { WeatherLine(weather: w, place: trip.place(trip.stay(on: today)?.place)?.city ?? Fmt.city(trip.calendar.timeZone)) }
                        else { WeatherOffer(tripID: trip.id) }
                        WeatherProblem(tripID: trip.id)
                        NoticeOffer()
                        DayLine(trip: trip, items: items, now: store.now)
                        PaymentsDue(trip: trip, now: store.now)
                        CancellationNotice(trip: trip, now: store.now)
                        if let stay = trip.stay(on: today) { TonightCard(trip: trip, stay: stay, night: trip.nightText(on: today)) }
                        DueTasks(trip: trip, now: store.now)
                        TomorrowTeaser(trip: trip, day: dayIndex)
                    } else if (trip.calendar.date(from: trip.end) ?? .distantFuture) < store.now {
                        TripRecap(trip: trip)
                    } else {
                        NoticeOffer()
                        BeforeTrip(trip: trip, now: store.now)
                    }
                }
                .padding(.horizontal, 16)
                .padding(.bottom, 24)
                .frame(maxWidth: 720)
                .frame(maxWidth: .infinity)
            }
            .background(Color.jfApp.ignoresSafeArea())
            .refreshable {
                await store.sync(trip.id)
                UIImpactFeedbackGenerator(style: .light).impactOccurred()
            }
            .navigationTitle("Today")
            .tripToolbar(trip)
        }
    }

    /// Says whose time Today shows when the phone is on another one.
    private func clocks(_ trip: Trip) -> String? {
        let here = trip.currentZone(store.now), phone = TimeZone.current
        guard here.secondsFromGMT(for: store.now) != phone.secondsFromGMT(for: store.now) else { return nil }
        return String(localized: "Times in \(Fmt.city(here)). This phone: \(Fmt.shortDay(store.now, phone)), \(Fmt.time(store.now, phone)) (\(Fmt.city(phone)))")
    }

    private func subtitle(_ trip: Trip, dayIndex: Int?, today: Date) -> String {
        if let dayIndex {
            String(localized: "\(trip.title) · \(Fmt.day(today, trip.calendar.timeZone)) · Day \(dayIndex + 1) of \(trip.days.count)")
        } else {
            "\(trip.title) · \(Fmt.range(trip))"
        }
    }
}

/// The next thing that happens, large, with the actions people need on the way.
struct NextCard: View {
    let trip: Trip
    @AppStorage(MapConsent.key) private var maps = ""
    let item: PlanItem
    let now: Date
    @Environment(AppNavigation.self) private var nav
    @State private var showTaxi = false

    var body: some View {
        let place = trip.target(item)
        let colors = item.kind.colors
        VStack(alignment: .leading, spacing: 14) {
            ViewThatFits(in: .horizontal) {
                HStack(alignment: .firstTextBaseline) {
                    Text(startsIn).font(.jf(.subheadline, .semibold)).foregroundStyle(Color.jfAccentSoftText).fixedSize()
                    Spacer()
                    StatusPill(status: item.status)
                }
                VStack(alignment: .leading, spacing: 6) {
                    Text(startsIn).font(.jf(.subheadline, .semibold)).foregroundStyle(Color.jfAccentSoftText)
                    StatusPill(status: item.status)
                }
            }
            HStack(alignment: .top, spacing: 12) {
                Image(systemName: item.transport?.mode.symbol ?? item.kind.symbol)
                    .font(.system(size: 20, weight: .semibold))
                    .foregroundStyle(colors.fg)
                    .frame(width: 44, height: 44)
                    .background(colors.bg, in: RoundedRectangle(cornerRadius: 12, style: .continuous))
                VStack(alignment: .leading, spacing: 4) {
                    Text(item.stay != nil ? "Check-in · \(item.title)" : item.title).font(.jf(.title3, .bold)).foregroundStyle(Color.jfText)
                    Text(timeLine).font(.jf(.subheadline)).foregroundStyle(Color.jfMuted)
                    if let place { Text(place.address).font(.jf(.subheadline)).foregroundStyle(Color.jfMuted).lineLimit(2) }
                    if let b = trip.booking(item.booking) {
                        Label(b.reference, systemImage: "ticket").font(.jf(.footnote, .medium)).foregroundStyle(Color.jfSubtleText)
                            .accessibilityLabel(Text("Booking \(b.reference)"))
                    }
                }
            }
            if let place, place.hasPosition, maps == "on" {
                MapSnapshot(coordinate: place.coordinate, symbol: item.kind.symbol, tint: colors.bar)
                    .frame(height: 120)
                    .clipShape(RoundedRectangle(cornerRadius: 12, style: .continuous))
            }
            ViewThatFits(in: .horizontal) {
                actions(place, stacked: false)
                actions(place, stacked: true)
            }
            .font(.jf(.callout, .semibold))
            .controlSize(.large)
        }
        .padding(16)
        .background(Color.jfSurface, in: RoundedRectangle(cornerRadius: 16, style: .continuous))
        .overlay(RoundedRectangle(cornerRadius: 16, style: .continuous).strokeBorder(Color.jfLine))
        .padding(.top, 4)
        .sheet(isPresented: $showTaxi) { if let place { TaxiCardView(place: place) } }
        .accessibilityElement(children: .contain)
    }

    @ViewBuilder
    private func actions(_ place: Place?, stacked: Bool) -> some View {
        let layout = stacked ? AnyLayout(VStackLayout(alignment: .leading, spacing: 10)) : AnyLayout(HStackLayout(spacing: 10))
        layout {
            if let place {
                Button { Maps.navigate(to: place) } label: {
                    Label("Navigate", systemImage: "location.fill").lineLimit(1).fixedSize().frame(maxWidth: .infinity)
                }
                .buttonStyle(.borderedProminent).foregroundStyle(Color.jfOnAccent)
            }
            HStack(spacing: 10) {
                if let place, place.localName != nil {
                    Button { showTaxi = true } label: { Label("Taxi card", systemImage: "car.fill").labelStyle(.iconOnly) }
                        .buttonStyle(.bordered)
                        .buttonBorderShape(.circle)
                }
                Button { nav.detail = item.id } label: { Label("Details", systemImage: "info").labelStyle(.iconOnly) }
                    .buttonStyle(.bordered)
                    .buttonBorderShape(.circle)
            }
        }
    }

    private var startsIn: String {
        guard let s = item.when.start else { return "" }
        if s <= now { return String(localized: "Now") }
        let minutes = Int(s.timeIntervalSince(now) / 60)
        return minutes < 60 ? String(localized: "Next · in \(minutes) min") : String(localized: "Next · in \(Fmt.duration(s.timeIntervalSince(now)))")
    }

    private var timeLine: String {
        if item.stay != nil, let s = item.when.start { return String(localized: "Check-in from \(Fmt.time(s, item.when.zone))") }
        return Fmt.when(item.when)
    }
}

extension Maps {
    static func apple(_ place: Place) {
        guard place.hasPosition else {
            // Without a position Maps looks for the address, or the name.
            var c = URLComponents(string: "https://maps.apple.com/")!
            c.queryItems = [URLQueryItem(name: "daddr", value: place.address.isEmpty ? place.name : place.address)]
            if let url = c.url { UIApplication.shared.open(url) }
            return
        }
        let item = MKMapItem(location: CLLocation(latitude: place.latitude, longitude: place.longitude), address: nil)
        item.name = place.name
        item.openInMaps(launchOptions: [MKLaunchOptionsDirectionsModeKey: MKLaunchOptionsDirectionsModeTransit])
    }
}

/// Today's plans on a vertical line, with a marker for now.
struct DayLine: View {
    let trip: Trip
    let items: [PlanItem]
    let now: Date
    @Environment(AppNavigation.self) private var nav

    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            Overline("Today's plan")
            VStack(spacing: 0) {
                ForEach(Array(items.enumerated()), id: \.element.id) { index, item in
                    let past = (item.when.end ?? .distantFuture) < now
                    if isNowSlot(index) { NowMarker(now: now, zone: trip.currentZone(now)) }
                    Button { nav.detail = item.id } label: {
                        HStack(alignment: .top, spacing: 12) {
                            VStack(spacing: 0) {
                                Circle().fill(past ? Color.jfLine : item.kind.colors.bar).frame(width: 10, height: 10).padding(.top, 5)
                                Rectangle().fill(Color.jfLine).frame(width: 2).frame(maxHeight: .infinity)
                                    .opacity(index == items.count - 1 ? 0 : 1)
                            }
                            .frame(width: 12)
                            Text(item.when.start.map { Fmt.time($0, item.when.zone) } ?? "")
                                .font(.jf(.subheadline, .semibold).monospacedDigit())
                                .foregroundStyle(past ? Color.jfSubtleText : Color.jfText)
                                .frame(minWidth: 46, alignment: .leading)
                            VStack(alignment: .leading, spacing: 2) {
                                Text(item.title).font(.jf(.body, .semibold)).foregroundStyle(past ? Color.jfSubtleText : Color.jfText)
                                Text(detail(item)).font(.jf(.footnote)).foregroundStyle(Color.jfMuted)
                            }
                            Spacer(minLength: 0)
                            Image(systemName: item.kind.symbol).font(.system(size: 13)).foregroundStyle(item.kind.colors.fg)
                                .accessibilityHidden(true)
                        }
                        .padding(.bottom, 14)
                        .contentShape(Rectangle())
                    }
                    .buttonStyle(.plain)
                    .accessibilityHint(past ? Text("Done") : Text(""))
                }
            }
            .padding(16)
            .background(Color.jfSurface, in: RoundedRectangle(cornerRadius: 16, style: .continuous))
            .overlay(RoundedRectangle(cornerRadius: 16, style: .continuous).strokeBorder(Color.jfLine))
        }
    }

    private func isNowSlot(_ index: Int) -> Bool {
        let started = items.prefix(index).allSatisfy { ($0.when.start ?? .distantPast) <= now }
        return started && (items[index].when.start ?? .distantFuture) > now
    }

    private func detail(_ item: PlanItem) -> String {
        var parts = [item.stay != nil ? String(localized: "Check-in") : Fmt.when(item.when)]
        if let t = item.transport, let to = trip.place(t.to) { parts.append("→ \(to.name)") }
        else if let p = trip.place(item.place), p.name != item.title { parts.append(p.name) }
        return parts.joined(separator: " · ")
    }
}

struct NowMarker: View {
    let now: Date
    let zone: TimeZone
    var body: some View {
        HStack(spacing: 8) {
            Circle().fill(Color.jfAccent).frame(width: 8, height: 8).padding(.leading, 2)
            Text("Now · \(Fmt.time(now, zone))").font(.jf(.caption, .bold)).foregroundStyle(Color.jfAccent)
            Rectangle().fill(Color.jfAccent).frame(height: 1.5)
        }
        .padding(.bottom, 12)
        .accessibilityElement(children: .combine)
    }
}

struct TonightCard: View {
    let trip: Trip
    let stay: PlanItem
    var night: String? = nil
    @Environment(AppNavigation.self) private var nav
    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            Overline("Tonight")
            Button { nav.detail = stay.id } label: {
                HStack(spacing: 12) {
                    Image(systemName: "bed.double.fill").foregroundStyle(Kind.accommodation.colors.fg)
                        .frame(width: 36, height: 36).background(Kind.accommodation.colors.bg, in: RoundedRectangle(cornerRadius: 10, style: .continuous))
                    VStack(alignment: .leading, spacing: 2) {
                        Text(stay.title).font(.jf(.body, .semibold)).foregroundStyle(Color.jfText)
                        if let night { Text(night).font(.jf(.footnote)).foregroundStyle(Color.jfMuted) }
                        if let end = stay.when.end {
                            Text("Check-out \(Fmt.shortDay(end, stay.when.zone)), \(Fmt.time(end, stay.when.zone))").font(.jf(.footnote)).foregroundStyle(Color.jfMuted)
                        }
                    }
                    Spacer()
                    Image(systemName: "chevron.right").font(.footnote.weight(.semibold)).foregroundStyle(Color.jfSubtleText)
                }
                .padding(14)
                .background(Color.jfSurface, in: RoundedRectangle(cornerRadius: 14, style: .continuous))
                .overlay(RoundedRectangle(cornerRadius: 14, style: .continuous).strokeBorder(Color.jfLine))
            }
            .buttonStyle(.plain)
        }
    }
}

struct DueTasks: View {
    let trip: Trip
    let now: Date
    @Environment(TripStore.self) private var store
    var body: some View {
        let due = trip.tasks.filter { !$0.done && $0.due < now.addingTimeInterval(86400) }
        if !due.isEmpty {
            VStack(alignment: .leading, spacing: 10) {
                Overline("To do")
                VStack(spacing: 0) {
                    ForEach(due) { task in
                        Button {
                            store.update(trip.id, "Task done") { t in if let i = t.tasks.firstIndex(where: { $0.id == task.id }) { t.tasks[i].done = true } }
                            UIImpactFeedbackGenerator(style: .light).impactOccurred()
                        } label: {
                            HStack(spacing: 12) {
                                Image(systemName: "circle").font(.title3).foregroundStyle(Color.jfAccent)
                                VStack(alignment: .leading, spacing: 2) {
                                    Text(task.title).font(.jf(.body, .medium)).foregroundStyle(Color.jfText)
                                    Text(task.due < trip.calendar.startOfDay(for: now) ? "Overdue · \(trip.person(task.assignee)?.name ?? "")" : "Due today · \(trip.person(task.assignee)?.name ?? "")")
                                        .font(.jf(.footnote)).foregroundStyle(task.due < now ? Color.jfWarning : Color.jfMuted)
                                }
                                Spacer()
                            }
                            .padding(14)
                            .contentShape(Rectangle())
                        }
                        .buttonStyle(.plain)
                        .accessibilityLabel(Text("Mark done: \(task.title)"))
                    }
                }
                .background(Color.jfSurface, in: RoundedRectangle(cornerRadius: 14, style: .continuous))
                .overlay(RoundedRectangle(cornerRadius: 14, style: .continuous).strokeBorder(Color.jfLine))
            }
        }
    }
}

struct TomorrowTeaser: View {
    let trip: Trip
    let day: Int
    @Environment(AppNavigation.self) private var nav
    var body: some View {
        if day + 1 < trip.days.count {
            let d = trip.days[day + 1]
            let items = trip.items(on: d)
            if let first = items.first {
                VStack(alignment: .leading, spacing: 10) {
                    Overline("Tomorrow")
                    Button { nav.planDay = day + 1; nav.tab = .plan } label: {
                        HStack {
                            VStack(alignment: .leading, spacing: 2) {
                                Text(Fmt.day(d, trip.calendar.timeZone)).font(.jf(.body, .semibold)).foregroundStyle(Color.jfText)
                                Text("\(items.count) plans · first \(first.title) at \(first.when.start.map { Fmt.time($0, first.when.zone) } ?? "")")
                                    .font(.jf(.footnote)).foregroundStyle(Color.jfMuted)
                            }
                            Spacer()
                            Image(systemName: "chevron.right").font(.footnote.weight(.semibold)).foregroundStyle(Color.jfSubtleText)
                        }
                        .padding(14)
                        .background(Color.jfSurface, in: RoundedRectangle(cornerRadius: 14, style: .continuous))
                        .overlay(RoundedRectangle(cornerRadius: 14, style: .continuous).strokeBorder(Color.jfLine))
                    }
                    .buttonStyle(.plain)
                }
            }
        }
    }
}

