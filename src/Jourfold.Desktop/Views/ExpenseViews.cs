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
            // Somebody always shares it; the last person cannot leave.
            if (included && split.People.Count == 1) { check.IsEnabled = false; ToolTip.SetTip(check, s["SplitLastPerson"]); }
            check.IsCheckedChanged += (_, _) => _ = change((t, e) => Expenses.WithPerson(t, e, person, check.IsChecked == true));
            Control? input = null;
            if (method != SplitMethod.Equal && included)
            {
                var stored = (expense.Data["split"]?["parts"] as System.Text.Json.Nodes.JsonArray ?? []).FirstOrDefault(p => p?["person"]?.ToString() == person.ToString())?["value"]?.ToString();
                var shown = ExchangeRates.Amount(stored) is { } v ? v.ToString(method == SplitMethod.Amount ? "N" + Expenses.MinorDigits(currency) : "0.##", System.Globalization.CultureInfo.CurrentCulture) : "";
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

    /// <summary>
    /// What each person owes for the expense, each with a check box for "paid back". A debt paid back in part, for
    /// example after the split changed, says what is still open of it.
    /// </summary>
    public static Control? PayingBack(MainWindow w, Entity expense)
    {
        var m = w.Model; var s = m.Strings; var trip = m.Trip!;
        var split = Expenses.Split(trip, expense);
        if (split.Debts.Count == 0) return null;
        var list = Ui.V(4);
        foreach (var debt in split.Debts)
        {
            // Money owed back after a changed split reads the other way round.
            var (from, to, amount) = debt.Open < 0 ? (debt.To, debt.From, -debt.Open) : (debt.From, debt.To, debt.Open == 0 ? debt.Amount : debt.Open);
            var text = string.Format(s["OwesTo"], Name(trip, from), Name(trip, to));
            var transferred = debt.PaidIn is { } t ? " · " + string.Format(s["PaidBackAs"], (t.Reference ? "≈ " : "") + Formats.Money(t.Value, t.Currency)) : "";
            var detail = debt.IsSettled && debt.SettledOn is { } day ? string.Format(s["PaidOn"], Formats.Date(day)) + transferred
                : debt.Open > 0 && debt.Settled > 0 ? string.Format(s["OpenOfDebt"], Formats.Money(debt.Settled, debt.Currency), Formats.Money(debt.Amount, debt.Currency)) : null;
            var check = new CheckBox { IsChecked = debt.IsSettled, MinWidth = 0, HorizontalContentAlignment = HorizontalAlignment.Stretch, Content = Ui.V(1, Ui.Line(text), detail is null ? null : Ui.Line(detail, "caption")) };
            AutomationProperties.SetName(check, string.Format(s["MarkPaidFor"], Name(trip, from), Name(trip, to), Formats.Money(amount, debt.Currency)));
            check.IsCheckedChanged += (_, _) => _ = m.SetDebtPaidAsync(debt.Key, check.IsChecked == true);
            var money = Ui.Text(Formats.Money(amount, debt.Currency), "strong").Also(t => { t.HorizontalAlignment = HorizontalAlignment.Right; t.VerticalAlignment = VerticalAlignment.Center; });
            if (debt.IsSettled) money.Classes.Add("muted");
            list.Children.Add(Ui.Columns("*,Auto", check, money));
        }
        var all = Ui.Button(split.IsSettled ? s["UnmarkAllPaid"] : s["MarkExpensePaid"], split.IsSettled ? "rotate-ccw" : "circle-check", "link");
        all.HorizontalAlignment = HorizontalAlignment.Left;
        all.Click += (_, _) => _ = m.SetExpensePaidAsync(expense.Id, !split.IsSettled);
        list.Children.Add(all);
        return list;
    }

    /// <summary>A pill for the cost list: still to pay, open, settled, or a split to check. Null when there is nothing to say.</summary>
    public static Control? Status(Localization s, ExpenseSplit split, TripSnapshot? trip = null)
    {
        if (trip is not null && Expenses.IsIdea(trip, split.Expense)) return Ui.Pill(s["IdeaPill"], "lightbulb", "Bg.Subtle", "Text.Muted");
        if (split.Issues.HasFlag(SplitIssues.Estimated)) return Ui.Pill(s["ToPay"], "clock", "Warning.Soft", "Warning");
        if (split.Issues.HasFlag(SplitIssues.SumMismatch) || split.Issues.HasFlag(SplitIssues.InvalidValue) || split.Issues.HasFlag(SplitIssues.NoPeople)) return Ui.Pill(s["CheckSplit"], "triangle-alert", "Danger.Soft", "Danger");
        if (split.Debts.Count == 0) return null;
        return split.IsSettled ? Ui.Pill(s["SettledState"], "circle-check", "Success.Soft", "Success") : Ui.Pill(string.Format(s["OpenAmount"], Formats.Money(split.Open, split.Amount!.Currency)), "clock", "Warning.Soft", "Warning");
    }

    /// <summary>
    /// Balances in the trip's currency, the fewest payments that settle them, and the pay function. Expenses whose
    /// split needs attention are named first, since they are left out until they are fixed.
    /// </summary>
    public static Control? WhoOwesWhom(MainWindow w)
    {
        var m = w.Model; var s = m.Strings; var trip = m.Trip!; var currency = m.TripCurrencyCode;
        var problems = Expenses.Problems(trip);
        if (Expenses.Debts(trip).Count == 0 && problems.Count == 0) return null;
        var balances = Expenses.Balances(trip, currency, m.EstimateRates);
        var body = Ui.V(12, Ui.V(2, Ui.Text(s["WhoOwesWhom"], "h3"), Ui.Text(string.Format(s["WhoOwesWhomHintIn"], currency), "caption").Also(t => t.TextWrapping = TextWrapping.Wrap)));
        if (problems.Count > 0)
        {
            var list = Ui.V(2, Notice("triangle-alert", "Danger", problems.Count == 1 ? s["SplitProblemsOne"] : string.Format(s["SplitProblems"], problems.Count)));
            foreach (var p in problems) list.Children.Add(InspectorView.Link(w, p.Expense).Also(b => b.HorizontalAlignment = HorizontalAlignment.Left));
            body.Children.Add(list);
        }
        if (balances.Count == 0)
        {
            body.Children.Add(Notice("circle-check", "Success", s["AllEven"]));
            return Ui.Card(body, 18);
        }
        var people = new WrapPanel { ItemSpacing = 18, LineSpacing = 10 };
        // One entry per person, with every currency they get or owe.
        foreach (var (person, direction, line) in Jourfold.UI.CostTexts.Balances(s, balances))
        {
            var name = Name(trip, person) + (m.Me?.Id == person ? " (" + s["You"] + ")" : "");
            var (icon, brush) = direction switch { > 0 => ("arrow-left", "Success"), < 0 => ("arrow-right", "Warning"), _ => ("coins", "Text.Subtle") };
            people.Children.Add(Ui.H(8, Ui.Avatar(Name(trip, person), 26), Ui.V(0, Ui.Line(name, "strong"), Ui.H(4, Ui.Icon(icon, 12, brush), Ui.Line(line, "caption")))));
        }
        body.Children.Add(people);
        var transfers = Expenses.SettleUp(trip, currency, m.EstimateRates);
        var plan = Ui.V(4, Ui.Text(s["SettleUpHint"], "caption"));
        foreach (var t in transfers.Where(t => t.Currency == currency))
            plan.Children.Add(Ui.H(8, Ui.Icon("arrow-right", 13, "Text.Subtle").Also(i => i.VerticalAlignment = VerticalAlignment.Center), Ui.Line(string.Format(s["PaysTo"], Name(trip, t.From), Name(trip, t.To))), Ui.Text((t.Estimated ? "≈ " : "") + Formats.Money(t.Amount, t.Currency), "strong").Margin(8, 0, 0, 0)));
        // What could not be converted at all stays in its own currency, listed apart with how to fix it.
        if (transfers.Where(t => t.Currency != currency).ToArray() is { Length: > 0 } apart)
        {
            plan.Children.Add(Notice("info", "Text.Subtle", string.Format(s["NotConvertedHint"], currency)).Margin(0, 6, 0, 0));
            foreach (var t in apart)
                plan.Children.Add(Ui.H(8, Ui.Icon("arrow-right", 13, "Text.Subtle").Also(i => i.VerticalAlignment = VerticalAlignment.Center), Ui.Line(string.Format(s["PaysTo"], Name(trip, t.From), Name(trip, t.To))), Ui.Text(Formats.Money(t.Amount, t.Currency), "strong").Margin(8, 0, 0, 0)));
        }
        body.Children.Add(plan);
        if (transfers.Any(t => t.Estimated)) body.Children.Add(Notice("info", "Text.Subtle", m.EstimatedNote));
        var pay = Ui.Button(s["PayBack"], "wallet", "soft"); ToolTip.SetTip(pay, s["PayBackHint"]);
        pay.Click += (_, _) => _ = m.PayAsync();
        var settle = Ui.Button(s["SettleAll"], "circle-check", "link");
        settle.Click += (_, _) => _ = m.SettleAllAsync();
        body.Children.Add(Ui.H(10, pay, settle));
        return Ui.Card(body, 18);
    }

    /// <summary>
    /// What the card actually booked for an expense in another currency than the trip's, next to the amount on the
    /// bill: typed as on the statement, saved with the rate and the day of the charge. Null when the expense is in the
    /// trip's currency.
    /// </summary>
    public static Control? Charged(MainWindow w, Entity expense)
    {
        var m = w.Model; var s = m.Strings; var home = m.TripCurrencyCode; var trip = m.Trip!;
        if (TripSummaries.Money(expense.Data["amount"]) is not { Amount: not 0 } money) return null;
        var charge = Expenses.ChargeOf(expense);
        if (money.Currency == home && charge is null) return null;
        var currency = charge?.Currency ?? home;
        var shown = charge is { } c ? c.Value.ToString("N" + Expenses.MinorDigits(c.Currency), System.Globalization.CultureInfo.CurrentCulture) : "";
        var box = new TextBox { Text = shown, Width = 130, HorizontalContentAlignment = HorizontalAlignment.Right, PlaceholderText = 0m.ToString("N" + Expenses.MinorDigits(currency), System.Globalization.CultureInfo.CurrentCulture), Tag = "charged" };
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
        var row = Ui.H(6, box, Ui.Text(currency, "caption").Also(t => t.VerticalAlignment = VerticalAlignment.Center));
        // The day of the charge, the expense's own day until changed; it keeps the rate historically right.
        if (charge is { } done)
        {
            var day = new DateField { SelectedDate = done.Date is { } d ? d.ToDateTimeUnspecified() : null, Width = 150 };
            AutomationProperties.SetName(day, s["ChargedOn"]);
            day.SelectedDateChanged += (_, _) => { if (day.SelectedDate is { } picked && NodaTime.LocalDate.FromDateTime(picked) is var chosen && chosen != done.Date) _ = m.SetChargeDayAsync(expense.Id, chosen); };
            row.Children.Add(day.Margin(8, 0, 0, 0));
        }
        var panel = Ui.V(6, row);
        panel.Children.Add(charge is { } known
            ? Ui.Text(string.Format(s["ChargedRate"], money.Currency, known.Rate.ToString("0.####", System.Globalization.CultureInfo.CurrentCulture), known.Currency), "caption")
            : Ui.Text(string.Format(s["ChargedHint"], money.Currency, currency), "caption").Also(t => t.TextWrapping = TextWrapping.Wrap));
        return panel;
    }

    /// <summary>
    /// What the trip cost so far in its currency: exact where amounts are in it or were charged in it, otherwise
    /// estimated with reference rates when allowed, what is still to pay, and what is still in another currency. The
    /// currency can be chosen here for everyone. Null without expenses.
    /// </summary>
    public static Control? TripTotal(MainWindow w)
    {
        // What is in the trip's currency and what is not, without a zero in front: "≈ EUR 40.00 + JPY 5,000".
        string Sum(ConvertedTotal t) => string.Join(" + ", new[] { t.Total != 0 ? (t.Estimated != 0 ? "≈ " : "") + Formats.Money(t.Total, t.Currency) : null }.Concat(t.NotConverted.Select(x => Formats.Money(x.Amount, x.Currency))).OfType<string>());
        var m = w.Model; var s = m.Strings; var trip = m.Trip!; var home = m.TripCurrencyCode;
        var expenses = trip.Entities.Values.Where(e => e.Type == "expense" && TripSummaries.Money(e.Data["amount"]) is not null).ToArray();
        if (expenses.Length == 0) return null;
        var total = Expenses.Total(expenses, home, m.EstimateRates);
        // Payments for ideas say what they would cost and are listed apart.
        var ideas = expenses.Where(e => Expenses.IsIdea(trip, e)).ToArray();
        var planned = Expenses.Total(expenses.Except(ideas), home, m.EstimateRates, planned: true);
        var choice = new SearchChoice(Editors.Currencies, home, s["TripCurrency"]) { Width = 120 };
        ToolTip.SetTip(choice, s["TripCurrencyHint"]);
        void Choose() { if (choice.Value is { } code && code != m.TripCurrencyCode) _ = m.SetTripCurrencyAsync(code); }
        choice.Input.SelectionChanged += (_, _) => Choose(); choice.Input.LostFocus += (_, _) => Choose();
        var body = Ui.V(8, Ui.Columns("*,Auto", Ui.Text(string.Format(s["InCurrency"], home), "overline").Also(t => t.VerticalAlignment = VerticalAlignment.Center), choice),
            Ui.Text((total.Estimated > 0 ? "≈ " : "") + Formats.Money(total.Total, home), "h1"),
            IconLine(Ui.Icon("circle-check", 13, "Success"), Ui.Text(string.Format(s["ExactlyCharged"], Formats.Money(total.Exact, home)), "caption")));
        if (total.Estimated > 0) body.Children.Add(IconLine(Ui.Icon("clock", 13, "Warning"), Ui.Text(string.Format(s["EstimatedAmountRates"], Formats.Money(total.Estimated, home), m.RatesDay), "caption")));
        if (Sum(planned) is { Length: > 0 } toPay) body.Children.Add(IconLine(Ui.Icon("calendar-clock", 13, "Text.Muted"), Ui.Text(string.Format(s["StillToPayAmount"], toPay), "caption")));
        if (ideas.Length > 0 && Sum(Expenses.Total(ideas, home, m.EstimateRates, planned: true)) is { Length: > 0 } maybe) body.Children.Add(IconLine(Ui.Icon("lightbulb", 13, "Text.Muted"), Ui.Text(string.Format(s["IdeasNotCounted"], maybe), "caption")));
        if (total.NotConverted.Count > 0) body.Children.Add(Notice("info", "Text.Subtle", string.Format(s["NotConvertedYet"], string.Join(" + ", total.NotConverted.Select(x => Formats.Money(x.Amount, x.Currency))))));
        if (total.WithoutCharge > 0 && expenses.Any(e => TripSummaries.Money(e.Data["amount"])!.Currency != home)) body.Children.Add(Ui.Text(total.WithoutCharge == 1 ? s["WithoutChargeOne"] : string.Format(s["WithoutChargeCount"], total.WithoutCharge), "caption").Also(t => t.TextWrapping = TextWrapping.Wrap));
        return Ui.Card(body, 18).Also(c => c.Width = 300);
    }

    /// <summary>The icon of an expense category; categories of the travellers' own share a tag.</summary>
    public static string CategoryIcon(string category) => CostTexts.CategoryIcon(category);

    /// <summary>The name of a category: standard ones translated, the travellers' own as they typed them.</summary>
    public static string CategoryName(Localization s, string category) => CostTexts.Category(s, category);

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
    /// The paid expenses per category in the trip's currency, largest first, each with a bar for its part of the total.
    /// What cannot be converted is listed below, per currency. Null without expenses.
    /// </summary>
    public static Control? ByCategory(MainWindow w)
    {
        var m = w.Model; var s = m.Strings; var trip = m.Trip!; var home = m.TripCurrencyCode;
        // Everyone's expenses in full, or the parts of one or several people, such as a couple.
        m.CategoryPeople.RemoveWhere(p => trip.Find(p) is not { Type: "person" });
        if (!trip.Entities.Values.Any(e => e.Type == "expense" && !Expenses.IsPlanned(e))) return null;
        var totals = ExpenseCategories.Totals(trip, home, m.EstimateRates, m.CategoryPeople.Count > 0 ? m.CategoryPeople : null);
        var whose = new WrapPanel { ItemSpacing = 6, LineSpacing = 6 };
        var everyone = Ui.Choice(s["Everyone"], "users", m.CategoryPeople.Count == 0, "chip"); everyone.Click += (_, _) => m.ToggleCategoryPerson(null);
        whose.Children.Add(everyone);
        foreach (var person in TripPeople.Travelers(trip))
        {
            var id = person.Id;
            var chip = Ui.Choice(person.Title, null, m.CategoryPeople.Contains(id), "chip");
            AutomationProperties.SetName(chip, string.Format(s["CategoriesOf"], person.Title));
            chip.Click += (_, _) => m.ToggleCategoryPerson(id);
            whose.Children.Add(chip);
        }
        AutomationProperties.SetName(whose, s["CategoriesFor"]);
        var rows = Ui.V(8);
        foreach (var currency in totals.GroupBy(t => t.Currency).OrderBy(g => g.Key == home ? 0 : 1))
        {
            if (currency.Key != home) rows.Children.Add(Ui.Text(string.Format(s["NotConvertedIn"], currency.Key), "caption").Margin(0, 6, 0, 0));
            var sum = currency.Sum(t => t.Amount);
            foreach (var t in currency)
            {
                var ratio = sum == 0 ? 0 : (double)(t.Amount / sum);
                var bar = Ui.ShareBar(ratio).Margin(0, 4, 0, 0);
                var amount = (t.Estimated ? "≈ " : "") + Formats.Money(t.Amount, t.Currency) + " · " + ratio.ToString("P0", System.Globalization.CultureInfo.CurrentCulture);
                rows.Children.Add(Ui.V(0, Ui.Columns("Auto,*,Auto", Ui.Icon(CategoryIcon(t.Category), 14, "Text.Muted").Margin(0, 0, 8, 0), Ui.Line(CategoryName(s, t.Category)), Ui.Text(amount, "strong")), bar));
            }
        }
        var heading = Ui.V(2, Ui.Text(s["ByCategory"], "h3"), Ui.Text(m.CategoryPeople.Count == 0 ? s["ByCategoryEveryone"] : string.Format(s["ByCategoryParts"], string.Join(", ", m.CategoryPeople.Select(p => trip.Find(p)?.Title).OfType<string>())), "caption"));
        var body = Ui.V(12, heading, whose, totals.Count == 0 ? Ui.Text(s["ByCategoryNone"], "caption") : rows);
        if (totals.Any(t => t.Estimated)) body.Children.Add(Notice("info", "Text.Subtle", m.EstimatedNote));
        return Ui.Card(body, 18);
    }

    /// <summary>
    /// In a booking's details: its payments, right there, each with its amount, what the card booked, who paid, who
    /// shares it and what is still open, and buttons to mark a planned one as paid or add another. A price an older
    /// version stored is shown apart until it is recorded as payments.
    /// </summary>
    public static Control BookingPayments(MainWindow w, Entity booking)
    {
        var m = w.Model; var s = m.Strings; var trip = m.Trip!;
        var payment = Expenses.Payment(trip, booking);
        var panel = Ui.V(8);
        if (payment.LegacyPrice is { } legacy)
        {
            var add = Ui.Button(s["RecordLegacyPrice"], "receipt", "soft"); add.Click += (_, _) => _ = m.AddPaymentForBookingAsync(booking.Id, planned: true);
            var drop = Ui.Button(s["DropLegacyPrice"], "x", "link"); drop.Click += (_, _) => _ = m.DropLegacyPriceAsync(booking.Id);
            panel.Children.Add(Ui.Card(Ui.V(6, Notice("info", "Text.Subtle", string.Format(s["LegacyPrice"], Formats.Money(legacy.Amount, legacy.Currency))), Ui.H(8, add, drop)), 10));
        }
        if (payment.Expenses.Count == 0 && payment.LegacyPrice is null) panel.Children.Add(Ui.Text(s["NoPaymentsYet"], "caption").Also(t => t.TextWrapping = TextWrapping.Wrap));
        foreach (var expense in payment.Expenses) panel.Children.Add(PaymentCard(w, expense));
        var home = m.TripCurrencyCode;
        if (payment.Expenses.Count > 1)
        {
            var paid = Expenses.Total(payment.Expenses, home, m.EstimateRates); var toPay = Expenses.Total(payment.Expenses, home, m.EstimateRates, planned: true);
            string Sum(ConvertedTotal t) => string.Join(" + ", new[] { t.Total != 0 ? (t.Estimated != 0 ? "≈ " : "") + Formats.Money(t.Total, home) : null }.Concat(t.NotConverted.Select(x => Formats.Money(x.Amount, x.Currency))).OfType<string>());
            var parts = new[] { Sum(paid) is { Length: > 0 } p ? string.Format(s["BookingPaidSum"], p) : null, Sum(toPay) is { Length: > 0 } q ? string.Format(s["StillToPayAmount"], q) : null }.OfType<string>().ToArray();
            if (parts.Length > 0) panel.Children.Add(Ui.Text(string.Join(" · ", parts), "caption"));
        }
        var addPaid = Ui.Button(s["AddPayment"], "plus", "soft"); addPaid.Click += (_, _) => _ = m.AddPaymentForBookingAsync(booking.Id);
        var addPlanned = Ui.Button(s["AddPlannedPayment"], "calendar-clock", "link"); addPlanned.Click += (_, _) => _ = m.AddPaymentForBookingAsync(booking.Id, planned: true);
        panel.Children.Add(Ui.H(8, addPaid, addPlanned));
        return panel;
    }

    /// <summary>One payment shown in place: amount and charged amount, state, who paid, who shares it, what is open.</summary>
    private static Control PaymentCard(MainWindow w, Entity expense)
    {
        var m = w.Model; var s = m.Strings; var trip = m.Trip!;
        var split = Expenses.Split(trip, expense);
        var money = split.Amount;
        var planned = Expenses.IsPlanned(expense);
        var amount = money is null ? "" : Formats.Money(money.Amount, money.Currency) + (Expenses.ChargeOf(expense) is { } charge ? " · " + string.Format(s["ChargedShort"], Formats.Money(charge.Value, charge.Currency))
            : CostTexts.Estimate(s, expense, m.TripCurrencyCode, m.EstimateRates) is { } estimate ? " · " + estimate : "");
        var title = new Button { Content = Ui.Line(expense.Title, "strong"), Padding = new Thickness(0) }.Classed("link");
        AutomationProperties.SetName(title, expense.Title); title.Click += (_, _) => m.Follow(expense);
        var lines = Ui.V(3, Ui.Columns("*,Auto,Auto", title, Status(s, split, trip)?.Margin(8, 0), Ui.Text(amount, "strong")));
        var payers = split.Payers.Select(p => Name(trip, p)).ToArray();
        if (payers.Length > 0) lines.Children.Add(Ui.Line(string.Format(s["PaidBy"], string.Join(", ", payers)), "caption"));
        if (money is not null && split.Shares.Count > 0) lines.Children.Add(Ui.Line(string.Format(s["SharedBy"], string.Join(", ", split.Shares.Select(x => Name(trip, x.Person) + " " + Formats.Money(x.Amount, money.Currency)))), "caption"));
        foreach (var debt in split.Debts.Where(d => !d.IsSettled))
        {
            var (from, to, open) = debt.Open < 0 ? (debt.To, debt.From, -debt.Open) : (debt.From, debt.To, debt.Open);
            lines.Children.Add(Ui.Line(string.Format(s["OwesTo"], Name(trip, from), Name(trip, to)) + " · " + Formats.Money(open, debt.Currency), "caption"));
        }
        if (planned)
        {
            if (Expenses.DueOf(expense) is { } due) lines.Children.Add(Ui.Line(string.Format(s["DueOn"], Formats.Date(due)), "caption"));
            var paid = Ui.Button(s["MarkPaymentPaid"], "circle-check", "soft").Also(b => b.HorizontalAlignment = HorizontalAlignment.Left);
            AutomationProperties.SetName(paid, string.Format(s["MarkPaymentPaidFor"], expense.Title));
            paid.Click += (_, _) => _ = m.MarkPaymentPaidAsync(expense.Id);
            lines.Children.Add(paid.Margin(0, 4, 0, 0));
        }
        return Ui.Card(lines, 10);
    }

    /// <summary>A pill for the bookings list: what is still to pay, or a price from an older version not recorded yet.</summary>
    public static Control? BookingPill(Localization s, TripSnapshot trip, IReadOnlyDictionary<Guid, BookingPayment> payments, Entity booking)
    {
        if (payments.GetValueOrDefault(booking.Id) is not { } payment) return null;
        // When nothing is paid yet, the amount beside it already says what is to pay.
        if (payment.Coverage == BookingCoverage.Planned && payment.Paid.Count == 0 && booking.Data["status"]?.ToString() is not ("idea" or "cancelled"))
            return Ui.Pill(s["ToPay"], "clock", "Warning.Soft", "Warning");
        if (payment.Coverage == BookingCoverage.Planned && booking.Data["status"]?.ToString() is not ("idea" or "cancelled"))
            return Ui.Pill(string.Format(s["ToPayAmount"], string.Join(" + ", payment.Planned.Select(e => TripSummaries.Money(e.Data["amount"])).OfType<Money>().GroupBy(x => x.Currency).Select(g => Formats.Money(g.Sum(x => x.Amount), g.Key)))), "clock", "Warning.Soft", "Warning");
        if (payment.LegacyPrice is not null && payment.Expenses.Count == 0) return Ui.Pill(s["PriceNotRecorded"], "info", "Bg.Subtle", "Text.Muted");
        return null;
    }

    /// <summary>What a booking costs for the bookings list: its payments per currency, or a price from an older version.</summary>
    public static string? BookingAmount(TripSnapshot trip, Entity booking) => CostTexts.BookingAmount(trip, booking);

    /// <summary>
    /// On the Costs page: payments still to pay, each with a button that marks it as paid, and bookings with a price
    /// from an older version that is not recorded yet. Null when there are none.
    /// </summary>
    public static Control? StillToPay(MainWindow w)
    {
        var m = w.Model; var s = m.Strings; var trip = m.Trip!;
        // Those due first come first.
        // Payments for ideas come last, marked, since they do not count.
        var planned = trip.Entities.Values.Where(e => e.Type == "expense" && Expenses.IsPlanned(e)).OrderBy(e => Expenses.IsIdea(trip, e)).ThenBy(e => Expenses.DueOf(e) ?? NodaTime.LocalDate.MaxIsoValue).ThenBy(e => e.Title, StringComparer.CurrentCulture).ToArray();
        var legacy = Expenses.WithoutExpense(trip);
        if (planned.Length == 0 && legacy.Count == 0) return null;
        var list = Ui.V(4);
        foreach (var e in planned)
        {
            var money = TripSummaries.Money(e.Data["amount"]);
            var paid = Ui.Button(s["MarkPaymentPaid"], "circle-check", "small");
            AutomationProperties.SetName(paid, string.Format(s["MarkPaymentPaidFor"], e.Title));
            paid.Click += (_, _) => _ = m.MarkPaymentPaidAsync(e.Id);
            var open = new Button { Content = Ui.Line(e.Title), Padding = new Thickness(0) }.Classed("link"); open.Click += (_, _) => m.Follow(e);
            var due = Expenses.IsIdea(trip, e) ? Ui.Line(s["IdeaPill"], "caption") : Expenses.DueOf(e) is { } day ? Ui.Line(string.Format(s["DueOn"], Formats.Date(day)), "caption") : null;
            list.Children.Add(Ui.Columns("*,Auto,Auto", Ui.V(0, open, due).Also(t => t.VerticalAlignment = VerticalAlignment.Center), Ui.Text(money is null ? "" : Formats.Money(money.Amount, money.Currency), "strong").Margin(12, 0).Also(t => t.VerticalAlignment = VerticalAlignment.Center), paid));
        }
        foreach (var booking in legacy)
        {
            var price = TripSummaries.Money(booking.Data["price"])!;
            var add = Ui.Button(s["RecordLegacyPriceShort"], "plus", "small");
            AutomationProperties.SetName(add, string.Format(s["RecordLegacyPriceFor"], booking.Title));
            add.Click += (_, _) => _ = m.AddPaymentForBookingAsync(booking.Id, planned: true);
            list.Children.Add(Ui.Columns("*,Auto,Auto", Ui.Line(booking.Title + " · " + s["PriceNotRecorded"]).Also(t => t.VerticalAlignment = VerticalAlignment.Center), Ui.Text(Formats.Money(price.Amount, price.Currency), "strong").Margin(12, 0).Also(t => t.VerticalAlignment = VerticalAlignment.Center), add));
        }
        var heading = Ui.V(2, Ui.Text(s["StillToPay"], "h3"), Ui.Text(s["StillToPayHint"], "caption").Also(t => t.TextWrapping = TextWrapping.Wrap));
        return Ui.Card(Ui.V(12, heading, list), 18);
    }

    private static readonly (string Scope, string Icon)[] BudgetScopes = [(Budgets.Trip, "luggage"), (Budgets.Category, "tag"), (Budgets.Participant, "user"), (Budgets.Collection, "layers")];

    /// <summary>What a budget covers, in words, such as "Food · Ben" or "Whole trip".</summary>
    public static string BudgetFor(Localization s, TripSnapshot trip, BudgetStatus status)
    {
        var f = status.Filters;
        if (f.IsWholeTrip) return s["BudgetWholeTrip"];
        return string.Join(" · ", new[] { f.Category is { } c ? CategoryName(s, c) : null, f.Person is { } p ? trip.Find(p)?.Title : null, f.Collection is { } k ? trip.Find(k)?.Title : null }.OfType<string>());
    }

    /// <summary>
    /// What a budget covers, as one row of chips per limit: a category, a person and, when the trip has collections,
    /// a collection. Each row starts with "All"; the limits chosen apply together, such as one person's food.
    /// </summary>
    public static Control BudgetScopeEditor(MainWindow w, Entity budget)
    {
        var m = w.Model; var s = m.Strings; var trip = m.Trip!;
        var panel = BudgetFiltersEditor(s, trip, Budgets.FiltersOf(budget), changed => _ = m.SetBudgetFiltersAsync(budget.Id, changed));
        if (Budgets.Status(trip, budget, m.EstimateRates) is { } status) panel.Children.Add(Ui.V(4, Ui.Text(BudgetFor(s, trip, status), "strong"), BudgetLine(s, trip, status, 300)));
        return panel;
    }

    /// <summary>
    /// What a budget applies to, as chips per category, person and collection, all applying together; used in the
    /// budget's details and when a budget is added. <paramref name="changed"/> gets the new limits.
    /// </summary>
    public static StackPanel BudgetFiltersEditor(Localization s, TripSnapshot trip, BudgetFilters filters, Action<BudgetFilters> changed)
    {
        Control Row(string label, string all, string? current, IEnumerable<(string Id, string Label, string Icon)> options, Func<string?, BudgetFilters> with)
        {
            var chips = new WrapPanel { ItemSpacing = 6, LineSpacing = 6 };
            foreach (var (id, text, icon) in options.Prepend((null!, all, "circle-dot")))
            {
                var selected = id is null ? current is null : string.Equals(id, current, StringComparison.CurrentCultureIgnoreCase);
                var chip = Ui.Choice(text, icon, selected, "chip");
                AutomationProperties.SetName(chip, label + ": " + text);
                chip.Click += (_, _) => { if (!selected) changed(with(id)); };
                chips.Children.Add(chip);
            }
            AutomationProperties.SetName(chips, label);
            return Ui.V(4, Ui.Text(label, "caption"), chips);
        }
        var panel = Ui.V(10,
            Row(s["category"], s["BudgetAny"], filters.Category, ExpenseCategories.Standard.Concat(ExpenseCategories.Custom(trip)).Select(c => (c, CategoryName(s, c), CategoryIcon(c))), c => filters with { Category = c }),
            Row(s["participant"], s["Everyone"], filters.Person?.ToString(), TripPeople.Travelers(trip).Select(p => (p.Id.ToString(), p.Title, "user")), p => filters with { Person = Guid.TryParse(p, out var id) ? id : null }));
        var collections = trip.Entities.Values.Where(e => e.Type == "collection" && !PackingLists.IsList(e)).OrderBy(e => e.Title, StringComparer.CurrentCulture).ToArray();
        if (collections.Length > 0)
            panel.Children.Add(Row(s["collection"], s["BudgetAny"], filters.Collection?.ToString(), collections.Select(c => (c.Id.ToString(), c.Title, "layers")), k => filters with { Collection = Guid.TryParse(k, out var id) ? id : null }));
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
        if (b.NotIncluded.Count > 0) line.Children.Add(Notice("info", "Text.Subtle", string.Format(s["BudgetNotIncluded"], string.Join(" + ", b.NotIncluded.GroupBy(x => x.Currency, StringComparer.Ordinal).Select(g => Formats.Money(g.Sum(x => x.Amount), g.Key))))));
        return line;
    }

    /// <summary>
    /// On the Costs page: the trip's one budget in the trip's currency, then budgets for a category, person or
    /// collection, each with what it covers and where it stands. Null without budgets.
    /// </summary>
    public static Control? BudgetsCard(MainWindow w)
    {
        var m = w.Model; var s = m.Strings; var trip = m.Trip!;
        var whole = Budgets.ForTrip(trip, m.EstimateRates);
        var limited = Budgets.Limited(trip, m.EstimateRates);
        if (whole is null && limited.Count == 0) return null;
        var rows = Ui.V(14);
        if (whole is not null)
        {
            var title = whole.Budgets.Count == 1 ? whole.Budgets[0].Title : s["TripBudget"];
            var open = new Button { Content = Ui.Columns("Auto,*", Ui.Icon("luggage", 14, "Text.Muted").Margin(0, 2, 8, 0).Also(i => i.VerticalAlignment = VerticalAlignment.Top), Ui.V(1, Ui.Line(title, "strong"), Ui.Line(s["BudgetWholeTrip"], "caption"))) }.Classed("row");
            AutomationProperties.SetName(open, title);
            var first = whole.Budgets[0]; open.Click += (_, _) => m.Follow(first);
            var status = new BudgetStatus(first, Budgets.Trip, null, new Money(Expenses.Format(whole.Amount, whole.Currency), whole.Currency), whole.Paid.Total, whole.Planned.Total, whole.Approximate, [.. whole.Paid.NotConverted, .. whole.Planned.NotConverted]);
            var line = BudgetLine(s, trip, status).Margin(8, 0);
            if (whole.Ideas is { } ideas && ideas.Total + ideas.NotConverted.Sum(x => x.Amount) != 0)
                ((StackPanel)line).Children.Add(Ui.Text(string.Format(s["IdeasNotCounted"], string.Join(" + ", new[] { ideas.Total != 0 ? (ideas.Estimated != 0 ? "≈ " : "") + Formats.Money(ideas.Total, ideas.Currency) : null }.Concat(ideas.NotConverted.Select(x => Formats.Money(x.Amount, x.Currency))).OfType<string>())), "caption"));
            if (whole.AmountNotConverted.Count > 0) ((StackPanel)line).Children.Add(Notice("info", "Text.Subtle", string.Format(s["BudgetAmountNotConverted"], string.Join(" + ", whole.AmountNotConverted.Select(x => Formats.Money(x.Amount, x.Currency))))));
            rows.Children.Add(Ui.V(6, open, line));
        }
        foreach (var b in limited)
        {
            var open = new Button { Content = Ui.Columns("Auto,*", Ui.Icon(BudgetScopes.First(x => x.Scope == b.Scope).Icon, 14, "Text.Muted").Margin(0, 2, 8, 0).Also(i => i.VerticalAlignment = VerticalAlignment.Top), Ui.V(1, Ui.Line(b.Budget.Title, "strong"), Ui.Line(BudgetFor(s, trip, b), "caption"))) }.Classed("row");
            AutomationProperties.SetName(open, b.Budget.Title);
            var budget = b.Budget; open.Click += (_, _) => m.Follow(budget);
            rows.Children.Add(Ui.V(6, open, BudgetLine(s, trip, b).Margin(8, 0)));
        }
        var body = Ui.V(12, Ui.Text(s["Budgets"], "h3"), rows);
        if ((whole?.Approximate ?? false) || limited.Any(b => b.Approximate)) body.Children.Add(Notice("info", "Text.Subtle", m.EstimatedNote));
        return Ui.Card(body, 18);
    }

    /// <summary>An icon beside a caption that wraps within its card instead of running past it.</summary>
    private static Control IconLine(Control icon, TextBlock text) =>
        Ui.Columns("Auto,*", icon.Margin(0, 2, 5, 0).Also(i => i.VerticalAlignment = VerticalAlignment.Top), text.Also(t => t.TextWrapping = TextWrapping.Wrap));

    /// <summary>
    /// The offer to bring costs older versions wrote up to date, for when it was postponed: the same as on the phone.
    /// Null when nothing needs it.
    /// </summary>
    public static Control? UpgradeBanner(MainWindow w)
    {
        var m = w.Model; var s = m.Strings;
        if (!m.CostUpgradePending) return null;
        var update = Ui.Button(s["UpgradeConfirm"], "refresh-cw", "soft").Also(b => b.HorizontalAlignment = HorizontalAlignment.Left);
        update.Click += (_, _) => _ = m.OfferCostUpgradeAsync(again: true);
        return Ui.Card(Ui.V(8, Ui.Text(s["UpgradeTitle"], "h3"), Ui.Text(s["UpgradeBody"].TrimEnd(':') + ".", "caption").Also(t => t.TextWrapping = TextWrapping.Wrap), update), 18);
    }

    private static Control Notice(string icon, string brush, string text) =>
        Ui.Columns("Auto,*", Ui.Icon(icon, 13, brush).Margin(0, 2, 6, 0).Also(i => i.VerticalAlignment = VerticalAlignment.Top), Ui.Text(text, "caption").Also(t => t.TextWrapping = TextWrapping.Wrap));
}
