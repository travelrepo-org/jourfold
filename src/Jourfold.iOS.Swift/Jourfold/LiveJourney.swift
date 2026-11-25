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
                                                   symbol: t.mode.symbol, seat: t.seat, platform: nil)
        do {
            let a = try Activity.request(attributes: JourneyAttributes(item: item.id.uuidString),
                                         content: .init(state: state, staleDate: arrives.addingTimeInterval(3600)))
            log.info("started \(a.id, privacy: .public)")
            end(item, except: a.id)
        } catch {
            log.error("could not start: \(String(describing: error), privacy: .public)")
        }
    }

    /// The travel day by itself, when Settings says so: from three hours before a flight and 45 minutes before other
    /// journeys. iOS starts one only while the app is on screen. Once per journey, so swiping it away keeps it away.
    static func follow(_ store: TripStore) {
        guard !DebugTools.flag("demo"), UserDefaults.standard.object(forKey: "liveActivities") as? Bool ?? true, UIApplication.shared.applicationState == .active,
              ActivityAuthorizationInfo().areActivitiesEnabled else { return }
        let key = "live.followed", now = Date()
        var followed = UserDefaults.standard.stringArray(forKey: key) ?? []
        for trip in store.trips {
            let me = store.me[trip.id]
            for item in trip.items where item.transport != nil && item.status != .cancelled && item.status != .idea {
                guard case .exact(let start, _, let end, _) = item.when, start > now.addingTimeInterval(item.transport?.mode == .flight ? -86400 : -43200) else { continue }
                if end <= now { self.end(item); continue }
                let opens = start.addingTimeInterval(item.transport?.mode == .flight ? -3 * 3600 : -45 * 60)
                guard opens <= now, me == nil || item.participants.isEmpty || item.participants.contains(me!) else { continue }
                let id = "\(item.id.uuidString)/\(Int(start.timeIntervalSince1970))"
                guard !followed.contains(id), !isRunning(item) else { continue }
                Self.start(trip, item)
                followed.append(id)
            }
        }
        UserDefaults.standard.set(Array(followed.suffix(20)), forKey: key)
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
