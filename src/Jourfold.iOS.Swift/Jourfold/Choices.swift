import SwiftUI

/// People as one row with their names; tapping it opens the list to tick them.
struct PeopleRow: View {
    let title: LocalizedStringKey
    let people: [Person]
    @Binding var chosen: Set<UUID>
    /// What the row says when nobody is ticked.
    var empty: LocalizedStringKey = "Nobody yet"

    var body: some View {
        NavigationLink {
            List {
                Section {
                    ForEach(people) { p in
                        CheckRow(on: chosen.contains(p.id)) { if chosen.remove(p.id) == nil { chosen.insert(p.id) } } label: {
                            HStack { PersonAvatar(person: p, size: 28).accessibilityHidden(true); Text(p.name) }
                        }
                    }
                }
                .listRowBackground(Color.jfSurface)
            }
            .jfBackground()
            .navigationTitle(title)
            .navigationBarTitleDisplayMode(.inline)
        } label: {
            LabeledContent {
                if names.isEmpty { Text(empty) } else { Text(names.formatted(.list(type: .and))) }
            } label: { Text(title) }
        }
    }

    private var names: [String] { people.filter { chosen.contains($0.id) }.map(\.name) }
}

/// The plans that are linked, each with a way to take it off, and a row that opens all of the trip's plans by day.
struct PlanRows: View {
    let trip: Trip
    @Binding var chosen: Set<UUID>

    var body: some View {
        ForEach(trip.items.filter { chosen.contains($0.id) }) { item in
            HStack {
                PlanLabel(item: item)
                Spacer()
                Button { withAnimation { _ = chosen.remove(item.id) } } label: { Image(systemName: "minus.circle").frame(minWidth: 44, minHeight: 44) }
                    .buttonStyle(.borderless).tint(.jfMuted)
                    .accessibilityLabel(Text("Unlink \(item.title)"))
            }
            .swipeActions { Button("Remove", role: .destructive) { chosen.remove(item.id) } }
        }
        NavigationLink { PlanChoice(trip: trip, chosen: $chosen) } label: { Label("Link plans", systemImage: "link").foregroundStyle(Color.jfAccent) }
    }
}

private struct PlanLabel: View {
    let item: PlanItem
    var body: some View {
        VStack(alignment: .leading, spacing: 2) {
            Text(item.title).foregroundStyle(Color.jfText)
            if let start = item.when.start { Text(Fmt.shortDay(start, item.when.zone)).font(.jf(.footnote)).foregroundStyle(Color.jfMuted) }
        }
    }
}

/// All plans of a trip by day, to tick; what was not planned yet comes last.
private struct PlanChoice: View {
    let trip: Trip
    @Binding var chosen: Set<UUID>
    @State private var search = ""
    @State private var searching = false

    var body: some View {
        let found = trip.items.filter { search.isEmpty || $0.title.localizedStandardContains(search) }
        let planned = Dictionary(grouping: found.filter { $0.when.start != nil }) { item in zoned(item).startOfDay(for: item.when.start!) }
        List {
            ForEach(planned.keys.sorted(), id: \.self) { day in
                let items = planned[day]!.sorted { $0.when.start! < $1.when.start! }
                Section { rows(items) } header: { Text(Fmt.shortDay(items[0].when.start!, items[0].when.zone)).font(.jf(.footnote, .semibold)) }
            }
            let open = found.filter { $0.when.start == nil }
            if !open.isEmpty { Section { rows(open) } header: { Overline("No day yet") } }
            if found.isEmpty { Text("Nothing found.").foregroundStyle(Color.jfMuted).listRowBackground(Color.jfSurface) }
        }
        .jfBackground()
        .searchable(text: $search, isPresented: $searching, placement: .navigationBarDrawer(displayMode: .always))
        .navigationTitle("Plans")
        .navigationBarTitleDisplayMode(.inline)
    }

    /// The day of a plan in the plan's own zone, so a day reads as it does in the plan.
    private func zoned(_ item: PlanItem) -> Calendar { var c = Calendar(identifier: .gregorian); c.timeZone = item.when.zone; return c }

    private func rows(_ items: [PlanItem]) -> some View {
        ForEach(items) { item in
            CheckRow(on: chosen.contains(item.id)) {
                if chosen.remove(item.id) == nil { chosen.insert(item.id) }
                // Found and ticked: back to all plans, with the way back in sight.
                search = ""; searching = false
            } label: { Text(item.title) }
        }
        .listRowBackground(Color.jfSurface)
    }
}

/// A row to tick, as iOS lists do it.
struct CheckRow<Content: View>: View {
    let on: Bool
    let action: () -> Void
    @ViewBuilder let label: Content
    var body: some View {
        Button(action: action) {
            HStack { label.foregroundStyle(Color.jfText); Spacer(); if on { Image(systemName: "checkmark").fontWeight(.semibold).foregroundStyle(Color.jfAccent).accessibilityHidden(true) } }
                .contentShape(Rectangle())
        }
        .accessibilityAddTraits(on ? .isSelected : [])
    }
}

/// A deadline that may be missing: "Add …" until there is one, then its date with a way to remove it.
struct OptionalDateRows: View {
    let add: LocalizedStringKey
    let title: LocalizedStringKey
    let remove: LocalizedStringKey
    @Binding var has: Bool
    @Binding var date: Date
    var parts: DatePickerComponents = [.date, .hourAndMinute]
    var body: some View {
        if has {
            DatePicker(title, selection: $date, displayedComponents: parts)
            Button(remove, role: .destructive) { withAnimation { has = false } }
        } else {
            Button { withAnimation { has = true } } label: { Label(add, systemImage: "calendar.badge.plus") }
        }
    }
}
