using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;
using TravelRepo.Core;
using TravelRepo.Repository;
namespace Jourfold.Infrastructure;

public static class PdfExport
{
    private sealed class Fonts(string file) : IFontResolver
    {
        public string DefaultFontName => "Plus Jakarta Sans";
        public byte[] GetFont(string faceName) => File.ReadAllBytes(file);
        public FontResolverInfo ResolveTypeface(string familyName, bool isBold, bool isItalic) => new("Jakarta");
    }
    public static void Write(TripSnapshot trip, string destination, string fontFile)
    {
        GlobalFontSettings.FontResolver ??= new Fonts(fontFile);
        using var doc = new PdfDocument(); doc.Info.Title = trip.Manifest.Title; doc.Info.Creator = "Jourfold";
        var font = new XFont("Plus Jakarta Sans", 11); var heading = new XFont("Plus Jakarta Sans", 20);
        XGraphics? graphics = null; var y = 0d;
        void Page() { graphics?.Dispose(); var page = doc.AddPage(); graphics = XGraphics.FromPdfPage(page); y = 48; }
        void Line(string text, bool title = false)
        {
            if (graphics is null || y > 770) Page();
            graphics!.DrawString(text, title ? heading : font, XBrushes.Black, new XPoint(42, y)); y += title ? 30 : 17;
        }
        Line(trip.Manifest.Title, true);
        foreach (var entity in trip.Entities.Values.OrderBy(e => e.Data["time"]?["start"]?["local"]?.ToString() ?? "~"))
        {
            Line(entity.Title, true);
            foreach (var paragraph in TripExport.Describe(entity).Split('\n'))
            {
                var words = paragraph.Split(' '); var line = "";
                foreach (var word in words) { if (line.Length + word.Length > 80) { Line(line); line = ""; } line += (line.Length > 0 ? " " : "") + word; }
                if (line.Length > 0) Line(line);
            }
            y += 12;
        }
        graphics?.Dispose(); doc.Save(destination);
    }
}
