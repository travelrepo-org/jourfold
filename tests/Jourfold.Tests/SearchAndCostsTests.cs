using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Jourfold.Desktop;
using Jourfold.Infrastructure;
using Jourfold.Mobile;
using Jourfold.Mobile.Controls;
using Jourfold.Mobile.Views;
using Jourfold.UI;
using NodaTime;
using TravelRepo.Core;
using TravelRepo.Git;
using Xunit;
namespace Jourfold.Tests;

/// <summary>
/// The search over the trip on phones, tablets and the computer; the bar's third button; and the costs at a glance:
/// the category's icon on every payment, the total above the categories and the category a title suggests.
/// </summary>
public sealed class SearchAndCostsTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "jourfold-search-" + Guid.NewGuid());
    public void Dispose() => TestFiles.Delete(root);

    private static readonly DateTimeZone Tokyo = DateTimeZoneProviders.Tzdb["Asia/Tokyo"];

    private async Task<(MobileSession Session, Window Window)> OpenAsync(string language = "en", double width = 412, double height = 915)
    {
        var now = new LocalDateTime(2027, 5, 16, 10, 40).InZoneStrictly(Tokyo).ToInstant();
        var session = new MobileSession(new MobileHost(root, new GitCliBackend(), new FakeClock(now), () => Tokyo, language, new MemorySecrets()));
        await session.OpenAsync(await session.CreateSampleAsync(new LocalDate(2027, 5, 14)));
        var window = new Window { Width = width, Height = height, Content = new AppShell(session) }; window.Show();
        Probe.Layout();
        return (session, window);
    }

    private static void Back(Window window)
    {
        var shell = (AppShell)window.Content!;
        shell.RaiseEvent(new Avalonia.Input.KeyEventArgs { RoutedEvent = Avalonia.Input.InputElement.KeyDownEvent, Key = Avalonia.Input.Key.Escape, Source = shell });
        Probe.Layout();
    }
    private static IEnumerable<string?> Buttons(Window window) { Probe.Layout(); return Probe.All<Button>(window).Where(b => b.IsEffectivelyVisible).Select(AutomationProperties.GetName).ToArray(); }
    private static bool Shows(Window window, string text) { Probe.Layout(); return Probe.All<TextBlock>(window).Any(t => (t.Text ?? t.Inlines?.Text) == text && t.IsEffectivelyVisible); }
    private static TextBox Box(Window window, string name) { Probe.Layout(); return Probe.All<TextBox>(window).First(b => AutomationProperties.GetName(b) == name); }
    private static Button[] Results(Window window) { Probe.Layout(); return Probe.All<Button>(window).Where(b => b.Tag is string tag && tag.StartsWith("search:", StringComparison.Ordinal)).ToArray(); }

    private static async Task TypeAsync(Window window, string name, string text)
    {
        Box(window, name).Text = text;
        await Task.Delay(SearchView.Pause + TimeSpan.FromMilliseconds(120)); Probe.Layout();
    }

    [AvaloniaFact]
    public async Task TheThirdButtonOpensTheCostsAsAPlaceAndCanBeChanged()
    {
        var (session, window) = await OpenAsync();
        try
        {
            Assert.Equal("costs", session.ThirdPlace);
            var bar = Probe.All<Button>(window).Where(b => b.Classes.Contains("tab")).Select(b => AutomationProperties.GetName(b) ?? "").ToArray();
            Assert.Equal(["Today", "Plan", "Costs", "More"], bar);

            Probe.Click(window, "Costs"); Probe.Layout();
            Assert.Equal((MobileTab.More, "costs"), (session.Tab, session.Page));
            Assert.True(session.PageIsPlace);
            // A place of its own: marked in the bar, More is not, and there is no way back on the page.
            Assert.True(((ChoiceButton)Probe.Button(window, "Costs")).IsSelected);
            Assert.False(((ChoiceButton)Probe.Button(window, "More")).IsSelected);
            Assert.DoesNotContain(Probe.All<Button>(window), b => b.Classes.Contains("ghost") && AutomationProperties.GetName(b) == "More");
            // A page opened from it leads back to it; back from the place itself leads to Today.
            session.OpenPage("converter"); Probe.Layout();
            Assert.Equal("Back", session.BackLabel);
            Back(window);
            Assert.Equal((MobileTab.More, "costs"), (session.Tab, session.Page));
            Assert.True(session.PageIsPlace);
            Back(window);
            Assert.Equal(MobileTab.Today, session.Tab);

            // The map moved to More, and back from it returns there.
            session.ShowTab(MobileTab.More); Probe.Layout();
            Probe.Click(window, "Map"); Probe.Layout();
            Assert.Equal(MobileTab.Map, session.Tab);
            Assert.True(((ChoiceButton)Probe.Button(window, "More")).IsSelected);
            Back(window);
            Assert.Equal((MobileTab.More, (string?)null), (session.Tab, session.Page));
            // Opened from More, the costs still have their way back.
            Probe.Click(window, "Costs"); Probe.Layout();

            // Chosen in the settings, and remembered on this device.
            session.ShowPage("settings"); Probe.Layout();
            Probe.Click(window, "Third button of the bar: Map"); Probe.Layout();
            Assert.Equal("map", session.ThirdPlace);
            Assert.Equal(["Today", "Plan", "Map", "More"], Probe.All<Button>(window).Where(b => b.Classes.Contains("tab")).Select(b => AutomationProperties.GetName(b) ?? "").ToArray());
            Assert.Equal("map", new MobileSession(session.Host).ThirdPlace);
            session.ShowTab(MobileTab.More); Probe.Layout();
            Assert.DoesNotContain(Probe.All<Button>(window), b => b.Classes.Contains("row") && AutomationProperties.GetName(b) == "Map");
            Probe.Click(window, "Map"); Probe.Layout();
            Assert.Equal(MobileTab.Map, session.Tab);
            Back(window);
            Assert.Equal(MobileTab.Today, session.Tab);
        }
        finally { window.Close(); session.Dispose(); }
    }

    [AvaloniaFact]
    public async Task ACategoryTappedInByCategoryNarrowsThePaymentsUntilItsChipIsTapped()
    {
        var (session, window) = await OpenAsync(height: 4000);
        try
        {
            session.ShowThirdPlace(); Probe.Layout();
            var trip = session.Trip!;
            string[] Shown() { Probe.Layout(); return Probe.All<Jourfold.UI.Icon>(window).Where(i => i.Tag is string tag && tag.StartsWith("category:", StringComparison.Ordinal)).Select(i => (string)i.Tag!).ToArray(); }
            var all = Shown();
            Assert.Equal(trip.Entities.Values.Count(e => e.Type == "expense"), all.Length);

            // The row of Transport under "By category" lists only transport below, with a chip in the search field.
            Probe.Click(window, "Show only payments for Transport"); Probe.Layout();
            Assert.Equal("transport", session.CostCategory);
            var transport = Shown();
            Assert.NotEmpty(transport);
            Assert.All(transport, tag => Assert.Equal("category:transport", tag));
            Assert.Equal(all.Count(t => t == "category:transport"), transport.Length);
            var search = Probe.All<TextBox>(window).Single(b => AutomationProperties.GetName(b) == "Search: name, category, person, place");
            Assert.NotNull(search.InnerRightContent);

            // The search still narrows within the category, and the chip lists every payment again.
            Probe.Click(window, "Show all categories again"); Probe.Layout();
            Assert.Null(session.CostCategory);
            Assert.Equal(all.Length, Shown().Length);
            Assert.Null(Probe.All<TextBox>(window).Single(b => AutomationProperties.GetName(b) == "Search: name, category, person, place").InnerRightContent);
        }
        finally { window.Close(); session.Dispose(); }
    }

    [AvaloniaFact]
    public async Task ACategoryTappedOnAPhoneScrollsToItsPaymentsWithTheSearchOnTop()
    {
        var (session, window) = await OpenAsync();
        try
        {
            session.ShowThirdPlace(); Probe.Layout();
            var row = Probe.Button(window, "Show only payments for Activities");
            row.BringIntoView(); Probe.Layout();
            row.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            TextBox Search() => Probe.All<TextBox>(window).Single(b => AutomationProperties.GetName(b) == "Search: name, category, person, place");
            // The page moves on to the narrowed list: its search, with the category's chip, at the top, or the whole of it
            // above the bar when the list is too short for that.
            bool Shown(TextBox search) => search.GetVisualAncestors().OfType<ScrollViewer>().First() is var page && search.TranslatePoint(default, page) is { Y: >= 0 } at
                && (at.Y < 40 || page.Offset.Y >= page.Extent.Height - page.Viewport.Height - 1) && at.Y + search.Bounds.Height <= page.Viewport.Height;
            await Probe.Until(() => Shown(Search()));
            Assert.NotNull(Search().InnerRightContent);
        }
        finally { window.Close(); session.Dispose(); }
    }

    [AvaloniaFact]
    public async Task PaymentsShowTheirCategoryAndTheCategoriesTheirTotal()
    {
        var (session, window) = await OpenAsync(height: 4000);
        try
        {
            session.ShowThirdPlace(); Probe.Layout();
            var trip = session.Trip!;
            // Every payment has the icon of its category, named for a screen reader.
            var icons = Probe.All<Jourfold.UI.Icon>(window).Where(i => i.Tag is string tag && tag.StartsWith("category:", StringComparison.Ordinal)).ToArray();
            Assert.Equal(trip.Entities.Values.Count(e => e.Type == "expense"), icons.Length);
            Assert.Contains(icons, i => (string)i.Tag! == "category:transport" && AutomationProperties.GetName(i) == "Category: Transport");
            Assert.Contains(icons, i => (string)i.Tag! == "category:accommodation");

            // The total stands above the categories, per currency, and follows the people chosen.
            string[] Totals() { Probe.Layout(); return Probe.All<Grid>(window).Where(g => g.Tag as string == "category.total").Select(g => string.Join(" | ", Probe.All<TextBlock>(g).Select(t => t.Text))).ToArray(); }
            var everyone = Totals();
            var home = ExpenseCategories.Totals(trip, session.TripCurrencyCode).Where(t => t.Currency == session.TripCurrencyCode).Sum(t => t.Amount);
            Assert.Equal("Total | " + Formats.Money(home, session.TripCurrencyCode), everyone[0]);
            var alex = TripPeople.Travelers(trip).First();
            Probe.Click(window, "Count the part of " + alex.Title);
            var part = ExpenseCategories.Totals(trip, session.TripCurrencyCode, null, [alex.Id]).Where(t => t.Currency == session.TripCurrencyCode).Sum(t => t.Amount);
            Assert.Equal("Total for " + alex.Title + " | " + Formats.Money(part, session.TripCurrencyCode), Totals()[0]);
            Assert.True(part < home);
        }
        finally { window.Close(); session.Dispose(); }
    }

    [AvaloniaFact]
    public async Task TheTitleOfANewExpenseSuggestsItsCategory()
    {
        var (session, window) = await OpenAsync();
        try
        {
            session.ShowQuickAdd("expense"); Probe.Layout();
            // The category chips are the last with these names; the kinds of plan above them have a "Transport" too.
            ChoiceButton Chip(string label) { Probe.Layout(); return Probe.All<ChoiceButton>(window).Last(c => c.Classes.Contains("chip") && Probe.All<TextBlock>(c).Any(t => t.Text == label)); }
            bool Hint() { Probe.Layout(); return Probe.All<TextBlock>(window).Any(t => t.Tag as string == "category.fromTitle"); }
            Assert.True(Chip("Other").IsSelected);
            Assert.False(Hint());

            var title = Box(window, "Title");
            title.Text = "Taxi to the hotel";
            Assert.True(Chip("Transport").IsSelected);
            Assert.True(Hint());
            title.Text = "Souvenirs";
            Assert.True(Chip("Souvenirs").IsSelected);
            title.Text = "Something";
            Assert.True(Chip("Other").IsSelected);
            Assert.False(Hint());

            // Kept when nothing else is chosen.
            title.Text = "Ramen restaurant";
            Assert.True(Chip("Food").IsSelected);
            Box(window, "Amount").Text = "2400";
            Probe.Click(window, "Add");
            await Probe.Until(() => session.Trip!.Entities.Values.Any(e => e.Title == "Ramen restaurant" && ExpenseCategories.Stored(e) is not null));
            Assert.Equal("food", ExpenseCategories.Stored(session.Trip!.Entities.Values.Single(e => e.Title == "Ramen restaurant")));

            // A category chosen by hand stays, whatever the title says.
            session.ShowQuickAdd("expense"); Probe.Layout();
            Box(window, "Title").Text = "Taxi";
            Chip("Activities").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Box(window, "Title").Text = "Taxi to the restaurant";
            Assert.True(Chip("Activities").IsSelected);
            Assert.False(Hint());
            Box(window, "Amount").Text = "900";
            Probe.Click(window, "Add");
            await Probe.Until(() => session.Trip!.Entities.Values.Any(e => e.Title == "Taxi to the restaurant" && ExpenseCategories.Stored(e) is not null));
            Assert.Equal("activity", ExpenseCategories.Stored(session.Trip!.Entities.Values.Single(e => e.Title == "Taxi to the restaurant")));
        }
        finally { window.Close(); session.Dispose(); }
    }

    [AvaloniaFact]
    public async Task SearchFindsAPlanShowsItsDetailsAndJumpsToItsDay()
    {
        var (session, window) = await OpenAsync();
        try
        {
            // The magnifier is in the trip's header on Today, Plan and More.
            foreach (var tab in new[] { MobileTab.Today, MobileTab.Plan, MobileTab.More })
            {
                session.ShowTab(tab);
                Assert.Contains("Search the trip", Buttons(window));
            }
            session.ShowTab(MobileTab.Plan);
            Probe.Click(window, "Search the trip"); Probe.Layout();
            Assert.Equal((MobileTab.More, "search"), (session.Tab, session.Page));
            Assert.Equal("Plan", session.BackLabel);
            Assert.Empty(Results(window));

            // A typo and no accents: still found, the best match first with its day and time.
            await TypeAsync(window, "Search the trip", "shinkansn");
            var shinkansen = session.Trip!.Entities.Values.Single(e => e.Title == "Shinkansen to Kyoto");
            var results = Results(window);
            Assert.Equal("search:" + shinkansen.Id, results[0].Tag);
            Assert.Equal("Plan: Shinkansen to Kyoto, Tue 18 May · " + Formats.Time(new LocalTime(8, 30)), AutomationProperties.GetName(results[0]));
            Assert.True(Shows(window, "Best match"));

            // A tap opens the details over the search, which stays.
            results[0].RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); Probe.Layout();
            Assert.Equal((Overlay.Details, shinkansen.Id), (session.Showing, session.OverlayItem!.Value));
            Assert.Equal("search", session.Page);
            session.CloseOverlay(); Probe.Layout();
            Assert.Equal("shinkansn", Box(window, "Search the trip").Text);
            Assert.NotEmpty(Results(window));

            // "Show in the plan" jumps to its day and marks it; back returns to the same results.
            session.ShowDetails(shinkansen.Id); Probe.Layout();
            Probe.Click(window, "Show in the plan"); Probe.Layout();
            Assert.Equal((MobileTab.Plan, new LocalDate(2027, 5, 18), Overlay.None), (session.Tab, session.SelectedDate, session.Showing));
            Assert.Equal(shinkansen.Id, session.Marked);
            Assert.Single(Probe.All<Panel>(window), p => p.Tag as string == "marked");
            Back(window);
            Assert.Equal((MobileTab.More, "search"), (session.Tab, session.Page));
            Assert.Null(session.Marked);
            Assert.Equal("shinkansn", Box(window, "Search the trip").Text);
            Assert.Equal("search:" + shinkansen.Id, Results(window)[0].Tag);
            // The plan's details opened elsewhere do not offer the jump.
            session.ShowTab(MobileTab.Plan); session.ShowDetails(shinkansen.Id);
            Assert.DoesNotContain("Show in the plan", Buttons(window));
        }
        finally { window.Close(); session.Dispose(); }
    }

    [AvaloniaFact]
    public async Task SearchFiltersGroupsAndJumpsToBookingsCostsAndTheMap()
    {
        var (session, window) = await OpenAsync();
        try
        {
            var trip = session.Trip!;
            session.ShowSearch(); Probe.Layout();
            await TypeAsync(window, "Search the trip", "gracery");
            var all = Results(window).Length;
            Assert.True(all >= 4);
            // By day: day headings in trip order after today's; by kind: the kinds.
            Assert.True(Shows(window, "Saturday 15 May"));
            Probe.Click(window, "By kind");
            Assert.False(session.SearchByDay);
            Assert.False(Shows(window, "Saturday 15 May"));
            Assert.True(Shows(window, "Bookings") && Shows(window, "Places"));
            Assert.False(new MobileSession(session.Host).SearchByDay);
            Probe.Click(window, "By day");

            // Only costs.
            Probe.Click(window, "Show only: Costs");
            var deposit = trip.Entities.Values.Single(e => e.Type == "expense" && e.Title == "Hotel Gracery deposit");
            Assert.Equal(["search:" + deposit.Id], Results(window).Select(r => (string)r.Tag!));
            // Jumping to it opens the costs with the row unfolded and marked; back returns to the search.
            session.ShowInPlace(deposit.Id); Probe.Layout();
            Assert.Equal((MobileTab.More, "costs"), (session.Tab, session.Page));
            Assert.Contains(deposit.Id, session.ExpandedGroups);
            Assert.Single(Probe.All<Panel>(window), p => p.Tag as string == "marked");
            Assert.Equal("Back", session.BackLabel);
            Back(window);
            Assert.Equal("search", session.Page);
            Probe.Click(window, "Show only: All");

            // A booking by its number; its page unfolds it.
            await TypeAsync(window, "Search the trip", "GR-1187");
            var booking = trip.Entities.Values.Single(e => e.Type == "booking" && e.Data["reference"]?.ToString() == "GR-1187");
            var first = Results(window)[0];
            Assert.Equal("search:" + booking.Id, first.Tag);
            Assert.Contains("Booking no.: GR-1187", AutomationProperties.GetName(first));
            session.ShowInPlace(booking.Id); Probe.Layout();
            Assert.Equal("bookings", session.Page);
            Assert.Contains(booking.Id, session.ExpandedGroups);
            Back(window);

            // A place opens on the map, on the day of a plan there.
            var station = trip.Entities.Values.Single(e => e.Type == "place" && e.Title == "Kyoto Station");
            Assert.Equal("SearchShowMap", session.ShowInPlaceKey(station));
            session.ShowInPlace(station.Id); Probe.Layout();
            Assert.Equal((MobileTab.Map, new LocalDate(2027, 5, 18)), (session.Tab, session.SelectedDate));
            Back(window);
            Assert.Equal("search", session.Page);

            // Nothing found says so, and that notes were left out; with notes it is found.
            var note = trip.Entities.Values.First(e => e.Type == "schedule_item").Copy(); note.Data["body"] = "Ask for the zabuton cushions.";
            await session.Workspace!.EditAsync(note); Probe.Layout();
            await TypeAsync(window, "Search the trip", "zabuton");
            Assert.Empty(Results(window));
            Assert.True(Shows(window, "Nothing found for “zabuton”."));
            var notes = Probe.All<CheckBox>(window).First(c => AutomationProperties.GetName(c) == "Also search notes");
            notes.IsChecked = true; Probe.Layout();
            Assert.True(session.SearchNotes);
            Assert.Equal("search:" + note.Id, Assert.Single(Results(window)).Tag);
            Assert.Contains("Note: Ask for the zabuton cushions.", AutomationProperties.GetName(Results(window)[0]));
        }
        finally { window.Close(); session.Dispose(); }
    }

    [AvaloniaTheory]
    [InlineData("en", 412, 915, false)]
    [InlineData("de", 412, 915, true)]
    [InlineData("de", 360, 740, false)]
    [InlineData("de", 1280, 800, true)]
    public async Task TheSearchFitsInEnglishAndGermanLightAndDark(string language, double width, double height, bool dark)
    {
        Avalonia.Application.Current!.RequestedThemeVariant = dark ? Avalonia.Styling.ThemeVariant.Dark : Avalonia.Styling.ThemeVariant.Light;
        var (session, window) = await OpenAsync(language, width, height);
        try
        {
            session.ShowSearch(); Probe.Layout();
            var name = session.Strings["SearchTrip"];
            await TypeAsync(window, name, "shin");
            Assert.NotEmpty(Results(window));
            // German category names find their plans: "Essen" is food.
            if (language == "de")
            {
                await TypeAsync(window, name, "Essen");
                Assert.Contains(Results(window), r => AutomationProperties.GetName(r)!.Contains("Kategorie: Essen", StringComparison.Ordinal));
            }
            // Nothing is wider than the page: chips wrap, long titles and places wrap.
            var page = Probe.All<ScrollViewer>(window).First(v => v.GetVisualDescendants().OfType<Button>().Any(b => b.Tag is string tag && tag.StartsWith("search:", StringComparison.Ordinal)));
            Assert.True(page.Extent.Width <= page.Viewport.Width + 0.5, "The results are wider than the page.");
            foreach (var result in Results(window))
            {
                var edge = result.TranslatePoint(new Avalonia.Point(result.Bounds.Width, 0), window)!.Value.X;
                Assert.True(edge <= width + 0.5, "A result reaches past the screen.");
            }
            foreach (var chip in Probe.All<ChoiceButton>(window).Where(c => c.Classes.Contains("chip") && c.IsEffectivelyVisible))
                Assert.True(chip.TranslatePoint(new Avalonia.Point(chip.Bounds.Width, 0), window)!.Value.X <= width + 0.5, "A chip reaches past the screen.");
        }
        finally { window.Close(); session.Dispose(); Avalonia.Application.Current!.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Light; }
    }

    [AvaloniaFact]
    public async Task ThePaletteOnTheComputerFindsWithTheSameSearch()
    {
        string path;
        using (var phone = new MobileSession(new MobileHost(Path.Combine(root, "phone"), new GitCliBackend(), new FakeClock(Instant.FromUtc(2027, 5, 16, 1, 40)), () => Tokyo, "en", new MemorySecrets())))
            path = await phone.CreateSampleAsync(new LocalDate(2027, 5, 14));
        var interaction = new TestInteraction(); using var store = new LocalStore(Path.Combine(root, "state"));
        using var model = new MainViewModel(store, new GitCliBackend(), new OsSecretStore(), interaction);
        // Other tests leave their language behind in the thread's culture.
        model.SetPreference("Language", "en");
        await model.OpenAsync(path);
        var trip = model.Trip!;
        var shinkansen = trip.Entities.Values.Single(e => e.Title == "Shinkansen to Kyoto");

        // A typo, the best match first, with its day; commands stay in the palette.
        var found = model.PaletteSearch("shinkansn");
        var best = found.First(c => Guid.TryParse(c.Id, out _));
        Assert.Equal((shinkansen.Id.ToString(), "Best match", "Tue 18 May · " + Formats.Time(new LocalTime(8, 30))), (best.Id, best.Group, best.Detail));
        Assert.NotEmpty(best.TitleRanges!);
        Assert.Contains(model.PaletteSearch("Settings"), c => c.Id == "command:Settings");

        // By day by default, with day headings; by kind when chosen, and remembered.
        var groups = model.PaletteSearch("gracery").Where(c => Guid.TryParse(c.Id, out _)).Select(c => c.Group).Distinct().ToArray();
        Assert.True(groups.Contains("Saturday 15 May"), string.Join(" / ", groups));
        model.SearchByDay = false;
        groups = model.PaletteSearch("gracery").Where(c => Guid.TryParse(c.Id, out _)).Select(c => c.Group).Distinct().ToArray();
        Assert.DoesNotContain("Saturday 15 May", groups);
        Assert.Contains("Places", groups);
        Assert.Equal("kind", store.Get("searchgroup"));

        // Where it was found, and choosing it goes there.
        var byNumber = model.PaletteSearch("GR-1187").First(c => Guid.TryParse(c.Id, out _));
        Assert.Equal("Booking no.: GR-1187", byNumber.Subtitle);
        await model.ExecutePaletteAsync(best);
        Assert.Equal(shinkansen.Id, model.Selected?.Id);
    }
}
