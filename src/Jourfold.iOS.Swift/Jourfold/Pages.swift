import TravelRepoKit
import SwiftUI

// The pages behind "More". Each is a plain native List, so they inherit iOS behaviour (swipe, edit mode, Dynamic
// Type, VoiceOver) without extra work.

struct PlacesView: View {
    let tripID: UUID
    @Environment(TripStore.self) private var store
    @State private var query = ""
    @State private var adding = false
    @State private var removing: Place?
    var body: some View {
        let trip = store.trip(tripID)
        let places = trip.places.filter { query.isEmpty || $0.name.localizedCaseInsensitiveContains(query) || $0.address.localizedCaseInsensitiveContains(query) }
        let byCity = Dictionary(grouping: places, by: \.city)
        JFList {
            ForEach(byCity.keys.sorted { ($0.isEmpty ? 1 : 0, $0) < ($1.isEmpty ? 1 : 0, $1) }, id: \.self) { city in
                Section {
                    ForEach(byCity[city]!) { p in
                        NavigationLink { PlaceDetail(tripID: tripID, placeID: p.id) } label: {
                            VStack(alignment: .leading, spacing: 2) {
                                Text(p.name).font(.jf(.body, .semibold))
                                Text(p.address).font(.jf(.footnote)).foregroundStyle(Color.jfMuted)
                            }
                        }
                        .contextMenu {
                            Button { Maps.navigate(to: p) } label: { Label("Navigate", systemImage: "location") }
                            Button { UIPasteboard.general.string = p.address } label: { Label("Copy address", systemImage: "doc.on.doc") }
                            Divider()
                            Button(role: .destructive) { removing = p } label: { Label("Delete", systemImage: "trash") }
                        }
                    }
                } header: { Overline(city.isEmpty ? "Without an address" : LocalizedStringKey(city)) }
            }
        }
        .listStyle(.insetGrouped)
        .jfBackground()
        .searchable(text: $query, prompt: Text("Places"))
        .navigationTitle("Places")
        .toolbar { ToolbarItem(placement: .topBarTrailing) { Button { adding = true } label: { Image(systemName: "plus") }.accessibilityLabel(Text("New place")) } }
        .sheet(isPresented: $adding) { PlaceEditor(tripID: tripID, placeID: nil) }
        .deleteConfirmation(tripID: tripID, ids: removing.map { [$0.id] }, title: removing?.name) { removing = nil }
    }
}

struct ConverterView: View {
    let trip: Trip
    @Environment(TripStore.self) private var store
    @State private var amount = "1000"
    @State private var from = "JPY"
    @State private var to = "EUR"
    @State private var on = Online.ratesOn
    @State private var fetching = false
    @State private var failed = false
    var body: some View {
        let value = Decimal(string: amount.replacingOccurrences(of: ",", with: ".")) ?? 0
        let currencies = Rates.perEUR.keys.sorted()
        JFForm {
            if !Rates.available {
                Section {
                    Text("Jourfold converts with the European Central Bank's reference rates. Fetching them sends nothing about your trip.")
                    Button { Task { await fetch() } } label: { Label("Fetch exchange rates", systemImage: "arrow.down.circle") }.disabled(fetching)
                    if failed { Text("The rates could not be fetched. Check the connection and try again.").foregroundStyle(Color.jfDanger) }
                } header: { Overline("Exchange rates") }
            } else {
                Section {
                    HStack {
                        TextField("Amount", text: $amount).decimalKeyboard().font(.jf(.title2, .semibold).monospacedDigit())
                        Picker("From", selection: $from) { choices(currencies) }.labelsHidden()
                    }
                    Button { swap(&from, &to) } label: { Label("Swap", systemImage: "arrow.up.arrow.down") }
                    HStack {
                        Text(Fmt.money(Rates.convert(Money(value: value, currency: from), to: to), to)).font(.jf(.title2, .bold).monospacedDigit()).textSelection(.enabled).accessibilityLabel(Text("\(Fmt.money(value, from)) is \(Fmt.money(Rates.convert(Money(value: value, currency: from), to: to), to))"))
                        Spacer()
                        Picker("To", selection: $to) { choices(currencies) }.labelsHidden()
                    }
                } footer: {
                    Footnote("Rates of \((Rates.date ?? Date()).formatted(date: .abbreviated, time: .omitted)) from the European Central Bank, for orientation: card and cash rates differ. Nothing is sent when you convert.")
                }
                Section {
                    ForEach([500, 1000, 3000, 5000, 10000], id: \.self) { v in
                        LabeledContent(Fmt.money(Decimal(v), from)) { Text(Fmt.money(Rates.convert(Money(value: Decimal(v), currency: from), to: trip.currency), trip.currency)).monospacedDigit() }
                    }
                } header: { Overline("Handy amounts") }
                if !Online.demo {
                    Section { Button { Task { await fetch(force: true) } } label: { Label("Fetch the newest rates", systemImage: "arrow.clockwise") }.disabled(fetching) }
                }
            }
        }
        .jfBackground()
        .navigationTitle("Currency converter")
        .onChange(of: from) { RecentCurrencies.remember(from, trip) }
        .onChange(of: to) { RecentCurrencies.remember(to, trip) }
        .task { if on { _ = await Online.updateRates(store: store) } }
        .onAppear { if !Rates.perEUR.keys.contains(from) { from = trip.currency == "EUR" ? (currencies.first { $0 != "EUR" } ?? "EUR") : trip.currency } }
    }

