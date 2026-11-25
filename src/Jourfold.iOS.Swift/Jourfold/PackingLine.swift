import Foundation
import SwiftUI

/// One line typed into a packing list, such as "2x Charger #Electronics @all": the title, the group ("_" is a
/// space), the quantity and who brings it. Names that match nobody or several people are kept apart; such a line is
/// not added. The same shorthand as in the .NET app; the item itself is added by TravelRepo.
struct PackingLine: Equatable {
    var title = "", group: String?, quantity: String?
    var people: [UUID] = [], everyone = false
    var unknown: [String] = [], ambiguous: [String] = []
    var canAdd: Bool { !title.isEmpty && unknown.isEmpty && ambiguous.isEmpty }

    static let everyoneWords = ["all", "alle", "everyone"]

    init(_ text: String, people: [Person]) {
        var words: [String] = []
        for word in text.split(separator: " ").map(String.init) {
            if word.count > 1, word.hasPrefix("#") { group = word.dropFirst().replacingOccurrences(of: "_", with: " "); continue }
            if word.count > 1, word.hasPrefix("@") {
                let name = String(word.dropFirst())
                if Self.everyoneWords.contains(name.lowercased()) { everyone = true; continue }
                let found = Self.matches(name, people)
                if found.count == 1 { if !self.people.contains(found[0].id) { self.people.append(found[0].id) } }
                else if found.isEmpty { unknown.append(name.replacingOccurrences(of: "_", with: " ")) }
                else { ambiguous.append(name.replacingOccurrences(of: "_", with: " ")) }
                continue
            }
            if quantity == nil, let m = word.wholeMatch(of: /(?:(\d{1,4})[xX×]|[xX×](\d{1,4}))/) { quantity = String(m.1 ?? m.2 ?? ""); continue }
            words.append(word)
        }
        title = words.joined(separator: " ")
    }

    static func matches(_ name: String, _ people: [Person]) -> [Person] {
        let wanted = name.replacingOccurrences(of: "_", with: " ")
        func same(_ a: String, _ b: String) -> Bool { a.compare(b, options: .caseInsensitive, locale: .current) == .orderedSame }
        let full = people.filter { same($0.name, wanted) }
        return full.isEmpty ? people.filter { same(first($0), wanted) } : full
    }

    private static func first(_ p: Person) -> String { p.name.split(separator: " ").first.map(String.init) ?? p.name }

    /// How "@" names a person: the first name when nobody else has it, otherwise the full name.
    static func mention(_ p: Person, _ people: [Person]) -> String {
        matches(first(p), people).count == 1 ? first(p) : p.name.replacingOccurrences(of: " ", with: "_")
    }

    /// What to offer for the last word when it starts with "@" or "#": the label and what replaces the word.
    static func suggestions(_ text: String, people: [Person], groups: [String]) -> [(label: String, insert: String)] {
        guard let word = text.split(separator: " ", omittingEmptySubsequences: false).last.map(String.init), let sign = word.first, sign == "@" || sign == "#" else { return [] }
        let prefix = word.dropFirst().replacingOccurrences(of: "_", with: " ")
        func fits(_ s: String) -> Bool { prefix.isEmpty || ([s] + s.split(separator: " ").map(String.init)).contains { $0.range(of: prefix, options: [.caseInsensitive, .anchored], locale: .current) != nil } }
        var found: [(String, String)] = []
        if sign == "@" {
            if fits(String(localized: "Everyone")) || everyoneWords.contains(where: fits) { found.append((String(localized: "Everyone"), "@all")) }
            found += people.filter { fits($0.name) }.map { ($0.name, "@" + mention($0, people)) }
        } else {
            found += groups.filter(fits).map { ($0, "#" + $0.replacingOccurrences(of: " ", with: "_")) }
        }
        return found.filter { $0.1.caseInsensitiveCompare(word) != .orderedSame }.prefix(8).map { (label: $0.0, insert: $0.1) }
    }

    static func accept(_ text: String, _ insert: String) -> String {
        let start = text.lastIndex(of: " ").map { text.index(after: $0) } ?? text.startIndex
        return text[..<start] + insert + " "
    }
}

/// The suggestions and the problem of a line being typed.
struct PackingLineHints: View {
    @Binding var text: String
    let trip: Trip
    let addPeople: () -> Void
    var body: some View {
        let line = PackingLine(text, people: trip.people)
        let groups = Array(Set(trip.packing.flatMap(\.items).map(\.group).filter { !$0.isEmpty })).sorted { $0.localizedStandardCompare($1) == .orderedAscending }
        let offers = PackingLine.suggestions(text, people: trip.people, groups: groups)
        if !offers.isEmpty {
            ScrollView(.horizontal, showsIndicators: false) {
                HStack { ForEach(offers, id: \.insert) { o in Button(o.label) { text = PackingLine.accept(text, o.insert) }.buttonStyle(.bordered).controlSize(.small) } }
            }
        }
        if let name = line.ambiguous.first {
            Label("Several people are called \(name). Write the full name with _ for spaces.", systemImage: "exclamationmark.triangle").font(.jf(.footnote)).foregroundStyle(Color.jfWarning)
        }
        // While the name is still being typed, its suggestions are help enough.
        if !line.unknown.isEmpty, offers.isEmpty {
            Label("Nobody here is called \(line.unknown.joined(separator: ", ")).", systemImage: "exclamationmark.triangle").font(.jf(.footnote)).foregroundStyle(Color.jfWarning)
            if !line.title.isEmpty, line.ambiguous.isEmpty { Button("Add \(line.unknown.joined(separator: ", ")) as a traveller and add the item", action: addPeople) }
        }
    }
}
