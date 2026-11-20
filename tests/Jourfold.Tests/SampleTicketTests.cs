using Jourfold.Application;
using NodaTime;
using TravelRepo.Core;
using TravelRepo.Documents;
using Xunit;
namespace Jourfold.Tests;

/// <summary>
/// The sample trip's tickets, one in each form: Wallet passes for the flight and the Shinkansen, a PDF for teamLab, and
/// a picture for the deer crackers in Nara.
/// </summary>
public sealed class SampleTicketTests
{
    [Fact]
    public void TheSampleTripHasATicketInEachForm()
    {
        var (edits, resources) = SampleTrip.Build(Entity.CreateTrip("Spring in Japan", "en", "Asia/Tokyo"), new LocalDate(2027, 5, 14));
        var entities = edits.Select(e => e.Value!).ToArray();
        byte[] Bytes(Entity document) => resources.Single(r => r.Path.EndsWith(document.Data["blob"]!["hash"]!.ToString(), StringComparison.Ordinal)).Bytes!;
        foreach (var (title, file, media) in new[]
        {
            ("Shinkansen to Kyoto", "Shinkansen to Kyoto.pkpass", Tickets.WalletPass),
            ("Flight to Tokyo", "Boarding pass LH 716.pkpass", Tickets.WalletPass),
            ("teamLab Planets", "teamLab Planets e-ticket.pdf", "application/pdf"),
            ("Nara deer park", "Deer crackers.png", "image/png"),
        })
        {
            var item = entities.Single(e => e.Title == title && e.Type == "schedule_item");
            var document = entities.Single(e => e.Type == "document" && e.Title == file);
            Assert.Equal(media, document.Data["media_type"]!.ToString());
            Assert.True(Tickets.IsTicket(media));
            Assert.Contains(document.Id.ToString(), item.Data["content"]!.ToJsonString());
            var bytes = Bytes(document);
            switch (media)
            {
                case Tickets.WalletPass: Assert.NotNull(WalletPass.Read(bytes)); break;
                case "application/pdf": Assert.StartsWith("%PDF-1.4", System.Text.Encoding.ASCII.GetString(bytes, 0, 8)); Assert.EndsWith("%%EOF\n", System.Text.Encoding.ASCII.GetString(bytes)); break;
                default: Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, bytes[..4]); break;
            }
        }
        // teamLab keeps its notes and link; the ticket comes after them.
        Assert.Equal(3, entities.Single(e => e.Title == "teamLab Planets" && e.Type == "schedule_item").Data["content"]!.AsArray().Count);
    }

    [Fact]
    public void ThePassSpeaksTheAppsLanguageAndItsCodeSaysHello()
    {
        var bytes = SampleTickets.Shinkansen("08:30", "2027-05-18T08:30:00+09:00");
        var english = WalletPass.Read(bytes, "en")!; var german = WalletPass.Read(bytes, "de")!;
        Assert.Equal(("Shinkansen to Kyoto", "Shinkansen nach Kyoto"), (english.Description, german.Description));
        Assert.Contains(english.Fields, f => f is { Label: "From", Value: "Tokyo" });
        Assert.Contains(german.Fields, f => f is { Label: "Von", Value: "Tokyo" });
        Assert.Contains(german.Fields, f => f.Group == "back" && f.Value.Contains("Platz 12E", StringComparison.Ordinal));
        Assert.Equal("qr", english.Barcode!.Format);
        Assert.Contains("not valid for travel", english.Barcode.Message, StringComparison.Ordinal);
        Assert.Contains("Mount Fuji", english.Barcode.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheFlightIsABoardingPassThePhoneShowsItself()
    {
        var bytes = SampleTickets.Flight("2027-05-14T13:20:00+02:00");
        var english = WalletPass.Read(bytes, "en")!; var german = WalletPass.Read(bytes, "de")!;
        Assert.Equal(("Flight to Tokyo", "Flug nach Tokyo"), (english.Description, german.Description));
        Assert.Contains(english.Fields, f => f is { Label: "Frankfurt", Value: "FRA" });
        Assert.Contains(german.Fields, f => f is { Label: "Plätze", Value: "32A, 32C" });
        Assert.Contains("not valid for travel", english.Barcode!.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheTicketsAreTheSameEachTime()
    {
        Assert.Equal(SampleTickets.DeerCrackers(), SampleTickets.DeerCrackers());
        Assert.Equal(SampleTickets.Flight("x"), SampleTickets.Flight("x"));
        Assert.Equal(SampleTickets.Shinkansen("08:30", "x"), SampleTickets.Shinkansen("08:30", "x"));
    }
}
