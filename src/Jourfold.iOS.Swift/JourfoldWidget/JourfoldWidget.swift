import WidgetKit
import SwiftUI
import ActivityKit
import AppIntents

// Jourfold's teal and navy, repeated here because the extension is its own module.
private extension Color {
    static let teal = Color.jfTeal
    static let tealLight = Color(red: 0x3E / 255, green: 0xC6 / 255, blue: 0xB0 / 255)
}

@main
struct JourfoldWidgets: WidgetBundle {
    var body: some Widget {
        NextUpWidget()
        CostsWidget()
        JourneyLiveActivity()
    }
}

// MARK: - Home and Lock Screen widget: the next plans

struct NextEntry: TimelineEntry {
    let date: Date
    let plans: [SharedInbox.UpcomingPlan]
    /// Check-outs of tomorrow, in the mode for today.
    var tomorrow: [SharedInbox.UpcomingPlan] = []
    var title: String? = nil
    var today = false
    /// The trip to open when there is no plan to open.
    var trip: UUID? = nil
}

/// The next plans; one entry each time the first of them ends.
struct NextProvider: AppIntentTimelineProvider {
    private var example: NextEntry {
        func plan(_ title: String, _ place: String, _ symbol: String, _ hours: Double) -> SharedInbox.UpcomingPlan {
            .init(title: title, trip: "Spring in Japan", start: .now.addingTimeInterval(hours * 3600), end: .now.addingTimeInterval((hours + 1) * 3600), zone: TimeZone.current.identifier, place: place, symbol: symbol)
        }
        return NextEntry(date: .now, plans: [plan("Hotel Gracery Shinjuku", "Shinjuku, Tokyo", "bed.double.fill", 1), plan("Explore Shinjuku", "Tokyo", "binoculars.fill", 2), plan("Dinner at Omoide Yokocho", "Tokyo", "fork.knife", 5), plan("Breakfast at Tsukiji", "Tokyo", "fork.knife", 18), plan("Senso-ji and Asakusa", "Tokyo", "building.columns.fill", 20)])
    }
    func placeholder(in context: Context) -> NextEntry { example }
    func snapshot(for configuration: NextPlansIntent, in context: Context) async -> NextEntry {
        if context.isPreview { return example }
        return entries(configuration).first ?? example
    }
    func timeline(for configuration: NextPlansIntent, in context: Context) async -> Timeline<NextEntry> { Timeline(entries: entries(configuration), policy: .atEnd) }

    private func entries(_ configuration: NextPlansIntent) -> [NextEntry] {
        SharedStore.set("seen/widget", Data(Date().formatted(.iso8601).utf8))
        let trip = configuration.trip?.uuid
        SharedStore.set("seen/widget-config", Data("\(configuration.mode.rawValue) \(configuration.trip?.id ?? "nil")".utf8))
        let all = SharedInbox.upcoming().filter { trip == nil || $0.tripID == trip }.sorted { $0.start < $1.start }
        let title = trip == nil ? nil : configuration.trip?.title
        // When something ends, and when a day begins where it happens, the widget changes.
        let midnights = all.map { p -> Date in var c = Calendar(identifier: .gregorian); c.timeZone = TimeZone(identifier: p.zone) ?? .current; return c.startOfDay(for: p.start) }
        let moments = [Date.now] + Array(Set(all.map(\.end) + (configuration.mode == .today ? midnights : []))).filter { $0 > .now }.sorted().prefix(24).map { $0.addingTimeInterval(1) }
        return moments.map { at in
            let open = all.filter { $0.end > at }
            guard configuration.mode == .today else { return NextEntry(date: at, plans: Array(open.prefix(8)), title: title, trip: trip ?? all.first?.tripID) }
            let (today, leaving) = SharedInbox.today(open, at: at)
            return NextEntry(date: at, plans: Array(today.prefix(8)), tomorrow: leaving, title: title, today: true, trip: trip ?? all.first?.tripID)
        }
    }
}

struct NextPlansView: View {
    @Environment(\.widgetFamily) private var family
    let entry: NextEntry

    private func name(_ p: SharedInbox.UpcomingPlan) -> String {
        p.role == "check_out" ? String(localized: "Check-out: \(p.title)") : p.role == "check_in" ? String(localized: "Check-in: \(p.title)") : p.title
    }
    private var nothing: String { entry.today ? String(localized: "Nothing more today") : String(localized: "Nothing planned") }