    /// The trip's currencies and those chosen lately come first.
    @ViewBuilder private func choices(_ all: [String]) -> some View {
        let near = RecentCurrencies.list(trip).filter(all.contains)
        Section { ForEach(near, id: \.self) { Text($0) } }
        Section { ForEach(all.filter { !near.contains($0) }, id: \.self) { Text($0) } }
    }

    private func fetch(force: Bool = false) async {
        fetching = true; failed = false
        Online.ratesOn = true; on = true
        failed = !(await Online.updateRates(force: true, store: store))
        fetching = false
    }
}

struct TripDetailsView: View {
    let tripID: UUID
    @State private var otherCurrency = ""
    @State private var budgets: [String] = []
    @State private var newCurrency = ""

    private func setCurrency(_ code: String, convert: Bool = false) async {
        guard let bridge = TravelRepoBridge.all[tripID] else { return }
        await bridge.changeCosts {
            let r = try await bridge.session.call("set_trip_currency", ["currency": .string(code), "convert_budgets": .bool(convert)])
            newCurrency = code; otherCurrency = ""
            budgets = convert ? [] : (r["budgets_in_other_currency"]?.array ?? []).compactMap { $0["title"]?.string }
        }
    }

    private func field<V>(_ path: WritableKeyPath<Trip, V>) -> Binding<V> {
        Binding(get: { store.trip(tripID)[keyPath: path] }, set: { new in store.update(tripID, "") { $0[keyPath: path] = new } })
    }

    @Environment(TripStore.self) private var store
    var body: some View {
        let trip = store.trip(tripID)
        let cal = trip.calendar
        JFForm {
            Section { TextField("Name of the trip", text: field(\.title)) }
            Section {
                DatePicker("Starts", selection: Binding(get: { cal.date(from: trip.start) ?? Date() }, set: { field(\.start).wrappedValue = cal.dateComponents([.year, .month, .day], from: $0) }), displayedComponents: .date)
                DatePicker("Ends", selection: Binding(get: { cal.date(from: trip.end) ?? Date() }, set: { field(\.end).wrappedValue = cal.dateComponents([.year, .month, .day], from: $0) }), displayedComponents: .date)
            }
            .environment(\.timeZone, cal.timeZone)
            Section {
                Picker("Currency", selection: Binding(get: { trip.currency }, set: { code in if code != trip.currency { Task { await setCurrency(code) } } })) { ForEach(RecentCurrencies.list(trip), id: \.self) { Text($0) } }
                HStack {
                    TextField("Other currency, such as CAD", text: $otherCurrency).textInputAutocapitalization(.characters).autocorrectionDisabled()
                    Button("Use") { Task { await setCurrency(otherCurrency.uppercased()) } }.disabled(otherCurrency.trimmingCharacters(in: .whitespaces).count != 3)
                }
                Picker("Main time zone", selection: field(\.zone)) { ForEach(TimeZonePicker.common(including: trip.zone), id: \.self) { Text(TimeZonePicker.name($0)).tag($0) } }
                LabeledContent("Variant", value: trip.variant)
            }
            JFSection("Notes") { TextField("Notes", text: field(\.notes), axis: .vertical).lineLimit(4...12) }
        }
        .jfBackground()
        .confirmationDialog(Text("Convert the budgets to \(newCurrency)?"), isPresented: Binding(get: { !budgets.isEmpty }, set: { if !$0 { budgets = [] } }), titleVisibility: .visible) {
            Button("Convert") { Task { await setCurrency(newCurrency, convert: true) } }
            Button("Keep as they are", role: .cancel) {}
        } message: { Text("\(budgets.joined(separator: ", ")): converted with the exchange rates on this phone and rounded to whole units.") }
        .navigationTitle("Trip details")
    }
}

