import SwiftUI
import UniformTypeIdentifiers

/// The trips on this phone (TravelRepo trip folders), split into upcoming and past, with search and a context menu
/// per trip. With no trips yet it offers a new trip, the sample trip and opening one from GitHub.
struct TripListView: View {
    @Environment(TripStore.self) private var store
    @Environment(AppNavigation.self) private var nav
    @Environment(Account.self) private var account
    @State private var query = ""
    @State private var showNew = false
    @State private var showSettings = false
    @State private var showGitHub = false
    @State private var importFolder = false
    @State private var confirmRemove: Trip?
    @State private var busy = false

    private var filtered: [Trip] {
        query.isEmpty ? store.trips : store.trips.filter { $0.title.localizedCaseInsensitiveContains(query) || $0.places.contains { $0.name.localizedCaseInsensitiveContains(query) } }
    }

    private func isPast(_ t: Trip) -> Bool {
        guard let e = t.calendar.date(from: t.end) else { return false }
        return e.addingTimeInterval(86400) < store.now
    }

    var body: some View {
        NavigationStack {
            JFList {
                UpdateNotice()
                if store.loading {
                    Section { HStack(spacing: 12) { ProgressView(); Text("Opening your trips…").foregroundStyle(Color.jfMuted) } }
                } else if store.trips.isEmpty {
                    Section {
                        VStack(alignment: .leading, spacing: 8) {
                            Image(systemName: "suitcase.rolling").font(.largeTitle).foregroundStyle(Color.jfAccent).accessibilityHidden(true)
                            Text("No trips yet").font(.jf(.title3, .bold))
                            Text("Start a new trip, open one that someone shared with you on GitHub, or look around in the sample trip.").font(.jf(.subheadline)).foregroundStyle(Color.jfMuted)
                        }
                        .padding(.vertical, 8)
                        Button { addSample() } label: { Label("Add the sample trip", systemImage: "sparkles") }.disabled(busy)
                    }
                }
                let current = filtered.filter { !isPast($0) }
                let past = filtered.filter(isPast)
                if !current.isEmpty {
                    Section { ForEach(current) { row($0) } } header: { Overline("Upcoming and current") }
                }
                if !past.isEmpty {
                    Section { ForEach(past) { row($0) } } header: { Overline("Past") }
                }
                if !store.unreadable.isEmpty {
                    Section {
                        ForEach(store.unreadable, id: \.self) { name in
                            Label { Text(name) } icon: { Image(systemName: "exclamationmark.triangle").foregroundStyle(Color.jfWarning) }
                        }
                    } header: { Overline("Could not be opened") } footer: { Footnote("These folders stay as they are. A report under Settings can help to find out why.") }
                }
                Section {
                    Button { showNew = true } label: { Label("New trip", systemImage: "plus") }
                    Button { showGitHub = true } label: { Label("Open from GitHub", systemImage: "arrow.down.circle") }
                    Button { importFolder = true } label: { Label("Open a trip folder", systemImage: "folder") }
                    if !store.trips.isEmpty, !store.trips.contains(where: { $0.title == "Spring in Japan" }) {
                        Button { addSample() } label: { Label("Add the sample trip", systemImage: "sparkles") }.disabled(busy)
                    }
                } header: { Overline("Start") }
            }
            .listStyle(.insetGrouped)
            .jfBackground()
            .task { await Updates.shared.look() }
            .navigationTitle("Trips")
            .searchable(text: $query, prompt: Text("Trips and places"))
            .refreshable { for trip in store.trips where TravelRepoBridge.all[trip.id]?.isShared == true { await store.sync(trip.id) } }
            .toolbar {
                ToolbarItem(placement: .topBarTrailing) {
                    Button { showSettings = true } label: { Image(systemName: "gearshape") }
                        .accessibilityLabel(Text("Settings"))
                }
                ToolbarItem(placement: .topBarTrailing) {
                    Button { showNew = true } label: { Image(systemName: "plus") }
                        .accessibilityLabel(Text("New trip"))
                }
            }
            .sheet(isPresented: $showNew) { NewTripSheet() }
            .sheet(isPresented: $showGitHub) { OpenFromGitHubSheet() }
            .sheet(isPresented: $showSettings) { NavigationStack { SettingsView() } }
            .fileImporter(isPresented: $importFolder, allowedContentTypes: [.folder]) { result in
                if case .success(let url) = result { Task { await openFolder(url) } }
            }
            .confirmationDialog(Text("Remove from this phone?"), isPresented: Binding(get: { confirmRemove != nil }, set: { if !$0 { confirmRemove = nil } }), titleVisibility: .visible, presenting: confirmRemove) { trip in
                Button("Remove from this phone", role: .destructive) {
                    do { try TripLibrary.remove(trip.id, store: store) } catch { store.problem = TravelRepoBridge.message(error) }
                }
            } message: { trip in
                Text(TravelRepoBridge.all[trip.id]?.isShared == true
                     ? "The trip stays on GitHub and with everyone you share it with. On this phone it moves to the app's recovery folder."
                     : "The trip is only on this phone. It moves to the app's recovery folder, where it can be brought back.")
            }
        }
    }

