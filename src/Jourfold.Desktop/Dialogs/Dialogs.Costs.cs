using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using TravelRepo.Core;

namespace Jourfold.Desktop;

public sealed partial class Dialogs
{
    /// <summary>
    /// The pay function. Every open item of the chosen person starts chosen; what the choice comes to is worked out by
    /// <see cref="Expenses.Pay"/> on every change, one payment per other person and currency.
    /// </summary>
    public async Task<PayAnswer?> PayAsync(PayRequest request)
    {
        var trip = request.Trip;
        string Name(Guid id) => (trip.Find(id)?.Title ?? "?") + (request.Me == id ? " (" + S["You"] + ")" : "");
        var person = request.Person;
        var chosen = new HashSet<DebtKey>();
        var items = Ui.V(2); var result = Ui.V(4); var checks = new List<CheckBox>();
        var ok = Ui.Button(S["PayConfirm"], "circle-check", "primary"); ok.IsDefault = true; var cancel = Action("Cancel");
        var heading = Ui.Text("", "muted").Also(t => t.TextWrapping = TextWrapping.Wrap);

        void Summarize()
        {
            result.Children.Clear();
            var plan = Expenses.Pay(trip, person, chosen, request.Currency, request.Rates);
            ok.IsEnabled = plan.Items.Count > 0;
            result.Children.Add(Ui.Text(S["PayResult"], "label"));
            if (plan.Items.Count == 0) { result.Children.Add(Ui.Text(S["PayNothing"], "caption")); return; }
            foreach (var p in plan.Payments)
                result.Children.Add(Ui.Columns("Auto,*,Auto", Ui.Icon("arrow-right", 13, "Text.Subtle").Margin(0, 0, 8, 0), Ui.Line(string.Format(S["PaysTo"], Name(p.From), Name(p.To))), Ui.Text((p.Estimated ? "≈ " : "") + Formats.Money(p.Amount, p.Currency), "strong")));
            if (plan.Payments.Any(p => p.Estimated)) result.Children.Add(Ui.Text(string.Format(S["EstimatedWithRates"], request.Rates is { } r ? Formats.Date(NodaTime.LocalDate.FromDateOnly(r.Date)) : ""), "caption").Also(t => t.TextWrapping = TextWrapping.Wrap));
            foreach (var (currency, total) in plan.Totals.OrderBy(t => t.Key, StringComparer.Ordinal))
                result.Children.Add(Ui.Text(total == 0 ? string.Format(S["PayEven"], Name(person)) : string.Format(S[total > 0 ? "PayTotalPays" : "PayTotalGets"], Name(person), Formats.Money(Math.Abs(total), currency)), "strong").Margin(0, 4, 0, 0));
            if (plan.Payments.Count == 0 && plan.Totals.Count == 0) result.Children.Add(Ui.Text(string.Format(S["PayEven"], Name(person)), "strong"));
        }

        void Fill()
        {
            items.Children.Clear(); checks.Clear(); chosen.Clear();
            var open = Expenses.Open(trip, person);
            heading.Text = open.Count == 0 ? string.Format(S["PayNone"], Name(person)) : S["PayBody"];
            foreach (var debt in open)
            {
                var (from, to, amount) = debt.Open < 0 ? (debt.To, debt.From, -debt.Open) : (debt.From, debt.To, debt.Open);
                var title = trip.Find(debt.Expense)?.Title ?? "";
                var line = string.Format(S["OwesTo"], Name(from), Name(to));
                // In the trip's currency like the result below; the amount as owed stays beside it.
                var shown = Formats.Money(amount, debt.Currency);
                if (request.Currency is { } target && trip.Find(debt.Expense) is { } expense && Expenses.Convert(expense, amount, debt.Currency, target, request.Rates) is { Currency: var code, How: var how } converted && code == target && how != Conversion.Same)
                { line += " · " + shown; shown = (how == Conversion.Reference ? "≈ " : "") + Formats.Money(converted.Amount, code); }
                var check = new CheckBox
                {
                    IsChecked = true,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    Margin = new Thickness(2, 2),
                    Content = Ui.Columns("*,Auto", Ui.V(1, Ui.Line(title), Ui.Line(line, "caption")), Ui.Text(shown, "strong").Margin(12, 0, 0, 0))
                };
                AutomationProperties.SetName(check, title + ", " + line + ", " + shown);
                chosen.Add(debt.Key);
                check.IsCheckedChanged += (_, _) => { if (check.IsChecked == true) chosen.Add(debt.Key); else chosen.Remove(debt.Key); Summarize(); };
                items.Children.Add(check); checks.Add(check);
            }
            Summarize();
        }

        Control? chooser = null;
        if (request.People.Count > 1)
        {
            var choices = request.People.Select(p => new Choice(p.Id.ToString(), Name(p.Id))).ToArray();
            var combo = new ComboBox { ItemsSource = choices, SelectedItem = choices.FirstOrDefault(c => c.Id == person.ToString()), MinWidth = 220 };
            AutomationProperties.SetName(combo, S["PayPerson"]);
            combo.SelectionChanged += (_, _) => { if (combo.SelectedItem is Choice c && Guid.TryParse(c.Id, out var id) && id != person) { person = id; Fill(); } };
            chooser = Ui.Field(S["PayPerson"], combo);
        }
        var all = Ui.Button(S["SelectAll"], "square-check", "link"); all.Click += (_, _) => { foreach (var c in checks) c.IsChecked = true; };
        var none = Ui.Button(S["SelectNone"], "square", "link"); none.Click += (_, _) => { foreach (var c in checks) c.IsChecked = false; };
        Fill();
        var sheet = new DialogSheet(S["PayTitle"], Ui.V(14, chooser, heading, Ui.H(12, all, none), new ScrollViewer { Content = items.Also(i => i.Margin = new Thickness(0, 0, 16, 0)), MaxHeight = 320 }, Ui.Card(result, 14), Footer(cancel, ok)), 600);
        ok.Click += (_, _) => sheet.Close(new PayAnswer(person, chosen.ToArray()));
        cancel.Click += (_, _) => sheet.Close(null);
        return await sheet.ShowDialog<PayAnswer?>(owner);
    }
}