struct TravellersView: View {
    let tripID: UUID
    @Environment(TripStore.self) private var store
    @Environment(Account.self) private var account
    @State private var newName = ""
    /// Who the user is in this trip, from TravelRepo's `me`.
    @State private var me: TravelRepoBridge.Me?
    @State private var planText: String?
    @State private var shareOnGitHub = false
    @State private var invite = false
    @State private var inviteLogin = ""

    private func sendInvite() async {
        let login = inviteLogin.trimmingCharacters(in: .whitespaces).replacingOccurrences(of: "@", with: ""); inviteLogin = ""
        guard !login.isEmpty, let bridge = TravelRepoBridge.all[tripID], let url = bridge.remotes.first(where: { $0.name == "origin" })?.url else { return }
        let fullName = url.replacingOccurrences(of: ".git", with: "").split(separator: "/").suffix(2).joined(separator: "/")
        do { try await GitHub.invite(login, to: fullName); store.problem = nil } catch { store.problem = TravelRepoBridge.message(error) }
    }
    var body: some View {
        let trip = store.trip(tripID)
        JFList {
            Section {
                ForEach(trip.people) { p in
                    HStack(spacing: 12) {
                        PersonAvatar(person: p, size: 36)
                        VStack(alignment: .leading) {
                            Text(p.name).font(.jf(.body, .semibold))
                            Text(p.isOrganizer ? "Organiser · traveller" : "Traveller").font(.jf(.footnote)).foregroundStyle(Color.jfMuted)
                        }
                        if let me {
                            Spacer()
                            if me.person == p.id {
                                Text("You").font(.jf(.footnote, .semibold)).foregroundStyle(Color.jfAccent)
                            } else {
                                Button("That's me") { Task { self.me = await TravelRepoBridge.all[tripID]?.claim(p.id, login: account.login) } }
                                    .buttonStyle(.bordered).controlSize(.small)
                                    .accessibilityLabel(Text("I am \(p.name)"))
                            }
                        }
                    }
                }
                if me?.person != nil {
                    Button("I'm not travelling along") { Task { self.me = await TravelRepoBridge.all[tripID]?.claimNobody() } }
                }
                HStack {
                    TextField("Add a traveller", text: $newName)
                    Button { store.update(tripID, "Person") { $0.people.append(Person(id: UUID.v7(), name: newName)) }; newName = "" } label: { Image(systemName: "plus.circle.fill").font(.title3) }
                        .disabled(newName.isEmpty).accessibilityLabel(Text("Add"))
                }
            } footer: { Footnote("People belong to this trip. Nobody needs a Jourfold account.") }
            Section {
                Button { if TravelRepoBridge.all[tripID]?.isShared == true { invite = true } else { shareOnGitHub = true } } label: { Label("Invite with GitHub", systemImage: "person.badge.plus") }
                if let planText { ShareLink(item: planText) { Label("Send the plan as text", systemImage: "square.and.arrow.up") } }
            } header: { Overline("Share") } footer: { Footnote("The plan as text is a copy: changes to it do not come back into the trip.") }
        }
        .listStyle(.insetGrouped)
        .jfBackground()
        .navigationTitle("Travellers")
        .task(id: "\(account.login ?? "")#\(store.travelRepoReads)") {
            me = await TravelRepoBridge.all[tripID]?.me(login: account.login)
            planText = try? await TravelRepoBridge.all[tripID]?.session.export("plan", format: "text")
        }
        .sheet(isPresented: $shareOnGitHub) { ShareOnGitHubSheet(tripID: tripID) }
        .alert(Text("Invite with GitHub"), isPresented: $invite) {
            TextField("GitHub name", text: $inviteLogin).textInputAutocapitalization(.never).autocorrectionDisabled()
            Button("Invite") { Task { await sendInvite() } }
            Button("Cancel", role: .cancel) { inviteLogin = "" }
        } message: { Text("GitHub sends them an invitation to the trip's repository.") }
    }
}

