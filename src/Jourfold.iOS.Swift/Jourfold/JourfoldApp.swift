import SwiftUI
import WidgetKit
import os

@main
struct JourfoldApp: App {
    @UIApplicationDelegateAdaptor(AppDelegate.self) private var delegate
    @State private var store = TripStore()
    @State private var nav = AppNavigation()
    @State private var account = Account()

    init() { Fonts.register(); NoticeCenter.shared.listen() }

    var body: some Scene {
        WindowGroup {
            RootView()
                .environment(store)
                .environment(nav)
                .environment(account)
                .font(.jf(.body))
                .tint(.jfAccent)
                .onAppear { store.account = account; nav.applyLaunchArguments(store, account: account) }
        }
        .commands { TripCommands(nav: nav, store: store) }
        .backgroundTask(.appRefresh(BackgroundNotices.id)) { await BackgroundNotices.run(store) }
    }
}

/// For a keyboard and the iPad's menu bar.
struct TripCommands: Commands {
    let nav: AppNavigation
    let store: TripStore
    var body: some Commands {
        CommandGroup(replacing: .newItem) {
            Button("Add to trip") { nav.quickAddKind = nil; nav.quickAdd = true }.keyboardShortcut("n").disabled(nav.openTrip == nil)
        }
        CommandMenu("Trip") {
            Group {
                Button("Today") { nav.tab = .today }.keyboardShortcut("1")
                Button("Plan") { nav.tab = .plan }.keyboardShortcut("2")
                Button(ThirdPlace.current.title) { nav.tab = .map }.keyboardShortcut("3")
                Button("Settings") { nav.go(to: .settings) }.keyboardShortcut("4")
                Divider()
                Button("Sync now") { if let trip = nav.openTrip { Task { await store.sync(trip) } } }.keyboardShortcut("r")
                Button("All trips") { nav.openTrip = nil }.keyboardShortcut("0")
            }
            .disabled(nav.openTrip == nil)
        }
    }
}

enum TripTab: Hashable { case today, plan, add, map, more, page(MorePage) }

enum MorePage: String, Hashable, CaseIterable {
    case bookings, tasks, packing, places, files, costs, converter, trip, travellers, sync, settings, map, merge, about, notifications
}

/// Where the app is: which trip is open, which tab, which sheet. Launch arguments can set it, so screenshots are
/// reproducible (`-trip 0 -tab plan -sheet detail -page costs`).
@Observable
final class AppNavigation {
    var openTrip: UUID?
    var tab: TripTab = .today
    var quickAdd = false
    var quickAddKind: QuickAddKind?
    /// A group the next plan becomes a step of.
    var quickAddInto: UUID?
    var detail: PlanItem.ID?
    var planDay: Int = 1
    var timetable = false
    var toast: String?
    var ideas = false
    var showReport = false
    var merge = false
    var morePath: [MorePage] = []
    /// A trip just opened from GitHub, which asks who the person is in it.
    var welcome: UUID?
    var colorScheme: ColorScheme?

    /// Opens a page behind More.
    func go(to page: MorePage) { tab = .more; morePath = [page] }

