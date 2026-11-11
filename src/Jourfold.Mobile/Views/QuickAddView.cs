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
    private string? providerBefore;
    // A currency not offered as a chip, such as the one of a country just arrived in.
    private readonly TextBox otherCurrency = new() { MaxLength = 3, Width = 96, HorizontalAlignment = HorizontalAlignment.Left };
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
        if (item is null) { into = s.QuickAddInto; if (s.QuickAddKind is { } preset && Kinds.Any(k => k.Kind == preset)) kind = preset; }
        currency = TripSummaries.Costs(trip).FirstOrDefault()?.Currency ?? "EUR";
        title.PlaceholderText = str["QuickAddTitleHint"]; AutomationProperties.SetName(title, str["Title"]);
        AutomationProperties.SetName(start, str["StartTime"]); AutomationProperties.SetName(end, str["EndTime"]);
        place.ItemsSource = from.ItemsSource = trip.Entities.Values.Where(e => e.Type == "place").Select(p => p.Title).Order(StringComparer.CurrentCulture).ToArray();
        place.PlaceholderText = from.PlaceholderText = str["PlaceOptional"]; AutomationProperties.SetName(place, str["Place"]); AutomationProperties.SetName(from, str["From"]);
        body.PlaceholderText = str["Notes"]; AutomationProperties.SetName(body, str["Notes"]);
        // With the decimal separator that amounts are shown with (Formats.Money).
        amount.PlaceholderText = 0m.ToString("0.00", CultureInfo.CurrentCulture); AutomationProperties.SetName(amount, str["Amount"]);
        idea.OnContent = idea.OffContent = str["OnlyAnIdea"];
        estimated.OnContent = estimated.OffContent = str["EstimatedOnly"]; AutomationProperties.SetName(estimated, str["EstimatedOnly"]);
        provider.PlaceholderText = str["ProviderHint"]; AutomationProperties.SetName(provider, str["Provider"]);
        reference.PlaceholderText = str["BookingNumberHint"]; AutomationProperties.SetName(reference, str["BookingNumber"]);
        otherCurrency.PlaceholderText = str["OtherCurrencyHint"]; AutomationProperties.SetName(otherCurrency, str["OtherCurrency"]);
        AutomationProperties.SetName(cancelDay, str["FreeCancellationUntil"]); AutomationProperties.SetName(cancelTime, str["FreeCancellationUntil"]);
        if (item is null)
        {
            // A new step is for the group's people; a new expense was paid by the user.
            if (into is not null) peopleChoice = PeopleChoice.AsGroup;
            else if (s.Me is { } me && kind is "expense") { peopleChoice = PeopleChoice.Chosen; people.Add(me.Id); }
            if (into is not null && trip.Find(into.Value) is { } group && s.Entry(group.Id)?.Day is { } groupDay) day = groupDay;
        }
        day = s.Agenda.Find(s.SelectedDate) is not null ? s.SelectedDate : s.Agenda.Find(s.PhoneToday) is not null ? s.PhoneToday : s.Agenda.Days.FirstOrDefault()?.Date;
        if (item is not null) Load(item);

        var close = Ui.IconButton("x", str["Close"]); close.Click += (_, _) => s.CloseOverlay();
        var heading = item is not null ? str["Edit"] : into is { } g && trip.Find(g) is { } groupItem ? string.Format(CultureInfo.CurrentCulture, str["NewStepIn"], groupItem.Title) : str["QuickAdd"];
        Header = Ui.Columns("*,Auto", Ui.Text(heading, "h3").Also(t => t.TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis), close).Margin(16, 0, 8, 8);
        var add = Ui.Button(item is null ? str["Add"] : str["Save"], item is null ? "plus" : "check", "primary").Also(b => b.HorizontalAlignment = HorizontalAlignment.Stretch);
        add.Click += async (_, _) => await SubmitAsync(add);
        View = Ui.V(14, form, problem, add).Margin(16, 0, 16, 24);
        Render();
        title.AttachedToVisualTree += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(() => title.Focus(), Avalonia.Threading.DispatcherPriority.Background);
    }

    private static TimePicker Clock() => new() { ClockIdentifier = "24HourClock", MinuteIncrement = 5, HorizontalAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Center };

    private static TimeSpan? Span(LocalTime? time) => time?.ToTimeOnly().ToTimeSpan();
    private static LocalTime? Time(TimePicker picker) => picker.SelectedTime is { } t ? LocalTime.FromTimeOnly(TimeOnly.FromTimeSpan(t)) : null;

    private void Load(Entity item)
    {
        var entry = s.Entry(item.Id);
        title.Text = item.Title; body.Text = item.Data["body"]?.ToString();
        // Only plan items have a day and a time; bookings, tasks and notes edit their title and text.
        kind = item.Type switch { "schedule_item" => Stays.IsStay(item) ? "accommodation" : "activity", "expense" => "expense", "task" => "task", "booking" => "booking", _ => "note" };
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

    private void Render()
    {
        // A field below the title that had the focus gets it back, so a chip tapped while typing keeps the keyboard.
        var focused = TopLevel.GetTopLevel(form)?.FocusManager?.GetFocusedElement() as TextBox;
        Draw();
        if (focused is not null && !ReferenceEquals(focused, title) && Avalonia.VisualTree.VisualExtensions.IsAttachedToVisualTree(focused)) focused.Focus();
    }

    private void Draw()
    {
        var str = s.Strings;
        if (form.Children.Count == 0) { if (editing is null) form.Children.Add(kinds); form.Children.Add(title); form.Children.Add(rest); }
        rest.Children.Clear();
        // The fields keep what was typed between draws; they leave the layout of the last draw first.
        foreach (var field in new Control[] { start, end, place, from, idea, body, amount, arrival, checkOut, windowFrom, windowUntil, latestOut, reference, provider, cancelDay, cancelTime, estimated, otherCurrency }.Concat(details.Values.Select(d => d.Box)))
            switch (field.Parent)
            {
                case Panel panel: panel.Children.Remove(field); break;
                case ContentControl content: content.Content = null; break;
                case Decorator decorator: decorator.Child = null; break;
            }
        if (editing is null)
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
                    Render();
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
            rest.Children.Add(estimated);
            if (PeopleField(str["Payer"]) is { } payers) rest.Children.Add(payers);
            rest.Children.Add(Field(str["BelongsTo"], RelatedControls()));
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

    /// <summary>The amount, the trip's currencies as chips and a field for any other code.</summary>
    private Control Money()
    {
        var currencies = Ui.Flow(8);
        var offered = TripSummaries.Costs(s.Trip!).Select(t => t.Currency).Prepend(currency).Append("EUR").Distinct().ToArray();
        foreach (var c in offered)
        {
            var chip = Chip(c, null, c == currency && string.IsNullOrWhiteSpace(otherCurrency.Text), "chip"); var chosen = c;
            chip.Click += (_, _) => { currency = chosen; otherCurrency.Text = ""; Render(); };
            currencies.Children.Add(chip);
        }
        currencies.Children.Add(otherCurrency);
        return Ui.V(8, amount, currencies);
    }

    /// <summary>The currency typed or chosen, as a three-letter code.</summary>
    private string Currency() => otherCurrency.Text?.Trim().ToUpperInvariant() is { Length: > 0 } typed ? typed : currency;

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
        rest.Children.Add(Field(str["Price"], Money()));
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
            if (kind is "expense" or "booking" && !(kind == "booking" && string.IsNullOrWhiteSpace(amount.Text)))
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
                Currency = kind is "expense" or "booking" ? Currency() : null,
                Reference = kind == "booking" ? reference.Text : null,
                People = peopleChoice == PeopleChoice.Chosen ? [.. people] : [],
                Date = Scheduled || kind == "task" ? day : null,
                Nights = nights,
                ArrivalDate = Journey && Time(arrival) is not null && day is { } d0 ? d0.PlusDays(arrivalDays) : null,
                ArrivalTime = Journey ? Time(arrival) : null,
                ArrivalTimezone = Journey ? arrivalZoneId : null,
            };
            var added = await s.AddAsync(draft, idea: kind == "idea" || (Scheduled && idea.IsChecked == true), into);
            if (kind is "booking" or "expense") await SaveOtherAsync(added, money, created: true);
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
            if (related != relatedBefore)
            {
                // The form shows the first link; others an expense may have stay.
                var links = (item.Data["related"] as System.Text.Json.Nodes.JsonArray)?.DeepClone() as System.Text.Json.Nodes.JsonArray ?? [];
                if (relatedBefore is { } old) foreach (var link in links.Where(l => l?["id"]?.ToString() == old.ToString()).ToArray()) links.Remove(link);
                if (related is { } now && trip.Find(now) is { } target) links.Insert(0, new System.Text.Json.Nodes.JsonObject { ["id"] = now.ToString(), ["type"] = target.Type });
                DetailEdits.Set(item, "related", links.Count == 0 ? null : links);
            }
        }
        if (kind == "booking")
        {
            if (created || Changed(provider.Text, providerBefore)) DetailEdits.SetText(item, "provider/name", provider.Text);
            if (item.Data["provider"] is System.Text.Json.Nodes.JsonObject { Count: 0 }) item.Data.Remove("provider");
            if (created || Changed(reference.Text, referenceBefore)) DetailEdits.SetText(item, "reference", reference.Text);
            item.Data["status"] = bookingStatus;
            DetailEdits.SetMoney(item, "price", money, Currency());
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
