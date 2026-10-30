using System.Text.Json.Nodes;
using Jourfold.Application;
using TravelRepo.Core;
using Xunit;
namespace Jourfold.Tests;

public sealed class PlaceEditTests
{
    [Theory]
    [InlineData("https://www.google.com/maps/place/Senso-ji/@35.7147651,139.7966553,17z/data=!3m1!4b1!4m6!3m5!1s0x0:0x0!8m2!3d35.7147651!4d139.7966553", 35.7147651, 139.7966553)]
    [InlineData("https://www.google.com/maps/@48.1371079,11.5753822,15z", 48.1371079, 11.5753822)]
    [InlineData("https://maps.google.com/?q=48.137108,11.575382", 48.137108, 11.575382)]
    [InlineData("https://www.google.com/maps/search/?api=1&query=-37.0093%2C174.7841", -37.0093, 174.7841)]
    [InlineData("https://maps.apple.com/?ll=35.6812,139.7671&q=Tokyo%20Station", 35.6812, 139.7671)]
    [InlineData("https://www.openstreetmap.org/?mlat=52.5163&mlon=13.3777#map=17/52.5163/13.3777", 52.5163, 13.3777)]
    [InlineData("https://www.openstreetmap.org/#map=15/52.5200/13.4050", 52.52, 13.405)]
    [InlineData("geo:35.6812,139.7671?q=Tokyo", 35.6812, 139.7671)]
    [InlineData("-37.0093, 174.7841", -37.0093, 174.7841)]
    public void MapLinksGiveTheirPosition(string link, double latitude, double longitude)
    {
        Assert.True(Coordinates.TryParseLink(link, out var lat, out var lon));
        Assert.Equal(latitude, lat, 6); Assert.Equal(longitude, lon, 6);
    }

    [Theory]
    [InlineData("https://maps.app.goo.gl/AbCdEf123")]
    [InlineData("Tokyo Station")]
    [InlineData("https://example.com/?q=200,10")]
    public void OtherTextIsNoPosition(string text) => Assert.False(Coordinates.TryParseLink(text, out _, out _));

    private static TripSnapshot Trip(params Entity[] entities) =>
        new(Entity.CreateTrip("Trip"), entities.ToDictionary(e => e.Id));

    [Fact]
    public void AJourneyGetsItsStartAndEnd()
    {
        var station = Entity.Create("place", "Tokyo Station");
        var train = Entity.Create("schedule_item", "Shinkansen"); train.Data["components"]!["transport"] = new JsonObject { ["type"] = "train" };
        var trip = Trip(station, train);
        Assert.Equal([PlaceSlot.From, PlaceSlot.To], PlaceEdits.Slots(train));

        var edits = PlaceEdits.Assign(trip, train.Id, PlaceSlot.From, PlaceChoice.Existing(station.Id));
        var changed = edits.Single().Value!;
        Assert.Equal(station.Id.ToString(), changed.Data["components"]!["transport"]!["departure"]!["place"]!.ToString());

        // A new name creates the place in the same step.
        edits = PlaceEdits.Assign(Trip(station, changed), train.Id, PlaceSlot.To, PlaceChoice.Create("Kyoto Station"));
        var kyoto = edits.Single(e => e.Value!.Type == "place").Value!;
        Assert.Equal("Kyoto Station", kyoto.Title);
        Assert.Equal(kyoto.Id.ToString(), edits.Single(e => e.Id == train.Id).Value!.Data["components"]!["transport"]!["arrival"]!["place"]!.ToString());
    }

    [Fact]
    public void AnActivityKeepsItsOwnPlaceAndCanLoseIt()
    {
        var temple = Entity.Create("place", "Senso-ji");
        var visit = Entity.Create("schedule_item", "Visit"); var trip = Trip(temple, visit);
        var withPlace = PlaceEdits.Assign(trip, visit.Id, PlaceSlot.Place, PlaceChoice.Existing(temple.Id)).Single().Value!;
        Assert.Equal(temple.Id.ToString(), withPlace.Data["default_place"]!.ToString());
        Assert.Equal(temple.Id, PlaceEdits.Current(Trip(temple, withPlace), withPlace, PlaceSlot.Place)!.Id);
        Assert.False(PlaceEdits.Assign(Trip(temple, withPlace), visit.Id, PlaceSlot.Place, null).Single().Value!.Data.ContainsKey("default_place"));
    }

    [Fact]
    public void APlaceGetsNameAddressAndPosition()
    {
        var place = Entity.Create("place", "Hotel"); place.Data["address"] = new JsonObject { ["formatted"] = "Old", ["city"] = "Tokyo" };
        PlaceEdits.Describe(place, "Hotel Gracery", "1-19-1 Kabukicho, Shinjuku", 35.6951234567, 139.7016);
        Assert.Equal("Hotel Gracery", place.Title);
        Assert.Equal("1-19-1 Kabukicho, Shinjuku", Places.Address(place));
        Assert.Equal("Tokyo", place.Data["address"]!["city"]!.ToString());
        Assert.Equal((35.695123, 139.7016), Places.Location(place));
        PlaceEdits.Describe(place, null, "", null, null);
        Assert.Null(Places.Address(place)); Assert.Null(Places.Location(place)); Assert.Equal("Hotel Gracery", place.Title);
    }
}
