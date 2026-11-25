import CoreLocation
import MapKit
import SwiftUI

/// Adds or changes a place: its name, address, the name and address in the local script (for a taxi driver), its
/// position and time zone. Finding an address asks Apple Maps only after the person turned on the address search,
/// as the .NET app does with online maps; the phone's own position is read only when asked for.
struct PlaceEditor: View {
    let tripID: UUID
    /// The place to change, or nil for a new one.
    let placeID: UUID?
    /// A plan that gets the new place, if it was opened from one.
    var forItem: UUID? = nil
    @Environment(\.dismiss) private var dismiss
    @Environment(TripStore.self) private var store
    @State private var name = ""
    @State private var address = ""
    @State private var localName = ""
    @State private var localAddress = ""
    @State private var latitude: Double?
    @State private var longitude: Double?
    @State private var zone = TimeZone.current.identifier
    @State private var query = ""
    @State private var results: [MKMapItem] = []
    @State private var searchOn = UserDefaults.standard.string(forKey: "maps.online") == "on"
    @State private var locating = false
    @State private var coordinates = ""
    @State private var looking = false

    /// A pair such as "35.0036, 135.7788", alone or in a map link.
    static func position(in text: String) -> (Double, Double)? {
        guard let m = text.firstMatch(of: /(-?\d{1,2}\.\d+)\s*(?:,|%2C|;)\s*(-?\d{1,3}\.\d+)/), let a = Double(m.1), let b = Double(m.2), abs(a) <= 90, abs(b) <= 180 else { return nil }
        return (a, b)
    }
    @State private var problem: String?

    var body: some View {
        NavigationStack {
            JFForm {
                Section {
                    TextField("Name", text: $name)
                    TextField("Address", text: $address, axis: .vertical)
                } header: { Overline("Place") }
                Section {
                    if searchOn {
                        TextField("Search for an address", text: $query).submitLabel(.search).onSubmit { Task { await search() } }
                        ForEach(results, id: \.self) { item in
                            Button { take(item) } label: {
                                VStack(alignment: .leading, spacing: 2) {
                                    Text(item.name ?? "").font(.jf(.body)).foregroundStyle(Color.jfText)
                                    Text(item.address?.fullAddress ?? "").font(.jf(.footnote)).foregroundStyle(Color.jfMuted)
                                }
                            }
                        }
                    } else {
                        Text("Finding an address asks Apple Maps with what you type. Nothing else about the trip is sent.").font(.jf(.footnote)).foregroundStyle(Color.jfMuted)
                        Button { UserDefaults.standard.set("on", forKey: "maps.online"); searchOn = true } label: { Label("Turn on address search", systemImage: "magnifyingglass") }
                    }
                    Button { Task { await locate() } } label: { Label(locating ? "Finding your position…" : "Use where I am now", systemImage: "location") }.disabled(locating)
                } header: { Overline("Find") }
                Section {
                    if let latitude, let longitude {
                        MapSnapshot(coordinate: .init(latitude: latitude, longitude: longitude), meters: 1500).frame(height: 160).listRowInsets(EdgeInsets())
                        LabeledContent("Position", value: String(format: "%.5f, %.5f", latitude, longitude))
                    } else {
                        Text("No position yet. Search for the address or use where you are.").foregroundStyle(Color.jfMuted)
                    }
                    TextField("Latitude, longitude, or a map link", text: $coordinates).autocorrectionDisabled().textInputAutocapitalization(.never)
                        .onChange(of: coordinates) { _, text in if let p = Self.position(in: text) { latitude = p.0; longitude = p.1 } }
                    if latitude == nil, let link = ShortLink(coordinates) {
                        Button { Task { await look(link) } } label: { Label("Position from the link", systemImage: "link") }.disabled(looking)
                        Text("Asks \(link.url.host() ?? "") where the short link leads. Nothing else is sent.").font(.jf(.footnote)).foregroundStyle(Color.jfMuted)
                    }
                    if latitude != nil { Button("Remove the position", role: .destructive) { latitude = nil; longitude = nil; coordinates = "" } }
                    Picker("Time zone", selection: $zone) { ForEach(TimeZonePicker.common(including: zone), id: \.self) { Text(TimeZonePicker.name($0)).tag($0) } }
                } header: { Overline("Position") }
                Section {
                    TextField("Name in the local script", text: $localName)
                    TextField("Address in the local script", text: $localAddress, axis: .vertical)
                } header: { Overline("For a taxi driver") } footer: { Footnote("Shown large on the taxi card, for example in Japanese.") }
                if let problem { Section { Text(problem).foregroundStyle(Color.jfDanger) } }
            }
            .jfBackground()
            .navigationTitle(placeID == nil ? "New place" : "Edit place")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) { Button(role: .cancel) { dismiss() } }
                ToolbarItem(placement: .confirmationAction) { Button(role: .confirm) { save() }.disabled(name.trimmingCharacters(in: .whitespaces).isEmpty) }
            }
            .onAppear(perform: load)
        }
    }

    private func load() {
        guard let trip = store.trips.first(where: { $0.id == tripID }) else { return }
        zone = trip.zone
        guard let id = placeID, let p = trip.place(id) else { return }
        name = p.name; address = p.address; localName = p.localName ?? ""; localAddress = p.localAddress ?? ""; zone = p.zone
        if let link = UserDefaults.standard.string(forKey: ShortLink.key(id)) { coordinates = link }
        if p.latitude != 0 || p.longitude != 0 { latitude = p.latitude; longitude = p.longitude }
    }

    private func look(_ link: ShortLink) async {
        looking = true; defer { looking = false }
        if let found = await link.position() { latitude = found.latitude; longitude = found.longitude; problem = nil }
        else { problem = String(localized: "The link's position could not be found.") }
    }

    private func search() async {
        guard !query.isEmpty else { return }
        let request = MKLocalSearch.Request(); request.naturalLanguageQuery = query
        if let latitude, let longitude { request.region = MKCoordinateRegion(center: .init(latitude: latitude, longitude: longitude), latitudinalMeters: 50_000, longitudinalMeters: 50_000) }
        do { results = Array(try await MKLocalSearch(request: request).start().mapItems.prefix(8)) } catch { problem = String(localized: "Nothing found, or no connection.") }
    }

    private func take(_ item: MKMapItem) {
        if name.isEmpty { name = item.name ?? "" }
        address = item.address?.fullAddress ?? address
        latitude = item.location.coordinate.latitude; longitude = item.location.coordinate.longitude
        if let z = item.timeZone?.identifier { zone = z }
        results = []; query = ""
    }

    private func locate() async {
        locating = true; defer { locating = false }
        do {
            for try await update in CLLocationUpdate.liveUpdates() {
                if let location = update.location { latitude = location.coordinate.latitude; longitude = location.coordinate.longitude; break }
                if update.authorizationDenied { problem = String(localized: "Jourfold may not use your position. Allow it under Settings → Apps → Jourfold."); break }
            }
        } catch { problem = error.localizedDescription }
    }

    private func save() {
        let place = Place(id: placeID ?? UUID.v7(), name: name.trimmingCharacters(in: .whitespaces), latitude: latitude ?? 0, longitude: longitude ?? 0, zone: zone,
                          address: address, localName: localName.isEmpty ? nil : localName, localAddress: localAddress.isEmpty ? nil : localAddress)
        store.update(tripID, placeID == nil ? "Add \(place.name)" : "Change \(place.name)") { t in
            if let i = t.places.firstIndex(where: { $0.id == place.id }) { t.places[i] = place } else { t.places.append(place) }
            if let item = forItem, let i = t.items.firstIndex(where: { $0.id == item }) {
                if t.items[i].stay != nil { t.items[i].stay?.place = place.id } else { t.items[i].place = place.id }
            }
        }
        if latitude != nil { UserDefaults.standard.removeObject(forKey: ShortLink.key(place.id)) }
        dismiss()
    }
}

