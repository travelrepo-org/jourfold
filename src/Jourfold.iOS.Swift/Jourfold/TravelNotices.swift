import SwiftUI
import UserNotifications

// Travel notifications like the live app's `TravelNotices`: departures and connections, arrivals, when to leave,
// check-in and check-out, cancellation deadlines, payments, tasks and packing. Demo mode runs at a fixed moment
// of the sample trip (`TripStore.now`).

enum NoticeKind: String, CaseIterable {
    case departure, connection, arrival, leaveBy, stay, checkOut, deadline, payment, taskDue, packing, weather

    var symbol: String {
        switch self {
        case .departure: "arrow.up.right"
        case .connection: "arrow.triangle.swap"
        case .arrival: "arrow.down.right"
        case .leaveBy: "figure.walk.departure"
        case .stay: "bed.double"
        case .checkOut: "door.left.hand.open"
        case .deadline: "calendar.badge.exclamationmark"
        case .payment: "creditcard"
        case .taskDue: "checklist"
        case .packing: "backpack"
        case .weather: "cloud.bolt.rain"
        }
    }

    var label: LocalizedStringResource {
        switch self {
        case .departure: "Departure"
        case .connection: "Connection"
        case .arrival: "Arrival"
        case .leaveBy: "Time to leave"
        case .stay: "Check-in"
        case .checkOut: "Check-out"
        case .deadline: "Cancellation deadline"
        case .payment: "Payment due"
        case .taskDue: "Task due"
        case .packing: "Packing"
        case .weather: "Weather"
        }
    }
}

struct TravelNotice: Identifiable, Hashable {
    let id: String
    var kind: NoticeKind
    var at: Date
    /// The zone the time is shown in: where the person will be.
    var zone: TimeZone
    var title: String
    var body: String
    var item: UUID?
}

