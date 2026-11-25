import SwiftUI

/// What opens from a plan's details or a search, as one sheet on the page itself. Sheets that hang on parts of a list
/// are given to every row; they get in each other's way and can close the page they came from.
enum TripSheet: Identifiable {
    case booking(String), expense(String), payment(related: String, title: String), task(String), document(TripDocument), note(UUID), packing(PackingItem), place(UUID)

    var id: String {
        switch self {
        case .booking(let id): "b" + id
        case .expense(let id): "e" + id
        case .payment(let related, _): "p" + related
        case .task(let id): "t" + id
        case .document(let d): "d\(d.id)"
        case .note(let id): "n\(id)"
        case .packing(let i): "k\(i.id)"
        case .place(let id): "l\(id)"
        }
    }

    @ViewBuilder
    func view(_ tripID: UUID) -> some View {
        switch self {
        case .booking(let id): BookingSheet(tripID: tripID, bookingID: id)
        case .expense(let id): ExpenseSheet(tripID: tripID, expenseID: id)
        case .payment(let related, let title): PaymentSheet(tripID: tripID, related: related, title: title)
        case .task(let id): TaskEditor(tripID: tripID, taskID: id)
        case .document(let d): DocumentView(tripID: tripID, document: d.id, name: d.name)
        case .note(let id): NoteSheet(tripID: tripID, noteID: id)
        case .packing(let item): PackingItemEditor(tripID: tripID, item: item)
        case .place(let id): NavigationStack { PlaceDetail(tripID: tripID, placeID: id) }
        }
    }
}
