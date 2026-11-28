import SwiftUI

/// A time grid as the live app's timetable: one day on the phone, as many as fit on a tablet. All-day plans in a strip above, plans as
/// tinted blocks with their kind's icon, ideas and parts of the day dashed, and a line for now. Swipe for the next day.
struct TimetableView: View {
    let tripID: UUID
    @Environment(TripStore.self) private var store
    @Environment(AppNavigation.self) private var nav
    @State private var day: Int

    private let hour: CGFloat = 56
    private let gutter: CGFloat = 52

    init(tripID: UUID, day: Int) {
        self.tripID = tripID
        _day = State(initialValue: day)
    }

    var body: some View {
        let trip = store.trip(tripID)
        GeometryReader { geo in
            let shown = Self.daysShown(geo.size.width, of: trip.days.count)
            Group {
                if shown == 1 {
                    TabView(selection: $day) { ForEach(Array(trip.days.enumerated()), id: \.offset) { i, _ in grid(trip, [i], width: geo.size.width).tag(i) } }
                        .tabViewStyle(.page(indexDisplayMode: .never))
                } else {
                    grid(trip, Self.window(day, shown, trip.days.count), width: geo.size.width)
                        .gesture(DragGesture(minimumDistance: 40).onEnded { g in
                            guard abs(g.translation.width) > abs(g.translation.height) * 1.5 else { return }
                            step(g.translation.width < 0 ? shown : -shown, trip, shown)
                        })
                }
            }
            .toolbar {
                ToolbarItemGroup(placement: .topBarTrailing) {
                    Button { step(-shown, trip, shown) } label: { Image(systemName: "chevron.left") }
                        .disabled(Self.window(day, shown, trip.days.count).first ?? 0 == 0).accessibilityLabel(shown == 1 ? Text("Previous day") : Text("Earlier days"))
                    Button { step(shown, trip, shown) } label: { Image(systemName: "chevron.right") }
                        .disabled(Self.window(day, shown, trip.days.count).last ?? 0 >= trip.days.count - 1).accessibilityLabel(shown == 1 ? Text("Next day") : Text("Later days"))
                }
            }
            .navigationTitle(title(trip, Self.window(day, shown, trip.days.count)))
        }
        .background(Color.jfApp.ignoresSafeArea())
        .navigationBarTitleDisplayMode(.inline)
    }

    /// As many days as fit, an odd number so the chosen one is in the middle; one on a phone.
    static func daysShown(_ width: CGFloat, of count: Int) -> Int {
        guard width >= 700 else { return 1 }
        var n = min(max(Int((width - 60) / 190), 3), 7); if n % 2 == 0 { n -= 1 }
        return max(1, min(n, count))
    }
    static func window(_ day: Int, _ shown: Int, _ count: Int) -> [Int] {
        guard count > 0 else { return [] }
        let start = min(max(day - shown / 2, 0), max(count - shown, 0))
        return Array(start..<min(start + shown, count))
    }
    private func step(_ by: Int, _ trip: Trip, _ shown: Int) {
        // From the middle of what is shown, so every step moves.
        let w = Self.window(day, shown, trip.days.count), middle = (w.first ?? 0) + shown / 2
        withAnimation { day = min(max((shown == 1 ? day : middle) + by, 0), trip.days.count - 1) }
    }
    private func title(_ trip: Trip, _ w: [Int]) -> String {
        guard let a = w.first, let b = w.last, trip.days.indices.contains(b) else { return "" }
        let zone = trip.calendar.timeZone
        return a == b ? Fmt.shortDay(trip.days[a], zone) : "\(Fmt.shortDay(trip.days[a], zone)) – \(Fmt.shortDay(trip.days[b], zone))"
    }

    /// A plan's part of one day, in minutes of that day's zone.
    private struct Block: Identifiable { let item: PlanItem; let from: Int; let to: Int; var lane = 0; var lanes = 1; var continued = false; var id: String { item.id.uuidString + (continued ? "c" : "") } }

    /// The zone the day's first own plan starts in; a journey continuing from the day before does not count.
    private func zone(_ trip: Trip, _ d: Date) -> TimeZone {
        trip.items(on: d).first { $0.stay == nil && { if case .exact = $0.when { true } else { false } }($0) }?.when.zone ?? trip.calendar.timeZone
    }

