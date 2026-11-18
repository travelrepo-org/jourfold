// The bridge between Jourfold's .NET code and ActivityKit, which is Swift only. IosLiveActivities calls these class
// methods through the Objective-C runtime; they take and return plain strings and numbers.
import Foundation
import UIKit
#if canImport(ActivityKit)
import ActivityKit
#endif

@objc(JourfoldLive)
public final class JourfoldLive: NSObject {
    /// The widget extension that draws the activity. A signed build without a profile for it leaves it out.
    private static let widget = "JourfoldLiveActivity.appex"
    /// 0: no Live Activities here (iOS before 16.2, an iPad, no widget); 1: the user turned them off; 2: allowed.
    @objc public static func support() -> Int {
        guard #available(iOS 16.2, *) else { return 0 }
        guard UIDevice.current.userInterfaceIdiom == .phone, hasWidget else { return 0 }
        return ActivityAuthorizationInfo().areActivitiesEnabled ? 2 : 1
    }

    /// Whether an activity is on the Lock Screen and in the Dynamic Island right now.
    @objc public static func isShowing() -> Bool {
        guard #available(iOS 16.2, *) else { return false }
        return Activity<TravelDayAttributes>.activities.contains { $0.activityState == .active || $0.activityState == .stale }
    }

    /// Shows, updates or (with nil) ends the travel day, given as JSON (TravelDayAttributes.ContentState and more).
    @objc public static func show(_ json: String?) {
        guard #available(iOS 16.2, *), hasWidget else { return }
        Live.show(json.flatMap { try? decoder.decode(Payload.self, from: Data($0.utf8)) })
    }

    private static var hasWidget: Bool {
        guard let plugins = Bundle.main.builtInPlugInsURL else { return false }
        return FileManager.default.fileExists(atPath: plugins.appendingPathComponent(widget).path)
    }

    private static let decoder: JSONDecoder = {
        let decoder = JSONDecoder()
        decoder.dateDecodingStrategy = .secondsSince1970
        return decoder
    }()

    @available(iOS 16.2, *)
    struct Payload: Decodable {
        var item: String
        /// When the activity is out of date: at the departure or arrival it counts towards.
        var stale: Date?
        var state: TravelDayAttributes.ContentState
    }

    @available(iOS 16.2, *)
    enum Live {
        typealias TravelDay = Activity<TravelDayAttributes>

        /// One activity for the journey under way: updated while it is the same journey, replaced by the next one.
        /// iOS reports an activity the user swiped away the same as one it removed itself, such as after an update of
        /// the app, so Jourfold shows the travel day again the next time it runs; Live Activities can be turned off for
        /// Jourfold in the Settings app.
        static func show(_ payload: Payload?) {
            let running = TravelDay.activities.filter { $0.activityState == .active || $0.activityState == .stale }
            guard let payload else { end(running); return }
            let content = ActivityContent(state: payload.state, staleDate: payload.stale)
            if let same = running.first(where: { $0.attributes.item == payload.item }) {
                end(running.filter { $0.id != same.id })
                Task { await same.update(content) }
            } else {
                end(running)
                // iOS starts an activity only while the app is on screen; the next time it is, it tries again.
                _ = try? TravelDay.request(attributes: TravelDayAttributes(item: payload.item), content: content, pushType: nil)
            }
        }

        private static func end(_ activities: [TravelDay]) {
            for activity in activities { Task { await activity.end(nil, dismissalPolicy: .immediate) } }
        }
    }
}
