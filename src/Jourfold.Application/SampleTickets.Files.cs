using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace Jourfold.Application;

public static partial class SampleTickets
{
    /// <summary>The flight's boarding pass as a PDF, with a stub to tear off and a code that is not a real one.</summary>
    public static byte[] Flight(string date) => Pdf(600, 250, page =>
    {
        page.Fill(0.059, 0.482, 0.478).Rect(0, 210, 600, 40);
        page.Fill(1, 1, 1).Text("Helvetica-Bold", 14, 24, 224, "BOARDING PASS").Text("Helvetica", 10, 300, 225, "SAMPLE - NOT VALID FOR TRAVEL");
        page.Fill(0.09, 0.145, 0.204);
        page.Text("Helvetica", 9, 24, 186, "PASSENGERS").Text("Helvetica-Bold", 13, 24, 170, "ALEX, SAM");
        page.Text("Helvetica-Bold", 40, 24, 112, "FRA").Text("Helvetica-Bold", 40, 250, 112, "HND");
        page.Stroke(0.059, 0.482, 0.478).Line(130, 126, 230, 126).Line(222, 131, 230, 126).Line(222, 121, 230, 126);
        page.Text("Helvetica", 9, 24, 96, "Frankfurt").Text("Helvetica", 9, 250, 96, "Tokyo Haneda");
        page.Fill(0.09, 0.145, 0.204);
        foreach (var (x, label, value) in new[] { (24.0, "FLIGHT", "LH 716"), (110.0, "DATE", date), (210.0, "DEPARTS", "13:20"), (290.0, "SEATS", "32A, 32C") })
            page.Text("Helvetica", 9, x, 64, label).Text("Helvetica-Bold", 13, x, 48, value);
        page.Fill(0.45, 0.5, 0.55).Text("Helvetica", 8, 24, 20, "A sample ticket of Jourfold's sample trip. Have a good flight!");
        // The stub, behind a perforation.
        page.Stroke(0.6, 0.65, 0.7).Dash(4, 4).Line(430, 12, 430, 202).Dash();
        page.Fill(0.09, 0.145, 0.204).Text("Helvetica-Bold", 13, 450, 180, "LH 716").Text("Helvetica", 10, 450, 164, "FRA - HND").Text("Helvetica", 10, 450, 150, "32A, 32C");
        // Bars that look like a code; the text below says they are none.
        var bits = Encoding.ASCII.GetBytes("JOURFOLD SAMPLE");
        for (var i = 0; i < 46; i++)
            if ((bits[i % bits.Length] >> (i % 7) & 1) == 1 || i % 5 == 0) page.Rect(450 + i * 2.6, 70, i % 3 == 0 ? 1.8 : 1.1, 60);
        page.Fill(0.45, 0.5, 0.55).Text("Helvetica", 7, 450, 58, "not a real code");
    });

    /// <summary>The teamLab Planets e-ticket as a PDF, with the tip the plan's notes give.</summary>
    public static byte[] TeamLab(string date) => Pdf(420, 300, page =>
    {
        page.Fill(0.08, 0.08, 0.12).Rect(0, 0, 420, 300);
        page.Fill(0.55, 0.85, 1).Text("Helvetica-Bold", 22, 24, 250, "teamLab Planets").Text("Helvetica", 11, 24, 232, "TOKYO - SAMPLE E-TICKET");
        page.Fill(1, 1, 1);
        foreach (var (y, label, value) in new[] { (190.0, "DATE", date), (156.0, "ENTRY", "15:30"), (122.0, "VISITORS", "2 adults - Alex, Sam"), (88.0, "BOOKING", "TLP-55120") })
            page.Text("Helvetica", 9, 24, y + 14, label).Text("Helvetica-Bold", 14, 24, y, value);
        // A ripple of circles, as on the floor of the water room.
        page.Stroke(0.55, 0.85, 1);
        for (var r = 12; r <= 72; r += 15) page.Circle(330, 150, r);
        page.Fill(0.7, 0.7, 0.8).Text("Helvetica", 9, 24, 40, "Wear shorts or trousers you can roll up: the water is knee-deep.")
            .Text("Helvetica", 8, 24, 24, "A sample ticket of Jourfold's sample trip. Not valid for entry.");
    });