enum TravelNotices {
    /// Notices from `now` up to `days` ahead, earliest first. `before` is "Remind me before" in minutes.
    static func upcoming(_ trip: Trip, now: Date, before: Int, days: Int = 3) -> [TravelNotice] {
        let until = now.addingTimeInterval(TimeInterval(days) * 86400)
        let lead = TimeInterval(before * 60)
        var out: [TravelNotice] = []
        // Only what concerns the person using the phone, once it knows who that is.
        let me = TravelRepoBridge.all[trip.id]?.store.me[trip.id]
        let active = trip.items.filter { i in i.status != .cancelled && i.status != .idea && (me == nil || i.participants.isEmpty || i.participants.contains(me!)) }
        let journeys = active.filter { $0.transport != nil }.sorted { ($0.when.start ?? .distantFuture) < ($1.when.start ?? .distantFuture) }

        for leg in journeys {
            guard case .exact(let start, _, let end, _) = leg.when, let t = leg.transport else { continue }
            let from = trip.place(t.from)?.name ?? "", to = trip.place(t.to)?.name ?? ""
            let line = [t.number, t.seat].compactMap { $0 }.joined(separator: " · ")
            // A leg that starts within three hours of the previous one ending at the same place is a change.
            let into = journeys.first { p in p.transport?.to == t.from && (p.when.end.map { start.timeIntervalSince($0) } ?? -1).isBetween(0, 3 * 3600) }
            let time = Fmt.time(start, leg.when.zone)
            // A flight needs its three hours, whatever is set for the rest.
            let ahead = t.mode == .flight ? max(lead, into == nil ? 3 * 3600 : 5400) : lead
            if into != nil {
                out.append(.init(id: "depart:\(leg.id)", kind: .connection, at: start.addingTimeInterval(-min(ahead, t.mode == .flight ? 5400 : 1800)), zone: leg.when.zone,
                                 title: String(localized: "Change to \(leg.title) at \(time)"),
                                 body: [String(localized: "From \(from)"), line].filter { !$0.isEmpty }.joined(separator: " · "), item: leg.id))
            } else {
                out.append(.init(id: "depart:\(leg.id)", kind: .departure, at: start.addingTimeInterval(-ahead), zone: leg.when.zone,
                                 title: String(localized: "\(leg.title) leaves at \(time)"),
                                 body: ["\(from) → \(to)", line].filter { !$0.isEmpty }.joined(separator: " · "), item: leg.id))
            }
            let onward = journeys.first { n in n.transport?.from == t.to && (n.when.start.map { $0.timeIntervalSince(end) } ?? -1).isBetween(0, 3 * 3600) }
            let next = onward.map { String(localized: "Next: \($0.title) at \(Fmt.time($0.when.start!, $0.when.zone))") }
                ?? trip.stay(on: trip.calendar.startOfDay(for: end)).map { String(localized: "Tonight: \($0.title)") } ?? ""
            out.append(.init(id: "arrive:\(leg.id)", kind: .arrival, at: end.addingTimeInterval(-600), zone: leg.when.endZone,
                             title: String(localized: "Arriving at \(to) at \(Fmt.time(end, leg.when.endZone))"), body: next, item: onward?.id ?? leg.id))
        }

        // Plans with a place and an exact time: when to leave. Stays and journeys have their own notices.
        for item in active where item.transport == nil && item.stay == nil {
            guard case .exact(let start, _, _, _) = item.when, let place = trip.place(item.place) else { continue }
            out.append(.init(id: "leave:\(item.id)", kind: .leaveBy, at: start.addingTimeInterval(-lead), zone: item.when.zone,
                             title: String(localized: "Leave for \(item.title)"),
                             body: String(localized: "Starts at \(Fmt.time(start, item.when.zone)) · \(place.name)"), item: item.id))
        }

        for stay in active where stay.stay != nil {
            guard let start = stay.when.start, let end = stay.when.end, let place = trip.place(stay.stay?.place) else { continue }
            out.append(.init(id: "stay:\(stay.id)", kind: .stay, at: start.addingTimeInterval(-2 * 3600), zone: stay.when.zone,
                             title: String(localized: "Check-in at \(stay.title) from \(Fmt.time(start, stay.when.zone))"),
                             body: place.address, item: stay.id))
            var cal = Calendar(identifier: .gregorian); cal.timeZone = stay.when.endZone
            if let evening = cal.date(bySettingHour: 20, minute: 0, second: 0, of: end.addingTimeInterval(-86400)) {
                out.append(.init(id: "checkout:\(stay.id)", kind: .checkOut, at: evening, zone: stay.when.endZone,
                                 title: String(localized: "Check out of \(stay.title) tomorrow by \(Fmt.time(end, stay.when.endZone))"),
                                 body: String(localized: "Pack the evening before; nothing is left in the room."), item: stay.id))
            }
        }

        for booking in trip.bookings {
            guard let deadline = booking.freeCancellationUntil, deadline > now else { continue }
            // Two days before; when that has passed already, right away.
            out.append(.init(id: "deadline:\(booking.id)", kind: .deadline, at: max(deadline.addingTimeInterval(-2 * 86400), now.addingTimeInterval(60)), zone: trip.calendar.timeZone,
                             title: String(localized: "Free cancellation of \(booking.title) ends soon"),
                             body: String(localized: "Until \(Fmt.shortDay(deadline, trip.calendar.timeZone)), \(Fmt.time(deadline, trip.calendar.timeZone)) · \(booking.provider)"),
                             item: booking.items.first))
        }

        for e in trip.expenses where e.estimated {
            guard let due = e.date else { continue }
            out.append(.init(id: "payment:\(e.id)", kind: .payment, at: due.addingTimeInterval(-3 * 3600), zone: trip.calendar.timeZone,
                             title: String(localized: "Pay \(e.title): \(Fmt.money(e.amount))"),
                             body: String(localized: "Due \(Fmt.shortDay(due, trip.calendar.timeZone)), \(Fmt.time(due, trip.calendar.timeZone))"), item: e.item))
        }

        // Only what the user has to do: their own tasks and those nobody has taken on yet.
        let own = TravelRepoBridge.all[trip.id]?.store.me[trip.id]
        for task in trip.tasks where !task.done {
            let entity = TravelRepoBridge.all[trip.id]?.entities[TravelRepoBridge.key(task.id)]
            if let entity, entity["due"]?["date"]?.string == nil { continue }
            let assigned = (entity?["assigned_to"]?.array ?? []).compactMap { $0.string.flatMap(UUID.init(uuidString:)) }
            if let own, !assigned.isEmpty, !assigned.contains(own) { continue }
            var cal = Calendar(identifier: .gregorian); cal.timeZone = trip.calendar.timeZone
            let morning = cal.date(bySettingHour: 9, minute: 0, second: 0, of: task.due) ?? task.due
            let who = assigned.compactMap { trip.person($0)?.name }.joined(separator: ", ")
            out.append(.init(id: "task:\(task.id)", kind: .taskDue, at: morning, zone: trip.calendar.timeZone,
                             title: String(localized: "Today: \(task.title)"), body: who.isEmpty ? String(localized: "Nobody has taken it on yet") : String(localized: "For \(who)"), item: task.related))
        }

        if trip.toPack > 0, let first = trip.days.first {
            let cal = trip.calendar
            if let evening = cal.date(bySettingHour: 18, minute: 0, second: 0, of: first.addingTimeInterval(-86400)) {
                out.append(.init(id: "packing", kind: .packing, at: evening, zone: cal.timeZone,
                                 title: String(localized: "Tomorrow you leave"),
                                 body: String(localized: "\(trip.toPack) things still to pack"), item: nil))
            }
        }

        // Weather that matters for a plan, on the morning of its day, or two hours before when that is earlier.
        for (id, concerns) in TravelRepoBridge.all[trip.id]?.weatherConcerns ?? [:] {
            guard let item = active.first(where: { $0.id == id }), let start = item.when.start else { continue }
            var cal = Calendar(identifier: .gregorian); cal.timeZone = item.when.zone
            let morning = cal.date(bySettingHour: 7, minute: 0, second: 0, of: start) ?? start
            out.append(.init(id: "weather:\(id)", kind: .weather, at: min(morning, start.addingTimeInterval(-2 * 3600)), zone: item.when.zone,
                             title: String(localized: "Weather for \(item.title)"),
                             body: concerns.map(\.text).joined(separator: ", ") + (concerns.contains(where: \.severe) ? " · " + String(localized: "it may not go ahead as planned") : ""),
                             item: id))
        }

        return out.filter { $0.at >= now && $0.at <= until }.sorted { $0.at < $1.at }
    }
}