    private func blocks(_ trip: Trip, _ d: Date) -> [Block] {
        let z = zone(trip, d)
        var out: [Block] = trip.items(on: d).compactMap { item in
            if case .allDay = item.when { return nil }
            guard item.stay == nil, let s = item.when.start, let e = item.when.end, let sm = minutesOf(s, z) else { return nil }
            var c = Calendar(identifier: .gregorian); c.timeZone = z
            let em = c.isDate(e, inSameDayAs: s) ? (minutesOf(e, z) ?? sm + 60) : 24 * 60
            return Block(item: item, from: sm, to: max(em, sm + 30))
        }
        // The rest of a journey that began the day before.
        for line in trip.lines(on: d) where line.role == "continued" {
            guard let item = trip.item(line.item), item.stay == nil, let e = item.when.end, let em = minutesOf(e, item.when.endZone) else { continue }
            var c = Calendar(identifier: .gregorian); c.timeZone = item.when.endZone
            let ends = c.ymd(e) == trip.calendar.ymd(d)
            if ends, em > 0 { out.append(Block(item: item, from: 0, to: max(em, 30), continued: true)) }
        }
        out.sort { ($0.from, $0.to) < ($1.from, $1.to) }
        // Plans at the same time share the width.
        var i = 0
        while i < out.count {
            var j = i, end = out[i].to
            while j + 1 < out.count, out[j + 1].from < end { j += 1; end = max(end, out[j].to) }
            var free: [Int] = []
            for k in i...j {
                if let lane = free.firstIndex(where: { $0 <= out[k].from }) { out[k].lane = lane; free[lane] = out[k].to } else { out[k].lane = free.count; free.append(out[k].to) }
            }
            for k in i...j { out[k].lanes = free.count }
            i = j + 1
        }
        return out
    }

    private func grid(_ trip: Trip, _ shown: [Int], width: CGFloat) -> some View {
        let days = shown.map { trip.days[$0] }
        let all = days.map { blocks(trip, $0) }
        let first = max(0, min((all.flatMap { $0 }.map { $0.from }.min() ?? 480) / 60, 8) - 1)
        let last = min(24, max((all.flatMap { $0 }.map { $0.to }.max() ?? 1200) / 60 + 1, 21))
        let column = max((width - gutter - 16) / CGFloat(max(days.count, 1)), 80)
        return VStack(spacing: 0) {
            HStack(alignment: .top, spacing: 0) {
                Color.clear.frame(width: gutter, height: 1)
                ForEach(Array(days.enumerated()), id: \.offset) { n, d in
                    VStack(alignment: .leading, spacing: 4) {
                        if days.count > 1 { Text(Fmt.shortDay(d, trip.calendar.timeZone)).font(.jf(.footnote, shown[n] == day ? .bold : .semibold)).foregroundStyle(shown[n] == day ? Color.jfAccentSoftText : Color.jfText) }
                        let z = zone(trip, d)
                        if z.secondsFromGMT(for: d) != trip.calendar.timeZone.secondsFromGMT(for: d) { Text("Times in \(Fmt.city(z))").font(.jf(.caption)).foregroundStyle(Color.jfMuted) }
                        ForEach(trip.items(on: d).filter { if case .allDay = $0.when { true } else { false } }) { chip($0, Text("All day")) }
                        if let stay = trip.stay(on: d) { chip(stay, Text("Tonight")) }
                    }
                    .padding(.trailing, 4)
                    .dynamicTypeSize(...DynamicTypeSize.xxLarge)
                    .frame(width: column, alignment: .leading)
                }
            }
            .frame(maxWidth: .infinity, alignment: .leading)
            .padding(.vertical, 8)
            ScrollView {
                ZStack(alignment: .topLeading) {
                    VStack(spacing: 0) {
                        ForEach(first..<last, id: \.self) { h in
                            HStack(alignment: .top, spacing: 8) {
                                Text(String(format: "%02d:00", h)).font(.jf(.caption2, .medium).monospacedDigit()).foregroundStyle(Color.jfMuted)
                                    .dynamicTypeSize(...DynamicTypeSize.large)
                                    .frame(width: gutter - 8, alignment: .trailing).offset(y: -7)
                                    .accessibilityHidden(true)
                                Rectangle().fill(Color.jfLine).frame(height: 1)
                            }
                            .frame(height: hour, alignment: .top)
                        }
                    }
                    ForEach(Array(days.enumerated()), id: \.offset) { n, d in
                        let x = gutter + CGFloat(n) * column, z = zone(trip, d)
                        dayMarkers(trip, d, z, first: first, last: last).frame(width: column, alignment: .topLeading).offset(x: x)
                        ForEach(all[n]) { b in
                            let w = (column - 6) / CGFloat(b.lanes)
                            block(b, width: w - 2, height: max(CGFloat(b.to - b.from) / 60 * hour - 2, 30))
                                .offset(x: x + CGFloat(b.lane) * w, y: CGFloat(b.from - first * 60) / 60 * hour + 1)
                        }
                        if trip.calendar.ymd(trip.today(store.now)) == trip.calendar.ymd(d), let m = minutesOf(store.now, trip.currentZone(store.now)), m >= first * 60 {
                            HStack(spacing: 0) {
                                Circle().fill(Color.jfAccent).frame(width: 8, height: 8)
                                Rectangle().fill(Color.jfAccent).frame(height: 2)
                            }
                            .frame(width: column)
                            .offset(x: x - 4, y: CGFloat(m - first * 60) / 60 * hour - 4)
                            .accessibilityElement(children: .ignore)
                            .accessibilityLabel(Text("Now · \(Fmt.time(store.now, trip.currentZone(store.now)))"))
                        }
                    }
                }
                .padding(.trailing, 16)
                .padding(.vertical, 12)
            }
        }
    }