struct SyncView: View {
    let tripID: UUID
    @Environment(TripStore.self) private var store
    @Environment(Account.self) private var account
    @AppStorage("advanced") private var advanced = false
    @State private var versionTitle = ""
    @State private var askTitle = false
    @State private var status: SyncStatus?
    @State private var health: GitHealth?
    @State private var confirmAbort = false
    @State private var share = false
    var body: some View {
        let trip = store.trip(tripID)
        let changes = store.changes[trip.id] ?? []
        let shared = TravelRepoBridge.all[tripID]?.isShared == true
        JFList {
            Section {
                HStack(spacing: 12) {
                    Image(systemName: store.isSyncing ? "arrow.triangle.2.circlepath" : shared ? "checkmark.icloud.fill" : "iphone")
                        .font(.title2).foregroundStyle(Color.jfAccent)
                        .symbolEffect(.rotate, isActive: store.isSyncing)
                        .accessibilityHidden(true)
                    VStack(alignment: .leading, spacing: 2) {
                        Text(store.isSyncing ? "Syncing…" : headline).font(.jf(.body, .semibold))
                        if let last = store.lastSync[tripID] { Text("Last synced \(last.formatted(date: .omitted, time: .shortened))").font(.jf(.footnote)).foregroundStyle(Color.jfMuted) }
                    }
                }
                if let why = store.syncProblem[tripID] ?? TravelRepoBridge.all[tripID]?.versionProblem {
                    Label { Text(why) } icon: { Image(systemName: "exclamationmark.triangle") }.font(.jf(.footnote)).foregroundStyle(Color.jfDanger)
                }
                if shared {
                    Button { Task { await store.sync(tripID); await loadStatus() } } label: { Label("Sync now", systemImage: "arrow.triangle.2.circlepath") }.disabled(store.isSyncing)
                } else {
                    Button { share = true } label: { Label("Share on GitHub", systemImage: "person.2.badge.plus") }
                }
            } header: { Overline("Sync") } footer: {
                if !shared { Text("The trip is only on this phone. Shared on GitHub, everyone you invite plans along, and every phone keeps the whole history.") }
            }
            if let health, health.staleLock != nil || health.operation != nil || health.detached {
                Section {
                    if let operation = health.operation {
                        Text("A \(operation) started outside Jourfold is unfinished. Until it is finished or cancelled, nothing can be saved as a version.")
                        Button(role: .destructive) { confirmAbort = true } label: { Label("Cancel it", systemImage: "xmark.circle") }
                    }
                    if health.staleLock != nil {
                        Text("Git left a lock file behind, perhaps after the app was stopped while saving.")
                        Button { Task { await repair { _ = try await $0.removeStaleLock() } } } label: { Label("Remove the old lock", systemImage: "lock.open") }
                    }
                    if health.detached { Text("The trip shows an old version instead of the current one. Syncing or choosing a version brings it back.") }
                    Button { Task { await repair { try await $0.recover() } } } label: { Label("Finish interrupted saving", systemImage: "wrench.and.screwdriver") }
                } header: { Overline("Needs repair") }
            }
            if let merge = store.pendingMerge, merge.tripID == tripID {
                Section {
                    NavigationLink(value: MorePage.merge) {
                        Label { VStack(alignment: .leading) { Text("\(merge.from) changed the same plans").font(.jf(.body, .semibold)); Text("Choose what to keep").font(.jf(.footnote)).foregroundStyle(Color.jfMuted) } } icon: { Image(systemName: "arrow.triangle.merge").foregroundStyle(Color.jfWarning) }
                    }
                }
            }
            Section {
                if changes.isEmpty && status?.hasLocalChanges != true { Text("No changes since the last version.").foregroundStyle(Color.jfMuted) }
                ForEach(changes.filter { !$0.text.isEmpty }) { Text($0.text) }
                Button { askTitle = true } label: { Label("Save as a version", systemImage: "bookmark") }.disabled(changes.isEmpty && status?.hasLocalChanges != true)
            } header: { Overline("Changes on this phone") } footer: { Footnote("Changes are saved right away. After two minutes, when you leave the app and before syncing, they become a version, and versions are sent.") }
            Section {
                ForEach(trip.versions) { v in
                    NavigationLink { CompareView(tripID: tripID, version: v) } label: {
                        VStack(alignment: .leading, spacing: 2) {
                            Text(v.title).font(.jf(.body, .semibold))
                            Text("\(v.author) · \(v.date.formatted(date: .abbreviated, time: .shortened))").font(.jf(.footnote)).foregroundStyle(Color.jfMuted)
                            if advanced, let commit = v.commit { Text(commit.prefix(10)).font(.system(.caption, design: .monospaced)).foregroundStyle(Color.jfSubtleText) }
                        }
                    }
                }
            } header: { Overline("Versions") } footer: { Footnote("Tap a version to see what changed since then, and to go back to it.") }
            if advanced, let status {
                Section {
                    LabeledContent("Branch", value: status.branch)
                    if let remote = status.remote { LabeledContent("Remote", value: remote) }
                    ForEach(TravelRepoBridge.all[tripID]?.remotes ?? [], id: \.self) { r in LabeledContent(r.name, value: r.url) }
                    LabeledContent("Ahead / behind", value: "\(status.ahead) / \(status.behind)")
                    LabeledContent("State", value: status.state)
                } header: { Overline("Git") }
            }
        }
        .listStyle(.insetGrouped)
        .jfBackground()
        .refreshable { if shared { await store.sync(tripID) }; await loadStatus() }
        .navigationTitle("Sync and versions")
        .task(id: store.travelRepoReads) { await loadStatus() }
        .sheet(isPresented: $share) { ShareOnGitHubSheet(tripID: tripID) }
        .confirmationDialog(Text("Cancel the unfinished operation?"), isPresented: $confirmAbort, titleVisibility: .visible) {
            Button("Cancel it", role: .destructive) { Task { await repair { try await $0.abortOperation() } } }
        } message: { Text("What was changed during it is lost. The trip goes back to how it was before.") }
        .alert(Text("Save as a version"), isPresented: $askTitle) {
            TextField("What changed?", text: $versionTitle)
            Button("Save") { store.saveVersion(tripID, title: versionTitle.isEmpty ? String(localized: "Changes") : versionTitle); versionTitle = "" }
            Button("Cancel", role: .cancel) {}
        }
    }

