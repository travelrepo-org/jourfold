import SwiftUI
import TravelRepoKit

/// Trip repositories on GitHub that the signed-in account may use (the GitHub App's installations). Opening clones the
/// trip into a folder of its own on this phone, with its whole history, and then asks who the person is in it.
struct OpenFromGitHubSheet: View {
    @Environment(\.dismiss) private var dismiss
    @Environment(Account.self) private var account
    @Environment(TripStore.self) private var store
    @Environment(AppNavigation.self) private var nav
    @Environment(\.openURL) private var openURL
    @State private var repositories: [GitHub.Repository]?
    @State private var access: URL?
    @State private var opening: String?
    @State private var progress: Double?
    @State private var problem: String?

    var body: some View {
        NavigationStack {
            JFList {
                if account.login == nil {
                    AccountSection()
                } else {
                    Section {
                        if repositories == nil, problem == nil { HStack { ProgressView(); Text("Looking for trips…").foregroundStyle(Color.jfMuted) } }
                        ForEach(repositories ?? [], id: \.fullName) { r in
                            let here = GitHubTrips.localTrip(r, in: store) != nil
                            Button { Task { await open(r) } } label: {
                                HStack(spacing: 12) {
                                    Image(systemName: "folder.badge.person.crop").foregroundStyle(Color.jfAccent).frame(width: 28).accessibilityHidden(true)
                                    VStack(alignment: .leading, spacing: 2) {
                                        Text(GitHubTrips.title(r)).font(.jf(.body, .semibold)).foregroundStyle(Color.jfText)
                                        Text(r.fullName).font(.jf(.footnote)).foregroundStyle(Color.jfMuted)
                                        if opening == r.fullName {
                                            if let progress { ProgressView(value: progress).tint(Color.jfAccent) } else { ProgressView().tint(Color.jfAccent) }
                                        }
                                    }
                                    Spacer()
                                    if here { Text("On this phone").font(.jf(.caption, .semibold)).foregroundStyle(Color.jfMuted) }
                                }
                                .contentShape(Rectangle())
                            }
                            .buttonStyle(.plain)
                            .disabled(opening != nil)
                        }
                    } header: { Overline("Trips on GitHub") } footer: {
                        VStack(alignment: .leading, spacing: 8) {
                            if let repositories, repositories.isEmpty { Text("No trip repositories yet. Choose on GitHub which repositories Jourfold may use.") }
                            if let problem { Text(problem).foregroundStyle(Color.jfDanger) }
                            Text("Opening copies the trip to this phone with its history. It stays in sync with GitHub.")
                        }
                    }
                    if let access {
                        Section {
                            Button { openURL(access) } label: { Label("Choose repositories on GitHub", systemImage: "arrow.up.right.square") }
                        } footer: { Footnote("Jourfold only sees the repositories you allow. After choosing, come back here.") }
                    }
                }
            }
            .listStyle(.insetGrouped)
            .jfBackground()
            .navigationTitle("Open from GitHub")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar { ToolbarItem(placement: .topBarLeading) { Button(role: .close) { dismiss() } } }
            .refreshable { await load() }
        }
        .presentationDetents([.medium, .large])
        .task(id: account.login) { await load() }
    }

    private func load() async {
        guard account.login != nil else { return }
        problem = nil
        do {
            repositories = try await GitHubTrips.repositories(demo: account.isDemo)
            access = account.isDemo ? nil : try await GitHubTrips.accessURL()
        } catch { problem = TravelRepoBridge.message(error); repositories = [] }
    }

    private func open(_ r: GitHub.Repository) async {
        if let id = GitHubTrips.localTrip(r, in: store) { dismiss(); nav.tab = .today; nav.openTrip = id; return }
        opening = r.fullName; progress = nil; problem = nil
        do {
            let id = try await GitHubTrips.clone(r, store: store, account: account) { fraction in progress = fraction }
            opening = nil
            dismiss()
            nav.tab = .today; nav.openTrip = id
            nav.welcome = id
        } catch { problem = TravelRepoBridge.message(error); opening = nil }
    }
}

/// GitHub through TravelRepoKit: the trip repositories, cloning, sharing a trip and inviting people.
@MainActor
enum GitHubTrips {
    /// The made-up repositories of the demo, which clone from trips in the app's own folder.
    private static let demo = ["alex-travels/autumn-in-montreal", "sam-travels/weekend-in-lyon"]