    /// Opens the trips on the device, then applies the launch arguments. `-reset YES` starts with no trips,
    /// `-samples YES` adds the made-up trips when there are none (for screenshots).
    func applyLaunchArguments(_ store: TripStore, account: Account? = nil) {
        TestReport.applyArguments()
        #if JOURFOLD_DEBUG_TOOLS
        if DebugTools.flag("storecheck") {
            // On a device: whether app, share extension and widget can exchange anything in this build.
            print("shared store: \(SharedStore.usesKeychain ? "keychain" : "app group"), writes: \(SharedStore.set("check", Data([1]))), reads: \(SharedStore.get("check") == Data([1]))")
            SharedStore.remove("check")
            exit(0)
        }
        #endif
        Task { @MainActor in
            if DebugTools.flag("reset") || DebugTools.flag("selftest") { TripLibrary.reset() }
            await TripLibrary.load(store)
            await account?.restore()
            for bridge in TravelRepoBridge.all.values { await bridge.recognise(account?.login) }
            #if JOURFOLD_DEBUG_TOOLS
            if DebugTools.flag("devicetest") { await DeviceTest.run(store, account); print("REPORT end"); exit(0) }
            if DebugTools.flag("testreport") { await TestReport.run(store, account); exit(0) }
            if DebugTools.flag("mecheck") {
                // Why "who am I" does not work, in counts and codes only; nothing of the trips is printed.
                print("mecheck trips \(store.trips.count) unreadable \(store.unreadable.count) signedIn \(account?.login != nil) ownEmail \(TripLibrary.ownEmail != nil)")
                for (i, t) in store.trips.enumerated() {
                    guard let b = TravelRepoBridge.all[t.id] else { print("mecheck \(i) no bridge"); continue }
                    print("mecheck \(i) " + (await b.meCheck(account?.login)))
                }
                exit(0)
            }
            #endif
            if store.trips.isEmpty, DebugTools.flag("samples") || DebugTools.flag("selftest") {
                do { try await TripLibrary.addSamples(store, all: true) } catch { store.problem = TravelRepoBridge.message(error) }
            }
            #if JOURFOLD_DEBUG_TOOLS
            if DebugTools.flag("oldcosts") {
                // Budgets as older versions of Jourfold wrote them, for the offer to update them.
                for bridge in TravelRepoBridge.all.values {
                    for (id, data) in bridge.entities where data["type"]?.string == "budget" { try? await bridge.patch(id, ["scope": "trip"]) }
                }
            }
            if DebugTools.flag("selftest") { await SelfTest.runTravelRepo(store); exit(0) }
            if DebugTools.flag("githubsynctest") { await GitHubSyncTest.run(); exit(0) }
            #endif
            applyNavigation(store)
        }
    }

    private func applyNavigation(_ store: TripStore) {
        NoticeCenter.shared.install(self, store)
        if let i = DebugTools.value("trip").flatMap(Int.init), store.trips.indices.contains(i) { openTrip = store.trips[i].id }
        switch DebugTools.value("tab") {
        case "plan": tab = .plan
        case "map": tab = .map
        case "more": tab = .more
        default: break
        }
        if let day = DebugTools.value("day").flatMap(Int.init) { planDay = day }
        if DebugTools.flag("timetable") { tab = .plan; timetable = true }
        if DebugTools.flag("ideas") { tab = .plan; ideas = true }
        if DebugTools.flag("live"), let trip = store.trips.first, let train = trip.items.first(where: { $0.title == "Shinkansen to Kyoto" }) {
            LiveJourney.start(trip, train, demo: true)
        }
        if DebugTools.flag("conflict"), let trip = store.trips.first { Task { await store.sync(trip.id) } }
        if DebugTools.flag("merge") { tab = .more; morePath = [.sync, .merge] }
        if let page = DebugTools.value("page").flatMap(MorePage.init(rawValue:)) {
            if DebugTools.flag("sidebar") { tab = .page(page) } else { tab = .more; morePath = page == .notifications ? [.settings, page] : [page] }
        }
        switch DebugTools.value("sheet") {
        case "detail":
            if let trip = store.trips.first(where: { $0.id == openTrip }),
               let title = DebugTools.value("item") ?? Optional("Hotel Gracery Shinjuku") {
                detail = trip.items.first { $0.title == title }?.id
            }
        case "report": tab = .more; morePath = [.settings]; showReport = true
        case "quickadd": quickAdd = true
        case let sheet?:
            if let k = QuickAddKind(rawValue: sheet) { quickAdd = true; quickAddKind = k }
        default: break
        }
    }
}

