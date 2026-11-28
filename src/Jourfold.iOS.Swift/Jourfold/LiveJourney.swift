import ActivityKit
import Foundation
import UIKit
import os

private let log = Logger(subsystem: "dev.jourfold.app", category: "live")

/// Starts and ends the journey's Live Activity. The widget extension draws it from `JourneyAttributes`.
enum LiveJourney {
    static func start(_ trip: Trip, _ item: PlanItem, demo: Bool = false) {
        TestReport.note("live activity started, allowed \(ActivityAuthorizationInfo().areActivitiesEnabled)")
        log.info("live activities enabled: \(ActivityAuthorizationInfo().areActivitiesEnabled)")
        guard let t = item.transport,
              case let from = trip.place(t.from)?.name ?? "", case let to = trip.place(t.to)?.name ?? item.title,
              var departs = item.when.start, var arrives = item.when.end else { return }
        if demo {
            // The demo's clock is fixed in January; for the demo the journey leaves in 25 minutes from now.
            let length = arrives.timeIntervalSince(departs)
            departs = Date.now.addingTimeInterval(25 * 60)
            arrives = departs.addingTimeInterval(length)
        }
        let state = JourneyAttributes.ContentState(title: item.title, from: from, to: to, departs: departs, arrives: arrives,
                                                   symbol: t.mode.symbol, seat: t.seat, platform: nil,
                                                   trip: trip.id.uuidString, hasTicket: Tickets.of(trip.id, item.id) != nil)
        do {
            let a = try Activity.request(attributes: JourneyAttributes(item: item.id.uuidString),
                                         content: .init(state: state, staleDate: Self.staleDate(departs, arrives)))
            log.info("started \(a.id, privacy: .public)")
            end(item, except: a.id)
        } catch {
            log.error("could not start: \(String(describing: error), privacy: .public)")
        }
    }

    /// The travel day by itself, when Settings says so: from three hours before a flight and 45 minutes before other
    /// journeys until the arrival. iOS starts one only while the app is on screen. As in the .NET app it comes back each
    /// time the app is opened while the journey is on: iOS does not tell an activity the person swiped away from one it
    /// ended itself, such as after an update of the app.
    static func follow(_ store: TripStore) {
        guard !DebugTools.flag("demo"), UserDefaults.standard.object(forKey: "liveActivities") as? Bool ?? true, UIApplication.shared.applicationState == .active,
              ActivityAuthorizationInfo().areActivitiesEnabled else { return }
        let now = Date()
        for trip in store.trips {
            let me = store.me[trip.id]
            for item in trip.items where item.transport != nil && item.status != .cancelled && item.status != .idea {
                guard case .exact(let start, _, let end, _) = item.when, start > now.addingTimeInterval(-86400) else { continue }
                if end <= now { self.end(item); continue }
                let opens = start.addingTimeInterval(item.transport?.mode == .flight ? -3 * 3600 : -45 * 60)
                guard opens <= now, me == nil || item.participants.isEmpty || item.participants.contains(me!), !isRunning(item) else { continue }
                Self.start(trip, item)
            }
        }
    }

    /// Up to the departure, the departure: then iOS draws it again and it counts to the arrival.
    static func staleDate(_ departs: Date, _ arrives: Date) -> Date { departs > .now ? departs : arrives.addingTimeInterval(3600) }

    /// Once under way, so it stays fresh until the arrival; while the app runs.
    static func moveOn() {
        Task {
            for activity in Activity<JourneyAttributes>.activities where activity.content.state.departs <= .now && (activity.content.staleDate ?? .distantFuture) <= activity.content.state.departs {
                let s = activity.content.state
                await activity.update(ActivityContent(state: s, staleDate: s.arrives.addingTimeInterval(3600)))
            }
        }
    }

    static func isRunning(_ item: PlanItem) -> Bool {
        Activity<JourneyAttributes>.activities.contains { $0.attributes.item == item.id.uuidString }
    }

    /// Ends the item's activities, all but `except` (the one just started).
    static func end(_ item: PlanItem, except: String? = nil) {
        let id = item.id.uuidString
        Task {
            for activity in Activity<JourneyAttributes>.activities where activity.attributes.item == id && activity.id != except {
                await activity.end(nil, dismissalPolicy: .immediate)
            }
        }
    }
}
