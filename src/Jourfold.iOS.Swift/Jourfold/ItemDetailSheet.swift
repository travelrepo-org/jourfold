import UserNotifications
import EventKitUI
import SwiftUI
import MapKit

/// A plan's details as a floating sheet: medium first, pull up for everything.
struct ItemDetailSheet: View {
    let tripID: UUID
    let itemID: UUID
    @Environment(TripStore.self) private var store
    @Environment(\.dismiss) private var dismiss
    @State private var editing = false
    @State private var detent: PresentationDetent = .medium
    @State private var newComment = ""
    @State private var showTaxi = false
    @State private var sheet: TripSheet?
    @State private var confirmDelete = false
    @State private var live = false
    @State private var showingDocument: Attachment?
    @State private var addingEvent = false
    @State private var showingNote: Attachment?
    @State private var importing = false
    @Environment(\.openURL) private var openURL

    var body: some View {
        let trip = store.trip(tripID)
        NavigationStack {
            if let item = trip.item(itemID) {
                content(trip, item)
                    .navigationTitle(item.title)
                    .navigationBarTitleDisplayMode(.inline)
                    .toolbar {
                        ToolbarItem(placement: .topBarLeading) {
                            Button(role: .close) { dismiss() }
                        }
                        ToolbarItem(placement: .topBarTrailing) {
                            Button { editing = true } label: { Image(systemName: "pencil") }.accessibilityLabel(Text("Edit"))
                        }
                        ToolbarItem(placement: .topBarTrailing) {
                            Menu {
                                ShareLink(item: item.title) { Label("Share", systemImage: "square.and.arrow.up") }
                                if item.when.start != nil {
                                    Button { addingEvent = true } label: { Label("Add to calendar", systemImage: "calendar.badge.plus") }
                                    Button { Task { await remind(trip, item) } } label: { Label("Remind me", systemImage: "bell") }
                                }
                                Divider()
                                Button(role: .destructive) { confirmDelete = true } label: { Label("Delete", systemImage: "trash") }
                            } label: { Image(systemName: "ellipsis") }
                            .accessibilityLabel(Text("More actions"))
                        }
                    }
                    .sheet(item: $sheet) { $0.view(tripID) }
                    .sheet(isPresented: $editing) { ItemEditor(tripID: tripID, original: item) }
                    .sheet(isPresented: $showTaxi) { if let p = trip.place(item.stay?.place ?? item.place) { TaxiCardView(place: p) } }
                    .deleteConfirmation(tripID: tripID, ids: confirmDelete ? [item.id] : nil, title: item.title) {
                        if confirmDelete, store.trips.first(where: { $0.id == tripID })?.item(item.id) == nil { dismiss() }
                        confirmDelete = false
                    }
                    .sheet(item: $showingDocument) { a in DocumentView(tripID: tripID, document: a.id, name: a.name) }
                    .sheet(item: $showingNote) { NoteSheet(tripID: tripID, noteID: $0.id) }
                    .sheet(isPresented: $addingEvent) { EventEditor(title: item.title, start: item.when.start ?? Date(), end: item.when.end ?? Date(), zone: item.when.zone, location: trip.place(item.stay?.place ?? item.place)?.address).ignoresSafeArea() }
                    .sheet(isPresented: $importing) { AddDocumentSheet(tripID: tripID, target: item.id) }
            } else {
                ContentUnavailableView("This plan is gone", systemImage: "questionmark.circle")
            }
        }
        .presentationDetents([.medium, .large], selection: $detent)
        .presentationBackground(Color.jfApp)
        .presentationBackgroundInteraction(.enabled(upThrough: .medium))
    }

    /// A local notification an hour before the plan, after asking for permission.
    private func remind(_ trip: Trip, _ item: PlanItem) async {
        guard let start = item.when.start else { return }
        let center = UNUserNotificationCenter.current()
        guard (try? await center.requestAuthorization(options: [.alert, .sound])) == true else { store.problem = String(localized: "Jourfold may not send notifications. Allow them under Settings → Apps → Jourfold."); return }
        let content = UNMutableNotificationContent()
        content.title = item.title
        content.body = String(localized: "In an hour, at \(Fmt.time(start, item.when.zone)).")
        content.userInfo = ["trip": tripID.uuidString, "item": item.id.uuidString]
        let at = max(start.addingTimeInterval(-3600), Date().addingTimeInterval(5))
        let trigger = NoticeCenter.trigger(at)
        try? await center.add(UNNotificationRequest(identifier: "remind-\(item.id.uuidString)", content: content, trigger: trigger))
    }

