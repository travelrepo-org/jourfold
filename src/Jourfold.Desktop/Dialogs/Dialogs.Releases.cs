using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Jourfold.Desktop;

public sealed partial class Dialogs
{
    public async Task ReleaseNotesAsync(IReadOnlyList<ReleaseNote> releases, bool highlightsOnly)
    {
        var s = S; var language = s.Language; var current = AppVersion.Version;
        static Control Bullet(string text) => Ui.Columns("Auto,*", Ui.Text("•", "muted").Margin(0, 0, 8, 0), new SelectableTextBlock { Text = text, TextWrapping = TextWrapping.Wrap });
        var body = Ui.V(18);
        if (highlightsOnly)
        {
            // After an update: the highlights as a short list with icons, then a link to every change.
            foreach (var release in releases)
                foreach (var entry in release.Highlights.Count > 0 ? release.Highlights : release.Changes)
                {
                    var row = Ui.Columns("Auto,*", Ui.Tile(entry.Icon ?? "sparkles", "Activity", 36), new SelectableTextBlock { Text = entry.Text(language), TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center }.Margin(14, 0, 0, 0));
                    AutomationProperties.SetName(row, entry.Text(language)); body.Children.Add(row);
                }
        }
        else
        {
            foreach (var release in releases)
            {
                var date = DateTime.TryParse(release.Date, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var day) ? Formats.ShortDate(NodaTime.LocalDate.FromDateTime(day)) + " " + day.Year : release.Date;
                var heading = Ui.Flow(8, Ui.Text("Jourfold " + release.Version, "h3"), Ui.Text(string.Format(s["QuotedName"], release.CodeName), "muted").Also(t => t.VerticalAlignment = VerticalAlignment.Center),
                    date is null ? null : Ui.Text(date, "caption").Also(t => t.VerticalAlignment = VerticalAlignment.Center),
                    release.Version == current ? Ui.Pill(s["InstalledVersion"], "check", "Accent.Soft", "Accent.SoftText") : null);
                var section = Ui.V(6, heading);
                foreach (var (entries, label) in new[] { (release.Highlights, s["ReleaseHighlights"]), (release.Changes, s["ReleaseChanges"]), (release.Fixes, s["ReleaseFixes"]) })
                {
                    if (entries.Count == 0) continue;
                    section.Children.Add(Ui.Text(label, "strong").Margin(0, 4, 0, 0));
                    foreach (var entry in entries) section.Children.Add(Bullet(entry.Text(language)));
                }
                body.Children.Add(section);
            }
        }
        var close = Action(highlightsOnly ? "LetsGo" : "Close", true);
        var all = highlightsOnly ? Ui.Button(s["VersionHistory"], "history", "ghost") : null;
        var title = highlightsOnly && releases.FirstOrDefault() is { } latest ? string.Format(s["WhatsNewTitle"], latest.Version, latest.CodeName) : s["VersionHistory"];
        var sheet = new DialogSheet(title, Ui.V(16, new ScrollViewer { MaxHeight = 520, Content = body.Margin(0, 0, 12, 0) }, Footer(all is null ? [close] : [all, close])), highlightsOnly ? 560 : 700);
        close.Click += (_, _) => sheet.Close();
        if (all is not null) all.Click += (_, _) => { sheet.Close(); _ = ReleaseNotesAsync(ReleaseNotes.All, highlightsOnly: false); };
        await sheet.ShowDialog(owner);
    }
}
