using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.Input;
using Jourfold.Infrastructure;
using TravelRepo.Core;
using TravelRepo.Documents;
using TravelRepo.Repository;
using Jourfold.Application;

namespace Jourfold.Desktop;

/// <summary>
/// Exports of the open trip: the plan as PDF, web page, text or calendar, and packing lists as PDF, text or CSV, for
/// everybody or one file per person. What goes into an export is decided by TravelRepo; this part asks, renders and
/// writes files. The last choices are remembered per trip on this computer.
/// </summary>
public partial class MainViewModel
{
    private static readonly HttpClient ExportHttp = new() { Timeout = TimeSpan.FromSeconds(20) };
    public static readonly IReadOnlyList<string> ExportLanguages = ["en", "de"];
    private string? ExportKey => Workspace is null ? null : "export:" + Workspace.Repository.Root;
    private static string FontFile => Path.Combine(AppContext.BaseDirectory, "fonts", "PlusJakartaSans-Regular.ttf");

    [RelayCommand]
    private Task Export() => ExportWith(null, true);

    /// <summary>
    /// The share menu's quick exports: <c>pdf</c>, <c>ics</c> and <c>copy</c> export the plan at once with the last
    /// choices for this trip; <c>packing</c> opens the dialog on packing lists. Quick exports do not change the
    /// remembered choices.
    /// </summary>
    [RelayCommand]
    private Task ShareExport(string action) => action switch
    {
        "packing" => ExportWith(c => c with { Kind = "packing", Format = c.Kind == "packing" ? c.Format : "pdf" }, true),
        "costs" => ExportWith(c => c with { Kind = "costs", Format = c.Kind == "costs" ? c.Format : "pdf" }, true),
        _ => ExportWith(c => c with { Kind = "plan", Format = action }, false)
    };

    private Task ExportWith(Func<ExportChoice, ExportChoice>? preset, bool ask) => RunAsync(async () =>
    {
        if (Workspace is not { } w) return;
        var trip = w.State.Trip;
        var (start, end) = TripQueries.EffectiveDates(trip);
        var people = trip.Entities.Values.Where(e => e.Type == "person").OrderBy(e => e.Title, StringComparer.CurrentCulture).ToArray();
        var initial = InitialExport(trip); if (preset is not null) initial = preset(initial);
        var zones = (SecondZone is { } second ? SecondZoneSuggestions().Prepend(second) : SecondZoneSuggestions()).Distinct().ToArray();
        var choice = ask ? await Interaction.ExportAsync(new ExportSetup(people, Me, PackingLists.All(trip), start, end, OnlineMaps, initial, ScheduleQueries.TripZone(trip).Id, zones) { HasCosts = trip.Entities.Values.Any(e => e.Type == "expense") }) : initial;
        if (choice is null) return;
        if (ask) RememberExport(choice);
        var strings = choice.Language == Strings.Language ? Strings : new Localization(choice.Language);
        var labels = ExportLabels(strings); var culture = ExportCulture(choice.Language);
        if (choice.Kind == "packing") await ExportPackingAsync(trip, choice, labels, culture, strings);
        else if (choice.Kind == "costs") await ExportCostsAsync(w, trip, choice, labels, culture, strings);
        else await ExportPlanAsync(w, trip, choice, labels, culture, strings);
    });

    private async Task ExportPlanAsync(Jourfold.Application.Workspace w, TripSnapshot trip, ExportChoice choice, ExportLabels labels, CultureInfo culture, Localization strings)
    {
        var document = TripExport.Document(trip, choice.Options, labels, culture);
        if (choice.Format == "copy") { await Interaction.CopyAsync(TripExport.Text(document)); Interaction.Toast(Strings["Copied"]); return; }
        var person = choice.Options.Person is { } id ? trip.Find(id) : null;
        var path = await Interaction.FileAsync(true, choice.Format, FolderName(person is null ? trip.Manifest.Title : trip.Manifest.Title + " - " + person.Title)); if (path is null) return;
        switch (choice.Format)
        {
            case "pdf": await PdfExport.WriteAsync(document, path, PdfSettings(w, document, strings)); break;
            case "ics": await File.WriteAllTextAsync(path, TripExport.Ics(trip, choice.Options)); break;
            case "txt": await File.WriteAllTextAsync(path, TripExport.Text(document)); break;
            default: await File.WriteAllTextAsync(path, TripExport.Html(document)); break;
        }
        Exported([path]);
    }

