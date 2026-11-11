using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using TravelRepo.Core;

namespace Jourfold.Mobile.Views;

/// <summary>
/// Everything that is not a daily place: bookings, tasks, places and costs of the trip, the travellers, sync and versions,
/// and the app's settings. Each row says the most useful thing about its page.
/// </summary>
public static class MoreView
{
    public static Control Build(MobileSession s, WidthClass width)
    {
        var trip = s.Trip!; var str = s.Strings;
        var bookings = trip.Entities.Values.Count(e => e.Type == "booking");
        var openTasks = OpenTasks(trip);
        var currencies = TripSummaries.Costs(trip).Select(c => c.Currency).ToArray();
        var people = trip.Entities.Values.Count(e => e.Type == "person");
        var places = trip.Entities.Values.Count(e => e.Type == "place");
        var files = trip.Entities.Values.Count(e => e.Type == "document");
        var page = Ui.V(18, Lines.TripHeader(s),
            Group(str["TripTools"],
                Row(s, "bookings", "ticket", str["Bookings"], string.Format(CultureInfo.CurrentCulture, str["CountBookings"], bookings)),
                Row(s, "tasks", "square-check", str["Tasks"], string.Format(CultureInfo.CurrentCulture, str["CountOpen"], openTasks)),
                Row(s, "packing", "backpack", str["Packing"], s.OpenPacking > 0 ? string.Format(CultureInfo.CurrentCulture, str["PackingOpen"], s.OpenPacking) : null),
                Row(s, "places", "map-pin", str["Places"], string.Format(CultureInfo.CurrentCulture, str["CountPlaces"], places)),
                Row(s, "files", "paperclip", str["Files"], string.Format(CultureInfo.CurrentCulture, str["CountFiles"], files)),
                Row(s, "costs", "wallet", str["Costs"], Jourfold.UI.CostTexts.PaidSummary(trip, s.TripCurrencyCode, s.EstimateRates) ?? (currencies.Length > 0 ? string.Join(" + ", currencies) : str["NoCostsYet"])),
                Row(s, "converter", "coins", str["Converter"], s.Rates is { } rates ? string.Format(CultureInfo.CurrentCulture, str["RatesFromShort"], rates.Date.ToString(Formats.DayPattern("d MMM", CultureInfo.GetCultureInfo(str.Language)), CultureInfo.GetCultureInfo(str.Language))) : null)),
            Group(str["TripTogether"],
                Row(s, "trip", "pencil", str["TripDetails"], Formats.DateRange(TripQueries.EffectiveDates(trip).Start, TripQueries.EffectiveDates(trip).End) is { Length: > 0 } range ? range : str["NoDatesYet"]),
                Row(s, "travellers", "users", str["Travellers"], string.Format(CultureInfo.CurrentCulture, str["CountPeople"], people)),
                Row(s, "sync", "refresh-cw", str["SyncAndVersions"], s.HasChanges ? str["ChangesNotInVersion"] : str["SavedOnPhone"])),
            Group(str["App"],
                Row(s, "settings", "settings", str["Settings"], null)));
        page.Margin = new Thickness(0, 0, 0, 24);
        return Ui.Scroll(page);
    }

    private static Control Group(string title, params Control[] rows)
    {
        var list = Ui.V(0);
        for (var i = 0; i < rows.Length; i++) { if (i > 0) list.Children.Add(Ui.Divider().Margin(52, 0, 0, 0)); list.Children.Add(rows[i]); }
        return Ui.V(8, Ui.Text(title, "overline").Margin(20, 0), new Border { Child = list, ClipToBounds = true }.Classed("card").Margin(16, 0));
    }

    private static Control Row(MobileSession s, string page, string icon, string title, string? detail)
    {
        var content = Ui.Columns("Auto,*,Auto", Ui.Icon(icon, 20, "Text.Muted").Margin(0, 0, 16, 0), Ui.V(1, Ui.Text(title, "strong"), detail is null ? null : Ui.Text(detail, "caption")), Ui.Icon("chevron-right", 18, "Text.Subtle"))
            .Also(g => g.MinHeight = 40);
        var row = new Button { Content = content }.Classed("row");
        Avalonia.Automation.AutomationProperties.SetName(row, title);
        row.Click += (_, _) => s.ShowPage(page);
        return row;
    }

    /// <summary>Tasks not done or cancelled yet; packing items count on their lists, not here.</summary>
    public static int OpenTasks(TripSnapshot trip) => trip.Entities.Values.Count(e => e.Type == "task" && e.Data["status"]?.ToString() is not ("done" or "completed" or "cancelled") && !PackingLists.IsItem(e));
}