    private func time(_ p: SharedInbox.UpcomingPlan, clock: Bool = false) -> String {
        if let text = p.when { return text }
        let zone = TimeZone(identifier: p.zone) ?? .current
        var c = Calendar(identifier: .gregorian); c.timeZone = zone
        let f = DateFormatter(); f.timeZone = zone
        f.setLocalizedDateFormatFromTemplate(clock || c.isDate(p.start, inSameDayAs: entry.date) ? "jmm" : "EEEjmm")
        return f.string(from: p.start)
    }
    private func url(_ p: SharedInbox.UpcomingPlan?) -> URL? {
        guard let trip = p?.tripID ?? entry.trip else { return URL(string: "jourfold://today") }
        return URL(string: "jourfold://plan/\(trip.uuidString)" + (p?.itemID.map { "/\($0.uuidString)" } ?? ""))
    }

    var body: some View {
        let first = entry.plans.first
        Group {
            switch family {
            case .accessoryInline: Label(first.map { "\(time($0)) \(name($0))" } ?? nothing, systemImage: first?.symbol ?? "suitcase.rolling")
            case .accessoryCircular:
                ZStack { AccessoryWidgetBackground(); VStack(spacing: 1) { Image(systemName: first?.symbol ?? "suitcase.rolling"); if let first { Text(time(first)).font(.caption2.monospacedDigit()).minimumScaleFactor(0.5).lineLimit(1) } }.padding(4) }
            case .accessoryRectangular:
                if let first {
                    VStack(alignment: .leading, spacing: 1) {
                        Label(time(first), systemImage: first.symbol).font(.caption.monospacedDigit())
                        Text(name(first)).font(.headline).lineLimit(1)
                        Text(first.place).font(.caption).lineLimit(1)
                    }
                    .frame(maxWidth: .infinity, alignment: .leading)
                } else { Text(nothing) }
            case .systemMedium: list(3)
            case .systemLarge: list(7)
            default: small(first)
            }
        }
        .containerBackground(for: .widget) { Color(.systemBackground) }
        .widgetURL(url(first))
    }

    /// Below the title: the role of a stay and the place, unless the title says the place already.
    private func detail(_ p: SharedInbox.UpcomingPlan, tomorrow: Bool = false, short: Bool = false) -> String {
        let role: String? = p.role == "check_in" ? String(localized: "Check-in") : p.role == "check_out" ? (tomorrow ? String(localized: "Check-out tomorrow") : String(localized: "Check-out")) : nil
        if short, let role { return role }
        return [role, p.title.localizedCaseInsensitiveContains(p.place) ? nil : p.place].compactMap { $0 }.joined(separator: " · ")
    }

    private func small(_ p: SharedInbox.UpcomingPlan?) -> some View {
        // With nothing left today, tomorrow's check-out takes the place of the plan.
        let out = p == nil ? entry.tomorrow.first : nil, shown = p ?? out
        return VStack(alignment: .leading, spacing: 4) {
            HStack {
                Image(systemName: out != nil ? "door.left.hand.open" : shown?.symbol ?? "suitcase.rolling").font(.headline).foregroundStyle(Color.teal)
                Spacer()
                if let shown { Text(time(shown, clock: out != nil)).font(.headline.monospacedDigit()).minimumScaleFactor(0.7).lineLimit(1) }
            }
            Spacer(minLength: 0)
            Text(out != nil ? "Nothing more today" : entry.today ? "Today" : "Next").font(.caption.weight(.semibold)).foregroundStyle(.secondary).lineLimit(1).minimumScaleFactor(0.8)
            Text(shown?.title ?? nothing).font(.subheadline.weight(.bold)).lineLimit(3).minimumScaleFactor(0.85)
            if let shown { Text(detail(shown, tomorrow: out != nil, short: true)).font(.caption).foregroundStyle(.secondary).lineLimit(1) }
        }
        .frame(maxWidth: .infinity, alignment: .leading)
    }

    private func row(_ p: SharedInbox.UpcomingPlan, lines: Int, tomorrow: Bool = false) -> some View {
        Link(destination: url(p) ?? URL(string: "jourfold://today")!) {
            HStack(alignment: lines > 1 ? .top : .center, spacing: 10) {
                Image(systemName: tomorrow ? "door.left.hand.open" : p.symbol).font(.callout.weight(.semibold)).foregroundStyle(Color.teal).frame(width: 26)
                VStack(alignment: .leading, spacing: 1) {
                    Text(p.title).font(.subheadline.weight(.semibold)).lineLimit(lines).fixedSize(horizontal: false, vertical: lines > 1)
                    let below = detail(p, tomorrow: tomorrow)
                    if !below.isEmpty { Text(below).font(.caption).foregroundStyle(.secondary).lineLimit(1) }
                }
                Spacer(minLength: 4)
                Text(time(p, clock: tomorrow)).font(.caption.monospacedDigit().weight(.semibold)).lineLimit(1).fixedSize()
            }
        }
        .accessibilityElement(children: .combine)
    }

