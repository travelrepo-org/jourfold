using System.Globalization;
using Jourfold.Application;
using Jourfold.UI;
using NodaTime;
using Jourfold.Mobile.Views;
using TravelRepo.Core;

namespace Jourfold.Mobile;

/// <summary>What a notification is about; each kind has its own channel, so users can silence one in the system.</summary>
public enum NoticeKind { Departure, Connection, Arrival, Stay, CheckOut, LeaveBy, Deadline, TaskDue, Packing, Weather }

/// <summary>
/// A notification at a moment, worked out from the plan. <see cref="Key"/> stays the same while the plan does, so a
/// notice is shown once. <see cref="Item"/> is what a tap opens; <see cref="Ticket"/> says whether it has a ticket.
/// </summary>
public sealed record TravelNotice(string Key, NoticeKind Kind, Instant At, string Title, string Text, Guid? Item, bool Ticket = false);

/// <summary>
/// The travel day as one ongoing notification: before a departure it counts down to it, on the way it shows the
/// progress to the arrival, during a change it counts down to the next departure. It ends at the arrival, which has a
/// notice of its own. <see cref="Until"/> is where the
/// countdown or progress goes, <see cref="From"/> where progress starts; <see cref="Changes"/> is when it looks
/// different next.
/// </summary>
public sealed record LiveNotice(string Title, string Text, Instant? From, Instant? Until, Instant Changes, Guid Item, bool Ticket);

/// <summary>
/// Works out the notifications of a trip from its plan alone, offline: departures, changes between journeys, arrivals
/// with what comes next, the stay on its arrival day with its access, check-out the evening before, when to leave for
/// a plan (walking time estimated from the distance), free cancellation and tasks, and the packing list the day before
/// the trip. Only plans that concern the user count (see <see cref="TripMe.Concerns"/>).
/// </summary>
public static class TravelNotices
{
    /// <summary>How far ahead notices are worked out; the plan is read again long before that runs out.</summary>
    public static readonly Duration Horizon = Duration.FromDays(3);
    /// <summary>A notice whose moment has passed by up to this is still shown, such as after the phone was off.</summary>
    public static readonly Duration Late = Duration.FromMinutes(30);
    private const double WalkMetresPerMinute = 75, DetourFactor = 1.3, LongestWalkMetres = 3_000;