    @ViewBuilder
    private func content(_ trip: Trip, _ item: PlanItem) -> some View {
        let place = trip.target(item)
        JFList {
            Group {
            Section {
                VStack(alignment: .leading, spacing: 10) {
                    FlexStack(spacing: 8) {
                        HStack(spacing: 5) { Image(systemName: item.transport?.mode.symbol ?? item.kind.symbol).accessibilityHidden(true); Text(item.transport.map { $0.mode.label } ?? item.kind.label).lineLimit(1) }
                            .font(.jf(.caption, .semibold)).padding(.horizontal, 8).padding(.vertical, 3)
                            .foregroundStyle(item.kind.colors.fg).background(item.kind.colors.bg, in: Capsule()).fixedSize()
                        StatusPill(status: item.status)
                    }
                    Text(item.title).font(.jf(.title2, .bold)).foregroundStyle(Color.jfText)
                    timeBlock(trip, item)
                }
                .padding(.vertical, 4)
            }
            Section {
                if let place {
                    FlexStack {
                        Button { Maps.navigate(to: place) } label: { HStack(spacing: 8) { Image(systemName: "location.fill"); Text("Navigate") }.frame(maxWidth: .infinity) }
                            .buttonStyle(.borderedProminent).foregroundStyle(Color.jfOnAccent)
                        if place.localName != nil {
                            Button { showTaxi = true } label: { HStack(spacing: 8) { Image(systemName: "car.fill"); Text("Taxi card") }.frame(maxWidth: .infinity) }
                                .buttonStyle(.bordered)
                        }
                    }
                    .font(.jf(.callout, .semibold))
                    .controlSize(.large)
                    .listRowBackground(Color.clear)
                    .listRowInsets(EdgeInsets())
                }
            }

            if item.transport != nil, item.when.start != nil {
                Section {
                    Toggle(isOn: Binding(get: { live }, set: { on in if on { LiveJourney.start(trip, item, demo: DebugTools.flag("demo")) } else { LiveJourney.end(item) }; live = on })) {
                        Label("Follow on the Lock Screen", systemImage: "platter.filled.bottom.iphone")
                    }
                    .onAppear { live = LiveJourney.isRunning(item) }
                }
            }

            if let place {
                Section {
                    if place.hasPosition, UserDefaults.standard.string(forKey: MapConsent.key) == "on" { MapSnapshot(coordinate: place.coordinate, meters: 1500, symbol: item.kind.symbol, tint: item.kind.colors.bar)
                    .frame(height: 160)
                    .listRowInsets(EdgeInsets()) }
                    NavigationLink { PlaceDetail(tripID: tripID, placeID: place.id) } label: {
                        VStack(alignment: .leading, spacing: 2) {
                            Text(place.name).font(.jf(.body, .semibold))
                            Text(place.address).font(.jf(.footnote)).foregroundStyle(Color.jfMuted)
                        }
                    }
                } header: { Overline("Place") }
            }

            PlanExtras(trip: trip, item: item, sheet: $sheet)

            Section {
                ForEach(item.participants.compactMap { trip.person($0) }) { p in
                    HStack(spacing: 10) { PersonAvatar(person: p); Text(p.name) }
                }
            } header: { Overline("Who goes") }

            Section {
                ForEach(item.attachments) { a in
                    Button {
                        if a.kind == .link { if let url = URL(string: a.name.hasPrefix("http") ? a.name : "https://" + a.name) { openURL(url) } }
                        else if a.kind == .note { showingNote = a } else { showingDocument = a }
                    } label: {
                        Label { Text(a.name).foregroundStyle(Color.jfText) } icon: { Image(systemName: FileIcon.symbol(a.kind)).foregroundStyle(Color.jfAccent) }
                    }
                    .contextMenu {
                        if a.kind != .link, a.kind != .note {
                            Button { Task { await TravelRepoBridge.all[tripID]?.detach(a.id, from: item.id) } } label: { Label("Take off this plan", systemImage: "minus.circle") }
                        }
                    }
                }
                Button { importing = true } label: { Label("Add a file or scan", systemImage: "paperclip") }
            } header: { Overline("Tickets and files") }

            if !item.notes.isEmpty {
                Section { Text(item.notes).font(.jf(.callout)).foregroundStyle(Color.jfText) } header: { Overline("Notes") }
            }

            Section {
                ForEach(item.comments) { c in
                    HStack(alignment: .top, spacing: 10) {
                        if let p = trip.person(c.author) { PersonAvatar(person: p, size: 26) }
                        VStack(alignment: .leading, spacing: 2) {
                            Text(trip.person(c.author)?.name ?? "").font(.jf(.footnote, .semibold))
                            Text(c.body).font(.jf(.callout))
                        }
                    }
                }
                HStack {
                    TextField("Add a comment", text: $newComment, axis: .vertical)
                    Button {
                        let text = newComment
                        guard let me = store.me[tripID] else { store.problem = String(localized: "Choose who you are under Travellers first, so the comment has an author."); return }
                        newComment = ""
                        Task {
                            guard let bridge = TravelRepoBridge.all[tripID] else { return }
                            let ok = await bridge.changeCosts { _ = try await bridge.session.addComment(on: TravelRepoBridge.key(item.id), author: TravelRepoBridge.key(me), body: text) }
                            if !ok { newComment = text }
                        }
                    } label: { Image(systemName: "arrow.up.circle.fill").font(.title2) }
                    .disabled(newComment.trimmingCharacters(in: .whitespaces).isEmpty)
                    .accessibilityLabel(Text("Send comment"))
                }
            } header: { Overline("Comments") }
            }
            .listRowBackground(Color.jfSurface)
        }
        .listStyle(.insetGrouped)
        .scrollContentBackground(.hidden)
    }


