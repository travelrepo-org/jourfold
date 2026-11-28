import SwiftUI

/// Changes from someone else that meet changes on this phone: for each, keep this phone's, take theirs.
struct MergeView: View {
    let tripID: UUID
    @Environment(TripStore.self) private var store
    @Environment(\.dismiss) private var dismiss
    @State private var choices: [UUID: MergeConflict.Choice] = [:]

    var body: some View {
        JFList {
            if let merge = store.pendingMerge {
                Section {
                    Text("\(merge.from) changed the same plans on their phone. Choose what to keep; nothing is lost until you apply.")
                        .font(.jf(.callout)).foregroundStyle(Color.jfMuted)
                        .listRowBackground(Color.clear)
                }
                ForEach(merge.parts) { part in
                    Section {
                        choice(part, .mine, Text("This phone"), part.mine)
                        choice(part, .theirs, Text("\(merge.from)'s change"), part.theirs)
                        choice(part, .both, Text("Keep both"), String(localized: "The other one is added as a copy"))
                    } header: {
                        VStack(alignment: .leading, spacing: 2) {
                            Text(part.title).font(.jf(.headline)).foregroundStyle(Color.jfText)
                            Text(part.field).font(.jf(.footnote)).foregroundStyle(Color.jfMuted)
                        }
                        .textCase(nil)
                    }
                }
                Section {
                    Button {
                        store.resolve(merge.parts.map { choices[$0.id] ?? .mine })
                        UINotificationFeedbackGenerator().notificationOccurred(.success)
                        dismiss()
                    } label: { Text("Apply").frame(maxWidth: .infinity) }
                    .buttonStyle(.borderedProminent).foregroundStyle(Color.jfOnAccent)
                    .controlSize(.large)
                    .disabled(merge.parts.contains { choices[$0.id] == nil })
                    .listRowBackground(Color.clear)
                    .listRowInsets(EdgeInsets())
                }
            } else {
                ContentUnavailableView("Nothing to choose", systemImage: "checkmark.circle", description: Text("Everything from the last sync fits together."))
            }
        }
        .listStyle(.insetGrouped)
        .jfBackground()
        .navigationTitle("Choose changes")
    }

