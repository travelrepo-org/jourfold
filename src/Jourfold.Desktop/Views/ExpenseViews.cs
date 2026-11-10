using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using TravelRepo.Core;

namespace Jourfold.Desktop;

/// <summary>
/// How expenses are divided and paid back: the split editor and the paying-back list in the inspector, the status
/// of an expense in the cost list, and who owes whom on the Costs page. Every number comes from TravelRepo's
/// <see cref="Expenses"/>; these views only show it and send edits.
/// </summary>
public static class ExpenseViews
{
    private static readonly (SplitMethod Method, string Label, string Icon)[] Methods =
        [(SplitMethod.Equal, "SplitEqual", "users"), (SplitMethod.Amount, "SplitAmount", "coins"), (SplitMethod.Share, "SplitShare", "sliders-horizontal"), (SplitMethod.Percent, "SplitPercent", "circle-dot")];

    /// <summary>Starts the tag of a person's value box, followed by the person's id, so a form can read a value not yet confirmed.</summary>
    public const string SplitValueTag = "split:";

    private static string Name(TripSnapshot trip, Guid id) => trip.Find(id)?.Title ?? "?";

    /// <summary>Who shares the expense and how, in the inspector: every change is an undoable edit of the trip.</summary>
    public static Control SplitEditor(MainWindow w, Entity expense) => SplitEditor(w.Model.Strings, w.Model.Trip!, expense, change => w.Model.ChangeExpenseAsync(expense.Id, change));

    /// <summary>
    /// Who shares the expense and how: method chips, one row per person, and what is still to divide. Each change is
    /// one of TravelRepo's <see cref="Expenses"/> edits, handed to <paramref name="change"/>, which saves it or, while
    /// the expense is still being created, keeps it in the form.
    /// </summary>
    public static Control SplitEditor(Localization s, TripSnapshot trip, Entity expense, Func<Func<TripSnapshot, Entity, Entity>, Task> change)
    {
        var split = Expenses.Split(trip, expense);
        var panel = Ui.V(10);
        if (split.Method is not { } method)
        {
            panel.Children.Add(Ui.H(6, Ui.Icon("info", 13, "Text.Subtle"), Ui.Text(s["SplitUnknown"], "caption").Also(t => t.TextWrapping = TextWrapping.Wrap)));
            return panel;
        }
        var chips = new WrapPanel { ItemSpacing = 6, LineSpacing = 6 };
        foreach (var (option, label, icon) in Methods)
        {
            var chip = Ui.Choice(s[label], icon, option == method, "chip");
            chip.Click += (_, _) => _ = change((t, e) => Expenses.ChangeMethod(t, e, option));
            chips.Children.Add(chip);
        }
        AutomationProperties.SetName(chips, s["SplitMethod"]);
        panel.Children.Add(chips);
        var currency = split.Amount?.Currency ?? "";
        panel.Children.Add(Ui.Text(method switch
        {
            SplitMethod.Equal => s["SplitHintEqual"],
            SplitMethod.Amount => string.Format(s["SplitHintAmount"], currency),
            SplitMethod.Share => s["SplitHintShare"],
            _ => s["SplitHintPercent"]
        }, "caption").Also(t => t.TextWrapping = TextWrapping.Wrap));

        // Everyone of the trip can share it; people named only in the split come last.
        var people = TripPeople.Travelers(trip).Select(p => p.Id).Concat(split.People).Distinct().ToArray();
        var values = split.Shares.ToDictionary(x => x.Person);
        var rows = Ui.V(4);
        foreach (var person in people)
        {
            var name = Name(trip, person);
            var included = split.People.Contains(person);
            var check = new CheckBox { IsChecked = included, MinWidth = 0, Content = Ui.H(8, Ui.Avatar(name, 22), Ui.Line(name)) };
            AutomationProperties.SetName(check, string.Format(s["SharesExpense"], name));
            check.IsCheckedChanged += (_, _) => _ = change((t, e) => Expenses.WithPerson(t, e, person, check.IsChecked == true));
            Control? input = null;
            if (method != SplitMethod.Equal && included)
            {
                var stored = (expense.Data["split"]?["parts"] as System.Text.Json.Nodes.JsonArray ?? []).FirstOrDefault(p => p?["person"]?.ToString() == person.ToString())?["value"]?.ToString();
                var shown = ExchangeRates.Amount(stored) is { } v ? v.ToString(method == SplitMethod.Amount ? "N2" : "0.##", System.Globalization.CultureInfo.CurrentCulture) : "";
                var box = new TextBox { Text = shown, Width = 92, HorizontalContentAlignment = HorizontalAlignment.Right, Tag = SplitValueTag + person };
                AutomationProperties.SetName(box, string.Format(s["SplitValueOf"], name));
                var original = shown;
                async Task Commit()
                {
                    if ((box.Text ?? "").Trim() == original.Trim()) return;
                    // Typed as people type amounts ("12,50" works); text that is no number is kept for correction.
                    if (ExchangeRates.Amount(box.Text) is not { } typed) { box.Classes.Add("invalid"); return; }
                    box.Classes.Remove("invalid"); original = box.Text ?? "";
                    await change((t, e) => Expenses.WithValue(t, e, person, typed));
                }
                box.LostFocus += async (_, _) => await Commit();
                box.KeyDown += async (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; await Commit(); } if (e.Key == Key.Escape) { box.Text = original; e.Handled = true; } };
                var suffix = method switch { SplitMethod.Amount => currency, SplitMethod.Percent => "%", _ => "×" };
                input = Ui.H(4, box, Ui.Text(suffix, "caption").Also(t => { t.VerticalAlignment = VerticalAlignment.Center; t.Width = 30; }));
            }
            var amount = values.TryGetValue(person, out var share) ? Ui.Text(Formats.Money(share.Amount, currency), "strong").Also(t => { t.HorizontalAlignment = HorizontalAlignment.Right; t.MinWidth = 90; t.TextAlignment = TextAlignment.Right; }) : null;
            rows.Children.Add(Ui.Columns("*,Auto,Auto", check, input?.Margin(8, 0), amount));
        }
        panel.Children.Add(rows);

