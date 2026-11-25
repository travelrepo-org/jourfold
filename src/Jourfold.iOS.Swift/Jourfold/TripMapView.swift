import SwiftUI
import MapKit

/// Every place of the trip on a map, filtered by day, with the day's route as a line.
struct TripMapView: View {
    let tripID: UUID
    /// Pushed from More, which has a navigation of its own.
    var embedded = false
    @Environment(TripStore.self) private var store
    @Environment(AppNavigation.self) private var nav
    @State private var day: Int? = nil
    @State private var selection: UUID?
    @State private var position: MapCameraPosition = .automatic
    @State private var standard = true
    @State private var listing = false
    @AppStorage(MapConsent.key) private var online = ""

    var body: some View {
        let trip = store.trip(tripID)
        if embedded { page(trip) } else { NavigationStack { page(trip).tripToolbar(trip) } }
    }

    private func page(_ trip: Trip) -> some View {
        let shown = places(trip)
        return Group {
            Group { if online == "on" {
            Map(position: $position, selection: $selection) {
                ForEach(Array(shown.enumerated()), id: \.element.0.id) { i, e in
                    // A day's stops are numbered in the order of its plan.
                    if day != nil { Marker(e.0.name, monogram: Text("\(i + 1)"), coordinate: e.0.coordinate).tint(e.1.colors.bar).tag(e.0.id) }
                    else { Marker(e.0.name, systemImage: e.2, coordinate: e.0.coordinate).tint(e.1.colors.bar).tag(e.0.id) }
                }
                if day != nil {
                    let route = shown.map(\.0.coordinate)
                    if route.count > 1 { MapPolyline(coordinates: route).stroke(Color.jfAccent.opacity(0.4), style: StrokeStyle(lineWidth: 2, lineCap: .round, dash: [3, 6])) }
                }
                // Journeys bend along the globe.
                ForEach(Array(journeys(trip).enumerated()), id: \.offset) { _, j in
                    MapPolyline(coordinates: [j.from, j.to], contourStyle: .geodesic).stroke(Color.jfAccent, style: StrokeStyle(lineWidth: 3, lineCap: .round, dash: [6, 6]))
                }
                UserAnnotation()
            }
            .mapStyle(standard ? .standard(pointsOfInterest: .excludingAll) : .hybrid)
            .mapControls { MapCompass(); MapScaleView(); MapUserLocationButton() }
            } else {
                // As on the map, the whole trip leaves out places in another zone, such as the home airport; the list has all.
                let numbered = shown.enumerated().map { (number: $0.offset + 1, place: $0.element.0, kind: $0.element.1) }
                let local = numbered.filter { $0.place.zone == trip.zone }
                let stops = day == nil && !local.isEmpty ? local : numbered
                let all = journeys(trip)
                let ways = stops.count == numbered.count ? all : all.filter { j in [j.from, j.to].allSatisfy { c in stops.contains { $0.place.latitude == c.latitude && $0.place.longitude == c.longitude } } }
                OfflineMap(stops: stops, journeys: ways, selection: $selection)
                    .background(Color.jfApp.ignoresSafeArea())
                    .overlay { if shown.isEmpty { ContentUnavailableView("No places with a position", systemImage: "mappin.slash") } }
            } }
            .safeAreaInset(edge: .top, spacing: 0) {
                VStack(spacing: 4) {
                ScrollView(.horizontal, showsIndicators: false) {
                    HStack(spacing: 8) {
                        chip(Text("Whole trip"), selected: day == nil) { day = nil }
                        ForEach(Array(trip.days.enumerated()), id: \.offset) { i, d in
                            chip(Text(Fmt.shortDay(d, trip.calendar.timeZone)), selected: day == i) { day = i }
                        }
                    }
                    .padding(.horizontal, 16).padding(.vertical, 8)
                }
                MapOffer()
                }
                .background(online == "on" ? Color.clear : Color.jfApp)
            }
            .onChange(of: day, initial: true) { withAnimation { position = .region(region(for: shown.map(\.0), trip: trip)) } }
            .navigationTitle("Map")
            .navigationBarTitleDisplayMode(.inline)
            .sheet(isPresented: $listing) {
                NavigationStack {
                    List {
                        ForEach(Array(shown.enumerated()), id: \.element.0.id) { i, e in Button { listing = false; selection = e.0.id } label: { Label("\(i + 1) · \(e.0.name)", systemImage: e.2) } }
                        let missing = trip.places.filter { !$0.hasPosition }
                        if !missing.isEmpty { Section { ForEach(missing) { p in Button { listing = false; selection = p.id } label: { Text(p.name) } } } header: { Text("Without a position") } }
                    }
                    .navigationTitle("Stops").navigationBarTitleDisplayMode(.inline)
                }
                .presentationDetents([.medium, .large])
            }
            .toolbar {
                ToolbarItem(placement: .topBarTrailing) { Button { listing = true } label: { Image(systemName: "list.number") }.accessibilityLabel(Text("Stops")) }
                if online == "on" { ToolbarItem(placement: .topBarTrailing) {
                    Button { standard.toggle() } label: { Image(systemName: standard ? "globe.europe.africa" : "map") }
                        .accessibilityLabel(standard ? Text("Satellite") : Text("Street map"))
                } }
            }
            .sheet(item: Binding(get: { selection.map(IdentifiedID.init) }, set: { selection = $0?.id })) { id in
                NavigationStack { PlaceDetail(tripID: tripID, placeID: id.id) }
                    .presentationDetents([.fraction(0.4), .large])
                    .presentationBackgroundInteraction(.enabled(upThrough: .fraction(0.4)))
            }
        }
    }

