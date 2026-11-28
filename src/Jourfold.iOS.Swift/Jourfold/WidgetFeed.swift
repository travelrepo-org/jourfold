import Foundation
import TravelRepoKit

/// What the widgets show, left in the shared store whenever a trip was read.
@MainActor
enum WidgetFeed {
    /// The next plans of every trip, a stay as its check-in and its check-out.
    static func plans(_ store: TripStore) -> [SharedInbox.UpcomingPlan] {
        store.trips.flatMap { trip -> [SharedInbox.UpcomingPlan] in
            let all: [SharedInbox.UpcomingPlan] = trip.items.flatMap { item -> [SharedInbox.UpcomingPlan] in
                guard item.status != .cancelled, item.status != .idea, let start = item.when.start, !trip.items.contains(where: { $0.parent == item.id }) else { return [] }
                let end = item.when.end ?? start, zone = item.when.zone.identifier
                let place = trip.place(item.stay?.place ?? item.place ?? item.transport?.to)?.name ?? trip.title
                let symbol = item.transport?.mode.symbol ?? item.kind.symbol
                func entry(_ from: Date, _ to: Date, _ zone: String, _ role: String? = nil, _ when: String? = nil) -> SharedInbox.UpcomingPlan {
                    .init(title: item.title, trip: trip.title, start: from, end: to, zone: zone, place: place, symbol: symbol, tripID: trip.id, itemID: item.id, role: role, when: when)
                }
                if item.stay != nil {
                    // A check-in stays in view for a while after it began.
                    return [entry(start, start.addingTimeInterval(2 * 3600), zone, "check_in"), entry(end, end, item.when.endZone.identifier, "check_out")]
                }
                if case .exact = item.when { return [entry(start, max(end, start.addingTimeInterval(60)), zone)] }
                return [entry(start, max(end, start.addingTimeInterval(3600)), zone, nil, Fmt.when(item.when))]
            }
            return Array(all.filter { $0.end > store.now }.sorted { $0.start < $1.start }.prefix(20))
        }.sorted { $0.start < $1.start }
    }

    static func publishCosts(_ store: TripStore) async {
        var out: [SharedInbox.CostSummary] = []
        for trip in store.trips {
            guard let bridge = TravelRepoBridge.all[trip.id], let c = try? await bridge.costDetails(), let paid = c.paid_in, !c.expenses.isEmpty || c.budget != nil || !c.budgets.isEmpty else { continue }
            var planned = Decimal(0)
            for (code, list) in Dictionary(grouping: c.expenses.filter { $0.planned && !$0.idea }, by: \.code) {
                let sum = list.reduce(Decimal(0)) { $0 + $1.value }
                if code == paid.currency { planned += sum }
                else if let v = try? await bridge.session.call("convert_currency", ["amount": .string(NSDecimalNumber(decimal: sum).stringValue), "from": .string(code), "to": .string(paid.currency)])["result"]?.number { planned += Decimal(v).rounded(paid.currency) }
            }
            let me = store.me[trip.id].flatMap { trip.person($0)?.name }
            let mine = me.flatMap { name in c.balances.first { $0.person == name && $0.currency == paid.currency } }
            out.append(.init(tripID: trip.id, trip: trip.title, currency: paid.currency, start: trip.calendar.date(from: trip.start) ?? .distantPast, end: trip.calendar.date(from: trip.end) ?? .distantFuture,
                             paid: paid.total, paidEstimated: paid.estimated > 0, planned: planned,
                             budget: c.budget.flatMap { b in b.currency == paid.currency ? .init(title: "", amount: b.amount, used: b.spent + b.planned, left: b.left, estimated: b.estimated == true) : nil },
                             budgets: c.budgets.filter { $0.currency == paid.currency }.map { .init(title: $0.title, amount: $0.amount, used: $0.spent + $0.planned, left: $0.left, estimated: $0.estimated == true) },
                             balance: me == nil ? nil : mine?.net ?? 0, balanceEstimated: mine?.estimated == true))
        }
        SharedInbox.publishCosts(out)
    }
}
