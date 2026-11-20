using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace Jourfold.Mobile.Controls;

/// <summary>A time chosen in the system's own picker: <see cref="Chosen"/> is false when the user cancelled.</summary>
public readonly record struct PickedTime(bool Chosen, TimeSpan Time);

/// <summary>A day chosen in the system's own picker: <see cref="Chosen"/> is false when the user cancelled.</summary>
public readonly record struct PickedDate(bool Chosen, DateTime Date);

/// <summary>
/// The system's own date and time pickers, where the platform has them (iOS). A tap on a date or time field opens
/// the system's picker instead of Avalonia's wheels; what is chosen goes back into the field, so forms read it as
/// before. Android keeps Avalonia's pickers.
/// </summary>
public static class SystemPickers
{
    private static bool installed;

    /// <summary>
    /// Sends taps on date and time fields to the host's pickers. A class handler runs before the field's own, which
    /// would open Avalonia's wheels; VoiceOver activates the field's button the same way.
    /// </summary>
    public static void Install()
    {
        if (installed) return;
        installed = true;
        Button.ClickEvent.AddClassHandler<Button>(OnClick, RoutingStrategies.Bubble);
    }

    private static void OnClick(Button button, RoutedEventArgs e)
    {
        if (e.Handled || e.Source != button || button.TemplatedParent is not Control owner) return;
        var host = owner.FindAncestorOfType<AppShell>()?.Session.Host;
        if (owner is TimePicker time && host?.PickTime is { } pickTime)
        {
            e.Handled = true;
            _ = PickTimeAsync(time, pickTime);
        }
        else if (owner is DatePicker date && host?.PickDate is { } pickDate)
        {
            e.Handled = true;
            _ = PickDateAsync(date, pickDate);
        }
    }

    /// <summary>The field's name for the picker's title, as VoiceOver reads it.</summary>
    private static string? Title(Control field) =>
        AutomationProperties.GetName(field) is { Length: > 0 } name ? name
        : field.GetVisualAncestors().OfType<Control>().Select(AutomationProperties.GetName).FirstOrDefault(n => !string.IsNullOrEmpty(n));

    private static async Task PickTimeAsync(TimePicker field, Func<TimeSpan?, int, string?, Task<PickedTime>> pick)
    {
        var picked = await pick(field.SelectedTime, Math.Max(1, field.MinuteIncrement), Title(field));
        if (picked.Chosen) field.SelectedTime = picked.Time;
    }

    private static async Task PickDateAsync(DatePicker field, Func<DateTime?, string?, Task<PickedDate>> pick)
    {
        var picked = await pick(field.SelectedDate?.Date, Title(field));
        if (picked.Chosen) field.SelectedDate = new DateTimeOffset(DateTime.SpecifyKind(picked.Date.Date, DateTimeKind.Unspecified), TimeSpan.Zero);
    }
}