    private async Task ExportPackingAsync(TripSnapshot trip, ExportChoice choice, ExportLabels labels, CultureInfo culture, Localization strings)
    {
        var options = new PackingExportOptions { Lists = choice.Lists.Count > 0 ? choice.Lists : null, UnassignedOnly = choice.UnassignedOnly, Comments = choice.Options.Comments };
        var people = choice.UnassignedOnly ? [] : choice.People.Select(trip.Find).OfType<Entity>().ToArray();
        var title = trip.Manifest.Title + " - " + strings["ExportPackingFile"];
        async Task Write(PackingExportOptions o, string path)
        {
            switch (choice.Format)
            {
                case "pdf":
                    var document = PackingExport.Document(trip, o, labels, culture);
                    await PdfExport.WriteAsync(document with { Options = document.Options with { Paper = choice.Options.Paper } }, path, new PdfExportSettings(FontFile)); break;
                // Spreadsheet programs need the byte order mark to read UTF-8 CSV files.
                case "csv": await File.WriteAllTextAsync(path, PackingExport.Csv(trip, o, labels, culture), new UTF8Encoding(true)); break;
                default: await File.WriteAllTextAsync(path, PackingExport.Text(trip, o, labels, culture)); break;
            }
        }
        if (people.Length <= 1)
        {
            var single = options with { Person = people.FirstOrDefault()?.Id };
            if (choice.Format == "copy") { await Interaction.CopyAsync(PackingExport.Text(trip, single, labels, culture)); Interaction.Toast(Strings["Copied"]); return; }
            var path = await Interaction.FileAsync(true, choice.Format, Path.GetFileNameWithoutExtension(PackingExport.FileName(title, people.FirstOrDefault(), choice.Format))); if (path is null) return;
            await Write(single, path); Exported([path]); return;
        }
        var folder = await Interaction.FolderAsync(Strings["ExportChooseFolder"]); if (folder is null) return;
        var files = people.Select(p => (Person: p, Path: Path.Combine(folder, PackingExport.FileName(title, p, choice.Format)))).ToArray();
        var existing = files.Count(f => File.Exists(f.Path));
        if (existing > 0 && !await Interaction.ConfirmAsync(Strings["ExportReplaceTitle"], string.Format(Strings[existing == 1 ? "ExportReplaceOne" : "ExportReplaceMany"], existing), Strings["ExportReplace"])) return;
        foreach (var (person, path) in files) await Write(options with { Person = person.Id }, path);
        Exported(files.Select(f => f.Path).ToArray());
    }

