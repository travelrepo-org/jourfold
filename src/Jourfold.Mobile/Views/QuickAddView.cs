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
/// title, day, time, places and notes, and everything needed on the road without a computer: a journey's arrival in
/// its own zone, number, terminal, gate and seat; a stay's nights, hours and access; the booking number; who a plan is
/// for; a booking's provider, status, price, free cancellation and plans; who paid an expense and what it belongs to.
/// </summary>
public sealed class QuickAddView
{
    private static readonly (string Kind, string Icon, string Label)[] Kinds =
        [("activity", "star", "quick.activity"), ("food", "utensils", "quick.food"), ("transport", "train-front", "quick.transport"), ("accommodation", "bed-double", "quick.accommodation"),
         ("group", "layers", "quick.group"), ("idea", "lightbulb", "idea"), ("task", "square-check", "quick.task"), ("note", "sticky-note", "quick.note"),
         ("expense", "receipt", "quick.expense"), ("booking", "ticket", "quick.booking")];

    private static readonly (string Status, string Icon)[] BookingStatuses =
        [("idea", "lightbulb"), ("pending", "clock"), ("reserved", "bookmark"), ("confirmed", "check"), ("completed", "circle-check"), ("cancelled", "x")];

    private readonly MobileSession s;
    private readonly Entity? editing;
    // The group a new plan becomes a step of.
    private readonly Guid? into;
    private string kind = "activity";
    private LocalDate? day;
    private readonly TextBox title = new() { FontSize = 18 };
    private readonly TimePicker start = new() { ClockIdentifier = "24HourClock", MinuteIncrement = 5, HorizontalAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Center };
    private readonly TimePicker end = new() { ClockIdentifier = "24HourClock", MinuteIncrement = 5, HorizontalAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Center };
    private readonly AutoCompleteBox place = new() { FilterMode = AutoCompleteFilterMode.ContainsOrdinal, MinimumPrefixLength = 1 };
    private readonly AutoCompleteBox from = new() { FilterMode = AutoCompleteFilterMode.ContainsOrdinal, MinimumPrefixLength = 1 };
    // The places the edited item had, to change only those that the form changed.
    private string? placeBefore, fromBefore;
    private bool journey;
    private readonly ToggleSwitch idea = new();
    private readonly TextBox body = new() { AcceptsReturn = true, TextWrapping = Avalonia.Media.TextWrapping.Wrap, MinHeight = 96 };
    private readonly TextBox amount = new();
    // The amount an edited item had, as it was put into the field.
    private (string Text, decimal Value)? amountBefore;
    private string currency;
    // A journey's arrival: the time, how many days after the departure day, and the zone it is read in.
    private readonly TimePicker arrival = new() { ClockIdentifier = "24HourClock", MinuteIncrement = 5, HorizontalAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Center };
    private int arrivalDays;
    private string? arrivalZone;
    // A stay's nights and hours.
    private int nights = 1;
    private readonly TimePicker checkOut = Clock(), windowFrom = Clock(), windowUntil = Clock(), latestOut = Clock();
    // Text details by path, with what they were, to write only what changed.
    private readonly Dictionary<string, (TextBox Box, string? Before)> details = [];
    private readonly TextBox reference = new();
    // A receipt photographed for a new expense, attached once it is added.
    private SharedFile? receipt;
    private string? referenceBefore;
    // Who the item is for, paid it or does it, with how it was, to write only a change.
    private PeopleChoice peopleChoice = PeopleChoice.Everyone, peopleChoiceBefore = PeopleChoice.Everyone;
    private readonly HashSet<Guid> people = [];
    private Guid[] peopleBefore = [];
    // A booking's provider, status, free cancellation and plans.
    private readonly TextBox provider = new();
    private string bookingStatus = "pending";
    private readonly DatePicker cancelDay = new() { HorizontalAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Center };
    private readonly TimePicker cancelTime = Clock();
    private readonly HashSet<Guid> plans = [];
    private Guid[] plansBefore = [];
    // An expense: still estimated, and the plan or booking it belongs to.
    private readonly ToggleSwitch estimated = new();
    private Guid? related, relatedBefore;
    // An expense's category, who shares it and what the card booked in the trip's currency, with how they were.
    private string? category, categoryBefore;
    private readonly HashSet<Guid> sharing = [];
    private Guid[] sharingBefore = [];
    private bool sharingTouched;
    private SplitMethod? splitMethod = SplitMethod.Equal;
    private readonly TextBox charged = new();
    private string? chargedBefore;
    // The currency of a charge already entered, which can differ from the trip's (entered earlier or elsewhere); it is kept.
    private string? chargedCurrency;
    // Paid with cash bought before instead of a card: then the rate the cash cost is asked for, not what was charged.
    private bool cash, cashBefore;
    private readonly TextBox cashRate = new() { Width = 110, HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Right };
    private string? cashRateBefore;
    // The rate filled in for the user and where it came from, said until they change it; and the calculator below it.
    private string? cashOffered, cashOfferSource;
    private bool calculating;
    // When a payment still to pay is due.
    private readonly DatePicker dueDate = new() { HorizontalAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Center };
    private LocalDate? dueBefore;
    private string? providerBefore;
    // An expense split by amounts, shares or percent: each person's value as typed, whether the split was changed, and
    // what is still to divide.
    private readonly Dictionary<Guid, TextBox> splitBoxes = [];
    private bool splitTouched;
    private readonly TextBlock splitState = Ui.Text("", "caption").Also(t => t.TextWrapping = Avalonia.Media.TextWrapping.Wrap);
    // The day a paid expense was paid.
    private readonly DatePicker paidOn = new() { HorizontalAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Center };
    private LocalDate? paidOnBefore;
    // A category of the travellers' own, typed in the form.
    private readonly TextBox newCategory = new();
    private bool addingCategory;
    // A budget: what it applies to.
    private BudgetFilters budgetFilters = BudgetFilters.None, budgetFiltersBefore = BudgetFilters.None;
    private Button? addButton;
    // A currency not offered as a chip, such as the one of a country just arrived in.
    // Typing suggests codes as on the converter; choosing one, or Enter after a code, makes it the expense's currency.
    private readonly AutoCompleteBox otherCurrency = new() { MaxLength = 3, Width = 110, HorizontalAlignment = HorizontalAlignment.Left, MinimumPrefixLength = 1, FilterMode = AutoCompleteFilterMode.StartsWith };
    private readonly TextBlock problem = Ui.Text("", "caption").Also(t => { t.IsVisible = false; t.Res(TextBlock.ForegroundProperty, "Danger"); });
    private readonly StackPanel form = Ui.V(14);
    // The kinds and the title stay in place while the rest of the form is drawn again, so the title keeps the focus
    // and the keyboard stays open when the kind changes.
    private readonly WrapPanel kinds = Ui.Flow(8);
    private readonly StackPanel rest = Ui.V(14);

    public Control View { get; }
    public Control Header { get; }

