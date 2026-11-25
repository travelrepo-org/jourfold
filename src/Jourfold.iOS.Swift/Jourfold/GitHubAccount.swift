import Foundation
import Security
import TravelRepoKit

/// The GitHub sign-in through TravelRepoKit, with the GitHub App a build names (`GitHubClientID` in Info.plist, like
/// the .NET app's `-p:JourfoldGitHubClientId`), one named in Settings, or `-github-client <id>`. The token lives in
/// the Keychain; TravelRepo only gets it in memory (`GitHub.setToken`) and uses it for Git on github.com.
enum GitHubSignIn {
    static var clientID: String? {
        let d = UserDefaults.standard
        for id in [DebugTools.value("github-client"), d.string(forKey: "githubClientID"), Bundle.main.object(forInfoDictionaryKey: "GitHubClientID") as? String] {
            if let id, !id.trimmingCharacters(in: .whitespaces).isEmpty { return id.trimmingCharacters(in: .whitespaces) }
        }
        return nil
    }
    static var appSlug: String? {
        DebugTools.value("github-app") ?? (Bundle.main.object(forInfoDictionaryKey: "GitHubAppSlug") as? String).flatMap { $0.isEmpty ? nil : $0 }
    }

    /// Hands a stored token to TravelRepo and returns the signed-in login, if any.
    static func restore() async -> String? {
        guard let clientID else { return nil }
        try? await GitHub.configure(clientId: clientID, appSlug: appSlug)
        guard let token = Keychain.token else { return nil }
        try? await GitHub.setToken(token)
        // No account means that GitHub refused the token. Offline, the account of the last sign-in counts.
        do {
            let login = try await GitHub.account()?.login
            if let login { remember(login) }
            return login
        } catch { return UserDefaults.standard.string(forKey: loginKey) }
    }

    static let loginKey = "githubLogin"
    private static func remember(_ login: String?) { UserDefaults.standard.set(login, forKey: loginKey) }

    /// Runs the device flow: `show` gets the code to enter on GitHub; returns the login once the person entered it.
    @MainActor
    static func signIn(show: (String, URL?) -> Void, isWaiting: () -> Bool) async throws -> String? {
        guard let clientID else { return nil }
        try await GitHub.configure(clientId: clientID, appSlug: appSlug)
        let authorization = try await GitHub.beginSignIn()
        show(authorization.userCode, URL(string: authorization.verificationUri))
        var interval = max(authorization.interval, 5)
        let end = Date().addingTimeInterval(TimeInterval(authorization.expiresIn))
        while isWaiting(), Date() < end {
            try await Task.sleep(for: .seconds(interval))
            guard isWaiting() else { return nil }
            switch try await GitHub.poll(authorization) {
            case .connected(let token):
                Keychain.token = token
                let login = try await GitHub.account()?.login
                remember(login)
                return login
            case .slowDown: interval += 5
            case .pending: break
            }
        }
        return nil
    }

    static func signOut() async {
        try? await GitHub.signOut()
        Keychain.token = nil
        remember(nil)
    }

    /// The GitHub token in the Keychain of this device, in the item the .NET app uses too.
    enum Keychain {
        private static let query: [String: Any] = [kSecClass as String: kSecClassGenericPassword, kSecAttrService as String: "dev.jourfold.app", kSecAttrAccount as String: "github.user-token"]

        static var token: String? {
            get {
                var result: AnyObject?
                let status = SecItemCopyMatching(query.merging([kSecReturnData as String: true, kSecMatchLimit as String: kSecMatchLimitOne]) { $1 } as CFDictionary, &result)
                return status == errSecSuccess ? (result as? Data).map { String(decoding: $0, as: UTF8.self) } : nil
            }
            set {
                SecItemDelete(query as CFDictionary)
                if let newValue {
                    SecItemAdd(query.merging([kSecValueData as String: Data(newValue.utf8), kSecAttrAccessible as String: kSecAttrAccessibleAfterFirstUnlockThisDeviceOnly]) { $1 } as CFDictionary, nil)
                }
            }
        }
    }
}
