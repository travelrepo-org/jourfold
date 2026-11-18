using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;

namespace Jourfold.Application;

/// <summary>
/// The sample trip's tickets, in each form a ticket comes in, so the ticket buttons, the pass view and the Live
/// Activity's "Show ticket" have something to show: a Wallet pass (.pkpass) for the Shinkansen to Kyoto, with English
/// and German labels as real passes have, a PDF boarding pass for the flight to Tokyo, a PDF e-ticket for teamLab
/// Planets and a picture of a voucher for deer crackers in Nara. All are clearly samples. Scanned, the pass's code
/// says hello.
/// </summary>
public static partial class SampleTickets
{
    /// <summary>The Shinkansen's ticket. Seat E is the window on the right, the side Mount Fuji is on towards Kyoto.</summary>
    public static byte[] Shinkansen(string departs, string date) => Build(new JsonObject
    {
        ["description"] = "Shinkansen to Kyoto",
        ["organizationName"] = "Jourfold sample",
        ["relevantDate"] = date,
        ["backgroundColor"] = "rgb(15, 123, 122)",
        ["foregroundColor"] = "rgb(255, 255, 255)",
        ["labelColor"] = "rgb(205, 235, 231)",
        ["barcodes"] = Code("PKBarcodeFormatQR", "Jourfold sample ticket, not valid for travel. Good trip, Alex and Sam! Mount Fuji is on the right, about 40 minutes after Tokyo: seat 12E is the window."),
        ["boardingPass"] = new JsonObject
        {
            ["transitType"] = "PKTransitTypeTrain",
            ["headerFields"] = Fields(("car", "car", "7")),
            ["primaryFields"] = Fields(("from", "from", "Tokyo"), ("to", "to", "Kyoto")),
            ["secondaryFields"] = Fields(("train", "train", "Nozomi 215"), ("departs", "departs", departs), ("arrives", "arrives", "10:45")),
            ["auxiliaryFields"] = Fields(("seats", "seats", "12D, 12E"), ("travellers", "travellers", "Alex, Sam")),
            ["backFields"] = Fields(("fuji", "fuji", "fuji_tip"), ("sample", "sample", "sample_note")),
        },
    }, Strings);

    /// <summary>The labels in English and German, keyed as in the passes.</summary>
    private static readonly Dictionary<string, (string En, string De)> Strings = new()
    {
        ["car"] = ("Car", "Wagen"),
        ["from"] = ("From", "Von"),
        ["to"] = ("To", "Nach"),
        ["train"] = ("Train", "Zug"),
        ["departs"] = ("Departs", "Abfahrt"),
        ["arrives"] = ("Arrives", "Ankunft"),
        ["seats"] = ("Seats", "Plätze"),
        ["travellers"] = ("Travellers", "Reisende"),
        ["fuji"] = ("Mount Fuji", "Fuji"),
        ["fuji_tip"] = ("Look out of the right window about 40 minutes after Tokyo, near Shin-Fuji. Seat 12E is the window.",
            "Etwa 40 Minuten nach Tokyo, bei Shin-Fuji, aus dem rechten Fenster schauen. Platz 12E ist am Fenster."),
        ["sample"] = ("Sample", "Beispiel"),
        ["sample_note"] = ("This is a sample ticket of Jourfold's sample trip. It is not valid for travel.",
            "Dies ist ein Beispielticket der Beispielreise von Jourfold. Es gilt nicht als Fahrkarte."),
        ["Shinkansen to Kyoto"] = ("Shinkansen to Kyoto", "Shinkansen nach Kyoto"),
        ["Jourfold sample"] = ("Jourfold sample", "Jourfold-Beispiel"),
    };

    private static JsonArray Code(string format, string message) =>
        new(new JsonObject { ["format"] = format, ["message"] = message, ["messageEncoding"] = "iso-8859-1" });

    private static JsonArray Fields(params (string Key, string Label, string Value)[] fields) =>
        new(fields.Select(f => (JsonNode)new JsonObject { ["key"] = f.Key, ["label"] = f.Label, ["value"] = f.Value }).ToArray());

    /// <summary>A pass as a .pkpass archive: pass.json and its strings. It is not signed, as a sample needs no signature.</summary>
    private static byte[] Build(JsonObject pass, Dictionary<string, (string En, string De)> strings)
    {
        pass["formatVersion"] = 1;
        pass["passTypeIdentifier"] = "pass.org.jourfold.sample";
        pass["serialNumber"] = pass["description"]!.ToString().ToLowerInvariant().Replace(' ', '-');
        pass["teamIdentifier"] = "SAMPLE";
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            // A fixed time, so the same sample trip always gives the same bytes.
            void Entry(string name, string text)
            {
                var entry = zip.CreateEntry(name, CompressionLevel.Optimal); entry.LastWriteTime = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
                using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
                writer.Write(text);
            }
            Entry("pass.json", pass.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            static string Escape(string s) => s.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
            Entry("en.lproj/pass.strings", string.Concat(strings.Select(s => $"\"{Escape(s.Key)}\" = \"{Escape(s.Value.En)}\";\n")));
            Entry("de.lproj/pass.strings", string.Concat(strings.Select(s => $"\"{Escape(s.Key)}\" = \"{Escape(s.Value.De)}\";\n")));
        }
        return stream.ToArray();
    }
}