    private func addSample() {
        busy = true
        Task {
            do { if let id = try await TripLibrary.addSamples(store) { nav.tab = .today; nav.openTrip = id } } catch { store.problem = TravelRepoBridge.message(error) }
            busy = false
        }
    }

    /// Copies a trip folder chosen in Files into the app, with its history, and opens it.
    private func openFolder(_ url: URL) async {
        let scoped = url.startAccessingSecurityScopedResource()
        defer { if scoped { url.stopAccessingSecurityScopedResource() } }
        guard FileManager.default.fileExists(atPath: url.appendingPathComponent("travel.yaml").path) else {
            store.problem = String(localized: "This folder holds no trip: travel.yaml is missing."); return
        }
        do {
            try await TripLibrary.prepare()
            let target = TripLibrary.newFolder(for: url.lastPathComponent)
            try FileManager.default.copyItem(at: url, to: target)
            let id = try await TravelRepoBridge.open(target, store: store)
            store.sortTrips()
            nav.tab = .today; nav.openTrip = id
        } catch { store.problem = TravelRepoBridge.message(error) }
    }

    /// A copy of the trip with its history, as a trip of its own on this phone.
    private func duplicate(_ trip: Trip) async {
        guard let bridge = TravelRepoBridge.all[trip.id] else { return }
        do {
            let target = TripLibrary.newFolder(for: trip.title + " copy")
            _ = try await bridge.session.copyTrip(to: target)
            try await TravelRepoBridge.open(target, store: store)
            store.sortTrips()
        } catch { store.problem = TravelRepoBridge.message(error) }
    }

    private func row(_ trip: Trip) -> some View {
        Button { nav.tab = .today; nav.openTrip = trip.id } label: {
            HStack(spacing: 14) {
                TripBadge(trip: trip, size: 48)
                VStack(alignment: .leading, spacing: 3) {
                    Text(trip.title).font(.jf(.headline)).foregroundStyle(Color.jfText)
                    Text(Fmt.range(trip)).font(.jf(.subheadline)).foregroundStyle(Color.jfMuted)
                    Text(trip.peopleText).font(.jf(.footnote)).foregroundStyle(Color.jfSubtleText)
                }
                Spacer(minLength: 0)
                if let changes = store.changes[trip.id], !changes.isEmpty {
                    Image(systemName: "circle.fill").font(.system(size: 8)).foregroundStyle(Color.jfAccent)
                        .accessibilityLabel(Text("Unsaved changes"))
                }
            }
            .padding(.vertical, 4)
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .contextMenu {
            Button { nav.tab = .today; nav.openTrip = trip.id } label: { Label("Open", systemImage: "arrow.right.circle") }
            Button { Task { await duplicate(trip) } } label: { Label("Duplicate", systemImage: "plus.square.on.square") }
            ShareLink(item: "\(trip.title) · \(Fmt.range(trip))") { Label("Share", systemImage: "square.and.arrow.up") }
            Divider()
            Button(role: .destructive) { confirmRemove = trip } label: { Label("Remove from this phone", systemImage: "minus.circle") }
        } preview: {
            TripPreview(trip: trip)
        }
        .swipeActions {
            Button(role: .destructive) { confirmRemove = trip } label: { Label("Remove", systemImage: "minus.circle") }
        }
    }
}

/// The card shown above the context menu: dates, places and how many plans.
struct TripPreview: View {
    let trip: Trip
    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            HStack(spacing: 12) {
                TripBadge(trip: trip, size: 52)
                VStack(alignment: .leading) {
                    Text(trip.title).font(.jf(.title3, .bold))
                    Text(Fmt.range(trip)).font(.jf(.subheadline)).foregroundStyle(Color.jfMuted)
                }
            }
            Text("\(trip.items.count) plans · \(trip.places.count) places · \(trip.bookings.count) bookings")
                .font(.jf(.footnote)).foregroundStyle(Color.jfMuted)
            Text(trip.places.prefix(5).map(\.name).joined(separator: " · ")).font(.jf(.footnote)).foregroundStyle(Color.jfSubtleText).lineLimit(2)
        }
        .padding(18)
        .frame(width: 320, alignment: .leading)
        .background(Color.jfSurface)
    }
}

