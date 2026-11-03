using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using TravelRepo.Core;

namespace Jourfold.Mobile.Views;

/// <summary>
/// Long-press menus. Everything in them can also be reached through the details, so the gesture is a shortcut, not
/// the only way (spec: no other essential hidden gestures). A right click opens the same menu with a mouse.
/// </summary>
public static class Menus
{
    /// <summary>
    /// Runs <paramref name="open"/> on a long press or a right click on the control, such as opening a menu. On a
    /// button neither the tap that ends a long press nor a finger that moved counts as a click, so call this before
    /// adding its click handler.
    /// </summary>
    public static void OnHold(Control button, Action open)
    {
        // Timed here rather than with Avalonia's holding gesture, which does not fire for touch on Android.
        var held = false; var moved = false; Point? from = null, pressed = null; IDisposable? timer = null;
        void Stop() { timer?.Dispose(); timer = null; from = null; }
        button.AddHandler(InputElement.PointerPressedEvent, (_, e) =>
        {
            held = false; moved = false; pressed = null; Stop();
            if (e.Pointer.Type == PointerType.Mouse) return;
            from = pressed = e.GetPosition(button); var pointer = e.Pointer;
            timer = DispatcherTimer.RunOnce(() =>
            {
                timer = null; from = null;
                // A swipe that took the finger over, or a page that changed meanwhile, is not a long press here.
                if (!button.IsAttachedToVisualTree() || pointer.Captured is Visual taken && taken != button && !button.IsVisualAncestorOf(taken)) return;
                held = true; open();
            }, TimeSpan.FromMilliseconds(500));
        }, RoutingStrategies.Tunnel, handledEventsToo: true);
        // Moving the finger is a scroll or a swipe, not a long press, and not a tap either: where nothing scrolls, such
        // as at the end of a list, the button keeps the finger and would take letting go over it as a click.
        button.AddHandler(InputElement.PointerMovedEvent, (_, e) =>
        {
            if (pressed is not { } start || Point.Distance(start, e.GetPosition(button)) <= 10) return;
            moved = true; pressed = null; Stop();
        }, RoutingStrategies.Tunnel, handledEventsToo: true);
        button.AddHandler(InputElement.PointerReleasedEvent, (_, _) => Stop(), RoutingStrategies.Tunnel, handledEventsToo: true);
        // The button clicks while the release passes it, so afterwards a click from TalkBack or the keyboard counts again.
        button.AddHandler(InputElement.PointerReleasedEvent, (_, _) => moved = false, RoutingStrategies.Bubble, handledEventsToo: true);
        button.AddHandler(InputElement.PointerCaptureLostEvent, (_, _) => Stop(), RoutingStrategies.Bubble, handledEventsToo: true);
        button.DetachedFromVisualTree += (_, _) => Stop();
        button.ContextRequested += (_, e) => { if (!held) open(); e.Handled = true; };
        button.AddHandler(Button.ClickEvent, (_, e) => { if (held || moved) { held = false; moved = false; e.Handled = true; } });
    }

    /// <summary>
    /// Copies <paramref name="value"/> on a long press or a right click on the text, such as a flight number or an
    /// address. Without a value, the text itself is copied. Android 13 and later confirm a copy themselves; elsewhere a
    /// short notice says what was copied.
    /// </summary>
    public static T Copies<T>(this T text, MobileSession s, string? value = null) where T : TextBlock
    {
        var copy = value ?? text.Text;
        if (string.IsNullOrWhiteSpace(copy)) return text;
        // Hits the whole line, not only the letters.
        text.Background ??= Avalonia.Media.Brushes.Transparent;
        OnHold(text, async () =>
        {
            if (TopLevel.GetTopLevel(text)?.Clipboard is not { } clipboard) return;
            await clipboard.SetTextAsync(copy);
            if (OperatingSystem.IsAndroidVersionAtLeast(33)) return;
            var notice = copy.Length <= 40 && !copy.Contains('\n') ? string.Format(CultureInfo.CurrentCulture, s.Strings["CopiedValue"], copy) : s.Strings["Copied"];
            Avalonia.VisualTree.VisualExtensions.FindAncestorOfType<AppShell>(text)?.Notify(notice);
        });
        return text;
    }

