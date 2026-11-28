import BackgroundTasks
import SwiftUI
import UIKit

// What reaches the app from outside, or goes there: files opened in Jourfold, maps apps, quick actions on the app's icon,
// and the time iOS gives it in the background.

/// Presents over whatever is open, such as a sheet, where SwiftUI's own presentations of the root would have to wait.
@MainActor
enum TopPresenter {
    static var top: UIViewController? {
        let scene = UIApplication.shared.connectedScenes.compactMap { $0 as? UIWindowScene }.first { $0.activationState == .foregroundActive } ?? UIApplication.shared.connectedScenes.compactMap { $0 as? UIWindowScene }.first
        var top = scene?.keyWindow?.rootViewController
        while let next = top?.presentedViewController, !next.isBeingDismissed { top = next }
        return top
    }

    static func present<V: View>(_ view: (@escaping () -> Void) -> V) {
        guard let top else { return }
        weak var shown: UIViewController?
        let host = UIHostingController(rootView: view { shown?.dismiss(animated: true) })
        shown = host
        top.present(host, animated: true)
    }
}

/// Full brightness while something is shown to be read or scanned, such as a ticket or the taxi card; then as it was.
struct BrightScreen: ViewModifier {
    var on = true
    @State private var before: CGFloat?
    func body(content: Content) -> some View {
        content
            .onChange(of: on, initial: true) { _, on in if on, before == nil, let screen { before = screen.brightness; screen.brightness = 1 } }
            .onDisappear { if let before { screen?.brightness = before } ; before = nil }
    }
    private var screen: UIScreen? { UIApplication.shared.connectedScenes.compactMap { ($0 as? UIWindowScene)?.screen }.first }
}

/// The maps apps a place can be handed to: Apple Maps and those that are installed (their schemes are in AppInfo.plist).
enum Maps {
    struct App { let name: String; let link: URL }

    static func others(_ place: Place) -> [App] {
        let at = place.hasPosition ? String(format: "%.6f,%.6f", locale: Locale(identifier: "en_US_POSIX"), place.latitude, place.longitude) : nil
        let query = place.address.isEmpty ? place.name : place.address
        func link(_ text: String, _ items: [(String, String)]) -> URL? {
            var c = URLComponents(string: text); c?.queryItems = items.map { URLQueryItem(name: $0.0, value: $0.1) }; return c?.url
        }
        return [("Google Maps", link("comgooglemaps://", [("q", at ?? query)])), ("Waze", link("waze://", [at.map { ("ll", $0) } ?? ("q", query), ("navigate", "yes")]))]
            .compactMap { name, url in url.map { App(name: name, link: $0) } }
    }

    @MainActor
    static func navigate(to place: Place) {
        let installed = others(place).filter { UIApplication.shared.canOpenURL($0.link) }
        guard !installed.isEmpty, let top = TopPresenter.top else { apple(place); return }
        let alert = UIAlertController(title: String(localized: "Navigate with"), message: place.name, preferredStyle: .alert)
        alert.addAction(UIAlertAction(title: String(localized: "Apple Maps"), style: .default) { _ in apple(place) })
        for app in installed { alert.addAction(UIAlertAction(title: app.name, style: .default) { _ in UIApplication.shared.open(app.link) }) }
        alert.addAction(UIAlertAction(title: String(localized: "Cancel"), style: .cancel))
        top.present(alert, animated: true)
    }
}

/// A file opened in Jourfold from another app: the person says which trip, plan or booking it belongs to, as in the share
/// extension, and it goes the same way into the trip.
@MainActor
enum OpenedFile {
    static let taken = Notification.Name("jourfold.shared")
    private static var waiting: [URL] = []

    static func open(_ url: URL, _ store: TripStore) {
        guard !store.loading else { waiting.append(url); return }
        let scoped = url.startAccessingSecurityScopedResource()
        defer { if scoped { url.stopAccessingSecurityScopedResource() } }
        guard !store.trips.isEmpty else { store.problem = String(localized: "Add a trip first, then open the file again."); return }
        guard let data = try? Data(contentsOf: url) else { store.problem = String(localized: "The file could not be read."); return }
        // iOS hands over a copy in the app's inbox, which nobody else removes.
        if url.path.contains("/Inbox/") { try? FileManager.default.removeItem(at: url) }
        let id = UUID(), name = url.lastPathComponent
        guard SharedInbox.putFile(data, name: name, entry: id) else { store.problem = String(localized: "“\(name)” is too large to share this way. Add it in Jourfold under Files."); return }
        TopPresenter.present { close in
            ShareForm(text: name, url: nil, files: [name], trips: store.trips.map(\.shareSummary), inApp: true) { entry in
                if var entry { entry.id = id; SharedInbox.put(entry); NotificationCenter.default.post(name: taken, object: nil) } else { SharedInbox.clean(id) }
                close()
            }
            .interactiveDismissDisabled()
        }
    }