    public QuickAddView(MobileSession session, Entity? item = null)
    {
        s = session; editing = item; var str = s.Strings;
        var trip = s.Trip!;
        if (item is null) { into = s.QuickAddInto; if (s.QuickAddKind is { } preset && (Kinds.Any(k => k.Kind == preset) || preset == "budget")) kind = preset; }
        // A budget starts in the trip's currency, an expense in the one most paid in.
        currency = kind == "budget" ? s.TripCurrencyCode : TripSummaries.Costs(trip).FirstOrDefault()?.Currency ?? "EUR";
        title.PlaceholderText = str["QuickAddTitleHint"]; AutomationProperties.SetName(title, str["Title"]);
        AutomationProperties.SetName(start, str["StartTime"]); AutomationProperties.SetName(end, str["EndTime"]);
        place.ItemsSource = from.ItemsSource = trip.Entities.Values.Where(e => e.Type == "place").Select(p => p.Title).Order(StringComparer.CurrentCulture).ToArray();
        place.PlaceholderText = from.PlaceholderText = str["PlaceOptional"]; AutomationProperties.SetName(place, str["Place"]); AutomationProperties.SetName(from, str["From"]);
        body.PlaceholderText = str["Notes"]; AutomationProperties.SetName(body, str["Notes"]);
        // With the decimal separator that amounts are shown with (Formats.Money).
        amount.PlaceholderText = 0m.ToString("0.00", CultureInfo.CurrentCulture); AutomationProperties.SetName(amount, str["Amount"]);
        // Phones show their keyboard for numbers, with the decimal separator.
        Avalonia.Input.TextInput.TextInputOptions.SetContentType(amount, Avalonia.Input.TextInput.TextInputContentType.Number);
        idea.OnContent = idea.OffContent = str["OnlyAnIdea"];
        estimated.OnContent = estimated.OffContent = str["StillToPay"]; AutomationProperties.SetName(estimated, str["StillToPay"]);
        // Still to pay offers a due day, so the form is drawn again when it changes.
        estimated.IsCheckedChanged += (_, _) => { if (kind == "expense") Avalonia.Threading.Dispatcher.UIThread.Post(() => Render()); };
        charged.PlaceholderText = 0m.ToString("0.00", CultureInfo.CurrentCulture);
        AutomationProperties.SetName(paidOn, str["PaidOnLabel"]);
        // What the card charged opens the number pad like the amount, with the decimal separator and sign.
        Avalonia.Input.TextInput.TextInputOptions.SetContentType(charged, Avalonia.Input.TextInput.TextInputContentType.Number);
        Avalonia.Input.TextInput.TextInputOptions.SetContentType(cashRate, Avalonia.Input.TextInput.TextInputContentType.Number);
        newCategory.PlaceholderText = str["NewCategory"]; AutomationProperties.SetName(newCategory, str["NewCategory"]);
        newCategory.KeyDown += (_, e) => { if (e.Key == Avalonia.Input.Key.Enter) { e.Handled = true; TakeNewCategory(); } };
        // The amount decides what an amount split has to add up to.
        amount.TextChanged += (_, _) => UpdateSplitState();
        if (item is null) paidOn.SelectedDate = new DateTimeOffset(s.PhoneToday.ToDateTimeUnspecified(), TimeSpan.Zero);
        provider.PlaceholderText = str["ProviderHint"]; AutomationProperties.SetName(provider, str["Provider"]);
        reference.PlaceholderText = str["BookingNumberHint"]; AutomationProperties.SetName(reference, str["BookingNumber"]);
        otherCurrency.PlaceholderText = str["OtherCurrencyHint"]; AutomationProperties.SetName(otherCurrency, str["OtherCurrency"]);
        otherCurrency.ItemsSource = Jourfold.UI.CurrencyCodes.Known(s.Rates);
        otherCurrency.SelectionChanged += (_, _) => { if (otherCurrency.SelectedItem is string code) ChooseCurrency(code); };
        // Enter takes a typed code even when no rates know it; the list may not be open.
        otherCurrency.AddHandler(Avalonia.Input.InputElement.KeyDownEvent, (_, e) =>
        {
            if (e.Key != Avalonia.Input.Key.Enter || Jourfold.UI.CurrencyCodes.Read(otherCurrency.Text) is not { } code) return;
            e.Handled = true; ChooseCurrency(code);
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        otherCurrency.LostFocus += (_, _) => { if (Jourfold.UI.CurrencyCodes.Read(otherCurrency.Text) is { } code && code != currency) ChooseCurrency(code); };
        AutomationProperties.SetName(cancelDay, str["FreeCancellationUntil"]); AutomationProperties.SetName(cancelTime, str["FreeCancellationUntil"]);
        if (item is null)
        {
            // A new step is for the group's people; a new expense was paid by the user.
            if (into is not null) peopleChoice = PeopleChoice.AsGroup;
            else if (s.Me is { } me && kind is "expense") { peopleChoice = PeopleChoice.Chosen; people.Add(me.Id); }
            if (into is not null && trip.Find(into.Value) is { } group && s.Entry(group.Id)?.Day is { } groupDay) day = groupDay;
            // A payment for a booking: it belongs to the booking, and one still to pay names no payer yet.
            if (s.QuickAddRelated is { } forBooking && trip.Find(forBooking) is { } booking)
            {
                related = forBooking; estimated.IsChecked = s.QuickAddPlanned; title.Text = booking.Title;
                if (s.QuickAddPlanned) people.Clear();
                if (TripSummaries.Money(booking.Data["price"]) is { } legacy) { amount.Text = legacy.Amount.ToString(CultureInfo.CurrentCulture); currency = legacy.Currency; }
            }
            s.ClearQuickAddPayment();
        }
        day = s.Agenda.Find(s.SelectedDate) is not null ? s.SelectedDate : s.Agenda.Find(s.PhoneToday) is not null ? s.PhoneToday : s.Agenda.Days.FirstOrDefault()?.Date;
        if (item is not null) Load(item);

        var close = Ui.IconButton("x", str["Close"]); close.Click += (_, _) => s.CloseOverlay();
        var heading = item is not null ? str["Edit"] : kind == "budget" ? str["AddBudget"] : into is { } g && trip.Find(g) is { } groupItem ? string.Format(CultureInfo.CurrentCulture, str["NewStepIn"], groupItem.Title) : str["QuickAdd"];
        Header = Ui.Columns("*,Auto", Ui.Text(heading, "h3").Also(t => t.TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis), close).Margin(16, 0, 8, 8);
        var add = Ui.Button(item is null ? str["Add"] : str["Save"], item is null ? "plus" : "check", "primary").Also(b => b.HorizontalAlignment = HorizontalAlignment.Stretch);
        add.Click += async (_, _) => await SubmitAsync(add);
        addButton = add;
        View = Ui.V(14, form, problem, add).Margin(16, 0, 16, 24);
        Render();
        // A new item starts with its title; an item being edited opens without the keyboard, so the form can be seen.
        if (item is null) title.AttachedToVisualTree += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(() => title.Focus(), Avalonia.Threading.DispatcherPriority.Background);
    }

    private static TimePicker Clock() => new() { ClockIdentifier = "24HourClock", MinuteIncrement = 5, HorizontalAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Center };

    private static TimeSpan? Span(LocalTime? time) => time?.ToTimeOnly().ToTimeSpan();
    private static LocalTime? Time(TimePicker picker) => picker.SelectedTime is { } t ? LocalTime.FromTimeOnly(TimeOnly.FromTimeSpan(t)) : null;

    private void Load(Entity item)
    {
        var entry = s.Entry(item.Id);
        title.Text = item.Title; body.Text = item.Data["body"]?.ToString();
        // Only plan items have a day and a time; bookings, tasks and notes edit their title and text.
        kind = item.Type switch { "schedule_item" => Stays.IsStay(item) ? "accommodation" : "activity", "expense" => "expense", "budget" => "budget", "task" => "task", "booking" => "booking", _ => "note" };
        if (item.Type == "budget") budgetFilters = budgetFiltersBefore = Budgets.FiltersOf(item);
        LoadPeople(item);
        if (item.Type == "task" && item.Data["due"]?["date"]?.ToString() is { } due
            && NodaTime.Text.LocalDatePattern.Iso.Parse(due) is { Success: true } parsed) day = parsed.Value;
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
        if (TripSummaries.Money(item.Data[item.Type == "booking" ? "price" : "amount"]) is { } money) { amount.Text = money.Amount.ToString(CultureInfo.InvariantCulture); amountBefore = (amount.Text, money.Amount); currency = money.Currency; }
        if (item.Type == "booking") LoadBooking(item);
        if (item.Type == "expense")
        {
            var trip = s.Trip!;
            category = categoryBefore = ExpenseCategories.Stored(item);
            var split = Expenses.Split(trip, item);
            splitMethod = split.Method; sharing.UnionWith(split.People); sharingBefore = [.. split.People]; sharingTouched = true;
            // Amounts, weights or percentages as stored, to change them here.
            foreach (var part in (item.Data["split"]?["parts"] as System.Text.Json.Nodes.JsonArray ?? []).OfType<System.Text.Json.Nodes.JsonObject>())
                if (Guid.TryParse(part["person"]?.ToString(), out var who) && ExchangeRates.Amount(part["value"]?.ToString()) is { } value) SplitBox(who).Text = ValueText(value);
            if (item.Data["date"] is not null && (paidOnBefore = Expenses.DateOf(trip, item)) is { } paidDay) paidOn.SelectedDate = new DateTimeOffset(paidDay.ToDateTimeUnspecified(), TimeSpan.Zero);
            if (Expenses.ChargeOf(item) is { } charge && charge.Source == Expenses.Cash)
            {
                cash = cashBefore = true; chargedCurrency = charge.Currency;
                cashRate.Text = cashRateBefore = CashForm().Shown(charge.Rate).ToString("0.####", CultureInfo.CurrentCulture);
            }
            else if (Expenses.ChargeOf(item) is { } card) { charged.Text = chargedBefore = card.Value.ToString(CultureInfo.CurrentCulture); chargedCurrency = card.Currency; }
            if ((dueBefore = Expenses.DueOf(item)) is { } dueDay) dueDate.SelectedDate = new DateTimeOffset(dueDay.ToDateTimeUnspecified(), TimeSpan.Zero);
            estimated.IsChecked = item.Data["estimated"] is System.Text.Json.Nodes.JsonValue flag && flag.TryGetValue<bool>(out var e) && e;
            related = relatedBefore = (item.Data["related"] as System.Text.Json.Nodes.JsonArray ?? []).Select(r => Guid.TryParse(r?["id"]?.ToString(), out var id) ? id : (Guid?)null).FirstOrDefault(id => id is not null);
        }
        if (item.Type != "schedule_item") return;
        reference.Text = referenceBefore = Lines.BookingReference(s.Trip!, item);
        if (journey)
        {
            if (item.Data["time"]?["end"] is System.Text.Json.Nodes.JsonObject end && ZonedTime.From(end) is var arrives)
            {
                var local = NodaTime.Text.LocalDateTimePattern.ExtendedIso.Parse(arrives.Local).Value;
                arrival.SelectedTime = Span(local.TimeOfDay); arrivalZone = arrives.Timezone;
                arrivalDays = entry?.Day is { } first ? Math.Max(0, Period.DaysBetween(first, local.Date)) : 0;
            }
            foreach (var path in JourneyFields.Select(f => f.Path)) Detail(item, path);
        }
        if (Stays.Resolve(s.Trip!, item) is { } stay)
        {
            nights = Math.Max(1, stay.Nights); day = stay.FirstNight ?? day;
            start.SelectedTime = stay.CheckIn is { } arrives && !stay.CheckInApproximate ? Span(NodaTime.Text.LocalDateTimePattern.ExtendedIso.Parse(arrives.Local).Value.TimeOfDay) : null;
            checkOut.SelectedTime = stay.CheckOut is { } leaves && !stay.CheckOutApproximate ? Span(NodaTime.Text.LocalDateTimePattern.ExtendedIso.Parse(leaves.Local).Value.TimeOfDay) : null;
            windowFrom.SelectedTime = Span(stay.CheckInWindow?.From); windowUntil.SelectedTime = Span(stay.CheckInWindow?.Until);
            latestOut.SelectedTime = Span(stay.CheckOutWindow?.Until);
            foreach (var path in StayFields.Select(f => f.Path)) Detail(item, path);
        }
    }

    /// <summary>Where the item keeps its people: guests of a stay, participants of a plan, payers, assignees or travellers.</summary>
    private string? PeoplePath => kind switch
    {
        "accommodation" => "components/accommodation/guests",
        "expense" => "paid_by",
        "task" => "assigned_to",
        "booking" => "travelers",
        "note" => null,
        _ => "participants",
    };

    private void LoadPeople(Entity item)
    {
        if (PeoplePath is not { } path) return;
        IReadOnlyList<Guid> chosen;
        if (path == "participants") (peopleChoice, chosen) = DetailEdits.Participants(s.Trip!, item);
        else { chosen = DetailEdits.People(item, path); peopleChoice = chosen.Count > 0 ? PeopleChoice.Chosen : PeopleChoice.Everyone; }
        foreach (var id in chosen) people.Add(id);
        peopleChoiceBefore = peopleChoice; peopleBefore = [.. people];
    }

    private void LoadBooking(Entity booking)
    {
        provider.Text = providerBefore = DetailView.Provider(booking);
        reference.Text = referenceBefore = booking.Data["reference"]?.ToString();
        bookingStatus = booking.Data["status"]?.ToString() ?? "pending";
        if (booking.Data["cancellation"]?["free_until"] is System.Text.Json.Nodes.JsonObject until && NodaTime.Text.LocalDateTimePattern.ExtendedIso.Parse(ZonedTime.From(until).Local) is { Success: true } local)
        {
            cancelDay.SelectedDate = new DateTimeOffset(local.Value.Date.ToDateTimeUnspecified(), TimeSpan.Zero);
            cancelTime.SelectedTime = Span(local.Value.TimeOfDay);
        }
        foreach (var id in (booking.Data["items"] as System.Text.Json.Nodes.JsonArray ?? []).Select(n => Guid.TryParse(n?.ToString(), out var g) ? g : Guid.Empty))
            if (s.Trip!.Find(id) is { Type: "schedule_item" }) plans.Add(id);
        foreach (var item in s.Trip!.Entities.Values.Where(e => e.Type == "schedule_item" && TripCommands.BookingOf(e) == booking.Id)) plans.Add(item.Id);
        plansBefore = [.. plans];
    }

    private static readonly (string Path, string Label, bool Multiline)[] JourneyFields =
        [("components/transport/carrier", "Carrier", false), ("components/transport/flight_number", "Number", false),
         ("components/transport/departure/terminal", "Terminal", false), ("components/transport/departure/gate", "Gate", false),
         ("components/transport/arrival/terminal", "ArrivalTerminal", false), ("components/transport/seat", "Seat", false)];

    private static readonly (string Path, string Label, bool Multiline)[] StayFields =
        [("components/accommodation/rooms", "Rooms", false), ("components/accommodation/access", "Access", true)];

    private void Detail(Entity item, string path)
    {
        var value = DetailEdits.Get(item, path) is System.Text.Json.Nodes.JsonArray list ? string.Join(", ", list.Select(v => v?.ToString())) : DetailEdits.Get(item, path)?.ToString();
        details[path] = (new TextBox { Text = value, AcceptsReturn = path.EndsWith("access", StringComparison.Ordinal), TextWrapping = Avalonia.Media.TextWrapping.Wrap }, value);
    }

    private bool Scheduled => kind is "activity" or "food" or "transport" or "accommodation" or "idea" or "group";
    private bool Stay => kind == "accommodation";
    private bool Journey => journey || kind == "transport";

    /// <summary>
    /// Draws the form again. Choosing the kind at the top keeps the keyboard, so a field below the title that had the
    /// focus gets it back. Any other choice closes the keyboard, so it does not cover the choices below it.
    /// </summary>
    private void Render(bool keepFocus = false)
    {
        var focusManager = TopLevel.GetTopLevel(form)?.FocusManager;
        var focused = focusManager?.GetFocusedElement() as TextBox;
        Draw();
        if (keepFocus && focused is not null && !ReferenceEquals(focused, title) && Avalonia.VisualTree.VisualExtensions.IsAttachedToVisualTree(focused)) focused.Focus();
        else if (!keepFocus && focused is not null) TopLevel.GetTopLevel(form)?.Focus();
        UpdateSplitState();
    }

    private void Draw()
    {
        var str = s.Strings;
        if (form.Children.Count == 0) { if (editing is null) form.Children.Add(kinds); form.Children.Add(title); form.Children.Add(rest); }
        rest.Children.Clear();
        // The fields keep what was typed between draws; they leave the layout of the last draw first.
        foreach (var field in new Control[] { start, end, place, from, idea, body, amount, arrival, checkOut, windowFrom, windowUntil, latestOut, reference, provider, cancelDay, cancelTime, estimated, otherCurrency, charged, cashRate, dueDate, paidOn, newCategory, splitState }.Concat(splitBoxes.Values).Concat(details.Values.Select(d => d.Box)))
            switch (field.Parent)
            {
                case Panel panel: panel.Children.Remove(field); break;
                case ContentControl content: content.Content = null; break;
                case Decorator decorator: decorator.Child = null; break;
            }
        if (editing is null && kind != "budget")
        {
            kinds.Children.Clear();
            // A step of a group is a plan.
            foreach (var (k, icon, label) in Kinds.Where(k => into is null || k.Kind is "activity" or "food" or "transport" or "idea"))
            {
                var chip = Chip(str[label], icon, k == kind, "chip"); var chosen = k;
                chip.Click += (_, _) =>
                {
                    kind = chosen;
                    // An expense on the road was paid by the user, until the form says otherwise.
                    people.Clear(); peopleChoice = into is not null ? PeopleChoice.AsGroup : PeopleChoice.Everyone;
                    if (kind == "expense" && s.Me is { } me) { peopleChoice = PeopleChoice.Chosen; people.Add(me.Id); }
                    Render(keepFocus: true);
                };
                kinds.Children.Add(chip);
            }
        }
        if (kind == "note") { rest.Children.Add(body); return; }
        if (kind == "task")
        {
            rest.Children.Add(Field(str["Due"], Days()));
            if (PeopleField(str["Assignees"]) is { } assignees) rest.Children.Add(assignees);
            if (editing is not null) rest.Children.Add(Field(str["Notes"], body));
            return;
        }
        if (kind == "booking") { BookingFields(); return; }
        if (kind == "budget") { BudgetFields(); return; }
        if (Scheduled) rest.Children.Add(Field(Stay ? str["FirstNight"] : str["DayLabel"], Days()));
        if (Scheduled)
        {
            rest.Children.Add(Field(Stay ? str["CheckIn"] : Journey ? str["DepartureTime"] : str["StartTime"], Optional(start)));
            if (Stay)
            {
                rest.Children.Add(Field(str["Nights"], Stepper(nights, 1, 60, n => nights = n)));
                rest.Children.Add(Field(str["CheckOut"], Optional(checkOut)));
            }
            else if (Journey) rest.Children.Add(Field(str["ArrivalTime"], ArrivalControls()));
            else rest.Children.Add(Field(str["EndTime"], end));
            if (Journey) { rest.Children.Add(Field(str["From"], from)); rest.Children.Add(Field(str["To"], place)); }
            else rest.Children.Add(Field(str["Place"], place));
            if (PeopleField(Stay ? str["Guests"] : str["WhoIsGoing"]) is { } who) rest.Children.Add(who);
            if (kind != "idea") rest.Children.Add(idea);
            // The details travellers look up on the way, offered once the item exists.
            if (editing is not null && Journey)
            {
                foreach (var (path, label, _) in JourneyFields) if (details.TryGetValue(path, out var d)) rest.Children.Add(Field(str[label], d.Box));
                rest.Children.Add(Field(str["BookingNumber"], reference));
            }
            if (editing is not null && Stay)
            {
                rest.Children.Add(Field(str["CheckInHours"], Ui.V(6, Ui.Text(str["WindowFrom"], "caption"), Optional(windowFrom), Ui.Text(str["WindowUntil"], "caption"), Optional(windowUntil))));
                rest.Children.Add(Field(str["LatestCheckOut"], Optional(latestOut)));
                foreach (var (path, label, _) in StayFields) if (details.TryGetValue(path, out var d)) rest.Children.Add(Field(str[label], d.Box));
                rest.Children.Add(Field(str["BookingNumber"], reference));
            }
        }
        if (kind == "expense")
        {
            rest.Children.Add(Field(str["Amount"], Money()));
            // What the card booked in the trip's currency (or the one of a charge already entered), next to the amount on the bill.
            if (Currency() != ChargeCurrency())
            {
                // By card, with what was charged, or with cash bought before, with the rate it cost.
                var card = Chip(str["PaidWithCard"], "credit-card", !cash, "chip"); card.Click += (_, _) => { cash = false; Render(); };
                var bank = Chip(str["PaidWithCash"], "banknote", cash, "chip"); bank.Click += (_, _) => { cash = true; OfferCashRate(); Render(); };
                rest.Children.Add(Field(str["PaidWith"], Ui.Flow(8, card, bank)));
                if (cash)
                {
                    var form = CashForm();
                    AutomationProperties.SetName(cashRate, str["CashRateLabel"] + ": 1 " + form.Left + " = … " + form.Right);
                    // The calculator opens below the rate, so the form stays short until it is needed.
                    var calculator = Ui.IconButton("calculator", str["RateCalculator"]).Also(b => { if (calculating) b.Classes.Add("selected"); });
                    calculator.Click += (_, _) => { calculating = !calculating; Render(); };
                    var row = Ui.H(6, Ui.Text("1 " + form.Left + " =").Also(t => t.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center), cashRate, Ui.Text(form.Right).Also(t => t.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center), calculator);
                    var note = cashOffered is not null && cashRate.Text == cashOffered && cashOfferSource is { } source ? string.Format(CultureInfo.CurrentCulture, str["RateTaken"], source) : str["CashRateHint"];
                    var parts = Ui.V(8, row, Ui.Text(note, "caption").Also(t => t.TextWrapping = Avalonia.Media.TextWrapping.Wrap));
                    if (calculating)
                        parts.Children.Add(Ui.Card(Jourfold.UI.RateCalculator.Build(str, Currency(), ChargeCurrency(), s.EstimateRates, rate =>
                        {
                            cashRate.Text = CashForm().Shown(rate).ToString("0.####", CultureInfo.CurrentCulture);
                            calculating = false; Render();
                        }), 12));
                    rest.Children.Add(Field(str["CashRateLabel"], parts));
                }
                else
                {
                    var label = string.Format(CultureInfo.CurrentCulture, str["ChargedIn"], ChargeCurrency());
                    AutomationProperties.SetName(charged, label);
                    rest.Children.Add(Field(label, Ui.V(4, charged, Ui.Text(str["ChargedHintShort"], "caption").Also(t => t.TextWrapping = Avalonia.Media.TextWrapping.Wrap))));
                }
            }
            rest.Children.Add(estimated);
            // A payment still to pay can be due on a day; the phone reminds three days before.
            if (estimated.IsChecked == true)
            {
                var clearDue = Ui.IconButton("x", str["NoDueDate"]); clearDue.Click += (_, _) => dueDate.SelectedDate = null;
                rest.Children.Add(Field(str["Due"], Ui.Columns("*,Auto", dueDate, clearDue)));
            }
            else
            {
                var clearPaid = Ui.IconButton("x", str["ClearPaidOn"]); clearPaid.Click += (_, _) => paidOn.SelectedDate = null;
                rest.Children.Add(Field(str["PaidOnLabel"], Ui.Columns("*,Auto", paidOn, clearPaid)));
            }
            if (PeopleField(str["Payer"]) is { } payers) rest.Children.Add(payers);
            rest.Children.Add(Field(str["BelongsTo"], RelatedControls()));
            rest.Children.Add(Field(str["SharedByLabel"], SharingControls()));
            rest.Children.Add(Field(str["Category"], CategoryControls()));
            if (editing is null && s.Host.TakePhoto is { } take)
            {
                var photo = Ui.Button(receipt is null ? str["PhotographReceipt"] : str["ReceiptTaken"], receipt is null ? "camera" : "check", "soft").Also(b => b.HorizontalAlignment = HorizontalAlignment.Left);
                photo.Click += async (_, _) => { if (await take(CancellationToken.None) is { } taken) { receipt = taken; Render(); } };
                rest.Children.Add(photo);
            }
        }
        if (editing is not null) rest.Children.Add(Field(str["Notes"], body));
    }

    /// <summary>A choice that leaves the focus where it is, so the keyboard stays open while choosing.</summary>
    private static ChoiceButton Chip(string text, string? icon, bool selected, params string[] classes) => Ui.Choice(text, icon, selected, classes).Also(c => c.Focusable = false);

    private static Control Field(string label, Control value) => Ui.V(6, Ui.Text(label, "overline"), value);

    /// <summary>
    /// The trip's people as chips to choose from. A plan offers "Everyone", and in a group "As the group"; payers,
    /// assignees, guests and travellers are simply the people chosen. Without people in the trip there is nothing to show.
    /// </summary>
    private Control? PeopleField(string label)
    {
        var str = s.Strings; var trip = s.Trip!;
        var everyone = trip.Entities.Values.Where(e => e.Type == "person").OrderBy(p => p.Title, StringComparer.CurrentCulture).ToArray();
        if (everyone.Length == 0 || PeoplePath is null) return null;
        // Quick Add stays short: a new plan or task is for everyone until it is edited, an expense or booking asks.
        if (editing is null && kind is not ("expense" or "booking")) return null;
        var plan = PeoplePath == "participants";
        var inGroup = into is not null || editing is not null && TripQueries.Parent(trip, editing.Id) is not null;
        var chips = Ui.Flow(8);
        void Option(string text, string icon, PeopleChoice choice)
        {
            var chip = Chip(text, icon, peopleChoice == choice, "chip");
            chip.Click += (_, _) => { peopleChoice = choice; people.Clear(); Render(); };
            chips.Children.Add(chip);
        }
        // A new step has no "Everyone": it is for the group's people unless people are chosen.
        if (plan && !(into is not null)) Option(str["Everyone"], "users", PeopleChoice.Everyone);
        if (plan && inGroup) Option(str["AsTheGroup"], "layers", PeopleChoice.AsGroup);
        foreach (var person in everyone)
        {
            var id = person.Id;
            var chip = Chip(person.Title + (s.Me?.Id == id ? " (" + str["MeBadge"] + ")" : ""), null, peopleChoice == PeopleChoice.Chosen && people.Contains(id), "chip");
            chip.Click += (_, _) =>
            {
                if (peopleChoice != PeopleChoice.Chosen) { people.Clear(); peopleChoice = PeopleChoice.Chosen; }
                if (!people.Remove(id)) people.Add(id);
                if (people.Count == 0) peopleChoice = into is not null ? PeopleChoice.AsGroup : PeopleChoice.Everyone;
                Render();
            };
            chips.Children.Add(chip);
        }
        return Field(label, chips);
    }

    private bool PeopleChanged => peopleChoice != peopleChoiceBefore || !people.SetEquals(peopleBefore);

    /// <summary>
    /// The amount, the currencies as chips (the expense's, the trip's, the phone's and those chosen lately, shared with
    /// the converter) and a field for any other code.
    /// </summary>
    private Control Money()
    {
        var currencies = Ui.Flow(8);
        var recent = s.RecentCurrencies.Where(c => !s.TripCurrencies().Contains(c)).ToArray();
        foreach (var c in s.TripCurrencies().Prepend(currency).Concat(recent).Distinct())
        {
            var chip = Chip(c, null, c == currency, "chip"); var chosen = c;
            chip.Click += (_, _) => ChooseCurrency(chosen);
            if (recent.Contains(c) && c != currency)
            {
                AutomationProperties.SetHelpText(chip, s.Strings["RemoveFromListHint"]);
                Menus.OnHold(chip, () => s.ShowMenu(new ActionMenu(chosen, null, [new MenuAction(s.Strings["RemoveFromList"], "x", _ => { s.ForgetConverterCurrency(chosen); Render(); return System.Threading.Tasks.Task.CompletedTask; })])));
            }
            currencies.Children.Add(chip);
        }
        currencies.Children.Add(otherCurrency);
        return Ui.V(8, amount, currencies);
    }

    /// <summary>Makes <paramref name="code"/> the expense's currency; one the trip does not offer joins the recent ones.</summary>
    private void ChooseCurrency(string code)
    {
        currency = code; otherCurrency.Text = ""; otherCurrency.SelectedItem = null;
        s.RememberCurrency(code);
        Render();
    }

    /// <summary>The currency typed or chosen, as a three-letter code.</summary>
    private string ChargeCurrency() => chargedCurrency ?? s.TripCurrencyCode;

    /// <summary>How the cash rate is shown: one unit of the stronger currency first, as on a cash machine's receipt.</summary>
    private Jourfold.UI.CostTexts.CashRateForm CashForm() =>
        Jourfold.UI.CostTexts.CashRateForm.For(Currency(), ChargeCurrency(), Jourfold.UI.CostTexts.SuggestedCashRate(s.Trip!, Currency(), ChargeCurrency(), s.EstimateRates));

    /// <summary>Fills in the rate of the last cash in this currency, or the reference rate, when none is typed yet.</summary>
    private void OfferCashRate()
    {
        if (!string.IsNullOrWhiteSpace(cashRate.Text) || Jourfold.UI.CostTexts.OfferCashRate(s.Trip!, Currency(), ChargeCurrency(), s.EstimateRates, editing?.Id) is not { } offer) return;
        cashRate.Text = cashOffered = CashForm().Shown(offer.Rate).ToString("0.####", CultureInfo.CurrentCulture);
        cashOfferSource = offer.Source(s.Strings, Currency());
    }
    private string Currency() => Jourfold.UI.CurrencyCodes.Read(otherCurrency.Text) ?? currency;

    /// <summary>A booking: provider, number, status, price, free cancellation, travellers and the plans it is for.</summary>
    private void BookingFields()
    {
        var str = s.Strings;
        rest.Children.Add(Field(str["Provider"], provider));
        rest.Children.Add(Field(str["BookingNumber"], reference));
        var statuses = Ui.Flow(8);
        foreach (var (status, icon) in BookingStatuses)
        {
            var chip = Chip(str[status], icon, status == bookingStatus, "chip"); var chosen = status;
            chip.Click += (_, _) => { bookingStatus = chosen; Render(); };
            statuses.Children.Add(chip);
        }
        rest.Children.Add(Field(str["Status"], statuses));
        // A booking has no price; a new one's amount becomes a payment still to pay, and payments are kept with the booking.
        if (editing is null) rest.Children.Add(Field(str["AmountStillToPay"], Money()));
        var clearDay = Ui.IconButton("x", str["NoFreeCancellation"]); clearDay.Click += (_, _) => { cancelDay.SelectedDate = null; cancelTime.SelectedTime = null; };
        rest.Children.Add(Field(str["FreeCancellationUntil"], Ui.V(6, Ui.Columns("*,Auto", cancelDay, clearDay), Optional(cancelTime))));
        if (PeopleField(str["Travellers"]) is { } travellers) rest.Children.Add(travellers);
        rest.Children.Add(Field(str["PlansOfBooking"], PlanControls()));
        if (editing is not null) rest.Children.Add(Field(str["Notes"], body));
    }

    /// <summary>The plans a booking is for, each with a button to unlink it, and a button to link another.</summary>
    private Control PlanControls()
    {
        var str = s.Strings; var trip = s.Trip!;
        var list = Ui.V(4);
        foreach (var plan in plans.Select(trip.Find).OfType<Entity>().OrderBy(e => s.Entry(e.Id)?.Start ?? Instant.MaxValue))
        {
            var id = plan.Id;
            var remove = Ui.IconButton("x", string.Format(CultureInfo.CurrentCulture, str["UnlinkPlan"], plan.Title));
            remove.Click += (_, _) => { plans.Remove(id); Render(); };
            list.Children.Add(Ui.Columns("Auto,*,Auto", Ui.Icon("calendar", 16, "Text.Muted").Margin(0, 0, 10, 0), Ui.V(0, Ui.Text(plan.Title), PlanDay(id) is { } when ? Ui.Text(when, "caption") : null), remove));
        }
        var add = Ui.Button(str["LinkPlan"], "link", "ghost", "compact").Also(b => b.HorizontalAlignment = HorizontalAlignment.Left);
        add.Click += (_, _) => s.ShowMenu(new ActionMenu(str["LinkPlan"], null, Plans(e => !plans.Contains(e.Id))
            .Select(e => new MenuAction(Label(e), "calendar", _ => { plans.Add(e.Id); Render(); return Task.CompletedTask; })).ToArray()));
        list.Children.Add(add);
        return list;
    }

    /// <summary>
    /// Who shares an expense and how: equally among the people chosen as chips (the booking's travellers or the plan's
    /// people at first), or by amounts, shares or percent with a value per person and what is still to divide.
    /// </summary>
    private Control SharingControls()
    {
        var str = s.Strings; var trip = s.Trip!;
        if (!sharingTouched) { sharing.Clear(); sharing.UnionWith(Expenses.PeopleFor(trip, related)); }
        if (splitMethod is not { } method)
            return Ui.Text(str["SplitUnknown"], "caption").Also(t => t.TextWrapping = Avalonia.Media.TextWrapping.Wrap);
        var panel = Ui.V(8);
        var methods = Ui.Flow(8);
        foreach (var (option, label, icon) in new[] { (SplitMethod.Equal, "SplitEqual", "users"), (SplitMethod.Amount, "SplitAmount", "coins"), (SplitMethod.Share, "SplitShare", "sliders-horizontal"), (SplitMethod.Percent, "SplitPercent", "circle-dot") })
        {
            var chip = Chip(str[label], icon, option == method, "chip"); var chosen = option;
            chip.Click += (_, _) => ChangeSplitMethod(chosen);
            methods.Children.Add(chip);
        }
        AutomationProperties.SetName(methods, str["SplitMethod"]);
        panel.Children.Add(methods);
        var people = TripPeople.Travelers(trip).Concat(sharing.Select(trip.Find).OfType<Entity>()).DistinctBy(p => p.Id).ToArray();
        if (method == SplitMethod.Equal)
        {
            var chips = Ui.Flow(8);
            foreach (var person in people)
            {
                var id = person.Id;
                var chip = Chip(person.Title, sharing.Contains(id) ? "check" : null, sharing.Contains(id), "chip");
                AutomationProperties.SetName(chip, string.Format(CultureInfo.CurrentCulture, str["SharesExpense"], person.Title));
                // Somebody always shares it.
                chip.Click += (_, _) => { if (sharing.Contains(id) && sharing.Count == 1) return; if (!sharing.Remove(id)) sharing.Add(id); sharingTouched = splitTouched = true; Render(); };
                chips.Children.Add(chip);
            }
            panel.Children.Add(chips);
            return panel;
        }
        panel.Children.Add(Ui.Text(method switch { SplitMethod.Amount => string.Format(CultureInfo.CurrentCulture, str["SplitHintAmount"], Currency()), SplitMethod.Share => str["SplitHintShare"], _ => str["SplitHintPercent"] }, "caption").Also(t => t.TextWrapping = Avalonia.Media.TextWrapping.Wrap));
        foreach (var person in people)
        {
            var id = person.Id; var included = sharing.Contains(id);
            var chip = Chip(person.Title, included ? "check" : null, included, "chip");
            AutomationProperties.SetName(chip, string.Format(CultureInfo.CurrentCulture, str["SharesExpense"], person.Title));
            chip.Click += (_, _) =>
            {
                if (included && sharing.Count == 1) return;
                if (included) sharing.Remove(id); else { sharing.Add(id); if (string.IsNullOrWhiteSpace(SplitBox(id).Text)) SplitBox(id).Text = ValueText(method == SplitMethod.Share ? 1m : 0m); }
                sharingTouched = splitTouched = true; Render();
            };
            var suffix = method switch { SplitMethod.Amount => Currency(), SplitMethod.Percent => "%", _ => "×" };
            panel.Children.Add(Ui.Columns("*,120,40", chip, included ? SplitBox(id) : null, included ? Ui.Text(suffix, "caption").Also(t => { t.VerticalAlignment = VerticalAlignment.Center; t.Margin = new Thickness(8, 0, 0, 0); }) : null));
        }
        panel.Children.Add(splitState);
        return panel;
    }

    /// <summary>The field for one person's amount, weight or percentage, kept between draws.</summary>
    private TextBox SplitBox(Guid person)
    {
        if (splitBoxes.TryGetValue(person, out var box)) return box;
        box = new TextBox { HorizontalContentAlignment = HorizontalAlignment.Right, MinHeight = 44 };
        Avalonia.Input.TextInput.TextInputOptions.SetContentType(box, Avalonia.Input.TextInput.TextInputContentType.Number);
        AutomationProperties.SetName(box, string.Format(CultureInfo.CurrentCulture, s.Strings["SplitValueOf"], s.Trip!.Find(person)?.Title));
        box.TextChanged += (_, _) => { splitTouched = true; UpdateSplitState(); };
        // A tap selects the value, so typing replaces it.
        box.GotFocus += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(box.SelectAll);
        return splitBoxes[person] = box;
    }

    private static string ValueText(decimal value) => value.ToString("0.##", CultureInfo.CurrentCulture);

    /// <summary>The people who share it, in the trip's order.</summary>
    private Guid[] SplitPeople() => TripPeople.Travelers(s.Trip!).Select(p => p.Id).Concat(sharing).Distinct().Where(sharing.Contains).ToArray();

    /// <summary>The expense as the form would save it, for TravelRepo to divide; null when a value is not a number.</summary>
    private Entity? SplitProbe()
    {
        var probe = Entity.Create("expense", "");
        var code = Currency(); var value = ExchangeRates.Amount(amount.Text) ?? amountBefore?.Value ?? 0m;
        probe.Data["amount"] = new System.Text.Json.Nodes.JsonObject { ["value"] = Expenses.Format(value, code.Length == 3 ? code : "EUR"), ["currency"] = code.Length == 3 ? code : "EUR" };
        var people = SplitPeople();
        if (splitMethod is SplitMethod.Equal) return Expenses.WithSplit(probe, SplitMethod.Equal, people.Select(p => (p, (decimal?)null)));
        if (splitMethod is not { } method) return null;
        var values = people.Select(p => (p, ExchangeRates.Amount(SplitBox(p).Text))).ToArray();
        return values.Any(v => v.Item2 is null) ? null : Expenses.WithSplit(probe, method, values);
    }

    /// <summary>Whether the split can be saved: equal, or values that are numbers and add up.</summary>
    private bool SplitValid() => kind != "expense" || splitMethod is null or SplitMethod.Equal
        || SplitProbe() is { } probe && (Expenses.Split(s.Trip!, probe).Issues & (SplitIssues.SumMismatch | SplitIssues.InvalidValue | SplitIssues.NoPeople)) == 0;

    /// <summary>Says what is still to divide and allows saving only a split that adds up, as on the computer.</summary>
    private void UpdateSplitState()
    {
        var valid = SplitValid();
        if (addButton is not null) addButton.IsEnabled = valid;
        if (kind != "expense" || splitMethod is null or SplitMethod.Equal) { splitState.IsVisible = false; return; }
        splitState.IsVisible = true;
        var str = s.Strings;
        if (SplitProbe() is not { } probe) { splitState.Text = "⚠ " + str["CheckSplit"]; return; }
        var split = Expenses.Split(s.Trip!, probe);
        if (split.Issues.HasFlag(SplitIssues.SumMismatch) && split.Target is { } target)
        {
            var rest = target - split.Assigned; var currencyCode = split.Amount?.Currency ?? Currency();
            string F(decimal x) => splitMethod == SplitMethod.Percent ? x.ToString("0.##", CultureInfo.CurrentCulture) + " %" : Formats.Money(x, currencyCode);
            splitState.Text = "⚠ " + string.Format(CultureInfo.CurrentCulture, str[rest > 0 ? "SplitLeft" : "SplitOver"], F(split.Assigned), F(target), F(Math.Abs(rest)));
        }
        else splitState.Text = valid ? string.Join(", ", split.Shares.Select(x => (s.Trip!.Find(x.Person)?.Title ?? "?") + " " + Formats.Money(x.Amount, split.Amount!.Currency))) : "⚠ " + str["CheckSplit"];
    }

    /// <summary>Another method, keeping the current division as far as it goes (see <see cref="Expenses.ChangeMethod"/>).</summary>
    private void ChangeSplitMethod(SplitMethod method)
    {
        if (method == splitMethod) return;
        var trip = s.Trip!;
        var probe = SplitProbe() ?? Expenses.WithSplit(SplitProbe(SplitMethod.Equal)!, SplitMethod.Equal, SplitPeople().Select(p => (p, (decimal?)null)));
        var changed = Expenses.ChangeMethod(trip, probe, method);
        foreach (var part in (changed.Data["split"]?["parts"] as System.Text.Json.Nodes.JsonArray ?? []).OfType<System.Text.Json.Nodes.JsonObject>())
            if (Guid.TryParse(part["person"]?.ToString(), out var who) && ExchangeRates.Amount(part["value"]?.ToString()) is { } value) SplitBox(who).Text = ValueText(value);
        splitMethod = method; splitTouched = sharingTouched = true;
        Render();
    }

    private Entity? SplitProbe(SplitMethod method) { var before = splitMethod; splitMethod = method; var probe = SplitProbe(); splitMethod = before; return probe; }

    /// <summary>The category as chips: the standard ones and the trip's own, the suggested one marked until another is chosen, and a new one.</summary>
    private Control CategoryControls()
    {
        var str = s.Strings; var trip = s.Trip!;
        var probe = Entity.Create("expense", "");
        if (related is { } id && trip.Find(id) is { } target) probe.Data["related"] = new System.Text.Json.Nodes.JsonArray(new System.Text.Json.Nodes.JsonObject { ["type"] = target.Type, ["id"] = id.ToString() });
        var current = category ?? ExpenseCategories.Suggested(trip, probe) ?? ExpenseCategories.Other;
        var chips = Ui.Flow(8);
        foreach (var c in ExpenseCategories.Standard.Concat(ExpenseCategories.Custom(trip)).Append(category).OfType<string>().Distinct(StringComparer.CurrentCultureIgnoreCase))
        {
            var chosen = c;
            var chip = Chip(Jourfold.UI.CostTexts.Category(str, c), null, string.Equals(c, current, StringComparison.CurrentCultureIgnoreCase), "chip");
            chip.Click += (_, _) => { category = chosen == category ? null : chosen; Render(); };
            chips.Children.Add(chip);
        }
        if (!addingCategory)
        {
            var add = Chip(str["NewCategory"], "plus", false, "chip");
            add.Click += (_, _) => { addingCategory = true; Render(); Avalonia.Threading.Dispatcher.UIThread.Post(() => newCategory.Focus(), Avalonia.Threading.DispatcherPriority.Background); };
            chips.Children.Add(add);
            return chips;
        }
        var ok = Ui.IconButton("check", str["NewCategory"]); ok.Click += (_, _) => TakeNewCategory();
        return Ui.V(8, chips, Ui.Columns("*,Auto", newCategory, ok));
    }

    /// <summary>The category typed as a new one, which both travellers then see.</summary>
    private void TakeNewCategory()
    {
        if (newCategory.Text?.Trim() is { Length: > 0 } typed) category = typed;
        newCategory.Text = ""; addingCategory = false; Render();
    }

    /// <summary>A budget: its amount and what it applies to (a category, a person's part, a collection, together).</summary>
    private void BudgetFields()
    {
        var str = s.Strings; var trip = s.Trip!;
        rest.Children.Add(Field(str["Amount"], Money()));
        Control Row(string label, string all, string? current, IEnumerable<(string Id, string Label)> options, Func<string?, BudgetFilters> with)
        {
            var chips = Ui.Flow(8);
            foreach (var (id, text) in options.Prepend((null!, all)))
            {
                var selected = id is null ? current is null : string.Equals(id, current, StringComparison.CurrentCultureIgnoreCase);
                var chip = Chip(text, null, selected, "chip");
                AutomationProperties.SetName(chip, label + ": " + text);
                chip.Click += (_, _) => { budgetFilters = with(id); Render(); };
                chips.Children.Add(chip);
            }
            return Field(label, chips);
        }
        var f = budgetFilters;
        rest.Children.Add(Ui.Text(str["BudgetScope"], "strong"));
        rest.Children.Add(Row(str["category"], str["BudgetAny"], f.Category, ExpenseCategories.Standard.Concat(ExpenseCategories.Custom(trip)).Select(c => (c, Jourfold.UI.CostTexts.Category(str, c))), c => budgetFilters with { Category = c }));
        rest.Children.Add(Row(str["participant"], str["Everyone"], f.Person?.ToString(), TripPeople.Travelers(trip).Select(p => (p.Id.ToString(), p.Title)), p => budgetFilters with { Person = Guid.TryParse(p, out var id) ? id : null }));
        var collections = trip.Entities.Values.Where(e => e.Type == "collection" && !PackingLists.IsList(e)).OrderBy(e => e.Title, StringComparer.CurrentCulture).ToArray();
        if (collections.Length > 0) rest.Children.Add(Row(str["collection"], str["BudgetAny"], f.Collection?.ToString(), collections.Select(c => (c.Id.ToString(), c.Title)), k => budgetFilters with { Collection = Guid.TryParse(k, out var id) ? id : null }));
        rest.Children.Add(Ui.Text(f.IsWholeTrip ? str["BudgetWholeTripHint"] : str["BudgetLimitedHint"], "caption").Also(t => t.TextWrapping = Avalonia.Media.TextWrapping.Wrap));
    }

    /// <summary>The plan or booking an expense belongs to, with buttons to choose another or none.</summary>
    private Control RelatedControls()
    {
        var str = s.Strings; var trip = s.Trip!;
        var current = related is { } id ? trip.Find(id) : null;
        var choose = Ui.Button(current?.Title ?? str["LinkPlanOrBooking"], current is null ? "link" : "pencil", "ghost", "compact").Also(b => b.HorizontalAlignment = HorizontalAlignment.Left);
        choose.Click += (_, _) => s.ShowMenu(new ActionMenu(str["BelongsTo"], null, Plans(_ => true)
            .Concat(trip.Entities.Values.Where(e => e.Type == "booking").OrderBy(e => e.Title, StringComparer.CurrentCulture))
            .Where(e => e.Id != related).Select(e => new MenuAction(e.Type == "booking" ? e.Title : Label(e), e.Type == "booking" ? "ticket" : "calendar", _ => { related = e.Id; Render(); return Task.CompletedTask; })).ToArray()));
        if (current is null) return choose;
        var clear = Ui.IconButton("x", str["RemoveLink"]); clear.Click += (_, _) => { related = null; Render(); };
        return Ui.Columns("*,Auto", choose, clear);
    }

    /// <summary>The trip's plans in time order, those without a day last; groups and their steps alike.</summary>
    private IEnumerable<Entity> Plans(Func<Entity, bool> where) => s.Trip!.Entities.Values.Where(e => e.Type == "schedule_item" && where(e))
        .OrderBy(e => s.Entry(e.Id)?.Start ?? Instant.MaxValue).ThenBy(e => e.Title, StringComparer.CurrentCulture);

    private string Label(Entity plan) => PlanDay(plan.Id) is { } when ? when + " · " + plan.Title : plan.Title;

    private string? PlanDay(Guid id) => s.Entry(id)?.Day is { } d ? Lines.DayShort(d, s.Strings) : null;

    /// <summary>A time that may also be left open, with a button that clears it.</summary>
    private Control Optional(TimePicker picker)
    {
        var clear = Ui.IconButton("x", s.Strings["ClearTime"]); clear.Click += (_, _) => picker.SelectedTime = null;
        return Ui.Columns("*,Auto", picker, clear);
    }

    private Control Stepper(int value, int min, int max, Action<int> set)
    {
        var shown = Ui.Text(value.ToString(CultureInfo.CurrentCulture), "h3").Also(t => { t.MinWidth = 40; t.TextAlignment = Avalonia.Media.TextAlignment.Center; t.VerticalAlignment = VerticalAlignment.Center; });
        var less = Ui.IconButton("minus", s.Strings["OneLess"]); var more = Ui.IconButton("plus", s.Strings["OneMore"]);
        void Show(int v) { value = Math.Clamp(v, min, max); shown.Text = value.ToString(CultureInfo.CurrentCulture); set(value); less.IsEnabled = value > min; more.IsEnabled = value < max; }
        less.Click += (_, _) => Show(value - 1); more.Click += (_, _) => Show(value + 1); Show(value);
        return Ui.H(4, less, shown, more);
    }

    /// <summary>The arrival time, the day it falls on and the zone it is read in, as tickets print it.</summary>
    private Control ArrivalControls()
    {
        var str = s.Strings;
        var days = Ui.Flow(6);
        for (var i = 0; i <= 2; i++)
        {
            var offset = i;
            var chip = Chip(i == 0 ? str["SameDay"] : i == 1 ? str["OneDayLater"] : string.Format(CultureInfo.CurrentCulture, str["DaysLater"], i), null, i == arrivalDays, "chip");
            chip.Click += (_, _) => { arrivalDays = offset; Render(); };
            days.Children.Add(chip);
        }
        var zones = Ui.Flow(6);
        foreach (var zone in ArrivalZones())
        {
            var chip = Chip(TimeText.Place(DateTimeZoneProviders.Tzdb[zone]), null, zone == (arrivalZone ?? ArrivalZones().First()), "chip"); var chosen = zone;
            chip.Click += (_, _) => { arrivalZone = chosen; Render(); };
            zones.Children.Add(chip);
        }
        return Ui.V(8, Optional(arrival), days, Ui.Text(str["ArrivalZone"], "caption"), zones);
    }

    /// <summary>
    /// The zones an arrival is likely in: the destination's, the one it has, then the trip's zones, most used first.
    /// </summary>
    private IReadOnlyList<string> ArrivalZones()
    {
        var trip = s.Trip!;
        var to = Choice(place.Text) is { Id: { } id } ? trip.Find(id)?.Data["timezone"]?.ToString() : null;
        var used = trip.Entities.Values.Where(e => e.Type == "schedule_item")
            .SelectMany(e => new[] { e.Data["time"]?["start"]?["timezone"]?.ToString(), e.Data["time"]?["end"]?["timezone"]?.ToString() })
            .OfType<string>().GroupBy(z => z).OrderByDescending(g => g.Count()).Select(g => g.Key);
        return new[] { to, arrivalZone, ScheduleQueries.TripZone(trip).Id }.OfType<string>().Concat(used).Append(s.PhoneZone.Id)
            .Where(z => DateTimeZoneProviders.Tzdb.GetZoneOrNull(z) is not null).Distinct().Take(6).ToArray();
    }

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
            var chip = Chip(d.Date.ToString("ddd d", culture), null, d.Date == day, "chip"); var chosen = d.Date;
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
            LocalTime? startTime = Time(start);
            LocalTime? endTime = Journey || Stay ? null : Time(end);
            Duration? length = startTime is { } a && endTime is { } b ? (b > a ? b - a : b - a + Period.FromDays(1)).ToDuration() : null;
            var arrivalZoneId = arrivalZone ?? (Journey ? ArrivalZones().First() : null);
            var placeChoice = Choice(place.Text); var fromChoice = Choice(from.Text);
            decimal? money = null;
            if (kind == "expense" && !SplitValid()) throw new DomainException("mobile.amount", s.Strings["SplitMustAddUp"]);
            if (kind is "expense" or "booking" or "budget" && !(kind == "booking" && string.IsNullOrWhiteSpace(amount.Text)))
            {
                // As people type it, "1.234,50" or "1,234.50" alike (see ExchangeRates.Amount). An amount left as it
                // was keeps its exact value, also one with three decimals that would read as thousands.
                money = amountBefore is { } before && before.Text == amount.Text ? before.Value
                    : ExchangeRates.Amount(amount.Text) ?? throw new DomainException("mobile.amount", s.Strings["EnterAmount"]);
            }
            if (money is not null && Currency() is var code && (code.Length != 3 || !code.All(char.IsAsciiLetterUpper))) throw new DomainException("mobile.amount", s.Strings["EnterCurrency"]);
            if (editing is not null)
            {
                // A stay's day and hours are its nights; they are saved with the details below.
                await s.SaveEditAsync(editing.Id, title.Text!, body.Text, Scheduled && !Stay || kind == "task" ? day : null, Scheduled && !Stay ? startTime : null, length, null, task: kind == "task");
                // Only places the form changed are written, so an inherited place stays inherited.
                if (editing.Type == "schedule_item" && Changed(place.Text, placeBefore)) await s.AssignPlaceAsync(editing.Id, journey ? PlaceSlot.To : PlaceSlot.Place, placeChoice);
                if (journey && Changed(from.Text, fromBefore)) await s.AssignPlaceAsync(editing.Id, PlaceSlot.From, fromChoice);
                if (editing.Type == "schedule_item") await SaveDetailsAsync(editing.Id, startTime, arrivalZoneId);
                else await SaveOtherAsync(editing.Id, money, created: false);
                s.ShowDetails(editing.Id);
                return;
            }
            var draft = new QuickAddDraft(kind is "idea" or "group" ? "activity" : kind, title.Text!)
            {
                Start = startTime,
                Length = length,
                Place = kind == "transport" ? null : placeChoice,
                From = kind == "transport" ? fromChoice : null,
                To = kind == "transport" ? placeChoice : null,
                Body = kind == "note" ? body.Text : null,
                Amount = money,
                Currency = kind is "expense" or "booking" or "budget" ? Currency() : null,
                Reference = kind == "booking" ? reference.Text : null,
                People = peopleChoice == PeopleChoice.Chosen ? [.. people] : [],
                Date = Scheduled || kind == "task" ? day : null,
                Nights = nights,
                ArrivalDate = Journey && Time(arrival) is not null && day is { } d0 ? d0.PlusDays(arrivalDays) : null,
                ArrivalTime = Journey ? Time(arrival) : null,
                ArrivalTimezone = Journey ? arrivalZoneId : null,
            };
            var added = await s.AddAsync(draft, idea: kind == "idea" || (Scheduled && idea.IsChecked == true), into);
            if (kind is "booking" or "expense" or "budget") await SaveOtherAsync(added, money, created: true);
            if (receipt is { } photo) await s.AddFilesAsync([photo], added);
            // A new group opens, to add its steps.
            if (kind == "group") s.ShowDetails(added);
        }
        // The form's own checks carry translated text; anything from the domain is looked up by its code.
        catch (DomainException ex) { problem.Text = ex.Code is "mobile.title" or "mobile.amount" or "mobile.dates" ? ex.Message : s.Strings.Diagnostic(ex.Code); problem.IsVisible = true; }
        finally { add.IsEnabled = true; }
    }

