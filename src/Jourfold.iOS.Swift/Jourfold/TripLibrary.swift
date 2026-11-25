import Foundation
import TravelRepoKit
import UIKit

/// The trips on this device: one TravelRepo trip folder each, under Application Support/trips, where the .NET app keeps
/// them too. Trip content lives only
/// in those folders (AGENTS.md: deleting the app's own state never destroys a trip); the app keeps nothing else about
/// a trip besides caches (weather) and settings.
@MainActor
enum TripLibrary {
    static let support: URL = (try? FileManager.default.url(for: .applicationSupportDirectory, in: .userDomainMask, appropriateFor: nil, create: true)) ?? URL(fileURLWithPath: NSTemporaryDirectory())
    static let root = support.appendingPathComponent("trips", isDirectory: true)
    /// Where TravelRepo puts files it moved aside or needs to finish an interrupted write.
    static let recovery = support.appendingPathComponent("recovery", isDirectory: true)
    static func recovery(for folder: URL) -> URL { recovery.appendingPathComponent(folder.lastPathComponent, isDirectory: true) }
    /// The exchange rates TravelRepo converts with: the European Central Bank's, fetched only when the person asks.
    static let rates = support.appendingPathComponent(DebugTools.flag("demo") || DebugTools.flag("selftest") ? "Caches/exchange-rates-demo.json" : "Caches/exchange-rates.json")
    static func weatherCache(for folder: URL) -> URL { support.appendingPathComponent("Caches/Weather/\(folder.lastPathComponent)", isDirectory: true) }

    /// TravelRepo's culture for dates, numbers and its own messages.
    static var culture: String {
        let language = Locale.current.language.languageCode?.identifier ?? "en"
        let region = Locale.current.region?.identifier ?? (language == "de" ? "DE" : "GB")
        return "\(language)-\(region)"
    }

    private static var configured = false

    /// Previews of this app kept trips under `Trips` and what was removed under `Recovery`, which on a phone are other
    /// folders than `trips` and `recovery`. They move once; a trip whose name is taken stays where it is until it is free.
    private static func takeEarlierFolders() {
        move(support.appendingPathComponent("Trips", isDirectory: true), into: root)
        move(support.appendingPathComponent("Recovery", isDirectory: true), into: recovery)
    }

    /// Renames the folder in one step; only when both are there, what is in it moves one by one, replacing nothing.
    /// By the names in the folder above: without a difference between upper and lower case, both names find the same.
    static func move(_ from: URL, into new: URL) {
        let files = FileManager.default
        let names = (try? files.contentsOfDirectory(atPath: from.deletingLastPathComponent().path)) ?? []
        guard names.contains(from.lastPathComponent) else { return }
        guard names.contains(new.lastPathComponent) else { try? files.moveItem(at: from, to: new); return }
        for item in (try? files.contentsOfDirectory(at: from, includingPropertiesForKeys: nil)) ?? [] {
            try? files.moveItem(at: item, to: new.appendingPathComponent(item.lastPathComponent))
        }
        if (try? files.contentsOfDirectory(atPath: from.path))?.isEmpty == true { try? files.removeItem(at: from) }
    }

    /// Tells TravelRepo where libgit2 is; once per start.
    static func prepare() async throws {
        guard !configured else { return }
        takeEarlierFolders()
        try FileManager.default.createDirectory(at: root, withIntermediateDirectories: true)
        try FileManager.default.createDirectory(at: rates.deletingLastPathComponent(), withIntermediateDirectories: true)
        if let git2 = Bundle.main.privateFrameworksPath.map({ $0 + "/git2.framework/git2" }), FileManager.default.fileExists(atPath: git2) {
            try await TravelRepo.configure(libgit2: git2)
        }
        // The demo (`-demo YES`, screenshots and the self-test) converts with the sample's fixed rates of 9 January 2027.
        if DebugTools.flag("demo") || DebugTools.flag("selftest") {
            let perEuro = Rates.demo.filter { $0.key != "EUR" }.mapValues { NSDecimalNumber(decimal: $0).doubleValue }
            try JSONSerialization.data(withJSONObject: ["Date": "2027-01-09", "PerEuro": perEuro]).write(to: rates)
        }
        Rates.reload()
        configured = true
    }

    /// The folders that hold a trip.
    static func folders() -> [URL] {
        let all = (try? FileManager.default.contentsOfDirectory(at: root, includingPropertiesForKeys: nil)) ?? []
        return all.filter { FileManager.default.fileExists(atPath: $0.appendingPathComponent("travel.yaml").path) }.sorted { $0.lastPathComponent < $1.lastPathComponent }
    }