    private var headline: LocalizedStringKey {
        switch status?.state {
        case "synced": "Up to date with GitHub"
        case "local_changes": "Versions to send"
        case "remote_changes": "New versions on GitHub"
        case "diverged": "Changed here and on GitHub"
        default: TravelRepoBridge.all[tripID]?.isShared == true ? "Shared on GitHub" : "Only on this phone"
        }
    }

    private func loadStatus() async {
        guard let bridge = TravelRepoBridge.all[tripID] else { return }
        status = try? await bridge.session.syncStatus(remote: bridge.isShared ? "origin" : nil)
        health = try? await bridge.session.gitHealth()
    }

    private func repair(_ action: (TravelRepoSession) async throws -> Void) async {
        guard let bridge = TravelRepoBridge.all[tripID] else { return }
        await bridge.flush()
        do { try await action(bridge.session); try await bridge.read() } catch { store.problem = TravelRepoBridge.message(error) }
        await loadStatus()
    }
}

/// Shares a trip that is only on this phone: a new private repository on GitHub, the trip sent there, and
/// invitations for the people named. Nothing leaves the phone before "Share" is tapped.
struct ShareOnGitHubSheet: View {
    let tripID: UUID
    @Environment(\.dismiss) private var dismiss
    @Environment(TripStore.self) private var store
    @Environment(Account.self) private var account
    @State private var name = ""
    @State private var invite = ""
    @State private var busy = false
    @State private var done: String?
    @State private var problem: String?

