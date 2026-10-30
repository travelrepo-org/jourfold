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
/// its own zone, number, terminal, gate and seat; a stay's nights, hours and access; the booking number.
/// </summary>
public sealed class QuickAddView
{
    private static readonly (string Kind, string Icon, string Label)[] Kinds =
        [("activity", "star", "quick.activity"), ("food", "utensils", "quick.food"), ("transport", "train-front", "quick.transport"), ("accommodation", "bed-double", "quick.accommodation"),
         ("idea", "lightbulb", "idea"), ("task", "square-check", "quick.task"), ("note", "sticky-note", "quick.note"), ("expense", "receipt", "quick.expense")];

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
    // A journey's arrival: the time, how many days after the departure day, and the zone it is read in.
    private readonly TimePicker arrival = new() { ClockIdentifier = "24HourClock", MinuteIncrement = 5, HorizontalAlignment = HorizontalAlignment.Stretch };
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

    private static TimePicker Clock() => new() { ClockIdentifier = "24HourClock", MinuteIncrement = 5, HorizontalAlignment = HorizontalAlignment.Stretch };

    private static TimeSpan? Span(LocalTime? time) => time?.ToTimeOnly().ToTimeSpan();
    private static LocalTime? Time(TimePicker picker) => picker.SelectedTime is { } t ? LocalTime.FromTimeOnly(TimeOnly.FromTimeSpan(t)) : null;

