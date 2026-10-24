using System.Globalization;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using TravelRepo.Core;
using TravelRepo.Repository;

namespace Jourfold.Infrastructure;

/// <summary>
/// A file to append to an exported PDF, prepared by the client: the pages of a PDF, an image, or text lines such as
/// an e-mail's headers and body (the first text line is shown as a heading). An appendix with none of them is listed
/// as not included.
/// </summary>
public sealed record PdfAppendix(string Label, string Name, byte[]? Pdf = null, byte[]? Image = null, IReadOnlyList<string>? Text = null);

/// <summary>Map tiles for the overview map: a tile loader (zoom, x, y) and the attribution the provider requires.</summary>
public sealed record PdfBasemap(Func<int, int, int, CancellationToken, Task<byte[]?>> Tile, string Attribution, int MaxZoom = 18);

public sealed record PdfExportSettings(string FontFile)
{
    /// <summary>Tiles for the map. Without them the map shows places and routes on a plain grid.</summary>
    public PdfBasemap? Basemap { get; init; }
    public IReadOnlyList<PdfAppendix> Appendices { get; init; } = [];
    /// <summary>Added to an attachment in the attachments section when its file could not be appended.</summary>
    public string NotIncluded { get; init; } = "not included in this PDF";
}

/// <summary>
/// Renders a TravelRepo <see cref="ExportDocument"/> as a PDF with the bundled Plus Jakarta Sans: sections and rows,
/// an overview map, and attached files as appendix pages.
/// </summary>
public static class PdfExport
{
    private sealed class Fonts(string regular, string bold) : IFontResolver
    {
        public byte[] GetFont(string faceName) => File.ReadAllBytes(faceName == "Bold" ? bold : regular);
        public FontResolverInfo ResolveTypeface(string familyName, bool isBold, bool isItalic) => new(isBold ? "Bold" : "Regular");
    }

    private static readonly XColor Ink = XColor.FromArgb(15, 42, 61), Muted = XColor.FromArgb(77, 97, 112), Accent = XColor.FromArgb(15, 123, 122), Rule = XColor.FromArgb(225, 231, 232), Land = XColor.FromArgb(243, 246, 246), Grid = XColor.FromArgb(214, 223, 225);

    /// <summary>The default export of a trip, for tests and callers without options.</summary>
    public static void Write(TripSnapshot trip, string destination, string fontFile, ExportLabels? labels = null, CultureInfo? culture = null) =>
        WriteAsync(TripExport.Document(trip, null, labels, culture), destination, new PdfExportSettings(fontFile)).GetAwaiter().GetResult();

    public static async Task WriteAsync(ExportDocument document, string destination, PdfExportSettings settings, CancellationToken ct = default)
    {
        var bold = Path.Combine(Path.GetDirectoryName(settings.FontFile)!, "PlusJakartaSans-Bold.ttf");
        GlobalFontSettings.FontResolver ??= new Fonts(settings.FontFile, File.Exists(bold) ? bold : settings.FontFile);
        var appendices = settings.Appendices.Select(Prepare).ToArray();
        try
        {
            using var pdf = new PdfDocument(); pdf.Info.Title = document.Title; pdf.Info.Creator = "Jourfold";
            var renderer = new Renderer(pdf, document, settings, appendices);
            await renderer.RenderAsync(ct);
            pdf.Save(destination);
        }
        finally { foreach (var appendix in appendices) appendix.Dispose(); }
    }

    /// <summary>An appendix loaded for drawing: imported PDF pages, page images, an image, or text.</summary>
    private sealed class Prepared(PdfAppendix source) : IDisposable
    {
        public PdfAppendix Source { get; } = source;
        public PdfDocument? Imported { get; set; }
        public List<(XImage Image, double Width, double Height)> PageImages { get; } = [];
        public XImage? Image { get; set; }
        public bool Included => Imported is not null || PageImages.Count > 0 || Image is not null || Source.Text is not null;
        public void Dispose() { Imported?.Dispose(); foreach (var p in PageImages) p.Image.Dispose(); Image?.Dispose(); }
    }

