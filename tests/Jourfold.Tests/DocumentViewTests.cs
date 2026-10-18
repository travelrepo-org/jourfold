using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Jourfold.Desktop;
using Jourfold.Infrastructure;
using MimeKit;
using TravelRepo.Core;
using TravelRepo.Documents;
using TravelRepo.Git;
using TravelRepo.Repository;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;
using Xunit;
namespace Jourfold.Tests;

/// <summary>Confirmations: e-mails and PDFs attached to bookings and shown in Jourfold's document window.</summary>
public sealed class DocumentViewTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "jourfold-documents-" + Guid.NewGuid());
    public void Dispose() => TestFiles.Delete(root);

    private static byte[] Pdf(params string[] pages)
    {
        var builder = new PdfDocumentBuilder(); var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        foreach (var text in pages) builder.AddPage(UglyToad.PdfPig.Content.PageSize.A4).AddText(text, 14, new UglyToad.PdfPig.Core.PdfPoint(60, 760), font);
        return builder.Build();
    }

    private string Confirmation()
    {
        var message = new MimeMessage { Subject = "Booking 4711 confirmed", Date = new DateTimeOffset(2026, 10, 2, 9, 30, 0, TimeSpan.FromHours(2)) };
        message.From.Add(new MailboxAddress("Lakeview Lodge", "bookings@lodge.example")); message.To.Add(new MailboxAddress("Alex", "alex@example.invalid"));
        var body = new BodyBuilder { HtmlBody = "<p>Check-in from 15:00.</p><img src=\"https://tracker.example/p.gif\">" };
        body.Attachments.Add("ticket.pdf", Pdf("Ticket 4711", "Second page"), new MimeKit.ContentType("application", "pdf"));
        message.Body = body.ToMessageBody();
        var path = Path.Combine(root, "confirmation.eml"); Directory.CreateDirectory(root);
        using (var file = File.Create(path)) message.WriteTo(file);
        return path;
    }

    private async Task<string> Trip()
    {
        var repository = new TravelRepository(Path.Combine(root, "trip")); await repository.InitializeAsync(Entity.CreateTrip("New Zealand", "en", "Pacific/Auckland"));
        await new GitRepository(repository, new GitCliBackend()).InitializeAsync("Test", "test@example.invalid");
        return repository.Root;
    }

    [Fact]
    public void PdfiumRendersPagesAndFindsText()
    {
        Assert.True(PdfiumDocument.IsAvailable);
        using var pdf = PdfiumDocument.Open(Pdf("Ticket 4711", "Lake view, ticket inside"));
        Assert.Equal(2, pdf.PageCount);
        var (width, height) = pdf.PageSize(0); Assert.InRange(width, 590, 600); Assert.InRange(height, 835, 845);
        var page = pdf.Render(0, 0.5);
        Assert.Equal((int)Math.Round(width * 0.5), page.Width); Assert.Equal(page.Stride * page.Height, page.Pixels.Length);
        Assert.Contains(page.Pixels, b => b < 200);
        var matches = pdf.Find("ticket");
        Assert.Equal([0, 1], matches.Select(m => m.Page).ToArray());
        Assert.All(matches, m => Assert.All(m.Rectangles, r => Assert.True(r.Top < r.Bottom && r.Top < 200)));
        Assert.Throws<InvalidDataException>(() => PdfiumDocument.Open("not a pdf"u8.ToArray()));
    }

    [AvaloniaFact]
    public async Task AConfirmationIsAttachedToABookingAndShownWithItsAttachments()
    {
        var w = new MainWindow(); w.Model.SetPreference("Language", "en"); w.Show(); Dispatcher.UIThread.RunJobs();
        try
        {
            var m = w.Model; await m.OpenAsync(await Trip()); m.InboxOpen = false;
            var booking = Entity.Create("booking", "Lakeview Lodge"); await m.Workspace!.EditAsync(booking);
            await m.AddFilesAsync([Confirmation()], booking.Id);
            var mail = Assert.Single(TripCommands.AttachedDocuments(m.Workspace.State.Trip, m.Workspace.State.Trip.Find(booking.Id)!));
            Assert.Equal(Mail.MediaType, mail.Data["media_type"]!.ToString());
            Assert.Equal("Booking 4711 confirmed", DocumentPreviews.Title(m, mail));
            Assert.Contains("Lakeview Lodge", DocumentPreviews.Summary(m, mail));

            // The inspector lists it under the booking's confirmations; opening it shows the document window.
            m.Navigate("Bookings"); m.Selected = m.Workspace.State.Trip.Find(booking.Id);
            // The inspector is rebuilt after queued renders; wait until the card is there.
            Button? card = null;
            for (var i = 0; i < 100 && card is null; i++)
            {
                Probe.Layout(); await Task.Delay(50);
                card = Probe.All<Button>(w.FindControl<Control>("InspectorHost")!).FirstOrDefault(b => Avalonia.Automation.AutomationProperties.GetName(b) == "Booking 4711 confirmed");
            }
            Assert.True(card is not null, "No confirmation card. Buttons: " + string.Join(" | ", Probe.All<Button>(w.FindControl<Control>("InspectorHost")!).Select(b => Avalonia.Automation.AutomationProperties.GetName(b))));
            card.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); Probe.Layout();
            var viewer = Assert.IsType<DocumentWindow>(DocumentWindow.Current);
            try
            {
                Probe.Layout();
                Assert.Contains(Probe.All<SelectableTextBlock>(viewer), t => t.Text == "Check-in from 15:00.");
                Assert.Contains(Probe.All<TextBlock>(viewer), t => t.Text?.StartsWith("1 image from the internet was not loaded", StringComparison.Ordinal) == true);

                // Its PDF attachment is added to the trip and attached to the same booking.
                var add = Probe.All<Button>(viewer).First(b => Avalonia.Automation.AutomationProperties.GetName(b) == m.Strings["AddToTrip"] + ": ticket.pdf");
                add.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                for (var i = 0; i < 20 && TripCommands.AttachedDocuments(m.Workspace.State.Trip, m.Workspace.State.Trip.Find(booking.Id)!).Count < 2; i++) { await Task.Delay(50); Probe.Layout(); }
                var ticket = TripCommands.AttachedDocuments(m.Workspace.State.Trip, m.Workspace.State.Trip.Find(booking.Id)!).Single(d => d.Id != mail.Id);
                Assert.Equal("application/pdf", ticket.Data["media_type"]!.ToString());

                // Viewing the attachment shows its two pages and finds text in it.
                var view = Probe.All<Button>(viewer).First(b => Avalonia.Automation.AutomationProperties.GetName(b) == m.Strings["OpenFile"] + ": ticket.pdf");
                view.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); Probe.Layout(); await Task.Delay(200); Probe.Layout();
                Assert.Equal(2, Probe.All<Border>(viewer).Count(b => Avalonia.Automation.AutomationProperties.GetName(b)?.StartsWith("Page ", StringComparison.Ordinal) == true));
                var search = Probe.All<TextBox>(viewer).Single(); search.Text = "page";
                search.RaiseEvent(new Avalonia.Input.KeyEventArgs { RoutedEvent = Avalonia.Input.InputElement.KeyDownEvent, Key = Avalonia.Input.Key.Enter });
                Probe.Layout();
                Assert.Contains(Probe.All<TextBlock>(viewer), t => t.Text == "1 of 1");
            }
            finally { viewer.Close(); }
        }
        finally { w.Close(); }
    }
}
