import Foundation
import Security

/// Where the app, the share extension and the widget leave things for each other: the app group, or, in a build
/// without one (a free developer team cannot get it from the command line), a Keychain group the three share.
/// Values are one item per key, so the extension adding an entry never overwrites what the app is taking.
enum SharedStore {
    static let container: URL? = {
        // AltStore and SideStore name the app groups they signed the app with.
        let groups = [Bundle.main.object(forInfoDictionaryKey: "JourfoldAppGroup") as? String].compactMap { $0 } + (Bundle.main.object(forInfoDictionaryKey: "ALTAppGroups") as? [String] ?? [])
        return groups.lazy.compactMap { FileManager.default.containerURL(forSecurityApplicationGroupIdentifier: $0) }.first
    }()
    /// `<team>.<app>.shared`. The team is not known when an unsigned build is made, and tools that sign it may also
    /// change the bundle ID, so both come from the running app: the team from what iOS puts a Keychain item into.
    private static let keychainGroup: String? = {
        var app = Bundle.main.bundleIdentifier ?? ""
        for end in [".share", ".widget"] where app.hasSuffix(end) { app.removeLast(end.count) }
        var probe: [String: Any] = [kSecClass as String: kSecClassGenericPassword, kSecAttrService as String: service + ".team", kSecAttrAccount as String: "team"]
        SecItemAdd(probe.merging([kSecValueData as String: Data([1]), kSecAttrAccessible as String: kSecAttrAccessibleAfterFirstUnlock]) { $1 } as CFDictionary, nil)
        probe[kSecReturnAttributes as String] = true
        var found: AnyObject?
        guard SecItemCopyMatching(probe as CFDictionary, &found) == errSecSuccess, let group = (found as? [String: Any])?[kSecAttrAccessGroup as String] as? String,
              let team = group.split(separator: ".").first else { return Bundle.main.object(forInfoDictionaryKey: "JourfoldKeychainGroup") as? String }
        return "\(team).\(app).shared"
    }()
    private static let service = "dev.jourfold.app.shared"
    /// A Keychain item is no place for large files.
    static let keychainFileLimit = 10_000_000
    static var usesKeychain: Bool { container == nil }

    private static var folder: URL? { container?.appendingPathComponent("Store", isDirectory: true) }
    private static func file(_ key: String) -> URL? { folder?.appendingPathComponent(key.addingPercentEncoding(withAllowedCharacters: .alphanumerics) ?? key) }
    private static func query(_ key: String?) -> [String: Any] {
        var q: [String: Any] = [kSecClass as String: kSecClassGenericPassword, kSecAttrService as String: service]
        if let keychainGroup { q[kSecAttrAccessGroup as String] = keychainGroup }
        if let key { q[kSecAttrAccount as String] = key }
        return q
    }

    @discardableResult
    static func set(_ key: String, _ data: Data) -> Bool { usesKeychain ? setKeychain(key, data) : setFile(key, data) }

    private static func setFile(_ key: String, _ data: Data) -> Bool {
        guard let folder, let file = file(key) else { return false }
        try? FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
        return (try? data.write(to: file, options: .atomic)) != nil
    }

    static func setKeychain(_ key: String, _ data: Data) -> Bool {
        SecItemDelete(query(key) as CFDictionary)
        return SecItemAdd(query(key).merging([kSecValueData as String: data, kSecAttrAccessible as String: kSecAttrAccessibleAfterFirstUnlock]) { $1 } as CFDictionary, nil) == errSecSuccess
    }

    static func get(_ key: String) -> Data? { usesKeychain ? getKeychain(key) : file(key).flatMap { try? Data(contentsOf: $0) } }

    static func getKeychain(_ key: String) -> Data? {
        var result: AnyObject?
        return SecItemCopyMatching(query(key).merging([kSecReturnData as String: true]) { $1 } as CFDictionary, &result) == errSecSuccess ? result as? Data : nil
    }

    static func remove(_ key: String) {
        if usesKeychain { SecItemDelete(query(key) as CFDictionary) } else if let file = file(key) { try? FileManager.default.removeItem(at: file) }
    }

    static func keys(_ prefix: String) -> [String] {
        if !usesKeychain {
            return ((try? FileManager.default.contentsOfDirectory(atPath: folder?.path ?? "")) ?? []).compactMap(\.removingPercentEncoding).filter { $0.hasPrefix(prefix) }
        }
        var result: AnyObject?
        guard SecItemCopyMatching(query(nil).merging([kSecReturnAttributes as String: true, kSecMatchLimit as String: kSecMatchLimitAll]) { $1 } as CFDictionary, &result) == errSecSuccess else { return [] }
        return (result as? [[String: Any]] ?? []).compactMap { $0[kSecAttrAccount as String] as? String }.filter { $0.hasPrefix(prefix) }
    }
}