    /// <summary>
    /// The costs as a PDF, a CSV file with the chosen separator or text: for everyone, or the parts of the chosen
    /// people together, in the trip's currency. The rows and totals come from TravelRepo's <see cref="CostExport"/>.
    /// </summary>
    private async Task ExportCostsAsync(Jourfold.Application.Workspace w, TripSnapshot trip, ExportChoice choice, ExportLabels labels, CultureInfo culture, Localization strings)
    {
        var options = new CostExportOptions { People = choice.People, Currency = TripCurrencyCode, Rates = EstimateRates };
        var costLabels = CostLabels(strings, RatesDay);
        var document = CostExport.Document(trip, options, costLabels, culture, labels);
        if (choice.Format == "copy") { await Interaction.CopyAsync(TripExport.Text(document)); Interaction.Toast(Strings["Copied"]); return; }
        var file = CostExport.FileName(trip, options, costLabels, choice.Format);
        var path = await Interaction.FileAsync(true, choice.Format, Path.GetFileNameWithoutExtension(file)); if (path is null) return;
        switch (choice.Format)
        {
            case "pdf": await PdfExport.WriteAsync(document with { Options = document.Options with { Paper = choice.Options.Paper } }, path, new PdfExportSettings(FontFile)); break;
            // Spreadsheet programs need the byte order mark to read UTF-8 CSV files.
            case "csv": await File.WriteAllTextAsync(path, CostExport.Csv(trip, options, costLabels, culture, CostExport.Separators.GetValueOrDefault(choice.Separator) ?? ","), new UTF8Encoding(true)); break;
            default: await File.WriteAllTextAsync(path, TripExport.Text(document)); break;
        }
        Exported([path]);
    }

    /// <summary>The words of a cost export in the chosen language.</summary>
    public static CostExportLabels CostLabels(Localization s, string ratesDay = "") => new()
    {
        Title = s["Costs"],
        For = s["CostExportFor"],
        In = s["InCurrency"],
        Overview = s["CostExportOverview"],
        Paid = s["CostExportPaid"],
        ToPay = s["StillToPay"],
        Share = s["CostExportShare"],
        Budget = s["CostExportBudget"],
        BudgetUsed = s["CostExportBudgetUsed"],
        NotConverted = s["NotConvertedYet"],
        Estimated = string.Format(s["EstimatedWithRates"], ratesDay),
        ByCategory = s["ByCategory"],
        WhoOwesWhom = s["WhoOwesWhom"],
        PaysTo = s["PaysTo"],
        AllEven = s["AllEven"],
        Payments = s["Payments"],
        PaidBy = s["PaidBy"],
        SharedBy = s["SharedBy"],
        Owes = s["ExportOwes"],
        Charged = s["ChargedShort"],
        StateToPay = s["ToPay"],
        StateSettled = s["SettledState"],
        StateOpen = s["OpenAmount"],
        DueOn = s["DueOn"],
        Date = s["CostExportDate"],
        Payment = s["CostExportPayment"],
        Category = s["Category"],
        BelongsTo = s["BelongsTo"],
        Amount = s["Amount"],
        Currency = s["Currency"],
        ChargedAmount = s["Charged"],
        ChargedCurrency = s["CostExportChargedCurrency"],
        InCurrency = s["InCurrency"],
        PaidByColumn = s["Payer"],
        State = s["Status"],
        Open = s["CostExportOpen"],
        Due = s["Due"],
        PartOf = s["CostExportPartOf"],
        PartsIn = s["CostExportPartsIn"],
        GeneratedBy = s["ExportFooter"],
        Ideas = s["CostExportIdeas"],
        StateIdea = s["CostExportStateIdea"],
        PaidBack = s["CostExportPaidBack"],
        TransferredAs = s["CostExportTransferredAs"],
        Categories = ExpenseCategories.Standard.ToDictionary(c => c, c => s["expcat." + c])
    };

    private void Exported(IReadOnlyList<string> paths) =>
        Interaction.Toast(paths.Count == 1 ? string.Format(Strings["Exported"], Path.GetFileName(paths[0])) : string.Format(Strings["ExportedFiles"], paths.Count), Strings["ShowFile"], () => { Interaction.Open(Path.GetDirectoryName(paths[0])!); return Task.CompletedTask; });

    /// <summary>Fonts, map tiles when the street map is on, and the attached files of a plan export.</summary>
    private PdfExportSettings PdfSettings(Jourfold.Application.Workspace w, ExportDocument document, Localization strings)
    {
        var settings = new PdfExportSettings(FontFile) { NotIncluded = strings["ExportNotIncluded"], Appendices = document.Attachments.Select(a => Appendix(w, a, strings)).ToArray() };
        if (document.Map is null || !OnlineMaps) return settings;
        var tiles = new MapTiles(Store.Root, ExportHttp, MapProviders.Current(Store));
        return settings with { Basemap = new(async (z, x, y, ct) => (await tiles.GetAsync(z, x, y, null, ct)).Bytes, tiles.Provider.Attribution, tiles.Provider.MaxZoom) };
    }