    var body: some View {
        NavigationStack {
            JFForm {
                if account.login == nil {
                    AccountSection()
                } else if let done {
                    Section {
                        Label("Shared on GitHub", systemImage: "checkmark.circle.fill").foregroundStyle(Color.jfSuccess)
                        if let url = URL(string: done) { Link(destination: url) { Text(done).font(.jf(.footnote)) } }
                    } footer: { Footnote("Invited people get an e-mail from GitHub. Once they accept, they can open the trip in Jourfold.") }
                } else {
                    Section {
                        TextField("Repository name", text: $name).textInputAutocapitalization(.never).autocorrectionDisabled()
                    } header: { Overline("New private repository") } footer: { Footnote("It belongs to \(account.login ?? ""). Only people you invite can see it.") }
                    Section {
                        TextField("GitHub names, separated by commas", text: $invite).textInputAutocapitalization(.never).autocorrectionDisabled()
                    } header: { Overline("Invite") } footer: { Footnote("This sends the whole trip with its history to GitHub: plans, places, people, costs and documents.") }
                    if let problem { Section { Text(problem).foregroundStyle(Color.jfDanger) } }
                }
            }
            .jfBackground()
            .navigationTitle("Share on GitHub")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) { Button(role: .close) { dismiss() } }
                if done == nil, account.login != nil {
                    ToolbarItem(placement: .confirmationAction) {
                        Button("Share") { Task { await share() } }.disabled(name.isEmpty || busy)
                    }
                }
            }
            .onAppear { if name.isEmpty { name = TripLibrary.folder(of: tripID)?.lastPathComponent ?? "trip" } }
        }
        .presentationDetents([.large])
    }

    private func share() async {
        busy = true; problem = nil
        let logins = invite.split(whereSeparator: { $0 == "," || $0 == " " }).map { $0.trimmingCharacters(in: .whitespaces).replacingOccurrences(of: "@", with: "") }
        do { done = try await GitHubTrips.share(tripID, name: name, invite: logins, store: store) }
        catch { problem = TravelRepoBridge.message(error) }
        busy = false
    }
}