    private void Load(Entity item)
    {
        var entry = s.Entry(item.Id);
        title.Text = item.Title; body.Text = item.Data["body"]?.ToString();
        // Only plan items have a day and a time; bookings, tasks and notes edit their title and text.
        kind = item.Type switch { "schedule_item" => Stays.IsStay(item) ? "accommodation" : "activity", "expense" => "expense", "task" => "task", _ => "note" };
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
        if (TripSummaries.Money(item.Data["amount"]) is { } money) { amount.Text = money.Amount.ToString(CultureInfo.InvariantCulture); currency = money.Currency; }
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

    private bool Scheduled => kind is "activity" or "food" or "transport" or "accommodation" or "idea";
    private bool Stay => kind == "accommodation";
    private bool Journey => journey || kind == "transport";

    private void Render()
    {
        var str = s.Strings; form.Children.Clear();
        // The fields keep what was typed between draws; they leave the layout of the last draw first.
        foreach (var field in new Control[] { title, start, end, place, from, idea, body, amount, arrival, checkOut, windowFrom, windowUntil, latestOut, reference }.Concat(details.Values.Select(d => d.Box)))
            switch (field.Parent)
            {
                case Panel panel: panel.Children.Remove(field); break;
                case ContentControl content: content.Content = null; break;
                case Decorator decorator: decorator.Child = null; break;
            }
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
        if (kind == "task") { form.Children.Add(Field(str["Due"], Days())); if (editing is not null) form.Children.Add(Field(str["Notes"], body)); return; }
        if (Scheduled) form.Children.Add(Field(Stay ? str["FirstNight"] : str["DayLabel"], Days()));
        if (Scheduled)
        {
            form.Children.Add(Field(Stay ? str["CheckIn"] : Journey ? str["DepartureTime"] : str["StartTime"], Optional(start)));
            if (Stay)
            {
                form.Children.Add(Field(str["Nights"], Stepper(nights, 1, 60, n => nights = n)));
                form.Children.Add(Field(str["CheckOut"], Optional(checkOut)));
            }
            else if (Journey) form.Children.Add(Field(str["ArrivalTime"], ArrivalControls()));
            else form.Children.Add(Field(str["EndTime"], end));
            if (Journey) { form.Children.Add(Field(str["From"], from)); form.Children.Add(Field(str["To"], place)); }
            else form.Children.Add(Field(str["Place"], place));
            if (kind != "idea") form.Children.Add(idea);
            // The details travellers look up on the way, offered once the item exists.
            if (editing is not null && Journey)
            {
                foreach (var (path, label, _) in JourneyFields) if (details.TryGetValue(path, out var d)) form.Children.Add(Field(str[label], d.Box));
                form.Children.Add(Field(str["BookingNumber"], reference));
            }
            if (editing is not null && Stay)
            {
                form.Children.Add(Field(str["CheckInHours"], Ui.Columns("*,Auto,*", Optional(windowFrom), Ui.Text("–").Margin(8, 0).Also(x => x.VerticalAlignment = VerticalAlignment.Center), Optional(windowUntil))));
                form.Children.Add(Field(str["LatestCheckOut"], Optional(latestOut)));
                foreach (var (path, label, _) in StayFields) if (details.TryGetValue(path, out var d)) form.Children.Add(Field(str[label], d.Box));
                form.Children.Add(Field(str["BookingNumber"], reference));
            }
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
            if (editing is null && s.Host.TakePhoto is { } take)
            {
                var photo = Ui.Button(receipt is null ? str["PhotographReceipt"] : str["ReceiptTaken"], receipt is null ? "camera" : "check", "soft").Also(b => b.HorizontalAlignment = HorizontalAlignment.Left);
                photo.Click += async (_, _) => { if (await take(CancellationToken.None) is { } taken) { receipt = taken; Render(); } };
                form.Children.Add(photo);
            }
        }
        if (editing is not null) form.Children.Add(Field(str["Notes"], body));
    }

    private static Control Field(string label, Control value) => Ui.V(6, Ui.Text(label, "overline"), value);

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
            var chip = Ui.Choice(i == 0 ? str["SameDay"] : string.Format(CultureInfo.CurrentCulture, str["DaysLater"], i), null, i == arrivalDays, "chip");
            chip.Click += (_, _) => { arrivalDays = offset; Render(); };
            days.Children.Add(chip);
        }
        var zones = Ui.Flow(6);
        foreach (var zone in ArrivalZones())
        {
            var chip = Ui.Choice(TimeText.Place(DateTimeZoneProviders.Tzdb[zone]), null, zone == (arrivalZone ?? ArrivalZones().First()), "chip"); var chosen = zone;
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
            LocalTime? startTime = Time(start);
            LocalTime? endTime = Journey || Stay ? null : Time(end);
            Duration? length = startTime is { } a && endTime is { } b ? (b > a ? b - a : b - a + Period.FromDays(1)).ToDuration() : null;
            var arrivalZoneId = arrivalZone ?? (Journey ? ArrivalZones().First() : null);
            var placeChoice = Choice(place.Text); var fromChoice = Choice(from.Text);
            decimal? money = null;
            if (kind == "expense")
            {
                if (!decimal.TryParse(amount.Text?.Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out var value)) throw new DomainException("mobile.amount", s.Strings["EnterAmount"]);
                money = value;
            }
            if (editing is not null)
            {
                // A stay's day and hours are its nights; they are saved with the details below.
                await s.SaveEditAsync(editing.Id, title.Text!, body.Text, Scheduled && !Stay || kind == "task" ? day : null, Scheduled && !Stay ? startTime : null, length, money, task: kind == "task");
                // Only places the form changed are written, so an inherited place stays inherited.
                if (editing.Type == "schedule_item" && Changed(place.Text, placeBefore)) await s.AssignPlaceAsync(editing.Id, journey ? PlaceSlot.To : PlaceSlot.Place, placeChoice);
                if (journey && Changed(from.Text, fromBefore)) await s.AssignPlaceAsync(editing.Id, PlaceSlot.From, fromChoice);
                if (editing.Type == "schedule_item") await SaveDetailsAsync(editing.Id, startTime, arrivalZoneId);
                s.ShowDetails(editing.Id);
                return;
            }
            var draft = new QuickAddDraft(kind == "idea" ? "activity" : kind, title.Text!)
            {
                Start = startTime,
                Length = length,
                Place = kind == "transport" ? null : placeChoice,
                From = kind == "transport" ? fromChoice : null,
                To = kind == "transport" ? placeChoice : null,
                Body = kind == "note" ? body.Text : null,
                Amount = money,
                Currency = kind == "expense" ? currency : null,
                Date = Scheduled || kind == "task" ? day : null,
                Nights = nights,
                ArrivalDate = Journey && Time(arrival) is not null && day is { } d0 ? d0.PlusDays(arrivalDays) : null,
                ArrivalTime = Journey ? Time(arrival) : null,
                ArrivalTimezone = Journey ? arrivalZoneId : null,
            };
            var added = await s.AddAsync(draft, idea: kind == "idea" || (Scheduled && idea.IsChecked == true));
            if (receipt is { } photo) await s.AddFilesAsync([photo], added);
        }
        // The form's own checks carry translated text; anything from the domain is looked up by its code.
        catch (DomainException ex) { problem.Text = ex.Code is "mobile.title" or "mobile.amount" ? ex.Message : s.Strings.Diagnostic(ex.Code); problem.IsVisible = true; }
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
}
