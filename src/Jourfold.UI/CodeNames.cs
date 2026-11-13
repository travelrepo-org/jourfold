using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Jourfold.UI;

/// <summary>
/// Release code names: an alliterative adjective and animal. The animal and the letter come from the major and minor
/// version, so a feature release and its patch releases share them. A feature release (x.y.0) and its pre-releases take
/// one of the letter's first adjectives; each patch release takes another of the letter's adjectives, in an order of
/// its own per feature release, so 0.12.0, 0.12.1 and 0.12.2 have three different ones. The words are in CodeNames.json, and
/// <c>eng/version.py --codename</c> computes the same name for release titles. Shared by the computer and the phone,
/// which both show the name of the version they are.
/// </summary>
public static class CodeNames
{
    private sealed record Words(string[] Adjectives, string[] Animals, string[]? PatchAdjectives);
    private static readonly Lazy<SortedDictionary<string, Words>> Lists = new(() =>
    {
        using var stream = typeof(CodeNames).Assembly.GetManifestResourceStream("Jourfold.CodeNames")!;
        var words = JsonSerializer.Deserialize<Dictionary<string, Words>>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        return new(words, StringComparer.Ordinal);
    });

    public static string For(string version)
    {
        var parts = version.Split('+')[0].Split('-')[0].Split('.');
        var feature = string.Join('.', parts.Take(2));
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes("jourfold:" + feature));
        uint Pick(int offset, int count) => BinaryPrimitives.ReadUInt32BigEndian(hash.AsSpan(offset, 4)) % (uint)count;
        var letter = Lists.Value.Values.ElementAt((int)Pick(0, Lists.Value.Count));
        var adjective = letter.Adjectives[Pick(4, letter.Adjectives.Length)];
        var animal = letter.Animals[Pick(8, letter.Animals.Length)];
        static int Number(string[] p, int i) => p.Length > i && int.TryParse(p[i], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var n) ? n : 0;
        var patch = Number(parts, 2);
        if (patch > 0)
        {
            // The letter's other adjectives in an order of their own for this feature release, one per patch.
            var others = letter.Adjectives.Concat(letter.PatchAdjectives ?? []).Where(a => a != adjective).Distinct()
                .OrderBy(a => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("jourfold:" + feature + ":" + a))), StringComparer.Ordinal).ToArray();
            adjective = others[(patch - 1) % others.Length];
        }
        return adjective + " " + animal;
    }
}
