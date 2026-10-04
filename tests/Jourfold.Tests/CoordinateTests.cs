using System.Globalization;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Jourfold.Application;
using Jourfold.Desktop;
using TravelRepo.Core;
using TravelRepo.Git;
using TravelRepo.Repository;
using Xunit;
namespace Jourfold.Tests;

public sealed class CoordinateTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "jourfold-coordinates-" + Guid.NewGuid());
    public void Dispose() => TestFiles.Delete(root);

    [Theory]
    [InlineData("-37.009313240995525", -37.009313240995525)]
    [InlineData("-37,0093", -37.0093)]
    [InlineData(" 174.78 ", 174.78)]
    [InlineData("+12", 12)]
    public void SingleValuesAcceptPointOrComma(string text, double expected) { Assert.True(Coordinates.TryParse(text, out var value)); Assert.Equal(expected, value, 9); }

    [Theory]
    [InlineData("1.234,5")]
    [InlineData("abc")]
    [InlineData("12,5,3")]
    [InlineData("")]
    public void SingleValuesRejectOtherText(string text) => Assert.False(Coordinates.TryParse(text, out _));

    [Theory]
    [InlineData("-37.009313240995525, 174.78415367054876", -37.009313240995525, 174.78415367054876)]
    [InlineData("(-37.0093; 174.7841)", -37.0093, 174.7841)]
    [InlineData("-37,0093 174,7841", -37.0093, 174.7841)]
    [InlineData("-37.0093,174.7841", -37.0093, 174.7841)]
    public void PairsSplitAsCopiedFromMaps(string text, double latitude, double longitude)
    {
        Assert.True(Coordinates.TryParsePair(text, out var lat, out var lon)); Assert.Equal(latitude, lat, 9); Assert.Equal(longitude, lon, 9);
    }

    [Theory]
    [InlineData("12,5")]
    [InlineData("-37.0093")]
    [InlineData("95, 10")]
    [InlineData("10, 190")]
    public void PairsRejectSingleValuesAndOutOfRange(string text) => Assert.False(Coordinates.TryParsePair(text, out _, out _));

    [Fact]
    public void SchemaErrorsReadAsSentences()
    {
        var json = """{"valid":false,"details":[{"valid":false,"instanceLocation":"/location","errors":{"required":"Required properties [\"longitude\"] are not present"}}]}""";
        var message = new Localization("en").Error(new DomainException("schema.invalid", json));
        Assert.StartsWith("This change does not fit the trip format", message);
        Assert.Contains("location: Required properties [longitude] are not present", message); Assert.DoesNotContain("{", message);
    }

    [AvaloniaFact]
    public async Task PlaceCoordinatesSaveOnlyAsAPairAndSplitPastedPairs()
    {
        var w = new MainWindow(); w.Model.SetPreference("Language", "en"); w.Show(); Probe.Layout();
        try
        {
            var repository = new TravelRepository(Path.Combine(root, "trip")); await repository.InitializeAsync(Entity.CreateTrip("Coordinates", "en", "Pacific/Auckland"));
            await new GitRepository(repository, new GitCliBackend()).InitializeAsync("Test", "test@example.invalid"); await w.Model.OpenAsync(repository.Root);
            var place = Entity.Create("place", "Auckland Airport"); await w.Model.Workspace!.ApplyAsync([new(place.Id, place)]);
            w.Model.Selected = w.Model.Workspace.State.Trip.Find(place.Id); Probe.Layout();
            // The inspector remembers the last tab; this test needs Details.
            Probe.Click(w, w.Model.Strings["Details"]); Probe.Layout();
            var inspector = Probe.Named<StackPanel>(w, "Inspector");
            TextBox Box(string tag) => Probe.Tagged<TextBox>(Probe.Named<StackPanel>(w, "Inspector"), tag);

            // Half a location is not saved and says why, in German number style too.
            Box("latitude").Text = "-37,0093"; Box("latitude").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(InputElement.LostFocusEvent)); Probe.Layout();
            Assert.Null(w.Model.Workspace.State.Trip.Find(place.Id)!.Data["location"]);
            Assert.Contains(Probe.All<TextBlock>(inspector), t => t.Text == w.Model.Strings["CoordinatesNeedBoth"] && t.IsVisible);
            Box("longitude").Text = "174,7841"; Box("longitude").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(InputElement.LostFocusEvent));
            await Probe.Until(() => w.Model.Workspace.State.Trip.Find(place.Id)!.Data["location"] is not null);
            var saved = w.Model.Workspace.State.Trip.Find(place.Id)!.Data["location"]!;
            Assert.Equal(-37.0093, (double)saved["latitude"]!, 6); Assert.Equal(174.7841, (double)saved["longitude"]!, 6);

            // Clearing both removes the location; pasting a pair into an empty field fills both.
            Box("latitude").Text = ""; Box("longitude").Text = ""; Box("longitude").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(InputElement.LostFocusEvent));
            await Probe.Until(() => w.Model.Workspace.State.Trip.Find(place.Id)!.Data["location"] is null);
            await w.Clipboard!.SetTextAsync("-37.009313240995525, 174.78415367054876");
            Box("longitude").Paste();
            await Probe.Until(() => w.Model.Workspace.State.Trip.Find(place.Id)!.Data["location"] is not null);
            Assert.Equal("-37.009313", Box("latitude").Text); Assert.Equal("174.784154", Box("longitude").Text);
        }
        finally { w.Close(); }
    }

    [AvaloniaFact]
    public void NumbersNeverReadThousandsSeparators()
    {
        var w = new MainWindow(); w.Show(); var box = new NumericUpDown(); ((Panel)w.FindControl<Grid>("DialogLayer")!).Children.Add(box); Probe.Layout();
        Assert.Equal(NumberStyles.Float, box.ParsingNumberStyle); w.Close();
    }
}
