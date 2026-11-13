using System.Globalization;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using TravelRepo.Core;

namespace Jourfold.UI;

/// <summary>
/// Works out the rate of a cash withdrawal from what was received and what the account was charged for it, fees
/// included, and how far that is from the reference rate. The computer shows it in a flyout next to a cash rate and
/// on the currency converter; phones show it below the cash rate in the expense form and on the converter page.
/// Two withdrawals at different rates, or cash bought through a third currency, are beyond it.
/// </summary>
public static class RateCalculator
{
    /// <param name="cash">The currency received, such as NZD.</param>
    /// <param name="account">The currency the account was charged in, the trip's.</param>
    /// <param name="rates">Reference rates to compare with, if any.</param>
    /// <param name="use">Takes the rate (units of <paramref name="account"/> per unit of <paramref name="cash"/>); without it the calculator only checks the fees.</param>
    /// <param name="titled">Whether it names itself; off where a button or header above it already does.</param>
    public static Control Build(Localization s, string cash, string account, ExchangeRates? rates, Action<decimal>? use = null, bool titled = true)
    {
        TextBox Box(string name) => new TextBox { Width = 120, HorizontalContentAlignment = HorizontalAlignment.Right }.Also(b =>
        {
            AutomationProperties.SetName(b, name);
            Avalonia.Input.TextInput.TextInputOptions.SetContentType(b, Avalonia.Input.TextInput.TextInputContentType.Number);
        });
        var received = Box(s["CashReceived"] + " (" + cash + ")"); var debited = Box(s["AccountDebited"] + " (" + account + ")");
        var rate = Ui.Text("–", "strong"); var markup = Ui.Text("", "caption").Also(t => t.TextWrapping = TextWrapping.Wrap);
        var take = use is null ? null : Ui.Button(s["UseRate"], "check", "primary").Also(b => { b.IsEnabled = false; b.HorizontalAlignment = HorizontalAlignment.Left; });
        decimal? Current() => CostTexts.WithdrawalRate(ExchangeRates.Amount(received.Text), ExchangeRates.Amount(debited.Text));
        void Show()
        {
            var value = Current();
            rate.Text = value is { } r ? CostTexts.CashRateForm.For(cash, account, r).Text(r) : "–";
            markup.Text = value is { } v ? CostTexts.MarkupText(s, v, rates, cash, account) ?? "" : "";
            markup.IsVisible = markup.Text.Length > 0;
            if (take is not null) take.IsEnabled = value is not null;
        }
        received.TextChanged += (_, _) => Show(); debited.TextChanged += (_, _) => Show();
        if (take is not null) take.Click += (_, _) => { if (Current() is { } r) use!(r); };
        Control Line(string label, TextBox box, string code) => Ui.V(4, Ui.Text(label, "caption"), Ui.H(6, box, Ui.Text(code, "caption").Also(t => t.VerticalAlignment = VerticalAlignment.Center)));
        Show();
        return Ui.V(10, titled ? Ui.Text(use is null ? s["CheckFees"] : s["RateCalculator"], "strong") : null,
            use is null ? Ui.Text(s["CheckFeesHint"], "caption").Also(t => t.TextWrapping = TextWrapping.Wrap) : null,
            Line(s["CashReceived"], received, cash), Line(s["AccountDebited"], debited, account),
            Ui.V(2, rate, markup), take).Also(v => v.MinWidth = 260);
    }
}
