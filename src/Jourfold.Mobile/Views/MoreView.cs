using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using TravelRepo.Core;

namespace Jourfold.Mobile.Views;

/// <summary>
/// Everything that is not a daily place: bookings, tasks and costs of the trip, the travellers, sync and versions,
/// and the app's settings. Each row says the most useful thing about its page.
/// </summary>
public static class MoreView
{
    public static Control Build(MobileSession s, WidthClass width)
    {
        var trip = s.Trip!; var str = s.Strings;
        var bookings = trip.Entities.Values.Count(e => e.Type == "booking");
        var openTasks = trip.Entities.Values.Count(e => e.Type == "task" && e.Data["status"]?.ToString() is not ("done" or "completed" or "cancelled") && !PackingLists.IsItem(e));
        var currencies = TripSummaries.Costs(trip).Select(c => c.Currency).ToArray();
        var people = trip.Entities.Values.Count(e => e.Type == "person");
        var page = Ui.V(18, Lines.TripHeader(s),
            Group(str["TripTools"],
                Row("ticket", str["Bookings"], string.Format(CultureInfo.CurrentCulture, str["CountBookings"], bookings)),
                Row("square-check", str["Tasks"], string.Format(CultureInfo.CurrentCulture, str["CountOpen"], openTasks)),
                Row("wallet", str["Costs"], currencies.Length > 0 ? string.Join(" + ", currencies) : str["NoCostsYet"])),
            Group(str["TripTogether"],
                Row("users", str["Travellers"], string.Format(CultureInfo.CurrentCulture, str["CountPeople"], people)),
                Row("refresh-cw", str["SyncAndVersions"], str["SavedOnPhone"])),
            Group(str["App"],
                Row("settings", str["Settings"], null)));
        page.Margin = new Thickness(0, 0, 0, 24);
        return Ui.Scroll(page);
    }

    private static Control Group(string title, params Control[] rows)
    {
        var list = Ui.V(0);
        for (var i = 0; i < rows.Length; i++) { if (i > 0) list.Children.Add(Ui.Divider().Margin(52, 0, 0, 0)); list.Children.Add(rows[i]); }
        return Ui.V(8, Ui.Text(title, "overline").Margin(20, 0), new Border { Child = list, ClipToBounds = true }.Classed("card").Margin(16, 0));
    }

    private static Control Row(string icon, string title, string? detail) =>
        Ui.Columns("Auto,*,Auto", Ui.Icon(icon, 20, "Text.Muted").Margin(0, 0, 16, 0), Ui.V(1, Ui.Text(title, "strong"), detail is null ? null : Ui.Text(detail, "caption")), Ui.Icon("chevron-right", 18, "Text.Subtle"))
            .Also(g => { g.MinHeight = 56; g.Margin = new Thickness(16, 8); });
}