    private func dayMarkers(_ trip: Trip, _ d: Date, _ z: TimeZone, first: Int, last: Int) -> some View {
        ZStack(alignment: .topLeading) {
            Color.clear.frame(height: 1)
            ForEach(trip.items.filter { $0.stay != nil && $0.status != .cancelled }) { stay in
                if let s = stay.when.start, trip.calendar.isDate(s, inSameDayAs: d), let h = minutesOf(s, z), h >= first * 60 {
                    marker(Text("Check-in \(stay.title)"), y: CGFloat(h - first * 60) / 60 * hour, color: Kind.accommodation.colors.bar)
                }
                // The night itself, as a bar along the day's edge.
                if let s = stay.when.start, let e = stay.when.end {
                    let begins = trip.calendar.isDate(s, inSameDayAs: d), ends = trip.calendar.isDate(e, inSameDayAs: d)
                    if begins || ends || (s < d && e > d) {
                        let from = begins ? max(minutesOf(s, z) ?? 0, first * 60) : first * 60, to = ends ? minutesOf(e, z) ?? 0 : last * 60
                        if to > from {
                            RoundedRectangle(cornerRadius: 2).fill(Kind.accommodation.colors.bar.opacity(0.45)).frame(width: 4, height: CGFloat(to - from) / 60 * hour)
                                .frame(maxWidth: .infinity, alignment: .trailing).offset(y: CGFloat(from - first * 60) / 60 * hour).accessibilityHidden(true)
                        }
                    }
                }
                if let e = stay.when.end, trip.calendar.isDate(e, inSameDayAs: d), let h = minutesOf(e, z), h >= first * 60 {
                    marker(Text("Check-out \(stay.title)"), y: CGFloat(h - first * 60) / 60 * hour, color: Kind.accommodation.colors.bar)
                }
            }
        }
    }

    private func chip(_ item: PlanItem, _ label: Text) -> some View {
        Button { nav.detail = item.id } label: {
            HStack(spacing: 8) {
                Image(systemName: item.kind.symbol).foregroundStyle(item.kind.colors.fg)
                Text(item.title).font(.jf(.footnote, .semibold)).foregroundStyle(Color.jfText).lineLimit(1)
                Spacer()
                label.font(.jf(.caption)).foregroundStyle(Color.jfMuted).lineLimit(1)
            }
            .padding(.horizontal, 10).padding(.vertical, 7)
            .background(item.kind.colors.bg, in: RoundedRectangle(cornerRadius: 8, style: .continuous))
        }
        .buttonStyle(.plain)
    }

    private func marker(_ label: Text, y: CGFloat, color: Color) -> some View {
        HStack(spacing: 6) {
            Rectangle().fill(color.opacity(0.6)).frame(width: 18, height: 2)
            label.font(.jf(.caption2, .medium)).foregroundStyle(Color.jfMuted).lineLimit(1)
        }
        .offset(y: y - 6)
        .accessibilityElement(children: .combine)
    }

    private func block(_ b: Block, width: CGFloat, height: CGFloat) -> some View {
        let item = b.item, c = item.kind.colors
        let dashed: Bool = { if case .exact = item.when { return item.status == .idea } else { return true } }()
        return Button { nav.detail = item.id } label: {
            HStack(alignment: .top, spacing: 6) {
                RoundedRectangle(cornerRadius: 2).fill(c.bar).frame(width: 3)
                VStack(alignment: .leading, spacing: 2) {
                    HStack(spacing: 5) {
                        Image(systemName: item.transport?.mode.symbol ?? item.kind.symbol).font(.system(size: 11, weight: .semibold))
                        Text(item.title).font(.jf(.footnote, .semibold)).lineLimit(height > 60 ? 3 : 1)
                    }
                    .foregroundStyle(c.fg)
                    if height > 44 { Text(Fmt.when(item.when)).font(.jf(.caption2)).foregroundStyle(Color.jfMuted).lineLimit(2) }
                }
                Spacer(minLength: 0)
            }
            .padding(.vertical, 5).padding(.trailing, 4)
            .frame(width: width, height: height, alignment: .topLeading)
            .background(c.bg.opacity(dashed ? 0.55 : 1), in: RoundedRectangle(cornerRadius: 8, style: .continuous))
            .clipShape(RoundedRectangle(cornerRadius: 8, style: .continuous))
            .overlay { if dashed { RoundedRectangle(cornerRadius: 8, style: .continuous).strokeBorder(c.bar, style: StrokeStyle(lineWidth: 1, dash: [4, 3])) } }
        }
        .buttonStyle(.plain)
        // The grid's blocks have the size of their time; very large text would not fit.
        .dynamicTypeSize(...DynamicTypeSize.xxxLarge)
        .accessibilityLabel(Text("\(item.title), \(Fmt.when(item.when))"))
    }

    private func minutesOf(_ date: Date?, _ zone: TimeZone) -> Int? {
        guard let date else { return nil }
        var c = Calendar(identifier: .gregorian); c.timeZone = zone
        let comps = c.dateComponents([.hour, .minute], from: date)
        return (comps.hour ?? 0) * 60 + (comps.minute ?? 0)
    }
}