struct RootView: View {
    @Environment(Account.self) private var account
    @Environment(AppNavigation.self) private var nav
    @Environment(TripStore.self) private var store
    @Environment(\.scenePhase) private var phase
    @AppStorage("appearance") private var appearance = 0
    @State private var shareForm = DebugTools.flag("shareform")
    var body: some View {
        Group {
            if let id = nav.openTrip {
                TripTabs(tripID: id)
                    .id(id)
                    .transition(.opacity)
            } else {
                TripListView()
                    .transition(.opacity)
            }
        }
        .animation(.easeInOut(duration: 0.2), value: nav.openTrip)
        .preferredColorScheme(appearance == 1 ? .light : appearance == 2 ? .dark : nil)
        .overlay(alignment: .top) {
            if let toast = nav.toast {
                Label(toast, systemImage: "square.and.arrow.down")
                    .font(.jf(.callout, .semibold))
                    .padding(.horizontal, 16).padding(.vertical, 10)
                    .glassEffect(.regular.tint(Color.jfAccent.opacity(0.25)), in: Capsule())
                    .padding(.top, 8)
                    .transition(.move(edge: .top).combined(with: .opacity))
                    .task { try? await Task.sleep(for: .seconds(3)); withAnimation { nav.toast = nil } }
                    .accessibilityAddTraits(.updatesFrequently)
            }
        }
        .alert(Text("That did not work"), isPresented: Binding(get: { store.problem != nil }, set: { if !$0 { store.problem = nil } })) {
            Button("OK", role: .cancel) { store.problem = nil }
        } message: { Text(store.problem ?? "") }
        .task(id: phase) {
            // The clock goes on while the app is open: "now" on Today, what is next, when notices are due.
            while phase == .active, !DebugTools.flag("demo"), !Task.isCancelled {
                try? await Task.sleep(for: .seconds(30))
                if !Task.isCancelled { store.now = Date() }
            }
        }
        .onChange(of: store.loading) { _, loading in if !loading { NoticeCenter.shared.openWaiting(); exchangeWithShareExtension(); OpenedFile.openWaiting(store) } }
        .onOpenURL { if $0.isFileURL { OpenedFile.open($0, store) } else { NoticeCenter.shared.open($0) } }
        .onReceive(NotificationCenter.default.publisher(for: OpenedFile.taken)) { _ in exchangeWithShareExtension() }
        .onAppear {
            // For tests: what a widget would open, also before the trips are there.
            if let link = DebugTools.value("openurl").flatMap(URL.init(string:)) { NoticeCenter.shared.open(link) }
        }
        .onChange(of: account.login) {
            // Trips are read before the account is back, and people sign in later: then it is known who they are.
            Task { for bridge in TravelRepoBridge.all.values { await bridge.recognise(account.login) } }
        }
        .onChange(of: store.trips.count) { exchangeWithShareExtension() }
        .onChange(of: store.travelRepoReads) { if !store.loading { publishForWidget(); NoticeCenter.shared.planSoon(store); Tickets.publish(store) } }
        .onChange(of: phase, initial: true) { _, p in
            if p == .background {
                BackgroundNotices.ask()
                // Leaving the app turns open changes into versions and sends them, while iOS still gives it time.
                let id = UIApplication.shared.beginBackgroundTask()
                Task {
                    for trip in store.trips { await store.autoSave(trip.id) }
                    if id != .invalid { UIApplication.shared.endBackgroundTask(id) }
                }
            }
            if p == .active {
                if !DebugTools.flag("demo") { store.now = Date() }
                exchangeWithShareExtension()
                NoticeCenter.shared.planSoon(store)
                Task {
                    for bridge in TravelRepoBridge.all.values {
                        // Quietly take new versions from GitHub (fast-forward only, never sends), then read what changed.
                        if bridge.isShared, account.login != nil, !DebugTools.flag("demo") { await store.sync(bridge.tripID, quiet: true) }
                        await bridge.refreshIfChanged()
                        // TravelRepo knows how long a forecast stays fresh, so this asks only when it is old.
                        await bridge.updateWeather()
                    }
                }
            }
        }
        .sheet(isPresented: $shareForm) {
            ShareForm(text: "Kyoto Railway Museum – opening hours and tickets", url: "https://example.org/kyoto-railway-museum",
                      trips: store.trips.map(\.shareSummary)) { _ in shareForm = false }
        }
    }

    /// Leaves the next plans of every trip for the Home Screen widget and asks it to draw again.
    private func publishForWidget() {
        SharedInbox.publishUpcoming(WidgetFeed.plans(store))
        WidgetCenter.shared.reloadAllTimelines()
        publishing?.cancel()
        publishing = Task {
            try? await Task.sleep(for: .seconds(1))
            if !Task.isCancelled { await WidgetFeed.publishCosts(store); WidgetCenter.shared.reloadTimelines(ofKind: "Costs") }
        }
    }
    @State private var publishing: Task<Void, Never>?