    @ViewBuilder
    private func timeBlock(_ trip: Trip, _ item: PlanItem) -> some View {
        if item.stay != nil, let s = item.when.start, let e = item.when.end {
            VStack(alignment: .leading, spacing: 2) {
                Text("\(Fmt.shortDay(s, item.when.zone)) – \(Fmt.shortDay(e, item.when.zone))").font(.jf(.callout, .semibold)).foregroundStyle(Color.jfText)
                Text("Check-in from \(Fmt.time(s, item.when.zone))").font(.jf(.callout)).foregroundStyle(Color.jfMuted)
            }
        } else if let s = item.when.start {
            VStack(alignment: .leading, spacing: 2) {
                Text(Fmt.day(s, item.when.zone)).font(.jf(.callout, .semibold)).foregroundStyle(Color.jfText)
                Text(Fmt.when(item.when)).font(.jf(.callout)).foregroundStyle(Color.jfMuted)
                if case .exact(_, let sz, _, let ez) = item.when, sz != ez {
                    let from = item.transport.flatMap { trip.place($0.from)?.city } ?? Fmt.city(item.when.zone)
                    let to = item.transport.flatMap { trip.place($0.to)?.city } ?? Fmt.city(item.when.endZone)
                    Text("Departs \(from) time, arrives \(to) time").font(.jf(.caption)).foregroundStyle(Color.jfSubtleText)
                }
            }
        } else {
            Text(Fmt.when(item.when)).font(.jf(.callout)).foregroundStyle(Color.jfMuted)
        }
    }
}

/// The hotel's name and address in the local script, large, to show a taxi driver.
struct TaxiCardView: View {
    let place: Place
    @Environment(\.dismiss) private var dismiss
    var body: some View {
        NavigationStack {
            VStack(alignment: .leading, spacing: 24) {
                Text("Please take me to:").font(.jf(.title3)).foregroundStyle(Color.jfMuted)
                Text(place.localName ?? place.name).font(.system(size: 40, weight: .bold)).minimumScaleFactor(0.5)
                Text(place.localAddress ?? place.address).font(.system(size: 28, weight: .medium))
                Divider()
                Text(place.name).font(.jf(.headline))
                Text(place.address).font(.jf(.subheadline)).foregroundStyle(Color.jfMuted)
                Spacer()
            }
            .padding(24)
            .frame(maxWidth: .infinity, alignment: .leading)
            .background(Color.jfSurface.ignoresSafeArea())
            .modifier(BrightScreen())
            .navigationTitle("Taxi card")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar { ToolbarItem(placement: .topBarLeading) { Button(role: .close) { dismiss() } } }
        }
        .presentationDetents([.large])
    }
}

