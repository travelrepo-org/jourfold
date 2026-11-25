// The journey as a Live Activity. This file belongs to the app and the widget extension; ActivityKit matches the two
// by the type's name. In a SwiftUI app this is one shared file in one project, not a bridge between .NET and Swift.
import Foundation
import ActivityKit

nonisolated struct JourneyAttributes: ActivityAttributes {
    nonisolated struct ContentState: Codable, Hashable {
        var title: String
        var from: String
        var to: String
        var departs: Date
        var arrives: Date
        var symbol: String
        var seat: String?
        var platform: String?
    }

    /// The plan's identifier; another journey is another activity.
    var item: String
}