struct SettingsView: View {
    @AppStorage("appearance") private var appearance = 0
    @AppStorage("notices") private var notices = true
    @AppStorage("calendar") private var calendar = false
    @AppStorage("calendar.kinds") private var calendarKinds = false
    @AppStorage("advanced") private var advanced = false
    @AppStorage(SecondZone.key) private var secondZone = SecondZone.phone
    /// Of the open trip; outside of one, what new trips start with.
    private var zoneChoice: Binding<String> {
        Binding { nav.openTrip.map { SecondZone.choice($0) } ?? secondZone } set: { new in
            if let trip = nav.openTrip { SecondZone.choose(new, trip) } else { secondZone = new }
        }
    }
    @AppStorage(MapConsent.key) private var maps = ""
    @AppStorage("authorName") private var authorName = ""
    @AppStorage("bar.third") private var third = "costs"
    @AppStorage("authorEmail") private var authorEmail = ""
    @AppStorage("githubClientID") private var githubClientID = ""
    @AppStorage("weather.online") private var weatherOnline = "off"
    @State private var calendarProblem: String?
    @Environment(TripStore.self) private var store
    @AppStorage("rates.online") private var ratesOnline = "off"
    @State private var showReport = false
    @Environment(AppNavigation.self) private var nav
    var body: some View {
        JFForm {
            Section {
                Picker("Appearance", selection: $appearance) {
                    Text("System").tag(0); Text("Light").tag(1); Text("Dark").tag(2)
                }
                Button { if let url = URL(string: UIApplication.openSettingsURLString) { UIApplication.shared.open(url) } } label: {
                    LabeledContent("Language") { Text(Locale.current.localizedString(forLanguageCode: Locale.current.language.languageCode?.identifier ?? "en") ?? "") }
                }
                Picker(nav.openTrip == nil ? "Second time zone" : "Second time zone in this trip", selection: zoneChoice) {
                    Text("None").tag("")
                    Text("This phone's").tag(SecondZone.phone)
                    ForEach(TimeZonePicker.common(including: zoneChoice.wrappedValue == SecondZone.phone ? "" : zoneChoice.wrappedValue).filter { !$0.isEmpty }, id: \.self) { Text(TimeZonePicker.name($0)).tag($0) }
                }
                Picker("In the bar", selection: $third) { ForEach(ThirdPlace.allCases) { Label(it: $0) } }
            } header: { Overline("Display") } footer: { Footnote("Text size follows Larger Text in the iOS settings.") }
            Section {
                NavigationLink { NotificationsView(tripID: nav.openTrip) } label: {
                    LabeledContent("Travel notifications") { Text(notices ? LocalizedStringKey("On") : "Off") }
                }
                Toggle("Show plans in Calendar", isOn: Binding(get: { calendar }, set: { on in
                    Task {
                        if on, !(await SystemCalendar.allowed(ask: true)) { calendar = false; calendarProblem = String(localized: "Jourfold may not use the calendar. Allow it under Settings → Apps → Jourfold."); return }
                        calendar = on; calendarProblem = nil
                        if on { await SystemCalendar.update(store.trips) } else { SystemCalendar.removeAll() }
                    }
                }))
                if calendar {
                    Toggle("One calendar per kind", isOn: Binding(get: { calendarKinds }, set: { calendarKinds = $0; Task { SystemCalendar.removeAll(); await SystemCalendar.update(store.trips) } }))
                }
                if let calendarProblem { Text(calendarProblem).font(.jf(.footnote)).foregroundStyle(Color.jfDanger) }
            } header: { Overline("Notifications and calendar") }
            Section {
                Toggle("Weather for the trip's places", isOn: Binding(get: { weatherOnline == "on" }, set: { weatherOnline = $0 ? "on" : "off" }))
                Toggle("Apple Maps", isOn: Binding(get: { maps == "on" }, set: { maps = $0 ? "on" : "off" }))
                Toggle("Exchange rates", isOn: Binding(get: { ratesOnline == "on" }, set: { ratesOnline = $0 ? "on" : "off" }))
            } header: { Overline("From the internet") } footer: { Footnote("Weather comes from Open-Meteo, which gets the places' positions and dates. Apple Maps gets the areas you look at and the addresses you search for. Rates come from the European Central Bank and get nothing about your trips.") }
            AccountSection()
            Section {
                TextField("Name", text: $authorName).textContentType(.name)
                TextField("E-mail", text: $authorEmail).textContentType(.emailAddress).keyboardType(.emailAddress).textInputAutocapitalization(.never)
            } header: { Overline("Versions are signed with") } footer: { Footnote("Stays in the trip's history and goes to GitHub with it. Leave empty to use your GitHub account.") }
            Section {
                Toggle("Advanced mode", isOn: $advanced)
                if advanced {
                    TextField("GitHub App client ID", text: $githubClientID).textInputAutocapitalization(.never).autocorrectionDisabled().font(.system(.body, design: .monospaced))
                }
                Button { showReport = true } label: { Label("Report a problem", systemImage: "exclamationmark.bubble") }
            } header: { Overline("Help") } footer: { Footnote("Advanced mode shows branches, remotes and commit hashes. Reports show you everything before anything is sent.") }
            UpdateSettings()
            StorageSection()
            Section {
                NavigationLink { AboutView() } label: { Text("About Jourfold") }
            }
        }
        .jfBackground()
        .navigationTitle("Settings")
        .sheet(isPresented: $showReport) { ReportView(tripID: nav.openTrip) }
        .onAppear { if nav.showReport { nav.showReport = false; showReport = true } }
    }
}

private extension Label where Title == Text, Icon == Image {
    init(it place: ThirdPlace) { self.init(place.title, systemImage: place.symbol) }
}

/// How much space the trips and what was downloaded take on this phone.
struct StorageSection: View {
    @Environment(TripStore.self) private var store
    @State private var sizes: [(name: String, bytes: Int64)] = []
    var body: some View {
        Section {
            ForEach(Array(sizes.enumerated()), id: \.offset) { _, s in LabeledContent(s.name, value: s.bytes.formatted(.byteCount(style: .file))) }
            if sizes.isEmpty { ProgressView() }
        } header: { Overline("Storage on this phone") } footer: { Footnote("Trips include their files and their whole history.") }
        .task {
            var folders = store.trips.compactMap { t in TravelRepoBridge.all[t.id].map { (t.title, $0.folder) } }
            folders.append((String(localized: "Downloaded weather and rates"), TripLibrary.caches))
            sizes = await Task.detached { folders.map { ($0.0, Self.size($0.1)) } }.value
        }
    }
    nonisolated static func size(_ folder: URL) -> Int64 {
        guard let files = FileManager.default.enumerator(at: folder, includingPropertiesForKeys: [.totalFileAllocatedSizeKey]) else { return 0 }
        var sum: Int64 = 0
        for case let url as URL in files { sum += Int64((try? url.resourceValues(forKeys: [.totalFileAllocatedSizeKey]))?.totalFileAllocatedSize ?? 0) }
        return sum
    }
}
