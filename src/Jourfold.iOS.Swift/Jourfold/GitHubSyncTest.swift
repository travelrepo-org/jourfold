#if JOURFOLD_DEBUG_TOOLS
import Foundation
import os
import TravelRepoKit

/// `-github-client <id> -githubsynctest YES` after a real GitHub sign-in: syncs the made-up trip
/// "Spring in Japan" through a private repository on GitHub between two copies on this device, Alex's and Sam's, as
/// two phones would. Covers the first upload, cloning, changes in both directions, a conflict answered with a merge,
/// the fast-forward pull and a variant that is not open. Only synthetic data; the repository is named by
/// `-githubsynctest-repo` (default jourfold-sync-test) and created if it does not exist.
enum GitHubSyncTest {
    private static let log = Logger(subsystem: "dev.jourfold.app", category: "synctest")

    @MainActor
    static func run() async {
        var failures = 0
        func check(_ name: String, _ ok: Bool, _ detail: String = "") {
            print(ok ? "PASS sync: \(name)" : "FAIL sync: \(name) \(detail)"); if ok { log.info("PASS sync: \(name, privacy: .public)") } else { failures += 1; log.error("FAIL sync: \(name, privacy: .public) \(detail, privacy: .public)") }
        }
        do {
            guard let clientID = GitHubSignIn.clientID, let token = GitHubSignIn.Keychain.token else { check("signed in", false, "no client id or token"); return }
            try await GitHub.configure(clientId: clientID, appSlug: DebugTools.value("github-app"))
            try await GitHub.setToken(token)
            guard let login = try await GitHub.account()?.login else { check("signed in", false); return }
            check("signed in as the account", true)
            if let git2 = Bundle.main.privateFrameworksPath.map({ $0 + "/git2.framework/git2" }) { try await TravelRepo.configure(libgit2: git2) }

            // The repository, created through the GitHub App as Jourfold would, or the one from an earlier run.
            let name = DebugTools.value("githubsynctest-repo") ?? "jourfold-sync-test"
            let url: String
            var created = false
            do { url = try await GitHub.createRepository(named: name).cloneUrl; created = true; check("private repository created", true) }
            catch {
                log.error("create failed: \(String(describing: error), privacy: .public)")
                url = try await TravelRepo.call("github_inspect_repository", ["full_name": .string(login + "/" + name)], as: GitHub.Repository.self).cloneUrl
                check("repository exists from an earlier run", true, String(describing: error))
            }

            let support = try FileManager.default.url(for: .applicationSupportDirectory, in: .userDomainMask, appropriateFor: nil, create: true)
            let root = support.appendingPathComponent("SyncTest")
            try? FileManager.default.removeItem(at: root)
            let alexRoot = root.appendingPathComponent("alex"), samRoot = root.appendingPathComponent("sam")

            // Alex makes the trip and uploads it, or, when the repository is left from an earlier run, clones it.
            let alex: TravelRepoSession
            let temple: String
            if created {
                try await TravelRepoSession.createTrip(root: alexRoot, title: "Spring in Japan", timezone: "Asia/Tokyo", gitName: "Alex", gitEmail: "alex@example.invalid")
                alex = try await TravelRepoSession.open(root: alexRoot, recoveryRoot: root.appendingPathComponent("recovery-alex"))
                temple = try await alex.call("create_entity", ["type": "place", "data": ["title": "Kiyomizu-dera"]])["id"]!.string!
                try await alex.call("create_entity", ["type": "schedule_item", "data": ["title": "Shinkansen to Kyoto",
                    "time": ["precision": "exact", "start": ["local": "2027-05-15T08:10:00", "timezone": "Asia/Tokyo"], "end": ["local": "2027-05-15T10:25:00", "timezone": "Asia/Tokyo"]]]])
                try await alex.call("create_version", ["message": "Plan Kyoto"])
                try await alex.call("add_remote", ["name": "origin", "url": .string(url)])
            } else {
                try await TravelRepo.call("clone", ["url": .string(url), "destination": .string(alexRoot.path)], as: JSONValue.self)
                alex = try await TravelRepoSession.open(root: alexRoot, recoveryRoot: root.appendingPathComponent("recovery-alex"))
                try await alex.setIdentity(name: "Alex", email: "alex@example.invalid")
                let places = try await alex.snapshot().entities.map(\.data).filter { $0["type"]?.string == "place" && ($0["title"]?.string ?? "").hasPrefix("Kiyomizu") }
                guard let id = places.first?["id"]?.string else { check("place from the earlier run", false); return }
                temple = id
                try await alex.call("update_entity", ["id": .string(temple), "changes": ["title": "Kiyomizu-dera"]])
                try await alex.call("create_version", ["message": "Start again"])
            }
            let first = try await alex.sync()
            let branch = try await alex.syncStatus().branch
            check(created ? "first upload" : "upload to the existing repository", first == "synced", first)

            // Sam's phone clones it.
            try await TravelRepo.call("clone", ["url": .string(url), "destination": .string(samRoot.path)], as: JSONValue.self)
            let sam = try await TravelRepoSession.open(root: samRoot, recoveryRoot: root.appendingPathComponent("recovery-sam"))
            try await sam.setIdentity(name: "Sam", email: "sam@example.invalid")
            func title(_ session: TravelRepoSession) async throws -> String? { try await session.call("get_entity", ["id": .string(temple)])["entity"]?["title"]?.string }
            let samTitle = try await title(sam)
            check("clone has the trip", samTitle == "Kiyomizu-dera", samTitle ?? "nil")

            func rename(_ session: TravelRepoSession, _ title: String, _ message: String) async throws {
                try await session.call("update_entity", ["id": .string(temple), "changes": ["title": .string(title)]])
                try await session.call("create_version", ["message": .string(message)])
            }

            // A change in each direction.
            try await rename(alex, "Kiyomizu-dera at dawn", "Go early")
            check("Alex sends a change", try await alex.sync() == "synced")
            let samPull = try await sam.sync()
            let samSees = try await title(sam)
            check("Sam receives it", samPull == "synced" && samSees == "Kiyomizu-dera at dawn", "\(samPull) \(samSees ?? "nil")")
            try await rename(sam, "Kiyomizu-dera at dawn, then Gion", "Add Gion")
            check("Sam sends a change", try await sam.sync() == "synced")
            let background = try await alex.pull()
            let alexSees = try await title(alex)
            check("Alex pulls it in the background", background == "synced" && alexSees == "Kiyomizu-dera at dawn, then Gion", "\(background) \(alexSees ?? "nil")")

            // Both change the same field: the second sync diverges and is answered with a merge.
            try await rename(alex, "Kiyomizu-dera at sunset", "Go late")
            try await rename(sam, "Kiyomizu-dera at noon", "Go at noon")
            check("Alex syncs first", try await alex.sync() == "synced")
            let diverged = try await sam.sync()
            check("Sam's sync diverges", diverged == "diverged", diverged)
            let preview = try await sam.mergePreview(branch: "origin/" + branch)
            check("one conflict on the title", preview.conflicts.count == 1, "\(preview.conflicts.count)")
            if let conflict = preview.conflicts.first {
                try await sam.mergeApply(branch: "origin/" + branch, resolutions: [conflict.key: .string("Kiyomizu-dera at sunset")])
                check("Sam merges and sends", try await sam.sync() == "synced")
                check("Alex gets the merge", try await alex.sync() == "synced")
                let merged = try await title(alex)
                check("both have the answer", merged == "Kiyomizu-dera at sunset", merged ?? "nil")
            }

            // A variant that is not open is uploaded without switching to it.
            let variantBranch = "variants/early-start-" + String(Int(Date().timeIntervalSince1970))
            try await alex.call("create_variant", ["branch": .string(variantBranch), "title": "Early start"])
            try await rename(alex, "Kiyomizu-dera before breakfast", "Even earlier")
            try await alex.call("switch_variant", ["branch": .string(branch)])
            let variant = try await alex.syncVariant(branch: variantBranch)
            check("variant uploaded without opening it", variant == "synced", variant)
            try await sam.sync()
            // Variants only on the remote are listed when the remote is named.
            let remoteVariants = try await sam.call("variants", ["remote": "origin"])["variants"]?.array ?? []
            check("Sam sees the variant", remoteVariants.contains { $0["title"]?.string == "Early start" && $0["remote_branch"]?.string == "origin/" + variantBranch },
                  remoteVariants.map { "\($0["title"]?.string ?? "?")@\($0["remote_branch"]?.string ?? "-")" }.joined(separator: ", "))

            let history = try await sam.history()
            check("versions of both arrive", Set(history.map(\.author)).isSuperset(of: ["Alex", "Sam"]))
            try? FileManager.default.removeItem(at: root)
        } catch {
            check("no error", false, String(describing: error))
        }
        print("sync test done: \(failures) failed, \(SelfTest.residentMegabytes()) MB in memory")
        log.info("sync test done: \(failures, privacy: .public) failed")
    }
}
#endif