        // What still needs attention, said in words and with an icon, never by colour alone.
        if (split.Issues.HasFlag(SplitIssues.SumMismatch) && split.Target is { } target)
        {
            var rest = target - split.Assigned;
            string F(decimal x) => method == SplitMethod.Percent ? x.ToString("0.##", System.Globalization.CultureInfo.CurrentCulture) + " %" : Formats.Money(x, currency);
            panel.Children.Add(Notice("triangle-alert", "Warning", string.Format(s[rest > 0 ? "SplitLeft" : "SplitOver"], F(split.Assigned), F(target), F(Math.Abs(rest)))));
        }
        else if (split.Issues.HasFlag(SplitIssues.NoPeople)) panel.Children.Add(Notice("triangle-alert", "Warning", s["SplitNoPeople"]));
        else if (split.Issues.HasFlag(SplitIssues.InvalidValue)) panel.Children.Add(Notice("triangle-alert", "Warning", s["CheckSplit"]));
        else if (split.Issues.HasFlag(SplitIssues.Estimated)) panel.Children.Add(Notice("clock", "Text.Subtle", s["SplitEstimated"]));
        else if (split.Issues.HasFlag(SplitIssues.NoPayer)) panel.Children.Add(Notice("info", "Text.Subtle", s["SplitNeedsPayer"]));
        return panel;
    }

    /// <summary>What each person owes for the expense, each with a check box for "paid back".</summary>
    public static Control? PayingBack(MainWindow w, Entity expense)
    {
        var m = w.Model; var s = m.Strings; var trip = m.Trip!;
        var split = Expenses.Split(trip, expense);
        if (split.Debts.Count == 0) return null;
        var list = Ui.V(4);
        foreach (var debt in split.Debts)
        {
            // Money owed back after a changed split reads the other way round.
            var (from, to, amount) = debt.Open < 0 ? (debt.To, debt.From, -debt.Open) : (debt.From, debt.To, debt.Amount);
            var text = string.Format(s["OwesTo"], Name(trip, from), Name(trip, to));
            var check = new CheckBox { IsChecked = debt.IsSettled, MinWidth = 0, HorizontalContentAlignment = HorizontalAlignment.Stretch, Content = Ui.V(1, Ui.Line(text), debt.IsSettled && debt.SettledOn is { } day ? Ui.Line(string.Format(s["PaidOn"], Formats.Date(day)), "caption") : null) };
            AutomationProperties.SetName(check, string.Format(s["MarkPaidFor"], Name(trip, from), Name(trip, to), Formats.Money(amount, debt.Currency)));
            check.IsCheckedChanged += (_, _) => _ = m.SetDebtPaidAsync(debt.Key, check.IsChecked == true);
            var money = Ui.Text(Formats.Money(amount, debt.Currency), "strong").Also(t => { t.HorizontalAlignment = HorizontalAlignment.Right; t.VerticalAlignment = VerticalAlignment.Center; });
            if (debt.IsSettled) money.Classes.Add("muted");
            list.Children.Add(Ui.Columns("*,Auto", check, money));
        }
        var all = Ui.Button(split.IsSettled ? s["UnmarkAllPaid"] : s["MarkAllPaid"], split.IsSettled ? "rotate-ccw" : "circle-check", "link");
        all.HorizontalAlignment = HorizontalAlignment.Left;
        all.Click += (_, _) => _ = m.SetExpensePaidAsync(expense.Id, !split.IsSettled);
        list.Children.Add(all);
        return list;
    }

    /// <summary>A pill for the cost list: open, settled, or a split to check. Null when the expense is not shared.</summary>
    public static Control? Status(Localization s, ExpenseSplit split)
    {
        if (split.Issues.HasFlag(SplitIssues.SumMismatch) || split.Issues.HasFlag(SplitIssues.InvalidValue) || split.Issues.HasFlag(SplitIssues.NoPeople)) return Ui.Pill(s["CheckSplit"], "triangle-alert", "Danger.Soft", "Danger");
        if (split.Debts.Count == 0) return null;
        return split.IsSettled ? Ui.Pill(s["SettledState"], "circle-check", "Success.Soft", "Success") : Ui.Pill(string.Format(s["OpenAmount"], Formats.Money(split.Open, split.Amount!.Currency)), "clock", "Warning.Soft", "Warning");
    }

    /// <summary>Balances per person and currency, the fewest payments that settle them, and the pay function.</summary>
    public static Control? WhoOwesWhom(MainWindow w)
    {
        var m = w.Model; var s = m.Strings; var trip = m.Trip!;
        if (Expenses.Debts(trip).Count == 0) return null;
        var balances = Expenses.Balances(trip, m.SettleCurrency, m.EstimateRates);
        var heading = Ui.V(2, Ui.Text(s["WhoOwesWhom"], "h3"), Ui.Text(m.SettleInHomeCurrency ? string.Format(s["WhoOwesWhomHintIn"], m.HomeCurrency) : s["WhoOwesWhomHint"], "caption").Also(t => t.TextWrapping = TextWrapping.Wrap));
        // With expenses in more than one currency, debts can be shown in the home currency or each in its own.
        Control? toggle = null;
        if (Expenses.Debts(trip).Select(d => d.Currency).Append(m.HomeCurrency).Distinct().Count() > 1)
        {
            var home = Ui.Choice(string.Format(s["SettleInHome"], m.HomeCurrency), "coins", m.SettleInHomeCurrency, "chip"); home.Click += (_, _) => m.SettleInHomeCurrency = true;
            var each = Ui.Choice(s["SettlePerCurrency"], "layers", !m.SettleInHomeCurrency, "chip"); each.Click += (_, _) => m.SettleInHomeCurrency = false;
            toggle = Ui.H(6, home, each).Also(p => AutomationProperties.SetName(p, s["SettleIn"]));
        }
        var body = Ui.V(12, Ui.Columns("*,Auto", heading, toggle?.Also(t => t.VerticalAlignment = VerticalAlignment.Top)));
        if (balances.Count == 0)
        {
            body.Children.Add(Notice("circle-check", "Success", s["AllEven"]));
            return Ui.Card(body, 18);
        }
        var people = new WrapPanel { ItemSpacing = 18, LineSpacing = 10 };
        foreach (var b in balances)
        {
            var name = Name(trip, b.Person) + (m.Me?.Id == b.Person ? " (" + s["You"] + ")" : "");
            var owed = b.Net > 0;
            var line = string.Format(s[owed ? "BalanceOwed" : "BalanceOwes"], (b.Estimated ? "≈ " : "") + Formats.Money(Math.Abs(b.Net), b.Currency));
            people.Children.Add(Ui.H(8, Ui.Avatar(Name(trip, b.Person), 26), Ui.V(0, Ui.Line(name, "strong"), Ui.H(4, Ui.Icon(owed ? "arrow-left" : "arrow-right", 12, owed ? "Success" : "Warning"), Ui.Line(line, "caption")))));
        }
        body.Children.Add(people);
        var transfers = Expenses.SettleUp(trip, m.SettleCurrency, m.EstimateRates);
        var plan = Ui.V(4, Ui.Text(s["SettleUpHint"], "caption"));
        foreach (var t in transfers)
            plan.Children.Add(Ui.H(8, Ui.Icon("arrow-right", 13, "Text.Subtle").Also(i => i.VerticalAlignment = VerticalAlignment.Center), Ui.Line(string.Format(s["PaysTo"], Name(trip, t.From), Name(trip, t.To))), Ui.Text((t.Estimated ? "≈ " : "") + Formats.Money(t.Amount, t.Currency), "strong").Margin(8, 0, 0, 0)));
        body.Children.Add(plan);
        // Estimates are named in words, and what could not be converted at all stays in its own currency.
        if (transfers.Any(t => t.Estimated)) body.Children.Add(Notice("info", "Text.Subtle", s["EstimatedWithRates"]));
        else if (m.SettleCurrency is { } target && transfers.Any(t => t.Currency != target)) body.Children.Add(Notice("info", "Text.Subtle", string.Format(s["NotConvertedHint"], target)));
        var pay = Ui.Button(s["PayBack"], "wallet", "soft"); ToolTip.SetTip(pay, s["PayBackHint"]);
        pay.Click += (_, _) => _ = m.PayAsync();
        var settle = Ui.Button(s["SettleAll"], "circle-check", "link");
        settle.Click += (_, _) => _ = m.SettleAllAsync();
        body.Children.Add(Ui.H(10, pay, settle));
        return Ui.Card(body, 18);
    }

    /// <summary>
    /// What the card actually charged for an expense in another currency than the home currency: typed as on the
    /// statement, saved with the rate and the day. Null when the expense is in the home currency.
    /// </summary>
    public static Control? Charged(MainWindow w, Entity expense)
    {
        var m = w.Model; var s = m.Strings; var home = m.HomeCurrency;
        if (TripSummaries.Money(expense.Data["amount"]) is not { Amount: not 0 } money) return null;
        var charge = Expenses.ChargeOf(expense);
        if (money.Currency == home && charge is null) return null;
        var currency = charge?.Currency ?? home;
        var shown = charge is { } c ? c.Value.ToString("N2", System.Globalization.CultureInfo.CurrentCulture) : "";
        var box = new TextBox { Text = shown, Width = 130, HorizontalContentAlignment = HorizontalAlignment.Right, PlaceholderText = "0.00", Tag = "charged" };
        AutomationProperties.SetName(box, string.Format(s["ChargedIn"], currency));
        var original = shown;
        async Task Commit()
        {
            if ((box.Text ?? "").Trim() == original.Trim()) return;
            if (await m.SetChargeAsync(expense.Id, box.Text, currency)) { box.Classes.Remove("invalid"); original = box.Text ?? ""; }
            else box.Classes.Add("invalid");
        }
        box.LostFocus += async (_, _) => await Commit();
        box.KeyDown += async (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; await Commit(); } if (e.Key == Key.Escape) { box.Text = original; e.Handled = true; } };
        var panel = Ui.V(6, Ui.H(6, box, Ui.Text(currency, "caption").Also(t => t.VerticalAlignment = VerticalAlignment.Center)));
        panel.Children.Add(charge is { } done
            ? Ui.Text(string.Format(s["ChargedRate"], money.Currency, done.Rate.ToString("0.####", System.Globalization.CultureInfo.CurrentCulture), done.Currency) + (done.Date is { } day ? " · " + Formats.Date(day) : ""), "caption")
            : Ui.Text(string.Format(s["ChargedHint"], money.Currency, currency), "caption").Also(t => t.TextWrapping = TextWrapping.Wrap));
        return panel;
    }

    /// <summary>
    /// The paid expenses in the home currency: exact where amounts are in it or were charged in it, otherwise estimated
    /// with reference rates when allowed, and what is still in another currency. Null when everything is in the home currency.
    /// </summary>
    public static Control? HomeTotal(MainWindow w)
    {
        var m = w.Model; var s = m.Strings; var trip = m.Trip!; var home = m.HomeCurrency;
        var expenses = trip.Entities.Values.Where(e => e.Type == "expense" && TripSummaries.Money(e.Data["amount"]) is not null).ToArray();
        if (expenses.All(e => TripSummaries.Money(e.Data["amount"])!.Currency == home)) return null;
        var total = Expenses.Total(trip, home, m.EstimateRates);
        // The home currency is chosen here; amounts charged keep the currency they were entered in.
        var choice = new SearchChoice(Editors.Currencies, home, s["HomeCurrency"]) { Width = 120 };
        void Choose() { if (choice.Value is { } code && code != m.HomeCurrency) m.HomeCurrency = code; }
        choice.Input.SelectionChanged += (_, _) => Choose(); choice.Input.LostFocus += (_, _) => Choose();
        var body = Ui.V(8, Ui.Columns("*,Auto", Ui.Text(string.Format(s["InCurrency"], home), "overline").Also(t => t.VerticalAlignment = VerticalAlignment.Center), choice),
            Ui.Text((total.Estimated > 0 ? "≈ " : "") + Formats.Money(total.Total, home), "h1"),
            Ui.H(5, Ui.Icon("circle-check", 13, "Success"), Ui.Text(string.Format(s["ExactlyCharged"], Formats.Money(total.Exact, home)), "caption")));
        if (total.Estimated > 0) body.Children.Add(Ui.H(5, Ui.Icon("clock", 13, "Warning"), Ui.Text(string.Format(s["EstimatedAmountRates"], Formats.Money(total.Estimated, home)), "caption")));
        if (total.NotConverted.Count > 0) body.Children.Add(Notice("info", "Text.Subtle", string.Format(s["NotConvertedYet"], string.Join(" + ", total.NotConverted.Select(x => Formats.Money(x.Amount, x.Currency))))));
        if (total.WithoutCharge > 0) body.Children.Add(Ui.Text((total.WithoutCharge == 1 ? s["WithoutChargeOne"] : string.Format(s["WithoutChargeCount"], total.WithoutCharge)), "caption").Also(t => t.TextWrapping = TextWrapping.Wrap));
        return Ui.Card(body, 18).Also(c => c.Width = 300);
    }

    /// <summary>The icon of an expense category; categories of the travellers' own share a tag.</summary>
    public static string CategoryIcon(string category) => category switch
    {
        ExpenseCategories.Transport => "route",
        ExpenseCategories.Accommodation => "bed-double",
        ExpenseCategories.Food => "utensils",
        ExpenseCategories.Activity => "ferris-wheel",
        ExpenseCategories.Souvenirs => "shopping-bag",
        ExpenseCategories.Other => "circle-dot",
        _ => "tag"
    };

    /// <summary>The name of a category: standard ones translated, the travellers' own as they typed them.</summary>
    public static string CategoryName(Localization s, string category) => ExpenseCategories.IsStandard(category) ? s["expcat." + category] : category;

    /// <summary>
    /// The category of an expense as chips: the standard ones, those the travellers created in this trip, and a
    /// button for a new one. Without a stored category the suggested one is marked, with a note where it comes from;
    /// choosing the marked chip again goes back to the suggestion.
    /// </summary>
    public static Control CategoryEditor(Localization s, TripSnapshot trip, Entity expense, Func<Func<TripSnapshot, Entity, Entity>, Task> change, Func<Task<string?>> askName)
    {
        var stored = ExpenseCategories.Stored(expense);
        var current = ExpenseCategories.Of(trip, expense);
        var chips = new WrapPanel { ItemSpacing = 6, LineSpacing = 6 };
        foreach (var category in ExpenseCategories.Standard.Concat(ExpenseCategories.Custom(trip)).Append(stored).OfType<string>().Distinct())
        {
            var chip = Ui.Choice(CategoryName(s, category), CategoryIcon(category), category == current, "chip");
            chip.Click += (_, _) => _ = change((t, e) => ExpenseCategories.With(t, e, category == stored ? null : category));
            chips.Children.Add(chip);
        }
        var add = Ui.Button(s["NewCategory"], "plus", "chip");
        add.Click += async (_, _) => { if (await askName() is { } name && name.Trim().Length > 0) await change((t, e) => ExpenseCategories.With(t, e, name)); };
        chips.Children.Add(add);
        AutomationProperties.SetName(chips, s["Category"]);
        var panel = Ui.V(6, chips);
        if (stored is null && ExpenseCategories.Suggested(trip, expense) is not null) panel.Children.Add(Ui.Text(s["CategorySuggested"], "caption").Also(t => t.TextWrapping = TextWrapping.Wrap));
        return panel;
    }

    /// <summary>
    /// The paid expenses per category, in the currency who owes whom uses where they can be converted, largest first,
    /// each with a bar for its part of its currency's total. Null without expenses.
    /// </summary>
    public static Control? ByCategory(MainWindow w)
    {
        var m = w.Model; var s = m.Strings; var trip = m.Trip!;
        var totals = ExpenseCategories.Totals(trip, m.SettleCurrency, m.EstimateRates);
        if (totals.Count == 0) return null;
        var rows = Ui.V(8);
        foreach (var currency in totals.GroupBy(t => t.Currency))
        {
            var sum = currency.Sum(t => t.Amount);
            foreach (var t in currency)
            {
                var ratio = sum == 0 ? 0 : (double)(t.Amount / sum);
                var bar = new Grid { ColumnDefinitions = new ColumnDefinitions($"{ratio.ToString(System.Globalization.CultureInfo.InvariantCulture)}*,{(1 - ratio).ToString(System.Globalization.CultureInfo.InvariantCulture)}*"), Height = 6, Margin = new Thickness(0, 4, 0, 0) };
                bar.Children.Add(new Border { CornerRadius = new CornerRadius(3) }.Res(Border.BackgroundProperty, "Bg.Muted").Col(0, 2));
                bar.Children.Add(new Border { CornerRadius = new CornerRadius(3) }.Res(Border.BackgroundProperty, "Accent"));
                var amount = (t.Estimated ? "≈ " : "") + Formats.Money(t.Amount, t.Currency) + " · " + ratio.ToString("P0", System.Globalization.CultureInfo.CurrentCulture);
                rows.Children.Add(Ui.V(0, Ui.Columns("Auto,*,Auto", Ui.Icon(CategoryIcon(t.Category), 14, "Text.Muted").Margin(0, 0, 8, 0), Ui.Line(CategoryName(s, t.Category)), Ui.Text(amount, "strong")), bar));
            }
        }
        var body = Ui.V(12, Ui.Text(s["ByCategory"], "h3"), rows);
        if (totals.Any(t => t.Estimated)) body.Children.Add(Notice("info", "Text.Subtle", s["EstimatedWithRates"]));
        return Ui.Card(body, 18);
    }

    /// <summary>
    /// In a booking's details: its expenses, and how they compare with the price. The price itself never counts; the
    /// note says whether nothing, part or all of it is recorded, and offers to record what is still open.
    /// </summary>
    public static Control? BookingExpenses(MainWindow w, Entity booking)
    {
        var m = w.Model; var s = m.Strings; var trip = m.Trip!;
        var payment = Expenses.Payment(trip, booking);
        if (payment.Coverage == BookingCoverage.NoPrice && payment.Expenses.Count == 0) return null;
        var panel = Ui.V(6);
        string Money(decimal amount) => Formats.Money(amount, payment.Price!.Currency);
        switch (payment.Coverage)
        {
            case BookingCoverage.None: panel.Children.Add(Notice("triangle-alert", "Warning", s["BookingNoExpense"])); break;
            case BookingCoverage.Partial: panel.Children.Add(Notice("clock", "Warning", string.Format(s["BookingPartlyRecorded"], Money(payment.Recorded), Money(payment.Price!.Amount), Money(payment.Rest)))); break;
            case BookingCoverage.Complete: panel.Children.Add(Notice("circle-check", "Success", s["BookingFullyRecorded"])); break;
            case BookingCoverage.More: panel.Children.Add(Notice("info", "Text.Subtle", string.Format(s["BookingMoreRecorded"], Money(payment.Recorded), Money(-payment.Rest)))); break;
            case BookingCoverage.OtherCurrency: panel.Children.Add(Notice("info", "Text.Subtle", string.Format(s["BookingOtherCurrency"], payment.Price!.Currency))); break;
        }
        foreach (var expense in payment.Expenses) panel.Children.Add(InspectorView.Link(w, expense));
        if (payment.Coverage is BookingCoverage.None or BookingCoverage.Partial)
        {
            var add = Ui.Button(s[payment.Coverage == BookingCoverage.None ? "AddAsExpense" : "AddRestAsExpense"], "receipt", "soft").Also(b => b.HorizontalAlignment = HorizontalAlignment.Left);
            add.Click += (_, _) => _ = m.AddExpenseForBookingAsync(booking.Id);
            panel.Children.Add(add);
        }
        return panel;
    }

    /// <summary>A pill for the bookings list when a booking's price is not, or only partly, recorded as expenses.</summary>
    public static Control? MissingExpensePill(Localization s, IReadOnlyDictionary<Guid, BookingPayment> payments, Entity booking) =>
        payments.GetValueOrDefault(booking.Id)?.Coverage switch
        {
            BookingCoverage.None => Ui.Pill(s["NoExpenseYet"], "receipt", "Warning.Soft", "Warning"),
            BookingCoverage.Partial => Ui.Pill(s["RestOpen"], "clock", "Warning.Soft", "Warning"),
            _ => null
        };

    /// <summary>On the Costs page: bookings whose price is not, or only partly, recorded, each with a button that records what is open.</summary>
    public static Control? MissingExpenses(MainWindow w)
    {
        var m = w.Model; var s = m.Strings; var trip = m.Trip!;
        var open = Expenses.Payments(trip).Where(p => p.Coverage is BookingCoverage.None or BookingCoverage.Partial).ToArray();
        if (open.Length == 0) return null;
        var list = Ui.V(4);
        foreach (var payment in open)
        {
            var booking = payment.Booking; var price = payment.Price!;
            var partial = payment.Coverage == BookingCoverage.Partial;
            var add = Ui.Button(s[partial ? "AddRestAsExpenseShort" : "AddAsExpense"], "plus", "small");
            AutomationProperties.SetName(add, string.Format(s[partial ? "AddRestAsExpenseFor" : "AddAsExpenseFor"], booking.Title));
            add.Click += (_, _) => _ = m.AddExpenseForBookingAsync(booking.Id);
            var amount = partial ? string.Format(s["RestAmount"], Formats.Money(payment.Rest, price.Currency)) : Formats.Money(price.Amount, price.Currency);
            list.Children.Add(Ui.Columns("*,Auto,Auto", Ui.Line(booking.Title).Also(t => t.VerticalAlignment = VerticalAlignment.Center), Ui.Text(amount, "strong").Margin(12, 0).Also(t => t.VerticalAlignment = VerticalAlignment.Center), add));
        }
        var heading = Ui.Columns("Auto,*", Ui.Icon("triangle-alert", 15, "Warning").Margin(0, 2, 8, 0).Also(i => i.VerticalAlignment = VerticalAlignment.Top),
            Ui.V(2, Ui.Text(open.Length == 1 ? s["BookingsWithoutExpenseOne"] : string.Format(s["BookingsWithoutExpense"], open.Length), "h3"), Ui.Text(s["BookingsWithoutExpenseHint"], "caption").Also(t => t.TextWrapping = TextWrapping.Wrap)));
        return Ui.Card(Ui.V(12, heading, list), 18);
    }

    private static readonly (string Scope, string Icon)[] BudgetScopes = [(Budgets.Trip, "luggage"), (Budgets.Category, "tag"), (Budgets.Participant, "user"), (Budgets.Collection, "layers")];

    /// <summary>What a budget covers, in words, such as "Category · Food" or "Person · Ana".</summary>
    public static string BudgetFor(Localization s, TripSnapshot trip, BudgetStatus status) => status.Scope switch
    {
        Budgets.Trip => s["BudgetWholeTrip"],
        Budgets.Category => s["category"] + " · " + (status.Target is { } c ? CategoryName(s, c) : s["BudgetChooseTarget"]),
        _ => s[status.Scope] + " · " + (Guid.TryParse(status.Target, out var id) && trip.Find(id) is { } e ? e.Title : s["BudgetChooseTarget"])
    };

    /// <summary>
    /// What a budget covers, as chips: the whole trip, a category, a person or a collection, and then which one.
    /// </summary>
    public static Control BudgetScopeEditor(MainWindow w, Entity budget)
    {
        var m = w.Model; var s = m.Strings; var trip = m.Trip!;
        var scope = Budgets.ScopeOf(budget); var target = Budgets.TargetOf(budget);
        var scopes = new WrapPanel { ItemSpacing = 6, LineSpacing = 6 };
        foreach (var (option, icon) in BudgetScopes)
        {
            var chip = Ui.Choice(s[option], icon, option == scope, "chip");
            chip.Click += (_, _) => { if (option != scope) _ = m.SetBudgetScopeAsync(budget.Id, option); };
            scopes.Children.Add(chip);
        }
        AutomationProperties.SetName(scopes, s["BudgetScope"]);
        var panel = Ui.V(8, scopes);
        IEnumerable<(string Id, string Label, string Icon)> targets = scope switch
        {
            Budgets.Category => ExpenseCategories.Standard.Concat(ExpenseCategories.Custom(trip)).Select(c => (c, CategoryName(s, c), CategoryIcon(c))),
            Budgets.Participant => TripPeople.Travelers(trip).Select(p => (p.Id.ToString(), p.Title, "user")),
            Budgets.Collection => trip.Entities.Values.Where(e => e.Type == "collection" && !PackingLists.IsList(e)).OrderBy(e => e.Title, StringComparer.CurrentCulture).Select(c => (c.Id.ToString(), c.Title, "layers")),
            _ => []
        };
        var choices = new WrapPanel { ItemSpacing = 6, LineSpacing = 6 };
        foreach (var (id, label, icon) in targets)
        {
            var chip = Ui.Choice(label, icon, string.Equals(id, target, StringComparison.CurrentCultureIgnoreCase), "chip");
            chip.Click += (_, _) => _ = m.SetBudgetScopeAsync(budget.Id, scope, id);
            choices.Children.Add(chip);
        }
        if (choices.Children.Count > 0) { AutomationProperties.SetName(choices, s["BudgetTarget"]); panel.Children.Add(choices); }
        else if (scope == Budgets.Collection) panel.Children.Add(Notice("info", "Text.Subtle", s["BudgetNoCollections"]));
        if (Budgets.Status(trip, budget, m.EstimateRates) is { } status) panel.Children.Add(BudgetLine(s, trip, status, 300));
        return panel;
    }

    /// <summary>One budget: what it covers, a bar, and what is spent, planned and left, said in words.</summary>
    private static Control BudgetLine(Localization s, TripSnapshot trip, BudgetStatus b, double? barWidth = null)
    {
        var currency = b.Amount.Currency;
        string Money(decimal x) => (b.Approximate ? "≈ " : "") + Formats.Money(x, currency);
        var ratio = b.Amount.Amount <= 0 ? 1 : (double)Math.Min(1m, b.Used / b.Amount.Amount);
        var over = b.Left < 0;
        var bar = new Grid { ColumnDefinitions = new ColumnDefinitions($"{ratio.ToString(System.Globalization.CultureInfo.InvariantCulture)}*,{(1 - ratio).ToString(System.Globalization.CultureInfo.InvariantCulture)}*"), Height = 8 };
        if (barWidth is { } width) { bar.Width = width; bar.HorizontalAlignment = HorizontalAlignment.Left; }
        bar.Children.Add(new Border { CornerRadius = new CornerRadius(4) }.Res(Border.BackgroundProperty, "Bg.Muted").Col(0, 2));
        bar.Children.Add(new Border { CornerRadius = new CornerRadius(4) }.Res(Border.BackgroundProperty, over ? "Danger" : ratio > 0.85 ? "Warning" : "Accent"));
        AutomationProperties.SetName(bar, string.Format(s["BudgetUsed"], Money(b.Used), Formats.Money(b.Amount.Amount, currency)));
        var line = Ui.V(4, bar, Ui.H(5, Ui.Icon(over ? "triangle-alert" : "circle-check", 13, over ? "Danger" : "Success"),
            Ui.Text(string.Format(s["BudgetUsed"], Money(b.Used), Formats.Money(b.Amount.Amount, currency)) + " · " + (over ? string.Format(s["BudgetOver"], Money(-b.Left)) : string.Format(s["BudgetLeft"], Money(b.Left))), "caption")));
        if (b.Planned > 0) line.Children.Add(Ui.Text(string.Format(s["BudgetPlanned"], Money(b.Planned)), "caption"));
        if (b.NotIncluded.Count > 0) line.Children.Add(Notice("info", "Text.Subtle", string.Format(s["BudgetNotIncluded"], string.Join(" + ", b.NotIncluded.Select(x => Formats.Money(x.Amount, x.Currency))))));
        return line;
    }

    /// <summary>On the Costs page: every budget with what it covers and where it stands. Null without budgets.</summary>
    public static Control? BudgetsCard(MainWindow w)
    {
        var m = w.Model; var s = m.Strings; var trip = m.Trip!;
        var budgets = Budgets.All(trip, m.EstimateRates);
        if (budgets.Count == 0) return null;
        var rows = Ui.V(14);
        foreach (var b in budgets)
        {
            var open = new Button { Content = Ui.Columns("Auto,*", Ui.Icon(BudgetScopes.First(x => x.Scope == b.Scope).Icon, 14, "Text.Muted").Margin(0, 2, 8, 0).Also(i => i.VerticalAlignment = VerticalAlignment.Top), Ui.V(1, Ui.Line(b.Budget.Title, "strong"), Ui.Line(BudgetFor(s, trip, b), "caption"))) }.Classed("row");
            AutomationProperties.SetName(open, b.Budget.Title);
            var budget = b.Budget; open.Click += (_, _) => m.Follow(budget);
            rows.Children.Add(Ui.V(6, open, BudgetLine(s, trip, b).Margin(8, 0)));
        }
        var body = Ui.V(12, Ui.Text(s["Budgets"], "h3"), rows);
        if (budgets.Any(b => b.Approximate)) body.Children.Add(Notice("info", "Text.Subtle", s["EstimatedWithRates"]));
        return Ui.Card(body, 18);
    }

    private static Control Notice(string icon, string brush, string text) =>
        Ui.Columns("Auto,*", Ui.Icon(icon, 13, brush).Margin(0, 2, 6, 0).Also(i => i.VerticalAlignment = VerticalAlignment.Top), Ui.Text(text, "caption").Also(t => t.TextWrapping = TextWrapping.Wrap));
}
