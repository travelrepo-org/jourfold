using System.Text.Json.Nodes;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Jourfold.Mobile;
using Jourfold.Mobile.Controls;
using Jourfold.UI;
using NodaTime;
using TravelRepo.Core;
using TravelRepo.Git;
using Xunit;
namespace Jourfold.Tests;

/// <summary>Editing on the phone what a trip needs beyond its plans: bookings, costs, people, the trip, and groups.</summary>
public sealed class MobileEditingTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "jourfold-mobile-edit-" + Guid.NewGuid());
    public void Dispose() => TestFiles.Delete(root);

    private static readonly DateTimeZone Tokyo = DateTimeZoneProviders.Tzdb["Asia/Tokyo"];

    private async Task<(MobileSession Session, Window Window)> OpenAsync()
    {
        var now = new LocalDateTime(2027, 5, 16, 10, 40).InZoneStrictly(Tokyo).ToInstant();
        var session = new MobileSession(new MobileHost(root, new GitCliBackend(), new FakeClock(now), () => Tokyo, "en", new MemorySecrets()));
        await session.OpenAsync(await session.CreateSampleAsync(new LocalDate(2027, 5, 14)));
        var window = new Window { Width = 412, Height = 915, Content = new AppShell(session) }; window.Show();
        Probe.Layout();
        return (session, window);
    }

    private static Sheet Form(Window window) { Probe.Layout(); return window.GetVisualDescendants().OfType<Sheet>().First(); }
    private static T Field<T>(Window window, string name) where T : Control => Probe.All<T>(Form(window)).First(c => AutomationProperties.GetName(c) == name);
    private static void Tap(Window window, string label) { Probe.Click(Form(window), label); Probe.Layout(); }
    private static async Task PickAsync(MobileSession session, string label)
    {
        var action = session.Menu!.Actions.First(a => a.Label.Contains(label, StringComparison.Ordinal));
        await session.ChooseAsync(action, null); Probe.Layout();
    }

    [AvaloniaFact]
    public async Task ABookingIsAddedWithItsPlansAndChangedLater()
    {
        var (session, window) = await OpenAsync();
        try
        {
            var temple = session.Trip!.Entities.Values.First(e => e.Title == "Senso-ji and Asakusa");
            session.ShowQuickAdd("booking");
            Field<TextBox>(window, "Title").Text = "Temple tour";
            Field<TextBox>(window, "Provider").Text = "City Walks";
            Field<TextBox>(window, "Booking number").Text = "CW-204";
            Tap(window, "Confirmed");
            Field<TextBox>(window, "Amount").Text = "48,50";
            Field<AutoCompleteBox>(window, "Other").Text = "usd";
            Tap(window, "Link a plan");
            await PickAsync(session, "Senso-ji and Asakusa");
            Tap(window, "Add");
            await Probe.Until(() => session.Trip!.Entities.Values.Any(e => e.Type == "booking" && e.Title == "Temple tour"));
            await Probe.Until(() => session.Trip!.Entities.Values.Single(e => e.Type == "booking" && e.Title == "Temple tour").Data["provider"] is not null);

            var booking = session.Trip!.Entities.Values.Single(e => e.Type == "booking" && e.Title == "Temple tour");
            Assert.Equal(("City Walks", "CW-204", "confirmed"), (booking.Data["provider"]!["name"]!.ToString(), booking.Data["reference"]!.ToString(), booking.Data["status"]!.ToString()));
            // A booking has no price: the amount is a payment still to pay that belongs to it.
            Assert.Null(booking.Data["price"]);
            var payment = Assert.Single(Expenses.ForBooking(session.Trip!, booking.Id));
            Assert.Equal(("48.50", "USD", true), (payment.Data["amount"]!["value"]!.ToString(), payment.Data["amount"]!["currency"]!.ToString(), Expenses.IsPlanned(payment)));
            Assert.Equal(booking.Id, TripCommands.BookingOf(session.Trip!.Find(temple.Id)!));

            // Later the tour is cancelled and the plan no longer belongs to it; free cancellation was until the day before.
            session.ShowEditor(booking.Id);
            Tap(window, "Cancelled");
            Field<DatePicker>(window, "Free cancellation until").SelectedDate = new DateTimeOffset(2027, 5, 15, 0, 0, 0, TimeSpan.Zero);
            Tap(window, "Remove Senso-ji and Asakusa from the booking");
            Tap(window, "Save");
            await Probe.Until(() => session.Trip!.Find(booking.Id)!.Data["status"]?.ToString() == "cancelled");
            booking = session.Trip!.Find(booking.Id)!;
            Assert.Equal(("2027-05-15T23:59:00", "Asia/Tokyo"), (booking.Data["cancellation"]!["free_until"]!["local"]!.ToString(), booking.Data["cancellation"]!["free_until"]!["timezone"]!.ToString()));
            Assert.Null(TripCommands.BookingOf(session.Trip!.Find(temple.Id)!));
            Assert.DoesNotContain(SemanticValidation.Validate(session.Trip!), d => d.Severity == Severity.Error);
        }
        finally { window.Close(); session.Dispose(); }
    }

    [AvaloniaFact]
    public async Task AnExpenseSaysWhoPaidInWhichCurrencyAndWhatItBelongsTo()
    {
        var (session, window) = await OpenAsync();
        try
        {
            var people = session.Trip!.Entities.Values.Where(e => e.Type == "person").OrderBy(p => p.Title, StringComparer.Ordinal).ToArray();
            var other = people.First(p => p.Id != session.Me!.Id);
            session.ShowQuickAdd("expense");
            Field<TextBox>(window, "Title").Text = "Museum tickets";
            Field<TextBox>(window, "Amount").Text = "3600";
            Tap(window, "JPY");
            // The user is chosen as the payer; the other traveller paid half of it, so both are named.
            Tap(window, other.Title);
            Tap(window, "Link to a plan or booking");
            await PickAsync(session, "teamLab Planets");
            Tap(window, "Add");
            await Probe.Until(() => session.Trip!.Entities.Values.Any(e => e.Title == "Museum tickets" && e.Data["related"] is not null));
            var expense = session.Trip!.Entities.Values.Single(e => e.Title == "Museum tickets");
            Assert.Equal(("3600", "JPY"), (expense.Data["amount"]!["value"]!.ToString(), expense.Data["amount"]!["currency"]!.ToString()));
            // Shared by the people of the plan it belongs to, named.
            Assert.False(Expenses.SharesWithEveryone(expense));
            Assert.Equal(new[] { session.Me!.Id, other.Id }.Order(), Jourfold.Application.DetailEdits.People(expense, "paid_by").Order());
            Assert.Equal("teamLab Planets", session.Trip!.Find(Guid.Parse(expense.Data["related"]![0]!["id"]!.ToString()))!.Title);

            // Edited: only an estimate so far, and paid by the other traveller alone.
            session.ShowEditor(expense.Id);
            Field<ToggleSwitch>(window, "Still to pay").IsChecked = true;
            Tap(window, session.Me!.Title + " (You)");
            Tap(window, "Save");
            await Probe.Until(() => session.Trip!.Find(expense.Id)!.Data["estimated"]?.GetValue<bool>() == true);
            Assert.Equal([other.Id], Jourfold.Application.DetailEdits.People(session.Trip!.Find(expense.Id)!, "paid_by"));
        }
        finally { window.Close(); session.Dispose(); }
    }

    [AvaloniaFact]
    public async Task AnAmountChargedIsInTheTripCurrencyAndAnEarlierOneKeepsItsOwn()
    {
        var (session, window) = await OpenAsync();
        try
        {
            Assert.Equal("EUR", session.TripCurrencyCode);
            var tickets = session.Trip!.Entities.Values.Single(e => e.Type == "expense" && e.Title == "teamLab tickets");
            session.ShowEditor(tickets.Id);
            Field<TextBox>(window, "Amount charged in EUR").Text = "46.10";
            Tap(window, "Save");
            await Probe.Until(() => Expenses.ChargeOf(session.Trip!.Find(tickets.Id)!)?.Value == 46.10m);
            Assert.Equal("EUR", Expenses.ChargeOf(session.Trip!.Find(tickets.Id)!)!.Currency);

            // A charge entered in another currency (earlier, or by another app) is edited in that currency.
            var tea = session.Trip!.Entities.Values.Single(e => e.Type == "expense" && e.Title == "Tea ceremony");
            var charged = Expenses.WithCharge(tea, 29.50m, "USD", new LocalDate(2027, 5, 17));
            await session.SaveDetailsAsync([new TravelRepo.Repository.EntityEdit(charged.Id, charged)]);
            session.ShowEditor(tea.Id);
            Assert.DoesNotContain(Probe.All<TextBox>(Form(window)), t => AutomationProperties.GetName(t) == "Amount charged in EUR");
            Field<TextBox>(window, "Amount charged in USD").Text = "28.90";
            Tap(window, "Save");
            await Probe.Until(() => Expenses.ChargeOf(session.Trip!.Find(tea.Id)!)?.Value == 28.90m);
            Assert.Equal("USD", Expenses.ChargeOf(session.Trip!.Find(tea.Id)!)!.Currency);
        }
        finally { window.Close(); session.Dispose(); }
    }

    [AvaloniaFact]
    public async Task APaymentWithCashTakesTheRateItCostInsteadOfACharge()
    {
        var (session, window) = await OpenAsync();
        try
        {
            var rail = session.Trip!.Entities.Values.Single(e => e.Type == "expense" && e.Title == "Rail passes");
            session.ShowEditor(rail.Id);
            // Cash instead of a card: the rate is asked for, as on the cash machine's receipt, and no charged amount.
            Tap(window, "Cash");
            Assert.DoesNotContain(Probe.All<TextBox>(Form(window)), t => AutomationProperties.GetName(t) == "Amount charged in EUR");
            Probe.All<TextBox>(Form(window)).First(t => AutomationProperties.GetName(t)?.StartsWith("Rate of the cash", StringComparison.Ordinal) == true).Text = "160";
            Tap(window, "Save");
            await Probe.Until(() => Expenses.IsCash(session.Trip!.Find(rail.Id)!));
            var paid = session.Trip!.Find(rail.Id)!;
            Assert.Equal(625.00m, Expenses.ChargeOf(paid)!.Value);
            Assert.Equal("Cash · 1 EUR = 160 JPY", Avalonia.LogicalTree.LogicalExtensions.GetLogicalDescendants(Jourfold.Mobile.Views.MorePages.ExpenseFacts(session, paid))
                .OfType<TextBlock>().Select(t => t.Text).First(t => t?.StartsWith("Cash", StringComparison.Ordinal) == true));

            // By card again, the cash rate goes.
            session.ShowEditor(rail.Id);
            Tap(window, "Card");
            Tap(window, "Save");
            await Probe.Until(() => !Expenses.IsCash(session.Trip!.Find(rail.Id)!));
            Assert.Null(Expenses.ChargeOf(session.Trip!.Find(rail.Id)!));
        }
        finally { window.Close(); session.Dispose(); }
    }

    [AvaloniaFact]
    public async Task AnotherCurrencyIsSuggestedWhileTypingAndTakenWithEnter()
    {
        var (session, window) = await OpenAsync();
        try
        {
            var rail = session.Trip!.Entities.Values.Single(e => e.Type == "expense" && e.Title == "Rail passes");
            session.ShowEditor(rail.Id);
            AutoCompleteBox Other() => Probe.All<AutoCompleteBox>(Form(window)).Single(b => AutomationProperties.GetName(b) == "Other");
            bool Chosen(string code) => Probe.All<ChoiceButton>(Form(window)).Any(c => c.IsSelected && Probe.All<TextBlock>(c).Any(t => t.Text == code));

            // Typing suggests codes, as on the converter.
            Other().GetVisualDescendants().OfType<TextBox>().First().Focus(); Probe.Layout();
            window.KeyTextInput("CH"); Probe.Layout();
            await Probe.Until(() => Other().IsDropDownOpen);
            Assert.Contains("CHF", (IEnumerable<string>)Other().ItemsSource!);

            // Enter takes a typed code: it becomes a chosen chip and the field empties.
            Other().Text = "thb"; Probe.Layout();
            Other().RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
            await Probe.Until(() => Chosen("THB"));
            Assert.Equal("", Other().Text ?? "");
            // What the card charged is asked in the trip currency for a payment in baht.
            Assert.Contains(Probe.All<TextBox>(Form(window)), t => AutomationProperties.GetName(t) == "Amount charged in EUR");
            Assert.Contains("THB", session.RecentCurrencies);

            // Choosing from the list does the same.
            Other().SelectedItem = "CHF";
            await Probe.Until(() => Chosen("CHF"));
            Assert.True(Probe.All<ChoiceButton>(Form(window)).Any(c => Probe.All<TextBlock>(c).Any(t => t.Text == "THB")), "the baht stays to tap again");

            // A currency added by hand has an x that takes it off the list.
            Probe.Click(Form(window), "Remove THB from the list"); Probe.Layout();
            await Probe.Until(() => !Probe.All<ChoiceButton>(Form(window)).Any(c => Probe.All<TextBlock>(c).Any(t => t.Text == "THB")));
            Assert.DoesNotContain("THB", session.RecentCurrencies);

            Tap(window, "Save");
            await Probe.Until(() => TripSummaries.Money(session.Trip!.Find(rail.Id)!.Data["amount"])?.Currency == "CHF");
        }
        finally { window.Close(); session.Dispose(); }
    }

    [AvaloniaFact]
    public async Task AJourneysDepartureTimeIsReadInTheZoneChosenForIt()
    {
        var (session, window) = await OpenAsync();
        try
        {
            var train = session.Trip!.Entities.Values.Single(e => e.Type == "schedule_item" && e.Title == "Shinkansen to Kyoto");
            var before = (System.Text.Json.Nodes.JsonObject)train.Data["time"]!["start"]!;
            Assert.Equal("Asia/Tokyo", before["timezone"]!.ToString());
            session.ShowEditor(train.Id);
            ChoiceButton Zone(string place) => Probe.All<ChoiceButton>(Form(window)).First(c => Probe.All<TextBlock>(c).Any(t => t.Text == place));
            // The departure has zone chips of its own, above the arrival's; the one it has is chosen.
            Assert.Contains(Probe.All<TextBlock>(Form(window)), t => t.Text == "Local time at departure");
            Assert.True(Zone("Tokyo").IsSelected);
            // Read in Berlin time instead: the same clock time, now Berlin's.
            Zone("Berlin").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); Probe.Layout();
            Assert.True(Zone("Berlin").IsSelected);
            Tap(window, "Save");
            await Probe.Until(() => session.Trip!.Find(train.Id)!.Data["time"]?["start"]?["timezone"]?.ToString() == "Europe/Berlin");
            Assert.Equal(before["local"]!.ToString(), session.Trip!.Find(train.Id)!.Data["time"]!["start"]!["local"]!.ToString());

            // A new journey departs in the zone chosen for it.
            session.ShowQuickAdd("transport");
            Field<TextBox>(window, "Title").Text = "Night bus";
            Probe.All<TimePicker>(Form(window)).First().SelectedTime = new TimeSpan(22, 0, 0);
            Zone("Berlin").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); Probe.Layout();
            Tap(window, "Add");
            await Probe.Until(() => session.Trip!.Entities.Values.Any(e => e.Title == "Night bus"));
            var bus = session.Trip!.Entities.Values.Single(e => e.Title == "Night bus");
            Assert.Equal("Europe/Berlin", bus.Data["time"]?["start"]?["timezone"]?.ToString());
            Assert.EndsWith("T22:00:00", bus.Data["time"]?["start"]?["local"]?.ToString());
        }
        finally { window.Close(); session.Dispose(); }
    }

    [AvaloniaFact]
    public async Task TheRateOfTheLastCashIsOfferedAndTheCalculatorWorksOutTheWithdrawal()
    {
        var (session, window) = await OpenAsync();
        try
        {
            // Earlier cash in yen cost 1 EUR = 160 JPY.
            var tea = session.Trip!.Entities.Values.Single(e => e.Type == "expense" && e.Title == "Tea ceremony");
            var earlier = Expenses.WithCash(tea, "EUR", 1m / 160m, new LocalDate(2027, 5, 15));
            await session.SaveDetailsAsync([new TravelRepo.Repository.EntityEdit(earlier.Id, earlier)]);
            var rail = session.Trip!.Entities.Values.Single(e => e.Type == "expense" && e.Title == "Rail passes");
            session.ShowEditor(rail.Id);
            Tap(window, "Cash");
            TextBox Rate() => Probe.All<TextBox>(Form(window)).First(t => AutomationProperties.GetName(t)?.StartsWith("Rate of the cash", StringComparison.Ordinal) == true);
            Assert.Equal("160", Rate().Text);
            Assert.Contains(Probe.All<TextBlock>(Form(window)), t => t.Text == "Filled in: Rate of the last cash payment in JPY. The calculator works out the rate of your own withdrawal.");

            // 30,000 yen from the cash machine cost 190 EUR with fees: 1 EUR = 157.8947 JPY.
            Tap(window, "Work out the rate from the withdrawal");
            Probe.All<TextBox>(Form(window)).First(t => AutomationProperties.GetName(t) == "Cash received (JPY)").Text = "30000";
            Probe.All<TextBox>(Form(window)).First(t => AutomationProperties.GetName(t) == "Charged to the account, fees included (EUR)").Text = "190";
            Assert.Contains(Probe.All<TextBlock>(Form(window)), t => t.Text == "1 EUR = 157.8947 JPY");
            Tap(window, "Use this rate");
            Assert.Equal("157.8947", Rate().Text);
            Tap(window, "Save");
            await Probe.Until(() => Expenses.IsCash(session.Trip!.Find(rail.Id)!));
            Assert.Equal(Math.Round(100000m / 157.8947m, 2), Expenses.ChargeOf(session.Trip!.Find(rail.Id)!)!.Value);
        }
        finally { window.Close(); session.Dispose(); }
    }

    [AvaloniaFact]
    public async Task APlanIsForChosenPeopleAndAStepForItsGroup()
    {
        var (session, window) = await OpenAsync();
        try
        {
            var trip = session.Trip!;
            var dinner = trip.Entities.Values.First(e => e.Title == "Dinner at Omoide Yokocho");
            var sam = trip.Entities.Values.First(e => e.Type == "person" && e.Title.StartsWith("Sam", StringComparison.Ordinal));
            var before = ScheduleQueries.Participants(trip, dinner).Select(p => p.Id).ToHashSet();
            session.ShowEditor(dinner.Id);
            Tap(window, sam.Title);
            Tap(window, "Save");
            // A tap on a person adds or removes them.
            var expected = before.ToHashSet(); if (!expected.Remove(sam.Id)) expected.Add(sam.Id);
            await Probe.Until(() => ScheduleQueries.Participants(session.Trip!, session.Trip!.Find(dinner.Id)!).Select(p => p.Id).ToHashSet().SetEquals(expected));

            // Back to everyone: the field goes.
            session.ShowEditor(dinner.Id);
            Tap(window, "Everyone");
            Tap(window, "Save");
            await Probe.Until(() => session.Trip!.Find(dinner.Id)!.Data["participants"] is null);

            // A step of the sample's day trip is for the group's people until it is given its own.
            var falls = session.Trip!.Entities.Values.First(e => TripQueries.Parent(session.Trip!, e.Id) is not null);
            session.ShowEditor(falls.Id);
            Assert.True(Probe.All<Avalonia.Controls.Button>(Form(window)).OfType<Jourfold.UI.ChoiceButton>().Single(b => AutomationProperties.GetName(b) == "As the group").IsSelected);
            Tap(window, sam.Title);
            Tap(window, "Save");
            await Probe.Until(() => session.Trip!.Find(falls.Id)!.Data["participants"]?["inherit"]?.GetValue<bool>() == false);
            session.ShowEditor(falls.Id);
            Tap(window, "As the group");
            Tap(window, "Save");
            await Probe.Until(() => session.Trip!.Find(falls.Id)!.Data["participants"]?["inherit"]?.GetValue<bool>() == true);
        }
        finally { window.Close(); session.Dispose(); }
    }

    [AvaloniaFact]
    public async Task AGroupGetsANewStepAndAnExistingPlanAndLetsOneGo()
    {
        var (session, window) = await OpenAsync();
        try
        {
            session.ShowQuickAdd("group");
            Field<TextBox>(window, "Title").Text = "Day in Kamakura";
            Tap(window, "Add");
            await Probe.Until(() => session.Trip!.Entities.Values.Any(e => e.Title == "Day in Kamakura"));
            var group = session.Trip!.Entities.Values.Single(e => e.Title == "Day in Kamakura");
            // The new group opens, to add its steps.
            Assert.Equal((Overlay.Details, group.Id), (session.Showing, session.OverlayItem));

            session.ShowQuickAdd(null, group.Id);
            Assert.Contains(Probe.All<TextBlock>(Form(window)), t => t.Text == "New step in Day in Kamakura");
            Field<TextBox>(window, "Title").Text = "Great Buddha";
            Tap(window, "Add");
            await Probe.Until(() => session.Trip!.Entities.Values.Any(e => e.Title == "Great Buddha"));
            var buddha = session.Trip!.Entities.Values.Single(e => e.Title == "Great Buddha");
            Assert.Equal(group.Id, TripQueries.Parent(session.Trip!, buddha.Id)!.Id);
            Assert.Equal("true", buddha.Data["participants"]!["inherit"]!.ToString());

            var ramen = session.Trip!.Entities.Values.First(e => e.Title == "Dinner at Omoide Yokocho");
            await session.NestAsync(ramen.Id, group.Id);
            Assert.Equal(group.Id, TripQueries.Parent(session.Trip!, ramen.Id)!.Id);
            Assert.Equal(2, ((JsonArray)session.Trip!.Find(group.Id)!.Data["children"]!).Count);
            await session.NestAsync(ramen.Id, null);
            Assert.Null(TripQueries.Parent(session.Trip!, ramen.Id));
            Assert.DoesNotContain(SemanticValidation.Validate(session.Trip!), d => d.Code.StartsWith("nest.", StringComparison.Ordinal));
        }
        finally { window.Close(); session.Dispose(); }
    }

    [AvaloniaFact]
    public async Task TheTripGetsATitleAndDatesAndATraveller()
    {
        var (session, window) = await OpenAsync();
        try
        {
            await session.SaveTripAsync("Spring in Japan, longer", new LocalDate(2027, 5, 13), new LocalDate(2027, 5, 23));
            Assert.Equal("Spring in Japan, longer", session.Trip!.Manifest.Title);
            Assert.Equal((new LocalDate(2027, 5, 13), new LocalDate(2027, 5, 23)), TripQueries.EffectiveDates(session.Trip!));
            Assert.NotNull(session.Agenda.Find(new LocalDate(2027, 5, 13)));
            Assert.Equal("mobile.dates", (await Assert.ThrowsAsync<DomainException>(() => session.SaveTripAsync("Trip", new LocalDate(2027, 5, 20), new LocalDate(2027, 5, 10)))).Code);

            // Without dates the trip follows its plans again.
            await session.SaveTripAsync("Spring in Japan", null, null);
            Assert.Null(session.Agenda.Find(new LocalDate(2027, 5, 13)));

            session.ShowPage("trip"); Probe.Layout();
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "First day");

            var kim = await session.AddPersonAsync("Kim");
            Assert.Contains(kim.ToString(), session.Trip!.Manifest.Data["participants"]!.ToJsonString());
            session.ShowEditor(session.Trip!.Entities.Values.First(e => e.Title == "Dinner at Omoide Yokocho").Id);
            Assert.Contains(Probe.All<Avalonia.Controls.Button>(Form(window)), b => AutomationProperties.GetName(b) == "Kim");
        }
        finally { window.Close(); session.Dispose(); }
    }

    private sealed class FakeClock(Instant now) : IClock { public Instant GetCurrentInstant() => now; }

    private sealed class MemorySecrets : TravelRepo.Providers.ISecretStore
    {
        private readonly Dictionary<string, string> values = [];
        public Task<string?> ReadAsync(string key, CancellationToken ct = default) => Task.FromResult(values.GetValueOrDefault(key));
        public Task WriteAsync(string key, string value, CancellationToken ct = default) { values[key] = value; return Task.CompletedTask; }
        public Task DeleteAsync(string key, CancellationToken ct = default) { values.Remove(key); return Task.CompletedTask; }
    }
}