    /// <summary>
    /// A picture of a voucher for deer crackers in Nara, as a photo of a paper ticket would be: a deer in pixels and
    /// the voucher's words. The deer in Nara bow for their crackers, so does this one's text.
    /// </summary>
    public static byte[] DeerCrackers()
    {
        const int Width = 300, Height = 150, Scale = 4;
        var cream = (0xFF, 0xF7, 0xE8); var teal = (0x0F, 0x7B, 0x7A); var ink = (0x17, 0x25, 0x34); var fur = (0x9C, 0x62, 0x30); var spot = (0xF4, 0xE3, 0xC8);
        var pixels = new (int R, int G, int B)[Width, Height];
        for (var x = 0; x < Width; x++) for (var y = 0; y < Height; y++)
            pixels[x, y] = x < 4 || y < 4 || x >= Width - 4 || y >= Height - 4 ? teal : (x == 104 && y % 6 < 3) ? teal : cream;
        // The deer, five pixels to each of its own.
        string[] deer =
        [
            "..#.......#.....",
            "..##.....##.....",
            "...##...##......",
            "....#####.......",
            "....#o##o#......",
            "....######......",
            ".....####.......",
            "......##........",
            "......########..",
            ".....#####:###:#",
            ".....##:#####:##",
            ".....##########.",
            ".....##.....##..",
            ".....##.....##..",
            ".....##.....##..",
        ];
        for (var row = 0; row < deer.Length; row++)
            for (var col = 0; col < deer[row].Length; col++)
                if (deer[row][col] is not '.' and var c)
                    for (var dx = 0; dx < 5; dx++) for (var dy = 0; dy < 5; dy++)
                        pixels[12 + col * 5 + dx, 30 + row * 5 + dy] = c == 'o' ? ink : c == ':' ? spot : fur;
        void Write(string text, int x, int y, int size, (int, int, int) color)
        {
            foreach (var ch in text)
            {
                if (Glyphs.TryGetValue(ch, out var glyph))
                    for (var row = 0; row < 7; row++) for (var col = 0; col < 5; col++)
                        if (glyph[row][col] == '1')
                            for (var dx = 0; dx < size; dx++) for (var dy = 0; dy < size; dy++) pixels[x + col * size + dx, y + row * size + dy] = color;
                x += 6 * size;
            }
        }
        Write("NARA DEER PARK", 116, 20, 2, teal);
        Write("SHIKA SENBEI", 116, 50, 2, ink);
        Write("1 PACK - SAMPLE", 116, 80, 1, ink);
        Write("FEED GENTLY - THEY BOW", 116, 100, 1, fur);
        Write("NOT A REAL TICKET", 116, 125, 1, teal);
        return Png(Width * Scale, Height * Scale, (x, y) => pixels[x / Scale, y / Scale]);
    }

    /// <summary>The letters the voucher needs, five pixels wide and seven high.</summary>
    private static readonly Dictionary<char, string[]> Glyphs = new()
    {
        ['A'] = ["01110", "10001", "10001", "11111", "10001", "10001", "10001"],
        ['B'] = ["11110", "10001", "10001", "11110", "10001", "10001", "11110"],
        ['C'] = ["01110", "10001", "10000", "10000", "10000", "10001", "01110"],
        ['D'] = ["11110", "10001", "10001", "10001", "10001", "10001", "11110"],
        ['E'] = ["11111", "10000", "10000", "11110", "10000", "10000", "11111"],
        ['F'] = ["11111", "10000", "10000", "11110", "10000", "10000", "10000"],
        ['G'] = ["01110", "10001", "10000", "10111", "10001", "10001", "01111"],
        ['H'] = ["10001", "10001", "10001", "11111", "10001", "10001", "10001"],
        ['I'] = ["01110", "00100", "00100", "00100", "00100", "00100", "01110"],
        ['K'] = ["10001", "10010", "10100", "11000", "10100", "10010", "10001"],
        ['L'] = ["10000", "10000", "10000", "10000", "10000", "10000", "11111"],
        ['M'] = ["10001", "11011", "10101", "10101", "10001", "10001", "10001"],
        ['N'] = ["10001", "11001", "10101", "10011", "10001", "10001", "10001"],
        ['O'] = ["01110", "10001", "10001", "10001", "10001", "10001", "01110"],
        ['P'] = ["11110", "10001", "10001", "11110", "10000", "10000", "10000"],
        ['R'] = ["11110", "10001", "10001", "11110", "10100", "10010", "10001"],
        ['S'] = ["01111", "10000", "10000", "01110", "00001", "00001", "11110"],
        ['T'] = ["11111", "00100", "00100", "00100", "00100", "00100", "00100"],
        ['W'] = ["10001", "10001", "10001", "10101", "10101", "10101", "01010"],
        ['Y'] = ["10001", "10001", "01010", "00100", "00100", "00100", "00100"],
        ['1'] = ["00100", "01100", "00100", "00100", "00100", "00100", "01110"],
        ['-'] = ["00000", "00000", "00000", "11111", "00000", "00000", "00000"],
    };

