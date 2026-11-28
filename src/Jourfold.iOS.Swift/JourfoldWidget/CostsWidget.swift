import SwiftUI
import WidgetKit

struct CostsEntry: TimelineEntry { let date: Date; let costs: SharedInbox.CostSummary? }

struct CostsProvider: AppIntentTimelineProvider {
    private var example: CostsEntry {
        .init(date: .now, costs: .init(tripID: UUID(), trip: "Spring in Japan", currency: "EUR", start: .now, end: .now, paid: 1280, paidEstimated: true, planned: 420,
                                       budget: .init(title: "", amount: 3500, used: 1700, left: 1800, estimated: true),
                                       budgets: [.init(title: "Food", amount: 400, used: 260, left: 140, estimated: false), .init(title: "Activities", amount: 300, used: 310, left: -10, estimated: false)], balance: -45, balanceEstimated: false))
    }
    func placeholder(in context: Context) -> CostsEntry { example }
    func snapshot(for configuration: CostsIntent, in context: Context) async -> CostsEntry { context.isPreview ? example : CostsEntry(date: .now, costs: pick(configuration)) }
    func timeline(for configuration: CostsIntent, in context: Context) async -> Timeline<CostsEntry> {
        Timeline(entries: [CostsEntry(date: .now, costs: pick(configuration))], policy: .after(.now.addingTimeInterval(3 * 3600)))
    }

    private func pick(_ configuration: CostsIntent) -> SharedInbox.CostSummary? {
        let all = SharedInbox.costs()
        if let id = configuration.trip?.uuid { return all.first { $0.tripID == id } }
        let now = Date.now
        return all.first { $0.start <= now && now < $0.end.addingTimeInterval(86400) } ?? all.filter { $0.start > now }.min { $0.start < $1.start } ?? all.max { $0.end < $1.end }
    }
}

struct CostsWidgetView: View {
    @Environment(\.widgetFamily) private var family
    let entry: CostsEntry

    private func money(_ v: Decimal, _ c: SharedInbox.CostSummary, _ estimated: Bool = false, whole: Bool = false) -> String {
        (estimated ? "≈ " : "") + v.formatted(.currency(code: c.currency).precision(.fractionLength(whole ? 0...0 : 0...2)))
    }

    var body: some View {
        Group {
            if let c = entry.costs {
                switch family {
                case .accessoryCircular: circular(c)
                case .accessoryRectangular: rectangular(c)
                case .accessoryInline: Text(c.budget.map { String(localized: "\(money($0.left, c, whole: true)) left") } ?? money(c.paid, c, c.paidEstimated, whole: true))
                case .systemMedium: page(c, limits: 1)
                case .systemLarge: page(c, limits: 5)
                default: small(c)
                }
            } else {
                Label("No costs yet", systemImage: "wallet.bifold").font(.subheadline).foregroundStyle(.secondary)
            }
        }
        .containerBackground(for: .widget) { Color(.systemBackground) }
        .widgetURL(URL(string: "jourfold://costs" + (entry.costs.map { "/\($0.tripID.uuidString)" } ?? "")))
        // Amounts are private on a locked screen.
        .privacySensitive()
    }

    private func bar(_ b: SharedInbox.CostSummary.Budget) -> some View {
        let part = b.amount > 0 ? NSDecimalNumber(decimal: b.used / b.amount).doubleValue : 0
        return ProgressView(value: min(max(part, 0), 1)).tint(part > 1 ? Color.red : part > 0.85 ? Color.orange : Color.jfTeal)
            .accessibilityLabel(Text("\(Int(part * 100)) percent used"))
    }
    private func leftText(_ b: SharedInbox.CostSummary.Budget, _ c: SharedInbox.CostSummary) -> String {
        b.left >= 0 ? String(localized: "\(money(b.left, c, b.estimated, whole: true)) left") : String(localized: "\(money(-b.left, c, b.estimated, whole: true)) over budget")
    }

    private func small(_ c: SharedInbox.CostSummary) -> some View {
        VStack(alignment: .leading, spacing: 5) {
            Label(c.trip, systemImage: "wallet.bifold").font(.caption.weight(.semibold)).foregroundStyle(Color.jfTeal).lineLimit(1)
            Spacer(minLength: 0)
            Text(money(c.paid, c, c.paidEstimated, whole: true)).font(.title3.weight(.bold)).minimumScaleFactor(0.6).lineLimit(1)
            Text("paid").font(.caption).foregroundStyle(.secondary)
            if let b = c.budget {
                bar(b)
                HStack(spacing: 3) {
                    if b.left < 0 { Image(systemName: "exclamationmark.triangle.fill") }
                    Text(leftText(b, c)).lineLimit(1).minimumScaleFactor(0.7)
                }
                .font(.caption).foregroundStyle(b.left < 0 ? Color.red : Color.secondary)
            }
        }
        .frame(maxWidth: .infinity, alignment: .leading)
    }