    /// <param name="weather">The forecasts kept on the device, for warnings on the morning of a plan; null without weather.</param>
    public static IReadOnlyList<TravelNotice> Plan(TripSnapshot trip, Entity? me, Instant now, DateTimeZone phone, Localization s, TravelRepo.Providers.Weather.TripWeather? weather = null)
    {
        var notices = new List<TravelNotice>();
        var connections = Connections.In(trip);
        var plans = trip.Entities.Values
            .Where(e => e.Type == "schedule_item" && e.Data["children"] is not System.Text.Json.Nodes.JsonArray { Count: > 0 } && e.Data["status"]?.ToString() is not ("cancelled" or "idea") && TripMe.Concerns(trip, e, me))
            .Select(e => (Item: e, Span: ScheduleQueries.Span(trip, e)))
            .Where(p => p.Span.IsPlaced && p.Span.Precision is TimePrecision.Exact or TimePrecision.Approximate or TimePrecision.Window)
            .OrderBy(p => p.Span.Start).ToArray();

        foreach (var (item, span) in plans)
        {
            var start = span.Start!.Value; var end = span.End!.Value;
            var startZone = Zone(span.StartZone, trip); var endZone = Zone(span.EndZone ?? span.StartZone, trip);
            if (item.Data["components"]?["transport"] is not null)
            {
                var flight = item.Data["components"]?["transport"]?["type"]?.ToString() == "flight";
                var ticket = Tickets.For(trip, item).Count > 0;
                var into = connections.FirstOrDefault(c => c.Departing.Id == item.Id);
                var lead = into is not null ? flight ? Duration.FromMinutes(90) : Duration.FromMinutes(30) : flight ? Duration.FromHours(3) : Duration.FromMinutes(45);
                var at = into is not null ? Instant.Max(into.Arrival + Duration.FromMinutes(15), start - lead) : start - lead;
                notices.Add(new("depart:" + item.Id + ":" + start, into is null ? NoticeKind.Departure : NoticeKind.Connection, at,
                    Format(s["NoticeDeparture"], item.Title, Clock(start, startZone, phone)), Details(item, s, departure: true), item.Id, ticket));

                var onward = connections.FirstOrDefault(c => c.Arriving.Id == item.Id);
                string title, text;
                if (onward is not null)
                {
                    title = Format(s["NoticeConnection"], Formats.Duration(onward.Layover, s));
                    var next = ScheduleQueries.Span(trip, onward.Departing);
                    text = Join(Format(s["NoticeDeparture"], onward.Departing.Title, Clock(onward.Departure, Zone(next.StartZone, trip), phone)), Details(onward.Departing, s, departure: true));
                }
                else
                {
                    title = Format(s["NoticeArrived"], ScheduleQueries.Route(trip, item).To?.Title ?? item.Title);
                    text = Join(Format(s["NoticeLocalTime"], end.InZone(endZone).ToString("HH:mm", CultureInfo.InvariantCulture)), Details(item, s, departure: false), WhatsNext(trip, me, end, plans, s, phone));
                }
                notices.Add(new("arrive:" + item.Id + ":" + end, NoticeKind.Arrival, end, title, text, onward?.Departing.Id ?? item.Id));
                continue;
            }
            if (Stays.IsStay(item)) continue;
            // When to leave: the walk from where the user is before, when that is close enough to walk.
            var from = Before(trip, plans, item, start);
            var walk = from is { } a && Places.Location(ScheduleQueries.PrimaryPlace(trip, item)) is { } b ? Places.Distance(a, b) * DetourFactor : (double?)null;
            var minutes = walk is { } metres && metres <= LongestWalkMetres ? (int)Math.Ceiling(metres / WalkMetresPerMinute / 5) * 5 : (int?)null;
            var leave = start - Duration.FromMinutes((minutes ?? 20) + 10);
            notices.Add(new("leave:" + item.Id + ":" + start, NoticeKind.LeaveBy, leave, Format(s["NoticeLeave"], item.Title),
                Join(Format(s["NoticeStartsAt"], Clock(start, startZone, phone)), minutes is { } m ? Format(s["NoticeWalk"], m) : null, Lines.Where(trip, item)), item.Id, Tickets.For(trip, item).Count > 0));
        }

        foreach (var stay in Stays.All(trip).Where(x => x.FirstNight is not null && x.Item.Data["status"]?.ToString() is not ("cancelled" or "idea") && TripMe.Concerns(trip, x.Item, me)))
        {
            var zone = Zone(stay.Zone, trip);
            Instant On(LocalDate day, LocalTime time) => zone.AtLeniently(day.At(time)).ToInstant();
            // On the arrival day: once the journey there has ended, otherwise an hour before check-in.
            var checkIn = stay.CheckIn is { } fixedIn && !stay.CheckInApproximate ? fixedIn.ToInstant() : On(stay.FirstNight!.Value, stay.CheckInWindow?.From ?? Stays.DefaultCheckIn);
            var arriving = plans.Where(p => p.Item.Data["components"]?["transport"] is not null && p.Span.End!.Value.InZone(zone).Date == stay.FirstNight).Select(p => p.Span.End!.Value).DefaultIfEmpty().Max();
            var at = arriving != default ? arriving + Duration.FromMinutes(30) : checkIn - Duration.FromHours(1);
            var hours = stay.CheckIn is { } shown && !stay.CheckInApproximate ? Format(s["CheckInAt"], Clock(checkIn, zone, phone))
                : stay.CheckInWindow is { From: { } open } window ? window.Until is { } until ? Format(s["CheckInBetween"], Hhmm(open), Hhmm(until)) : Format(s["CheckInFromTime"], Hhmm(open)) : null;
            notices.Add(new("stay:" + stay.Item.Id + ":" + stay.FirstNight, NoticeKind.Stay, at, Format(s["NoticeStay"], stay.Item.Title),
                Join(hours, stay.Access, Lines.Where(trip, stay.Item)), stay.Item.Id));
            // The evening before leaving.
            if (stay.DepartureDay is { } leaving)
            {
                var by = stay.CheckOut is { } fixedOut && !stay.CheckOutApproximate ? Hhmm(fixedOut.ToInstant().InZone(zone).TimeOfDay) : stay.CheckOutWindow?.Until is { } latest ? Hhmm(latest) : null;
                notices.Add(new("checkout:" + stay.Item.Id + ":" + leaving, NoticeKind.CheckOut, On(leaving.PlusDays(-1), new LocalTime(20, 0)), Format(s["NoticeCheckOut"], stay.Item.Title),
                    by is null ? "" : Format(s["CheckOutByTime"], by), stay.Item.Id));
            }
        }

        // Free cancellation two days before it ends, tasks on the morning they are due.
        foreach (var reminder in TripReminders.For(trip, now + Duration.FromDays(2)).Where(r => r.Kind == ReminderKind.CancellationDeadline && r.Moment is not null))
            notices.Add(new("deadline:" + reminder.Key, NoticeKind.Deadline, reminder.Moment!.Value - Duration.FromDays(2), Format(s["NoticeDeadline"], reminder.Title),
                Format(s["NoticeDeadlineText"], reminder.Moment.Value.InZone(phone).ToString(s["DayShortFormat"] + " HH:mm", CultureInfo.GetCultureInfo(s.Language))), reminder.Entity));
        foreach (var reminder in TripReminders.For(trip, now + Duration.FromDays(1)).Where(r => r.Kind == ReminderKind.TaskDue && r.Dates.Count > 0))
        {
            var task = trip.Find(reminder.Entity!.Value);
            if (task is not null && me is not null && task.Data["assigned_to"] is System.Text.Json.Nodes.JsonArray { Count: > 0 } assigned && !assigned.Any(a => a?.ToString() == me.Id.ToString())) continue;
            notices.Add(new(reminder.Key, NoticeKind.TaskDue, phone.AtLeniently(reminder.Dates[0].At(new LocalTime(9, 0))).ToInstant(), Format(s["NoticeTask"], reminder.Title), "", reminder.Entity));
        }

        // Payments still to pay, three mornings before they are due, for the people who share them.
        foreach (var payment in trip.Entities.Values.Where(e => e.Type == "expense" && Expenses.IsPlanned(e)))
        {
            if (Expenses.DueOf(payment) is not { } due || Expenses.IsIdea(trip, payment) || (me is not null && !Expenses.Split(trip, payment).People.Contains(me.Id))) continue;
            var amount = TripSummaries.Money(payment.Data["amount"]) is { } money ? Formats.Money(money.Amount, money.Currency) : "";
            notices.Add(new("payment:" + payment.Id + ":" + due, NoticeKind.Deadline, phone.AtLeniently(due.PlusDays(-3).At(new LocalTime(9, 0))).ToInstant(), Format(s["NoticePayment"], payment.Title),
                Format(s["NoticePaymentText"], amount, due.ToString(s["DayShortFormat"], CultureInfo.GetCultureInfo(s.Language))), payment.Id));
        }

        // Weather that matters for a plan, on the morning of its day, or two hours before when that is earlier.
        if (weather is not null)
            foreach (var warning in TravelRepo.Providers.Weather.WeatherAdvice.Reminders(trip, weather, now).Where(r => r.Moment is not null && r.Entity is { } id && trip.Find(id) is { } item && TripMe.Concerns(trip, item, me)))
            {
                var start = warning.Moment!.Value; var morning = phone.AtLeniently(start.InZone(phone).Date.At(new LocalTime(7, 30))).ToInstant();
                var concerns = weather.Items.TryGetValue(warning.Entity!.Value, out var w) ? WeatherDisplay.Concerns(w.Concerns, s, imperial: false) : "";
                notices.Add(new("weather:" + warning.Key, NoticeKind.Weather, Instant.Min(morning, start - Duration.FromHours(2)), Format(s["NoticeWeather"], warning.Title), concerns, warning.Entity));
            }

        // The evening before the trip: what is left to pack.
        if (TripQueries.EffectiveDates(trip).Start is { } first)
        {
            var open = PackingLists.All(trip).SelectMany(l => l.Items).Count(i => me is null ? !i.Done : i.IsFor(me.Id) && !i.IsPackedBy(me.Id));
            if (open > 0) notices.Add(new("packing:" + first, NoticeKind.Packing, phone.AtLeniently(first.PlusDays(-1).At(new LocalTime(18, 0))).ToInstant(), s["NoticePacking"], Format(s["NoticePackingText"], open), null));
        }

        return notices.Where(n => n.At >= now - Late && n.At <= now + Horizon).OrderBy(n => n.At).ToArray();
    }

