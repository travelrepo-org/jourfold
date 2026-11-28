import AppIntents
import WidgetKit

/// A trip to choose in a widget's settings, as the app left the trips' names in the shared store.
struct WidgetTrip: AppEntity {
    static let typeDisplayRepresentation = TypeDisplayRepresentation(name: "Trip")
    static let defaultQuery = WidgetTripQuery()
    /// The trip's id as text, or "all".
    let id: String
    let title: String
    static var everything: WidgetTrip { WidgetTrip(id: "all", title: String(localized: "All trips")) }
    var displayRepresentation: DisplayRepresentation { DisplayRepresentation(title: "\(title)") }
    var uuid: UUID? { UUID(uuidString: id) }
}

struct WidgetTripQuery: EntityQuery {
    private var all: [WidgetTrip] { [WidgetTrip.everything] + SharedInbox.trips().compactMap { t in t.id.map { WidgetTrip(id: $0.uuidString, title: t.title) } } }
    // A trip that is gone keeps its place; the widget then shows nothing planned instead of another trip.
    func entities(for identifiers: [String]) async throws -> [WidgetTrip] { identifiers.map { id in all.first { $0.id == id } ?? WidgetTrip(id: id, title: "–") } }
    func suggestedEntities() async throws -> [WidgetTrip] { all }
    func defaultResult() async -> WidgetTrip? { WidgetTrip.everything }
}

enum WidgetMode: String, AppEnum {
    case next, today
    static let typeDisplayRepresentation = TypeDisplayRepresentation(name: "Shows")
    static let caseDisplayRepresentations: [WidgetMode: DisplayRepresentation] = [.next: "The next plans", .today: "Only today"]
}

struct NextPlansIntent: WidgetConfigurationIntent {
    static let title: LocalizedStringResource = "Next plans"
    static let description = IntentDescription("Which plans the widget shows.")
    @Parameter(title: "Shows", default: .next) var mode: WidgetMode
    @Parameter(title: "Trip") var trip: WidgetTrip?
}

struct CostsIntent: WidgetConfigurationIntent {
    static let title: LocalizedStringResource = "Costs and budgets"
    static let description = IntentDescription("Which trip the widget shows.")
    /// No trip means the one that is on now, or comes next.
    @Parameter(title: "Trip") var trip: WidgetTrip?
}