    private func page(_ c: SharedInbox.CostSummary, limits: Int) -> some View {
        VStack(alignment: .leading, spacing: limits > 1 ? 9 : 6) {
            HStack(alignment: .firstTextBaseline) {
                Label(c.trip, systemImage: "wallet.bifold").font(.caption.weight(.semibold)).foregroundStyle(Color.jfTeal).lineLimit(1)
                Spacer()
                if let balance = c.balance, balance != 0 {
                    Text(balance > 0 ? "You get \(money(balance, c, c.balanceEstimated))" : "You owe \(money(-balance, c, c.balanceEstimated))").font(.caption.weight(.semibold)).lineLimit(1)
                }
            }
            HStack(alignment: .firstTextBaseline, spacing: 14) {
                VStack(alignment: .leading, spacing: 0) { Text(money(c.paid, c, c.paidEstimated)).font(.title3.weight(.bold)).minimumScaleFactor(0.6).lineLimit(1); Text("paid").font(.caption).foregroundStyle(.secondary) }
                if c.planned > 0 { VStack(alignment: .leading, spacing: 0) { Text(money(c.planned, c)).font(.headline).minimumScaleFactor(0.6).lineLimit(1); Text("still to pay").font(.caption).foregroundStyle(.secondary) } }
                Spacer(minLength: 0)
            }
            if let b = c.budget { row(String(localized: "Whole trip"), b, c) }
            ForEach(Array(c.budgets.prefix(c.budget == nil ? limits + 1 : limits).enumerated()), id: \.offset) { _, b in row(b.title, b, c) }
            if c.budget == nil, c.budgets.isEmpty { Text("No budget yet").font(.caption).foregroundStyle(.secondary) }
            Spacer(minLength: 0)
        }
        .frame(maxWidth: .infinity, alignment: .leading)
    }

    private func row(_ title: String, _ b: SharedInbox.CostSummary.Budget, _ c: SharedInbox.CostSummary) -> some View {
        VStack(alignment: .leading, spacing: 2) {
            HStack {
                Text(title).font(.caption.weight(.semibold)).lineLimit(1)
                Spacer()
                if b.left < 0 { Image(systemName: "exclamationmark.triangle.fill").font(.caption2).foregroundStyle(.red) }
                Text(leftText(b, c)).font(.caption).foregroundStyle(b.left < 0 ? Color.red : Color.secondary).lineLimit(1)
            }
            bar(b)
        }
        .accessibilityElement(children: .combine)
    }

    private func circular(_ c: SharedInbox.CostSummary) -> some View {
        let part = c.budget.map { $0.amount > 0 ? min(max(NSDecimalNumber(decimal: $0.used / $0.amount).doubleValue, 0), 1) : 0 } ?? 0
        return Gauge(value: part) { Image(systemName: "wallet.bifold") } currentValueLabel: { Text(c.budget == nil ? "–" : "\(Int(part * 100))%") }.gaugeStyle(.accessoryCircularCapacity)
    }

    private func rectangular(_ c: SharedInbox.CostSummary) -> some View {
        VStack(alignment: .leading, spacing: 1) {
            Label(c.trip, systemImage: "wallet.bifold").font(.caption).lineLimit(1)
            Text("\(money(c.paid, c, c.paidEstimated, whole: true)) paid").font(.headline).lineLimit(1)
            if let b = c.budget { Text(leftText(b, c)).font(.caption).lineLimit(1) }
        }
        .frame(maxWidth: .infinity, alignment: .leading)
    }
}

extension Color {
    /// Jourfold's teal, lighter on a dark background.
    static let jfTeal = Color(UIColor { $0.userInterfaceStyle == .dark ? UIColor(red: 0x3E / 255, green: 0xC6 / 255, blue: 0xB0 / 255, alpha: 1) : UIColor(red: 0x0F / 255, green: 0x7B / 255, blue: 0x7A / 255, alpha: 1) })
}

struct CostsWidget: Widget {
    var body: some WidgetConfiguration {
        AppIntentConfiguration(kind: "Costs", intent: CostsIntent.self, provider: CostsProvider()) { CostsWidgetView(entry: $0) }
            .configurationDisplayName("Costs and budgets")
            .description("What the trip has cost and what is left of its budgets.")
            .supportedFamilies([.systemSmall, .systemMedium, .systemLarge, .accessoryRectangular, .accessoryCircular, .accessoryInline])
    }
}