    private func chip(_ text: Text, selected: Bool, action: @escaping () -> Void) -> some View {
        Button(action: action) {
            text.font(.jf(.footnote, .semibold))
                .padding(.horizontal, 12).padding(.vertical, 7)
                .foregroundStyle(selected ? Color.jfOnAccent : Color.jfText)
                .background(selected ? Color.jfAccent : Color.jfSurface, in: Capsule())
                .overlay(Capsule().strokeBorder(selected ? Color.clear : Color.jfLine))
        }
        .buttonStyle(.plain)
        .accessibilityAddTraits(selected ? .isSelected : [])
    }

    /// The region around the places, leaving out ones in another zone (the home airport) unless they are all there is.
    private func region(for places: [Place], trip: Trip) -> MKCoordinateRegion {
        let local = places.filter { $0.zone == trip.zone }
        let pts = (local.isEmpty ? places : local).map(\.coordinate)
        guard let first = pts.first else { return MKCoordinateRegion(center: .init(latitude: 35.68, longitude: 139.76), span: .init(latitudeDelta: 4, longitudeDelta: 4)) }
        var minLat = first.latitude, maxLat = first.latitude, minLon = first.longitude, maxLon = first.longitude
        for p in pts { minLat = min(minLat, p.latitude); maxLat = max(maxLat, p.latitude); minLon = min(minLon, p.longitude); maxLon = max(maxLon, p.longitude) }
        return MKCoordinateRegion(center: .init(latitude: (minLat + maxLat) / 2, longitude: (minLon + maxLon) / 2),
                                  span: .init(latitudeDelta: max((maxLat - minLat) * 1.5, 0.02), longitudeDelta: max((maxLon - minLon) * 1.5, 0.02)))
    }

    private func journeys(_ trip: Trip) -> [(from: CLLocationCoordinate2D, to: CLLocationCoordinate2D)] {
        (day.map { trip.items(on: trip.days[$0]) } ?? trip.items).filter { $0.status != .cancelled }.compactMap { i in
            guard let a = trip.place(i.transport?.from), let b = trip.place(i.transport?.to), a.hasPosition, b.hasPosition else { return nil }
            return (a.coordinate, b.coordinate)
        }
    }

    private func places(_ trip: Trip) -> [(Place, Kind, String)] {
        let items = day.map { trip.items(on: trip.days[$0]) } ?? trip.items
        var seen = Set<UUID>(); var out: [(Place, Kind, String)] = []
        for i in items {
            for id in [i.transport?.from, i.transport?.to, i.stay?.place ?? i.place].compactMap({ $0 }) where !seen.contains(id) {
                if let p = trip.place(id), p.hasPosition { seen.insert(id); out.append((p, i.kind, i.transport?.mode.symbol ?? i.kind.symbol)) }
            }
        }
        return out
    }
}