    /// <summary>A page drawn with PDF's own operators and its standard fonts, which every viewer has.</summary>
    private sealed class PdfPage
    {
        public StringBuilder Ops { get; } = new();
        private static string N(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);
        public PdfPage Fill(double r, double g, double b) { Ops.Append($"{N(r)} {N(g)} {N(b)} rg\n"); return this; }
        public PdfPage Stroke(double r, double g, double b) { Ops.Append($"{N(r)} {N(g)} {N(b)} RG 1.5 w\n"); return this; }
        public PdfPage Rect(double x, double y, double w, double h) { Ops.Append($"{N(x)} {N(y)} {N(w)} {N(h)} re f\n"); return this; }
        public PdfPage Line(double x1, double y1, double x2, double y2) { Ops.Append($"{N(x1)} {N(y1)} m {N(x2)} {N(y2)} l S\n"); return this; }
        public PdfPage Dash(double on = 0, double off = 0) { Ops.Append(on > 0 ? $"[{N(on)} {N(off)}] 0 d\n" : "[] 0 d\n"); return this; }
        public PdfPage Circle(double x, double y, double r)
        {
            // Four Bézier quarters.
            var k = r * 0.5523;
            Ops.Append($"{N(x + r)} {N(y)} m {N(x + r)} {N(y + k)} {N(x + k)} {N(y + r)} {N(x)} {N(y + r)} c {N(x - k)} {N(y + r)} {N(x - r)} {N(y + k)} {N(x - r)} {N(y)} c ");
            Ops.Append($"{N(x - r)} {N(y - k)} {N(x - k)} {N(y - r)} {N(x)} {N(y - r)} c {N(x + k)} {N(y - r)} {N(x + r)} {N(y - k)} {N(x + r)} {N(y)} c S\n");
            return this;
        }
        public PdfPage Text(string font, double size, double x, double y, string text)
        {
            var escaped = text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("(", "\\(", StringComparison.Ordinal).Replace(")", "\\)", StringComparison.Ordinal);
            Ops.Append($"BT /{(font == "Helvetica-Bold" ? "F2" : "F1")} {N(size)} Tf {N(x)} {N(y)} Td ({escaped}) Tj ET\n");
            return this;
        }
    }

    /// <summary>A one-page PDF.</summary>
    private static byte[] Pdf(int width, int height, Action<PdfPage> draw)
    {
        var page = new PdfPage(); draw(page);
        var content = Encoding.ASCII.GetBytes(page.Ops.ToString());
        string[] objects =
        [
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {width} {height}] /Resources << /Font << /F1 4 0 R /F2 5 0 R >> >> /Contents 6 0 R >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>",
        ];
        using var pdf = new MemoryStream();
        void Write(string text) { var bytes = Encoding.ASCII.GetBytes(text); pdf.Write(bytes); }
        Write("%PDF-1.4\n");
        var offsets = new List<long>();
        for (var i = 0; i < objects.Length; i++) { offsets.Add(pdf.Position); Write($"{i + 1} 0 obj\n{objects[i]}\nendobj\n"); }
        offsets.Add(pdf.Position);
        Write($"6 0 obj\n<< /Length {content.Length} >>\nstream\n"); pdf.Write(content); Write("\nendstream\nendobj\n");
        var xref = pdf.Position;
        Write($"xref\n0 {offsets.Count + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets) Write(offset.ToString("D10", CultureInfo.InvariantCulture) + " 00000 n \n");
        Write($"trailer\n<< /Size {offsets.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return pdf.ToArray();
    }

    /// <summary>An RGB PNG, written by hand: the sample needs no image library.</summary>
    private static byte[] Png(int width, int height, Func<int, int, (int R, int G, int B)> pixel)
    {
        using var raw = new MemoryStream();
        using (var z = new ZLibStream(raw, CompressionLevel.Optimal, leaveOpen: true))
        {
            var row = new byte[1 + width * 3];
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++) { var (r, g, b) = pixel(x, y); row[1 + x * 3] = (byte)r; row[2 + x * 3] = (byte)g; row[3 + x * 3] = (byte)b; }
                z.Write(row);
            }
        }
        using var png = new MemoryStream();
        png.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        void Chunk(string type, byte[] data)
        {
            var head = new byte[4]; System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(head, data.Length); png.Write(head);
            var typed = Encoding.ASCII.GetBytes(type).Concat(data).ToArray(); png.Write(typed);
            var crc = new byte[4]; System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(crc, Crc(typed)); png.Write(crc);
        }
        var header = new byte[13];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(header, width); System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = 8; header[9] = 2; // 8 bits, RGB
        Chunk("IHDR", header); Chunk("IDAT", raw.ToArray()); Chunk("IEND", []);
        return png.ToArray();
    }

    private static uint Crc(byte[] data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in data)
        {
            crc ^= b;
            for (var k = 0; k < 8; k++) crc = (crc & 1) != 0 ? 0xEDB88320u ^ (crc >> 1) : crc >> 1;
        }
        return ~crc;
    }
}
