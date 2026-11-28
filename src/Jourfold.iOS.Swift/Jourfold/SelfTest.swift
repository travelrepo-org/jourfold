#if JOURFOLD_DEBUG_TOOLS
import Foundation
import os

/// Checks of the logic behind the screens, started with `-selftest YES` (./selftest.sh); the app quits afterwards.
enum SelfTest {
    private static let log = Logger(subsystem: "dev.jourfold.app", category: "selftest")

    static func run(_ store: TripStore) async {
        var failures = 0
        func check(_ name: String, _ ok: Bool) {
            print(ok ? "PASS \(name)" : "FAIL \(name)"); if ok { log.info("PASS \(name, privacy: .public)") } else { failures += 1; log.error("FAIL \(name, privacy: .public)") }
        }
        let trip = store.trips[0]

        // Costs as on the Costs page.
        let balances = CostsView.balances(trip)
        let expected = "198.92"
        check("one balance: Alex pays Sam \(expected) EUR", balances.count == 1 && trip.person(balances[0].from)?.name == "Alex" && balances[0].amount == Decimal(string: expected))
        // TravelRepo adds up per currency.
        let summary = CostsView.paidSummary(trip)
        check("paid summary", (summary.contains("1,749.80") || summary.contains("1.749,80")) && (summary.contains("158,800") || summary.contains("158.800")))

        // Rates: JPY has no minor unit.
        check("JPY rounding", Rates.convert(Money(value: 100, currency: "EUR"), to: "JPY") == 16390)

        // Travel notifications over the next three days.
        let notices = TravelNotices.upcoming(trip, now: store.now, before: 60)
        check("notices are in order and in the future", notices == notices.sorted { $0.at < $1.at } && notices.allSatisfy { $0.at >= store.now })
        check("Shinkansen departure an hour before", notices.contains { $0.kind == .departure && $0.title.contains("Shinkansen to Kyoto") && Fmt.time($0.at, $0.zone).contains("7") && Fmt.time($0.at, $0.zone).contains("30") })
        check("check-out the evening before", notices.contains { $0.kind == .checkOut })
        check("cancellation deadline of the ryokan", notices.contains { $0.kind == .deadline && $0.title.contains("Ryokan in Gion") })

        // Moving an idea into a day gives it the afternoon of that day.
        let onsen = trip.items.first { $0.title == "Onsen evening?" }!
        PlanMoves.move(store, trip.id, onsen.id, to: 5)
        let moved = store.trips[0].item(onsen.id)!
        check("idea moved into day 6", store.trips[0].items(on: store.trips[0].days[5]).contains { $0.id == onsen.id })
        check("moved idea becomes planned", moved.status == .planned)
        PlanMoves.makeIdea(store, trip.id, onsen.id)
        check("back to ideas", store.trips[0].ideas.contains { $0.id == onsen.id })

        // Moving a timed plan keeps its local time.
        let breakfast = trip.items.first { $0.title == "Breakfast at Tsukiji" }!
        await PlanMoves.move(store, trip.id, breakfast.id, to: 3)?.value
        let b = store.trips[0].item(breakfast.id)!
        check("same local time on the new day", Fmt.when(b.when).hasPrefix("8:30") || Fmt.when(b.when).hasPrefix("08:30"))
        check("on day 4", store.trips[0].items(on: store.trips[0].days[3]).contains { $0.id == breakfast.id })

        // The flight starts in Frankfurt's zone and ends in Tokyo's.
        let flight = trip.items.first { $0.title == "Flight to Tokyo" }!
        check("flight zones", flight.when.zone.identifier == "Europe/Berlin" && flight.when.endZone.identifier == "Asia/Tokyo")
        check("flight shows the next day", Fmt.when(flight.when).hasSuffix("+1"))

        // Today: the stay that covers the night.
        check("tonight at Hotel Gracery", trip.stay(on: trip.days[1])?.title == "Hotel Gracery Shinjuku")
        check("night of day 5 in the ryokan", trip.stay(on: trip.days[4])?.title == "Ryokan in Gion")

        // Packing counts.
        check("7 still to pack", trip.toPack == 7)

        print("SELFTEST DONE failures=\(failures)"); log.info("SELFTEST DONE failures=\(failures)")
    }
}
#endif