    /// Tells the share extension which trips and plans exist, and adds what it left in the inbox.
    @State private var sharedLink = false
    private func exchangeWithShareExtension() {
        // What waits in the inbox is taken only once the trips are open.
        guard !store.loading, !store.trips.isEmpty else { return }
        publishForWidget()
        SharedInbox.publish(store.trips.map(\.shareSummary))
        if DebugTools.flag("sharedemo") {
            SharedInbox.put(.init(text: "Kyoto Railway Museum", url: "https://example.org/kyoto-railway-museum", trip: "Spring in Japan", plan: "Arashiyama bamboo grove"))
        }
        // For tests: `-sharelink <url>` as the share extension would leave it, to a new place.
        if let link = DebugTools.value("sharelink"), !sharedLink {
            sharedLink = true
            SharedInbox.put(.init(text: link, url: link, trip: "Spring in Japan", plan: nil, newPlace: true))
        }
        if DebugTools.flag("openfile"), !sharedLink, let made = SampleDocuments.make(Attachment(id: UUID(), name: "Kyoto Railway Museum ticket.pdf", kind: .pdf)) {
            // For tests: a file as another app opens it in Jourfold.
            sharedLink = true
            let file = FileManager.default.temporaryDirectory.appendingPathComponent("Kyoto Railway Museum ticket.pdf")
            try? made.data.write(to: file)
            OpenedFile.open(file, store)
        }
        if DebugTools.flag("sharefile") {
            // The demo of a shared file: a made-up ticket PDF, as the share extension would leave it.
            var entry = SharedInbox.Entry(text: "Kyoto Railway Museum ticket.pdf", url: nil, trip: "Spring in Japan", plan: "Arashiyama Bamboo Grove", files: ["Kyoto Railway Museum ticket.pdf"])
            entry.id = UUID()
            if let made = SampleDocuments.make(Attachment(id: UUID(), name: "Kyoto Railway Museum ticket.pdf", kind: .pdf)) {
                _ = SharedInbox.putFile(made.data, name: entry.files[0], entry: entry.id)
                SharedInbox.put(entry)
            }
            UserDefaults.standard.set(false, forKey: "sharefile")
        }
        let entries = SharedInbox.take()
        for e in entries {
            TestReport.note("share taken: \(e.files.count) files, link \(e.url != nil), place \(e.newPlace == true), plan \(e.plan != nil), booking \(e.booking != nil)")
            guard let i = store.trips.firstIndex(where: { e.tripID == nil ? $0.title == e.trip : $0.id == e.tripID }) else {
                // Its trip is not open here: it waits, unless the trip is gone for good.
                if store.unreadable.isEmpty { SharedInbox.clean(e); store.problem = String(localized: "“\(e.text)” could not be added, because the trip “\(e.trip)” is not on this phone.") } else { SharedInbox.put(e) }
                continue
            }
            let tripID = store.trips[i].id
            let plan = e.planID.flatMap { id in store.trips[i].items.first { $0.id == id }?.id } ?? e.plan.flatMap { p in store.trips[i].items.first { $0.title.caseInsensitiveCompare(p) == .orderedSame }?.id }
            let line = [e.files.isEmpty ? e.text : nil, e.url].compactMap { $0 }.joined(separator: "\n")
            if e.newPlace == true || (plan != nil && e.files.isEmpty), let link = MapLink(e.url) {
                let title = link.name ?? (e.text == e.url ? nil : e.text.split(separator: "\n").first.map(String.init)) ?? String(localized: "New place")
                store.update(tripID, "Add \(title)") { t in
                    let place = Place(id: UUID.v7(), name: title, latitude: link.latitude ?? 0, longitude: link.longitude ?? 0, zone: t.zone, address: "")
                    t.places.append(place)
                    if link.latitude == nil, let short = ShortLink(e.url) { UserDefaults.standard.set(short.url.absoluteString, forKey: ShortLink.key(place.id)) }
                    // Shared to a plan, it becomes that plan's place; a journey goes there.
                    if let plan, let j = t.items.firstIndex(where: { $0.id == plan }) {
                        if t.items[j].transport != nil { t.items[j].transport?.to = place.id } else if t.items[j].stay != nil { t.items[j].stay?.place = place.id } else { t.items[j].place = place.id }
                    }
                }
            } else if let booking = e.booking.map(TravelRepoBridge.key), let bridge = TravelRepoBridge.all[tripID], bridge.entities[booking] != nil {
                if !line.isEmpty { Task { await bridge.changeCosts { let old = bridge.entities[booking]?["body"]?.string ?? ""; try await bridge.patch(booking, ["body": .string(old.isEmpty ? line : old + "\n\n" + line)]) } } }
            } else if !line.isEmpty {
                store.update(tripID, "Shared: \(e.text)") { t in
                    if let plan, let j = t.items.firstIndex(where: { $0.id == plan }) {
                        t.items[j].notes += (t.items[j].notes.isEmpty ? "" : "\n\n") + line
                        if let url = e.url { t.items[j].attachments.append(Attachment(id: UUID.v7(), name: url, kind: .link)) }
                    } else {
                        t.notes += (t.notes.isEmpty ? "" : "\n\n") + line
                    }
                }
            }
            // Files become the trip's documents, with the plan or booking when one was chosen.
            if !e.files.isEmpty, let bridge = TravelRepoBridge.all[tripID] {
                Task {
                    var left = e; left.text = e.files.joined(separator: ", "); left.url = nil; left.files = []; left.newPlace = nil
                    for name in e.files {
                        guard let data = SharedInbox.file(name, of: e) else { continue }
                        do { try await bridge.importFile(data, name: name, attachTo: e.booking ?? plan) } catch { store.problem = TravelRepoBridge.message(error); left.files.append(name) }
                    }
                    // What could not be imported stays for the next time.
                    if left.files.isEmpty { SharedInbox.clean(e) } else { SharedInbox.put(left) }
                }
            }
        }
        if let last = entries.last {
            withAnimation { nav.toast = String(localized: "Added “\(last.text)” to \(last.plan ?? last.trip)") }
        }
    }
}