    /// <summary>What can be done with an item: the same as in its details, plus duplicating and moving it.</summary>
    public static ActionMenu Item(MobileSession s, Entity item)
    {
        var str = s.Strings; var trip = s.Trip!;
        var actions = new List<MenuAction>
        {
            new(str["ShowDetails"], "info", _ => { s.ShowDetails(item.Id); return Task.CompletedTask; }),
            new(str["Edit"], "pencil", _ => { s.ShowEditor(item.Id); return Task.CompletedTask; })
        };
        if (item.Type == "schedule_item") actions.Add(new(str["ChangeStatus"], "check", _ => { s.ShowMenu(Status(s, item)); return Task.CompletedTask; }));
        if (Navigation.Target(trip, item) is { } target)
            actions.Add(new(str["Navigate"], "navigation", async top => { if (top?.Launcher is { } launcher) await launcher.LaunchUriAsync(Navigation.Link(target.Lat, target.Lon, target.Name, target.Query)); }));
        if (Lines.BookingReference(trip, item) is { Length: > 0 } reference)
            actions.Add(new(string.Format(CultureInfo.CurrentCulture, str["CopyReferenceValue"], reference), "copy", async top => { if (top?.Clipboard is { } clipboard) await clipboard.SetTextAsync(reference); }));
        // A stay's nights are changed in the stay itself; moving a single night would split it.
        if (item.Type == "schedule_item" && item.Data["components"]?["accommodation"] is null)
        {
            actions.Add(new(str["Duplicate"], "files", _ => s.DuplicateAsync(item.Id)));
            if (s.Agenda.Days.Count > 1) actions.Add(new(str["MoveToDay"], "calendar", _ => { s.ShowMenu(Days(s, item)); return Task.CompletedTask; }));
        }
        actions.Add(new(str["Delete"], "trash-2", _ =>
        {
            s.Confirm(string.Format(CultureInfo.CurrentCulture, str["DeleteItemQuestion"], item.Title), str["Delete"], () => s.DeleteAsync(item.Id));
            return Task.CompletedTask;
        }, Danger: true));
        return new(item.Title, s.Entry(item.Id) is { } entry ? TimeText.Local(entry, s.PhoneZone, str, s.Trip).Primary : null, actions);
    }

    private static ActionMenu Status(MobileSession s, Entity item)
    {
        var current = item.Data["status"]?.ToString() ?? "planned";
        var actions = new[] { ("idea", "lightbulb"), ("planned", "calendar"), ("reserved", "clock"), ("confirmed", "check"), ("completed", "check"), ("cancelled", "x") }
            .Select(o => new MenuAction(s.Strings[o.Item1] + (o.Item1 == current ? " ✓" : ""), o.Item2, _ => s.SetStatusAsync(item.Id, o.Item1))).ToArray();
        return new(item.Title, s.Strings["ChangeStatus"], actions);
    }

    private static ActionMenu Days(MobileSession s, Entity item)
    {
        var current = s.Entry(item.Id)?.Day;
        var actions = s.Agenda.Days.Where(d => d.Date != current)
            .Select(d => new MenuAction(Lines.DayShort(d.Date, s.Strings) + (d.Number > 0 ? " · " + string.Format(CultureInfo.CurrentCulture, s.Strings["DayN"], d.Number) : ""), "calendar",
                _ => s.MoveToDayAsync(item.Id, d.Date))).ToArray();
        return new(item.Title, s.Strings["MoveToDay"], actions);
    }

    /// <summary>A trip on the list: open it, or remove it from this phone after a question that says what is lost.</summary>
    public static ActionMenu Trip(MobileSession s, TripCard trip)
    {
        var str = s.Strings; var title = trip.Trip.Manifest.Title;
        return new(title, Formats.DateRange(trip.Start, trip.End) is { Length: > 0 } dates ? dates : null,
        [
            new(str["Open"], "folder-open", _ => s.OpenAsync(trip.Path)),
            new(str["DeleteFromPhone"], "trash-2", async _ =>
            {
                var shared = await s.IsSharedAsync(trip.Path);
                s.Confirm(string.Format(CultureInfo.CurrentCulture, str[shared ? "DeleteSharedTripQuestion" : "DeleteLocalTripQuestion"], title), str["Delete"], () => s.DeleteTripAsync(trip.Path));
            }, Danger: true)
        ]);
    }
}
