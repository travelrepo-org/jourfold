using System.Globalization;
using System.Text;
using Jourfold.Mobile.Views;
using TravelRepo.Core;

namespace Jourfold.Mobile;

public sealed partial class MobileSession
{
    /// <summary>
    /// A day of the plan as plain text for people without Jourfold, such as in a messenger: the day, then each plan with
    /// its local time, place and a map link. Ideas and cancelled plans are left out.
    /// </summary>
    public string DayText(AgendaDay day)
    {
        var trip = Trip!; var text = new StringBuilder();
        text.AppendLine(string.Format(CultureInfo.CurrentCulture, Strings["ShareDayTitle"], Lines.DayTitle(day.Date, this), trip.Manifest.Title));
        foreach (var entry in day.Entries.Where(e => e.Role is AgendaRole.Item or AgendaRole.CheckIn && !e.IsIdea && e.Status != "cancelled").SelectMany(e => e.IsGroup ? e.Steps : [e]))
        {
            var time = TimeText.Local(entry, PhoneZone, Strings, trip);
            var when = entry.IsTimed ? time.Primary + (time.Arrival is { } arrival ? " – " + arrival : "") : Strings["AllDay"];
            var where = Lines.Where(trip, entry.Item);
            text.AppendLine().Append(when).Append("  ").Append(entry.Item.Title);
            if (where.Length > 0) text.Append(" · ").Append(where);
            var place = ScheduleQueries.Route(trip, entry.Item).To ?? ScheduleQueries.PrimaryPlace(trip, entry.Item);
            if (Places.Location(place) is { } at)
                text.AppendLine().Append("  https://www.openstreetmap.org/?mlat=").Append(at.Latitude.ToString("0.#####", CultureInfo.InvariantCulture))
                    .Append("&mlon=").Append(at.Longitude.ToString("0.#####", CultureInfo.InvariantCulture)).Append("#map=17/")
                    .Append(at.Latitude.ToString("0.#####", CultureInfo.InvariantCulture)).Append('/').Append(at.Longitude.ToString("0.#####", CultureInfo.InvariantCulture));
        }
        return text.ToString().TrimEnd();
    }

    /// <summary>Hands the day's text to another app through the share sheet.</summary>
    public Task ShareDayAsync(AgendaDay day) => Host.ShareText is { } share ? share(Lines.DayTitle(day.Date, this), DayText(day)) : Task.CompletedTask;
}