    /// <summary>The journey's arrival, the stay's nights and hours, the text details and the booking number, as one change.</summary>
    private async Task SaveDetailsAsync(Guid id, LocalTime? startTime, string? zone)
    {
        var trip = s.Trip!; var item = trip.Find(id)!.Copy();
        if (Journey && Time(arrival) is { } arrives && s.Entry(id)?.Day is { } departure && zone is not null) item = DetailEdits.WithArrival(item, departure.PlusDays(arrivalDays), arrives, zone);
        if (Stay && day is { } first)
        {
            var stayZone = Stays.Resolve(trip, item)?.Zone ?? ScheduleQueries.TripZone(trip).Id;
            item = DetailEdits.WithStay(item, first, nights, startTime, Time(checkOut), stayZone);
            DetailEdits.SetWindow(item, "check_in_window", Time(windowFrom), Time(windowUntil));
            var latest = Time(latestOut);
            DetailEdits.SetWindow(item, "check_out_window", null, latest);
        }
        if (PeoplePath is { } who && PeopleChanged)
        {
            if (who == "participants") DetailEdits.SetParticipants(trip, item, peopleChoice, people);
            else DetailEdits.SetPeople(item, who, peopleChoice == PeopleChoice.Chosen ? people : []);
        }
        foreach (var (path, (box, before)) in details)
        {
            if (!Changed(box.Text, before)) continue;
            if (path.EndsWith("rooms", StringComparison.Ordinal))
                DetailEdits.Set(item, path, string.IsNullOrWhiteSpace(box.Text) ? null : new System.Text.Json.Nodes.JsonArray(box.Text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(r => (System.Text.Json.Nodes.JsonNode?)r).ToArray()));
            else DetailEdits.SetText(item, path, box.Text);
        }
        var edits = new List<TravelRepo.Repository.EntityEdit>();
        if (Changed(reference.Text, referenceBefore))
        {
            // A new booking links the item to it, so the item from that edit is the one to save.
            var linked = DetailEdits.WithBookingReference(trip, item, reference.Text);
            edits.AddRange(linked.Where(e => e.Id != item.Id));
            if (linked.FirstOrDefault(e => e.Id == item.Id)?.Value is { } withBooking) item = withBooking;
        }
        edits.Add(new(item.Id, item));
        await s.SaveDetailsAsync(edits);
    }

