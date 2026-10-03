using System.Globalization;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;
using TravelRepo.Core;
using TravelRepo.Repository;

namespace Jourfold.Infrastructure;

/// <summary>A printable A4 itinerary rendered with the bundled Plus Jakarta Sans.</summary>
public static class PdfExport
{
    private sealed class Fonts(string regular, string bold) : IFontResolver
    {
        public byte[] GetFont(string faceName) => File.ReadAllBytes(faceName == "Bold" ? bold : regular);
        public FontResolverInfo ResolveTypeface(string familyName, bool isBold, bool isItalic) => new(isBold ? "Bold" : "Regular");
    }

    private static readonly XColor Ink = XColor.FromArgb(15, 42, 61), Muted = XColor.FromArgb(77, 97, 112), Accent = XColor.FromArgb(15, 123, 122), Rule = XColor.FromArgb(225, 231, 232);

    public static void Write(TripSnapshot trip, string destination, string fontFile, ExportLabels? labels = null, CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture; labels ??= new();
        var bold = Path.Combine(Path.GetDirectoryName(fontFile)!, "PlusJakartaSans-Bold.ttf");
        GlobalFontSettings.FontResolver ??= new Fonts(fontFile, File.Exists(bold) ? bold : fontFile);
        var it = TripItinerary.Build(trip, labels, culture);
        using var doc = new PdfDocument(); doc.Info.Title = it.Title; doc.Info.Creator = "Jourfold";
        XFont F(double size, bool strong = false) => new("Plus Jakarta Sans", size, strong ? XFontStyleEx.Bold : XFontStyleEx.Regular);
        const double left = 48, width = 499, bottom = 790;
        XGraphics? g = null; var y = 0d; var pageNumber = 0;
        void NewPage()
        {
            g?.Dispose(); var page = doc.AddPage(); page.Size = PdfSharp.PageSize.A4; g = XGraphics.FromPdfPage(page); pageNumber++; y = 56;
            g.DrawString(it.Title + " · " + pageNumber.ToString(culture), F(8), new XSolidBrush(Muted), new XRect(left, 812, width, 12), XStringFormats.TopRight);
        }
        void Need(double height) { if (g is null || y + height > bottom) NewPage(); }
        IEnumerable<string> Wrap(string text, XFont font, double max)
        {
            var line = "";
            foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var candidate = line.Length == 0 ? word : line + " " + word;
                if (line.Length > 0 && g!.MeasureString(candidate, font).Width > max) { yield return line; line = word; } else line = candidate;
            }
            if (line.Length > 0) yield return line;
        }
        double Text(string text, XFont font, XColor color, double x, double max)
        {
            var lines = Wrap(text, font, max).ToArray(); var height = font.Size * 1.35;
            foreach (var line in lines) { g!.DrawString(line, font, new XSolidBrush(color), new XPoint(x, y + font.Size)); y += height; }
            return lines.Length * height;
        }
        NewPage();
        Text(it.Title, F(24, true), Ink, left, width); y += 2;
        var dates = it.Start is { } a && it.End is { } b ? a.ToString("d MMMM yyyy", culture) + " – " + b.ToString("d MMMM yyyy", culture) : "";
        Text(string.Join(" · ", new[] { dates, it.Zone }.Where(x => x.Length > 0)), F(10), Muted, left, width);
        if (it.Travellers.Count > 0) Text(labels.Travellers + ": " + string.Join(", ", it.Travellers), F(10), Muted, left, width);
        y += 8;
        void Heading(string overline, string title)
        {
            Need(60); y += 14;
            if (overline.Length > 0) { g!.DrawString(overline.ToUpper(culture), F(8.5, true), new XSolidBrush(Accent), new XPoint(left, y + 9)); y += 13; }
            Text(title, F(13.5, true), Ink, left, width);
            g!.DrawLine(new XPen(Accent, 1.2), left, y + 2, left + width, y + 2); y += 10;
        }
        void Entries(IEnumerable<ItineraryEntry> entries)
        {
            foreach (var e in entries)
            {
                var sub = string.Join(" · ", new[] { e.Group, e.Where, e.People.Count > 0 ? string.Join(", ", e.People) : null, e.BookingReference is null ? null : labels.Reference + " " + e.BookingReference, e.Status }.Where(x => !string.IsNullOrEmpty(x)));
                Need(40); var top = y;
                Text(e.Time, F(9.5), Muted, left, 112); var timeBottom = y; y = top;
                Text(e.Item.Title, F(11, true), Ink, left + 120, width - 120);
                if (sub.Length > 0) Text(sub, F(9), Muted, left + 120, width - 120);
                y = Math.Max(Math.Max(y, timeBottom), top + 18) + 5;
                g.DrawLine(new XPen(Rule, 0.6), left, y, left + width, y); y += 6;
            }
        }
        foreach (var day in it.Days) { Heading(day.Number > 0 ? string.Format(culture, labels.Day, day.Number) : "", day.Date.ToString("dddd, d MMMM", culture)); Entries(day.Entries); }
        if (it.Unscheduled.Count > 0) { Heading("", labels.Unscheduled); Entries(it.Unscheduled); }
        if (it.Bookings.Count > 0)
        {
            Heading("", labels.Bookings);
            foreach (var booking in it.Bookings)
            {
                var price = TripSummaries.Money(booking.Data["price"]);
                Need(36); var top = y;
                g!.DrawString(booking.Data["reference"]?.ToString() ?? "", F(9.5, true), new XSolidBrush(Muted), new XPoint(left, y + 11));
                Text(booking.Title, F(11, true), Ink, left + 120, width - 120);
                Text(string.Join(" · ", new[] { booking.Data["provider"]?["name"]?.ToString(), price is null ? null : price.Amount.ToString("N2", culture) + " " + price.Currency, labels.Statuses.GetValueOrDefault(booking.Data["status"]?.ToString() ?? "") }.Where(x => !string.IsNullOrEmpty(x))), F(9), Muted, left + 120, width - 120);
                y = Math.Max(y, top + 18) + 6;
            }
        }
        if (it.Costs.Count > 0)
        {
            Heading("", labels.Costs);
            foreach (var c in it.Costs) { Need(20); Text(c.Currency + "  " + c.Paid.ToString("N2", culture) + " " + labels.Paid + (c.Estimated > 0 ? " · " + c.Estimated.ToString("N2", culture) + " " + labels.Estimated : "") + (c.Budget > 0 ? " · " + c.Budget.ToString("N2", culture) + " " + labels.Budget : ""), F(10), Ink, left, width); }
        }
        g?.Dispose(); doc.Save(destination);
    }
}
