using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Jourfold.Mobile.Controls;
using TravelRepo.Core;

namespace Jourfold.Mobile.Views;

/// <summary>
/// Long-press menus. Everything in them can also be reached through the details, so the gesture is a shortcut, not
/// the only way (spec: no other essential hidden gestures). A right click opens the same menu with a mouse.
/// </summary>
public static class Menus
{
    /// <summary>
    /// Runs <paramref name="open"/> on a long press or a right click on the control, such as opening a menu. A long
    /// press counts when the finger is lifted after <see cref="HoldGesture.Threshold"/> without having moved; the control
    /// looks pressed more strongly and the device gives a short vibration once that time is reached, so people know they
    /// can let go. On a button neither the tap that ends a long press nor a finger that moved counts as a click, so call
    /// this before adding its click handler.
    /// </summary>
    public static void OnHold(Control button, Action open)
    {
        // Read here rather than with Avalonia's holding gesture, which does not fire for touch on Android.
        var gesture = new HoldGesture(); var held = false; var moved = false; IDisposable? timer = null;
        void Settle() { timer?.Dispose(); timer = null; button.Classes.Remove("held"); }
        void Cancel() { gesture.Cancel(); Settle(); }
        button.AddHandler(InputElement.PointerPressedEvent, (_, e) =>
        {
            held = false; moved = false; Settle();
            if (e.Pointer.Type == PointerType.Mouse) { gesture.Cancel(); return; }
            gesture.Press(e.GetPosition(button), e.Timestamp); var pointer = e.Pointer;
            // Only a sign that letting go now opens the menu; nothing opens while the finger is down.
            timer = DispatcherTimer.RunOnce(() =>
            {
                timer = null;
                if (!gesture.Active || !button.IsAttachedToVisualTree() || TakenOver(pointer)) { Cancel(); return; }
                button.Classes.Add("held"); MobileApp.Host?.Haptic?.Invoke();
            }, HoldGesture.Threshold);
            // A scrolling list that took the finger over gets its release, so the end of the press is also read here.
            if (TopLevel.GetTopLevel(button) is { } top)
            {
                void Ended(object? sender, PointerReleasedEventArgs r)
                {
                    if (r.Pointer != pointer) return;
                    top.RemoveHandler(InputElement.PointerReleasedEvent, Ended);
                    Cancel();
                }
                top.AddHandler(InputElement.PointerReleasedEvent, Ended, RoutingStrategies.Bubble, handledEventsToo: true);
            }
        }, RoutingStrategies.Tunnel, handledEventsToo: true);
        bool TakenOver(IPointer pointer) => pointer.Captured is Visual taken && taken != button && !button.IsVisualAncestorOf(taken);
        // Moving the finger is a scroll or a swipe, not a long press, and not a tap either: where nothing scrolls, such
        // as at the end of a list, the button keeps the finger and would take letting go over it as a click.
        button.AddHandler(InputElement.PointerMovedEvent, (_, e) =>
        {
            if (gesture.Move(e.GetPosition(button))) { moved = true; Settle(); }
        }, RoutingStrategies.Tunnel, handledEventsToo: true);
        // Before the button's own release, so the click that follows is known to end a long press.
        button.AddHandler(InputElement.PointerReleasedEvent, (_, e) =>
        {
            Settle();
            if (gesture.Release(e.Timestamp) != HoldOutcome.Hold || !button.IsAttachedToVisualTree()) return;
            // A swipe that took the finger over is not a long press here.
            if (TakenOver(e.Pointer)) return;
            held = true; open();
        }, RoutingStrategies.Tunnel, handledEventsToo: true);
        // The button clicks while the release passes it, so afterwards a click from TalkBack or the keyboard counts again.
        button.AddHandler(InputElement.PointerReleasedEvent, (_, _) => moved = false, RoutingStrategies.Bubble, handledEventsToo: true);
        button.AddHandler(InputElement.PointerCaptureLostEvent, (_, _) => Cancel(), RoutingStrategies.Bubble, handledEventsToo: true);
        button.DetachedFromVisualTree += (_, _) => Cancel();
        // A right click, the menu key, or a platform that reports a long press itself. While a finger is down the menu
        // waits for it to be lifted, and a long press that already opened it does not open it twice.
        button.ContextRequested += (_, e) => { if (!held && !gesture.Active) open(); e.Handled = true; };
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