    /// <summary>
    /// What a booking, expense or task has beyond its title: people, a booking's provider, number, status, price, free
    /// cancellation and plans; an expense's amount, currency, whether it is estimated and what it belongs to. A new
    /// item gets all of it, an edited one only what the form changed.
    /// </summary>
    private async Task SaveOtherAsync(Guid id, decimal? money, bool created)
    {
        var trip = s.Trip!; var item = trip.Find(id)!.Copy();
        var edits = new List<TravelRepo.Repository.EntityEdit>();
        // A new item has its people from Quick Add already.
        if (PeoplePath is { } path && !created && PeopleChanged) DetailEdits.SetPeople(item, path, peopleChoice == PeopleChoice.Chosen ? people : []);
        if (kind == "expense")
        {
            if (money is { } value) DetailEdits.SetMoney(item, "amount", value, Currency());
            item.Data["estimated"] = estimated.IsChecked == true;
            var dueDay = estimated.IsChecked == true && dueDate.SelectedDate is { } picked ? LocalDate.FromDateTime(picked.Date) : (LocalDate?)null;
            if (dueDay != dueBefore || created) item = Expenses.WithDue(item, dueDay);
            if (category != categoryBefore) item = ExpenseCategories.With(trip, item, category);
            if (splitMethod is SplitMethod.Equal && sharing.Count > 0 && (created || splitTouched || !sharing.SetEquals(sharingBefore))) item = Expenses.WithSplit(item, SplitMethod.Equal, SplitPeople().Select(p => (p, (decimal?)null)));
            else if (splitMethod is { } method and not SplitMethod.Equal && sharing.Count > 0 && (created || splitTouched || !sharing.SetEquals(sharingBefore)))
                item = Expenses.WithSplit(item, method, SplitPeople().Select(p => (p, ExchangeRates.Amount(SplitBox(p).Text))));
            // The day it was paid, for a paid one; a payment still to pay gets it when it is marked as paid.
            var paidDay = estimated.IsChecked != true && paidOn.SelectedDate is { } chosenDay ? LocalDate.FromDateTime(chosenDay.Date) : (LocalDate?)null;
            if (paidDay != paidOnBefore) item = Expenses.WithDate(item, paidDay);
            if (cash && Currency() != ChargeCurrency() && TripSummaries.Money(item.Data["amount"]) is { Amount: not 0 })
            {
                // The rate the cash cost; a corrected amount keeps it, and the amount in the trip's currency follows.
                var rate = CashForm().Parse(cashRate.Text);
                if (rate is { } cost && (!cashBefore || Changed(cashRate.Text, cashRateBefore)))
                    item = Expenses.WithCash(item, ChargeCurrency(), cost, Expenses.ChargeOf(item)?.Date ?? Expenses.DayOf(trip, item) ?? s.PhoneToday);
            }
            else if (cashBefore && !cash) item = Expenses.WithoutCharge(item);
            if (!cash && Changed(charged.Text, chargedBefore) && Currency() != ChargeCurrency())
            {
                if (string.IsNullOrWhiteSpace(charged.Text)) item = Expenses.WithoutCharge(item);
                else if (ExchangeRates.Amount(charged.Text) is { } booked && TripSummaries.Money(item.Data["amount"]) is { Amount: not 0 })
                    item = Expenses.WithCharge(item, booked, ChargeCurrency(), Expenses.ChargeOf(item)?.Date ?? Expenses.DayOf(trip, item) ?? s.PhoneToday);
            }
            if (related != relatedBefore)
            {
                // The form shows the first link; others an expense may have stay.
                var links = (item.Data["related"] as System.Text.Json.Nodes.JsonArray)?.DeepClone() as System.Text.Json.Nodes.JsonArray ?? [];
                if (relatedBefore is { } old) foreach (var link in links.Where(l => l?["id"]?.ToString() == old.ToString()).ToArray()) links.Remove(link);
                if (related is { } now && trip.Find(now) is { } target) links.Insert(0, new System.Text.Json.Nodes.JsonObject { ["id"] = now.ToString(), ["type"] = target.Type });
                DetailEdits.Set(item, "related", links.Count == 0 ? null : links);
            }
        }
        if (kind == "budget")
        {
            if (money is { } value && !created) DetailEdits.SetMoney(item, "amount", value, Currency());
            if (created || budgetFilters != budgetFiltersBefore) item = Budgets.WithFilters(trip, item, budgetFilters);
        }
        if (kind == "booking")
        {
            if (created || Changed(provider.Text, providerBefore)) DetailEdits.SetText(item, "provider/name", provider.Text);
            if (item.Data["provider"] is System.Text.Json.Nodes.JsonObject { Count: 0 }) item.Data.Remove("provider");
            if (created || Changed(reference.Text, referenceBefore)) DetailEdits.SetText(item, "reference", reference.Text);
            item.Data["status"] = bookingStatus;
            var zone = item.Data["cancellation"]?["free_until"]?["timezone"]?.ToString()
                ?? plans.Select(p => s.Entry(p)?.Zone).OfType<string>().FirstOrDefault() ?? ScheduleQueries.TripZone(trip).Id;
            // A day without an hour lasts until its end.
            DetailEdits.SetFreeCancellation(item, cancelDay.SelectedDate is { } d ? LocalDate.FromDateTime(d.Date).At(Time(cancelTime) ?? new LocalTime(23, 59)) : null, zone);
            if (!plans.SetEquals(plansBefore))
                foreach (var changed in TripCommands.LinkBooking(trip, id, [.. plans]))
                    if (changed.Id == id) DetailEdits.Set(item, "items", changed.Data["items"]); else edits.Add(new(changed.Id, changed));
        }
        edits.Insert(0, new(item.Id, item));
        await s.SaveDetailsAsync(edits);
    }
}
