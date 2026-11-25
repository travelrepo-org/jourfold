import SwiftUI

/// Tells about a newer Jourfold on GitHub, as the .NET apps do: only builds handed out there look
/// (`JourfoldDistribution=github`), at most once a day, sending nothing but the app's name and version. On unless turned
/// off. Nothing is downloaded: the notice links to the release page.
@MainActor @Observable
final class Updates {
    static let shared = Updates()
    static let available = (Bundle.main.object(forInfoDictionaryKey: "JourfoldDistribution") as? String) == "github" || DebugTools.flag("updates")
    static var current: String { Bundle.main.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String ?? "0.0.0" }
    private static let on = "updates.check", checked = "updates.checked", skipped = "updates.skip"

    nonisolated struct Release: Decodable, Equatable {
        var tag_name: String
        var name: String?
        var html_url: URL
        var draft: Bool?
        var prerelease: Bool?
        var version: String { String(tag_name.drop { $0 == "v" || $0 == "V" }) }
    }
    enum Answer: Equatable { case newer(Release), latest, unreachable }

    private(set) var offer: Release?
    private(set) var asking = false
    private(set) var answer: Answer?

    var looks: Bool {
        get { access(keyPath: \.looks); return Self.available && UserDefaults.standard.string(forKey: Self.on) != "off" }
        set { withMutation(keyPath: \.looks) { UserDefaults.standard.set(newValue ? "on" : "off", forKey: Self.on) }; if newValue { Task { await look() } } else { offer = nil } }
    }

    /// When a day has passed since GitHub last answered.
    func look() async {
        guard looks else { return }
        let d = UserDefaults.standard
        if let last = d.object(forKey: Self.checked) as? Date, last <= Date(), Date().timeIntervalSince(last) < 86400, !DebugTools.flag("updates") { return }
        if case .newer(let release) = await ask(), d.string(forKey: Self.skipped) != release.version { offer = release }
    }

    /// Because the person asked, also when looking is off.
    func lookNow() async {
        guard !asking else { return }
        answer = nil
        let found = await ask()
        answer = found
        if case .newer(let release) = found { offer = release }
    }

    func skip(_ release: Release) { UserDefaults.standard.set(release.version, forKey: Self.skipped); offer = nil }

    private func ask() async -> Answer {
        asking = true; defer { asking = false }
        if let made = DebugTools.value("updateversion") { return Self.compare(made, Self.current) > 0 ? .newer(.init(tag_name: "v" + made, name: nil, html_url: URL(string: "https://example.com/")!)) : .latest }
        var request = URLRequest(url: URL(string: "https://api.github.com/repos/travelrepo-org/jourfold/releases/latest")!, timeoutInterval: 15)
        request.setValue("Jourfold/\(Self.current)", forHTTPHeaderField: "User-Agent")
        request.setValue("application/vnd.github+json", forHTTPHeaderField: "Accept")
        guard let (data, response) = try? await URLSession(configuration: .ephemeral).data(for: request), (response as? HTTPURLResponse)?.statusCode == 200,
              let release = try? JSONDecoder().decode(Release.self, from: data) else { return .unreachable }
        UserDefaults.standard.set(Date(), forKey: Self.checked)
        guard release.draft != true, release.prerelease != true, release.html_url.scheme == "https", !release.version.contains("-"), Self.compare(release.version, Self.current) > 0 else { return .latest }
        return .newer(release)
    }

    /// Of versions with three numbers, as releases have them; something else counts as lowest.
    static func compare(_ a: String, _ b: String) -> Int {
        func numbers(_ v: String) -> [Int]? { let n = v.split(separator: ".", omittingEmptySubsequences: false).map { Int($0) }; return n.count == 3 && !n.contains(nil) ? n.compactMap { $0 } : nil }
        guard let x = numbers(a), let y = numbers(b) else { return (numbers(a) == nil ? 0 : 1) - (numbers(b) == nil ? 0 : 1) }
        for i in 0..<3 where x[i] != y[i] { return x[i] < y[i] ? -1 : 1 }
        return 0
    }
}

/// Above the trips, when there is a newer release.
struct UpdateNotice: View {
    @Environment(\.openURL) private var openURL
    private let updates = Updates.shared
    var body: some View {
        if let release = updates.offer {
            Section {
                VStack(alignment: .leading, spacing: 4) {
                    Text("\(release.name ?? "Jourfold " + release.version) is available").font(.jf(.headline))
                    Text("Installed: \(Updates.current)").font(.jf(.footnote)).foregroundStyle(Color.jfMuted)
                }
                Button { openURL(release.html_url) } label: { Label("View release", systemImage: "arrow.up.forward.square") }
                Button("Skip this version") { withAnimation { updates.skip(release) } }
                Button("Stop checking for updates") { withAnimation { updates.looks = false } }
            } footer: { Footnote("You can turn this on and off in Settings.") }
        }
    }
}

struct UpdateSettings: View {
    private let updates = Updates.shared
    var body: some View {
        if Updates.available {
            Section {
                Toggle("Check for new versions", isOn: Binding(get: { updates.looks }, set: { updates.looks = $0 }))
                Button { Task { await updates.lookNow() } } label: { Text(updates.asking ? LocalizedStringKey("Checking…") : "Check now") }.disabled(updates.asking)
                switch updates.answer {
                case .newer(let release): Link(destination: release.html_url) { Text("\(release.name ?? "Jourfold " + release.version) is available") }
                case .latest: Text("You have the latest version (\(Updates.current)).").foregroundStyle(Color.jfMuted)
                case .unreachable: Text("GitHub could not be reached. Try again later.").foregroundStyle(Color.jfMuted)
                case nil: EmptyView()
                }
            } header: { Overline("Updates") } footer: { Footnote("Asks GitHub once a day for a newer version. Only the app version is sent.") }
        }
    }
}
