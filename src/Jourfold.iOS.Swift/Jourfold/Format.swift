import Foundation

/// Times are always shown in the zone of the place where they happen, like the live app.
enum Fmt {
    /// Digits after the decimal point of a currency (JPY none, KWD three).
    static func minorUnits(_ currency: String) -> Int {
        let f = NumberFormatter(); f.numberStyle = .currency; f.currencyCode = currency
        return f.maximumFractionDigits
    }

    static func time(_ date: Date, _ zone: TimeZone) -> String {
        var f = Date.FormatStyle(date: .omitted, time: .shortened)
        f.timeZone = zone
        return date.formatted(f)
    }

    static func day(_ date: Date, _ zone: TimeZone, weekday: Bool = true) -> String {
        var f = weekday ? Date.FormatStyle().weekday(.wide).day().month(.wide) : Date.FormatStyle().day().month(.wide)
        f.timeZone = zone
        return date.formatted(f)
    }

    static func shortDay(_ date: Date, _ zone: TimeZone) -> String {
        var f = Date.FormatStyle().weekday(.abbreviated).day().month(.abbreviated)
        f.timeZone = zone
        return date.formatted(f)
    }

    static func weekdayShort(_ date: Date, _ zone: TimeZone) -> String {
        var f = Date.FormatStyle().weekday(.abbreviated)
        f.timeZone = zone
        return date.formatted(f)
    }

    static func dayNumber(_ date: Date, _ zone: TimeZone) -> String {
        var f = Date.FormatStyle().day()
        f.timeZone = zone
        return date.formatted(f)
    }

    static func range(_ trip: Trip) -> String {
        let cal = trip.calendar
        guard let s = cal.date(from: trip.start), let e = cal.date(from: trip.end) else { return "" }
        var f = Date.IntervalFormatStyle(date: .abbreviated, time: .omitted)
        f.timeZone = cal.timeZone
        return (s..<e.addingTimeInterval(1)).formatted(f)
    }

    static func duration(_ seconds: TimeInterval) -> String {
        Duration.seconds(seconds).formatted(.units(allowed: [.hours, .minutes], width: .abbreviated))
    }

    static func money(_ m: Money) -> String {
        m.value.formatted(.currency(code: m.currency))
    }

    static func money(_ value: Decimal, _ currency: String) -> String {
        value.formatted(.currency(code: currency))
    }

    static func city(_ zone: TimeZone) -> String {
        zone.identifier.split(separator: "/").last.map { $0.replacingOccurrences(of: "_", with: " ") } ?? zone.identifier
    }

    /// "16:00–18:00", "Afternoon", "All day", "about 19:00".
    static func when(_ w: When) -> String {
        switch w {
        case .exact(let s, _, let e, _):
            let st = time(s, w.zone), et = time(e, w.endZone)
            var c = Calendar(identifier: .gregorian); c.timeZone = w.zone
            var c2 = Calendar(identifier: .gregorian); c2.timeZone = w.endZone
            let sd = c.dateComponents([.year, .month, .day], from: s), ed = c2.dateComponents([.year, .month, .day], from: e)
            let n = c.dateComponents([.day], from: c.date(from: sd)!, to: c.date(from: ed)!).day ?? 0
            return n == 0 ? "\(st)–\(et)" : "\(st)–\(et) +\(n)"
        case .approximate(let s, _, _):
            return String(localized: "about \(time(s, w.zone))")
        case .dayPart(_, let p, _):
            return String(localized: p.label)
        case .allDay:
            return String(localized: "All day")
        case .unscheduled(let m):
            if let m { return Fmt.duration(TimeInterval(m * 60)) }
            return String(localized: "No time yet")
        }
    }
}

/// Converts with a fixed set of rates "downloaded" on the demo day, like the live app's cached rates.
/// The exchange rates TravelRepo converts with, from the file it keeps (the European Central Bank's reference rates,
/// fetched only when the person allowed it; the demo uses the sample's fixed rates). Without rates nothing is
/// converted and costs stay per currency.
@MainActor
enum Rates {
    /// The demo's fixed rates of 9 January 2027.
    nonisolated static let demo: [String: Decimal] = ["EUR": 1, "JPY": Decimal(string: "163.9")!, "NZD": Decimal(string: "1.82")!, "CAD": Decimal(string: "1.49")!, "USD": Decimal(string: "1.08")!]
    private(set) static var perEUR: [String: Decimal] = load().rates
    private(set) static var date: Date? = load().date
    /// When the file was last written, for "downloaded …".
    static var fetched: Date? { (try? FileManager.default.attributesOfItem(atPath: TripLibrary.rates.path))?[.modificationDate] as? Date }
    static var available: Bool { perEUR.count > 1 }

    static func reload() { let l = load(); perEUR = l.rates; date = l.date }

    private static func load() -> (rates: [String: Decimal], date: Date?) {
        guard let data = try? Data(contentsOf: TripLibrary.rates), let json = try? JSONSerialization.jsonObject(with: data) as? [String: Any],
              let per = json["PerEuro"] as? [String: Any] else { return (["EUR": 1], nil) }
        var rates: [String: Decimal] = ["EUR": 1]
        for (k, v) in per { if let n = v as? NSNumber { rates[k] = Decimal(string: n.stringValue) } }
        let f = DateFormatter(); f.dateFormat = "yyyy-MM-dd"; f.locale = Locale(identifier: "en_US_POSIX")
        return (rates, (json["Date"] as? String).flatMap(f.date(from:)))
    }

    static func convert(_ m: Money, to currency: String) -> Decimal {
        guard let from = perEUR[m.currency], let to = perEUR[currency] else { return m.value }
        var result = m.value / from * to
        var rounded = Decimal()
        NSDecimalRound(&rounded, &result, Fmt.minorUnits(currency), .plain)
        return rounded
    }

}
