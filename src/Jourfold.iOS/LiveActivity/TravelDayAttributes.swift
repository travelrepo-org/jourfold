// The travel day as a Live Activity. This file is compiled into both the bridge (JourfoldLive.framework), which starts,
// updates and ends the activity, and the widget extension, which draws it; ActivityKit matches the two by the type's
// name. Everything the activity shows is worked out by Jourfold's shared notices (LiveNotice) and handed over as text,
// already in the user's language, so neither side has texts of its own.
import Foundation
#if canImport(ActivityKit)
import ActivityKit

@available(iOS 16.1, *)
public struct TravelDayAttributes: ActivityAttributes {
    public struct ContentState: Codable, Hashable {
        /// Which part of the travel day this is (LiveNotice.Key); the user swipes away a part, not the whole day.
        public var key: String
        public var title: String
        public var text: String
        /// The short text for the Dynamic Island: the departure or arrival time (LiveNotice.Chip).
        public var chip: String?
        /// From when the Dynamic Island counts the minutes down instead of showing the time.
        public var countdownFrom: Date?
        /// Where the progress starts; nil before the journey leaves.
        public var from: Date?
        /// The departure or arrival the activity counts towards.
        public var until: Date?
        /// The vehicle, as the app's icon name (plane, train-front, bus, ...).
        public var icon: String
        /// What a tap opens: the plan's details in Jourfold.
        public var url: String
        /// The ticket's link and its label, when the journey has a ticket.
        public var ticketUrl: String?
        public var ticketLabel: String?
        /// VoiceOver's names for the countdown and the progress.
        public var timeLeftLabel: String
        public var progressLabel: String
    }

    /// The journey; another journey is another activity.
    public var item: String

    public init(item: String) { self.item = item }
}
#endif
