using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Jourfold.Application;
using NodaTime;
using TravelRepo.Core;

namespace Jourfold.Mobile.Views;

/// <summary>
/// Quick Add behind the plus: a type, a title, the day, an optional time and place, and for expenses the amount.
/// "Only an idea" keeps a plan tentative. One Add button. The same form edits an item's title, day, time and notes.
/// </summary>
public sealed class QuickAddView
{
    private static readonly (string Kind, string Icon, string Label)[] Kinds =
        [("activity", "star", "quick.activity"), ("food", "utensils", "quick.food"), ("transport", "train-front", "quick.transport"), ("accommodation", "bed-double", "quick.accommodation"),
         ("idea", "lightbulb", "idea"), ("note", "sticky-note", "quick.note"), ("expense", "receipt", "quick.expense")];

    private readonly MobileSession s;
    private readonly Entity? editing;
    private string kind = "activity";
    private LocalDate? day;
    private readonly TextBox title = new() { FontSize = 18 };
    private readonly TimePicker start = new() { ClockIdentifier = "24HourClock", MinuteIncrement = 5, HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly TimePicker end = new() { ClockIdentifier = "24HourClock", MinuteIncrement = 5, HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly AutoCompleteBox place = new() { FilterMode = AutoCompleteFilterMode.ContainsOrdinal, MinimumPrefixLength = 1 };
    private readonly ToggleSwitch idea = new();
    private readonly TextBox body = new() { AcceptsReturn = true, TextWrapping = Avalonia.Media.TextWrapping.Wrap, MinHeight = 96 };
    private readonly TextBox amount = new();
    private string currency;
    private readonly TextBlock problem = Ui.Text("", "caption").Also(t => { t.IsVisible = false; t.Res(TextBlock.ForegroundProperty, "Danger"); });
    private readonly StackPanel form = Ui.V(14);

    public Control View { get; }
    public Control Header { get; }

    public QuickAddView(MobileSession session, Entity? item = null)
    {
        s = session; editing = item; var str = s.Strings;
        var trip = s.Trip!;
        currency = TripSummaries.Costs(trip).FirstOrDefault()?.Currency ?? "EUR";
        title.PlaceholderText = str["QuickAddTitleHint"]; AutomationProperties.SetName(title, str["Title"]);
        AutomationProperties.SetName(start, str["StartTime"]); AutomationProperties.SetName(end, str["EndTime"]);
        place.ItemsSource = trip.Entities.Values.Where(e => e.Type == "place").Select(p => p.Title).Order(StringComparer.CurrentCulture).ToArray();
        place.PlaceholderText = str["PlaceOptional"]; AutomationProperties.SetName(place, str["Place"]);
        body.PlaceholderText = str["Notes"]; AutomationProperties.SetName(body, str["Notes"]);
        amount.PlaceholderText = "0.00"; AutomationProperties.SetName(amount, str["Amount"]);
        idea.OnContent = idea.OffContent = str["OnlyAnIdea"];
        day = s.Agenda.Find(s.SelectedDate) is not null ? s.SelectedDate : s.Agenda.Find(s.PhoneToday) is not null ? s.PhoneToday : s.Agenda.Days.FirstOrDefault()?.Date;
        if (item is not null) Load(item);

        var close = Ui.IconButton("x", str["Close"]); close.Click += (_, _) => s.CloseOverlay();
        Header = Ui.Columns("*,Auto", Ui.Text(item is null ? str["QuickAdd"] : str["Edit"], "h3"), close).Margin(16, 0, 8, 8);
        var add = Ui.Button(item is null ? str["Add"] : str["Save"], item is null ? "plus" : "check", "primary").Also(b => b.HorizontalAlignment = HorizontalAlignment.Stretch);
        add.Click += async (_, _) => await SubmitAsync(add);
        View = Ui.V(14, form, problem, add).Margin(16, 0, 16, 24);
        Render();
        title.AttachedToVisualTree += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(() => title.Focus(), Avalonia.Threading.DispatcherPriority.Background);
    }

    private void Load(Entity item)
    {
        var entry = s.Entry(item.Id);
        title.Text = item.Title; body.Text = item.Data["body"]?.ToString();
        kind = item.Type == "note" ? "note" : item.Type == "expense" ? "expense" : "activity";
        day = entry?.Day ?? day;
        var zone = DateTimeZoneProviders.Tzdb.GetZoneOrNull(entry?.Zone ?? "") ?? ScheduleQueries.TripZone(s.Trip!);
        if (entry is { IsTimed: true, Start: { } a }) { start.SelectedTime = a.InZone(zone).TimeOfDay.ToTimeOnly().ToTimeSpan(); if (entry.End is { } b) end.SelectedTime = b.InZone(zone).TimeOfDay.ToTimeOnly().ToTimeSpan(); }
        idea.IsChecked = item.Data["status"]?.ToString() == "idea";
    }

    private bool Scheduled => kind is "activity" or "food" or "transport" or "accommodation" or "idea";

    private void Render()
    {
        var str = s.Strings; form.Children.Clear();
        if (editing is null)
        {
            var kinds = Ui.Flow(8);
            foreach (var (k, icon, label) in Kinds)
            {
                var chip = Ui.Choice(str[label], icon, k == kind, "chip"); var chosen = k;
                chip.Click += (_, _) => { kind = chosen; Render(); };
                kinds.Children.Add(chip);
            }
            form.Children.Add(kinds);
        }
        form.Children.Add(title);
        if (kind == "note") { form.Children.Add(body); return; }
        if (Scheduled || kind == "expense") form.Children.Add(Field(kind == "expense" ? str["Date"] : str["DayLabel"], Days()));
        if (Scheduled)
        {
            form.Children.Add(Field(kind == "accommodation" ? str["CheckIn"] : str["StartTime"], start));
            if (kind != "accommodation") form.Children.Add(Field(str["EndTime"], end));
            form.Children.Add(Field(str["Place"], place));
            if (kind != "idea") form.Children.Add(idea);
        }
        if (kind == "expense")
        {
            var currencies = Ui.Flow(8);
            foreach (var c in TripSummaries.Costs(s.Trip!).Select(t => t.Currency).Append("EUR").Distinct())
            {
                var chip = Ui.Choice(c, null, c == currency, "chip"); var chosen = c;
                chip.Click += (_, _) => { currency = chosen; Render(); };
                currencies.Children.Add(chip);
            }
            form.Children.Add(Field(str["Amount"], Ui.V(8, amount, currencies)));
        }
        if (editing is not null) form.Children.Add(Field(str["Notes"], body));
    }

    private static Control Field(string label, Control value) => Ui.V(6, Ui.Text(label, "overline"), value);

    private Control Days()
    {
        var culture = CultureInfo.GetCultureInfo(s.Strings.Language);
        var chips = Ui.H(6);
        foreach (var d in s.Agenda.Days)
        {
            var chip = Ui.Choice(d.Date.ToString("ddd d", culture), null, d.Date == day, "chip"); var chosen = d.Date;
            chip.Click += (_, _) => { day = day == chosen ? null : chosen; Render(); };
            chips.Children.Add(chip);
        }
        return new ScrollViewer { Content = chips, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
    }

    private async Task SubmitAsync(Button add)
    {
        problem.IsVisible = false; add.IsEnabled = false;
        try
        {
            if (string.IsNullOrWhiteSpace(title.Text)) throw new DomainException("mobile.title", s.Strings["EnterTitle"]);
            LocalTime? from = start.SelectedTime is { } t ? LocalTime.FromTimeOnly(TimeOnly.FromTimeSpan(t)) : null;
            LocalTime? to = end.SelectedTime is { } e ? LocalTime.FromTimeOnly(TimeOnly.FromTimeSpan(e)) : null;
            Duration? length = from is { } a && to is { } b ? (b > a ? b - a : b - a + Period.FromDays(1)).ToDuration() : null;
            var placeText = place.Text?.Trim();
            var placeChoice = string.IsNullOrEmpty(placeText) ? null
                : s.Trip!.Entities.Values.FirstOrDefault(p => p.Type == "place" && string.Equals(p.Title, placeText, StringComparison.CurrentCultureIgnoreCase)) is { } known ? PlaceChoice.Existing(known.Id) : PlaceChoice.Create(placeText);
            if (editing is not null) { await s.SaveEditAsync(editing.Id, title.Text!, body.Text, day, from, length); return; }
            decimal? money = null;
            if (kind == "expense")
            {
                if (!decimal.TryParse(amount.Text?.Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out var value)) throw new DomainException("mobile.amount", s.Strings["EnterAmount"]);
                money = value;
            }
            var draft = new QuickAddDraft(kind == "idea" ? "activity" : kind, title.Text!)
            {
                Date = kind == "note" ? null : day,
                Start = from,
                Length = length,
                Place = kind == "transport" ? null : placeChoice,
                To = kind == "transport" ? placeChoice : null,
                Body = kind == "note" ? body.Text : null,
                Amount = money,
                Currency = kind == "expense" ? currency : null
            };
            await s.AddAsync(draft, idea: kind == "idea" || (Scheduled && idea.IsChecked == true));
        }
        // The form's own checks carry translated text; anything from the domain is looked up by its code.
        catch (DomainException ex) { problem.Text = ex.Code is "mobile.title" or "mobile.amount" ? ex.Message : s.Strings.Diagnostic(ex.Code); problem.IsVisible = true; }
        finally { add.IsEnabled = true; }
    }
}