struct TripTabs: View {
    let tripID: UUID
    @Environment(Account.self) private var account
    @Environment(AppNavigation.self) private var nav
    @Environment(TripStore.self) private var store
    @Environment(\.horizontalSizeClass) private var sizeClass
    /// The tab that was showing before the centre "+" was tapped, to return to it.
    @State private var lastTab: TripTab = .today
    @AppStorage("bar.third") private var third = "costs"

    var body: some View {
        @Bindable var nav = nav
        TabView(selection: $nav.tab) {
            Tab("Today", systemImage: "sun.horizon", value: TripTab.today) { TodayView(tripID: tripID) }
            Tab("Plan", systemImage: "calendar", value: TripTab.plan) { PlanView(tripID: tripID) }
            Tab("New", systemImage: "plus.circle.fill", value: TripTab.add) { Color.clear }
            // The fourth place in the bar is the person's choice, as in the .NET app: costs (the default), map,
            // bookings, tasks or packing lists.
            Tab(ThirdPlace.current.title, systemImage: ThirdPlace.current.symbol, value: TripTab.map) {
                if let page = ThirdPlace.current.page {
                    NavigationStack {
                        MorePageView(tripID: tripID, page: page)
                            .navigationDestination(for: MorePage.self) { MorePageView(tripID: tripID, page: $0) }
                    }
                } else { TripMapView(tripID: tripID) }
            }
            Tab("More", systemImage: "ellipsis", value: TripTab.more) { MoreView(tripID: tripID) }
                .defaultVisibility(.hidden, for: .sidebar)
            // On iPad the pages behind More sit in the sidebar. The phone leaves them out: more than five tabs would
            // make UIKit wrap the rest in its own "More" navigation controller, with a second back button.
            if sizeClass == .regular {
            TabSection("Trip") {
                // The one chosen for the bar is in the sidebar already.
                if third != "map" { pageTab(.map, "Map", "map") }
                if third != "costs" { pageTab(.costs, "Costs", "wallet.bifold") }
                if third != "bookings" { pageTab(.bookings, "Bookings", "ticket") }
                if third != "tasks" { pageTab(.tasks, "Tasks", "checklist") }
                if third != "packing" { pageTab(.packing, "Packing lists", "backpack") }
                pageTab(.places, "Places", "mappin.and.ellipse")
                pageTab(.files, "Files", "paperclip")
                pageTab(.converter, "Currency converter", "coloncurrencysign.arrow.trianglehead.counterclockwise.rotate.90")
                pageTab(.trip, "Trip details", "suitcase.rolling")
            }
            .defaultVisibility(.hidden, for: .tabBar)
            TabSection("Together") {
                pageTab(.travellers, "Travellers", "person.2")
                pageTab(.sync, "Sync and versions", "arrow.triangle.2.circlepath")
                pageTab(.settings, "Settings", "gearshape")
            }
            .defaultVisibility(.hidden, for: .tabBar)
            }
        }
        .tabViewStyle(.sidebarAdaptable)
        .defaultAdaptableTabBarPlacement(DebugTools.flag("sidebar") ? .sidebar : .automatic)
        .tabViewSidebarHeader {
            let trip = store.trip(tripID)
            HStack(spacing: 12) {
                TripBadge(trip: trip, size: 40)
                VStack(alignment: .leading, spacing: 1) {
                    Text(trip.title).font(.jf(.headline))
                    Text(Fmt.range(trip)).font(.jf(.footnote)).foregroundStyle(Color.jfMuted)
                }
            }
            .padding(.vertical, 6)
        }
        .tabViewSidebarBottomBar {
            Button { nav.openTrip = nil } label: { Label("Trips", systemImage: "chevron.backward") }
                .font(.jf(.body, .semibold))
                .padding(.vertical, 8)
        }
        .tabBarMinimizeBehavior(.onScrollDown)
        .onChange(of: nav.tab) { old, new in
            // The centre tab is an action, not a place: it opens Quick Add and stays where it was.
            if new == .add { nav.tab = old; nav.quickAdd = true } else { lastTab = new }
        }
        .sheet(isPresented: $nav.quickAdd, onDismiss: { nav.quickAddKind = nil; nav.quickAddInto = nil }) {
            QuickAddSheet(tripID: tripID)
        }
        .sheet(item: Binding(get: { nav.detail.map(IdentifiedID.init) }, set: { nav.detail = $0?.id })) { id in
            ItemDetailSheet(tripID: tripID, itemID: id.id)
        }
        .sheet(isPresented: Binding(get: { nav.welcome == tripID }, set: { if !$0 { nav.welcome = nil } })) { WelcomeSheet(tripID: tripID) }
        .task {
            // The first time a trip opens here and TravelRepo does not know who the person is, ask.
            guard !WelcomeSheet.seen(tripID), !DebugTools.flag("demo"), let bridge = TravelRepoBridge.all[tripID] else { return }
            if await bridge.me(login: account.login)?.person == nil { nav.welcome = tripID } else { WelcomeSheet.markSeen(tripID) }
        }
    }
}