/// A place: map, address, and every plan there.
struct PlaceDetail: View {
    let tripID: UUID
    let placeID: UUID
    @Environment(TripStore.self) private var store
    @Environment(AppNavigation.self) private var nav
    @State private var showTaxi = false
    @State private var editing = false

    var body: some View {
        let trip = store.trip(tripID)
        if let place = trip.place(placeID) {
            JFList {
                Section {
                    if place.hasPosition, UserDefaults.standard.string(forKey: MapConsent.key) == "on" { MapSnapshot(coordinate: place.coordinate, meters: 2000).frame(height: 220).listRowInsets(EdgeInsets()) }
                    else { Text("No position yet. Navigating looks for the address.").font(.jf(.footnote)).foregroundStyle(Color.jfMuted) }
                    VStack(alignment: .leading, spacing: 4) {
                        Text(place.address).font(.jf(.callout))
                        if let l = place.localAddress { Text(l).font(.jf(.callout)).foregroundStyle(Color.jfMuted) }
                        Text("\(Fmt.city(TimeZone(identifier: place.zone)!)) time").font(.jf(.footnote)).foregroundStyle(Color.jfSubtleText)
                    }
                    HStack {
                        Button { Maps.navigate(to: place) } label: { HStack(spacing: 8) { Image(systemName: "location.fill"); Text("Navigate") }.frame(maxWidth: .infinity) }.buttonStyle(.borderedProminent).foregroundStyle(Color.jfOnAccent)
                        if place.localName != nil {
                            Button { showTaxi = true } label: { HStack(spacing: 8) { Image(systemName: "car.fill"); Text("Taxi card") }.frame(maxWidth: .infinity) }.buttonStyle(.bordered)
                        }
                    }
                    .controlSize(.large)
                    .listRowBackground(Color.clear)
                    .listRowInsets(EdgeInsets())
                }
                let here = trip.items.filter { $0.place == place.id || $0.stay?.place == place.id || $0.transport?.to == place.id }
                if !here.isEmpty {
                    Section {
                        ForEach(here) { item in
                            Button { nav.detail = item.id } label: { PlanRow(trip: trip, item: item, showDay: true) }.buttonStyle(.plain)
                        }
                    } header: { Overline("Plans here") }
                }
            }
            .listStyle(.insetGrouped)
            .jfBackground()
            .navigationTitle(place.name)
            .navigationBarTitleDisplayMode(.inline)
            .toolbar { ToolbarItem(placement: .topBarTrailing) { Button { editing = true } label: { Image(systemName: "pencil") }.accessibilityLabel(Text("Edit")) } }
            .sheet(isPresented: $showTaxi) { TaxiCardView(place: place) }
            .sheet(isPresented: $editing) { PlaceEditor(tripID: tripID, placeID: place.id) }
        }
    }
}

/// The system's event editor, filled with the plan; the person picks the calendar and saves. Needs no access to the
/// calendar, because iOS saves it.
struct EventEditor: UIViewControllerRepresentable {
    let title: String, start: Date, end: Date, zone: TimeZone, location: String?
    func makeCoordinator() -> Coordinator { Coordinator() }
    func makeUIViewController(context: Context) -> EKEventEditViewController {
        let store = EKEventStore()
        let event = EKEvent(eventStore: store)
        event.title = title; event.startDate = start; event.endDate = max(end, start); event.timeZone = zone; event.location = location
        let c = EKEventEditViewController(); c.eventStore = store; c.event = event; c.editViewDelegate = context.coordinator
        return c
    }
    func updateUIViewController(_ controller: EKEventEditViewController, context: Context) {}
    final class Coordinator: NSObject, EKEventEditViewDelegate {
        func eventEditViewController(_ controller: EKEventEditViewController, didCompleteWith action: EKEventEditViewAction) { controller.dismiss(animated: true) }
    }
}
