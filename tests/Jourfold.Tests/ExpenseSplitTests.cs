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
        Assert.Contains("2 items marked as paid", interaction.Toasts);
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

    [AvaloniaFact]
    public async Task WhatTheCardChargedIsKeptAndUsedToSettleInEuros()
    {
        using var store = new LocalStore(Path.Combine(root, "local")); var interaction = new TestInteraction();
        using var m = new MainViewModel(store, new GitCliBackend(), new OsSecretStore(), interaction); m.SetPreference("Language", "en");
        var (path, p) = await Trip(); await m.OpenAsync(path);
        m.HomeCurrency = "EUR";
        var tour = Expense("Hobbiton", "300.00", "NZD", p.Ana); await m.Workspace!.EditAsync(tour);

        // Nothing charged yet and no rates: who owes whom stays in NZD.
        Assert.All(Expenses.SettleUp(m.Trip!, m.SettleCurrency, m.EstimateRates), t => Assert.Equal("NZD", t.Currency));
        Assert.False(await m.SetChargeAsync(tour.Id, "lots", "EUR"));
        Assert.True(await m.SetChargeAsync(tour.Id, "168,30", "EUR"));
        var charge = Expenses.ChargeOf(m.Trip!.Find(tour.Id)!)!;
        Assert.Equal((168.30m, "EUR", 0.561m, m.Today), (charge.Value, charge.Currency, charge.Rate, charge.Date!.Value));
        // Ben and Cleo each owe a third of what the card charged, exactly.
        var transfers = Expenses.SettleUp(m.Trip!, m.SettleCurrency, m.EstimateRates);
        Assert.Equal([56.10m, 56.10m], transfers.Select(t => t.Amount));
        Assert.All(transfers, t => Assert.True(t.Currency == "EUR" && !t.Estimated));

        // The pay function pays in euros too, and each expense's own currency is still there to choose.
        interaction.Pay = request => { Assert.Equal("EUR", request.Currency); Assert.Equal([new Transfer(p.Ben.Id, p.Ana.Id, "EUR", 56.10m)], Expenses.Pay(request.Trip, request.Person, Expenses.Open(request.Trip, request.Person).Select(d => d.Key), request.Currency, request.Rates).Payments); return null; };
        await m.PayAsync(p.Ben.Id);
        m.SettleInHomeCurrency = false;
        Assert.All(Expenses.SettleUp(m.Trip!, m.SettleCurrency, m.EstimateRates), t => Assert.Equal("NZD", t.Currency));
        m.SettleInHomeCurrency = true;

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
            m.HomeCurrency = "EUR";
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
            Assert.Contains(Probe.All<Button>(w), b => AutomationProperties.GetName(b) == chip);
            // Everything is in euros now and nothing is estimated.
            Assert.DoesNotContain(texts, t => t.StartsWith("≈", StringComparison.Ordinal));
        }
        finally { w.Close(); }
    }

    [AvaloniaTheory]
    [InlineData("en", "1 booking not fully recorded as expenses", "No expense", "Add Te Anau Lakeview Holiday Park as an expense", "Add")]
    [InlineData("de", "1 Buchung nicht vollständig als Ausgaben erfasst", "Keine Ausgabe", "Te Anau Lakeview Holiday Park als Ausgabe erfassen", "Hinzufügen")]
    public async Task ABookingsPriceBecomesAnExpenseInOneStep(string language, string heading, string pill, string button, string add)
    {
        var w = await Window(language, "Light"); var m = w.Model;
        try
        {
            var booking = Entity.Create("booking", "Te Anau Lakeview Holiday Park"); booking.Data["status"] = "confirmed";
            booking.Data["price"] = new System.Text.Json.Nodes.JsonObject { ["value"] = "347.60", ["currency"] = "NZD" };
            await m.Workspace!.EditAsync(booking);
            m.Navigate("Bookings");
            Assert.Contains(pill, Texts(w));
            m.Navigate("Costs");
            Assert.Contains(heading, Texts(w));

            var adding = m.AddExpenseForBookingAsync(booking.Id);
            await Probe.Until(() => Probe.All<TextBox>(Probe.Sheet(w)).Any(t => t.Tag as string == "title"));
            var sheet = Probe.Sheet(w);
            Assert.Equal("Te Anau Lakeview Holiday Park", Probe.Tagged<TextBox>(sheet, "title").Text);
            Assert.Equal(347.60m, Probe.All<NumericUpDown>(sheet).Single(n => AutomationProperties.GetName(n) == m.Strings["Amount"]).Value);
            Probe.Click(sheet, add);
            await adding;

            var expense = Assert.Single(Expenses.ForBooking(m.Trip!, booking.Id));
            Assert.Equal(("347.60", "NZD"), (expense.Data["amount"]!["value"]!.ToString(), expense.Data["amount"]!["currency"]!.ToString()));
            Assert.Empty(Expenses.WithoutExpense(m.Trip!));
            m.Navigate("Plan"); m.Navigate("Costs");
            Assert.DoesNotContain(heading, Texts(w));
            Assert.DoesNotContain(Probe.All<Button>(w), b => AutomationProperties.GetName(b) == button);
            // The booking's details link to its expense now.
            m.Navigate("Bookings"); m.Selected = m.Trip!.Find(booking.Id); Probe.ShowDetails(w);
            Assert.DoesNotContain(pill, Texts(w));
            await Probe.Until(() => Probe.All<Button>(w.FindControl<Control>("InspectorHost")!).Any(b => AutomationProperties.GetName(b) == "Te Anau Lakeview Holiday Park"));
        }
        finally { w.Close(); }
    }

    [AvaloniaTheory]
    [InlineData("en", "Accommodation", "Souvenirs", "By category", "Rest open", "Add the rest as expense")]
    [InlineData("de", "Unterkunft", "Souvenirs", "Nach Kategorie", "Rest offen", "Rest als Ausgabe erfassen")]
    public async Task ExpensesHaveCategoriesAndABookingShowsWhatIsStillOpen(string language, string accommodation, string souvenirs, string byCategory, string restOpen, string addRest)
    {
        var w = await Window(language, "Light"); var m = w.Model;
        try
        {
            var stay = Entity.Create("schedule_item", "Park Hotel Ruapehu"); stay.Data["components"] = new System.Text.Json.Nodes.JsonObject { ["accommodation"] = new System.Text.Json.Nodes.JsonObject() };
            var booking = Entity.Create("booking", "Park Hotel Ruapehu"); booking.Data["status"] = "confirmed"; booking.Data["items"] = new System.Text.Json.Nodes.JsonArray(stay.Id.ToString());
            booking.Data["price"] = new System.Text.Json.Nodes.JsonObject { ["value"] = "348.50", ["currency"] = "NZD" };
            await m.Workspace!.ApplyAsync([new(stay.Id, stay), new(booking.Id, booking)]);

            // A deposit through the form: the stay suggests accommodation.
            var deposit = m.AddExpenseForBookingAsync(booking.Id);
            await Probe.Until(() => Probe.All<TextBox>(Probe.Sheet(w)).Any(t => t.Tag as string == "title"));
            var sheet = Probe.Sheet(w);
            Assert.Contains(Probe.All<ChoiceButton>(sheet), c => AutomationProperties.GetName(c) == accommodation && c.IsSelected);
            Probe.All<NumericUpDown>(sheet).Single(n => AutomationProperties.GetName(n) == m.Strings["Amount"]).Value = 100m; Probe.Layout();
            Probe.Click(sheet, m.Strings["Add"]);
            await deposit;
            Assert.Equal(BookingCoverage.Partial, Expenses.Payment(m.Trip!, m.Trip!.Find(booking.Id)!).Coverage);
            Assert.Equal("accommodation", ExpenseCategories.Of(m.Trip!, Expenses.ForBooking(m.Trip!, booking.Id).Single()));

            // The booking says what is open and records the rest in one step.
            m.Navigate("Bookings"); Assert.Contains(restOpen, Texts(w));
            m.Selected = m.Trip!.Find(booking.Id); Probe.ShowDetails(w);
            var inspector = w.FindControl<Control>("InspectorHost")!;
            await Probe.Until(() => Probe.All<Button>(inspector).Any(b => AutomationProperties.GetName(b) == addRest));
            var rest = m.AddExpenseForBookingAsync(booking.Id);
            await Probe.Until(() => Probe.All<TextBox>(Probe.Sheet(w)).Any(t => t.Tag as string == "title"));
            Assert.Equal(248.50m, Probe.All<NumericUpDown>(Probe.Sheet(w)).Single(n => AutomationProperties.GetName(n) == m.Strings["Amount"]).Value);
            Probe.Click(Probe.Sheet(w), m.Strings["Add"]);
            await rest;
            Assert.Equal(BookingCoverage.Complete, Expenses.Payment(m.Trip!, m.Trip!.Find(booking.Id)!).Coverage);

            // A category of their own, created in the details.
            var kiwi = Expense("Kiwi plush", "35.00", "NZD", m.Trip!.Entities.Values.Single(e => e.Title == "Ben"));
            await m.Workspace.EditAsync(kiwi);
            m.Navigate("Costs"); m.Selected = m.Trip!.Find(kiwi.Id); Probe.ShowDetails(w);
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
    [InlineData("en", "Primary currency")]
    [InlineData("de", "Primäre Währung")]
    public async Task ThePrimaryCurrencyIsChosenInSettings(string language, string label)
    {
        var w = await Window(language, "Light"); var m = w.Model;
        try
        {
            m.HomeCurrency = "EUR";
            var settings = ((Dialogs)m.Interaction).SettingsAsync(m); Probe.Layout();
            // The dialog opens the page used last, so General is chosen first.
            Probe.Click(Probe.Sheet(w), m.Strings["General"]); Probe.Layout();
            var sheet = Probe.Sheet(w);
            Assert.Contains(label, Texts(sheet));
            // The only currency choice on the General page; choosing a currency there is the primary currency at once.
            var primary = Probe.All<ComboBox>(sheet).Single(c => AutomationProperties.GetName(c) == label);
            primary.SelectedItem = Editors.Currencies.Single(c => c.Id == "CHF"); Probe.Layout();
            Assert.Equal("CHF", m.HomeCurrency);
            Assert.Equal("CHF", m.SettleCurrency);
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
            Assert.False(Probe.Button(sheet, "Als bezahlt markieren").IsEnabled);
            Probe.Click(sheet, m.Strings["SelectAll"]); Probe.Layout();
            Probe.Click(sheet, "Als bezahlt markieren");
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
