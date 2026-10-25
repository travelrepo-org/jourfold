using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Jourfold.Mobile;
using NodaTime;
using TravelRepo.Core;
using TravelRepo.Git;
using Xunit;
namespace Jourfold.Tests;

public sealed class MobileTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "jourfold-mobile-" + Guid.NewGuid());
    public void Dispose() => TestFiles.Delete(root);

    private static readonly DateTimeZone Tokyo = DateTimeZoneProviders.Tzdb["Asia/Tokyo"], Berlin = DateTimeZoneProviders.Tzdb["Europe/Berlin"];
    private static readonly Localization English = new("en");

    private static AgendaEntry Line(string start, string end, string zone = "Asia/Tokyo", string? endZone = null)
    {
        var item = Entity.Create("schedule_item", "Item");
        item.Data["time"] = new JsonObject { ["precision"] = "exact", ["start"] = new ZonedTime(start, zone).ToJson(), ["end"] = new ZonedTime(end, endZone ?? zone).ToJson() };
        var manifest = Entity.CreateTrip("Trip", "en", "Asia/Tokyo");
        return TripAgenda.Build(new TripSnapshot(manifest, new Dictionary<Guid, Entity> { [item.Id] = item })).Days.SelectMany(d => d.Entries).Single();
    }

    [Fact]
    public void PlanShowsLocalTimeAndTheTodayViewShowsThePhonesTime()
    {
        var temple = Line("2027-05-16T11:00:00", "2027-05-16T13:00:00");

        // In Tokyo both read the same, and nothing else is added.
        Assert.Equal(new ClockText("11:00", "13:00"), TimeText.Local(temple, Tokyo, English));
        Assert.Equal(new ClockText("11:00", "13:00"), TimeText.OnPhone(temple, Tokyo, English));

        // Planning from Berlin: the plan stays in Tokyo time, the phone's time is added quietly.
        Assert.Equal(new ClockText("11:00", "13:00", Other: "04:00 CEST"), TimeText.Local(temple, Berlin, English));
        // On the road with a phone still in Berlin time, Today leads with the phone's clock.
        Assert.Equal(new ClockText("04:00", "06:00", Other: "11:00 JST"), TimeText.OnPhone(temple, Berlin, English));
    }

    [Fact]
    public void AnotherCalendarDayIsNamedAndAJourneyNamesBothZones()
    {
        var breakfast = Line("2027-05-16T06:00:00", "2027-05-16T07:00:00");
        Assert.Equal("Sat 23:00 CEST", TimeText.Local(breakfast, Berlin, English).Other);

        var flight = Line("2027-05-14T13:20:00", "2027-05-15T08:35:00", "Europe/Berlin", "Asia/Tokyo");
        var time = TimeText.Local(flight, Tokyo, English);
        Assert.Equal("13:20", time.Start); Assert.Equal("CEST", time.Zone); Assert.Equal("08:35 JST +1", time.Arrival); Assert.Null(time.Other);
    }

    [AvaloniaFact]
    public async Task TheSampleTripOpensOnTodayAndEveryPlaceRenders()
    {
        var now = new LocalDateTime(2027, 5, 16, 10, 40).InZoneStrictly(Tokyo).ToInstant();
        var session = new MobileSession(new MobileHost(root, new GitCliBackend(), new FakeClock(now), () => Tokyo, "en"));
        await session.OpenAsync(await session.CreateSampleAsync(new LocalDate(2027, 5, 14)));
        var window = new Window { Width = 412, Height = 915, Content = new AppShell(session) }; window.Show();
        try
        {
            IEnumerable<string> Texts() { Probe.Layout(); return window.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text ?? ""); }
            Assert.Equal(MobileTab.Today, session.Tab);
            Assert.Contains("Sunday 16 May · Day 3", Texts());
            Assert.Contains("Senso-ji and Asakusa", Texts());
            Assert.Contains("Now", Texts());
            foreach (var tab in Enum.GetValues<MobileTab>()) { session.ShowTab(tab); Assert.NotEmpty(Texts()); }
            session.ShowTab(MobileTab.Plan); session.ShowDate(new LocalDate(2027, 5, 17));
            var nikko = session.Agenda.Find(new LocalDate(2027, 5, 17))!.Entries.Single(e => e.IsGroup);
            Assert.DoesNotContain("Kegon Falls", Texts());
            session.ToggleGroup(nikko.Item.Id);
            Assert.Contains("Kegon Falls", Texts());
            Assert.Contains("Ideas", Texts());
        }
        finally { window.Close(); session.Dispose(); }
    }

    [AvaloniaFact]
    public async Task QuickAddDetailsStatusEditAndDeleteWorkOnThePhone()
    {
        var now = new LocalDateTime(2027, 5, 16, 10, 40).InZoneStrictly(Tokyo).ToInstant();
        var session = new MobileSession(new MobileHost(root, new GitCliBackend(), new FakeClock(now), () => Tokyo, "en"));
        await session.OpenAsync(await session.CreateSampleAsync(new LocalDate(2027, 5, 14)));
        var window = new Window { Width = 412, Height = 915, Content = new AppShell(session) }; window.Show();
        try
        {
            IEnumerable<string> Texts() { Probe.Layout(); return window.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text ?? ""); }
            session.ShowQuickAdd(); Assert.Contains("Only an idea", Texts());
            await session.AddAsync(new Jourfold.Application.QuickAddDraft("food", "Ramen near Shinjuku station") { Date = new LocalDate(2027, 5, 16), Start = new LocalTime(20, 0), Length = Duration.FromHours(1) }, idea: true);
            Assert.Equal(Overlay.None, session.Showing);
            var ramen = session.Agenda.Find(new LocalDate(2027, 5, 16))!.Entries.Single(e => e.Item.Title == "Ramen near Shinjuku station");
            Assert.True(ramen.IsIdea);

            session.ShowDetails(ramen.Item.Id);
            Assert.Contains("Status", Texts());
            await session.SetStatusAsync(ramen.Item.Id, "planned");
            Assert.Equal("planned", session.Entry(ramen.Item.Id)!.Status);
            await session.SaveEditAsync(ramen.Item.Id, "Ramen in Shinjuku", "Ichiran, ticket machine at the door", new LocalDate(2027, 5, 16), new LocalTime(20, 30), null);
            var edited = session.Entry(ramen.Item.Id)!;
            Assert.Equal("Ramen in Shinjuku", edited.Item.Title);
            Assert.Equal(new LocalDateTime(2027, 5, 16, 20, 30).InZoneStrictly(Tokyo).ToInstant(), edited.Start);
            Assert.Equal(Duration.FromHours(1), edited.End - edited.Start);

            session.Confirm("Delete?", "Delete", () => session.DeleteAsync(ramen.Item.Id));
            Assert.Contains("Delete?", Texts());
            await session.AnswerAsync(true);
            Assert.Null(session.Entry(ramen.Item.Id));
        }
        finally { window.Close(); session.Dispose(); }
    }

    [AvaloniaFact]
    public async Task ThePagesUnderMoreRenderAndChangesBecomeVersions()
    {
        var now = new LocalDateTime(2027, 5, 16, 10, 40).InZoneStrictly(Tokyo).ToInstant();
        var session = new MobileSession(new MobileHost(root, new GitCliBackend(), new FakeClock(now), () => Tokyo, "en"));
        var path = await session.CreateSampleAsync(new LocalDate(2027, 5, 14));
        await session.SaveSettingsAsync("Sam Taylor", "sam@example.invalid");
        await session.OpenAsync(path);
        var window = new Window { Width = 412, Height = 915, Content = new AppShell(session) }; window.Show();
        try
        {
            IEnumerable<string> Texts() { Probe.Layout(); return window.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text ?? ""); }
            session.ShowPage("bookings");
            Assert.Contains("Reserved", Texts()); Assert.Contains("Ref. GR-1187", Texts());
            var ryokan = session.Trip!.Entities.Values.Single(e => e.Type == "booking" && e.Data["reference"]?.ToString() == "GR-1187");
            Assert.DoesNotContain("GR-1187", Texts());
            session.ToggleGroup(ryokan.Id);
            Assert.Contains("GR-1187", Texts());

            session.ShowPage("costs");
            Assert.Contains("JPY 35,600.00 left", Texts());
            Assert.Contains("Estimated", Texts());

            session.ShowPage("tasks");
            Assert.Contains("Overdue", Texts());
            var wifi = session.Trip!.Entities.Values.Single(e => e.Title == "Rent pocket Wi-Fi");
            await session.SetTaskDoneAsync(wifi.Id, true);
            Assert.Equal("completed", session.Trip!.Find(wifi.Id)!.Data["status"]!.ToString());
            Assert.True(session.HasChanges);

            // Leaving the app keeps the change as a version under the name set on this phone.
            await session.SaveChangesAsync();
            Assert.False(session.HasChanges);
            await session.RefreshVersionsAsync();
            Assert.Equal("Changes made on the phone", session.History[0].Message.Split('\n')[0]);
            Assert.Equal("Sam Taylor", session.History[0].Author);

            await session.SetTaskDoneAsync(wifi.Id, false);
            await session.CreateVersionAsync("Wi-Fi still to rent");
            session.ShowPage("sync");
            await session.RefreshVersionsAsync();
            Assert.Contains("Wi-Fi still to rent", Texts());
            Assert.Contains("All changes saved on this phone", Texts());

            session.ShowPage("travellers"); Assert.Contains("Alex", Texts());
            session.ShowPage("settings"); Assert.Contains("About Jourfold", Texts());
            Assert.Equal("Sam Taylor", MobileSettings.Load(Path.Combine(root, "settings.json")).Name);
        }
        finally { window.Close(); session.Dispose(); }
    }

    private sealed class FakeClock(Instant now) : IClock { public Instant GetCurrentInstant() => now; }
}