    static func openWaiting(_ store: TripStore) { let all = waiting; waiting = []; all.forEach { open($0, store) } }
}

/// Tickets of the next journeys as quick actions on the app's icon, and "Show ticket" on a notification.
@MainActor
enum Tickets {
    static let action = "ticket"
    private static let type = "ticket"

    static func of(_ trip: UUID, _ item: UUID) -> TripDocument? {
        guard let bridge = TravelRepoBridge.all[trip] else { return nil }
        let key = TravelRepoBridge.key(item), booking = bridge.store.trips.first { $0.id == trip }?.item(item)?.booking.map(TravelRepoBridge.key)
        let ids = bridge.documentIDs(of: key) + (booking.map { bridge.documentIDs(of: $0) } ?? [])
        return bridge.documents().filter { d in ids.contains(d.id) && (d.mediaType == "application/pdf" || d.mediaType == "application/vnd.apple.pkpass" || d.mediaType.hasPrefix("image/") || (DebugTools.flag("ticketsahead") && d.mediaType == "message/rfc822")) }.first
    }

    static func show(_ trip: UUID, _ item: UUID) {
        guard let ticket = of(trip, item) else { return }
        TopPresenter.present { _ in DocumentView(tripID: trip, document: ticket.id, name: ticket.name).tint(.jfAccent).font(.jf(.body)) }
    }

    /// Journeys from an hour ago to the next day that have a ticket; three at most.
    static func publish(_ store: TripStore) {
        // The sample has no journey with a ticket on the demo's next day, so a test looks further.
        let now = store.now, ahead: TimeInterval = DebugTools.flag("ticketsahead") ? 86400 * 5 : 86400
        var journeys: [(trip: Trip, item: PlanItem)] = []
        for trip in store.trips {
            for item in trip.items where item.transport != nil && item.status != .cancelled {
                guard let start = item.when.start, let end = item.when.end, start < now.addingTimeInterval(ahead), end > now.addingTimeInterval(-3600) else { continue }
                if let me = store.me[trip.id], !item.participants.isEmpty, !item.participants.contains(me) { continue }
                if of(trip.id, item.id) != nil { journeys.append((trip, item)) }
            }
        }
        journeys.sort { ($0.item.when.start ?? .distantFuture) < ($1.item.when.start ?? .distantFuture) }
        UIApplication.shared.shortcutItems = journeys.prefix(3).map { trip, i in
            UIApplicationShortcutItem(type: type, localizedTitle: String(localized: "Ticket: \(i.title)"), localizedSubtitle: nil, icon: UIApplicationShortcutIcon(systemImageName: "ticket"),
                                      userInfo: ["trip": trip.id.uuidString as NSString, "item": i.id.uuidString as NSString])
        }
    }

    static func perform(_ item: UIApplicationShortcutItem) -> Bool {
        guard item.type == type, let trip = (item.userInfo?["trip"] as? String).flatMap(UUID.init(uuidString:)), let plan = (item.userInfo?["item"] as? String).flatMap(UUID.init(uuidString:)) else { return false }
        NoticeCenter.shared.open(trip, plan, ticket: true)
        return true
    }
}

/// Quick actions come to the scene, which SwiftUI has no word for.
final class AppDelegate: NSObject, UIApplicationDelegate {
    func application(_ application: UIApplication, configurationForConnecting session: UISceneSession, options: UIScene.ConnectionOptions) -> UISceneConfiguration {
        if let item = options.shortcutItem { _ = Tickets.perform(item) }
        let configuration = UISceneConfiguration(name: nil, sessionRole: session.role)
        configuration.delegateClass = SceneDelegate.self
        return configuration
    }
}

final class SceneDelegate: NSObject, UIWindowSceneDelegate {
    func windowScene(_ windowScene: UIWindowScene, performActionFor shortcutItem: UIApplicationShortcutItem, completionHandler: @escaping (Bool) -> Void) {
        completionHandler(Tickets.perform(shortcutItem))
    }
}

/// iOS shows notices by itself at their time; now and then it lets the app work them out again in the background, such
/// as for a trip that comes closer than the days planned ahead.
@MainActor
enum BackgroundNotices {
    static let id = (Bundle.main.bundleIdentifier ?? "dev.jourfold.app") + ".notices"

    static func ask() {
        guard UserDefaults.standard.object(forKey: "notices") as? Bool ?? true else { BGTaskScheduler.shared.cancel(taskRequestWithIdentifier: id); return }
        let request = BGAppRefreshTaskRequest(identifier: id)
        request.earliestBeginDate = Date().addingTimeInterval(12 * 3600)
        // The simulator refuses it; the notices that are planned come either way.
        try? BGTaskScheduler.shared.submit(request)
    }

    static func run(_ store: TripStore) async {
        ask()
        if store.trips.isEmpty { await TripLibrary.load(store) }
        await NoticeCenter.shared.planNow(store)
    }
}