    private func list(_ count: Int) -> some View {
        let plans = Array(entry.plans.prefix(entry.tomorrow.isEmpty ? count : max(count - 1, 1)))
        let leaving = Array(entry.tomorrow.prefix(max(count - plans.count - (plans.isEmpty ? 1 : 0), 1)))
        // Few rows have room for long titles.
        let lines = plans.count + leaving.count + (plans.isEmpty ? 1 : 0) <= (count > 3 ? 4 : 2) ? 2 : 1
        return VStack(alignment: .leading, spacing: 8) {
            Text([entry.today ? String(localized: "Today") : nil, entry.title ?? entry.plans.first?.trip ?? entry.tomorrow.first?.trip ?? String(localized: "Next plans")].compactMap { $0 }.joined(separator: " · ")).font(.caption.weight(.semibold)).foregroundStyle(.secondary).lineLimit(1)
            if plans.isEmpty { Text(nothing).font(leaving.isEmpty ? .subheadline : .footnote).foregroundStyle(leaving.isEmpty ? Color.primary : Color.secondary) }
            ForEach(Array(plans.enumerated()), id: \.offset) { _, p in row(p, lines: lines) }
            ForEach(Array(leaving.enumerated()), id: \.offset) { _, p in row(p, lines: lines, tomorrow: true) }
            Spacer(minLength: 0)
        }
        .frame(maxWidth: .infinity, alignment: .leading)
    }
}

struct NextUpWidget: Widget {
    var body: some WidgetConfiguration {
        AppIntentConfiguration(kind: "NextUp", intent: NextPlansIntent.self, provider: NextProvider()) { NextPlansView(entry: $0) }
            .configurationDisplayName("Next plans")
            .description("What comes next, of all trips or one.")
            .supportedFamilies([.systemSmall, .systemMedium, .systemLarge, .accessoryRectangular, .accessoryCircular, .accessoryInline])
    }
}

// MARK: - Live Activity: a journey on its way

struct JourneyLiveActivity: Widget {
    var body: some WidgetConfiguration {
        ActivityConfiguration(for: JourneyAttributes.self) { context in
            let s = context.state
            VStack(alignment: .leading, spacing: 10) {
                HStack(spacing: 10) {
                    Image(systemName: s.symbol).font(.title3.weight(.semibold)).foregroundStyle(Color.tealLight)
                    VStack(alignment: .leading, spacing: 1) {
                        Text(s.title).font(.headline)
                        Text("\(s.from) → \(s.to)").font(.caption).foregroundStyle(.secondary)
                    }
                    Spacer()
                    Text(timerInterval: Date.now...max(s.departs, .now), countsDown: true)
                        .font(.title3.monospacedDigit().weight(.semibold))
                        .multilineTextAlignment(.trailing)
                        .frame(width: 80)
                }
                ProgressView(timerInterval: s.departs...s.arrives, countsDown: false) { EmptyView() } currentValueLabel: { EmptyView() }
                    .tint(Color.tealLight)
                HStack {
                    Text(s.departs, style: .time)
                    Spacer()
                    if let seat = s.seat { Label(seat, systemImage: "chair") }
                    Spacer()
                    Text(s.arrives, style: .time)
                }
                .font(.caption.monospacedDigit())
                .foregroundStyle(.secondary)
            }
            .padding(16)
            .activityBackgroundTint(Color(red: 0x0E / 255, green: 0x21 / 255, blue: 0x35 / 255))
            .activitySystemActionForegroundColor(Color.tealLight)
        } dynamicIsland: { context in
            let s = context.state
            return DynamicIsland {
                DynamicIslandExpandedRegion(.leading) { Image(systemName: s.symbol).foregroundStyle(Color.tealLight).font(.title2) }
                DynamicIslandExpandedRegion(.trailing) { Text(s.departs, style: .time).font(.title3.monospacedDigit()) }
                DynamicIslandExpandedRegion(.center) { Text(s.title).font(.headline).lineLimit(1) }
                DynamicIslandExpandedRegion(.bottom) {
                    ProgressView(timerInterval: s.departs...s.arrives, countsDown: false) { EmptyView() } currentValueLabel: { EmptyView() }
                        .tint(Color.tealLight)
                }
            } compactLeading: {
                Image(systemName: s.symbol).foregroundStyle(Color.tealLight)
            } compactTrailing: {
                Text(s.departs, style: .time).monospacedDigit()
            } minimal: {
                Image(systemName: s.symbol).foregroundStyle(Color.tealLight)
            }
        }
    }
}