private extension Double {
    func isBetween(_ a: Double, _ b: Double) -> Bool { self >= a && self <= b }
}

/// Schedules the notices with the system and opens the plan when one is tapped.
@MainActor
final class NoticeCenter: NSObject, UNUserNotificationCenterDelegate {
    static let shared = NoticeCenter()
    weak var nav: AppNavigation?
    var store: TripStore?

    func install(_ nav: AppNavigation, _ store: TripStore) {
        self.nav = nav; self.store = store
        listen()
        openWaiting()
    }

    /// Asks once, then replaces everything that was scheduled. Returns false when the person said no.
    @discardableResult
    func reschedule(_ store: TripStore, on: Bool, before: Int) async -> Bool {
        // The time as it is now, not as it was when the app was opened; the demo has a fixed one.
        if !DebugTools.flag("demo") { store.now = Date() }
        let center = UNUserNotificationCenter.current()
        // The reminders people set on a plan themselves stay.
        let old = await center.pendingNotificationRequests().map(\.identifier).filter { !$0.hasPrefix("remind-") }
        guard on else { center.removePendingNotificationRequests(withIdentifiers: old); return true }
        guard (try? await center.requestAuthorization(options: [.alert, .sound, .badge])) == true else { return false }
        // iOS keeps at most 64; the nearest count.
        let all = store.trips.flatMap { trip in TravelNotices.upcoming(trip, now: store.now, before: before, days: 7).map { (trip, $0) } }.sorted { $0.1.at < $1.1.at }.prefix(50)
        center.removePendingNotificationRequests(withIdentifiers: old)
        for (trip, n) in all { await add(n, trip, after: n.at.timeIntervalSince(store.now), id: "\(trip.id.uuidString)/\(n.id)") }
        TestReport.note("notices planned \(all.count)")
        return true
    }

