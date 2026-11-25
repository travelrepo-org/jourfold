import SwiftUI
import TravelRepoKit

/// What the app fetches from the internet, each off until the person says yes once (as in the .NET app):
/// the weather for the trip's places (TravelRepo's `fetch_weather`, from Open-Meteo) and the European Central Bank's
/// exchange rates (`fetch_exchange_rates`). Nothing about the trip leaves the phone for rates; for weather only the
/// places' positions and dates do.
enum Online {
    static var weatherOn: Bool {
        get { UserDefaults.standard.string(forKey: "weather.online") == "on" }
        set { UserDefaults.standard.set(newValue ? "on" : "off", forKey: "weather.online") }
    }
    static var ratesOn: Bool {
        get { UserDefaults.standard.string(forKey: "rates.online") == "on" }
        set { UserDefaults.standard.set(newValue ? "on" : "off", forKey: "rates.online") }
    }
    static var demo: Bool { DebugTools.flag("demo") }

    /// Fetches the newest rates when allowed and older than a day, then reads the trips again so costs convert.
    @MainActor
    static func updateRates(force: Bool = false, store: TripStore) async -> Bool {
        guard ratesOn, !demo else { return true }
        if !force, let fetched = Rates.fetched, Date().timeIntervalSince(fetched) < 86400 { return true }
        guard let any = TravelRepoBridge.all.values.first else { return false }
        do {
            _ = try await any.session.fetchExchangeRates()
            Rates.reload()
            for bridge in TravelRepoBridge.all.values { try? await bridge.read() }
            return true
        } catch { return false }
    }
}

extension TravelRepoBridge {
    /// Fetches the weather for the trip's places and days when weather is on. TravelRepo keeps a cache per trip and
    /// knows how long a forecast stays fresh, so calling this often costs nothing. False when a service failed.
    @discardableResult
    func updateWeather(refresh: Bool = false) async -> Bool {
        guard Online.weatherOn, !Online.demo, !weatherLoading else { return true }
        weatherLoading = true; store.travelRepoReads += 1
        defer { weatherLoading = false; store.travelRepoReads += 1 }
        do {
            let result = try await session.fetchWeather(refresh: refresh)
            var days: [DateComponents: DayWeather] = [:]
            for day in result["days"]?.array ?? [] {
                guard let date = Self.dayComponents(day["date"]?.string), let outlook = day["outlook"] else { continue }
                days[date] = DayWeather(outlook)
            }
            weather = days
            var outlooks: [UUID: [(date: String?, weather: DayWeather)]] = [:]
            var concerns: [UUID: [WeatherConcern]] = [:]
            for item in result["items"]?.array ?? [] {
                guard let id = item["id"]?.string.flatMap(UUID.init(uuidString:)) else { continue }
                outlooks[id] = (item["outlooks"]?.array ?? []).compactMap { o in DayWeather(o).map { (o["day"]?["date"]?.string, $0) } }
                let list = (item["concerns"]?.array ?? []).compactMap { c in c["kind"]?.string.map { WeatherConcern(kind: $0, severe: c["severe"]?.bool ?? false, value: c["value"]?.number ?? 0) } }
                if !list.isEmpty { concerns[id] = list }
            }
            weatherConcerns = concerns
            itemWeather = outlooks
            weatherSources = (result["sources"]?.array ?? []).compactMap { s in s["text"]?.string.map { ($0, s["url"]?.string) } }
            weatherProblems = (result["problems"]?.array ?? []).compactMap { $0["message"]?.string ?? $0.string }
            store.travelRepoReads += 1
            return weatherProblems.isEmpty
        } catch { weatherProblems = [Self.message(error)]; store.travelRepoReads += 1; return false }
    }
}

/// Weather that matters for a plan, as TravelRepo's weather advice finds it: thunderstorms, snow, wind (peak gust in
/// km/h), rain (mm), heat or cold (°C). Severe weather can cancel or endanger the plan.
struct WeatherConcern: Hashable {
    var kind: String
    var severe: Bool
    var value: Double

    var text: String {
        switch kind {
        case "thunderstorm": String(localized: "thunderstorms")
        case "snow": String(localized: "snow")
        case "wind": String(localized: "gusts up to \(Int(value)) km/h")
        case "rain": String(localized: "\(value.formatted(.number.precision(.fractionLength(0...1)))) mm of rain")
        case "heat": String(localized: "up to \(Int(value.rounded())) °C")
        case "cold": String(localized: "down to \(Int(value.rounded())) °C")
        default: kind
        }
    }
}

