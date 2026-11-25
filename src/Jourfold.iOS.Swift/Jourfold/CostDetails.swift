import Foundation
import TravelRepoKit

/// TravelRepo's `get_costs` in the trip's currency: amounts with what was charged, who owes whom and what is only
/// estimated. The page shows these numbers and works out none of its own rules (splits, rates, settling).
nonisolated struct CostDetails: Decodable {
    nonisolated struct Sum: Decodable, Hashable { let amount: String; let currency: String }
    nonisolated struct Debt: Decodable, Hashable {
        nonisolated struct PaidIn: Decodable, Hashable { let amount: Decimal; let currency: String; let estimated: Bool? }
        let from: String, to: String
        let amount: Decimal, paid: Decimal, open: Decimal
        let paid_on: String?
        let paid_in: PaidIn?
    }
    nonisolated struct Part: Decodable, Hashable { let person: String; let amount: Decimal; let value: Decimal? }
    nonisolated struct Division: Decodable, Hashable { let method: String?; let parts: [Part]; let debts: [Debt]; let idea: Bool?; let open: Decimal?; let issues: [String]? }
    nonisolated struct Charged: Decodable, Hashable { let amount: Decimal; let currency: String; let rate: Decimal; let date: String? }
    nonisolated struct Expense: Decodable, Identifiable, Hashable {
        let id: String, title: String
        let amount: String?, currency: String?
        let estimated: Bool?
        let belongs_to: [String]
        let category: String
        let date: String?, due: String?
        let split: Division
        let charged: Charged?
        var planned: Bool { estimated == true }
        var idea: Bool { split.idea == true }
        var value: Decimal { amount.flatMap { Decimal(string: $0, locale: Locale(identifier: "en_US_POSIX")) } ?? 0 }
        var code: String { currency ?? "XXX" }
        var uuid: UUID? { UUID(uuidString: id) }
        var paidBack: Bool { !split.debts.isEmpty && split.debts.allSatisfy { $0.open == 0 } }
    }
    nonisolated struct Total: Decodable, Hashable { let currency: String; let paid: Decimal; let estimated: Decimal }
    nonisolated struct Balance: Decodable { let person: String; let currency: String; let net: Decimal; let estimated: Bool? }
    nonisolated struct Transfer: Decodable { let from: String; let to: String; let currency: String; let amount: Decimal; let estimated: Bool? }
    nonisolated struct Problem: Decodable { let id: String; let title: String }
    nonisolated struct Budget: Decodable { let amount: Decimal; let currency: String; let spent: Decimal; let planned: Decimal; let left: Decimal; let estimated: Bool? }
    nonisolated struct Limited: Decodable { let id: String; let title: String; let category: String?; let person: String?; let collection: String?; let amount: Decimal; let currency: String; let spent: Decimal; let planned: Decimal; let left: Decimal; let estimated: Bool? }
    nonisolated struct Category: Decodable { let category: String; let currency: String; let amount: Decimal; let estimated: Bool? }
    nonisolated struct PaidIn: Decodable { let currency: String; let exact: Decimal; let estimated: Decimal; let total: Decimal; let without_charged_amount: Int; let not_converted: [Sum] }
    let totals: [Total]
    let expenses: [Expense]
    let balances: [Balance]
    let settle_up: [Transfer]
    let needs_attention: [Problem]
    let budget: Budget?
    let budgets: [Limited]
    let by_category: [Category]
    let paid_in: PaidIn?
}

extension TravelRepoBridge {
    /// The costs in the trip's currency; `people` limits the categories to their parts.
    func costDetails(for people: Set<UUID> = []) async throws -> CostDetails {
        let currency = travel["currency"]?.string ?? lastRead.currency
        return try await session.call("get_costs", ["in": .string(currency), "for": .array(people.map { .string(Self.key($0)) })], as: CostDetails.self)
    }

    /// Runs a cost command after what still waits was written, then reads again; problems are shown.
    @discardableResult
    func changeCosts(_ action: () async throws -> Void) async -> Bool {
        await flush()
        var done = true
        do { try await action(); noteChange() } catch { store.problem = Self.message(error); done = false }
        try? await read()
        return done
    }

    private var today: String {
        let d = Self.today(in: lastRead.zone)
        return String(format: "%04d-%02d-%02d", d.year ?? 0, d.month ?? 1, d.day ?? 1)
    }

    /// The one person with this name; get_costs names people, and two of the same name must not be mixed up.
    func personNamed(_ name: String) throws -> UUID {
        let found = lastRead.people.filter { $0.name == name }
        guard found.count == 1 else { throw AppError.message(String(localized: "“\(name)” is not clear, because several people have that name. Give them different names first.")) }
        return found[0].id
    }

    /// Marks what people owe for an expense as paid back today, or takes the mark back.
    func markPaidBack(_ expense: UUID, person: UUID?, _ paid: Bool) async throws {
        var arguments: [String: JSONValue] = ["expense": .string(Self.key(expense)), "paid": .bool(paid)]
        if let person { arguments["person"] = .string(Self.key(person)) }
        if paid { arguments["date"] = .string(today) }
        _ = try await session.call("mark_paid", arguments)
    }

    /// Marks a payment that was still to pay as paid today, by the user when it names nobody yet.
    func recordPayment(_ expense: UUID) async throws {
        var arguments: [String: JSONValue] = ["expense": .string(Self.key(expense)), "date": .string(today)]
        if let me = store.me[tripID], (entities[Self.key(expense)]?["paid_by"]?.array ?? []).isEmpty { arguments["paid_by"] = [.string(Self.key(me))] }
        _ = try await session.call("record_payment", arguments)
    }
}
