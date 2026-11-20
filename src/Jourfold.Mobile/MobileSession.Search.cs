using TravelRepo.Core;

namespace Jourfold.Mobile;

/// <summary>
/// The search over the open trip. Finding is TravelRepo's <see cref="TripSearch"/>; the session keeps how the results
/// are shown and jumps to where a result is.
/// </summary>
public sealed partial class MobileSession
{
    /// <summary>The key of the search field's text in <see cref="Drafts"/>, which stays while the trip is open.</summary>
    public const string SearchDraft = "search";

    /// <summary>Opens the search with its field ready to type in; back returns to where it was opened from.</summary>
    public void ShowSearch()
    {
        if (Workspace is null) return;
        if (Page == "search") { FocusDraft = SearchDraft; Changed?.Invoke(); return; }
        Showing = Overlay.None; OverlayItem = null; detailTrail.Clear();
        FocusDraft = SearchDraft;
        OpenPage("search");
    }

    /// <summary>Whether note texts are searched too. Off at first; the choice is remembered on this device.</summary>
    public bool SearchNotes
    {
        get => Settings.Values.GetValueOrDefault("search.notes") == "on";
        set { Settings.Values["search.notes"] = value ? "on" : "off"; Settings.Save(); }
    }

    /// <summary>Whether results are grouped by day (the default) or by kind; remembered on this device.</summary>
    public bool SearchByDay
    {
        get => Settings.Values.GetValueOrDefault("search.group") != "kind";
        set { Settings.Values["search.group"] = value ? "day" : "kind"; Settings.Save(); }
    }

    /// <summary>The one kind the results are limited to, or null for all; kept while the trip is open.</summary>
    public SearchKind? SearchOnly { get; set; }

    /// <summary>The item a jump from the search led to, marked on its page until the user goes elsewhere.</summary>
    public Guid? Marked { get; private set; }
    private bool markToScroll;

    /// <summary>Whether the marked item still has to be scrolled into view; true once after each jump.</summary>
    public bool TakeMarkScroll() { var take = markToScroll; markToScroll = false; return take; }

    /// <summary>A position the map moves to once, after a jump to a place no plan of the shown day uses.</summary>
    private (double Latitude, double Longitude)? mapCenter;
    public (double Latitude, double Longitude)? TakeMapCenter() { var take = mapCenter; mapCenter = null; return take; }

    /// <summary>
    /// The name of the jump from a search result to where it is, as a key of the strings: the plan for a plan, the
    /// bookings and costs pages, the map for a place, the tasks, the packing list. Null for what has no place of its
    /// own, such as a note or a file.
    /// </summary>
    public string? ShowInPlaceKey(Entity entity) => entity.Type switch
    {
        "schedule_item" => "SearchShowPlan",
        "booking" => "SearchShowBookings",
        "expense" => "SearchShowCosts",
        "place" when Places.Location(entity) is not null || PlanAt(entity.Id) is not null => "SearchShowMap",
        "task" => PackingLists.IsItem(entity) ? "SearchShowPacking" : "SearchShowTasks",
        "collection" when PackingLists.IsList(entity) => "SearchShowPacking",
        _ => null
    };

    /// <summary>The first plan with a day at a place, with that day.</summary>
    private (AgendaDay Day, AgendaEntry Entry)? PlanAt(Guid place)
    {
        foreach (var day in Agenda.Days)
            foreach (var entry in day.Entries.SelectMany(e => e.Steps.Prepend(e)))
            {
                var (from, to) = ScheduleQueries.Route(Trip!, entry.Item);
                if (from?.Id == place || to?.Id == place || ScheduleQueries.PrimaryPlace(Trip!, entry.Item)?.Id == place) return (day, entry);
            }
        return null;
    }

    /// <summary>
    /// Jumps from a search result to where it is: a plan to its day in the plan, marked; a booking or a payment to
    /// its page with the row unfolded; a place to the map; a task to the tasks; a packing item to its list. Back
    /// returns to the search, with the same words and results.
    /// </summary>
    public void ShowInPlace(Guid id)
    {
        if (Trip?.Find(id) is not { } entity || ShowInPlaceKey(entity) is null) return;
        Showing = Overlay.None; OverlayItem = null; PlaceFor = null; detailTrail.Clear(); Menu = null;
        if (entity.Type == "task" && PackingLists.IsItem(entity) || entity.Type == "collection") { ShowPacking(id); return; }
        trail.Add((Tab, Page)); pageAsPlace = false;
        Marked = id; markToScroll = true;
        switch (entity.Type)
        {
            case "schedule_item":
                // A step of a group shows inside its group, which is opened for it.
                var top = Agenda.Days.SelectMany(d => d.Entries.Select(e => (Day: d, Entry: e))).FirstOrDefault(x => x.Entry.Item.Id == id || x.Entry.Steps.Any(step => step.Item.Id == id));
                if (top.Entry is not null) { SelectedDate = top.Day.Date; if (top.Entry.Item.Id != id) expanded.Add(top.Entry.Item.Id); }
                SelectedItem = id; Tab = MobileTab.Plan; Page = null;
                break;
            case "booking": expanded.Add(id); Tab = MobileTab.More; Page = "bookings"; break;
            case "expense":
                // The page's own search would hide the row.
                Drafts.Remove("costs.search");
                expanded.Add(id); Tab = MobileTab.More; Page = "costs"; break;
            case "task": Tab = MobileTab.More; Page = "tasks"; break;
            case "place":
                if (PlanAt(id) is { } at) { SelectedDate = at.Day.Date; SelectedItem = at.Entry.Item.Id; }
                else mapCenter = Places.Location(entity);
                Tab = MobileTab.Map; Page = null;
                break;
        }
        Changed?.Invoke();
    }

    /// <summary>The mark of a jump goes when the user moves on.</summary>
    private void Unmark() { Marked = null; markToScroll = false; }
}