extension DayWeather {
    /// A day of TravelRepo's weather: a forecast, a trend or what is usual on that day (climate).
    init?(_ outlook: JSONValue) {
        let kind = outlook["kind"]?.string ?? "forecast"
        let fetched = outlook["fetched"]?.string.flatMap { ISO8601DateFormatter().date(from: $0) }
        if let day = outlook["day"], day != .null {
            let condition = day["condition"]?.string ?? "cloudy"
            self.init(symbol: Self.symbol(condition), high: Int((day["max_c"]?.number ?? 0).rounded()), low: Int((day["min_c"]?.number ?? 0).rounded()),
                      rain: Int(day["precipitation_chance"]?.number ?? 0), summary: Self.summary(condition), kind: kind, fetched: fetched)
        } else if let climate = outlook["climate"], climate != .null {
            self.init(symbol: "chart.bar.xaxis", high: Int((climate["max_c"]?.number ?? 0).rounded()), low: Int((climate["min_c"]?.number ?? 0).rounded()),
                      rain: Int(((climate["rain_day_share"]?.number ?? 0) * 100).rounded()), summary: "Usual for the season", kind: "climate", fetched: fetched)
        } else if let hours = outlook["hours"]?.array, !hours.isEmpty {
            // A plan in the coming days has the hours it takes: from the lowest to the highest, with the worst of them.
            let worst = ["clear", "partly_cloudy", "cloudy", "fog", "drizzle", "rain", "heavy_rain", "snow", "thunderstorm"]
            let condition = hours.compactMap { $0["condition"]?.string }.max { (worst.firstIndex(of: $0) ?? 0) < (worst.firstIndex(of: $1) ?? 0) } ?? "cloudy"
            let degrees = hours.compactMap { $0["temperature_c"]?.number }
            guard let low = degrees.min(), let high = degrees.max() else { return nil }
            self.init(symbol: Self.symbol(condition), high: Int(high.rounded()), low: Int(low.rounded()), rain: Int(hours.compactMap { $0["precipitation_chance"]?.number }.max() ?? 0), summary: Self.summary(condition), kind: kind, fetched: fetched)
            hourly = true
        } else { return nil }
    }

    static func symbol(_ condition: String) -> String {
        switch condition {
        case "clear": "sun.max.fill"
        case "partly_cloudy": "cloud.sun.fill"
        case "cloudy": "cloud.fill"
        case "fog": "cloud.fog.fill"
        case "drizzle": "cloud.drizzle.fill"
        case "rain": "cloud.rain.fill"
        case "heavy_rain": "cloud.heavyrain.fill"
        case "snow": "cloud.snow.fill"
        case "thunderstorm": "cloud.bolt.rain.fill"
        default: "cloud.fill"
        }
    }

    static func summary(_ condition: String) -> LocalizedStringResource {
        switch condition {
        case "clear": "Sunny"
        case "partly_cloudy": "Partly cloudy"
        case "cloudy": "Cloudy"
        case "fog": "Fog"
        case "drizzle": "Drizzle"
        case "rain": "Rain"
        case "heavy_rain": "Heavy rain"
        case "snow": "Snow"
        case "thunderstorm": "Thunderstorms"
        default: "Cloudy"
        }
    }

    /// The weather for a day of the trip: the demo's made-up weather, or TravelRepo's when weather is on.
    @MainActor
    static func of(_ trip: Trip, _ day: Date) -> DayWeather? {
        if Online.demo { return SampleData.weather(trip, day) }
        // Read through the observed count, so views draw again once the weather is there.
        _ = TravelRepoBridge.all[trip.id]?.store.travelRepoReads
        return TravelRepoBridge.all[trip.id]?.weather[trip.calendar.ymd(day)]
    }
}