/// A link that only the maps service can turn into a position, such as maps.app.goo.gl; asked only when the person wants it.
struct ShortLink {
    let url: URL
    static func key(_ place: UUID) -> String { "shortlink-\(place.uuidString)" }

    init?(_ text: String?) {
        guard let url = text.flatMap({ URL(string: $0.trimmingCharacters(in: .whitespacesAndNewlines)) }), url.scheme == "https", let host = url.host() else { return nil }
        guard ["maps.app.goo.gl", "goo.gl", "g.co"].contains(host) || (host == "maps.apple.com" && MapLink(url.absoluteString)?.latitude == nil) else { return nil }
        self.url = url
    }

    func position() async -> (latitude: Double, longitude: Double)? {
        let session = URLSession(configuration: .ephemeral, delegate: Stay(), delegateQueue: nil)
        defer { session.finishTasksAndInvalidate() }
        var next = url
        for _ in 0..<6 {
            if let m = MapLink(next.absoluteString), let a = m.latitude, let b = m.longitude { return (a, b) }
            guard let (_, response) = try? await session.data(for: URLRequest(url: next, timeoutInterval: 15)), let http = response as? HTTPURLResponse, [301, 302, 303, 307, 308].contains(http.statusCode),
                  let location = http.value(forHTTPHeaderField: "Location"), let to = URL(string: location, relativeTo: next)?.absoluteURL, to.scheme == "https" else { return nil }
            next = to
        }
        return nil
    }

    /// Follows nothing by itself, so every address can be read first.
    private final class Stay: NSObject, URLSessionTaskDelegate {
        nonisolated func urlSession(_ session: URLSession, task: URLSessionTask, willPerformHTTPRedirection response: HTTPURLResponse, newRequest request: URLRequest, completionHandler: @escaping (URLRequest?) -> Void) { completionHandler(nil) }
    }
}