    static func repositories(demo isDemo: Bool) async throws -> [GitHub.Repository] {
        if isDemo { return try demo.map { try JSONDecoder().decode(GitHub.Repository.self, from: Data(#"{"full_name":"\#($0)","clone_url":"demo:\#($0)","web_url":"https://github.com/\#($0)","private":true}"#.utf8)) } }
        return try await GitHub.tripRepositories()
    }

    /// Where to let Jourfold see more repositories, when it does not see any yet.
    static func accessURL() async throws -> URL? {
        let access = try await TravelRepo.call("github_repository_access", as: JSONValue.self)
        return access["has_access"]?.bool == true ? nil : access["install_url"]?.string.flatMap(URL.init(string:))
    }

    static func title(_ r: GitHub.Repository) -> String {
        let name = r.fullName.split(separator: "/").last.map(String.init) ?? r.fullName
        return name.replacingOccurrences(of: "-", with: " ").capitalized(with: .current)
    }

    /// The trip on this phone that came from the repository, if it is here already.
    static func localTrip(_ r: GitHub.Repository, in store: TripStore) -> UUID? {
        let demo = r.fullName.replacingOccurrences(of: "/", with: "--")
        return TravelRepoBridge.all.first { _, bridge in
            bridge.remotes.contains { $0.url == r.cloneUrl || $0.url.hasSuffix(r.fullName + ".git") || $0.url.hasSuffix(r.fullName) || (r.cloneUrl.hasPrefix("demo:") && $0.url.hasSuffix(demo)) }
        }?.key
    }

    /// Clones the repository into a new trip folder, sets the Git author and opens it. `progress` gets a fraction
    /// from TravelRepo's clone events.
    static func clone(_ r: GitHub.Repository, store: TripStore, account: Account, progress: @escaping @MainActor (Double) -> Void) async throws -> UUID {
        try await TripLibrary.prepare()
        let target = TripLibrary.newFolder(for: title(r))
        var url = r.cloneUrl
        if url.hasPrefix("demo:") { url = try await demoSource(String(url.dropFirst(5))).path }
        TravelRepo.setEventHandler { event in
            guard event["type"]?.string == "clone_progress" else { return }
            let total = (event["total_objects"]?.number ?? 0) + (event["checkout_total"]?.number ?? 0)
            let done = (event["indexed_objects"]?.number ?? 0) + (event["checkout_completed"]?.number ?? 0)
            if total > 0 { Task { @MainActor in progress(min(1, done / total)) } }
        }
        defer { TravelRepo.setEventHandler(nil) }
        do {
            try await TravelRepo.call("clone", ["url": .string(url), "destination": .string(target.path)], as: JSONValue.self)
        } catch { try? FileManager.default.removeItem(at: target); throw error }
        let id = try await TravelRepoBridge.open(target, store: store)
        if let bridge = TravelRepoBridge.all[id] {
            let author = TripLibrary.author(account)
            try? await bridge.session.setIdentity(name: author.name, email: author.email)
        }
        store.sortTrips()
        return id
    }

    /// For the demo: a made-up trip in a folder of the app, standing in for the repository on GitHub.
    private static func demoSource(_ fullName: String) async throws -> URL {
        let folder = TripLibrary.support.appendingPathComponent("DemoGitHub/" + fullName.replacingOccurrences(of: "/", with: "--"))
        if FileManager.default.fileExists(atPath: folder.appendingPathComponent("travel.yaml").path) { return folder }
        try FileManager.default.createDirectory(at: folder.deletingLastPathComponent(), withIntermediateDirectories: true)
        var trip = fullName.hasSuffix("montreal") ? SampleData.autumnInMontreal() : SampleData.weekendInParis()
        if fullName.hasSuffix("lyon") { trip.title = "Weekend in Lyon" }
        try await TravelRepoBridge.seed(trip, into: folder, author: ("Sam", "sam@example.invalid"), message: "Start the trip")
        return folder
    }

    /// Shares the trip on GitHub: a new private repository, the trip sent there, and invitations for the logins
    /// given. Returns the repository's web address.
    static func share(_ tripID: UUID, name: String, invite logins: [String], store: TripStore) async throws -> String {
        guard let bridge = TravelRepoBridge.all[tripID] else { throw TravelRepoBridge.AppError.message(String(localized: "The trip is not open.")) }
        // Only versions are sent, so what is open becomes one first.
        guard await bridge.saveChanges() else { throw TravelRepoBridge.AppError.message(bridge.versionProblem ?? String(localized: "The trip could not be saved as a version.")) }
        let repository = try await GitHub.createRepository(named: name)
        try await bridge.session.call("add_remote", ["name": "origin", "url": .string(repository.cloneUrl)])
        _ = try await bridge.session.sync(remote: "origin")
        for login in logins where !login.isEmpty { try await GitHub.invite(login, to: repository.fullName) }
        try await bridge.read()
        return repository.webUrl
    }
}

/// What changed between a version and now, from TravelRepo's restore preview, and going back to it. Going back is
/// itself a new change on top, so nothing is lost.
struct CompareView: View {
    let tripID: UUID
    let version: Version
    @Environment(TripStore.self) private var store
    @Environment(\.dismiss) private var dismiss
    @State private var preview: RestorePreview?
    @State private var confirm = false
    @State private var problem: String?

    var body: some View {
        JFList {
            Section {
                VStack(alignment: .leading, spacing: 4) {
                    Text(version.title).font(.jf(.headline))
                    Text("\(version.author) · \(version.date.formatted(date: .abbreviated, time: .shortened))").font(.jf(.footnote)).foregroundStyle(Color.jfMuted)
                }
            }
            Section {
                if let preview {
                    if preview.changes.isEmpty { Text("Nothing changed since this version.").foregroundStyle(Color.jfMuted) }
                    ForEach(Array(preview.changes.enumerated()), id: \.offset) { _, change in
                        Label { Text(describe(change)) } icon: { Image(systemName: icon(change.kind)).foregroundStyle(Color.jfAccent) }
                    }
                } else if problem == nil { ProgressView() }
                if let problem { Text(problem).foregroundStyle(Color.jfDanger) }
            } header: { Overline("To go back, these change") }
            if let preview, !preview.changes.isEmpty {
                Section {
                    Button { confirm = true } label: { Label("Go back to this version", systemImage: "clock.arrow.circlepath") }
                } footer: { Footnote("Going back keeps today's state as a version, so nothing is lost.") }
            }
        }
        .listStyle(.insetGrouped)
        .jfBackground()
        .navigationTitle("Compare")
        .navigationBarTitleDisplayMode(.inline)
        .task { await load() }
        .confirmationDialog(Text("Go back to “\(version.title)”?"), isPresented: $confirm, titleVisibility: .visible) {
            Button("Go back") { Task { await restore() } }
        } message: { Text("The trip looks as it did then. Today's state stays in the history.") }
    }

    private func load() async {
        guard let bridge = TravelRepoBridge.all[tripID], let commit = version.commit else { problem = String(localized: "This version cannot be compared."); return }
        await bridge.flush()
        do { preview = try await bridge.session.restorePreview(commit: commit) } catch { problem = TravelRepoBridge.message(error) }
    }

    private func restore() async {
        guard let bridge = TravelRepoBridge.all[tripID], let commit = version.commit, let preview else { return }
        do {
            _ = try await bridge.session.restoreApply(commit: commit, plan: preview.plan)
            try await bridge.session.call("create_version", ["message": .string(String(localized: "Back to “\(version.title)”"))])
            try await bridge.read()
            dismiss()
        } catch { problem = TravelRepoBridge.message(error) }
    }

    /// The title of anything else in the trip, such as a packing item or a document.
    private func title(of id: UUID) -> String? {
        let d = TravelRepoBridge.all[tripID]?.entities[TravelRepoBridge.key(id)]
        return d?["title"]?.string ?? d?["name"]?.string ?? d?["display_name"]?.string
    }

    /// A file path of the trip as words: the entity it holds, by its title in the trip.
    private func describe(_ change: RestorePreview.Change) -> String {
        let trip = store.trips.first { $0.id == tripID }
        let id = change.path.split(separator: "/").last.map { String($0.split(separator: ".").first ?? "") }.flatMap(UUID.init(uuidString:))
        var name = change.path == "travel.yaml" ? String(localized: "Trip details") : change.path
        if let id, let known = trip?.item(id)?.title ?? title(of: id) { name = known }
        switch change.kind {
        case "added": return String(localized: "\(name) comes back")
        case "removed": return String(localized: "\(name) goes")
        default: return String(localized: "\(name) changes back")
        }
    }

    private func icon(_ kind: String) -> String { kind == "added" ? "plus.circle" : kind == "removed" ? "minus.circle" : "pencil.line" }
}
