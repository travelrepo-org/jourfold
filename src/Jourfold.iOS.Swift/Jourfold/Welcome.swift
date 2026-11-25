import SwiftUI

/// "Who are you in this trip?", the first time a trip opens on this phone and TravelRepo does not know the person
/// yet (`me`): one of the trip's people, a new person, or not part of the trip. The choice is stored on the person
/// in the trip (`claim_person`), so every phone recognises them, and versions are signed with it.
struct WelcomeSheet: View {
    let tripID: UUID
    @Environment(\.dismiss) private var dismiss
    @Environment(TripStore.self) private var store
    @Environment(Account.self) private var account
    @State private var me: TravelRepoBridge.Me?
    @State private var newName = ""
    @State private var busy = false
    @State private var chosen: UUID?
    @State private var email = ""
    @State private var remember = true

    /// GitHub's private address of the signed-in account, for people who do not want theirs in the history.
    private var privateEmail: String? { account.login.map { "\($0)@users.noreply.github.com" } }
    private var emailValid: Bool { email.wholeMatch(of: /[^@\s]+@[^@\s]+\.[^@\s]+/) != nil }

    static func seen(_ trip: UUID) -> Bool { UserDefaults.standard.bool(forKey: "welcomed-\(trip.uuidString)") }
    static func markSeen(_ trip: UUID) { UserDefaults.standard.set(true, forKey: "welcomed-\(trip.uuidString)") }

    var body: some View {
        let trip = store.trips.first { $0.id == tripID }
        NavigationStack {
            JFList {
                Section {
                    ForEach(trip?.people ?? []) { person in
                        let taken = me?.taken.contains(person.id) == true
                        Button { ask(person.id) } label: {
                            HStack(spacing: 12) {
                                PersonAvatar(person: person, size: 36).accessibilityHidden(true)
                                VStack(alignment: .leading, spacing: 1) {
                                    Text(person.name).font(.jf(.body, .semibold)).foregroundStyle(Color.jfText)
                                    if taken { Text("Has another account or e-mail").font(.jf(.footnote)).foregroundStyle(Color.jfMuted) }
                                }
                                Spacer()
                            }
                            .contentShape(Rectangle())
                        }
                        .buttonStyle(.plain)
                        .disabled(busy)
                    }
                } header: { Overline("I am") }
                Section {
                    TextField("Your name", text: $newName)
                    Button { Task { await addMe() } } label: { Label("Add me to the trip", systemImage: "person.badge.plus") }
                        .disabled(newName.trimmingCharacters(in: .whitespaces).isEmpty || busy)
                } header: { Overline("Not in the list") }
                Section {
                    Button("I'm not travelling along") { Task { _ = await TravelRepoBridge.all[tripID]?.claimNobody(); done() } }
                } footer: { Footnote("Jourfold then shows the trip without “you”. You can choose later under Travellers.") }
            }
            .listStyle(.insetGrouped)
            .jfBackground()
            .navigationTitle("Who are you?")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar { ToolbarItem(placement: .cancellationAction) { Button("Not now") { dismiss() } } }
            .navigationDestination(item: $chosen) { person in emailStep(person) }
            .task { me = await TravelRepoBridge.all[tripID]?.me(login: account.login) }
        }
        .presentationDetents([.medium, .large])
        .interactiveDismissDisabled(busy)
    }

    private func ask(_ person: UUID) {
        let known = TravelRepoBridge.all[tripID]?.entities[TravelRepoBridge.key(person)]?["identities"]?["git"]?.array?.first?["email"]?.string
        email = known ?? TripLibrary.ownEmail ?? privateEmail ?? ""
        chosen = person
    }

    private func emailStep(_ person: UUID) -> some View {
        JFForm {
            Section {
                TextField("E-mail", text: $email).textContentType(.emailAddress).keyboardType(.emailAddress).textInputAutocapitalization(.never).autocorrectionDisabled()
                if let privateEmail, privateEmail != email { Button("Use GitHub's private address") { email = privateEmail } }
                if TripLibrary.ownEmail == nil { Toggle("Also use it for other trips", isOn: $remember) }
            } footer: { Footnote("Your versions are saved under it, so your other devices recognise you. It is in the trip's history, and everyone who has the trip can see it.") }
            Section {
                Button { Task { await choose(person, email: email) } } label: { Text("Continue") }.disabled(busy || !emailValid)
                Button { Task { await choose(person, email: nil) } } label: { Text("Without an e-mail") }.disabled(busy)
            }
        }
        .jfBackground()
        .navigationTitle("Your e-mail in this trip")
        .navigationBarTitleDisplayMode(.inline)
    }

    private func choose(_ person: UUID, email: String?) async {
        busy = true
        if let email, remember, TripLibrary.ownEmail == nil { UserDefaults.standard.set(email, forKey: "authorEmail") }
        _ = await TravelRepoBridge.all[tripID]?.claim(person, login: account.login, email: email)
        busy = false
        done()
    }

    private func addMe() async {
        let person = Person(id: UUID.v7(), name: newName.trimmingCharacters(in: .whitespaces))
        store.update(tripID, String(localized: "Add \(person.name)")) { $0.people.append(person) }
        await TravelRepoBridge.all[tripID]?.flush()
        ask(person.id)
    }

    private func done() { Self.markSeen(tripID); dismiss() }
}
