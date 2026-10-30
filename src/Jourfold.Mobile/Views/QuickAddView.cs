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
/// Quick Add behind the plus: a type, a title, the day, an optional time and place (start and end for a journey), and
/// for expenses the amount. "Only an idea" keeps a plan tentative. One Add button. The same form edits an item's
/// title, day, time, places and notes.
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
    private readonly AutoCompleteBox from = new() { FilterMode = AutoCompleteFilterMode.ContainsOrdinal, MinimumPrefixLength = 1 };
    // The places the edited item had, to change only those that the form changed.
    private string? placeBefore, fromBefore;
    private bool journey;
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
        place.ItemsSource = from.ItemsSource = trip.Entities.Values.Where(e => e.Type == "place").Select(p => p.Title).Order(StringComparer.CurrentCulture).ToArray();
        place.PlaceholderText = from.PlaceholderText = str["PlaceOptional"]; AutomationProperties.SetName(place, str["Place"]); AutomationProperties.SetName(from, str["From"]);
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
        // Only plan items have a day and a time; bookings, tasks and notes edit their title and text.
        kind = item.Type switch { "schedule_item" => "activity", "expense" => "expense", _ => "note" };
        day = entry?.Day ?? day;
        var zone = DateTimeZoneProviders.Tzdb.GetZoneOrNull(entry?.Zone ?? "") ?? ScheduleQueries.TripZone(s.Trip!);
        if (entry is { IsTimed: true, Start: { } a }) { start.SelectedTime = a.InZone(zone).TimeOfDay.ToTimeOnly().ToTimeSpan(); if (entry.End is { } b) end.SelectedTime = b.InZone(zone).TimeOfDay.ToTimeOnly().ToTimeSpan(); }
        idea.IsChecked = item.Data["status"]?.ToString() == "idea";
        if (item.Type == "schedule_item")
        {
            journey = item.Data["components"]?["transport"] is System.Text.Json.Nodes.JsonObject;
            place.Text = placeBefore = PlaceEdits.Current(s.Trip!, item, journey ? PlaceSlot.To : PlaceSlot.Place)?.Title ?? "";
            if (journey) from.Text = fromBefore = PlaceEdits.Current(s.Trip!, item, PlaceSlot.From)?.Title ?? "";
        }
        if (TripSummaries.Money(item.Data["amount"]) is { } money) { amount.Text = money.Amount.ToString(CultureInfo.InvariantCulture); currency = money.Currency; }
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
        if (Scheduled) form.Children.Add(Field(str["DayLabel"], Days()));
        if (Scheduled)
        {
            form.Children.Add(Field(kind == "accommodation" ? str["CheckIn"] : str["StartTime"], start));
            if (kind != "accommodation") form.Children.Add(Field(str["EndTime"], end));
            if (journey || kind == "transport") { form.Children.Add(Field(str["From"], from)); form.Children.Add(Field(str["To"], place)); }
            else form.Children.Add(Field(str["Place"], place));
            if (kind != "idea") form.Children.Add(idea);
        }
        if (kind == "expense")
        {
            var currencies = Ui.Flow(8);
            // An expense keeps its currency once it exists.
            foreach (var c in editing is not null ? [currency] : TripSummaries.Costs(s.Trip!).Select(t => t.Currency).Append("EUR").Distinct())
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

    private static bool Changed(string? text, string? before) => !string.Equals(text?.Trim() ?? "", before ?? "", StringComparison.CurrentCulture);

    /// <summary>A place of the trip with the typed name, a new place with it, or none for an empty field.</summary>
    private PlaceChoice? Choice(string? text)
    {
        text = text?.Trim();
        if (string.IsNullOrEmpty(text)) return null;
        return s.Trip!.Entities.Values.FirstOrDefault(p => p.Type == "place" && string.Equals(p.Title, text, StringComparison.CurrentCultureIgnoreCase)) is { } known ? PlaceChoice.Existing(known.Id) : PlaceChoice.Create(text);
    }

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
            LocalTime? startTime = start.SelectedTime is { } t ? LocalTime.FromTimeOnly(TimeOnly.FromTimeSpan(t)) : null;
            LocalTime? endTime = end.SelectedTime is { } e ? LocalTime.FromTimeOnly(TimeOnly.FromTimeSpan(e)) : null;
            Duration? length = startTime is { } a && endTime is { } b ? (b > a ? b - a : b - a + Period.FromDays(1)).ToDuration() : null;
            var placeChoice = Choice(place.Text); var fromChoice = Choice(from.Text);
            decimal? money = null;
            if (kind == "expense")
            {
                if (!decimal.TryParse(amount.Text?.Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out var value)) throw new DomainException("mobile.amount", s.Strings["EnterAmount"]);
                money = value;
            }
            if (editing is not null)
            {
                await s.SaveEditAsync(editing.Id, title.Text!, body.Text, Scheduled ? day : null, Scheduled ? startTime : null, length, money);
                // Only places the form changed are written, so an inherited place stays inherited.
                if (editing.Type == "schedule_item" && Changed(place.Text, placeBefore)) await s.AssignPlaceAsync(editing.Id, journey ? PlaceSlot.To : PlaceSlot.Place, placeChoice);
                if (journey && Changed(from.Text, fromBefore)) await s.AssignPlaceAsync(editing.Id, PlaceSlot.From, fromChoice);
                return;
            }
            var draft = new QuickAddDraft(kind == "idea" ? "activity" : kind, title.Text!)
            {
                Date = Scheduled ? day : null,
                Start = startTime,
                Length = length,
                Place = kind == "transport" ? null : placeChoice,
                From = kind == "transport" ? fromChoice : null,
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