struct NewTripSheet: View {
    @Environment(\.dismiss) private var dismiss
    @Environment(TripStore.self) private var store
    @Environment(AppNavigation.self) private var nav
    @Environment(Account.self) private var account
    @State private var title = ""
    @State private var start = Calendar.current.date(byAdding: .month, value: 1, to: Date()) ?? Date()
    @State private var end = Calendar.current.date(byAdding: .day, value: 37, to: Date()) ?? Date()
    @State private var zone = TimeZone.current.identifier
    @State private var currency = Locale.current.currency?.identifier ?? "EUR"
    @State private var traveller = UserDefaults.standard.string(forKey: "authorName") ?? ""
    @State private var busy = false

    var body: some View {
        NavigationStack {
            JFForm {
                Section { TextField("Name of the trip", text: $title) }
                Section {
                    DatePicker("Starts", selection: $start, displayedComponents: .date)
                    DatePicker("Ends", selection: $end, in: start..., displayedComponents: .date)
                    Picker("Main time zone", selection: $zone) {
                        ForEach(TimeZonePicker.common(including: zone), id: \.self) { Text(TimeZonePicker.name($0)).tag($0) }
                    }
                }
                Section {
                    Picker("Currency", selection: $currency) { ForEach(Array(Set(["EUR", "JPY", "NZD", "CAD", "USD", currency])).sorted(), id: \.self) { Text($0) } }
                } footer: { Footnote("Costs add up in this currency. Each expense keeps its own.") }
                Section {
                    TextField("Your name", text: $traveller)
                } footer: { Footnote("Saved as a folder on this phone, with its own history. You can share it on GitHub later.") }
            }
            .jfBackground()
            .navigationTitle("New trip")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) { Button(role: .cancel) { dismiss() } }
                ToolbarItem(placement: .confirmationAction) {
                    Button(role: .confirm) { create() }
                        .disabled(title.trimmingCharacters(in: .whitespaces).isEmpty || busy)
                }
            }
        }
        .presentationDetents([.medium, .large])
    }

    private func create() {
        busy = true
        var c = Calendar(identifier: .gregorian); c.timeZone = TimeZone(identifier: zone) ?? .current
        let days = (Calendar.current.dateComponents([.year, .month, .day], from: start), Calendar.current.dateComponents([.year, .month, .day], from: end))
        if !traveller.isEmpty { UserDefaults.standard.set(traveller, forKey: "authorName") }
        Task {
            do {
                let id = try await TripLibrary.create(title: title.trimmingCharacters(in: .whitespaces), start: days.0, end: days.1, zone: zone, currency: currency, traveller: traveller, store: store, account: account)
                dismiss()
                nav.tab = .today; nav.openTrip = id
            } catch { store.problem = TravelRepoBridge.message(error) }
            busy = false
        }
    }
}

/// Time zones to choose from: the places of the example cities and the phone's own, by city name.
enum TimeZonePicker {
    static let cities = ["Europe/Berlin", "Europe/Paris", "Asia/Tokyo", "Pacific/Auckland", "America/Toronto", "America/Vancouver", "America/St_Johns", "UTC"]
    static func common(including zone: String) -> [String] {
        var all = cities
        for z in [TimeZone.current.identifier, zone] where !all.contains(z) { all.insert(z, at: 0) }
        return all
    }
    static func name(_ id: String) -> String {
        guard let zone = TimeZone(identifier: id) else { return id }
        let city = id.split(separator: "/").last.map { $0.replacingOccurrences(of: "_", with: " ") } ?? id
        return "\(city) (\(zone.localizedName(for: .shortGeneric, locale: .current) ?? id))"
    }
}