    private static Prepared Prepare(PdfAppendix appendix)
    {
        var prepared = new Prepared(appendix);
        if (appendix.Pdf is { } bytes)
        {
            try { prepared.Imported = PdfReader.Open(new MemoryStream(bytes), PdfDocumentOpenMode.Import); }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // Some PDFs, such as damaged ones, cannot be imported page by page; PDFium can usually still draw them.
                try
                {
                    if (PdfiumDocument.IsAvailable)
                    {
                        using var pdfium = PdfiumDocument.Open(bytes);
                        for (var i = 0; i < pdfium.PageCount; i++) { var size = pdfium.PageSize(i); prepared.PageImages.Add((XImage.FromStream(new MemoryStream(Bmp(pdfium.Render(i, 2, print: true)))), size.Width, size.Height)); }
                    }
                }
                catch (Exception inner) when (inner is not OutOfMemoryException) { foreach (var p in prepared.PageImages) p.Image.Dispose(); prepared.PageImages.Clear(); }
            }
        }
        else if (appendix.Image is { } image)
        {
            try { prepared.Image = XImage.FromStream(new MemoryStream(image)); }
            catch (Exception ex) when (ex is not OutOfMemoryException) { prepared.Image = null; }
        }
        return prepared;
    }

    /// <summary>A 24-bit BMP from PDFium's BGRA pixels, which PDFsharp can embed.</summary>
    internal static byte[] Bmp(PdfPageImage image)
    {
        var row = (image.Width * 3 + 3) & ~3; var size = 54 + row * image.Height; var b = new byte[size];
        void Int(int at, int value) => BitConverter.TryWriteBytes(b.AsSpan(at, 4), value);
        b[0] = (byte)'B'; b[1] = (byte)'M'; Int(2, size); Int(10, 54); Int(14, 40); Int(18, image.Width); Int(22, image.Height);
        b[26] = 1; b[28] = 24; Int(34, row * image.Height); Int(38, 5669); Int(42, 5669);
        for (var y = 0; y < image.Height; y++)
        {
            var source = y * image.Stride; var target = 54 + (image.Height - 1 - y) * row;
            for (var x = 0; x < image.Width; x++) { b[target + x * 3] = image.Pixels[source + x * 4]; b[target + x * 3 + 1] = image.Pixels[source + x * 4 + 1]; b[target + x * 3 + 2] = image.Pixels[source + x * 4 + 2]; }
        }
        return b;
    }

    private sealed class Renderer(PdfDocument pdf, ExportDocument document, PdfExportSettings settings, IReadOnlyList<Prepared> appendices)
    {
        private const double Left = 48, Top = 56;
        private readonly (double Width, double Height) paper = document.Options.Paper == PaperSize.Letter ? (612, 792) : (595.28, 841.89);
        private double Width => paper.Width - 2 * Left;
        private double Bottom => paper.Height - 52;
        private XGraphics? g;
        private double y;
        private readonly List<PdfPage> pages = [];

        /// <summary>The app's colours for each kind of plan, light theme: background, text and bar.</summary>
        private static readonly Dictionary<string, (XColor Bg, XColor Fg, XColor Bar)> Tones = new()
        {
            ["activity"] = (Hex(0xE2F3F1), Hex(0x0A5654), Hex(0x14968F)),
            ["transport"] = (Hex(0xE5EEFB), Hex(0x1B4C8A), Hex(0x3B7DD8)),
            ["accommodation"] = (Hex(0xEEE9FA), Hex(0x4C3796), Hex(0x7C5CD6)),
            ["food"] = (Hex(0xFDEEE1), Hex(0x844210), Hex(0xE2843A)),
            ["sightseeing"] = (Hex(0xE3F3E8), Hex(0x1C653C), Hex(0x36A464)),
            ["culture"] = (Hex(0xF8E7F0), Hex(0x842B58), Hex(0xC9558E)),
            ["nature"] = (Hex(0xEBF3DE), Hex(0x455F18), Hex(0x7DA83A)),
            ["shopping"] = (Hex(0xFBE9E9), Hex(0x872D2D), Hex(0xD9605F)),
            ["nightlife"] = (Hex(0xE8E8F8), Hex(0x383A8A), Hex(0x6366D1)),
            ["meeting"] = (Hex(0xE9EEF2), Hex(0x324657), Hex(0x6B8396)),
            ["free_time"] = (Hex(0xEEF1F2), Hex(0x43515C), Hex(0x9AA7B1)),
            ["other"] = (Hex(0xEEF1F2), Hex(0x43515C), Hex(0x9AA7B1)),
        };
        private static XColor Hex(int rgb) => XColor.FromArgb((rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);
        private static (XColor Bg, XColor Fg, XColor Bar) Tone(string? tone) => Tones.GetValueOrDefault(tone ?? "other", Tones["other"]);

        private static XFont F(double size, bool strong = false) => new("Plus Jakarta Sans", size, strong ? XFontStyleEx.Bold : XFontStyleEx.Regular);
        private static XSolidBrush B(XColor color) => new(color);

        private PdfPage AddPage() { var page = pdf.AddPage(); page.Width = XUnit.FromPoint(paper.Width); page.Height = XUnit.FromPoint(paper.Height); return page; }

        private void NewPage()
        {
            g?.Dispose(); var page = AddPage(); pages.Add(page); g = XGraphics.FromPdfPage(page); y = Top;
            // From the second page on, a quiet header says which time zone the times are in.
            if (pages.Count > 1 && document.ZoneNote is { } note) g.DrawString(note, F(7.5), B(Muted), new XRect(Left, 30, Width, 10), XStringFormats.TopRight);
        }

        /// <summary>Page numbers out of the total, drawn once every page of the plan exists.</summary>
        private void Footers()
        {
            for (var i = 0; i < pages.Count; i++)
            {
                using var pg = XGraphics.FromPdfPage(pages[i], XGraphicsPdfPageOptions.Append);
                pg.DrawString(document.Title + " · " + (i + 1).ToString(CultureInfo.InvariantCulture) + " / " + pages.Count.ToString(CultureInfo.InvariantCulture), F(8), B(Muted), new XRect(Left, paper.Height - 30, Width, 12), XStringFormats.TopRight);
            }
        }

        private void Need(double height) { if (g is null || y + height > Bottom) NewPage(); }

        private List<string> Wrap(string text, XFont font, double max)
        {
            var lines = new List<string>(); var line = "";
            foreach (var paragraph in text.Split('\n'))
            {
                foreach (var word in paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                {
                    var candidate = line.Length == 0 ? word : line + " " + word;
                    if (g!.MeasureString(candidate, font).Width <= max) { line = candidate; continue; }
                    if (line.Length > 0) lines.Add(line);
                    line = word;
                    // A single word wider than the column, such as a long link, is broken where it has to be.
                    while (g.MeasureString(line, font).Width > max && line.Length > 1)
                    {
                        var cut = line.Length - 1; while (cut > 1 && g.MeasureString(line[..cut], font).Width > max) cut--;
                        lines.Add(line[..cut]); line = line[cut..];
                    }
                }
                if (line.Length > 0) { lines.Add(line); line = ""; }
            }
            return lines;
        }

        private void Lines(IEnumerable<string> lines, XFont font, XColor color, double x)
        {
            foreach (var line in lines) { g!.DrawString(line, font, B(color), new XPoint(x, y + font.Size)); y += font.Size * 1.35; }
        }

        private double Height(int lines, XFont font) => lines * font.Size * 1.35;

        public async Task RenderAsync(CancellationToken ct)
        {
            NewPage();
            Lines(Wrap(document.Title, F(24, true), Width), F(24, true), Ink, Left); y += 2;
            foreach (var meta in document.Meta) Lines(Wrap(meta, F(10), Width), F(10), Muted, Left);
            y += 8;
            if (document.Map is { } map) { Heading(null, document.Labels.Map); await MapAsync(map, ct); }
            foreach (var section in document.Sections)
            {
                var timeline = document.Options.Timeline ? section.Timeline : null;
                var timelineHeight = timeline is null ? 0 : TimelineHeight(timeline);
                Heading(section.Overline is { } o && section.Part != ExportParts.Packing ? o : null, section.Title, timelineHeight);
                var x = Left; var timelineBottom = 0d; var timelinePage = pages.Count;
                if (timeline is not null) { DrawTimeline(timeline, y + 2); timelineBottom = y + 2 + timelineHeight; x = Left + TimelineWidth + 14; }
                foreach (var row in section.Rows) Row(section, row, x);
                // A day's time line stays beside its rows; the next section starts below whichever is longer.
                if (timeline is not null && pages.Count == timelinePage && y < timelineBottom) y = timelineBottom + 6;
            }
            g?.Dispose(); g = null;
            Footers();
            Appendices();
        }

        private void Heading(string? overline, string title, double keepWith = 0)
        {
            Need(Math.Max(70, keepWith + 50)); y += 14;
            if (overline is { Length: > 0 }) { g!.DrawString(overline.ToUpper(CultureInfo.CurrentCulture), F(8.5, true), B(Accent), new XPoint(Left, y + 9)); y += 13; }
            Lines(Wrap(title, F(13.5, true), Width), F(13.5, true), Ink, Left);
            g!.DrawLine(new XPen(Accent, 1.2), Left, y + 2, Left + Width, y + 2); y += 10;
        }

        private void Row(ExportSection section, ExportRow row, double x)
        {
            var width = Left + Width - x;
            if (row.Kind == ExportRowKind.Heading)
            {
                Need(34); y += 6; Lines(Wrap(row.Title, F(10, true), width), F(10, true), Accent, x); y += 2; return;
            }
            if (row.Kind == ExportRowKind.Night)
            {
                // Where the travellers sleep: a quiet band in the colour of stays, like the night band in the app.
                var tone = Tone(row.Tone);
                var text = row.Summary is { Length: > 0 } n ? row.Title + "  ·  " + n : row.Title;
                var lines = Wrap(text, F(8.5), width - 16);
                var band = Height(lines.Count, F(8.5)) + 7;
                Need(band + 6); y += 2;
                g!.DrawRoundedRectangle(B(tone.Bg), new XRect(x, y, width, band), new XSize(5, 5));
                g.DrawRectangle(B(tone.Bar), new XRect(x, y + 2, 2.5, band - 4));
                y += 3.5; Lines(lines, F(8.5), tone.Fg, x + 10); y += 6; return;
            }
            var summary = row.Summary ?? "";
            if (section.Part == ExportParts.Attachments && appendices.FirstOrDefault(a => a.Source.Label == row.Lead) is { Included: false })
                summary = string.Join(" · ", new[] { summary, settings.NotIncluded }.Where(s => s.Length > 0));
            var attachments = row.AttachmentLabels.Count > 0 ? TripExport.AttachmentNote(document, row) : null;
            if (row.Kind == ExportRowKind.Check)
            {
                const double indent = 17;
                var title = Wrap(row.Lead.Length > 0 ? row.Title + "  (" + row.Lead + ")" : row.Title, F(10.5, true), width - indent);
                var sub = summary.Length > 0 ? Wrap(summary, F(9), width - indent) : [];
                Need(Height(title.Count, F(10.5, true)) + Height(sub.Count, F(9)) + 6);
                var box = new XRect(x, y + 2.2, 9.5, 9.5);
                g!.DrawRectangle(new XPen(row.Checked ? Accent : Muted, 0.9), row.Checked ? B(Accent) : XBrushes.White, box);
                if (row.Checked) g.DrawLines(new XPen(XColors.White, 1.4), [new XPoint(box.Left + 2, box.Top + 5), new XPoint(box.Left + 4, box.Top + 7.3), new XPoint(box.Left + 7.6, box.Top + 2.4)]);
                Lines(title, F(10.5, true), row.Checked ? Muted : Ink, x + indent);
                Lines(sub, F(9), Muted, x + indent);
                y += 5; return;
            }
            var lead = x > Left ? 104 : 120;
            // The status sits as a small tag at the end of the title line.
            var pill = string.IsNullOrEmpty(row.Status) ? null : row.Status;
            var pillWidth = pill is null ? 0 : g!.MeasureString(pill, F(7.5)).Width + 10;
            var leadLines = Wrap(row.Lead, F(9.5), lead - 10);
            // Times with two zones break after the dash, so each end stays on its own line.
            if (leadLines.Count > 1 && row.Lead.Split(" – ") is [var from, var until]) leadLines = [.. Wrap(from + " –", F(9.5), lead - 10), .. Wrap(until, F(9.5), lead - 10)];
            var titleLines = Wrap(row.Title, F(11, true), width - lead - (pill is null ? 0 : pillWidth + 8));
            var summaryLines = summary.Length > 0 ? Wrap(summary, F(9), width - lead) : [];
            var detailLines = row.DetailLines.SelectMany(d => Wrap(d, F(9), width - lead)).ToList();
            var attachmentLines = attachments is null ? [] : Wrap(attachments, F(8.5, true), width - lead);
            var height = Math.Max(Height(leadLines.Count, F(9.5)), Height(titleLines.Count, F(11, true)) + Height(summaryLines.Count + detailLines.Count, F(9)) + Height(attachmentLines.Count, F(8.5, true)));
            // Rows taller than a page are split; shorter ones move to the next page as a whole.
            Need(Math.Min(height, 160) + 12);
            var top = y;
            Lines(leadLines, F(9.5), Muted, x); var leadBottom = y; y = top;
            if (row.Tone is not null) g!.DrawRoundedRectangle(B(Tone(row.Tone).Bar), new XRect(x + lead - 8, top + 2, 2.5, Height(titleLines.Count, F(11, true)) - 2), new XSize(1.2, 1.2));
            if (pill is not null)
            {
                var box = new XRect(x + width - pillWidth, top + 1.5, pillWidth, 11.5);
                g!.DrawRoundedRectangle(B(Hex(0xEEF2F2)), box, new XSize(6, 6));
                g.DrawString(pill, F(7.5), B(Muted), box, XStringFormats.Center);
            }
            Lines(titleLines, F(11, true), Ink, x + lead);
            Lines(summaryLines, F(9), Muted, x + lead);
            foreach (var line in detailLines) { Need(14); Lines([line], F(9), Muted, x + lead); }
            Lines(attachmentLines, F(8.5, true), Accent, x + lead);
            y = Math.Max(Math.Max(y, leadBottom), top + 18) + 5;
            g!.DrawLine(new XPen(Rule, 0.6), x, y, Left + Width, y); y += 6;
        }

        private const double TimelineWidth = 66;

        private static (int First, int Last) Hours(ExportTimeline timeline)
        {
            var first = timeline.Blocks.Count == 0 ? 8 : Math.Min(8, timeline.Blocks.Min(b => b.Start) / 60);
            var last = timeline.Blocks.Count == 0 ? 20 : Math.Max(20, (timeline.Blocks.Max(b => b.End) + 59) / 60);
            return (first, Math.Min(24, last));
        }

        private static double HourHeight(ExportTimeline timeline) { var (first, last) = Hours(timeline); return Math.Clamp(150.0 / (last - first), 6.5, 10); }

        private static double TimelineHeight(ExportTimeline timeline) { var (first, last) = Hours(timeline); return (last - first) * HourHeight(timeline) + (timeline.Nights.Count > 0 ? 16 : 0); }

        /// <summary>A day's plans on a vertical time line, in the trip's time zone, with the night's stay at the bottom.</summary>
        private void DrawTimeline(ExportTimeline timeline, double top)
        {
            var (first, last) = Hours(timeline); var hour = HourHeight(timeline);
            const double axis = 16; var area = new XRect(Left + axis, top, TimelineWidth - axis, (last - first) * hour);
            g!.DrawRoundedRectangle(B(Hex(0xF7F9F9)), area, new XSize(3, 3));
            for (var h = first; h <= last; h++)
            {
                var hy = top + (h - first) * hour;
                g.DrawLine(new XPen(Rule, h % 2 == 0 ? 0.5 : 0.3), area.Left, hy, area.Right, hy);
                if (h % 2 == 0 && h < last) g.DrawString(h.ToString("00", CultureInfo.InvariantCulture), F(6), B(Muted), new XPoint(Left, hy + 5));
            }
            foreach (var block in timeline.Blocks)
            {
                var tone = Tone(block.Tone);
                var laneWidth = area.Width / Math.Max(1, block.Lanes);
                var rect = new XRect(area.Left + block.Lane * laneWidth + 0.6, top + (block.Start / 60.0 - first) * hour + 0.4, laneWidth - 1.2, Math.Max(2.5, (block.End - block.Start) / 60.0 * hour - 0.8));
                g.DrawRoundedRectangle(B(tone.Bg), rect, new XSize(2, 2));
                g.DrawRectangle(B(tone.Bar), new XRect(rect.Left, rect.Top, 1.6, rect.Height));
                if (rect.Height >= 8 && rect.Width >= 16) Fitted(block.Title, F(5.8), tone.Fg, rect.Left + 3, rect.Top + 6.4, rect.Width - 4);
            }
            if (timeline.Nights.Count > 0)
            {
                var tone = Tone("accommodation");
                var band = new XRect(area.Left, area.Bottom + 3, area.Width, 11);
                g.DrawRoundedRectangle(B(tone.Bg), band, new XSize(3, 3));
                Fitted(string.Join(", ", timeline.Nights), F(5.8), tone.Fg, band.Left + 3, band.Top + 7.6, band.Width - 5);
            }
        }

        /// <summary>Draws text cut to fit <paramref name="max"/> points, ending with an ellipsis when cut.</summary>
        private void Fitted(string text, XFont font, XColor color, double x, double baseline, double max)
        {
            var shown = text;
            if (g!.MeasureString(shown, font).Width > max)
            {
                while (shown.Length > 1 && g.MeasureString(shown + "…", font).Width > max) shown = shown[..^1];
                shown = shown.TrimEnd() + "…";
            }
            g.DrawString(shown, font, B(color), new XPoint(x, baseline));
        }

        private async Task MapAsync(ExportMap map, CancellationToken ct)
        {
            const double height = 250;
            Need(height + 10);
            var area = new XRect(Left, y, Width, height);
            // Web Mercator, as on the map page, at two tile pixels per point.
            static (double X, double Y) World(double lat, double lon, int zoom)
            {
                var size = 256 * Math.Pow(2, zoom); var rad = Math.Clamp(lat, -85, 85) * Math.PI / 180;
                return ((lon + 180) / 360 * size, (1 - Math.Log(Math.Tan(rad) + 1 / Math.Cos(rad)) / Math.PI) / 2 * size);
            }
            var points = Fit(map.Places.Select(p => (p.Latitude, p.Longitude)).Concat(map.Routes.SelectMany(r => new[] { (r.FromLatitude, r.FromLongitude), (r.ToLatitude, r.ToLongitude) })).ToArray());
            var maxZoom = Math.Min(settings.Basemap?.MaxZoom ?? 16, 16);
            var zoom = 1;
            for (var z = maxZoom; z >= 1; z--)
            {
                var projected = points.Select(p => World(p.Item1, p.Item2, z)).ToArray();
                var w = projected.Max(p => p.X) - projected.Min(p => p.X); var h = projected.Max(p => p.Y) - projected.Min(p => p.Y);
                if (w <= area.Width * 2 * 0.8 && h <= area.Height * 2 * 0.8) { zoom = points.Length == 1 || (w < 1 && h < 1) ? Math.Min(z, 13) : z; break; }
            }
            var world = points.Select(p => World(p.Item1, p.Item2, zoom)).ToArray();
            var center = ((world.Min(p => p.X) + world.Max(p => p.X)) / 2, (world.Min(p => p.Y) + world.Max(p => p.Y)) / 2);
            var origin = (X: center.Item1 - area.Width, Y: center.Item2 - area.Height);
            XPoint At(double lat, double lon) { var p = World(lat, lon, zoom); return new(area.Left + (p.X - origin.X) / 2, area.Top + (p.Y - origin.Y) / 2); }

            g!.Save(); g.IntersectClip(area);
            g.DrawRectangle(B(Land), area);
            var drewTiles = false;
            if (settings.Basemap is { } basemap)
            {
                var count = 1 << zoom;
                for (var tx = (int)Math.Floor(origin.X / 256); tx <= (int)Math.Floor((origin.X + area.Width * 2) / 256); tx++)
                    for (var ty = Math.Max(0, (int)Math.Floor(origin.Y / 256)); ty <= Math.Min(count - 1, (int)Math.Floor((origin.Y + area.Height * 2) / 256)); ty++)
                    {
                        byte[]? bytes;
                        try { bytes = await basemap.Tile(zoom, ((tx % count) + count) % count, ty, ct); }
                        catch (HttpRequestException) { bytes = null; }
                        if (bytes is null) continue;
                        try
                        {
                            using var tile = XImage.FromStream(new MemoryStream(bytes));
                            g.DrawImage(tile, area.Left + (tx * 256 - origin.X) / 2, area.Top + (ty * 256 - origin.Y) / 2, 128.5, 128.5); drewTiles = true;
                        }
                        catch (Exception ex) when (ex is not OutOfMemoryException) { }
                    }
            }
            if (!drewTiles)
            {
                // A plain grid of meridians and parallels, so the map stays readable offline.
                var size = 256 * Math.Pow(2, zoom);
                double Lon(double x) => x / size * 360 - 180;
                double Lat(double py) => Math.Atan(Math.Sinh(Math.PI * (1 - 2 * py / size))) * 180 / Math.PI;
                var (west, east, north, south) = (Lon(origin.X), Lon(origin.X + area.Width * 2), Lat(origin.Y), Lat(origin.Y + area.Height * 2));
                var step = new[] { 0.01, 0.02, 0.05, 0.1, 0.2, 0.5, 1, 2, 5, 10, 20, 30 }.FirstOrDefault(s => (east - west) / s <= 8, 45);
                for (var lon = Math.Ceiling(west / step) * step; lon <= east; lon += step) { var x = At(0, lon).X; g.DrawLine(new XPen(Grid, 0.5), x, area.Top, x, area.Bottom); }
                for (var lat = Math.Ceiling(south / step) * step; lat <= north; lat += step) { var py = At(lat, 0).Y; g.DrawLine(new XPen(Grid, 0.5), area.Left, py, area.Right, py); }
            }
            foreach (var route in map.Routes)
            {
                var pen = new XPen(Accent, 1.6) { DashStyle = route.Confirmed ? XDashStyle.Solid : XDashStyle.Dash, LineCap = XLineCap.Round };
                g.DrawLine(pen, At(route.FromLatitude, route.FromLongitude), At(route.ToLatitude, route.ToLongitude));
            }
            var dots = map.Places.Select(place => (Place: place, At: At(place.Latitude, place.Longitude))).Where(d => area.Contains(d.At)).ToArray();
            foreach (var (_, p) in dots) g.DrawEllipse(new XPen(XColors.White, 1.2), B(Accent), p.X - 3.4, p.Y - 3.4, 6.8, 6.8);
            // Labels go right of their dot, or left near the edge, and are left out where they would cover another label.
            var taken = new List<XRect>();
            foreach (var (place, p) in dots)
            {
                var size = g.MeasureString(place.Title, F(7.5));
                var box = new XRect(p.X + 5, p.Y - size.Height / 2 - 1, size.Width + 4, size.Height + 1);
                if (box.Right > area.Right - 2) box = box with { X = p.X - 5 - box.Width };
                if (box.Left < area.Left + 2 || box.Top < area.Top || box.Bottom > area.Bottom || taken.Any(t => t.IntersectsWith(box))) continue;
                taken.Add(box);
                g.DrawRectangle(new XSolidBrush(XColor.FromArgb(215, 255, 255, 255)), box);
                g.DrawString(place.Title, F(7.5), B(Ink), new XPoint(box.X + 2, p.Y + 2.6));
            }
            if (drewTiles && settings.Basemap is { } attributed)
            {
                var size = g.MeasureString(attributed.Attribution, F(6.5));
                g.DrawRectangle(new XSolidBrush(XColor.FromArgb(215, 255, 255, 255)), area.Right - size.Width - 6, area.Bottom - size.Height - 3, size.Width + 6, size.Height + 3);
                g.DrawString(attributed.Attribution, F(6.5), B(Muted), new XPoint(area.Right - size.Width - 3, area.Bottom - 3.5));
            }
            g.Restore();
            g.DrawRectangle(new XPen(Rule, 0.8), area);
            y += height + 10;
        }

        /// <summary>
        /// The points the map is fitted to. A few far-away points, such as the airport a long-haul flight leaves from,
        /// would shrink the trip to a corner, so points more than 2,000 km from the middle are left out as long as most
        /// remain. Their routes still run off the edge of the map.
        /// </summary>
        private static (double Lat, double Lon)[] Fit((double Lat, double Lon)[] points)
        {
            if (points.Length < 3) return points;
            static double Median(IEnumerable<double> values) { var sorted = values.Order().ToArray(); return sorted[sorted.Length / 2]; }
            var middle = (Lat: Median(points.Select(p => p.Lat)), Lon: Median(points.Select(p => p.Lon)));
            static double Km((double Lat, double Lon) a, (double Lat, double Lon) b)
            {
                double R(double d) => d * Math.PI / 180;
                var h = Math.Pow(Math.Sin(R(b.Lat - a.Lat) / 2), 2) + Math.Cos(R(a.Lat)) * Math.Cos(R(b.Lat)) * Math.Pow(Math.Sin(R(b.Lon - a.Lon) / 2), 2);
                return 2 * 6371 * Math.Asin(Math.Min(1, Math.Sqrt(h)));
            }
            var near = points.Where(p => Km(p, middle) <= 2000).ToArray();
            return near.Length >= points.Length * 0.6 && near.Length > 0 ? near : points;
        }

        private void Appendices()
        {
            foreach (var appendix in appendices.Where(a => a.Included))
            {
                var label = appendix.Source.Label + " · " + appendix.Source.Name;
                if (appendix.Imported is { } imported)
                    foreach (var page in imported.Pages) Stamp(pdf.AddPage(page), label);
                foreach (var (image, width, height) in appendix.PageImages)
                {
                    var page = pdf.AddPage(); page.Width = XUnit.FromPoint(width); page.Height = XUnit.FromPoint(height);
                    using (var pg = XGraphics.FromPdfPage(page)) pg.DrawImage(image, 0, 0, width, height);
                    Stamp(page, label);
                }
                if (appendix.Image is { } picture)
                {
                    g = XGraphics.FromPdfPage(AddPage()); y = Top;
                    Lines(Wrap(label, F(11, true), Width), F(11, true), Ink, Left); y += 8;
                    var scale = Math.Min(Width / picture.PointWidth, (Bottom - y) / picture.PointHeight);
                    scale = Math.Min(scale, 1.5);
                    g.DrawImage(picture, Left, y, picture.PointWidth * scale, picture.PointHeight * scale);
                    g.Dispose(); g = null;
                }
                if (appendix.Source.Text is { } text)
                {
                    g = XGraphics.FromPdfPage(AddPage()); y = Top;
                    Lines(Wrap(label, F(11, true), Width), F(11, true), Ink, Left); y += 8;
                    if (text.Count > 0) { Lines(Wrap(text[0], F(10.5, true), Width), F(10.5, true), Ink, Left); y += 2; }
                    foreach (var line in text.Skip(1).SelectMany(t => t.Length == 0 ? [""] : Wrap(t, F(9.5), Width)))
                    {
                        if (y + 14 > Bottom) { g.Dispose(); g = XGraphics.FromPdfPage(AddPage()); y = Top; }
                        Lines([line], F(9.5), Ink, Left);
                    }
                    g.Dispose(); g = null;
                }
            }
        }

        /// <summary>Marks an appended page with its attachment label in the bottom margin.</summary>
        private static void Stamp(PdfPage page, string label)
        {
            using var pg = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);
            var size = pg.MeasureString(label, F(7));
            pg.DrawRectangle(new XSolidBrush(XColor.FromArgb(230, 255, 255, 255)), 12, page.Height.Point - size.Height - 10, size.Width + 8, size.Height + 4);
            pg.DrawString(label, F(7), B(Muted), new XPoint(16, page.Height.Point - 10.5));
        }
    }
}