    static var caches: URL { support.appendingPathComponent("Caches", isDirectory: true) }

    /// The folder of an open trip.
    static func folder(of trip: UUID) -> URL? { TravelRepoBridge.all[trip]?.folder }

    /// Opens every trip on the device. A trip that cannot be read is listed in `store.unreadable` instead of
    /// stopping the others.
    static func load(_ store: TripStore) async {
        do { try await prepare() } catch { store.problem = TravelRepoBridge.message(error); store.loading = false; return }
        DotNetApp.takeSettings()
        store.unreadable = []
        for folder in folders() {
            do { try await TravelRepoBridge.open(folder, store: store) }
            catch { TravelRepoBridge.log.error("could not open \(folder.lastPathComponent): \(String(describing: error))"); store.unreadable.append(folder.lastPathComponent) }
        }
        DotNetApp.takeTrips(store)
        store.sortTrips()
        store.loading = false
    }

    /// A free folder name for a trip title, such as `spring-in-japan` or `spring-in-japan-2`.
    static func newFolder(for title: String) -> URL {
        let slug = title.folding(options: [.diacriticInsensitive, .caseInsensitive], locale: .current).lowercased()
            .map { $0.isLetter || $0.isNumber ? String($0) : "-" }.joined()
            .split(separator: "-").joined(separator: "-")
        let base = slug.isEmpty ? "trip" : String(slug.prefix(48))
        var name = base, n = 2
        while FileManager.default.fileExists(atPath: root.appendingPathComponent(name).path) { name = "\(base)-\(n)"; n += 1 }
        return root.appendingPathComponent(name, isDirectory: true)
    }

    /// The e-mail address from Settings, if there is one; never a placeholder, so it can go onto a person.
    static var ownEmail: String? { UserDefaults.standard.string(forKey: "authorEmail").map { $0.trimmingCharacters(in: .whitespaces) }.flatMap { $0.contains("@") ? $0 : nil } }

    /// The Git author for new versions: the signed-in GitHub account, else the person's name from Settings.
    static func author(_ account: Account?) -> (name: String, email: String) {
        let name = UserDefaults.standard.string(forKey: "authorName").flatMap { $0.isEmpty ? nil : $0 } ?? account?.login ?? String(localized: "Me")
        let email = UserDefaults.standard.string(forKey: "authorEmail").flatMap { $0.isEmpty ? nil : $0 } ?? account?.login.map { "\($0)@users.noreply.github.com" } ?? "me@example.invalid"
        return (name, email)
    }

    /// A new, empty trip with its first traveller, saved as a trip folder with Git history.
    @discardableResult
    static func create(title: String, start: DateComponents, end: DateComponents, zone: String, currency: String, traveller: String, store: TripStore, account: Account?) async throws -> UUID {
        try await prepare()
        let folder = newFolder(for: title)
        let me = Person(id: UUID.v7(), name: traveller.isEmpty ? author(account).name : traveller, isOrganizer: true)
        let trip = Trip(id: UUID.v7(), title: title, start: start, end: end, zone: zone, currency: currency, colorHue: 0, variant: "", people: [me], places: [], items: [],
                        bookings: [], expenses: [], budgets: [], tasks: [], packing: [], versions: [], notes: "")
        do {
            try await TravelRepoBridge.seed(trip, into: folder, author: author(account), message: "Start the trip")
            let id = try await TravelRepoBridge.open(folder, store: store)
            WelcomeSheet.markSeen(id)
            if let bridge = TravelRepoBridge.all[id], let person = store.trips.first(where: { $0.id == id })?.people.first, account?.login == nil {
                _ = await bridge.claim(person.id, login: nil)
            }
            if let login = account?.login, let bridge = TravelRepoBridge.all[id], let person = store.trips.first(where: { $0.id == id })?.people.first {
                _ = await bridge.claim(person.id, login: login)
            }
            store.sortTrips()
            return id
        } catch {
            try? FileManager.default.removeItem(at: folder)
            throw error
        }
    }

    /// "Spring in Japan" (and with `all` the other made-up trips), written into trip folders of their own.
    @discardableResult
    static func addSamples(_ store: TripStore, all: Bool = false) async throws -> UUID? {
        try await prepare()
        var first: UUID?
        let samples = all ? [SampleData.springInJapan(), SampleData.weekendInParis(), SampleData.newZealandSummer()] : [SampleData.springInJapan()]
        for sample in samples {
            let folder = newFolder(for: sample.title)
            try await TravelRepoBridge.seed(sample, into: folder, author: (sample.people.first?.name ?? "Alex", "alex@example.invalid"), message: "Add the sample trip")
            let id = try await TravelRepoBridge.open(folder, store: store)
            WelcomeSheet.markSeen(id)
            first = first ?? id
        }
        store.sortTrips()
        return first
    }

