import SwiftUI
import TravelRepoKit
import UIKit

/// A problem report the person reads and edits before anything leaves the phone. Nothing is sent automatically:
/// they copy it, or share it through an app they choose.
struct ReportView: View {
    /// The trip open when the report started; its folder's Git state and TravelRepo's check results may be added.
    var tripID: UUID? = nil
    @Environment(\.dismiss) private var dismiss
    @State private var text = String(localized: "What happened:\n\nWhat I expected:\n\nSteps to reproduce:")
    @State private var facts: [Fact] = Fact.current()
    @State private var copied = false

    struct Fact: Identifiable {
        let id = UUID.v7()
        var label: LocalizedStringResource
        var value: String
        var include = true

        static func current() -> [Fact] {
            let d = UIDevice.current
            return [
                Fact(label: "App", value: "Jourfold for iOS \(Bundle.main.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String ?? "") (\(Bundle.main.object(forInfoDictionaryKey: "CFBundleVersion") as? String ?? ""))"),
                Fact(label: "System", value: "\(d.systemName) \(d.systemVersion)"),
                Fact(label: "Device type", value: d.model),
                Fact(label: "Language", value: Locale.current.identifier),
            ]
        }
    }

    /// TravelRepo's version, and for the open trip the Git state (git_health's summary, which leaves out addresses and
    /// credentials) and the codes of TravelRepo's check results, never their texts, which can quote the trip.
    private func addTripFacts() async {
        if let info = try? await TravelRepo.info() { facts.append(Fact(label: "TravelRepo", value: "\(info.version) (format \(info.formatVersion))")) }
        guard let tripID, let bridge = TravelRepoBridge.all[tripID] else { return }
        if let health = try? await bridge.session.gitHealth() { facts.append(Fact(label: "State of the trip's history", value: health.summary)) }
        let codes = Dictionary(grouping: bridge.diagnostics, by: \.code).map { "\($0.key) ×\($0.value.count)" }.sorted()
        facts.append(Fact(label: "Checks of the trip", value: codes.isEmpty ? String(localized: "no problems") : codes.joined(separator: ", ")))
    }

    private var report: String {
        ([text, "", String(localized: "About this device:")] + facts.filter(\.include).map { "\(String(localized: $0.label)): \($0.value)" }).joined(separator: "\n")
    }

    var body: some View {
        NavigationStack {
            JFForm {
                Section {
                    TextEditor(text: $text).frame(minHeight: 160).font(.jf(.body))
                } footer: { Footnote("Review and edit the report, then copy it or send it from your own mail program. Nothing is sent automatically.") }
                Section {
                    ForEach($facts) { $f in
                        Toggle(isOn: $f.include) {
                            VStack(alignment: .leading, spacing: 1) {
                                Text(f.label).font(.jf(.body, .medium))
                                Text(f.value).font(.jf(.footnote)).foregroundStyle(Color.jfMuted)
                            }
                        }
                    }
                } header: { Overline("About this device") } footer: { Footnote("Switch off what you do not want to send. No trip data, places or names are added; the trip's state names no addresses or accounts.") }
                Section {
                    Text(report).font(.jf(.footnote).monospaced()).foregroundStyle(Color.jfMuted).textSelection(.enabled)
                } header: { Overline("What leaves the phone") }
                Section {
                    Button { UIPasteboard.general.string = report; copied = true } label: { Label(copied ? "Copied" : "Copy", systemImage: copied ? "checkmark" : "doc.on.doc") }
                    ShareLink(item: report) { Label("Send by e-mail or another app", systemImage: "square.and.arrow.up") }
                }
            }
            .jfBackground()
            .task { await addTripFacts() }
            .navigationTitle("Report a problem")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar { ToolbarItem(placement: .topBarLeading) { Button(role: .close) { dismiss() } } }
        }
    }
}

/// GitHub's device sign-in: Jourfold shows a code, the person enters it on github.com, the app waits. Without a
/// GitHub App (no client id) there is no sign-in; the demo (`-demo YES`) signs in as the made-up `alex-travels`.
@Observable
final class Account {
    var login: String?
    var code: String?
    var verification: URL?
    var waiting = false
    var failure: String?
    /// The made-up account of the demo, which never talks to GitHub.
    var isDemo: Bool { login != nil && GitHubSignIn.clientID == nil }

