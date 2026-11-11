using System.IO.Compression;
using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Jourfold.Desktop;
using Jourfold.UI.Controls;
using TravelRepo.Core;
using TravelRepo.Git;
using TravelRepo.Repository;
using Xunit;
namespace Jourfold.Tests;

/// <summary>What the phone does on the road, on the desktop too: bookings and groups both ways, layovers, tickets and the converter.</summary>
public sealed class DesktopTravelTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "jourfold-desktop-travel-" + Guid.NewGuid());
    public void Dispose() => TestFiles.Delete(root);

    private async Task<MainWindow> OpenAsync()
    {
        var repository = new TravelRepository(Path.Combine(root, "trip")); await repository.InitializeAsync(Entity.CreateTrip("Test trip", "en", "Europe/Berlin"));
        await new GitRepository(repository, new GitCliBackend()).InitializeAsync("Test", "test@example.invalid");
        var w = new MainWindow(); w.Model.SetPreference("Language", "en"); w.Show(); Dispatcher.UIThread.RunJobs();
        await w.Model.OpenAsync(repository.Root); w.Model.InboxOpen = false;
        return w;
    }

    private static Entity Place(string name, double lat, double lon) { var p = Entity.Create("place", name); p.Data["location"] = new JsonObject { ["latitude"] = lat, ["longitude"] = lon }; return p; }

    private static Entity Journey(string title, Entity from, Entity to, string start, string startZone, string end, string endZone)
    {
        var item = Entity.Create("schedule_item", title); item.Data["status"] = "confirmed";
        item.Data["time"] = new JsonObject { ["precision"] = "exact", ["start"] = new ZonedTime(start, startZone).ToJson(), ["end"] = new ZonedTime(end, endZone).ToJson() };
        item.Data["components"] = new JsonObject { ["transport"] = new JsonObject { ["type"] = "flight", ["departure"] = new JsonObject { ["place"] = from.Id.ToString() }, ["arrival"] = new JsonObject { ["place"] = to.Id.ToString() } } };
        return item;
    }

    private static IEnumerable<string?> Texts(MainWindow w) { Probe.Layout(); return Probe.All<TextBlock>(w.FindControl<Control>("InspectorHost")!).Select(t => t.Text); }

    [AvaloniaFact]
    public async Task BookingsAndGroupsChangeBothSidesAndALayoverIsShown()
    {
        var w = await OpenAsync(); var m = w.Model;
        try
        {
            var home = Place("Home airport", 50.03, 8.57); var hub = Place("Hub airport", 37.46, 126.44); var far = Place("Destination airport", 34.43, 135.23);
            var first = Journey("First flight", home, hub, "2027-03-01T13:00:00", "Europe/Berlin", "2027-03-02T07:00:00", "Asia/Seoul");
            var second = Journey("Second flight", hub, far, "2027-03-02T09:30:00", "Asia/Seoul", "2027-03-02T11:30:00", "Asia/Tokyo");
            var flights = Entity.Create("booking", "Flights"); var other = Entity.Create("booking", "Other booking");
            var tour = Entity.Create("schedule_item", "Day tour"); var temple = Entity.Create("schedule_item", "Temple");
            await m.Workspace!.ApplyAsync(new[] { home, hub, far, first, second, flights, other, tour, temple }.Select(e => new EntityEdit(e.Id, e)).ToArray());

            // The booking lists both flights and each flight points back; moving one to another booking takes it out.
            await m.LinkBookingAsync(flights.Id, [first.Id, second.Id]);
            Assert.Equal(flights.Id, TripCommands.BookingOf(m.Trip!.Find(second.Id)!));
            await m.SetBookingAsync(second.Id, other.Id);
            Assert.Equal([first.Id], MainViewModel.BookedItems(m.Trip!, flights.Id));
            Assert.Equal([second.Id], MainViewModel.BookedItems(m.Trip!, other.Id));
            await m.SetBookingAsync(second.Id, null);
            Assert.Null(TripCommands.BookingOf(m.Trip!.Find(second.Id)!));

            await m.NestAsync(temple.Id, tour.Id);
            Assert.Equal(tour.Id, TripQueries.Parent(m.Trip!, temple.Id)!.Id);
            await m.NestAsync(temple.Id, null);
            Assert.Null(TripQueries.Parent(m.Trip!, temple.Id));
            Assert.DoesNotContain(m.Workspace.State.Diagnostics, d => d.Severity == Severity.Error);

            // The first flight says how long there is to change and where, and which flight follows.
            m.Navigate("Plan"); m.Selected = m.Trip!.Find(first.Id); Probe.ShowDetails(w);
            await Probe.Until(() => Texts(w).Contains("2\u00a0h\u00a030\u00a0min to change at Hub airport"));
            Assert.Contains("Continues with", Texts(w));
            m.Selected = m.Trip!.Find(second.Id); Probe.ShowDetails(w);
            await Probe.Until(() => Texts(w).Contains("Comes from"));
        }
        finally { w.Close(); }
    }

    [AvaloniaFact]
    public async Task DetailsOpenedFromDetailsHaveAWayBackAndCloseStillCloses()
    {
        var w = await OpenAsync(); var m = w.Model;
        try
        {
            var stay = Entity.Create("schedule_item", "Lake hotel"); var booking = Entity.Create("booking", "Hotel booking");
            await m.Workspace!.ApplyAsync([new(stay.Id, stay), new(booking.Id, booking)]);
            await m.SetBookingAsync(stay.Id, booking.Id);
            m.Navigate("Plan"); m.Selected = m.Trip!.Find(stay.Id); Probe.ShowDetails(w);
            Button? Back() { Probe.Layout(); return Probe.All<Button>(w.FindControl<Control>("InspectorHost")!).FirstOrDefault(b => Avalonia.Automation.AutomationProperties.GetName(b)?.StartsWith("Back", StringComparison.Ordinal) == true); }
            Assert.Null(Back());

            m.Follow(m.Trip!.Find(booking.Id)!); Probe.ShowDetails(w);
            await Probe.Until(() => Back() is not null);
            Back()!.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(stay.Id, m.Selected?.Id);
            Assert.False(m.CanGoBackInDetails);

            // Close closes even with a way back; choosing something from the plan starts afresh.
            m.Follow(m.Trip!.Find(booking.Id)!);
            m.Selected = null;
            Assert.False(m.CanGoBackInDetails);
            m.Selected = m.Trip!.Find(stay.Id); m.Follow(m.Trip!.Find(booking.Id)!);
            m.Selected = m.Trip!.Find(stay.Id);
            Assert.False(m.CanGoBackInDetails);
        }
        finally { w.Close(); }
    }

    private static byte[] Pass()
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        using (var writer = new StreamWriter(zip.CreateEntry("pass.json").Open()))
            writer.Write("""{ "description": "Boarding pass", "organizationName": "Example Air", "barcode": { "message": "M1SAMPLE", "format": "PKBarcodeFormatQR", "messageEncoding": "iso-8859-1", "altText": "ABC123" }, "boardingPass": { "primaryFields": [ { "key": "from", "label": "From", "value": "AAA" } ] } }""");
        return stream.ToArray();
    }

    [AvaloniaFact]
    public async Task ATicketOpensFromItsPlanWithItsBarcode()
    {
        var w = await OpenAsync(); var m = w.Model;
        try
        {
            var flight = Entity.Create("schedule_item", "Flight"); await m.Workspace!.EditAsync(flight);
            var file = Path.Combine(root, "boarding.pkpass"); await File.WriteAllBytesAsync(file, Pass());
            await m.AddFilesAsync([file], flight.Id);
            m.Navigate("Plan"); m.Selected = m.Trip!.Find(flight.Id); Probe.ShowDetails(w);
            Button? ticket = null;
            await Probe.Until(() => (ticket = Probe.All<Button>(w.FindControl<Control>("InspectorHost")!).FirstOrDefault(b => Avalonia.Automation.AutomationProperties.GetName(b) == "Show ticket")) is not null);
            ticket!.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); Probe.Layout();
            var viewer = Assert.IsType<DocumentWindow>(DocumentWindow.Current);
            try { Probe.Layout(); Assert.Single(Probe.All<BarcodeView>(viewer)); Assert.Contains(Probe.All<TextBlock>(viewer), t => t.Text == "ABC123"); }
            finally { viewer.Close(); }
        }
        finally { w.Close(); }
    }

    [AvaloniaFact]
    public async Task TheConverterUsesRatesOnlyAfterAYesAndAssistantsReadTheSameFile()
    {
        var w = await OpenAsync(); var m = w.Model;
        try
        {
            await m.SetRatesAsync(false);
            Assert.Null(MainViewModel.RatesForAssistants(m.Store.Root));
            var fetched = 0;
            m.RatesSource = _ => { fetched++; return Task.FromResult(new ExchangeRates(new DateOnly(2027, 3, 1), new Dictionary<string, decimal> { ["NZD"] = 1.8m, ["JPY"] = 160m })); };
            await m.FetchRatesAsync(force: true);
            Assert.Equal(0, fetched);

            m.Navigate("Costs"); Probe.Layout();
            Assert.Contains(Probe.All<Button>(w), b => Avalonia.Automation.AutomationProperties.GetName(b) == "Fetch exchange rates");
            await m.SetRatesAsync(true);
            Assert.Equal(1, fetched);
            Assert.Equal(1600m, m.Rates!.Convert(18m, "NZD", "JPY"));
            Assert.Equal(MainViewModel.RatesFile(m.Store), MainViewModel.RatesForAssistants(m.Store.Root));
            m.Navigate("Plan"); m.Navigate("Costs"); Probe.Layout();
            await Probe.Until(() => Probe.All<TextBlock>(w).Any(t => t.Text == "Currency converter"));
            Assert.DoesNotContain(Probe.All<Button>(w), b => Avalonia.Automation.AutomationProperties.GetName(b) == "Fetch exchange rates");
        }
        finally { await m.SetRatesAsync(false); w.Close(); }
    }

    [AvaloniaFact]
    public async Task APlaceHasItsLocalScriptAndATaxiCardAndAnExpenseItsReceipt()
    {
        var w = await OpenAsync(); var m = w.Model;
        try
        {
            var hotel = Place("Lakeside Hotel", 35.68, 139.77); hotel.Data["local_name"] = "湖畔ホテル"; hotel.Data["address"] = new JsonObject { ["formatted"] = "1 Lake Road", ["local"] = "湖畔通り1" };
            var lunch = Entity.Create("expense", "Lunch"); lunch.Data["amount"] = new JsonObject { ["value"] = "12.00", ["currency"] = "EUR" }; lunch.Data["estimated"] = false;
            await m.Workspace!.ApplyAsync([new(hotel.Id, hotel), new(lunch.Id, lunch)]);
            m.Navigate("Places"); m.Selected = m.Trip!.Find(hotel.Id); Probe.ShowDetails(w);
            await Probe.Until(() => Probe.All<Button>(w.FindControl<Control>("InspectorHost")!).Any(b => Avalonia.Automation.AutomationProperties.GetName(b) == "Taxi card"));
            Assert.Contains(Probe.All<TextBox>(w.FindControl<Control>("InspectorHost")!), t => t.Text == "湖畔通り1");
            m.Navigate("Costs"); m.Selected = m.Trip!.Find(lunch.Id); Probe.ShowDetails(w);
            await Probe.Until(() => Texts(w).Contains("Choose file") || Probe.All<Button>(w.FindControl<Control>("InspectorHost")!).Any(b => Avalonia.Automation.AutomationProperties.GetName(b) == "Choose file"));
        }
        finally { w.Close(); }
    }
}
