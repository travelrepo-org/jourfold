using System.Text.Json.Nodes;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Jourfold.Mobile;
using Jourfold.Mobile.Views;
using Jourfold.UI;
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
        return TripAgenda.Build(new TripSnapshot(manifest, new Dictionary<Guid, Entity> { [item.Id] = item })).Days.SelectMany(d => d.Entries).Single(e => e.Role == AgendaRole.Item);
    }

    [Fact]
    public void PlanShowsLocalTimeAndTheTodayViewShowsThePhonesTime()
    {
        var temple = Line("2027-05-16T11:00:00", "2027-05-16T13:00:00");

        // In Tokyo both read the same, and nothing else is added.
        Assert.Equal(new ClockText("11:00", "13:00"), TimeText.Local(temple, Tokyo, English));
        Assert.Equal(new ClockText("11:00", "13:00"), TimeText.OnPhone(temple, Tokyo, English));

        // Planning from Berlin: the plan stays in Tokyo time, the phone's time is added quietly.
        Assert.Equal(new ClockText("11:00", "13:00", Other: "Berlin: 04:00"), TimeText.Local(temple, Berlin, English));
        // On the road with a phone still in Berlin time, Today leads with the phone's clock.
        Assert.Equal(new ClockText("04:00", "06:00", Other: "Tokyo: 11:00"), TimeText.OnPhone(temple, Berlin, English));
    }

    [Fact]
    public void AnotherCalendarDayIsNamedAndAJourneyNamesBothZones()
    {
        var breakfast = Line("2027-05-16T06:00:00", "2027-05-16T07:00:00");
        Assert.Equal("Berlin: 23:00 (Sat)", TimeText.Local(breakfast, Berlin, English).Other);

        var flight = Line("2027-05-14T13:20:00", "2027-05-15T08:35:00", "Europe/Berlin", "Asia/Tokyo");
        var time = TimeText.Local(flight, Tokyo, English);
        Assert.Equal("13:20", time.Start); Assert.Equal("Berlin", time.Zone); Assert.Equal("08:35 Tokyo +1", time.Arrival); Assert.Null(time.Other);
    }

    [AvaloniaFact]
    public async Task BeforeTheTripTodayShowsHowItStarts()
    {
        var now = new LocalDateTime(2027, 5, 16, 10, 40).InZoneStrictly(Tokyo).ToInstant();
        var session = new MobileSession(new MobileHost(root, new GitCliBackend(), new FakeClock(now), () => Tokyo, "en", new MemorySecrets()));
        var window = new Window { Width = 412, Height = 915, Content = new AppShell(session) }; window.Show();
        try
        {
            IEnumerable<string> Texts() { Probe.Layout(); return window.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text ?? ""); }
            // Without a trip the list welcomes instead of staying empty.
            await Probe.Until(() => Texts().Contains("Plan trips together, at your own pace"));
            await session.OpenAsync(await session.CreateSampleAsync(new LocalDate(2027, 6, 4)));
            var texts = Texts().ToList();
            Assert.Contains("The trip starts in 19 days.", texts);
            Assert.Contains(texts, t => t.StartsWith("Getting there · Friday 4 June · 13:20", StringComparison.Ordinal));
            Assert.Contains(texts, t => t.StartsWith("First stay · Saturday 5 June", StringComparison.Ordinal));
            Assert.Contains(texts, t => t.StartsWith("First activity · Saturday 5 June", StringComparison.Ordinal));
            Assert.Contains("Flight to Tokyo", texts); Assert.Contains("Hotel Gracery Shinjuku", texts);
            Assert.Contains("Open tasks before the trip: 2", texts);
        }
        finally { window.Close(); session.Dispose(); }
    }

    [AvaloniaFact]
    public async Task ASignInThatGitHubNoLongerAcceptsEndsQuietly()
    {
        var github = new FakeGitHub { Revoked = true }; var secrets = new MemorySecrets();
        await secrets.WriteAsync("github.user-token", "token");
        var session = new MobileSession(new MobileHost(root, new GitCliBackend(), NodaTime.SystemClock.Instance, () => Tokyo, "en", secrets, github, GitHubClientId: "Iv23-built"));
        await session.LoadGitHubAsync();
        await Probe.Until(() => session.GitHubLogin is null);
        Assert.Equal("GitHub no longer accepts the sign-in, for example because Jourfold was removed from your GitHub account. Sign in again.", session.SignInProblem);
        Assert.Null(await secrets.ReadAsync("github.user-token"));
    }

    [AvaloniaFact]
    public async Task APlaceTakesAPastedPositionAndBecomesTheItemsPlace()
    {
        var now = new LocalDateTime(2027, 5, 16, 10, 40).InZoneStrictly(Tokyo).ToInstant();
        var session = new MobileSession(new MobileHost(root, new GitCliBackend(), new FakeClock(now), () => Tokyo, "en", new MemorySecrets()));
        await session.OpenAsync(await session.CreateSampleAsync(new LocalDate(2027, 5, 14)));
        var window = new Window { Width = 412, Height = 915, Content = new AppShell(session) }; window.Show();
        try
        {
            var breakfast = session.Agenda.Find(new LocalDate(2027, 5, 16))!.Entries.First(e => e.Item.Title == "Breakfast at Tsukiji").Item;
            await session.AssignPlaceAsync(breakfast.Id, Jourfold.Application.PlaceSlot.Place, null);
            Assert.Null(Jourfold.Application.PlaceEdits.Current(session.Trip!, session.Trip!.Find(breakfast.Id)!, Jourfold.Application.PlaceSlot.Place));

            // From the details, a new place goes straight into the item.
            session.ShowDetails(breakfast.Id); session.ShowPlace(null, breakfast.Id, Jourfold.Application.PlaceSlot.Place); Probe.Layout();
            TextBox Box(string name) => window.GetVisualDescendants().OfType<TextBox>().Single(b => AutomationProperties.GetName(b) == name);
            Box("Title").Text = "Tsukiji Outer Market stall";
            // Typing a pair character by character does not split it early; pasting it does.
            Box("Latitude").Text = "35.6"; Assert.Equal("35.6", Box("Latitude").Text); Assert.True(string.IsNullOrEmpty(Box("Longitude").Text));
            Box("Latitude").Text = "35.6655, 139.7707"; Probe.Layout();
            Assert.Equal("35.6655", Box("Latitude").Text); Assert.Equal("139.7707", Box("Longitude").Text);
            // A map link pasted into the address gives the position too and leaves the address empty.
            Box("Address").Text = "https://www.google.com/maps/@35.6654,139.7706,17z"; Probe.Layout();
            Assert.Equal("35.6654", Box("Latitude").Text); Assert.True(string.IsNullOrEmpty(Box("Address").Text));
            Probe.Click(window, "Save");
            await Probe.Until(() => session.Showing == Overlay.Details);
            var place = Jourfold.Application.PlaceEdits.Current(session.Trip!, session.Trip!.Find(breakfast.Id)!, Jourfold.Application.PlaceSlot.Place)!;
            Assert.Equal("Tsukiji Outer Market stall", place.Title);
            Assert.Equal((35.6654, 139.7706), TravelRepo.Core.Places.Location(place));
            Assert.Equal(breakfast.Id, session.OverlayItem);
        }
        finally { window.Close(); session.Dispose(); }
    }

    [Fact]
    public void SharedTextGivesPlaceNameAddressAndPosition()
    {
        var google = new SharedContent([], "Senso-ji\n2 Chome-3-1 Asakusa, Taito City, Tokyo\nhttps://www.google.com/maps/place/Senso-ji/@35.7147651,139.7966553,17z");
        Assert.Equal((35.7147651, 139.7966553), google.Position);
        Assert.Equal(["Senso-ji", "2 Chome-3-1 Asakusa, Taito City, Tokyo"], google.Lines);
        var shortLink = new SharedContent([], "Senso-ji https://maps.app.goo.gl/AbC123");
        Assert.Null(shortLink.Position); Assert.Equal("maps.app.goo.gl", shortLink.ShortLink?.Host);
        Assert.Null(new SharedContent([], "Remember the umbrella").Position);
    }

    [AvaloniaFact]
    public async Task SharedFilesAttachToAnItemOrStayWithTheTrip()
    {
        var now = new LocalDateTime(2027, 5, 16, 10, 40).InZoneStrictly(Tokyo).ToInstant();
        var session = new MobileSession(new MobileHost(root, new GitCliBackend(), new FakeClock(now), () => Tokyo, "en", new MemorySecrets()));
        // Shared before a trip is open: the trip that opens next gets it.
        session.Receive(new SharedContent([new("ticket.pdf", "application/pdf", [1, 2, 3])], null));
        Assert.Null(session.Workspace); Assert.NotNull(session.Incoming);
        await session.OpenAsync(await session.CreateSampleAsync(new LocalDate(2027, 5, 14)));
        Assert.Equal(Overlay.Share, session.Showing);
        var flight = session.Trip!.Entities.Values.First(e => e.Title == "Flight to Tokyo");
        await session.AddFilesAsync(session.Incoming!.Files, flight.Id); session.FinishSharing(flight.Id);
        var ticket = session.Trip!.Entities.Values.Single(e => e.Type == "document" && e.Title == "ticket.pdf");
        Assert.Contains(ticket.Id, TravelRepo.Core.TripReferences.From(session.Trip!.Find(flight.Id)!).Select(r => r.Target));
        Assert.Equal(Overlay.Details, session.Showing); Assert.Null(session.Incoming);
        await session.AddFilesAsync([new("receipt.png", "image/png", [4, 5])], null);
        Assert.Contains(session.Trip!.Entities.Values, e => e.Type == "document" && e.Title == "receipt.png");
        await Assert.ThrowsAsync<TravelRepo.Core.DomainException>(() => session.AddFilesAsync([new("huge.mp4", "video/mp4", new byte[26 * 1024 * 1024])], null));

        await session.AppendNoteAsync(flight.Id, "Gate changes are announced by text.");
        Assert.EndsWith("Gate changes are announced by text.", session.Trip!.Find(flight.Id)!.Data["body"]!.ToString());
        session.Dispose();
    }

    [AvaloniaFact]
    public async Task TheMapShowsTheDaysStopsInOrder()
    {
        var now = new LocalDateTime(2027, 5, 16, 10, 40).InZoneStrictly(Tokyo).ToInstant();
        var session = new MobileSession(new MobileHost(root, new GitCliBackend(), new FakeClock(now), () => Tokyo, "en", new MemorySecrets()));
        await session.OpenAsync(await session.CreateSampleAsync(new LocalDate(2027, 5, 14)));
        var window = new Window { Width = 412, Height = 915, Content = new AppShell(session) }; window.Show();
        try
        {
            session.ShowTab(MobileTab.Map); session.ShowDate(new LocalDate(2027, 5, 16)); Probe.Layout();
            IEnumerable<string?> Names() { Probe.Layout(); return window.GetVisualDescendants().OfType<Button>().Select(AutomationProperties.GetName); }
            Assert.Contains("1. Breakfast at Tsukiji", Names()); Assert.Contains("3. teamLab Planets", Names());
            // Online maps stay off until the user turns them on.
            Assert.False(session.Settings.OnlineMaps);
            Assert.Contains("Show street map", Names());
            var map = window.GetVisualDescendants().OfType<MapPage>().Single();
            session.Select(session.Agenda.Find(new LocalDate(2027, 5, 16))!.Entries.First(e => e.Item.Title.StartsWith("teamLab", StringComparison.Ordinal)).Item.Id);
            Assert.Contains("Navigate", Names()); Assert.Contains("Details", Names());
            // The map is the same control after the page was drawn again, so it keeps its position.
            Assert.Same(map, window.GetVisualDescendants().OfType<MapPage>().Single());
        }
        finally { window.Close(); session.Dispose(); }
    }

    [AvaloniaFact]
    public async Task ANoticeShowsAboveTheDetailsAndGoesAway()
    {
        var now = new LocalDateTime(2027, 5, 16, 10, 40).InZoneStrictly(Tokyo).ToInstant();
        var session = new MobileSession(new MobileHost(root, new GitCliBackend(), new FakeClock(now), () => Tokyo, "en", new MemorySecrets()));
        await session.OpenAsync(await session.CreateSampleAsync(new LocalDate(2027, 5, 14)));
        var shell = new AppShell(session);
        var window = new Window { Width = 412, Height = 915, Content = shell }; window.Show();
        try
        {
            IEnumerable<string> Texts() { Probe.Layout(); return window.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text ?? ""); }
            session.ShowDetails(session.Agenda.Find(new LocalDate(2027, 5, 14))!.Entries.First(e => e.Item.Title == "Flight to Tokyo").Item.Id);
            shell.Notify("Copied: LH 716");
            Assert.Contains("Copied: LH 716", Texts());
            await Probe.Until(() => !Texts().Contains("Copied: LH 716"), 4000);
        }
        finally { window.Close(); session.Dispose(); }
    }

    [AvaloniaFact]
    public async Task TheSampleTripOpensOnTodayAndEveryPlaceRenders()
    {
        var now = new LocalDateTime(2027, 5, 16, 10, 40).InZoneStrictly(Tokyo).ToInstant();
        var session = new MobileSession(new MobileHost(root, new GitCliBackend(), new FakeClock(now), () => Tokyo, "en", new MemorySecrets()));
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
        var session = new MobileSession(new MobileHost(root, new GitCliBackend(), new FakeClock(now), () => Tokyo, "en", new MemorySecrets()));
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
        var session = new MobileSession(new MobileHost(root, new GitCliBackend(), new FakeClock(now), () => Tokyo, "en", new MemorySecrets()));
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

    [AvaloniaFact]
    public async Task ThePageWaitsWithRedrawingWhileSomeoneTypes()
    {
        var now = new LocalDateTime(2027, 5, 16, 10, 40).InZoneStrictly(Tokyo).ToInstant();
        var session = new MobileSession(new MobileHost(root, new GitCliBackend(), new FakeClock(now), () => Tokyo, "en", new MemorySecrets()));
        await session.OpenAsync(await session.CreateSampleAsync(new LocalDate(2027, 5, 14)));
        var window = new Window { Width = 412, Height = 915, Content = new AppShell(session) }; window.Show();
        try
        {
            TextBox Field() { Probe.Layout(); return window.GetVisualDescendants().OfType<TextBox>().Single(t => t.PlaceholderText == "Describe this version"); }
            session.ShowPage("sync"); await session.RefreshVersionsAsync();
            var field = Field();
            field.Focus(); field.Text = "Before the Kyoto days";

            // A change in the background, such as a finished sync, does not replace the field being typed in.
            var wifi = session.Trip!.Entities.Values.Single(e => e.Title == "Rent pocket Wi-Fi");
            await session.SetTaskDoneAsync(wifi.Id, true); await session.SaveChangesAsync();
            Assert.Same(field, Field());
            Assert.True(field.IsFocused);

            // Leaving the field draws what waited; the text is kept.
            Probe.Button(window, "Create version").Focus();
            Probe.Layout();
            Assert.NotSame(field, Field());
            Assert.Equal("Before the Kyoto days", Field().Text);
            Assert.Contains("Changes made on the phone", window.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text));

            // Going somewhere else is drawn at once, even with a field focused.
            Field().Focus(); session.ShowPage(null); Probe.Layout();
            Assert.Contains("Sync and versions", window.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text));
        }
        finally { window.Close(); session.Dispose(); }
    }

    [AvaloniaFact]
    public async Task SigningInToGitHubShowsTheCodeAndRemembersTheAccount()
    {
        var github = new FakeGitHub();
        var session = new MobileSession(new MobileHost(root, new GitCliBackend(), NodaTime.SystemClock.Instance, () => Tokyo, "en", new MemorySecrets(), github));
        await session.SignInAsync();
        Assert.Equal("Enter the client ID of your GitHub App first.", session.SignInProblem);

        var signIn = session.SignInAsync("Iv23-test");
        while (session.SignInCode is null && !signIn.IsCompleted) await Task.Delay(20);
        Assert.Equal("WDJB-MJHT", session.SignInCode?.UserCode);
        await signIn;
        Assert.Null(session.SignInProblem);
        Assert.Equal("sam-travels", session.GitHubLogin);
        Assert.Equal("Sam Taylor", session.Settings.Name);
        Assert.Equal("Iv23-test", MobileSettings.Load(Path.Combine(root, "settings.json")).GitHubClientId);

        // A new start reads the sign-in without going online.
        var again = new MobileSession(new MobileHost(root, new GitCliBackend(), NodaTime.SystemClock.Instance, () => Tokyo, "en", session.Host.Secrets, github));
        await again.LoadGitHubAsync();
        Assert.Equal("sam-travels", again.GitHubLogin);
        await again.SignOutAsync();
        Assert.Null(again.GitHubLogin);
    }

    [AvaloniaFact]
    public async Task TwoPhonesSyncAndCombineTheirChanges()
    {
        var git = new GitCliBackend();
        var shared = Path.Combine(root, "shared.git"); Directory.CreateDirectory(shared);
        Assert.Equal(0, (await git.ExecuteAsync(shared, ["init", "--bare", "-b", "main"])).ExitCode);
        var secrets = new MemorySecrets(); await secrets.WriteAsync("github.user-token", "token");
        MobileSession Phone(string name)
        {
            var settings = Path.Combine(root, name); Directory.CreateDirectory(settings);
            File.WriteAllText(Path.Combine(settings, "settings.json"), """{ "GitHubLogin": "alex" }""");
            // Loading the sign-in asks GitHub whether it still counts; the fake says it does.
            return new MobileSession(new MobileHost(settings, git, NodaTime.SystemClock.Instance, () => Tokyo, "en", secrets, new FakeGitHub()));
        }
        var alex = Phone("alex"); var sam = Phone("sam");
        await alex.LoadGitHubAsync(); await sam.LoadGitHubAsync();
        var path = await alex.CreateSampleAsync(new LocalDate(2027, 5, 14));
        await alex.OpenAsync(path);
        await alex.Workspace!.Git.AddRemoteAsync("origin", shared);
        await alex.OpenAsync(path);
        Assert.Equal("origin", alex.Remote);
        // Opening a shared trip syncs it in the background.
        while (alex.Syncing) await Task.Delay(20);
        Assert.Null(alex.SyncProblem); Assert.NotNull(alex.LastSync);

        await sam.GetTripAsync(new TravelRepo.Providers.RemoteRepository("alex/spring-in-japan", shared, "https://github.com/alex/spring-in-japan", true));
        Assert.Equal("Spring in Japan", sam.Trip!.Manifest.Title);
        while (sam.Syncing) await Task.Delay(20);

        var wifi = alex.Trip!.Entities.Values.Single(e => e.Title == "Rent pocket Wi-Fi");
        var passport = sam.Trip!.Entities.Values.Single(e => e.Title == "Check passport validity");
        await alex.SetTaskDoneAsync(wifi.Id, true); await alex.AutoSaveAsync();
        await sam.SetTaskDoneAsync(passport.Id, false); await sam.AutoSaveAsync();
        Assert.Null(sam.SyncProblem);
        Assert.Equal("completed", sam.Trip!.Find(wifi.Id)!.Data["status"]!.ToString());
        await alex.SyncAsync(quiet: false);
        Assert.Equal("open", alex.Trip!.Find(passport.Id)!.Data["status"]!.ToString());
        alex.Dispose(); sam.Dispose();
    }

    private sealed class MemorySecrets : TravelRepo.Providers.ISecretStore
    {
        private readonly Dictionary<string, string> values = [];
        public Task<string?> ReadAsync(string key, CancellationToken ct = default) => Task.FromResult(values.GetValueOrDefault(key));
        public Task WriteAsync(string key, string value, CancellationToken ct = default) { values[key] = value; return Task.CompletedTask; }
        public Task DeleteAsync(string key, CancellationToken ct = default) { values.Remove(key); return Task.CompletedTask; }
    }

    /// <summary>GitHub's device sign-in, answered at once.</summary>
    [AvaloniaFact]
    public async Task ANightFlightShowsOnTheDayItLandsAndThePlanNamesItsTimes()
    {
        var now = new LocalDateTime(2027, 5, 16, 10, 40).InZoneStrictly(Tokyo).ToInstant();
        var session = new MobileSession(new MobileHost(root, new GitCliBackend(), new FakeClock(now), () => Berlin, "en", new MemorySecrets()));
        await session.OpenAsync(await session.CreateSampleAsync(new LocalDate(2027, 5, 14)));
        var window = new Window { Width = 412, Height = 915, Content = new AppShell(session) }; window.Show();
        try
        {
            IEnumerable<string> Texts() { Probe.Layout(); return window.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text ?? ""); }
            // The sample's flight leaves Frankfurt on the first day and lands in Tokyo the next morning.
            var flight = session.Agenda.Find(new LocalDate(2027, 5, 15))!.Entries.Single(e => e.Role == AgendaRole.Continued);
            Assert.Equal(ScheduleKind.Transport, flight.Kind);
            session.ShowTab(MobileTab.Plan); session.ShowDate(new LocalDate(2027, 5, 15));
            Assert.Contains(Texts(), t => t.StartsWith("Arrival", StringComparison.Ordinal));
            Assert.Contains("Day 2 · Local time Tokyo", Texts());
            // The stay's check-in says how many nights; the night line is not repeated under it.
            Assert.Contains("3 nights", Texts());
            Assert.DoesNotContain(Texts(), t => t.Contains("Night 1 of", StringComparison.Ordinal));
            Assert.Contains(Texts(), t => t.StartsWith("Berlin: ", StringComparison.Ordinal));
            // Today follows the phone and shows the flight once, with its own times.
            session.ShowTab(MobileTab.Today);
            Assert.Contains("Times as on your phone (Berlin)", Texts());
        }
        finally { window.Close(); session.Dispose(); }
    }

    [AvaloniaFact]
    public async Task TheLongPressMenusMoveCopyAndRemove()
    {
        var now = new LocalDateTime(2027, 5, 16, 10, 40).InZoneStrictly(Tokyo).ToInstant();
        var session = new MobileSession(new MobileHost(root, new GitCliBackend(), new FakeClock(now), () => Tokyo, "en", new MemorySecrets()));
        var path = await session.CreateSampleAsync(new LocalDate(2027, 5, 14));
        await session.OpenAsync(path);
        var window = new Window { Width = 412, Height = 915, Content = new AppShell(session) }; window.Show();
        try
        {
            var senso = session.Agenda.Find(new LocalDate(2027, 5, 16))!.Entries.Single(e => e.Item.Title == "Senso-ji and Asakusa");
            var menu = Jourfold.Mobile.Views.Menus.Item(session, senso.Item);
            Assert.Equal(["Details", "Edit", "Change status", "Navigate", "Duplicate", "Move to another day", "Delete"], menu.Actions.Select(a => a.Label));
            session.ShowMenu(menu); Probe.Layout();
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Move to another day");

            // Moving keeps the local time; the plan follows to the new day.
            await session.ChooseAsync(menu.Actions.Single(a => a.Label == "Move to another day"), null);
            var days = session.Menu!;
            await session.ChooseAsync(days.Actions.First(a => a.Label.StartsWith("Tue 18 May", StringComparison.Ordinal)), null);
            var moved = session.Entry(senso.Item.Id)!;
            Assert.Equal(new LocalDateTime(2027, 5, 18, 11, 0).InZoneStrictly(Tokyo).ToInstant(), moved.Start);
            Assert.Equal(senso.End - senso.Start, moved.End - moved.Start);
            Assert.Equal(new LocalDate(2027, 5, 18), session.SelectedDate);

            await session.DuplicateAsync(senso.Item.Id);
            Assert.Contains(session.Agenda.Find(new LocalDate(2027, 5, 18))!.Entries, e => e.Item.Title == "Senso-ji and Asakusa (copy)");

            // A trip only on this phone says it will be gone; removing it deletes the folder, Git files included.
            session.Close();
            await session.LoadTripsAsync();
            var trip = Jourfold.Mobile.Views.Menus.Trip(session, session.Trips.Single());
            await session.ChooseAsync(trip.Actions.Single(a => a.Danger), null);
            Assert.Contains("only on this phone", session.Asking!.Text);
            await session.AnswerAsync(true);
            Assert.False(Directory.Exists(path));
            Assert.Empty(session.Trips);
        }
        finally { window.Close(); session.Dispose(); }
    }

    [AvaloniaFact]
    public async Task ABookingEmailOpensOnThePhoneWithItsAttachments()
    {
        var now = new LocalDateTime(2027, 5, 16, 10, 40).InZoneStrictly(Tokyo).ToInstant();
        var session = new MobileSession(new MobileHost(root, new GitCliBackend(), new FakeClock(now), () => Tokyo, "en", new MemorySecrets()));
        await session.OpenAsync(await session.CreateSampleAsync(new LocalDate(2027, 5, 14)));
        var mail = Path.Combine(root, "booking.eml");
        await File.WriteAllTextAsync(mail, """
            From: teamLab Tickets <tickets@example.invalid>
            To: Alex <alex@example.invalid>
            Subject: Your tickets for teamLab Planets
            Date: Mon, 3 May 2027 09:12:00 +0900
            MIME-Version: 1.0
            Content-Type: multipart/mixed; boundary="b"

            --b
            Content-Type: text/plain; charset=utf-8

            Booking TLP-55120, Sunday 16 May, entry 15:30.
            --b
            Content-Type: application/pdf; name="tickets.pdf"
            Content-Disposition: attachment; filename="tickets.pdf"
            Content-Transfer-Encoding: base64

            JVBERi0xLjQK
            --b--
            """.Replace("\n", "\r\n"));
        var document = await session.Workspace!.ImportDocumentAsync(mail, "message/rfc822");
        var planets = session.Trip!.Entities.Values.Single(e => e.Type == "schedule_item" && e.Title == "teamLab Planets");
        var copy = planets.Copy(); copy.Data["documents"] = new JsonArray(document.Id.ToString()); await session.Workspace.EditAsync(copy);
        string? opened = null;
        var window = new Window { Width = 412, Height = 915, Content = new AppShell(session) }; window.Show();
        try
        {
            IEnumerable<string> Texts() { Probe.Layout(); return window.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text ?? ""); }
            session.ShowDetails(planets.Id);
            Probe.Layout();
            Probe.Click(window, document.Title);
            Assert.Contains("Your tickets for teamLab Planets", Texts());
            Assert.Contains(Texts(), t => t.Contains("TLP-55120", StringComparison.Ordinal));
            Assert.Contains("tickets.pdf", Texts());

            // A PDF goes to another app, as a copy in the shared folder.
            var host = session.Host with { OpenFile = (p, media) => { opened = p + " " + media; return Task.FromResult(true); } };
            var phone = new MobileSession(host);
            Assert.True(await phone.OpenElsewhereAsync(new ShownFile("tickets.pdf", "application/pdf", [1, 2, 3])));
            Assert.Equal(Path.Combine(root, "shared", "tickets.pdf") + " application/pdf", opened);

            session.CloseFile();
            Assert.Empty(session.Viewing);
            Assert.Equal(Overlay.Details, session.Showing);
        }
        finally { window.Close(); session.Dispose(); }
    }

    [AvaloniaFact]
    public async Task ASignInGoesOnAfterTheAppWasStoppedInTheBrowser()
    {
        var github = new FakeGitHub(); var secrets = new MemorySecrets();
        var first = new MobileSession(new MobileHost(root, new GitCliBackend(), NodaTime.SystemClock.Instance, () => Tokyo, "en", secrets, github));
        var signIn = first.SignInAsync("Iv23-test");
        while (first.SignInCode is null) await Task.Delay(20);
        // Android ends the app while the code is entered in the browser; the next start waits for GitHub again.
        first.CancelSignIn(); await signIn;
        await secrets.WriteAsync("github.pending-sign-in", System.Text.Json.JsonSerializer.Serialize(new { Code = new TravelRepo.Providers.DeviceAuthorization("device", "WDJB-MJHT", "https://github.com/login/device", 900, 0), Expires = DateTimeOffset.UtcNow.AddMinutes(10) }));
        var again = new MobileSession(new MobileHost(root, new GitCliBackend(), NodaTime.SystemClock.Instance, () => Tokyo, "en", secrets, github));
        var load = again.LoadGitHubAsync();
        while (again.SignInCode is null && !load.IsCompleted) await Task.Delay(20);
        Assert.Equal("WDJB-MJHT", again.SignInCode?.UserCode);
        await load;
        Assert.Equal("sam-travels", again.GitHubLogin);
        Assert.Null(await secrets.ReadAsync("github.pending-sign-in"));
    }

    [AvaloniaFact]
    public async Task ASignInWithoutRepositoriesLeadsToChoosingThemOnGitHub()
    {
        var github = new FakeGitHub { Installed = false };
        var session = new MobileSession(new MobileHost(root, new GitCliBackend(), NodaTime.SystemClock.Instance, () => Tokyo, "en", new MemorySecrets(), github, GitHubClientId: "Iv23-built", GitHubAppSlug: "jourfold"));
        await session.SignInAsync();
        // Signing in does not install the App, so it sees no repository yet.
        Assert.True(session.NeedsRepositoryAccess);
        Assert.Equal("https://github.com/apps/jourfold/installations/new", session.RepositoryAccessUri?.AbsoluteUri);
        Assert.True(Offers(GitHubView.FindTrips(session), "Choose repositories on GitHub"));

        github.Installed = true;
        var progress = new List<TravelRepo.Providers.DiscoveryProgress>();
        session.Changed += () => { if (session.Searching is { } p) progress.Add(p); };
        await session.FindRemoteTripsAsync();
        Assert.False(session.NeedsRepositoryAccess);
        Assert.Null(session.Searching);
        Assert.Equal(["sam-travels/kyoto"], session.RemoteTrips!.Select(t => t.FullName));
        Assert.Contains(progress, p => p.Total == 2);
        // A trip in a repository the App was not given can be added from the list.
        Assert.True(Offers(GitHubView.RemoteTrips(session, session.RemoteTrips!), "Choose repositories on GitHub"));

        static bool Offers(Control page, string button)
        {
            var window = new Window { Width = 412, Height = 915, Content = page }; window.Show();
            try { return Ui.FindDescendant<Button>(page, b => AutomationProperties.GetName(b) == button) is not null; }
            finally { window.Close(); }
        }
    }

    private sealed class FakeGitHub : HttpMessageHandler
    {
        public bool Installed { get; set; } = true;
        /// <summary>The user removed Jourfold from their account: every request with the token is refused.</summary>
        public bool Revoked { get; set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (Revoked && request.Headers.Authorization is not null) return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.Unauthorized));
            var trip = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(TravelRepo.Serialization.YamlCodec.Write(Entity.CreateTrip("Kyoto"))));
            static string Repo(string name) => $$"""{ "full_name": "sam-travels/{{name}}", "clone_url": "https://github.com/sam-travels/{{name}}.git", "html_url": "https://github.com/sam-travels/{{name}}", "private": true, "size": 4 }""";
            var body = request.RequestUri!.AbsolutePath switch
            {
                "/login/device/code" => """{ "device_code": "device", "user_code": "WDJB-MJHT", "verification_uri": "https://github.com/login/device", "expires_in": 900, "interval": 0 }""",
                "/login/oauth/access_token" => """{ "access_token": "token" }""",
                "/user" => """{ "login": "sam-travels", "name": "Sam Taylor" }""",
                "/user/installations" => Installed ? """{ "total_count": 1, "installations": [{ "id": 7 }] }""" : """{ "total_count": 0, "installations": [] }""",
                "/user/installations/7/repositories" => "{ \"total_count\": 2, \"repositories\": [" + Repo("kyoto") + ", " + Repo("dotfiles") + "] }",
                "/repos/sam-travels/kyoto/contents/travel.yaml" => "{ \"content\": \"" + trip + "\" }",
                "/repos/sam-travels/dotfiles/contents/travel.yaml" => null,
                _ => throw new InvalidOperationException(request.RequestUri.ToString())
            };
            return Task.FromResult(body is null ? new HttpResponseMessage(System.Net.HttpStatusCode.NotFound) : new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent(body) });
        }
    }

    private sealed class FakeClock(Instant now) : IClock { public Instant GetCurrentInstant() => now; }
}