    /// <summary>
    /// The travel day's ongoing notification at <paramref name="now"/>: from shortly before a departure until the
    /// arrival, through changes between journeys. Null when no journey is close.
    /// </summary>
    public static LiveNotice? Live(TripSnapshot trip, Entity? me, Instant now, DateTimeZone phone, Localization s)
    {
        var connections = Connections.In(trip);
        var journeys = trip.Entities.Values
            .Where(e => e.Type == "schedule_item" && e.Data["components"]?["transport"] is not null && e.Data["status"]?.ToString() is not ("cancelled" or "idea") && TripMe.Concerns(trip, e, me))
            .Select(e => (Item: e, Span: ScheduleQueries.Span(trip, e)))
            .Where(j => j.Span.IsPlaced && j.Span.Precision is TimePrecision.Exact or TimePrecision.Approximate)
            .OrderBy(j => j.Span.Start).ToArray();
        foreach (var (item, span) in journeys)
        {
            var start = span.Start!.Value; var end = span.End!.Value;
            var flight = item.Data["components"]?["transport"]?["type"]?.ToString() == "flight";
            var into = connections.FirstOrDefault(c => c.Departing.Id == item.Id);
            var opens = into?.Arrival ?? start - (flight ? Duration.FromHours(3) : Duration.FromMinutes(45));
            var ticket = Tickets.For(trip, item).Count > 0;
            var startZone = Zone(span.StartZone, trip); var endZone = Zone(span.EndZone ?? span.StartZone, trip);
            if (now < opens || now >= end) continue;
            if (now < start)
                return into is not null
                    ? new(Format(s["NoticeConnection"], Formats.Duration(into.Layover, s)), Join(Format(s["NoticeDeparture"], item.Title, Clock(start, startZone, phone)), Details(item, s, departure: true)), null, start, start, item.Id, ticket)
                    : new(Format(s["NoticeDeparture"], item.Title, Clock(start, startZone, phone)), Details(item, s, departure: true), null, start, start, item.Id, ticket);
            if (now < end)
                return new(Format(s["NoticeArrives"], item.Title, Clock(end, endZone, phone)), Lines.Where(trip, item), start, end, end, item.Id, ticket);
            // Once arrived, the arrival notice says where and what comes next; a change to another journey goes on above.
        }
        return null;
    }

