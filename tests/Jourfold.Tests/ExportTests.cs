using System.Text;
using System.Text.Json.Nodes;
using Avalonia.Headless.XUnit;
using Jourfold.Desktop;
using Jourfold.Infrastructure;
using PdfSharp.Pdf;
using TravelRepo.Core;
using TravelRepo.Git;
using TravelRepo.Repository;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;
using Xunit;
namespace Jourfold.Tests;

/// <summary>The export dialog's choices end up in files: plan PDFs with map and attachments, and packing lists per person.</summary>
public sealed class ExportTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "jourfold-export-" + Guid.NewGuid());
    public void Dispose() => TestFiles.Delete(root);
    private static string Font => Path.Combine(AppContext.BaseDirectory, "fonts", "PlusJakartaSans-Regular.ttf");

    private static string Text(string pdf)
    {
        using var document = UglyToad.PdfPig.PdfDocument.Open(pdf);
        return string.Join("\n", document.GetPages().Select(p => p.Text));
    }

    private static void Capture(string file)
    {
        if (Environment.GetEnvironmentVariable("JOURFOLD_CAPTURE_DIRECTORY") is not { } capture) return;
        Directory.CreateDirectory(capture); File.Copy(file, Path.Combine(capture, Path.GetFileName(file)), true);
    }

    private static byte[] TicketPdf(string text)
    {
        var builder = new PdfDocumentBuilder(); var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        builder.AddPage(UglyToad.PdfPig.Content.PageSize.A4).AddText(text, 14, new UglyToad.PdfPig.Core.PdfPoint(60, 760), font);
        return builder.Build();
    }

    private async Task<(MainViewModel Model, TestInteraction Interaction, LocalStore Store, Entity Ana, Entity Ben)> Open()
    {
        var repository = new TravelRepository(Path.Combine(root, "trip"));
        var manifest = Entity.CreateTrip("Lake weekend", "en", "Europe/Berlin"); manifest.Data["dates"] = new JsonObject { ["start"] = "2027-06-04", ["end"] = "2027-06-06" };
        await repository.InitializeAsync(manifest);
        await new GitRepository(repository, new GitCliBackend()).InitializeAsync("Test", "test@example.invalid");
        var store = new LocalStore(Path.Combine(root, "local")); var interaction = new TestInteraction();
        var m = new MainViewModel(store, new GitCliBackend(), new OsSecretStore(), interaction);
        await m.OpenAsync(repository.Root);
        var ana = Entity.Create("person", "Ana"); var ben = Entity.Create("person", "Ben");
        await m.Workspace!.ApplyAsync([new(ana.Id, ana), new(ben.Id, ben)]);
        return (m, interaction, store, ana, ben);
    }

    [AvaloniaFact]
    public async Task PlanPdfHasTheMapOnePersonsDaysAndAttachedConfirmations()
    {
        var (m, interaction, store, ana, ben) = await Open();
        using var _ = m; using var __ = store;
        var w = m.Workspace!;
        Entity Place(string name, double lat, double lon) { var p = Entity.Create("place", name); p.Data["location"] = new JsonObject { ["latitude"] = lat, ["longitude"] = lon }; return p; }
        Entity Item(string title, string start, string end, Entity person) => Entity.Create("schedule_item", title).Also(i =>
        {
            i.Data["time"] = new JsonObject { ["precision"] = "exact", ["start"] = new ZonedTime(start, "Europe/Berlin").ToJson(), ["end"] = new ZonedTime(end, "Europe/Berlin").ToJson() };
            i.Data["participants"] = new JsonObject { ["inherit"] = false, ["values"] = new JsonArray(person.Id.ToString()) };
        });
        var town = Place("Lakeside", 47.6, 9.4); var harbour = Place("Old harbour", 47.66, 9.48);
        var ferry = Item("Ferry", "2027-06-04T10:00:00", "2027-06-04T11:00:00", ana);
        ferry.Data["components"]!["transport"] = new JsonObject { ["type"] = "ferry", ["departure"] = new JsonObject { ["place"] = town.Id.ToString() }, ["arrival"] = new JsonObject { ["place"] = harbour.Id.ToString() } };
        var climbing = Item("Climbing", "2027-06-05T10:00:00", "2027-06-05T12:00:00", ben);
        await w.ApplyAsync([new(town.Id, town), new(harbour.Id, harbour), new(ferry.Id, ferry), new(climbing.Id, climbing)]);

        async Task<Entity> Import(string name, string media, byte[] bytes) { var path = Path.Combine(root, name); await File.WriteAllBytesAsync(path, bytes); return await w.ImportDocumentAsync(path, media); }
        var ticket = await Import("ticket.pdf", "application/pdf", TicketPdf("Boarding pass 4711"));
        var photo = await Import("pier.png", "image/png", Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAgAAAAICAYAAADED76LAAAAEklEQVR4nGPgr676jw8zjAwFAA4GgMHKS38GAAAAAElFTkSuQmCC"));
        var mail = await Import("confirmation.eml", "message/rfc822", Encoding.UTF8.GetBytes("From: Ferry office <office@example.org>\r\nTo: ana@example.org\r\nSubject: Your crossing\r\nContent-Type: text/plain; charset=utf-8\r\n\r\nPlease be at the pier ten minutes early.\r\n"));
        var sheet = await Import("rules.bin", "application/octet-stream", [1, 2, 3]);
        await w.ReloadAsync();
        var booking = Entity.Create("booking", "Ferry tickets"); booking.Data["reference"] = "FX-204"; booking.Data["items"] = new JsonArray(ferry.Id.ToString());
        booking.Data["documents"] = new JsonArray(new[] { ticket, photo, mail, sheet }.Select(d => (JsonNode)d.Id.ToString()).ToArray());
        await w.ApplyAsync([new(booking.Id, booking)]);

        var pdf = Path.Combine(root, "plan.pdf");
        interaction.Export = setup =>
        {
            Assert.Equal(new NodaTime.LocalDate(2027, 6, 4), setup.Start); Assert.Equal(["Ana", "Ben"], setup.People.Select(p => p.Title));
            return setup.Initial with { Options = setup.Initial.Options with { Parts = ExportParts.Standard | ExportParts.Map | ExportParts.Attachments, Person = ana.Id } };
        };
        interaction.Answers.Enqueue(pdf);
        await m.ExportCommand.ExecuteAsync(null);

        var text = Text(pdf);
        Assert.Contains("Plan for Ana", text); Assert.Contains("Ferry", text); Assert.DoesNotContain("Climbing", text);
        Assert.Contains("Attachment A1, A2, A3, A4", text);
        Assert.Contains("Boarding pass 4711", text); Assert.Contains("Please be at the pier ten minutes early.", text);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(text, "not included in this PDF")); // only rules.bin
        Assert.Contains("Lakeside", text); // map label
        using (var read = PdfSharp.Pdf.IO.PdfReader.Open(pdf, PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import)) Assert.True(read.PageCount >= 4);
        Assert.Contains(interaction.Toasts, t => t.Contains("plan.pdf"));
        Capture(pdf);
    }

    [AvaloniaFact]
    public async Task PackingListsGoOutPerPersonAsTextAndAsCsv()
    {
        var (m, interaction, store, ana, ben) = await Open();
        using var _ = m; using var __ = store;
        var list = PackingLists.CreateList("Barbecue");
        (var grill, list) = PackingLists.AddItem(list, "Grill", "Cooking", "1", [ana.Id]);
        (var bread, list) = PackingLists.AddItem(list, "Bread", null, null, [ben.Id]);
        (var drinks, list) = PackingLists.AddItem(list, "Drinks", everyone: true);
        await m.Workspace!.ApplyAsync([new(list.Id, list), new(grill.Id, grill), new(bread.Id, bread), new(drinks.Id, drinks)]);

        // One file per chosen person, in a folder; existing files are replaced after asking.
        var folder = Path.Combine(root, "lists"); Directory.CreateDirectory(folder);
        await File.WriteAllTextAsync(Path.Combine(folder, "Lake weekend - Packing list - Ben.pdf"), "old");
        interaction.Export = setup => setup.Initial with { Kind = "packing", Format = "pdf", People = [ana.Id, ben.Id] };
        interaction.Answers.Enqueue(folder);
        await m.ExportCommand.ExecuteAsync(null);
        var anaText = Text(Path.Combine(folder, "Lake weekend - Packing list - Ana.pdf"));
        Assert.Contains("Packing list for Ana", anaText); Assert.Contains("Grill", anaText); Assert.DoesNotContain("Bread", anaText);
        Assert.Contains("Bread", Text(Path.Combine(folder, "Lake weekend - Packing list - Ben.pdf")));
        Assert.Contains(interaction.Toasts, t => t.Contains("Exported 2 files"));
        Capture(Path.Combine(folder, "Lake weekend - Packing list - Ana.pdf"));

        // The dialog starts with the last choices for this trip.
        interaction.Export = setup =>
        {
            Assert.Equal("packing", setup.Initial.Kind); Assert.Equal([ana.Id, ben.Id], setup.Initial.People);
            return setup.Initial with { Format = "copy", People = [ben.Id] };
        };
        await m.ExportCommand.ExecuteAsync(null);
        Assert.Contains("☐ Bread", interaction.Copied.Last()); Assert.Contains("☐ Drinks · Everyone", interaction.Copied.Last());

        // CSV in German, for everybody, with the separator and byte order mark spreadsheets expect.
        var csv = Path.Combine(root, "list.csv");
        interaction.Export = setup => setup.Initial with { Format = "csv", People = [], Language = "de" };
        interaction.Answers.Enqueue(csv);
        await m.ExportCommand.ExecuteAsync(null);
        var bytes = await File.ReadAllBytesAsync(csv);
        Assert.Equal([0xEF, 0xBB, 0xBF], bytes[..3]);
        Assert.StartsWith("Liste;Gruppe;Sache;Menge;Bringt mit;Eingepackt;Erledigt\r\nBarbecue;Cooking;Grill;1;Ana;;nein", Encoding.UTF8.GetString(bytes[3..]));
    }

    [AvaloniaFact]
    public async Task TheShareMenuExportsAtOnceWithTheLastChoices()
    {
        var (m, interaction, store, ana, _) = await Open();
        using var _ = m; using var __ = store;
        var walk = Entity.Create("schedule_item", "Harbour walk");
        walk.Data["time"] = new JsonObject { ["precision"] = "exact", ["start"] = new ZonedTime("2027-06-05T09:00:00", "Europe/Berlin").ToJson(), ["end"] = new ZonedTime("2027-06-05T10:30:00", "Europe/Berlin").ToJson() };
        await m.Workspace!.ApplyAsync([new(walk.Id, walk)]);

        // A choice made in the dialog is remembered...
        interaction.Export = setup => setup.Initial with { Format = "txt", Options = setup.Initial.Options with { Detail = ExportDetail.Compact } };
        interaction.Answers.Enqueue(Path.Combine(root, "plan.txt"));
        await m.ExportCommand.ExecuteAsync(null);
        var remembered = store.Get("export:" + m.Workspace.Repository.Root);

        // ...and quick exports use it without asking, and without changing it.
        var pdf = Path.Combine(root, "quick.pdf");
        interaction.Answers.Enqueue(pdf);
        await m.ShareExportCommand.ExecuteAsync("pdf");
        Assert.Single(interaction.ExportSetups);
        var text = Text(pdf);
        Assert.Contains("Harbour walk", text); Assert.Contains("Time zone: Berlin (CEST, UTC+2)", text); Assert.Contains("1 / 1", text);
        await m.ShareExportCommand.ExecuteAsync("copy");
        Assert.Contains("09:00–10:30  Harbour walk", interaction.Copied.Last());
        Assert.Equal(remembered, store.Get("export:" + m.Workspace.Repository.Root));
    }

    [AvaloniaFact]
    public async Task DamagedPdfsAreAppendedAsPageImages()
    {
        if (!PdfiumDocument.IsAvailable) return;
        Directory.CreateDirectory(root);
        // A PDF whose cross-reference table points nowhere: PDFsharp cannot import its pages, PDFium rebuilds the table and draws them.
        var broken = Path.Combine(root, "broken.pdf");
        var text = Encoding.Latin1.GetString(TicketPdf("Damaged ticket"));
        var at = text.LastIndexOf("startxref", StringComparison.Ordinal);
        await File.WriteAllBytesAsync(broken, Encoding.Latin1.GetBytes(text[..at] + "startxref\n999999\n%%EOF\n"));
        Assert.ThrowsAny<Exception>(() => PdfSharp.Pdf.IO.PdfReader.Open(broken, PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import).Dispose());
        var trip = new TripSnapshot(Entity.CreateTrip("Lake weekend"), new Dictionary<Guid, Entity>());
        var document = TripExport.Document(trip);
        var output = Path.Combine(root, "out.pdf");
        await PdfExport.WriteAsync(document, output, new PdfExportSettings(Font) { Appendices = [new("A1", "broken.pdf", Pdf: await File.ReadAllBytesAsync(broken))] });
        using var read = PdfSharp.Pdf.IO.PdfReader.Open(output, PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import);
        Assert.Equal(2, read.PageCount);
        Capture(output);
    }

    [AvaloniaFact]
    public async Task LetterPaperAndTheGridMapWorkWithoutAnyNetwork()
    {
        Directory.CreateDirectory(root);
        var map = new ExportMap([new("Lakeside", 47.6, 9.4), new("Old harbour", 47.66, 9.48)], [new(47.6, 9.4, 47.66, 9.48, false)]);
        var document = new ExportDocument("Lake weekend", ["4 June 2027"], [], map, [], "Footer", "en", new ExportOptions { Paper = PaperSize.Letter }, new ExportLabels());
        var output = Path.Combine(root, "letter.pdf");
        await PdfExport.WriteAsync(document, output, new PdfExportSettings(Font));
        using var read = PdfSharp.Pdf.IO.PdfReader.Open(output, PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import);
        Assert.Equal(612, read.Pages[0].Width.Point, 1); Assert.Equal(792, read.Pages[0].Height.Point, 1);
        Assert.Contains("Old harbour", Text(output)); Assert.DoesNotContain("© Test tiles", Text(output));

        // With the street map on, tiles come from the basemap and its attribution is printed on the map.
        var requested = new List<(int Zoom, int X, int Y)>();
        var tile = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAgAAAAICAYAAADED76LAAAAEklEQVR4nGPgr676jw8zjAwFAA4GgMHKS38GAAAAAElFTkSuQmCC");
        var tiled = Path.Combine(root, "tiles.pdf");
        await PdfExport.WriteAsync(document, tiled, new PdfExportSettings(Font) { Basemap = new((z, x, y, _) => { requested.Add((z, x, y)); return Task.FromResult<byte[]?>(tile); }, "© Test tiles") });
        Assert.NotEmpty(requested); Assert.All(requested, r => Assert.Equal(requested[0].Zoom, r.Zoom)); Assert.InRange(requested.Count, 2, 30);
        Assert.Contains("© Test tiles", Text(tiled));
    }
}