    @MainActor
    func start() async {
        failure = nil
        guard GitHubSignIn.clientID != nil else {
            guard DebugTools.flag("demo") else {
                failure = String(localized: "This build names no GitHub App to sign in with. Enter one under Settings → GitHub App.")
                return
            }
            code = "WDJB-MJHT"
            waiting = true
            try? await Task.sleep(for: .seconds(4))
            if waiting { login = TravelRepoBridge.demoLogin; code = nil; waiting = false }
            return
        }
        waiting = true
        do {
            let signedIn = try await GitHubSignIn.signIn(show: { code, url in self.code = code; self.verification = url }, isWaiting: { self.waiting })
            if let signedIn { login = signedIn }
        } catch { failure = TravelRepoBridge.message(error) }
        code = nil; waiting = false
    }

    /// Picks up the sign-in stored on this device.
    @MainActor
    func restore() async {
        if let restored = await GitHubSignIn.restore() { login = restored }
        else if DebugTools.flag("demo"), DebugTools.flag("signedin") { login = TravelRepoBridge.demoLogin }
    }

    func cancel() { waiting = false; code = nil }
    func signOut() {
        login = nil
        if GitHubSignIn.clientID != nil { Task { await GitHubSignIn.signOut() } }
    }
}

struct AccountSection: View {
    @Environment(Account.self) private var account
    @Environment(\.openURL) private var openURL
    @State private var confirmSignOut = false

    var body: some View {
        Section {
            if let login = account.login {
                HStack(spacing: 12) {
                    Circle().fill(Color.jfAccentSoft).frame(width: 36, height: 36)
                        .overlay(Text(String(login.prefix(1)).uppercased()).font(.jf(.headline)).foregroundStyle(Color.jfAccentSoftText))
                    VStack(alignment: .leading, spacing: 1) {
                        Text(login).font(.jf(.body, .semibold))
                        Text("Signed in to GitHub").font(.jf(.footnote)).foregroundStyle(Color.jfMuted)
                    }
                }
                Button("Sign out", role: .destructive) { confirmSignOut = true }
                    .confirmationDialog(Text("Sign out of GitHub?"), isPresented: $confirmSignOut, titleVisibility: .visible) {
                        Button("Sign out", role: .destructive) { account.signOut() }
                    } message: { Text("Trips stay on this phone. They sync again after you sign in.") }
            } else if let code = account.code {
                VStack(alignment: .leading, spacing: 10) {
                    Text("Enter this code on GitHub:").font(.jf(.callout))
                    Text(code).font(.system(size: 32, weight: .bold, design: .monospaced)).textSelection(.enabled)
                        .accessibilityLabel(Text(code.map { String($0) }.joined(separator: " ")))
                    VStack(spacing: 10) {
                        Button { UIPasteboard.general.string = code; openURL(account.verification ?? URL(string: "https://github.com/login/device")!) } label: {
                            HStack(spacing: 8) { Image(systemName: "doc.on.clipboard"); Text("Copy code and open GitHub") }.frame(maxWidth: .infinity)
                        }
                        .buttonStyle(.borderedProminent).foregroundStyle(Color.jfOnAccent)
                        Button { account.cancel() } label: { Text("Cancel").frame(maxWidth: .infinity) }.buttonStyle(.bordered)
                    }
                    .font(.jf(.callout, .semibold)).controlSize(.large)
                    HStack(spacing: 8) { ProgressView(); Text("Waiting for GitHub…").font(.jf(.footnote)).foregroundStyle(Color.jfMuted) }
                }
                .padding(.vertical, 4)
            } else if account.waiting {
                HStack(spacing: 10) { ProgressView(); Text("Asking GitHub for a code…").foregroundStyle(Color.jfMuted) }
            } else {
                Button { Task { await account.start() } } label: { Label("Sign in with GitHub", systemImage: "person.crop.circle.badge.checkmark") }
                if let failure = account.failure { Text(failure).font(.jf(.footnote)).foregroundStyle(.red) }
            }
        } header: { Overline("Account") } footer: { Footnote("Only used to sync trips you choose. Jourfold has no account of its own.") }
    }
}