    /// <summary>Terminal, gate, platform and seat at one end of a journey, as far as the plan has them.</summary>
    private static string Details(Entity item, Localization s, bool departure)
    {
        var transport = item.Data["components"]?["transport"]; var end = transport?[departure ? "departure" : "arrival"];
        string? Part(string key, string? value) => string.IsNullOrWhiteSpace(value) ? null : Format(s[key], value);
        return Join(Part("NoticeTerminal", end?["terminal"]?.ToString()), Part("NoticeGate", end?["gate"]?.ToString() ?? end?["platform"]?.ToString()),
            departure ? Part("NoticeSeat", transport?["seat"]?.ToString()) : null);
    }

    /// <summary>The next plan after an arrival: the stay to check in to that day, or the next plan with a time.</summary>
    private static string? WhatsNext(TripSnapshot trip, Entity? me, Instant arrival, (Entity Item, ScheduleSpan Span)[] plans, Localization s, DateTimeZone phone)
    {
        var next = plans.FirstOrDefault(p => p.Span.Start >= arrival && p.Span.Start - arrival <= Duration.FromHours(12));
        var stay = Stays.All(trip).FirstOrDefault(x => x.FirstNight is { } night && TripMe.Concerns(trip, x.Item, me) && night == arrival.InZone(Zone(x.Zone, trip)).Date);
        if (stay is not null && (next.Item is null || Stays.IsStay(next.Item) || next.Span.Start > arrival + Duration.FromHours(4))) return Format(s["NoticeNext"], stay.Item.Title);
        return next.Item is null ? null : Format(s["NoticeNext"], next.Item.Title + " " + Clock(next.Span.Start!.Value, Zone(next.Span.StartZone, trip), phone));
    }

    /// <summary>Where the user is before a plan on its day: the place of the plan before, or the stay of the night before.</summary>
    private static (double Latitude, double Longitude)? Before(TripSnapshot trip, (Entity Item, ScheduleSpan Span)[] plans, Entity item, Instant start)
    {
        var zone = ScheduleQueries.TripZone(trip); var day = start.InZone(zone).Date;
        var previous = plans.LastOrDefault(p => p.Item.Id != item.Id && p.Span.End <= start && p.Span.End!.Value.InZone(zone).Date == day && p.Item.Data["components"]?["transport"] is null);
        if (previous.Item is not null && Places.Location(ScheduleQueries.PrimaryPlace(trip, previous.Item)) is { } there) return there;
        var stay = Stays.All(trip).FirstOrDefault(x => x.Covers(day.PlusDays(-1)) || x.Covers(day));
        return stay is null ? null : Places.Location(stay.Place is { } id ? trip.Find(id) : ScheduleQueries.PrimaryPlace(trip, stay.Item));
    }

    private static DateTimeZone Zone(string? id, TripSnapshot trip) => DateTimeZoneProviders.Tzdb.GetZoneOrNull(id ?? "") ?? ScheduleQueries.TripZone(trip);

    /// <summary>A time in its own zone, with the place when that is not the phone's zone, such as "13:20 Frankfurt".</summary>
    private static string Clock(Instant at, DateTimeZone zone, DateTimeZone phone) =>
        at.InZone(zone).ToString("HH:mm", CultureInfo.InvariantCulture) + (zone.GetUtcOffset(at) == phone.GetUtcOffset(at) ? "" : " " + TimeText.Place(zone));

    private static string Hhmm(LocalTime time) => time.ToString("HH:mm", CultureInfo.InvariantCulture);
    private static string Format(string pattern, params object[] values) => string.Format(CultureInfo.CurrentCulture, pattern, values);
    private static string Join(params string?[] parts) => string.Join(" · ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
}
