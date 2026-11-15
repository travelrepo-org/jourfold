using System.Globalization;
using System.Text.Json.Nodes;
using Jourfold.UI;
using TravelRepo.Core;
using Avalonia;
using Xunit;

namespace Jourfold.Tests;

/// <summary>People's circles: a colour of their own within a trip, letters that tell them apart, never the warning colour.</summary>
public sealed class PersonLooksTests
{
    /// <summary>A trip with these people, who joined in this order (their IDs sort the same way).</summary>
    private static TripSnapshot Trip(params string[] names)
    {
        var people = names.Select((n, i) => Entity.Create("person", n).Also(p => p.Data["id"] = new Guid($"0197a000-0000-7000-8000-{i + 1:D12}").ToString())).ToArray();
        return new TripSnapshot(Entity.CreateTrip("Trip", "en", "Europe/Berlin"), people.ToDictionary(p => p.Id));
    }

    private static double Luminance(string hex)
    {
        double Channel(int at) { var v = int.Parse(hex.AsSpan(at, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255.0; return v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4); }
        return 0.2126 * Channel(1) + 0.7152 * Channel(3) + 0.0722 * Channel(5);
    }

    private static double Hue(string hex)
    {
        var (r, g, b) = (int.Parse(hex.AsSpan(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255.0, int.Parse(hex.AsSpan(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255.0, int.Parse(hex.AsSpan(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255.0);
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), d = max - min;
        if (d == 0) return -1;
        var h = max == r ? (g - b) / d % 6 : max == g ? (b - r) / d + 2 : (r - g) / d + 4;
        return (h * 60 + 360) % 360;
    }

    [Fact]
    public void TheFifteenColoursAreReadableDistinctAndNeverTheWarningColour()
    {
        Assert.Equal(15, PersonLooks.Palette.Count);
        Assert.Equal(PersonLooks.Palette.Count, PersonLooks.Palette.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        foreach (var colour in PersonLooks.Palette)
        {
            // White letters on it, at least 4.5 to 1.
            Assert.True(1.05 / (Luminance(colour) + 0.05) >= 4.5, colour + " is too light for white letters");
            // Yellow, amber and orange belong to warnings.
            var hue = Hue(colour);
            Assert.False(hue is >= 20 and <= 80, colour + " is in the warning colour's range");
        }
    }

    [Fact]
    public void UpToFifteenPeopleGetDifferentColoursAndANewPersonChangesNobodyElses()
    {
        var trip = Trip(Enumerable.Range(1, 15).Select(i => "Person " + i).ToArray());
        Assert.Equal(15, PersonLooks.For(trip).Values.Select(l => l.Color).Distinct().Count());
        var before = PersonLooks.For(trip).ToDictionary(p => p.Key, p => p.Value.Color);
        var newcomer = Entity.Create("person", "Zoe"); newcomer.Data["id"] = new Guid("0197a000-0000-7000-8000-000000000099").ToString();
        var grown = trip with { Entities = trip.Entities.Values.Append(newcomer).ToDictionary(e => e.Id) };
        foreach (var (id, colour) in before) Assert.Equal(colour, PersonLooks.Of(grown, id).Color);
    }

    [Fact]
    public void TheSameNameTwiceStillDiffersInColourAndLetters()
    {
        var trip = Trip("Max", "Max", "Mara", "Lena Weber");
        var looks = PersonLooks.For(trip).Values.ToArray();
        Assert.Equal(4, looks.Select(l => l.Color).Distinct().Count());
        Assert.Equal(4, looks.Select(l => l.Initials).Distinct().Count());
        Assert.Contains(looks, l => l.Initials == "LW");
        var maxes = looks.Where(l => l.Name == "Max").Select(l => l.Initials).Order().ToArray();
        Assert.Equal(["M2", "MA"], maxes);
        Assert.Equal("MR", looks.Single(l => l.Name == "Mara").Initials);
    }

    [Fact]
    public void RenamingKeepsTheColour()
    {
        var trip = Trip("Alex", "Sam");
        var sam = trip.Entities.Values.Single(e => e.Title == "Sam");
        var colour = PersonLooks.Of(trip, sam).Color;
        var renamed = sam.Copy(); renamed.Data["display_name"] = "Samuel";
        var after = trip with { Entities = trip.Entities.Values.Select(e => e.Id == sam.Id ? renamed : e).ToDictionary(e => e.Id) };
        Assert.Equal(colour, PersonLooks.Of(after, sam.Id).Color);
        Assert.Equal("SA", PersonLooks.Of(after, sam.Id).Initials);
    }

    [Fact]
    public void SomeoneWhoIsNoPersonGetsTheSameColourFromTheirNameEverywhere()
    {
        Assert.Equal(PersonLooks.ForName("Alex Example").Color, PersonLooks.ForName("Alex Example").Color);
        Assert.Contains(PersonLooks.ForName("Alex Example").Color, PersonLooks.Palette);
        Assert.Equal("AE", PersonLooks.ForName("Alex Example").Initials);
    }
}
