using TravelRepo.Core;

namespace Jourfold.Mobile;

/// <summary>
/// The tickets of a plan: documents attached to it or to its booking that can be shown at a gate or to a conductor,
/// such as a boarding pass as PDF, image or Wallet pass (.pkpass). Mails and other files are not tickets.
/// </summary>
public static class Tickets
{
    public const string WalletPass = "application/vnd.apple.pkpass";

    public static IReadOnlyList<Entity> For(TripSnapshot trip, Entity item)
    {
        var booking = Views.DetailView.Booking(trip, item);
        return TripCommands.AttachedDocuments(trip, item).Concat(booking is null || booking.Id == item.Id ? [] : TripCommands.AttachedDocuments(trip, booking))
            .Where(d => IsTicket(d.Data["media_type"]?.ToString())).DistinctBy(d => d.Id).ToArray();
    }

    public static bool IsTicket(string? mediaType) => mediaType is "application/pdf" or WalletPass || mediaType?.StartsWith("image/", StringComparison.Ordinal) == true;
}