    private var planning: Task<Void, Never>?

    /// Plans anew shortly after something changed, without asking: opening the app, a plan moved, a sync.
    func planSoon(_ store: TripStore) {
        guard !DebugTools.flag("demo"), !DebugTools.flag("selftest"), !store.loading else { return }
        planning?.cancel()
        planning = Task {
            try? await Task.sleep(for: .seconds(2))
            if !Task.isCancelled { await planNow(store) }
        }
    }

    func planNow(_ store: TripStore) async {
        LiveJourney.follow(store)
        guard await UNUserNotificationCenter.current().notificationSettings().authorizationStatus == .authorized else { return }
        let d = UserDefaults.standard
        await reschedule(store, on: d.object(forKey: "notices") as? Bool ?? true, before: d.object(forKey: "notifyBefore") as? Int ?? 60)
    }

    /// The next notice of the open trip in five seconds, to see what one looks like.
    func showExample(_ trip: Trip, now: Date, before: Int) async {
        let center = UNUserNotificationCenter.current()
        guard (try? await center.requestAuthorization(options: [.alert, .sound])) == true,
              let n = TravelNotices.upcoming(trip, now: now, before: before).first else { return }
        await add(n, trip, after: 5, id: "example")
    }

    /// A fixed moment, whatever zone the phone is on or changes to.
    static func trigger(_ at: Date) -> UNCalendarNotificationTrigger {
        var utc = Calendar(identifier: .gregorian); utc.timeZone = TimeZone(identifier: "UTC")!
        var c = utc.dateComponents([.year, .month, .day, .hour, .minute, .second], from: at)
        c.timeZone = utc.timeZone; c.calendar = utc
        return UNCalendarNotificationTrigger(dateMatching: c, repeats: false)
    }

    private func add(_ n: TravelNotice, _ trip: Trip, after: TimeInterval, id: String? = nil) async {
        let content = UNMutableNotificationContent()
        content.title = n.title
        content.body = n.body
        content.subtitle = trip.title
        content.sound = .default
        content.threadIdentifier = trip.id.uuidString
        content.interruptionLevel = n.kind == .departure || n.kind == .connection ? .timeSensitive : .active
        if let item = n.item, [.departure, .connection, .arrival, .leaveBy].contains(n.kind), Tickets.of(trip.id, item) != nil { content.categoryIdentifier = Tickets.action }
        content.userInfo = ["trip": trip.id.uuidString, "item": n.item?.uuidString ?? ""]
        let trigger = Self.trigger(Date().addingTimeInterval(max(1, after)))
        try? await UNUserNotificationCenter.current().add(UNNotificationRequest(identifier: id ?? n.id, content: content, trigger: trigger))
    }

    // With completion handlers on the main thread: iOS stops the app when the async variants finish on another one.
    nonisolated func userNotificationCenter(_ center: UNUserNotificationCenter, willPresent notification: UNNotification, withCompletionHandler completionHandler: @escaping (UNNotificationPresentationOptions) -> Void) {
        TestReport.note("notification shown")
        completionHandler([.banner, .sound, .list])
    }

    nonisolated func userNotificationCenter(_ center: UNUserNotificationCenter, didReceive response: UNNotificationResponse, withCompletionHandler completionHandler: @escaping () -> Void) {
        TestReport.note("notification tapped")
        let info = response.notification.request.content.userInfo
        let trip = (info["trip"] as? String).flatMap(UUID.init(uuidString:))
        let item = (info["item"] as? String).flatMap(UUID.init(uuidString:))
        let ticket = response.actionIdentifier == "ticket"
        nonisolated(unsafe) let finish = completionHandler
        DispatchQueue.main.async {
            self.open(trip, item, ticket: ticket)
            finish()
        }
    }