/// Asks once whether to show the weather, saying what is sent.
struct WeatherOffer: View {
    let tripID: UUID
    /// Answered: on, or not now; Settings can change it.
    @State private var on = UserDefaults.standard.string(forKey: "weather.online") != nil
    var body: some View {
        if !on, !Online.demo {
            VStack(alignment: .leading, spacing: 8) {
                Label("Show the weather?", systemImage: "cloud.sun").font(.jf(.body, .semibold))
                Text("Jourfold asks Open-Meteo for the forecast at the trip's places. Only their positions and the dates are sent.")
                    .font(.jf(.footnote)).foregroundStyle(Color.jfMuted)
                Button("Show weather") {
                    Online.weatherOn = true; on = true
                    Task { await TravelRepoBridge.all[tripID]?.updateWeather() }
                }
                .buttonStyle(.bordered)
                Button("Not now") { UserDefaults.standard.set("off", forKey: "weather.online"); on = true }.font(.jf(.footnote))
            }
            .padding(14)
            .frame(maxWidth: .infinity, alignment: .leading)
            .background(Color.jfSurface, in: RoundedRectangle(cornerRadius: 14, style: .continuous))
            .overlay(RoundedRectangle(cornerRadius: 14, style: .continuous).strokeBorder(Color.jfLine))
        }
    }
}

/// The weather of a plan: what may disturb it, the outlook of its days, as of when, who it comes from, and asking again.
struct PlanWeather: View {
    let tripID: UUID
    let item: UUID
    @Environment(TripStore.self) private var store
    @State private var busy = false
    @State private var failed = false

    var body: some View {
        let _ = store.travelRepoReads
        let bridge = TravelRepoBridge.all[tripID]
        let outlooks = bridge?.itemWeather[item] ?? [], concerns = bridge?.weatherConcerns[item] ?? []
        if !outlooks.isEmpty || !concerns.isEmpty {
            Section {
                if !concerns.isEmpty {
                    Label { Text(concerns.map(\.text).joined(separator: ", ")) } icon: { Image(systemName: concerns.contains(where: \.severe) ? "exclamationmark.triangle" : "cloud.rain").foregroundStyle(Color.jfWarning) }
                }
                ForEach(Array(outlooks.enumerated()), id: \.offset) { _, o in
                    let w = o.weather
                    Label {
                        VStack(alignment: .leading, spacing: 1) {
                            Text(verbatim: "\(w.hourly ? (w.low == w.high ? "\(w.high)°" : "\(w.low)–\(w.high)°") : "\(w.high)° / \(w.low)°") · \(String(localized: w.summary))\(w.kind == "climate" ? "" : " · \(w.rain) %")")
                            let kind = w.kind == "trend" ? String(localized: "Trend, may still change") : nil
                            let detail = [o.date.map { CostsView.day($0, weekday: true) }, kind].compactMap { $0 }.joined(separator: " · ")
                            if !detail.isEmpty { Text(detail).font(.jf(.footnote)).foregroundStyle(Color.jfMuted) }
                        }
                    } icon: { Image.weather(w.symbol).accessibilityHidden(true) }
                }
                Button { Task { busy = true; failed = !(await bridge?.updateWeather(refresh: true) ?? false); busy = false } } label: { Label(busy ? "Updating…" : "Update the weather", systemImage: "arrow.clockwise") }.disabled(busy)
                if failed { Text("The weather could not be updated.").font(.jf(.footnote)).foregroundStyle(Color.jfDanger) }
            } header: { Overline("Weather") } footer: {
                VStack(alignment: .leading) {
                    if let at = outlooks.compactMap(\.weather.fetched).max() { Text("As of \(at.formatted(date: .abbreviated, time: .shortened))") }
                    ForEach(Array((bridge?.weatherSources ?? []).enumerated()), id: \.offset) { _, s in
                        if let url = s.url.flatMap(URL.init(string:)) { Link(s.text, destination: url) } else { Text(s.text) }
                    }
                }
            }
        }
    }
}

/// Says that the weather could not be loaded, with a way to try again; nothing while it is off or fine.
struct WeatherProblem: View {
    let tripID: UUID
    @Environment(TripStore.self) private var store
    @State private var busy = false
    var body: some View {
        let _ = store.travelRepoReads
        if Online.weatherOn, !Online.demo, let bridge = TravelRepoBridge.all[tripID], bridge.weatherLoading, bridge.weather.isEmpty {
            Label("Loading the weather…", systemImage: "cloud").font(.jf(.footnote)).foregroundStyle(Color.jfMuted)
        } else if Online.weatherOn, !Online.demo, let bridge = TravelRepoBridge.all[tripID], !bridge.weatherProblems.isEmpty {
            HStack {
                Label("The weather could not be loaded completely.", systemImage: "cloud.slash").font(.jf(.footnote)).foregroundStyle(Color.jfMuted)
                Spacer()
                Button(busy ? "Updating…" : "Try again") { Task { busy = true; await bridge.updateWeather(); busy = false } }.font(.jf(.footnote, .semibold)).disabled(busy)
            }
        }
    }
}
