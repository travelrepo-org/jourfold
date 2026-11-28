import SwiftUI

/// The share sheet's form: what came in, which trip, and whether it becomes a note or goes to a plan.
/// Used by the share extension, and by the app for its screenshots.
struct ShareForm: View {
    let text: String
    let url: String?
    /// The names of files that came along.
    var files: [String] = []
    /// Why something that was shared could not be taken.
    var problem: String? = nil
    let trips: [SharedInbox.TripSummary]
    /// Shown by the app itself, which adds it at once.
    var inApp = false
    let done: (SharedInbox.Entry?) -> Void
    @State private var trip = 0
    @State private var target = Destination.notes
    @State private var query = ""
    private enum Destination: Hashable { case notes, place, plan(Int), booking(UUID) }

    var body: some View {
        NavigationStack {
            Form {
                Section {
                    VStack(alignment: .leading, spacing: 4) {
                        Text(text).font(.headline).lineLimit(3)
                        if let url { Text(url).font(.footnote).foregroundStyle(.secondary).lineLimit(1) }
                        ForEach(files, id: \.self) { Label($0, systemImage: "doc").font(.footnote) }
                        if let problem { Label(problem, systemImage: "exclamationmark.triangle").font(.footnote) }
                    }
                    .padding(.vertical, 4)
                } header: { Text("Shared") }
                if trips.isEmpty {
                    Section { Text("Open Jourfold once, then trips appear here.").foregroundStyle(.secondary) }
                } else {
                    Section {
                        Picker("Trip", selection: $trip) {
                            ForEach(Array(trips.enumerated()), id: \.offset) { i, t in Text(t.title).tag(i) }
                        }
                        let chosen = trips[min(trip, trips.count - 1)]
                        if chosen.plans.count > 8 { TextField("Search the plans and bookings", text: $query) }
                        Picker("Add to", selection: $target) {
                            Text("Notes of the trip").tag(Destination.notes)
                            if MapLink(url) != nil { Label("New place", systemImage: "mappin.and.ellipse").tag(Destination.place) }
                            Section("Plans") { ForEach(Array(chosen.plans.enumerated()).filter { query.isEmpty || $0.element.localizedStandardContains(query) }, id: \.offset) { i, p in Text(p).tag(Destination.plan(i)) } }
                            if let bookings = chosen.bookings, !bookings.isEmpty { Section("Bookings") { ForEach(bookings.filter { query.isEmpty || $0.title.localizedStandardContains(query) }, id: \.id) { Text($0.title).tag(Destination.booking($0.id)) } } }
                        }
                        .pickerStyle(.navigationLink)
                    } footer: { if !inApp { Text("Jourfold adds it the next time you open the app. Nothing leaves this phone.") } }
                }
            }
            .navigationTitle("Add to Jourfold")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) { Button(role: .cancel) { done(nil) } }
                ToolbarItem(placement: .confirmationAction) {
                    Button(role: .confirm) {
                        let t = trips[trip]
                        var entry = SharedInbox.Entry(text: text, url: url, trip: t.title, tripID: t.id, plan: nil, files: files)
                        switch target {
                        case .notes: break
                        case .place: entry.newPlace = true
                        case .plan(let i): entry.plan = t.plans[i]; entry.planID = t.planTargets.flatMap { $0.indices.contains(i) ? $0[i].id : nil }
                        case .booking(let id): entry.booking = id
                        }
                        done(entry)
                    }
                    .disabled(trips.isEmpty || (problem != nil && files.isEmpty && url == nil))
                }
            }
        }
        .onAppear { if MapLink(url) != nil { target = .place } }
        .onChange(of: trip) { target = MapLink(url) != nil ? .place : .notes }
        .tint(Color(red: 0x0F / 255, green: 0x7B / 255, blue: 0x7A / 255))
    }
}
