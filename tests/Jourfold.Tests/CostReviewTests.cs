using System.Text.Json.Nodes;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Jourfold.Desktop;
using Jourfold.Infrastructure;
using Jourfold.Mobile;
using Jourfold.UI;
using NodaTime;
using TravelRepo.Core;
using TravelRepo.Git;
using TravelRepo.Repository;
using Xunit;
namespace Jourfold.Tests;

/// <summary>
/// What the review of the costs added: the search and the day of a payment, one balance line per person, the add
/// dialog with everything at once, budgets with their limits, and on the phone the pay function, every split, budgets
/// and payments due on Today.
/// </summary>
public sealed class CostReviewTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "jourfold-cost-review-" + Guid.NewGuid());
    public void Dispose() => TestFiles.Delete(root);

    private static readonly Localization English = new("en");

    private static Entity Expense(string title, string value, string currency, Entity payer, params Entity[] sharing)
    {
        var e = Entity.Create("expense", title);
        e.Data["amount"] = new JsonObject { ["value"] = value, ["currency"] = currency };
        e.Data["estimated"] = false; e.Data["paid_by"] = new JsonArray(payer.Id.ToString());
        return sharing.Length == 0 ? e : Expenses.WithSplit(e, SplitMethod.Equal, sharing.Select(p => (p.Id, (decimal?)null)));
    }

    [Fact]
    public void TheSearchFindsAPaymentByItsNameAndByThePeopleInIt()
    {
        var alex = Entity.Create("person", "Alex"); var sam = Entity.Create("person", "Sam");
        var place = Entity.Create("place", "Café Zürich");
        var lunch = Entity.Create("schedule_item", "Lunch"); lunch.Data["default_place"] = place.Id.ToString();
        var pizza = Expense("Alex's Pizza", "30.00", "EUR", sam, sam);
        var taxi = Expense("Taxi", "20.00", "EUR", alex, alex, sam);
        var coffee = Expense("Coffee", "8.00", "EUR", sam, sam); coffee.Data["related"] = new JsonArray(new JsonObject { ["type"] = "schedule_item", ["id"] = lunch.Id.ToString() });
        var manifest = Entity.CreateTrip("Test", "en", "Europe/Berlin");
        var trip = new TripSnapshot(manifest, new[] { alex, sam, place, lunch, pizza, taxi, coffee }.ToDictionary(e => e.Id));
        string[] Found(string query) => new[] { pizza, taxi, coffee }.Where(e => CostTexts.Matches(English, trip, e, query)).Select(e => e.Title).ToArray();

        // "alex" finds both the pizza place and what Alex paid or shares.
        Assert.Equal(["Alex's Pizza", "Taxi"], Found("alex"));
        // The place of the plan it belongs to, without accents, and the category in words.
        Assert.Equal(["Coffee"], Found("zurich"));
        // The coffee belongs to a plan, which makes it food; the others are "other".
        Assert.Equal(["Alex's Pizza", "Taxi"], Found("other"));
        Assert.Equal(["Taxi"], Found("taxi sam"));
        Assert.Equal(3, Found(" ").Length);
    }

    [Fact]
    public void EachPersonHasOneBalanceLineWithEveryCurrency()
    {
        var alex = Guid.NewGuid(); var sam = Guid.NewGuid();
        var lines = CostTexts.Balances(English, [new(alex, "EUR", 119.56m), new(sam, "EUR", -119.56m), new(sam, "JPY", 52200m), new(alex, "JPY", -52200m)]);
        Assert.Equal(2, lines.Count);
        Assert.All(lines, l => Assert.Equal(0, l.Direction));
        Assert.Contains(lines, l => l.Person == alex && l.Text == "gets EUR 119.56 · owes JPY 52,200");
    }

    private async Task<(string Path, Entity Ana, Entity Ben)> Trip()
    {
        var repository = new TravelRepository(Path.Combine(root, "trip"));
        var ana = Entity.Create("person", "Ana"); var ben = Entity.Create("person", "Ben");
        await repository.InitializeAsync(Entity.CreateTrip("Lakes", "en", "Europe/Berlin"));
        var state = await repository.ReadAsync();
        var manifest = TripCurrency.With(state.Trip.Manifest, "EUR");
        manifest.Data["participants"] = new JsonArray(ana.Id.ToString(), ben.Id.ToString());
        await repository.ApplyAsync(state, [new(ana.Id, ana), new(ben.Id, ben), new(manifest.Id, manifest)]);
        await new GitRepository(repository, new GitCliBackend()).InitializeAsync("Test", "test@example.invalid");
        return (repository.Root, ana, ben);
    }

    [AvaloniaFact]
    public async Task TheAddDialogSavesOnlyASplitThatAddsUpWithTheDayAndWhatTheCardCharged()
    {
        var (path, ana, ben) = await Trip();
        var w = new MainWindow(); w.Model.SetPreference("Language", "en"); w.Show(); Dispatcher.UIThread.RunJobs();
        await w.Model.OpenAsync(path); var m = w.Model;
        try
        {
            var adding = m.AddAsync("expense");
            await Probe.Until(() => w.FindControl<Grid>("DialogLayer")!.Children.Count > 0 && Probe.All<TextBox>(Probe.Sheet(w)).Any(t => t.Tag as string == "title"));
            var sheet = Probe.Sheet(w);
            Probe.Tagged<TextBox>(sheet, "title").Text = "Kayak";
            Probe.All<NumericUpDown>(sheet).Single(n => AutomationProperties.GetName(n) == "Amount").Value = 90m; Probe.Layout();
            Probe.Click(sheet, "Amounts"); Probe.Layout();
            var benPart = Probe.All<TextBox>(sheet).Single(b => AutomationProperties.GetName(b) == "Part of Ben");
            benPart.Focus(); benPart.Text = "80"; Probe.Tagged<TextBox>(sheet, "title").Focus(); Probe.Layout();
            // 45 + 80 is more than 90: Add waits until it adds up.
            Assert.False(Probe.Button(sheet, "Add").IsEnabled);
            var anaPart = Probe.All<TextBox>(sheet).Single(b => AutomationProperties.GetName(b) == "Part of Ana");
            anaPart.Focus(); anaPart.Text = "10"; Probe.Tagged<TextBox>(sheet, "title").Focus(); Probe.Layout();
            Assert.True(Probe.Button(sheet, "Add").IsEnabled);
            Assert.Contains(Probe.All<CalendarDatePicker>(sheet), d => AutomationProperties.GetName(d) == "Paid on" && d.SelectedDate is not null);
            Probe.Click(sheet, "Add");
            await adding;
            var kayak = m.Trip!.Entities.Values.Single(e => e.Title == "Kayak");
            Assert.Equal(m.Today, Expenses.DateOf(m.Trip!, kayak));
            Assert.Equal((10m, 80m), (Expenses.Split(m.Trip!, kayak).ShareOf(ana.Id), Expenses.Split(m.Trip!, kayak).ShareOf(ben.Id)));

            // A budget is limited right when it is added.
            var budgeting = m.AddAsync("budget");
            await Probe.Until(() => w.FindControl<Grid>("DialogLayer")!.Children.Count > 0 && Probe.All<TextBox>(Probe.Sheet(w)).Any(t => t.Tag as string == "title"));
            sheet = Probe.Sheet(w);
            Probe.Tagged<TextBox>(sheet, "title").Text = "Ben's food";
            Probe.All<NumericUpDown>(sheet).Single(n => AutomationProperties.GetName(n) == "Amount").Value = 200m;
            Probe.Click(sheet, "Category: Food"); Probe.Layout();
            Probe.Click(Probe.Sheet(w), "Person: Ben"); Probe.Layout();
            Probe.Click(Probe.Sheet(w), "Add");
            await budgeting;
            var food = m.Trip!.Entities.Values.Single(e => e.Title == "Ben's food");
            Assert.Equal(new BudgetFilters("food", ben.Id, null), Budgets.FiltersOf(food));
            Assert.Equal("EUR", TripSummaries.Money(food.Data["amount"])!.Currency);
        }
        finally { w.Close(); }
    }

    private async Task<(MobileSession Session, Window Window)> Phone(int width = 412)
    {
        var tokyo = DateTimeZoneProviders.Tzdb["Asia/Tokyo"];
        var now = new LocalDateTime(2027, 5, 16, 10, 40).InZoneStrictly(tokyo).ToInstant();
        var session = new MobileSession(new MobileHost(root, new GitCliBackend(), new FakeClock(now), () => tokyo, "en", new MemorySecrets()));
        var path = await session.CreateSampleAsync(new LocalDate(2027, 5, 14));
        await session.OpenAsync(path);
        var window = new Window { Width = width, Height = 915, Content = new Jourfold.Mobile.AppShell(session) }; window.Show();
        return (session, window);
    }

    [AvaloniaFact]
    public async Task ThePhoneHasThePayFunction()
    {
        var (session, window) = await Phone();
        try
        {
            IEnumerable<string> All() { Probe.Layout(); return window.GetVisualDescendants().OfType<TextBlock>().Select(t => (t.Text ?? "").Replace(' ', ' ')); }
            session.ShowPage("costs"); Probe.Layout();
            Probe.Click(window, "Pay back…"); Probe.Layout();
            Assert.Equal("pay", session.Page);
            Assert.Contains("That comes to", All());
            var me = session.Me!.Id;
            var open = Expenses.Open(session.Trip!, me);
            Assert.NotEmpty(open);
            // Leave one out: only the others are marked.
            var first = window.GetVisualDescendants().OfType<CheckBox>().First(c => c.IsChecked == true);
            first.IsChecked = false; Probe.Layout();
            Probe.Click(window, "Mark as paid back");
            await Probe.Until(() => Expenses.Open(session.Trip!, me).Count == 1);
            Assert.Equal("costs", session.Page);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task ThePhoneSplitsByAmountsAndSavesOnlyWhatAddsUp()
    {
        var (session, window) = await Phone();
        try
        {
            session.ShowPage("costs");
            session.ShowQuickAdd("expense"); Probe.Layout();
            var form = window.GetVisualDescendants().OfType<TextBox>().ToArray();
            form.First(t => AutomationProperties.GetName(t) == "Title").Text = "Sushi";
            form.First(t => AutomationProperties.GetName(t) == "Amount").Text = "9000"; Probe.Layout();
            Probe.Click(window, "JPY"); Probe.Layout();
            Probe.Click(window, "Amounts"); Probe.Layout();
            var parts = window.GetVisualDescendants().OfType<TextBox>().Where(t => (AutomationProperties.GetName(t) ?? "").StartsWith("Part of ", StringComparison.Ordinal)).ToArray();
            Assert.Equal(2, parts.Length);
            parts[0].Text = "6000"; Probe.Layout();
            var add = window.GetVisualDescendants().OfType<Button>().Last(b => b.Content is StackPanel or string && Probe.All<TextBlock>(b).Any(t => t.Text == "Add"));
            Assert.False(add.IsEnabled);
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => (t.Text ?? "").Contains("too much", StringComparison.Ordinal));
            parts[1].Text = "3000"; Probe.Layout();
            Assert.True(add.IsEnabled);
            add.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            await Probe.Until(() => session.Trip!.Entities.Values.Any(e => e.Title == "Sushi"));
            await Probe.Until(() => Expenses.Split(session.Trip!, session.Trip!.Entities.Values.Single(e => e.Title == "Sushi")).Method == SplitMethod.Amount);
            var sushi = session.Trip!.Entities.Values.Single(e => e.Title == "Sushi");
            Assert.Equal(new[] { 6000m, 3000m }.Order(), Expenses.Split(session.Trip!, sushi).Shares.Select(x => x.Amount).Order());
            Assert.Equal(session.PhoneToday, Expenses.DateOf(session.Trip!, sushi));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task ThePhoneAddsBudgetsAndShowsPaymentsDueOnToday()
    {
        var (session, window) = await Phone();
        try
        {
            IEnumerable<string> All() { Probe.Layout(); return window.GetVisualDescendants().OfType<TextBlock>().Select(t => (t.Text ?? "").Replace(' ', ' ')); }
            // The ryokan is paid on site: due in two days, it shows on Today.
            var ryokan = session.Trip!.Entities.Values.Single(e => e.Type == "expense" && e.Title == "Ryokan in Gion");
            await session.SaveDetailsAsync([new(ryokan.Id, Expenses.WithDue(ryokan, session.PhoneToday.PlusDays(2)))]);
            session.ShowTab(MobileTab.Today);
            Assert.Contains("Payments due", All());

            session.ShowPage("costs"); Probe.Layout();
            Probe.Click(window, "Add budget"); Probe.Layout();
            window.GetVisualDescendants().OfType<TextBox>().First(t => AutomationProperties.GetName(t) == "Title").Text = "Souvenirs";
            window.GetVisualDescendants().OfType<TextBox>().First(t => AutomationProperties.GetName(t) == "Amount").Text = "100";
            Probe.Click(window, "Category: Souvenirs"); Probe.Layout();
            var add = window.GetVisualDescendants().OfType<Button>().Last(b => Probe.All<TextBlock>(b).Any(t => t.Text == "Add"));
            add.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            await Probe.Until(() => session.Trip!.Entities.Values.Any(e => e.Type == "budget" && e.Title == "Souvenirs" && e.Data["category"] is not null));
            var budget = session.Trip!.Entities.Values.Single(e => e.Type == "budget" && e.Title == "Souvenirs");
            Assert.Equal("souvenirs", Budgets.FiltersOf(budget).Category);
            Assert.Equal(session.TripCurrencyCode, TripSummaries.Money(budget.Data["amount"])!.Currency);
        }
        finally { window.Close(); }
    }
}