    /// <summary>An attached file prepared for the PDF: PDF pages, an image (saved again as PNG unless it is a JPEG), or the text of an e-mail or text file.</summary>
    private static PdfAppendix Appendix(Jourfold.Application.Workspace w, ExportAttachment attachment, Localization strings)
    {
        byte[] bytes;
        try { bytes = File.ReadAllBytes(w.Repository.DocumentPath(attachment.Document)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DomainException) { return new(attachment.Label, attachment.Name); }
        var media = attachment.MediaType;
        if (media == "application/pdf") return new(attachment.Label, attachment.Name, Pdf: bytes);
        if (media == "image/jpeg") return new(attachment.Label, attachment.Name, Image: bytes);
        // PDFsharp reads JPEG and only some PNG variants, so other images are decoded and saved again as plain PNG.
        if (media.StartsWith("image/", StringComparison.Ordinal))
        {
            try
            {
                using var bitmap = new Avalonia.Media.Imaging.Bitmap(new MemoryStream(bytes)); using var png = new MemoryStream(); bitmap.Save(png, Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
                return new(attachment.Label, attachment.Name, Image: png.ToArray());
            }
            catch (Exception ex) when (ex is not OutOfMemoryException) { return new(attachment.Label, attachment.Name); }
        }
        if (Mail.IsMail(media) || attachment.Name.EndsWith(".eml", StringComparison.OrdinalIgnoreCase))
        {
            var mail = Mail.Read(bytes);
            var lines = new List<string> { mail.Subject ?? "" };
            if (mail.From.Count > 0) lines.Add(strings["MailFrom"] + ": " + string.Join(", ", mail.From));
            if (mail.To.Count > 0) lines.Add(strings["MailTo"] + ": " + string.Join(", ", mail.To));
            if (mail.Date is { } date) lines.Add(strings["MailDate"] + ": " + date.ToString("g", CultureInfo.CurrentCulture));
            if (mail.Attachments.Count > 0) lines.Add(strings["Attachments"] + ": " + string.Join(", ", mail.Attachments.Select(a => a.FileName)));
            lines.Add(""); lines.AddRange(mail.Text.Replace("\r\n", "\n").Split('\n'));
            return new(attachment.Label, attachment.Name, Text: lines);
        }
        if (media.StartsWith("text/", StringComparison.Ordinal)) return new(attachment.Label, attachment.Name, Text: Encoding.UTF8.GetString(bytes).Replace("\r\n", "\n").Split('\n'));
        return new(attachment.Label, attachment.Name);
    }

    private static CultureInfo ExportCulture(string language) =>
        CultureInfo.CurrentCulture.TwoLetterISOLanguageName == language ? CultureInfo.CurrentCulture : CultureInfo.CreateSpecificCulture(language);

    /// <summary>The choices the export dialog starts with: the last ones for this trip, or sensible defaults.</summary>
    private ExportChoice InitialExport(TripSnapshot trip)
    {
        var lists = PackingLists.All(trip).Select(l => l.Collection.Id).ToArray();
        // Until someone chooses otherwise, an export shows the plan's second time zone too, as the timetable does.
        var fallback = new ExportChoice("plan", "pdf", new ExportOptions { Paper = ExportOptions.PaperFor(CultureInfo.CurrentCulture), OtherTimeZones = SecondZone is { } second ? [second] : [] }, Strings.Language, lists, Me is { } me ? [me.Id] : [], false) { Separator = CultureInfo.CurrentCulture.TextInfo.ListSeparator == ";" ? "semicolon" : "comma" };
        if (ExportKey is not { } key || Store.Get(key) is not { } saved) return fallback;
        try
        {
            var json = JsonNode.Parse(saved)!.AsObject();
            Guid? Person(JsonNode? n) => Guid.TryParse(n?.ToString(), out var id) && trip.Find(id) is { Type: "person" } ? id : null;
            var options = new ExportOptions
            {
                Parts = json["parts"]?.ToString() is { } parts ? ExportOptions.ParseParts(parts) : fallback.Options.Parts,
                Detail = json["detail"]?.ToString() == "compact" ? ExportDetail.Compact : ExportDetail.Detailed,
                Paper = json["paper"]?.ToString() == "letter" ? PaperSize.Letter : json["paper"]?.ToString() == "a4" ? PaperSize.A4 : fallback.Options.Paper,
                Person = Person(json["person"]),
                Timeline = json["timeline"]?.GetValue<bool>() != false,
                Comments = json["comments"]?.GetValue<bool>() == true,
                // A zone this version's time zone database does not know falls back to the trip's.
                TimeZone = json["timezone"]?.ToString() is { Length: > 0 } zone && NodaTime.DateTimeZoneProviders.Tzdb.GetZoneOrNull(zone) is not null ? zone : null,
                // A choice saved before this option existed gets the default; a saved empty choice stays empty.
                OtherTimeZones = json["other_timezones"] is JsonArray chosen
                    ? chosen.Select(n => n?.ToString()).OfType<string>().Where(z => NodaTime.DateTimeZoneProviders.Tzdb.GetZoneOrNull(z) is not null).Distinct().ToArray()
                    : fallback.Options.OtherTimeZones
            };
            var savedLists = (json["lists"] as JsonArray ?? []).Select(n => Guid.TryParse(n?.ToString(), out var id) ? id : Guid.Empty).Where(lists.Contains).ToArray();
            var kind = json["kind"]?.ToString() switch { "packing" when lists.Length > 0 => "packing", "costs" => "costs", _ => "plan" };
            return new(kind, json["format"]?.ToString() is { Length: > 0 } format ? format : "pdf", options, ExportLanguages.Contains(json["language"]?.ToString() ?? "") ? json["language"]!.ToString() : Strings.Language,
                savedLists.Length > 0 ? savedLists : lists, (json["people"] as JsonArray ?? []).Select(Person).OfType<Guid>().ToArray(), json["unassigned"]?.GetValue<bool>() == true)
            { Separator = json["separator"]?.ToString() is { } separator && CostExport.Separators.ContainsKey(separator) ? separator : fallback.Separator };
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidOperationException or FormatException or DomainException) { return fallback; }
    }

    private void RememberExport(ExportChoice choice)
    {
        if (ExportKey is not { } key) return;
        Store.Set(key, new JsonObject
        {
            ["kind"] = choice.Kind,
            ["format"] = choice.Format,
            ["parts"] = ExportOptions.FormatParts(choice.Options.Parts),
            ["detail"] = choice.Options.Compact ? "compact" : "detailed",
            ["paper"] = choice.Options.Paper == PaperSize.Letter ? "letter" : "a4",
            ["person"] = choice.Options.Person?.ToString(),
            ["timeline"] = choice.Options.Timeline,
            ["comments"] = choice.Options.Comments,
            ["timezone"] = choice.Options.TimeZone,
            ["other_timezones"] = new JsonArray(choice.Options.OtherTimeZones.Select(z => (JsonNode)z).ToArray()),
            ["language"] = choice.Language,
            ["unassigned"] = choice.UnassignedOnly,
            ["lists"] = new JsonArray(choice.Lists.Select(l => (JsonNode)l.ToString()).ToArray()),
            ["people"] = new JsonArray(choice.People.Select(p => (JsonNode)p.ToString()).ToArray()),
            ["separator"] = choice.Separator
        }.ToJsonString());
    }

    /// <summary>Export wording in the chosen language, by default the interface language. Trip content itself is never translated.</summary>
    public ExportLabels ExportLabels(Localization? strings = null)
    {
        var s = strings ?? Strings;
        return new()
        {
            Day = s["DayN"],
            AllDay = s["AllDay"],
            Unscheduled = s["NotScheduledYet"],
            Bookings = s["Bookings"],
            Costs = s["Costs"],
            Travellers = s["ExportTravellers"],
            Paid = s["ExportPaid"],
            Estimated = s["ExportEstimated"],
            Budget = s["ExportBudget"],
            Between = s["BetweenTimes"],
            Reference = s["ExportRef"],
            GeneratedBy = s["ExportFooter"],
            Stays = s["ExportStays"],
            Contacts = s["ExportContacts"],
            Tasks = s["Tasks"],
            Notes = s["Notes"],
            PackingLists = s["ExportPackingLists"],
            Map = s["Map"],
            Attachments = s["Attachments"],
            AttachedAs = s["ExportAttachedAs"],
            Nights = s["ExportNights"],
            OneNight = s["ExportOneNight"],
            CheckIn = s["ExportCheckIn"],
            CheckOut = s["ExportCheckOut"],
            CheckInHours = s["ExportCheckInHours"],
            CheckOutHours = s["ExportCheckOutHours"],
            FromTime = s["ExportFromTime"],
            UntilTime = s["ExportUntilTime"],
            FreeCancellation = s["ExportFreeCancellation"],
            Breakfast = s["ExportBreakfast"],
            Parking = s["ExportParking"],
            ParkingIncluded = s["ExportParkingIncluded"],
            Due = s["ExportDue"],
            PlanFor = s["ExportPlanFor"],
            PackingFor = s["ExportPackingFor"],
            NobodyYet = s["ExportNobodyYet"],
            Everyone = s["Everyone"],
            Nobody = s["Unassigned"],
            With = s["ExportWith"],
            PackedBy = s["ExportPackedBy"],
            PaidBy = s["ExportPaidBy"],
            Share = s["ExportShare"],
            ShareOf = s["ExportShareOf"],
            Owes = s["ExportOwes"],
            PaidBack = s["ExportPaidBack"],
            OpenAmount = s["ExportOpen"],
            Settled = s["ExportSettled"],
            SettleUp = s["ExportSettleUp"],
            PaysTo = s["ExportPaysTo"],
            ToPay = s["ExportToPay"],
            BudgetUsed = s["ExportBudgetUsed"],
            Yes = s["ExportYes"],
            No = s["ExportNo"],
            List = s["ExportList"],
            Group = s["ExportGroup"],
            Item = s["ExportItem"],
            Quantity = s["ExportQuantity"],
            BroughtBy = s["ExportBroughtBy"],
            Packed = s["ExportPacked"],
            Done = s["ExportDone"],
            OtherItems = s["ExportOtherItems"],
            CheckInAt = s["ExportCheckInAt"],
            CheckOutAt = s["ExportCheckOutAt"],
            NightAt = s["ExportNightAt"],
            NightOf = s["ExportNightOf"],
            TimeZone = s["ExportTimeZone"],
            TimeZones = s["ExportTimeZones"],
            Comments = s["Comments"],
            Someone = s["Someone"],
            DayParts = new[] { "morning", "afternoon", "evening", "night" }.ToDictionary(k => k, k => s[k]),
            Statuses = ScheduleCategories.Statuses.Concat(["pending", "open", "in_progress"]).Distinct().ToDictionary(k => k, k => s[k]),
            Access = new[] { "reception", "self", "host" }.ToDictionary(k => k, k => s["ExportAccess." + k]),
            BreakfastValues = new[] { "included", "available", "none" }.ToDictionary(k => k, k => s["ExportBreakfast." + k])
        };
    }
}