    private func choice(_ part: MergeConflict.Part, _ value: MergeConflict.Choice, _ label: Text, _ detail: String) -> some View {
        let selected = choices[part.id] == value
        return Button { choices[part.id] = value; UISelectionFeedbackGenerator().selectionChanged() } label: {
            HStack(spacing: 12) {
                Image(systemName: selected ? "largecircle.fill.circle" : "circle").font(.title3).foregroundStyle(Color.jfAccent)
                VStack(alignment: .leading, spacing: 2) {
                    label.font(.jf(.body, .semibold)).foregroundStyle(Color.jfText)
                    Text(detail).font(.jf(.footnote)).foregroundStyle(Color.jfMuted)
                }
                Spacer()
            }
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .accessibilityAddTraits(selected ? .isSelected : [])
    }
}

/// About the app: version, licences, and what leaves the device.
struct AboutView: View {
    var body: some View {
        JFList {
            Section {
                VStack(alignment: .leading, spacing: 6) {
                    Text("Jourfold").font(.jf(.title2, .bold))
                    Text("Plan trips together. Your trips are folders you own, synced with Git when you choose.").font(.jf(.callout)).foregroundStyle(Color.jfMuted)
                }
                .padding(.vertical, 4)
                LabeledContent("Version", value: "\(Bundle.main.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String ?? "")")
            }
            Section {
                NavigationLink { LicenseText(name: "Jourfold-GPL-3.0") } label: { LabeledContent("Jourfold", value: "GPL-3.0-or-later") }
                NavigationLink { LicenseText(name: "TravelRepo-Apache-2.0") } label: { LabeledContent("TravelRepo", value: "Apache-2.0") }
                NavigationLink { LicenseText(name: "ThirdPartyNotices") } label: { Text("Open-source components") }
                LabeledContent("Plus Jakarta Sans", value: "OFL-1.1")
            } header: { Overline("Licences") }
            Section {
                Label("No analytics and no automatic crash reports.", systemImage: "hand.raised")
                Label("Trips, logs and diagnostics leave the phone only when you send them.", systemImage: "lock")
            } header: { Overline("Privacy") }
        }
        .listStyle(.insetGrouped)
        .jfBackground()
        .navigationTitle("About Jourfold")
    }
}

/// A licence that comes with the app, to read and copy.
struct LicenseText: View {
    let name: String
    var body: some View {
        ScrollView {
            Text(Bundle.main.url(forResource: name, withExtension: "txt").flatMap { try? String(contentsOf: $0, encoding: .utf8) } ?? "").font(.system(.caption, design: .monospaced)).textSelection(.enabled).padding()
        }
        .jfBackground().navigationTitle(name).navigationBarTitleDisplayMode(.inline)
    }
}

/// The day's downloaded forecast in one line, with when it was downloaded.
struct WeatherLine: View {
    let weather: DayWeather
    let place: String
    private var footnote: String {
        if weather.kind == "climate" { return String(localized: "\(place) · usually on this day, not a forecast") }
        let when = weather.fetched.map { $0.formatted(.relative(presentation: .named)) } ?? String(localized: "this morning")
        return weather.kind == "trend"
            ? String(localized: "\(place) · rain \(weather.rain) % · a trend that can still change · downloaded \(when)")
            : String(localized: "\(place) · rain \(weather.rain) % · downloaded \(when)")
    }
    var body: some View {
        HStack(spacing: 12) {
            Image.weather(weather.symbol).font(.title2)
                .frame(width: 36)
            VStack(alignment: .leading, spacing: 2) {
                Text("\(weather.high)° / \(weather.low)° · \(String(localized: weather.summary))").font(.jf(.body, .semibold))
                Text(footnote).font(.jf(.footnote)).foregroundStyle(Color.jfMuted)
            }
            Spacer()
        }
        .padding(14)
        .background(Color.jfSurface, in: RoundedRectangle(cornerRadius: 14, style: .continuous))
        .overlay(RoundedRectangle(cornerRadius: 14, style: .continuous).strokeBorder(Color.jfLine))
        .accessibilityElement(children: .combine)
    }
}

/// Free cancellation that ends within two days: the last moment to change one's mind.
struct CancellationNotice: View {
    let trip: Trip
    let now: Date
    @Environment(AppNavigation.self) private var nav
    var body: some View {
        let soon = trip.bookings.filter { b in
            guard let until = b.freeCancellationUntil, b.status != .cancelled else { return false }
            return until > now && until < now.addingTimeInterval(2 * 86400)
        }
        ForEach(soon) { b in
            Button { if let first = b.items.first { nav.detail = first } } label: {
                HStack(alignment: .top, spacing: 12) {
                    Image(systemName: "arrow.uturn.backward.circle").font(.title3).foregroundStyle(Color.jfWarning)
                    VStack(alignment: .leading, spacing: 2) {
                        Text("Free cancellation ends soon").font(.jf(.body, .semibold)).foregroundStyle(Color.jfText)
                        Text("\(b.title) · until \(b.freeCancellationUntil!.formatted(.dateTime.weekday(.abbreviated).hour().minute()))").font(.jf(.footnote)).foregroundStyle(Color.jfMuted)
                    }
                    Spacer()
                }
                .padding(14)
                .background(Color.jfWarning.opacity(0.10), in: RoundedRectangle(cornerRadius: 14, style: .continuous))
                .overlay(RoundedRectangle(cornerRadius: 14, style: .continuous).strokeBorder(Color.jfWarning.opacity(0.3)))
            }
            .buttonStyle(.plain)
        }
    }
}
