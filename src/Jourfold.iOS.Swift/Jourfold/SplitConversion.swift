import SwiftUI

/// When the way an expense is split changes, what was entered is carried over, so nobody types it again. Only the numbers
/// in the fields; what each person owes comes from TravelRepo.
enum SplitConversion {
    static func convert(_ values: [Decimal], from: String, to: String, total: Decimal?, digits: Int) -> [Decimal]? {
        guard !values.isEmpty, from != to, to != "equal" else { return nil }
        let count = Decimal(values.count), sum = values.reduce(0, +)
        let parts: [Decimal]
        if from == "equal" { parts = values.map { _ in 1 / count } } else if sum > 0 { parts = values.map { $0 / sum } } else { return nil }
        switch to {
        case "share": return from == "equal" ? values.map { _ in 1 } : values
        case "percent": return exact(parts, whole: 100, digits: 2)
        case "amount": return total.map { exact(parts, whole: $0, digits: digits) }
        default: return nil
        }
    }

    /// Rounded down, and what is left over goes to the first, unit by unit, so the fields add up.
    private static func exact(_ parts: [Decimal], whole: Decimal, digits: Int) -> [Decimal] {
        var out = parts.map { part in var exact = part * whole, down = Decimal(); NSDecimalRound(&down, &exact, digits, .down); return down }
        let unit = Decimal(sign: .plus, exponent: -digits, significand: 1)
        var left = whole - out.reduce(0, +), i = 0
        while left >= unit { out[i % out.count] += unit; left -= unit; i += 1 }
        if left > 0 { out[0] += left }
        return out
    }
}

extension Binding where Value == String {
    /// The method of a split, which converts the fields as it changes.
    @MainActor
    func converting(_ values: Binding<[UUID: String]>, people: [UUID], total: Decimal?, code: String) -> Binding<String> {
        Binding(get: { wrappedValue }, set: { new in
            let old = wrappedValue
            let f = NumberFormatter(); f.numberStyle = .currency; f.currencyCode = code
            let now = people.map { Decimal(string: (values.wrappedValue[$0] ?? "").replacingOccurrences(of: ",", with: "."), locale: Locale(identifier: "en_US_POSIX")) ?? 0 }
            if let converted = SplitConversion.convert(now, from: old, to: new, total: total, digits: f.maximumFractionDigits) {
                for (id, v) in zip(people, converted) { values.wrappedValue[id] = v.formatted(.number.grouping(.never)) }
            }
            wrappedValue = new
        })
    }
}
