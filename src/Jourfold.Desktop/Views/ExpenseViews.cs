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

    /// <summary>In a booking's details: the expenses recorded for it, or, when it has a price but none, a note and a button to add one.</summary>
    public static Control? BookingExpenses(MainWindow w, Entity booking)
    {
        var m = w.Model; var s = m.Strings; var trip = m.Trip!;
        if (Expenses.WithoutExpense(trip).Any(b => b.Id == booking.Id))
        {
            var add = Ui.Button(s["AddAsExpense"], "receipt", "soft").Also(b => b.HorizontalAlignment = HorizontalAlignment.Left);
            add.Click += (_, _) => _ = m.AddExpenseForBookingAsync(booking.Id);
            return Ui.V(8, Notice("triangle-alert", "Warning", s["BookingNoExpense"]), add);
        }
        var expenses = Expenses.ForBooking(trip, booking.Id);
        return expenses.Count == 0 ? null : Ui.V(2, expenses.Select(e => (Control)InspectorView.Link(w, e)).ToArray());
    }

    /// <summary>A pill for the bookings list when a booking's price is not recorded as an expense yet.</summary>
    public static Control? MissingExpensePill(Localization s, IReadOnlySet<Guid> missing, Entity booking) =>
        missing.Contains(booking.Id) ? Ui.Pill(s["NoExpenseYet"], "receipt", "Warning.Soft", "Warning") : null;

    /// <summary>On the Costs page: bookings with a price but no expense, each with a button that records it.</summary>
    public static Control? MissingExpenses(MainWindow w)
    {
        var m = w.Model; var s = m.Strings; var trip = m.Trip!;
        var missing = Expenses.WithoutExpense(trip);
        if (missing.Count == 0) return null;
        var list = Ui.V(4);
        foreach (var booking in missing)
        {
            var price = TripSummaries.Money(booking.Data["price"])!;
            var add = Ui.Button(s["AddAsExpense"], "plus", "small");
            AutomationProperties.SetName(add, string.Format(s["AddAsExpenseFor"], booking.Title));
            add.Click += (_, _) => _ = m.AddExpenseForBookingAsync(booking.Id);
            list.Children.Add(Ui.Columns("*,Auto,Auto", Ui.Line(booking.Title).Also(t => t.VerticalAlignment = VerticalAlignment.Center), Ui.Text(Formats.Money(price.Amount, price.Currency), "strong").Margin(12, 0).Also(t => t.VerticalAlignment = VerticalAlignment.Center), add));
        }
        var heading = Ui.Columns("Auto,*", Ui.Icon("triangle-alert", 15, "Warning").Margin(0, 2, 8, 0).Also(i => i.VerticalAlignment = VerticalAlignment.Top),
            Ui.V(2, Ui.Text(missing.Count == 1 ? s["BookingsWithoutExpenseOne"] : string.Format(s["BookingsWithoutExpense"], missing.Count), "h3"), Ui.Text(s["BookingsWithoutExpenseHint"], "caption").Also(t => t.TextWrapping = TextWrapping.Wrap)));
        return Ui.Card(Ui.V(12, heading, list), 18);
    }

    private static Control Notice(string icon, string brush, string text) =>
        Ui.Columns("Auto,*", Ui.Icon(icon, 13, brush).Margin(0, 2, 6, 0).Also(i => i.VerticalAlignment = VerticalAlignment.Top), Ui.Text(text, "caption").Also(t => t.TextWrapping = TextWrapping.Wrap));
}