    /// What was tapped before the trips were open, such as when a notification or a widget started the app.
    private var waiting: Target?
    private struct Target { var trip: UUID?; var item: UUID? = nil; var costs = false; var ticket = false }

    func open(_ trip: UUID?, _ item: UUID?, ticket: Bool = false) { if let trip { open(Target(trip: trip, item: item, ticket: ticket)) } }

    /// From a widget: jourfold://plan/<trip>/<plan>, jourfold://costs/<trip>, or jourfold://today for the trip that is on.
    func open(_ url: URL) {
        let ids = url.pathComponents.compactMap(UUID.init(uuidString:))
        open(Target(trip: ids.first, item: ids.dropFirst().first, costs: url.host() == "costs"))
    }

    private func open(_ target: Target) {
        guard let nav, let store, !store.loading else { waiting = target; return }
        // Without a trip, or when it is gone: the one that is on now, or comes next.
        let now = store.now
        func start(_ t: Trip) -> Date { t.calendar.date(from: t.start) ?? .distantPast }
        func end(_ t: Trip) -> Date { (t.calendar.date(from: t.end) ?? .distantPast).addingTimeInterval(86400) }
        guard let trip = store.trips.first(where: { $0.id == target.trip }) ?? store.trips.first(where: { start($0) <= now && now < end($0) }) ?? store.trips.filter({ start($0) > now }).min(by: { start($0) < start($1) }) else { return }
        nav.openTrip = trip.id
        let ticket = target.ticket ? target.item.flatMap { Tickets.of(trip.id, $0) } : nil
        nav.detail = trip.item(target.item) == nil || ticket != nil ? nil : target.item
        // Once the trip is on screen, which a cold start still has to draw.
        if ticket != nil, let item = target.item { Task { try? await Task.sleep(for: .seconds(0.7)); Tickets.show(trip.id, item) } }
        if target.costs { nav.go(to: .costs) } else { nav.tab = .today }
        TestReport.note("opened from outside: plan \(nav.detail != nil) costs \(target.costs)")
    }

    /// Set at the start of the app, or iOS does not say which notification started it.
    func listen() {
        let center = UNUserNotificationCenter.current()
        center.delegate = self
        center.setNotificationCategories([UNNotificationCategory(identifier: Tickets.action, actions: [UNNotificationAction(identifier: Tickets.action, title: String(localized: "Show ticket"), options: .foreground)], intentIdentifiers: [])])
    }

    func openWaiting() {
        guard let w = waiting else { return }
        waiting = nil; open(w)
    }
}

/// Settings → Notifications: what Jourfold will tell you over the next days, so nothing comes as a surprise.
struct NotificationsView: View {
    let tripID: UUID?
    @Environment(TripStore.self) private var store
    @AppStorage("notices") private var on = true
    @AppStorage("notifyBefore") private var before = 60
    @AppStorage("liveActivities") private var liveActivities = true
    @State private var denied = false

