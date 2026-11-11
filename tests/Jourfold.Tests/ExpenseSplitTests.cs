using System.Text.Json.Nodes;
using Jourfold.Application;
using Jourfold.Mobile.Views;
using Jourfold.UI;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Jourfold.Desktop;
using Jourfold.Infrastructure;
using Jourfold.Mobile;
using NodaTime;
using TravelRepo.Core;
using TravelRepo.Git;
using TravelRepo.Repository;
using Xunit;
namespace Jourfold.Tests;

/// <summary>Dividing expenses, marking them as paid back, who owes whom and the pay function, on the desktop and the phone.</summary>
public sealed class ExpenseSplitTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "jourfold-expenses-" + Guid.NewGuid());
    public void Dispose() => TestFiles.Delete(root);

    private sealed record People(Entity Ana, Entity Ben, Entity Cleo);

    private async Task<(string Path, People People)> Trip()
    {
        var repository = new TravelRepository(Path.Combine(root, "trip"));
        var ana = Entity.Create("person", "Ana Silva"); var ben = Entity.Create("person", "Ben"); var cleo = Entity.Create("person", "Cleo");
        await repository.InitializeAsync(Entity.CreateTrip("New Zealand", "en", "Pacific/Auckland"));
        var state = await repository.ReadAsync();
        var manifest = state.Trip.Manifest.Copy();
        manifest.Data["participants"] = new JsonArray(ana.Id.ToString(), ben.Id.ToString(), cleo.Id.ToString());
        await repository.ApplyAsync(state, [new(ana.Id, ana), new(ben.Id, ben), new(cleo.Id, cleo), new(manifest.Id, manifest)]);
        await new GitRepository(repository, new GitCliBackend()).InitializeAsync("Test", "test@example.invalid");
        return (repository.Root, new(ana, ben, cleo));
    }

    private static Entity Expense(string title, string value, string currency, Entity payer)
    {
        var e = Entity.Create("expense", title);
        e.Data["amount"] = new JsonObject { ["value"] = value, ["currency"] = currency };
        e.Data["estimated"] = false; e.Data["paid_by"] = new JsonArray(payer.Id.ToString());
        return e;
    }

    [Fact]
    public void MoneyIsShownWithTheCurrencysOwnDecimals()
    {
        var before = System.Globalization.CultureInfo.CurrentCulture;
        System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo("en-US");
        try
        {
            // Yen have no smaller unit; an amount that has more digits than its currency keeps them.
            Assert.Equal("JPY 5,000", Formats.Money(5000m, "JPY"));
            Assert.Equal("EUR 12.50", Formats.Money(12.5m, "EUR"));
            Assert.Equal("JPY 12.5", Formats.Money(12.5m, "JPY"));
        }
        finally { System.Globalization.CultureInfo.CurrentCulture = before; }
    }

    [AvaloniaFact]
    public async Task SplitsAreEditedAndPaidBackAsUndoableSteps()
    {
        using var store = new LocalStore(Path.Combine(root, "local")); var interaction = new TestInteraction();
        using var m = new MainViewModel(store, new GitCliBackend(), new OsSecretStore(), interaction); m.SetPreference("Language", "en");
        var (path, p) = await Trip(); await m.OpenAsync(path);
        var ferry = Expense("Ferry to Waiheke", "90.00", "NZD", p.Ana); await m.Workspace!.EditAsync(ferry);
        Expenses.Split(m.Trip!, m.Trip!.Find(ferry.Id)!).Shares.Select(x => x.Amount).Should(30m, 30m, 30m);

        await m.SetSplitMethodAsync(ferry.Id, SplitMethod.Amount);
        Assert.True(await m.SetSplitValueAsync(ferry.Id, p.Ben.Id, "40,00"));
        Assert.False(await m.SetSplitValueAsync(ferry.Id, p.Ben.Id, "forty"));
        var split = Expenses.Split(m.Trip!, m.Trip!.Find(ferry.Id)!);
        Assert.True(split.Issues.HasFlag(SplitIssues.SumMismatch)); Assert.Equal(100m, split.Assigned);
        Assert.True(await m.SetSplitValueAsync(ferry.Id, p.Cleo.Id, "20"));
        split = Expenses.Split(m.Trip!, m.Trip!.Find(ferry.Id)!);
        split.Shares.Select(x => x.Amount).Should(30m, 40m, 20m);

        await m.SetSplitPersonAsync(ferry.Id, p.Cleo.Id, false);
        await m.SetSplitMethodAsync(ferry.Id, SplitMethod.Equal);
        Expenses.Split(m.Trip!, m.Trip!.Find(ferry.Id)!).Shares.Select(x => x.Amount).Should(45m, 45m);

        var debt = Assert.Single(Expenses.Open(m.Trip!));
        await m.SetDebtPaidAsync(debt.Key, true);
        var paid = Assert.Single(Expenses.Debts(m.Trip!));
        Assert.True(paid.IsSettled); Assert.Equal(m.Today, paid.SettledOn);
        await m.UndoCommand.ExecuteAsync(null);
        Assert.Single(Expenses.Open(m.Trip!));
        await m.SetExpensePaidAsync(ferry.Id, true);
        Assert.Empty(Expenses.Open(m.Trip!));
        await m.SetExpensePaidAsync(ferry.Id, false);
        Assert.Null(m.Trip!.Find(ferry.Id)!.Data["settled"]);
    }

    [AvaloniaFact]
    public async Task ThePayFunctionSettlesOnlyTheChosenItems()
    {
        using var store = new LocalStore(Path.Combine(root, "local")); var interaction = new TestInteraction();
        using var m = new MainViewModel(store, new GitCliBackend(), new OsSecretStore(), interaction); m.SetPreference("Language", "en");
        var (path, p) = await Trip(); await m.OpenAsync(path);
        var dinner = Expense("Dinner in Aachen", "90.00", "EUR", p.Ana);   // Ben owes Ana 30
        var taxi = Expense("Taxi", "30.00", "EUR", p.Ben);                  // Ana owes Ben 10
        var museum = Expense("Te Papa", "60.00", "NZD", p.Cleo);           // Ben owes Cleo 20 NZD
        await m.Workspace!.ApplyAsync([new(dinner.Id, dinner), new(taxi.Id, taxi), new(museum.Id, museum)]);

        // Ben settles the dinner and the taxi with Ana now; the museum stays open.
        interaction.Pay = request =>
        {
            Assert.Equal(p.Ben.Id, request.Person);
            var mine = Expenses.Open(request.Trip, request.Person);
            Assert.Equal(4, mine.Count);
            var chosen = mine.Where(d => d.Expense != museum.Id && (d.From == p.Ben.Id || d.To == p.Ben.Id) && (d.From == p.Ana.Id || d.To == p.Ana.Id)).Select(d => d.Key).ToArray();
            Assert.Equal([new Transfer(p.Ben.Id, p.Ana.Id, "EUR", 20m)], Expenses.Pay(request.Trip, request.Person, chosen).Payments);
            return new PayAnswer(request.Person, chosen);
        };
        await m.PayAsync(p.Ben.Id);
        Assert.Contains("2 items marked as paid back", interaction.Toasts);
        var open = Expenses.Open(m.Trip!, p.Ben.Id);
        Assert.All(open, d => Assert.True(d.Expense == museum.Id || d.Expense == taxi.Id && d.From == p.Cleo.Id));
        // One step undoes the whole payment.
        await m.UndoCommand.ExecuteAsync(null);
        Assert.Equal(4, Expenses.Open(m.Trip!, p.Ben.Id).Count);

        // Cancelling changes nothing; marking all needs a yes and then clears everything.
        interaction.Pay = _ => null; await m.PayAsync(p.Ben.Id);
        Assert.Equal(4, Expenses.Open(m.Trip!, p.Ben.Id).Count);
        await m.SettleAllAsync();
        Assert.Empty(Expenses.Open(m.Trip!)); Assert.Empty(Expenses.SettleUp(m.Trip!));
    }

    [AvaloniaTheory]
    [InlineData("en", "30.00 share", "To settle up", "Ben owes Ana Silva")]
    [InlineData("de", "30,00 Anteil", "Zum Ausgleich", "Ben schuldet Ana Silva")]
    public async Task OnePersonsPlanShowsTheirPartAndHowToSettleUp(string language, string share, string settle, string owes)
    {
        using var store = new LocalStore(Path.Combine(root, "local")); var interaction = new TestInteraction();
        using var m = new MainViewModel(store, new GitCliBackend(), new OsSecretStore(), interaction); m.SetPreference("Language", "en");
        var (path, p) = await Trip(); await m.OpenAsync(path);
        var dinner = Expense("Dinner in Aachen", "90.00", "EUR", p.Ana); await m.Workspace!.EditAsync(dinner);
        var labels = m.ExportLabels(new Jourfold.UI.Localization(language));
        var text = TripExport.Text(TripExport.Document(m.Trip!, new ExportOptions { Parts = ExportParts.Costs, Person = p.Ben.Id }, labels, System.Globalization.CultureInfo.GetCultureInfo(language)));
        Assert.True(text.Contains(share, StringComparison.Ordinal), text);
        Assert.Contains(settle, text);
        Assert.Contains(owes, text);
    }

    [AvaloniaFact]
    public async Task WhatTheCardChargedIsKeptAndUsedToSettleInEuros()
    {
        using var store = new LocalStore(Path.Combine(root, "local")); var interaction = new TestInteraction();
        using var m = new MainViewModel(store, new GitCliBackend(), new OsSecretStore(), interaction); m.SetPreference("Language", "en");
        var (path, p) = await Trip(); await m.OpenAsync(path);
        await m.SetTripCurrencyAsync("EUR");
        Assert.Equal("EUR", TripCurrency.Stored(m.Trip!));
        var tour = Expense("Hobbiton", "300.00", "NZD", p.Ana); await m.Workspace!.EditAsync(tour);

        // Nothing charged yet and no rates: who owes whom stays in NZD.
        Assert.All(Expenses.SettleUp(m.Trip!, m.TripCurrencyCode, m.EstimateRates), t => Assert.Equal("NZD", t.Currency));
        Assert.False(await m.SetChargeAsync(tour.Id, "lots", "EUR"));
        Assert.True(await m.SetChargeAsync(tour.Id, "168,30", "EUR"));
        var charge = Expenses.ChargeOf(m.Trip!.Find(tour.Id)!)!;
        Assert.Equal((168.30m, "EUR", 0.561m, m.Today), (charge.Value, charge.Currency, charge.Rate, charge.Date!.Value));
        // Ben and Cleo each owe a third of what the card charged, exactly.
        var transfers = Expenses.SettleUp(m.Trip!, m.TripCurrencyCode, m.EstimateRates);
        Assert.Equal([56.10m, 56.10m], transfers.Select(t => t.Amount));
        Assert.All(transfers, t => Assert.True(t.Currency == "EUR" && !t.Estimated));

        // The pay function pays in euros too, and each expense's own currency is still there to choose.
        interaction.Pay = request => { Assert.Equal("EUR", request.Currency); Assert.Equal([new Transfer(p.Ben.Id, p.Ana.Id, "EUR", 56.10m)], Expenses.Pay(request.Trip, request.Person, Expenses.Open(request.Trip, request.Person).Select(d => d.Key), request.Currency, request.Rates).Payments); return null; };
        await m.PayAsync(p.Ben.Id);

        Assert.True(await m.SetChargeAsync(tour.Id, "", "EUR"));
        Assert.Null(m.Trip!.Find(tour.Id)!.Data["converted"]);
    }

    [AvaloniaTheory]
    [InlineData("en", "Amount charged in EUR", "as charged", "In EUR")]
    [InlineData("de", "Abgebuchter Betrag in EUR", "wie abgebucht", "In EUR")]
    public async Task TheChargedAmountIsEnteredInTheDetailsAndTotalledInEuros(string language, string field, string exact, string chip)
    {
        var w = await Window(language, "Light"); var m = w.Model;
        try
        {
            await m.SetTripCurrencyAsync("EUR");
            var boat = m.Trip!.Entities.Values.Single(e => e.Title.StartsWith("Boat", StringComparison.Ordinal));
            await m.SetSplitMethodAsync(boat.Id, SplitMethod.Equal);
            m.Navigate("Costs"); m.Selected = m.Trip!.Find(boat.Id); Probe.ShowDetails(w);
            var inspector = w.FindControl<Control>("InspectorHost")!;
            TextBox? box = null;
            await Probe.Until(() => (box = Probe.All<TextBox>(inspector).FirstOrDefault(b => AutomationProperties.GetName(b) == field)) is not null);
            box!.Text = "33,60"; box.RaiseEvent(new Avalonia.Input.KeyEventArgs { RoutedEvent = Avalonia.Input.InputElement.KeyDownEvent, Key = Avalonia.Input.Key.Enter });
            await Probe.Until(() => Expenses.ChargeOf(m.Trip!.Find(boat.Id)!) is { Value: 33.60m });
            m.Navigate("Plan"); m.Navigate("Costs");
            var texts = Texts(w).ToArray();
            Assert.Contains(texts, t => t.EndsWith(exact, StringComparison.Ordinal) && t.Contains("123", StringComparison.Ordinal));
            Assert.Contains(texts, t => t.Equals(chip, StringComparison.OrdinalIgnoreCase));
            // Everything is in euros now and nothing is estimated.
            Assert.DoesNotContain(texts, t => t.StartsWith("≈", StringComparison.Ordinal));
        }
        finally { w.Close(); }
    }

    [AvaloniaFact]
    public async Task CostsFromAnOlderVersionAreUpgradedOnceAfterAskingAndNothingOwedChanges()
    {
        using var store = new LocalStore(Path.Combine(root, "local")); var interaction = new TestInteraction();
        using var m = new MainViewModel(store, new GitCliBackend(), new OsSecretStore(), interaction); m.SetPreference("Language", "en");
        var (path, p) = await Trip();
        // What an older version wrote: a booking with a price paid in part, one without any payment, and an expense
        // without a split that Ben already paid back.
        var repository = new TravelRepository(path); var state = await repository.ReadAsync();
        var lodge = Entity.Create("booking", "Lodge"); lodge.Data["status"] = "confirmed"; lodge.Data["price"] = new System.Text.Json.Nodes.JsonObject { ["value"] = "300.00", ["currency"] = "NZD" };
        var motel = Entity.Create("booking", "Motel"); motel.Data["status"] = "confirmed"; motel.Data["price"] = new System.Text.Json.Nodes.JsonObject { ["value"] = "120.00", ["currency"] = "NZD" };
        var deposit = Expense("Lodge deposit", "100.00", "NZD", p.Ana); deposit.Data["related"] = new System.Text.Json.Nodes.JsonArray(new System.Text.Json.Nodes.JsonObject { ["type"] = "booking", ["id"] = lodge.Id.ToString() });
        deposit.Data["settled"] = new System.Text.Json.Nodes.JsonArray(new System.Text.Json.Nodes.JsonObject { ["person"] = p.Ben.Id.ToString(), ["to"] = p.Ana.Id.ToString(), ["value"] = "33.33" });
        await repository.ApplyAsync(state, [new(lodge.Id, lodge), new(motel.Id, motel), new(deposit.Id, deposit)]);
        await new GitRepository(repository, new GitCliBackend()).CreateVersionAsync("Older costs");
        var before = Expenses.Debts((await repository.ReadAsync()).Trip).Select(d => (d.From, d.To, d.Amount, d.Settled)).ToArray();

        // Opening asks; the test answers yes. The upgrade becomes a version of its own.
        await m.OpenAsync(path);
        await Probe.Until(() => interaction.Toasts.Contains("Costs are up to date"));
        Assert.True(CostUpgrade.Plan(m.Trip!).IsEmpty);
        var trip = m.Trip!;
        Assert.Equal("NZD", TripCurrency.Stored(trip));
        Assert.Null(trip.Find(lodge.Id)!.Data["price"]); Assert.Null(trip.Find(motel.Id)!.Data["price"]);
        Assert.Equal([("Lodge", "200.00"), ("Motel", "120.00")], trip.Entities.Values.Where(e => e.Type == "expense" && Expenses.IsPlanned(e)).OrderBy(e => e.Title).Select(e => (e.Title, e.Data["amount"]!["value"]!.ToString())));
        Assert.Equal(before, Expenses.Debts(trip).Select(d => (d.From, d.To, d.Amount, d.Settled)));
        Assert.True(string.IsNullOrEmpty(await m.Workspace!.Git.StatusAsync()));
        Assert.StartsWith("Costs brought up to date", (await m.Workspace.Git.HistoryAsync())[0].Message, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task ATravellerWhoJoinsSharesOnlyTheEarlierExpensesChosen()
    {
        using var store = new LocalStore(Path.Combine(root, "local")); var interaction = new TestInteraction();
        using var m = new MainViewModel(store, new GitCliBackend(), new OsSecretStore(), interaction); m.SetPreference("Language", "en");
        var (path, p) = await Trip(); await m.OpenAsync(path);
        // Written as an older version did: no split, so every traveller of the moment.
        var dinner = Expense("Dinner", "90.00", "EUR", p.Ana); var taxi = Expense("Taxi", "30.00", "EUR", p.Ana);
        await m.Workspace!.ApplyAsync([new(dinner.Id, dinner), new(taxi.Id, taxi)]);
        dinner = Expenses.SettleAll(m.Trip!).Single(e => e.Id == dinner.Id); await m.Workspace.EditAsync(dinner);

        // Dana joins and should share the taxi only.
        string[]? offered = null;
        interaction.Pick = (choices, selected) => { offered = choices.Select(c => c.Label).ToArray(); Assert.Empty(selected); return [choices.Single(c => c.Label.StartsWith("Taxi", StringComparison.Ordinal)).Id]; };
        interaction.QuickAdds.Enqueue(r => new Jourfold.Application.QuickAddDraft("person", "Dana"));
        await m.AddAsync("person");
        await Probe.Until(() => offered is not null && m.Trip!.Entities.Values.Single(e => e.Title == "Taxi") is var t && Expenses.Split(m.Trip!, t).People.Count == 4);
        var trip = m.Trip!; var dana = trip.Entities.Values.Single(e => e.Title == "Dana");
        Assert.Equal(2, offered!.Length);
        // The dinner stays with the three who shared it, and what Ben and Cleo paid back is exactly right still.
        Assert.Equal([p.Ana.Id, p.Ben.Id, p.Cleo.Id], Expenses.Split(trip, trip.Find(dinner.Id)!).People);
        Assert.True(Expenses.Split(trip, trip.Find(dinner.Id)!).IsSettled);
        Assert.Contains(dana.Id, Expenses.Split(trip, trip.Find(taxi.Id)!).People);
    }

    [AvaloniaFact]
    public async Task CostsAreExportedAsCsvWithTheChosenSeparatorForEveryoneOrSomePeople()
    {
        using var store = new LocalStore(Path.Combine(root, "local")); var interaction = new TestInteraction();
        using var m = new MainViewModel(store, new GitCliBackend(), new OsSecretStore(), interaction); m.SetPreference("Language", "en");
        var (path, p) = await Trip(); await m.OpenAsync(path);
        await m.SetTripCurrencyAsync("EUR");
        var dinner = Expense("Dinner", "90.00", "EUR", p.Ana); dinner = Expenses.WithSplit(dinner, SplitMethod.Equal, [(p.Ana.Id, null), (p.Ben.Id, null), (p.Cleo.Id, null)]); dinner.Data["category"] = "food";
        await m.Workspace!.EditAsync(dinner);

        // Everyone, separated by semicolons.
        var all = Path.Combine(root, "costs.csv"); interaction.Answers.Enqueue(all);
        interaction.Export = setup => { Assert.True(setup.HasCosts); return setup.Initial with { Kind = "costs", Format = "csv", People = [], Separator = "semicolon" }; };
        await m.ShareExportCommand.ExecuteAsync("costs");
        var lines = File.ReadAllLines(all);
        Assert.StartsWith("Date;Payment;Category", lines[0], StringComparison.Ordinal);
        Assert.Contains(lines, l => l.Contains(";Dinner;Food;", StringComparison.Ordinal) && l.EndsWith("30.00;30.00;30.00", StringComparison.Ordinal) | l.EndsWith("30,00;30,00;30,00", StringComparison.Ordinal));
        // The choice is remembered for the next time.
        interaction.Export = setup => { Assert.Equal(("costs", "semicolon"), (setup.Initial.Kind, setup.Initial.Separator)); return null; };
        await m.ShareExportCommand.ExecuteAsync("costs");

        // Ben's part only, as a PDF.
        var bens = Path.Combine(root, "ben.pdf"); interaction.Answers.Enqueue(bens);
        interaction.Export = setup => setup.Initial with { Kind = "costs", Format = "pdf", People = [p.Ben.Id] };
        await m.ShareExportCommand.ExecuteAsync("costs");
        Assert.True(new FileInfo(bens).Length > 1000);
    }

    [AvaloniaFact]
    public async Task APaymentStillToPayHasADueDayWithAReminderAndAPersonsOwnBudgetIsOfferedOnDelete()
    {
        using var store = new LocalStore(Path.Combine(root, "local")); var interaction = new TestInteraction();
        using var m = new MainViewModel(store, new GitCliBackend(), new OsSecretStore(), interaction); m.SetPreference("Language", "en");
        var (path, p) = await Trip(); await m.OpenAsync(path);
        var rest = Expense("Lodge balance", "300.00", "EUR", p.Ana); rest.Data["estimated"] = true;
        await m.Workspace!.EditAsync(rest);
        await m.SetDueAsync(rest.Id, m.Today.PlusDays(3));
        Assert.Equal(m.Today.PlusDays(3), Expenses.DueOf(m.Trip!.Find(rest.Id)!));
        await m.UpdateRemindersAsync();
        var reminder = Assert.Single(m.Reminders, r => r.Kind == ReminderKind.PaymentDue);
        Assert.Contains("Due", m.ReminderText(reminder), StringComparison.Ordinal);
        m.ShowReminder(reminder);
        Assert.Equal("Costs", m.View);
        await m.MarkPaymentPaidAsync(rest.Id);
        Assert.Null(Expenses.DueOf(m.Trip!.Find(rest.Id)!));

        // Ben's own budget is offered, already ticked, when Ben is deleted, and goes with him.
        var budget = Entity.Create("budget", "Ben's money"); budget.Data["amount"] = new System.Text.Json.Nodes.JsonObject { ["value"] = "100.00", ["currency"] = "EUR" };
        budget = Budgets.WithFilters(m.Trip!, budget, new BudgetFilters(null, p.Ben.Id, null));
        await m.Workspace.EditAsync(budget);
        interaction.Deletion = prompt => { Assert.Contains(prompt.Unused, e => e.Id == budget.Id); Assert.Contains(budget.Id, prompt.Preselected); return [budget.Id]; };
        await m.DeleteAsync(p.Ben.Id);
        Assert.Null(m.Trip!.Find(budget.Id));
    }

    [AvaloniaFact]
    public async Task ByCategoryCountsOnePersonsOrACouplesPartOnTheComputerAndThePhone()
    {
        var w = await Window("en", "Light"); var m = w.Model;
        try
        {
            var trip = m.Trip!; var ben = trip.Entities.Values.Single(e => e.Title == "Ben");
            var dinner = trip.Entities.Values.Single(e => e.Title == "Dinner at the lake");
            await m.ChangeExpenseAsync(dinner.Id, (t, e) => ExpenseCategories.With(t, Expenses.WithSplit(e, SplitMethod.Equal, TripPeople.Travelers(t).Select(x => (x.Id, (decimal?)null))), "food"));
            m.Navigate("Costs");
            Assert.Contains("Every payment in full.", Texts(w));
            Probe.All<Button>(w).Single(b => AutomationProperties.GetName(b) == "Count the part of Ben").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Probe.Layout();
            Assert.Equal([ben.Id], m.CategoryPeople);
            Assert.Contains("The part of Ben.", Texts(w));
            Assert.Contains(Texts(w), t => t.StartsWith(Formats.Money(30m, "EUR"), StringComparison.Ordinal));
        }
        finally { w.Close(); }
    }

    [AvaloniaFact]
    public async Task IdeasAreListedApartAndPayingBackKeepsWhatWasTransferred()
    {
        var w = await Window("en", "Light"); var m = w.Model;
        try
        {
            await m.SetTripCurrencyAsync("EUR");
            var trip = m.Trip!; var ana = trip.Entities.Values.Single(e => e.Title == "Ana Silva"); var ben = trip.Entities.Values.Single(e => e.Title == "Ben");
            // A lodge that is only an idea, with what it would cost.
            var lodge = Entity.Create("booking", "Lakeside lodge"); lodge.Data["status"] = "idea";
            var maybe = Expense("Lakeside lodge", "800.00", "EUR", ana); maybe.Data["estimated"] = true; maybe.Data.Remove("paid_by");
            maybe.Data["related"] = new System.Text.Json.Nodes.JsonArray(new System.Text.Json.Nodes.JsonObject { ["type"] = "booking", ["id"] = lodge.Id.ToString() });
            var budget = Entity.Create("budget", "Trip"); budget.Data["amount"] = new System.Text.Json.Nodes.JsonObject { ["value"] = "1000.00", ["currency"] = "EUR" };
            await m.Workspace!.ApplyAsync([new(lodge.Id, lodge), new(maybe.Id, maybe), new(budget.Id, budget)]);
            m.Navigate("Plan"); m.Navigate("Costs");
            var texts = Texts(w).ToArray();
            Assert.Contains(texts, t => t == string.Format(m.Strings["IdeasNotCounted"], Formats.Money(800m, "EUR")));
            Assert.Contains("Idea, not counted", texts);
            Assert.Equal(0m, Budgets.ForTrip(m.Trip!)!.Planned.Total);

            // A jet boat in NZD that the card charged as 33.60 EUR: paying it back keeps the euros transferred.
            var boat = Expenses.WithSplit(Expense("Jet boat", "60.00", "NZD", ben), SplitMethod.Equal, [(ana.Id, null), (ben.Id, null)]);
            await m.Workspace.EditAsync(boat);
            Assert.True(await m.SetChargeAsync(boat.Id, "33,60", "EUR"));
            var key = Expenses.Split(m.Trip!, m.Trip!.Find(boat.Id)!).Debts.First(d => d.From == ana.Id).Key;
            await m.SetDebtPaidAsync(key, true);
            var paid = Expenses.Split(m.Trip!, m.Trip!.Find(boat.Id)!).Debts.Single(d => d.Key == key);
            Assert.Equal(("EUR", false), (paid.PaidIn!.Currency, paid.PaidIn.Reference));
            Assert.Equal(Math.Round(paid.Amount * 0.56m, 2), paid.PaidIn.Value);
            m.Selected = m.Trip!.Find(boat.Id); Probe.ShowDetails(w);
            var inspector = w.FindControl<Control>("InspectorHost")!;
            await Probe.Until(() => Texts(inspector).Any(t => t.Contains("as " + Formats.Money(paid.PaidIn.Value, "EUR"), StringComparison.Ordinal)));
        }
        finally { w.Close(); }
    }

    [AvaloniaTheory]
    [InlineData("en", "Still to pay", "Price not recorded", "{0} to pay", "Add")]
    [InlineData("de", "Noch zu zahlen", "Preis nicht erfasst", "{0} zu zahlen", "Hinzufügen")]
    public async Task AnOlderBookingPriceBecomesAPaymentStillToPay(string language, string heading, string pill, string toPay, string add)
    {
        var w = await Window(language, "Light"); var m = w.Model;
        try
        {
            // A price an older version stored: not counted, and offered as a payment still to pay.
            var booking = Entity.Create("booking", "Lakeside Holiday Park"); booking.Data["status"] = "confirmed";
            booking.Data["price"] = new System.Text.Json.Nodes.JsonObject { ["value"] = "320.00", ["currency"] = "NZD" };
            await m.Workspace!.EditAsync(booking);
            m.Navigate("Bookings");
            Assert.Contains(pill, Texts(w));
            m.Navigate("Costs");
            Assert.Contains(heading, Texts(w));

            var adding = m.AddPaymentForBookingAsync(booking.Id, planned: true);
            await Probe.Until(() => Probe.All<TextBox>(Probe.Sheet(w)).Any(t => t.Tag as string == "title"));
            var sheet = Probe.Sheet(w);
            Assert.Equal("Lakeside Holiday Park", Probe.Tagged<TextBox>(sheet, "title").Text);
            Assert.Equal(320.00m, Probe.All<NumericUpDown>(sheet).Single(n => AutomationProperties.GetName(n) == m.Strings["Amount"]).Value);
            Assert.True(Probe.All<CheckBox>(sheet).Single(c => AutomationProperties.GetName(c) == m.Strings["StillToPay"]).IsChecked);
            Probe.Click(sheet, add);
            await adding;

            // The price is gone; the payment is still to pay, creates no debts and is shared by everyone travelling.
            var expense = Assert.Single(Expenses.ForBooking(m.Trip!, booking.Id));
            Assert.Null(m.Trip!.Find(booking.Id)!.Data["price"]);
            Assert.True(Expenses.IsPlanned(expense));
            Assert.Empty(Expenses.Split(m.Trip!, expense).Debts);
            Assert.Equal(3, Expenses.Split(m.Trip!, expense).People.Count);
            Assert.Equal(BookingCoverage.Planned, Expenses.Payment(m.Trip!, m.Trip!.Find(booking.Id)!).Coverage);
            m.Navigate("Bookings");
            // Nothing is paid yet, so the amount beside it is what is to pay and the mark only says so.
            Assert.Contains(m.Strings["ToPay"], Texts(w));
            Assert.Contains(Formats.Money(320.00m, "NZD"), Texts(w));

            // The booking's details show the payment in place, with a button that marks it as paid.
            m.Selected = m.Trip!.Find(booking.Id); Probe.ShowDetails(w);
            var inspector = w.FindControl<Control>("InspectorHost")!;
            await Probe.Until(() => Probe.All<Button>(inspector).Any(b => AutomationProperties.GetName(b) == string.Format(m.Strings["MarkPaymentPaidFor"], expense.Title)));
            await m.MarkPaymentPaidAsync(expense.Id);
            Assert.Equal(BookingCoverage.Paid, Expenses.Payment(m.Trip!, m.Trip!.Find(booking.Id)!).Coverage);
            Assert.False(Expenses.IsPlanned(m.Trip!.Find(expense.Id)!));
        }
        finally { w.Close(); }
    }

    [AvaloniaTheory]
    [InlineData("en", "Accommodation", "Souvenirs", "By category", "{0} to pay")]
    [InlineData("de", "Unterkunft", "Souvenirs", "Nach Kategorie", "{0} zu zahlen")]
    public async Task ExpensesHaveCategoriesAndABookingShowsWhatIsStillToPay(string language, string accommodation, string souvenirs, string byCategory, string toPay)
    {
        var w = await Window(language, "Light"); var m = w.Model;
        try
        {
            var stay = Entity.Create("schedule_item", "Mountain Hotel"); stay.Data["components"] = new System.Text.Json.Nodes.JsonObject { ["accommodation"] = new System.Text.Json.Nodes.JsonObject() };
            var booking = Entity.Create("booking", "Mountain Hotel"); booking.Data["status"] = "confirmed"; booking.Data["items"] = new System.Text.Json.Nodes.JsonArray(stay.Id.ToString());
            await m.Workspace!.ApplyAsync([new(stay.Id, stay), new(booking.Id, booking)]);

            // A deposit through the form: the stay suggests accommodation.
            var deposit = m.AddPaymentForBookingAsync(booking.Id);
            await Probe.Until(() => Probe.All<TextBox>(Probe.Sheet(w)).Any(t => t.Tag as string == "title"));
            var sheet = Probe.Sheet(w);
            Assert.Contains(Probe.All<ChoiceButton>(sheet), c => AutomationProperties.GetName(c) == accommodation && c.IsSelected);
            Probe.All<NumericUpDown>(sheet).Single(n => AutomationProperties.GetName(n) == m.Strings["Amount"]).Value = 100m; Probe.Layout();
            Probe.All<SearchChoice>(sheet).First().Input.Text = "NZD"; Probe.Layout();
            Probe.Click(sheet, m.Strings["Add"]);
            await deposit;
            Assert.Equal(BookingCoverage.Paid, Expenses.Payment(m.Trip!, m.Trip!.Find(booking.Id)!).Coverage);
            Assert.Equal("accommodation", ExpenseCategories.Of(m.Trip!, Expenses.ForBooking(m.Trip!, booking.Id).Single()));

            // The rest is due on site: a payment still to pay.
            var rest = m.AddPaymentForBookingAsync(booking.Id, planned: true);
            await Probe.Until(() => Probe.All<TextBox>(Probe.Sheet(w)).Any(t => t.Tag as string == "title"));
            Probe.All<NumericUpDown>(Probe.Sheet(w)).Single(n => AutomationProperties.GetName(n) == m.Strings["Amount"]).Value = 240.00m; Probe.Layout();
            Probe.All<SearchChoice>(Probe.Sheet(w)).First().Input.Text = "NZD"; Probe.Layout();
            Probe.Click(Probe.Sheet(w), m.Strings["Add"]);
            await rest;
            Assert.Equal(BookingCoverage.Planned, Expenses.Payment(m.Trip!, m.Trip!.Find(booking.Id)!).Coverage);
            m.Navigate("Bookings"); Assert.Contains(string.Format(toPay, Formats.Money(240.00m, "NZD")), Texts(w));

            // A category of their own, created in the details.
            var kiwi = Expense("Kiwi plush", "35.00", "NZD", m.Trip!.Entities.Values.Single(e => e.Title == "Ben"));
            await m.Workspace.EditAsync(kiwi);
            m.Navigate("Costs"); m.Selected = m.Trip!.Find(kiwi.Id); Probe.ShowDetails(w);
            var inspector = w.FindControl<Control>("InspectorHost")!;
            await Probe.Until(() => Probe.All<ChoiceButton>(inspector).Any(c => AutomationProperties.GetName(c) == souvenirs));
            Probe.All<ChoiceButton>(inspector).Single(c => AutomationProperties.GetName(c) == souvenirs).RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            await Probe.Until(() => ExpenseCategories.Stored(m.Trip!.Find(kiwi.Id)!) == "souvenirs");
            m.Navigate("Plan"); m.Navigate("Costs");
            Assert.Contains(byCategory, Texts(w));
            Assert.Contains(accommodation, Texts(w));
        }
        finally { w.Close(); }
    }

    [AvaloniaTheory]
    [InlineData("en", "Trip currency")]
    [InlineData("de", "Reisewährung")]
    public async Task TheTripCurrencyIsSharedInTheTripDetails(string language, string label)
    {
        var w = await Window(language, "Light"); var m = w.Model;
        try
        {
            // Not a setting of this computer any more: the trip's details choose it for everyone.
            var settings = ((Dialogs)m.Interaction).SettingsAsync(m); Probe.Layout();
            Probe.Click(Probe.Sheet(w), m.Strings["General"]); Probe.Layout();
            Assert.DoesNotContain(label, Texts(Probe.Sheet(w)));
            Probe.Click(Probe.Sheet(w), m.Strings["Close"]); Probe.Layout();
            m.Selected = m.Trip!.Manifest; Probe.ShowDetails(w);
            await Probe.Until(() => Texts(w.FindControl<Control>("InspectorHost")!).Contains(label));
            await m.SetTripCurrencyAsync("CHF");
            Assert.Equal("CHF", TripCurrency.Stored(m.Trip!));
            Assert.Equal("CHF", m.TripCurrencyCode);
            Assert.Contains("currency: CHF", File.ReadAllText(Path.Combine(m.Workspace!.Repository.Root, "travel.yaml")), StringComparison.Ordinal);
        }
        finally { w.Close(); }
    }

    [AvaloniaTheory]
    [InlineData("en", "Category", "Food", "Person", "Eating out", "of", "left")]
    [InlineData("de", "Kategorie", "Essen", "Person", "Eating out", "von", "übrig")]
    public async Task ABudgetCombinesACategoryAndAPersonAndShowsWhatIsLeft(string language, string category, string food, string person, string title, string of, string left)
    {
        var w = await Window(language, "Light"); var m = w.Model;
        try
        {
            var dinner = m.Trip!.Entities.Values.Single(e => e.Title == "Dinner at the lake");
            await m.ChangeExpenseAsync(dinner.Id, (t, e) => ExpenseCategories.With(t, e, "food"));
            var budget = Entity.Create("budget", title); budget.Data["amount"] = new System.Text.Json.Nodes.JsonObject { ["value"] = "150.00", ["currency"] = "EUR" };
            await m.Workspace!.EditAsync(budget);
            m.Navigate("Costs"); m.Selected = m.Trip!.Find(budget.Id); Probe.ShowDetails(w);
            var inspector = w.FindControl<Control>("InspectorHost")!;
            void Choose(string name)
            {
                Probe.All<ChoiceButton>(inspector).Single(c => AutomationProperties.GetName(c) == name).RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            }
            await Probe.Until(() => Probe.All<ChoiceButton>(inspector).Any(c => AutomationProperties.GetName(c) == category + ": " + food));
            Choose(category + ": " + food);
            await Probe.Until(() => Budgets.FiltersOf(m.Trip!.Find(budget.Id)!).Category == "food");
            var status = Budgets.Status(m.Trip!, m.Trip!.Find(budget.Id)!)!;
            Assert.Equal((90m, 60m), (status.Spent, status.Left));
            // The whole trip's EUR budget is not this one.
            Assert.Equal(0m, TripSummaries.Costs(m.Trip!).Single(c => c.Currency == "EUR").Budget);

            // Ben's food: his third of the dinner.
            Probe.ShowDetails(w);
            await Probe.Until(() => Probe.All<ChoiceButton>(inspector).Any(c => AutomationProperties.GetName(c) == person + ": Ben"));
            Choose(person + ": Ben");
            await Probe.Until(() => Budgets.FiltersOf(m.Trip!.Find(budget.Id)!).Person is not null);
            var both = Budgets.Status(m.Trip!, m.Trip!.Find(budget.Id)!)!;
            Assert.Equal(("food", 30m), (both.Filters.Category, both.Spent));
            m.Navigate("Plan"); m.Navigate("Costs");
            var texts = Texts(w).ToArray();
            Assert.Contains(texts, t => t.Contains(" " + of + " ", StringComparison.Ordinal) && t.Contains(left, StringComparison.Ordinal) && t.Contains("150", StringComparison.Ordinal));
            Assert.Contains(food + " · Ben", texts);
            Assert.Contains(Probe.All<Button>(w), b => AutomationProperties.GetName(b) == title);
        }
        finally { w.Close(); }
    }

    private async Task<MainWindow> Window(string language, string theme)
    {
        var (path, p) = await Trip();
        var w = new MainWindow(); w.Model.SetPreference("Language", language); w.Model.SetPreference("Theme", theme); w.Show(); Dispatcher.UIThread.RunJobs();
        await w.Model.OpenAsync(path); w.Model.InboxOpen = false;
        var dinner = Expense("Dinner at the lake", "90.00", "EUR", p.Ana);
        var boat = Expenses.WithSplit(Expense("Boat hire on Lake Wakatipu", "60.00", "NZD", p.Ben), SplitMethod.Percent, [(p.Ana.Id, 50m), (p.Ben.Id, 25m), (p.Cleo.Id, 15m)]);
        await w.Model.Workspace!.ApplyAsync([new(dinner.Id, dinner), new(boat.Id, boat)]);
        return w;
    }

    private static IEnumerable<string> Texts(Control root) { Probe.Layout(); return Probe.All<TextBlock>(root).Select(t => t.Text ?? ""); }

    [AvaloniaTheory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public async Task TheCostsPageShowsWhoOwesWhomAndTheInspectorEditsTheSplit(string theme)
    {
        var w = await Window("en", theme); var m = w.Model;
        try
        {
            Assert.Equal(theme == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light, w.ActualThemeVariant);
            m.Navigate("Costs");
            var texts = Texts(w).ToArray();
            Assert.Contains("Who owes whom", texts);
            Assert.Contains("Ben pays Ana Silva", texts); Assert.Contains("Cleo pays Ana Silva", texts);
            Assert.True(texts.Any(t => t.Contains("EUR", StringComparison.Ordinal) && t.EndsWith(" open", StringComparison.Ordinal)), string.Join(" | ", texts));
            // A split that does not add up says so in words, not only by colour.
            Assert.Contains("Check split", texts);

            var boat = m.Trip!.Entities.Values.Single(e => e.Title.StartsWith("Boat", StringComparison.Ordinal));
            m.Selected = boat; Probe.ShowDetails(w);
            var inspector = w.FindControl<Control>("InspectorHost")!;
            await Probe.Until(() => Texts(inspector).Any(t => t.Contains("90 % of 100 % divided", StringComparison.Ordinal)));
            var cleoPart = Probe.All<TextBox>(inspector).Single(b => AutomationProperties.GetName(b) == "Part of Cleo");
            cleoPart.Text = "25"; cleoPart.RaiseEvent(new Avalonia.Input.KeyEventArgs { RoutedEvent = Avalonia.Input.InputElement.KeyDownEvent, Key = Avalonia.Input.Key.Enter });
            await Probe.Until(() => Expenses.Split(m.Trip!, m.Trip!.Find(boat.Id)!).Divides);

            // Every person and every debt can be reached by keyboard and has a name for screen readers.
            Probe.ShowDetails(w); Probe.Layout();
            Assert.Contains(Probe.All<CheckBox>(inspector), c => AutomationProperties.GetName(c) == "Ben shares this expense" && c.Focusable);
            var payBack = Probe.All<CheckBox>(inspector).Single(c => (AutomationProperties.GetName(c) ?? "").StartsWith("Paid back: Ana Silva to Ben", StringComparison.Ordinal));
            Assert.True(payBack.Focusable && payBack.IsTabStop);
            payBack.IsChecked = true;
            var ana = m.Trip!.Entities.Values.Single(e => e.Title == "Ana Silva").Id;
            await Probe.Until(() => Expenses.Debts(m.Trip!).Single(d => d.Expense == boat.Id && d.From == ana).IsSettled);
            m.Navigate("Plan"); m.Navigate("Costs");
            Probe.Layout();
            Assert.Contains(Probe.All<Button>(w), b => AutomationProperties.GetName(b) == "Pay back…");
        }
        finally { w.Close(); }
    }

    [AvaloniaFact]
    public async Task AnExpenseIsDividedWhileItIsCreated()
    {
        var w = await Window("en", "Light"); var m = w.Model;
        try
        {
            var ben = m.Trip!.Entities.Values.Single(e => e.Title == "Ben").Id; var cleo = m.Trip!.Entities.Values.Single(e => e.Title == "Cleo").Id;
            var adding = m.AddAsync("expense");
            await Probe.Until(() => Probe.All<TextBox>(Probe.Sheet(w)).Any(t => t.Tag as string == "title"));
            var sheet = Probe.Sheet(w);
            Probe.Tagged<TextBox>(sheet, "title").Text = "Jet boat in Queenstown";
            Probe.All<NumericUpDown>(sheet).Single(n => AutomationProperties.GetName(n) == "Amount").Value = 120m; Probe.Layout();
            // Equally among everyone to begin with; Cleo stays at home, then the rest is divided by amounts.
            Assert.Contains(Probe.All<CheckBox>(sheet), c => AutomationProperties.GetName(c) == "Cleo shares this expense" && c.IsChecked == true);
            Probe.All<CheckBox>(sheet).Single(c => AutomationProperties.GetName(c) == "Cleo shares this expense").IsChecked = false; Probe.Layout();
            Probe.Click(sheet, "Amounts"); Probe.Layout();
            var benPart = Probe.All<TextBox>(sheet).Single(b => AutomationProperties.GetName(b) == "Part of Ben");
            // Leaving the box confirms the part.
            benPart.Focus(); Probe.Layout(); benPart.Text = "80"; Probe.Tagged<TextBox>(sheet, "title").Focus(); Probe.Layout();
            Assert.Contains(Texts(sheet), t => t.Contains("too much", StringComparison.Ordinal));
            var anaPart = Probe.All<TextBox>(sheet).Single(b => AutomationProperties.GetName(b) == "Part of Ana Silva");
            // Enter in the last box adds the expense at once, with the value just typed.
            anaPart.Text = "40"; anaPart.Focus(); Probe.Layout(); w.KeyPressQwerty(Avalonia.Input.PhysicalKey.Enter, Avalonia.Input.RawInputModifiers.None); w.KeyReleaseQwerty(Avalonia.Input.PhysicalKey.Enter, Avalonia.Input.RawInputModifiers.None);
            await adding;
            var boat = m.Trip!.Entities.Values.Single(e => e.Title == "Jet boat in Queenstown");
            var split = Expenses.Split(m.Trip!, boat);
            Assert.Equal(SplitMethod.Amount, split.Method);
            Assert.Equal(40m, split.ShareOf(m.Trip!.Entities.Values.Single(e => e.Title == "Ana Silva").Id)); Assert.Equal(80m, split.ShareOf(ben)); Assert.Equal(0m, split.ShareOf(cleo));
            // One undo removes the expense together with its split.
            await m.UndoCommand.ExecuteAsync(null);
            Assert.Null(m.Trip!.Find(boat.Id));
        }
        finally { w.Close(); }
    }

    [AvaloniaFact]
    public async Task ThePayDialogAddsUpTheChoiceInGerman()
    {
        var w = await Window("de", "Light"); var m = w.Model;
        try
        {
            m.Navigate("Costs");
            Assert.Contains("Wer schuldet wem", Texts(w));
            Assert.Contains("Aufteilung prüfen", Texts(w));
            var ben = m.Trip!.Entities.Values.Single(e => e.Title == "Ben").Id;
            var paying = m.PayAsync(ben);
            await Probe.Until(() => Texts(Probe.Sheet(w)).Contains("Das ergibt"));
            var sheet = Probe.Sheet(w);
            Assert.Contains(Texts(sheet), t => t.StartsWith("Ben zahlt insgesamt ", StringComparison.Ordinal) && t.Contains("30", StringComparison.Ordinal) && t.Contains("EUR", StringComparison.Ordinal));
            var dinner = Probe.All<CheckBox>(sheet).Single(c => (AutomationProperties.GetName(c) ?? "").StartsWith("Dinner", StringComparison.Ordinal));
            dinner.IsChecked = false; Probe.Layout();
            Assert.Contains("Noch nichts ausgewählt.", Texts(sheet));
            Assert.False(Probe.Button(sheet, "Als zurückgezahlt markieren").IsEnabled);
            Probe.Click(sheet, m.Strings["SelectAll"]); Probe.Layout();
            Probe.Click(sheet, "Als zurückgezahlt markieren");
            await paying;
            Assert.Empty(Expenses.Open(m.Trip!, ben));
            // Long German labels stay inside the window.
            m.Navigate("Plan"); m.Navigate("Costs"); Probe.Layout();
            foreach (var text in Probe.All<TextBlock>(w).Where(t => t.IsEffectivelyVisible && t.Bounds.Width > 0))
                Assert.True(text.TranslatePoint(new Point(text.Bounds.Width, 0), w)!.Value.X <= w.Bounds.Width + 0.5, text.Text);
        }
        finally { w.Close(); }
    }

    [AvaloniaFact]
    public async Task ThePhoneShowsWhoOwesWhomAndMarksPartsAsPaid()
    {
        var tokyo = DateTimeZoneProviders.Tzdb["Asia/Tokyo"];
        var now = new LocalDateTime(2027, 5, 16, 10, 40).InZoneStrictly(tokyo).ToInstant();
        var session = new MobileSession(new MobileHost(root, new GitCliBackend(), new FakeClock(now), () => tokyo, "en", new MemorySecrets()));
        var path = await session.CreateSampleAsync(new LocalDate(2027, 5, 14));
        await session.OpenAsync(path);
        var window = new Window { Width = 412, Height = 915, Content = new AppShell(session) }; window.Show();
        try
        {
            IEnumerable<string> All() { Probe.Layout(); return window.GetVisualDescendants().OfType<TextBlock>().Select(t => (t.Text ?? "").Replace(' ', ' ')); }
            session.ShowPage("costs");
            Assert.Contains("Who owes whom", All());
            Assert.Contains("Alex (You) pays Sam", All());
            Assert.Contains("Settled", All());
            var rail = session.Trip!.Entities.Values.Single(e => e.Title == "Rail passes");
            session.ToggleGroup(rail.Id); Probe.Layout();
            var box = window.GetVisualDescendants().OfType<CheckBox>().Single(c => (AutomationProperties.GetName(c) ?? "").StartsWith("Paid back: Alex to Sam", StringComparison.Ordinal));
            Assert.True(box.Bounds.Height >= 44);
            box.IsChecked = true;
            await Probe.Until(() => Expenses.Split(session.Trip!, session.Trip!.Find(rail.Id)!).IsSettled);
            Assert.True(session.HasChanges);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task ThePhoneShowsABookingsPaymentsAndUpgradesOlderCostsAfterAsking()
    {
        var tokyo = DateTimeZoneProviders.Tzdb["Asia/Tokyo"];
        var now = new LocalDateTime(2027, 5, 16, 10, 40).InZoneStrictly(tokyo).ToInstant();
        var session = new MobileSession(new MobileHost(root, new GitCliBackend(), new FakeClock(now), () => tokyo, "en", new MemorySecrets()));
        var path = await session.CreateSampleAsync(new LocalDate(2027, 5, 14));
        // An older version stored a price on a booking without payments.
        var repository = new TravelRepository(path); var state = await repository.ReadAsync();
        var walk = Entity.Create("booking", "Night walk"); walk.Data["status"] = "confirmed"; walk.Data["price"] = new System.Text.Json.Nodes.JsonObject { ["value"] = "5000", ["currency"] = "JPY" };
        await repository.ApplyAsync(state, [new(walk.Id, walk)]);
        await session.OpenAsync(path);
        var window = new Window { Width = 412, Height = 915, Content = new AppShell(session) }; window.Show();
        try
        {
            IEnumerable<string> All() { Probe.Layout(); return window.GetVisualDescendants().OfType<TextBlock>().Select(t => (t.Text ?? "").Replace(' ', ' ')); }
            // Asked when the trip opens; the answer is yes.
            Assert.Equal("Update the costs of this trip?", session.Menu?.Title);
            Assert.Contains("Night walk: the price of JPY 5,000 becomes a payment still to pay.", session.Menu!.Detail);
            await session.Menu.Actions[0].Run(null);
            await Probe.Until(() => session.CostUpgradePlan.IsEmpty);
            Assert.True(Expenses.IsPlanned(Assert.Single(Expenses.ForBooking(session.Trip!, walk.Id))));

            // The ryokan is paid at check-in: its payment is shown in the booking and marked as paid there.
            session.ShowPage("bookings");
            var ryokan = session.Trip!.Entities.Values.Single(e => e.Type == "booking" && e.Title == "Ryokan in Gion");
            session.ToggleGroup(ryokan.Id);
            Assert.Contains("Payments", All());
            var paid = window.GetVisualDescendants().OfType<Button>().Single(b => AutomationProperties.GetName(b) == "Mark Ryokan in Gion as paid");
            Assert.True(paid.Bounds.Height >= 44);
            paid.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            await Probe.Until(() => Expenses.Payment(session.Trip!, session.Trip!.Find(ryokan.Id)!).Coverage == BookingCoverage.Paid);

            // Costs are in the trip's currency, as on the computer.
            session.ShowPage("costs");
            Assert.Contains("In EUR", All());
            Assert.Contains(All(), t => t.Contains("EUR 3,500.00", StringComparison.Ordinal));
        }
        finally { window.Close(); session.Dispose(); }
    }
}

internal sealed class FakeClock(Instant now) : IClock { public Instant GetCurrentInstant() => now; }

internal sealed class MemorySecrets : TravelRepo.Providers.ISecretStore
{
    private readonly Dictionary<string, string> values = [];
    public Task<string?> ReadAsync(string key, CancellationToken ct = default) => Task.FromResult(values.GetValueOrDefault(key));
    public Task WriteAsync(string key, string value, CancellationToken ct = default) { values[key] = value; return Task.CompletedTask; }
    public Task DeleteAsync(string key, CancellationToken ct = default) { values.Remove(key); return Task.CompletedTask; }
}

internal static class SequenceAssertions
{
    public static void Should(this IEnumerable<decimal> actual, params decimal[] expected) => Assert.Equal(expected, actual);
}