    static let deleted = recovery.appendingPathComponent(".deleted", isDirectory: true)

    /// Removes a trip from this device: its folder goes to recovery/.deleted, so nothing is lost by a wrong tap.
    /// What was shared on GitHub stays there.
    static func remove(_ id: UUID, store: TripStore) throws {
        guard let bridge = TravelRepoBridge.all[id] else { return }
        bridge.stopWatching()
        let bin = deleted
        try FileManager.default.createDirectory(at: bin, withIntermediateDirectories: true)
        let stamp = ISO8601DateFormatter().string(from: Date()).replacingOccurrences(of: ":", with: "-")
        try FileManager.default.moveItem(at: bridge.folder, to: bin.appendingPathComponent("\(bridge.folder.lastPathComponent)-\(stamp)"))
        TravelRepoBridge.all[id] = nil
        store.trips.removeAll { $0.id == id }
    }

    /// Removes every trip and cache, for screenshots and the self-test (`-reset YES`).
    static func reset() {
        takeEarlierFolders()
        for key in UserDefaults.standard.dictionaryRepresentation().keys where key.hasPrefix("me-") || key.hasPrefix("welcomed-") { UserDefaults.standard.removeObject(forKey: key) }
        for folder in [root, recovery, support.appendingPathComponent("Caches")] { try? FileManager.default.removeItem(at: folder) }
        TravelRepoBridge.all = [:]
    }
}

/// Small made-up files for the sample's attachments, so documents open like real ones: a one-page PDF ticket and an
/// image. A Wallet pass needs Apple's signature, which a sample cannot have; it stays a plain text file.
enum SampleDocuments {
    struct Made { let data: Data; let mediaType: String; let ext: String; let contentType: String }

    static func make(_ attachment: Attachment) -> Made? {
        switch attachment.kind {
        case .pdf:
            let page = CGRect(x: 0, y: 0, width: 595, height: 420)
            let data = UIGraphicsPDFRenderer(bounds: page).pdfData { context in
                context.beginPage()
                let title = attachment.name.replacingOccurrences(of: ".pdf", with: "")
                (title as NSString).draw(at: CGPoint(x: 40, y: 40), withAttributes: [.font: UIFont.boldSystemFont(ofSize: 22)])
                ("Sample ticket · Alex, Sam · made-up booking" as NSString).draw(at: CGPoint(x: 40, y: 80), withAttributes: [.font: UIFont.systemFont(ofSize: 14), .foregroundColor: UIColor.darkGray])
            }
            return Made(data: data, mediaType: "application/pdf", ext: "pdf", contentType: "pdf")
        case .image:
            let size = CGSize(width: 600, height: 400)
            let image = UIGraphicsImageRenderer(size: size).image { context in
                UIColor(red: 0.06, green: 0.48, blue: 0.48, alpha: 1).setFill(); context.fill(CGRect(origin: .zero, size: size))
                (attachment.name.replacingOccurrences(of: ".png", with: "") as NSString).draw(at: CGPoint(x: 32, y: 170), withAttributes: [.font: UIFont.boldSystemFont(ofSize: 36), .foregroundColor: UIColor.white])
            }
            return image.pngData().map { Made(data: $0, mediaType: "image/png", ext: "png", contentType: "image") }
        case .walletPass, .mail:
            // A made-up booking e-mail from example.com in place of a pass, which only Apple can sign.
            let title = attachment.name.replacingOccurrences(of: ".pkpass", with: "").replacingOccurrences(of: ".eml", with: "")
            let mail = """
            From: Bookings <bookings@example.com>\r
            To: Alex <alex@example.org>\r
            Subject: \(title)\r
            Date: Mon, 04 Jan 2027 09:12:00 +0100\r
            MIME-Version: 1.0\r
            Content-Type: text/plain; charset=utf-8\r
            \r
            Hello Alex,\r
            \r
            this is a made-up confirmation for the sample trip: \(title).\r
            Travellers: Alex, Sam.\r
            \r
            Have a good trip!\r

            """
            return Made(data: Data(mail.utf8), mediaType: "message/rfc822", ext: "eml", contentType: "file")
        case .file, .note:
            return Made(data: Data("\(attachment.name)\nSample document.\n".utf8), mediaType: "text/plain", ext: "txt", contentType: "file")
        case .link:
            return nil
        }
    }
}