    var body: some View {
        let trip = tripID.map(store.trip) ?? store.trips.first ?? Trip.empty(id: UUID())
        let notices = TravelNotices.upcoming(trip, now: store.now, before: before)
        let zone = trip.calendar.timeZone
        let byDay = Dictionary(grouping: notices) { trip.calendar.startOfDay(for: $0.at) }
        JFList {
            Section {
                Toggle("Travel notifications", isOn: $on)
                Picker("Remind me before", selection: $before) {
                    Text("15 min").tag(15); Text("30 min").tag(30); Text("1 hour").tag(60); Text("3 hours").tag(180)
                }
                .disabled(!on)
                Toggle("Live Activity on the way", isOn: $liveActivities)
            } footer: {
                if denied {
                    Label("Notifications are turned off for Jourfold in the iOS settings.", systemImage: "exclamationmark.triangle")
                } else {
                    Text("Times are where you will be. Nothing leaves this iPhone; the notifications are planned here.")
                }
            }
            if on {
                if notices.isEmpty {
                    Section { Text("Nothing in the next three days.").foregroundStyle(Color.jfMuted) }
                }
                ForEach(byDay.keys.sorted(), id: \.self) { day in
                    Section {
                        ForEach(byDay[day]!) { n in NoticeRow(notice: n) }
                    } header: { Overline("\(Fmt.day(day, zone))") }
                }
                if DebugTools.inBuild {
                Section {
                    Button {
                        Task { await NoticeCenter.shared.showExample(trip, now: store.now, before: before) }
                    } label: { Label("Show the next one now", systemImage: "bell.badge") }
                } footer: { Footnote("Shows the first notification in five seconds.") }
                }
            }
        }
        .listStyle(.insetGrouped)
        .jfBackground()
        .navigationTitle("Notifications")
        // Asks for permission only when the person changes something here; opening the page only refreshes.
        .task {
            let status = await UNUserNotificationCenter.current().notificationSettings().authorizationStatus
            denied = status == .denied
            if status == .authorized { await NoticeCenter.shared.reschedule(store, on: on, before: before) }
        }
        .onChange(of: "\(on)-\(before)") { Task { denied = !(await NoticeCenter.shared.reschedule(store, on: on, before: before)) } }
    }
}

struct NoticeRow: View {
    let notice: TravelNotice
    var body: some View {
        HStack(alignment: .top, spacing: 12) {
            Image(systemName: notice.kind.symbol)
                .font(.jf(.callout, .semibold))
                .foregroundStyle(Color.jfAccent)
                .frame(width: 30, height: 30)
                .background(Color.jfAccent.opacity(0.12), in: Circle())
                .accessibilityHidden(true)
            VStack(alignment: .leading, spacing: 2) {
                HStack(alignment: .firstTextBaseline) {
                    Text(notice.kind.label).font(.jf(.caption, .semibold)).foregroundStyle(Color.jfMuted)
                    Spacer()
                    Text(Fmt.time(notice.at, notice.zone)).font(.jf(.caption, .semibold)).monospacedDigit().foregroundStyle(Color.jfMuted)
                }
                Text(notice.title).font(.jf(.subheadline, .semibold))
                if !notice.body.isEmpty { Text(notice.body).font(.jf(.footnote)).foregroundStyle(Color.jfMuted) }
            }
        }
        .padding(.vertical, 2)
        .accessibilityElement(children: .combine)
    }
}

/// Asks once whether to send travel notifications, saying what they are.
struct NoticeOffer: View {
    @Environment(TripStore.self) private var store
    @AppStorage("notices.asked") private var asked = false
    @State private var open = false
    var body: some View {
        Group {
            if open, !asked {
                VStack(alignment: .leading, spacing: 8) {
                    Label("Remind you of plans?", systemImage: "bell").font(.jf(.body, .semibold))
                    Text("Jourfold reminds you of departures, check-ins, payments and tasks. Everything stays on this phone.").font(.jf(.footnote)).foregroundStyle(Color.jfMuted)
                    HStack {
                        Button("Turn on") { asked = true; Task { await NoticeCenter.shared.reschedule(store, on: true, before: UserDefaults.standard.object(forKey: "notifyBefore") as? Int ?? 60) } }.buttonStyle(.bordered)
                        Button("Not now") { asked = true }
                    }
                }
                .padding(14).frame(maxWidth: .infinity, alignment: .leading)
                .background(Color.jfSurface, in: RoundedRectangle(cornerRadius: 14, style: .continuous))
                .overlay(RoundedRectangle(cornerRadius: 14, style: .continuous).strokeBorder(Color.jfLine))
            } else { Color.clear.frame(height: 0) }
        }
        .task {
            guard !asked, !DebugTools.flag("demo") else { return }
            open = await UNUserNotificationCenter.current().notificationSettings().authorizationStatus == .notDetermined
        }
    }
}