extension TripTabs {
    private func pageTab(_ page: MorePage, _ title: LocalizedStringKey, _ symbol: String) -> some TabContent<TripTab> {
        Tab(title, systemImage: symbol, value: TripTab.page(page)) {
            NavigationStack {
                MorePageView(tripID: tripID, page: page)
                    .navigationDestination(for: MorePage.self) { MorePageView(tripID: tripID, page: $0) }
            }
        }
    }
}

struct IdentifiedID: Identifiable { let id: UUID }

/// The place the bar shows between New and More.
enum ThirdPlace: String, CaseIterable, Identifiable {
    case costs, map, bookings, tasks, packing
    var id: String { rawValue }
    static var current: ThirdPlace { ThirdPlace(rawValue: UserDefaults.standard.string(forKey: "bar.third") ?? "") ?? .costs }
    var title: LocalizedStringKey {
        switch self { case .costs: "Costs"; case .map: "Map"; case .bookings: "Bookings"; case .tasks: "Tasks"; case .packing: "Packing lists" }
    }
    var symbol: String {
        switch self { case .costs: "wallet.bifold"; case .map: "map"; case .bookings: "ticket"; case .tasks: "checklist"; case .packing: "backpack" }
    }
    var page: MorePage? {
        switch self { case .costs: .costs; case .map: nil; case .bookings: .bookings; case .tasks: .tasks; case .packing: .packing }
    }
}


extension Trip {
    /// What the share extension may offer of the trip.
    var shareSummary: SharedInbox.TripSummary {
        let plans = items.filter { $0.when.isScheduled }
        return .init(title: title, plans: plans.map(\.title), id: id, planTargets: plans.map { .init(id: $0.id, title: $0.title) }, bookings: bookings.map { .init(id: $0.id, title: $0.title) })
    }
}
