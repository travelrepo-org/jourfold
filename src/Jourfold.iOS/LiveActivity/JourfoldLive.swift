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
    /// The part of the travel day last shown, kept across launches, so a part the user swiped away stays away.
    private static let shownKey = "dev.jourfold.live.shown"

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

    /// Shows, updates or (with nil) ends the travel day. Returns the part the user swiped away when it is the one asked
    /// for, so Jourfold remembers it; nil otherwise.
    @objc public static func show(_ json: String?) -> String? {
        guard #available(iOS 16.2, *), hasWidget else { return nil }
        let payload = json.flatMap { try? decoder.decode(Payload.self, from: Data($0.utf8)) }
        return Live.show(payload)
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

        static func show(_ payload: Payload?) -> String? {
            let all = TravelDay.activities
            let defaults = UserDefaults.standard
            // Gone without Jourfold ending it: the user swiped it away (or iOS ended it after many hours).
            var swiped: String?
            if let shown = defaults.string(forKey: shownKey),
               !all.contains(where: { $0.content.state.key == shown && $0.activityState != .dismissed }) {
                swiped = shown
            }
            guard let payload else {
                end(all)
                defaults.removeObject(forKey: shownKey)
                return nil
            }
            if swiped == payload.state.key {
                end(all)
                defaults.removeObject(forKey: shownKey)
                return swiped
            }
            let content = ActivityContent(state: payload.state, staleDate: payload.stale)
            let running = all.filter { $0.activityState == .active || $0.activityState == .stale }
            if let same = running.first(where: { $0.attributes.item == payload.item }) {
                end(running.filter { $0.id != same.id })
                Task { await same.update(content) }
            } else {
                end(running)
                // iOS starts an activity only while the app is on screen; the next time it is, it tries again.
                do { _ = try TravelDay.request(attributes: TravelDayAttributes(item: payload.item), content: content, pushType: nil) }
                catch { defaults.removeObject(forKey: shownKey); return nil }
            }
            defaults.set(payload.state.key, forKey: shownKey)
            return nil
        }

        private static func end(_ activities: [TravelDay]) {
            for activity in activities { Task { await activity.end(nil, dismissalPolicy: .immediate) } }
        }
    }
}
