using Avalonia.Automation;
using Avalonia.Controls;
using NodaTime;

namespace Jourfold.Desktop;

/// <summary>
/// When weather or exchange rates on show were fetched, as a quiet button that fetches them again: "↻ as of 14:20".
/// The phone has the same (<c>Lines.Fetched</c>). A failure says so in a notice and keeps what is there.
/// </summary>
public static class FreshnessButton
{
    /// <param name="format">The text around the time, such as <c>WeatherAsOf</c>.</param>
    /// <param name="label">What the button does, for its tooltip and screen readers.</param>
    /// <param name="refresh">Fetches again; false when that failed.</param>
    /// <param name="failed">The notice after a failure.</param>
    /// <param name="iconOnly">Only the icon, with the time in its tooltip, where room is tight such as the plan's toolbar.</param>
    public static Button Build(MainWindow w, Instant fetched, string format, string label, Func<Task<bool>> refresh, string failed, bool iconOnly = false)
    {
        var text = Freshness.Text(fetched, w.Model.Clock(), DateTimeZoneProviders.Tzdb.GetSystemDefault(), format);
        var button = iconOnly ? Ui.IconButton("refresh-cw", label, "small")
            : new Button { Content = Ui.H(5, Ui.Icon("refresh-cw", 13), Ui.Text(text, "caption")) }.Classed("ghost", "compact");
        AutomationProperties.SetName(button, label + ", " + text); ToolTip.SetTip(button, iconOnly ? label + " (" + text + ")" : label);
        button.Click += async (_, _) =>
        {
            button.IsEnabled = false;
            if (!await refresh()) w.Model.Interaction.Toast(failed);
            button.IsEnabled = true;
        };
        return button;
    }
}
