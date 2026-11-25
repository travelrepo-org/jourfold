import SwiftUI

/// Everything that is not a daily place, as in the live app's More page: each row says the most useful thing about it.
struct MoreView: View {
    let tripID: UUID
    @Environment(TripStore.self) private var store
    @Environment(AppNavigation.self) private var nav

    var body: some View {
        @Bindable var nav = nav
        let trip = store.trip(tripID)
        NavigationStack(path: $nav.morePath) {
            JFList {
                Section {
                    HStack(spacing: 14) {
                        TripBadge(trip: trip, size: 52)
                        VStack(alignment: .leading, spacing: 2) {
                            Text(trip.title).font(.jf(.title3, .bold))
                            Text("\(Fmt.range(trip)) · \(trip.peopleText)").font(.jf(.footnote)).foregroundStyle(Color.jfMuted)
                            HStack(spacing: 4) { Image(systemName: "square.stack.3d.up"); Text(trip.variant) }.font(.jf(.caption, .medium)).foregroundStyle(Color.jfSubtleText)
                        }
                    }
                    .padding(.vertical, 4)
                }
                Section {
                    row(.bookings, "ticket", "Bookings", Text("\(trip.bookings.count) bookings"))
                    row(.tasks, "checklist", "Tasks", Text("\(trip.openTasks) open"))
                    row(.packing, "backpack", "Packing lists", trip.toPack > 0 ? Text("\(trip.toPack) still to pack") : Text("All packed"))
                    row(.map, "map", "Map", nil)
                    row(.places, "mappin.and.ellipse", "Places", Text("\(trip.places.count) places"))
                    row(.files, "paperclip", "Files", Text("\(trip.items.flatMap(\.attachments).count) files"))
                    row(.costs, "wallet.bifold", "Costs", Text(CostsView.paidSummary(trip)))
                    row(.converter, "coloncurrencysign.arrow.trianglehead.counterclockwise.rotate.90", "Currency converter", Text(Rates.date.map { String(localized: "Rates from \($0.formatted(.dateTime.day().month(.abbreviated)))") } ?? String(localized: "No rates yet")))
                } header: { Overline("Trip") }
                Section {
                    row(.trip, "pencil", "Trip details", Text("\(Fmt.range(trip)) · \(trip.currency)"))
                    row(.travellers, "person.2", "Travellers", Text("\(trip.people.count) people"))
                    let changes = store.changes[trip.id]?.count ?? 0
                    row(.sync, "arrow.triangle.2.circlepath", "Sync and versions", changes > 0 ? Text("\(changes) changes not in a version yet") : Text("Saved on this phone"))
                } header: { Overline("Together") }
                Section {
                    row(.settings, "gearshape", "Settings", nil)
                } header: { Overline("App") }
            }
            .listStyle(.insetGrouped)
            .jfBackground()
            .navigationTitle("More")
            .tripToolbar(trip)
            .navigationDestination(for: MorePage.self) { page in MorePageView(tripID: tripID, page: page) }
        }
    }

    private func row(_ page: MorePage, _ symbol: String, _ title: LocalizedStringKey, _ detail: Text?) -> some View {
        NavigationLink(value: page) {
            Label {
                VStack(alignment: .leading, spacing: 1) {
                    Text(title).font(.jf(.body, .semibold))
                    if let detail { detail.font(.jf(.footnote)).foregroundStyle(Color.jfMuted) }
                }
            } icon: {
                Image(systemName: symbol).foregroundStyle(Color.jfAccent)
            }
        }
    }
}

/// One page behind More, also used as a sidebar destination on iPad.
struct MorePageView: View {
    let tripID: UUID
    let page: MorePage
    @Environment(TripStore.self) private var store
    var body: some View {
        switch page {
        case .bookings: BookingsView(tripID: tripID)
        case .tasks: TasksView(tripID: tripID)
        case .packing: PackingView(tripID: tripID)
        case .places: PlacesView(tripID: tripID)
        case .files: FilesView(tripID: tripID)
        case .costs: CostsView(tripID: tripID)
        case .converter: ConverterView(trip: store.trip(tripID))
        case .trip: TripDetailsView(tripID: tripID)
        case .travellers: TravellersView(tripID: tripID)
        case .sync: SyncView(tripID: tripID)
        case .settings: SettingsView()
        case .map: TripMapView(tripID: tripID, embedded: true)
        case .merge: MergeView(tripID: tripID)
        case .about: AboutView()
        case .notifications: NotificationsView(tripID: tripID)
        }
    }
}
