using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Jourfold.Application;
using TravelRepo.Core;

namespace Jourfold.Mobile.Views;

/// <summary>
/// What another app shared, and where it goes: files to an item of the plan or a booking, or only into the trip's
/// files; a place from a maps app to a new place or an item's place; other text to a note or an item's notes.
/// </summary>
public sealed class ShareView
{
    private readonly MobileSession s;
    private readonly SharedContent content;
    private readonly StackPanel body = Ui.V(16);
    private readonly TextBlock problem = Ui.Text("", "caption").Also(t => { t.IsVisible = false; t.Res(TextBlock.ForegroundProperty, "Danger"); t.TextWrapping = TextWrapping.Wrap; });
    private (double Latitude, double Longitude)? position;
    private bool choosing;

    public Control View { get; }
    public Control Header { get; }

    public ShareView(MobileSession session, SharedContent shared)
    {
        s = session; content = shared; position = shared.Position;
        var close = Ui.IconButton("x", s.Strings["Close"]); close.Click += (_, _) => s.DropIncoming();
        Header = Ui.Columns("*,Auto", Ui.V(2, Ui.Text(s.Strings["SharedWithJourfold"], "h3"), Ui.Text(s.Trip?.Manifest.Title ?? "", "caption")), close).Margin(16, 0, 8, 8);
        View = body.Margin(16, 0, 16, 24);
        Render();
    }

    private void Render()
    {
        var str = s.Strings; body.Children.Clear();
        body.Children.Add(Summary());
        if (content.Files.Count > 0)
        {
            body.Children.Add(Ui.Text(str["AttachTo"], "overline"));
            body.Children.Add(Chooser(includeBookings: true, async id => { await s.AddFilesAsync(content.Files, id); s.FinishSharing(id); }));
            var loose = Ui.Button(str["KeepWithTripFiles"], "folder", "soft").Also(b => b.HorizontalAlignment = HorizontalAlignment.Stretch);
            loose.Click += async (_, _) => await Run(async () => { await s.AddFilesAsync(content.Files, null); s.FinishSharing(null); s.ShowPage("files"); });
            body.Children.Add(loose);
        }
        else if (position is not null || content.ShortLink is not null)
        {
            var lines = content.Lines;
            (string?, string?, double?, double?) Draft() => (lines.FirstOrDefault(), lines.Count > 1 ? string.Join(", ", lines.Skip(1)) : null, position?.Latitude, position?.Longitude);
            if (position is null && content.ShortLink is { } link)
            {
                // A short link says nothing by itself; only the maps service knows where it leads.
                var resolve = Ui.Button(str["PositionFromLink"], "map-pin", "primary").Also(b => b.HorizontalAlignment = HorizontalAlignment.Stretch);
                resolve.Click += async (_, _) => await Run(async () =>
                {
                    resolve.IsEnabled = false;
                    position = await s.ResolveShortLinkAsync(link);
                    if (position is null) Show(str["NoPositionInLink"]); else Render();
                });
                body.Children.Add(Ui.V(6, resolve, Ui.Text(string.Format(CultureInfo.CurrentCulture, str["PositionFromLinkHint"], link.Host), "caption").Also(t => t.TextWrapping = TextWrapping.Wrap)));
            }
            var place = Ui.Button(str["NewPlace"], "map-pin", position is null ? "soft" : "primary").Also(b => b.HorizontalAlignment = HorizontalAlignment.Stretch);
            place.Click += (_, _) => { var draft = Draft(); s.FinishSharing(null); s.ShowPlace(null, draft: draft); };
            body.Children.Add(place);
            body.Children.Add(Ui.Text(str["PlaceOfItem"], "overline"));
            body.Children.Add(Chooser(includeBookings: false, id =>
            {
                var draft = Draft(); var item = s.Trip!.Find(id)!;
                var slot = PlaceEdits.Slots(item).Contains(PlaceSlot.To) ? PlaceSlot.To : PlaceSlot.Place;
                s.FinishSharing(null); s.ShowPlace(null, id, slot, draft);
                return Task.CompletedTask;
            }));
        }
        else if (content.Text is { } text)
        {
            var note = Ui.Button(str["SaveAsNote"], "sticky-note", "primary").Also(b => b.HorizontalAlignment = HorizontalAlignment.Stretch);
            note.Click += async (_, _) => await Run(async () =>
            {
                var title = content.Lines.FirstOrDefault() ?? text.Trim();
                await s.AddNoteAsync(title.Length > 60 ? title[..60].TrimEnd() + "…" : title, text); s.FinishSharing(null);
            });
            body.Children.Add(note);
            body.Children.Add(Ui.Text(str["AddToNotesOf"], "overline"));
            body.Children.Add(Chooser(includeBookings: true, async id => { await s.AppendNoteAsync(id, text); s.FinishSharing(id); }));
        }
        body.Children.Add(problem);
    }

    /// <summary>The files with their size, or the text as it came.</summary>
    private Control Summary()
    {
        var rows = Ui.V(8);
        foreach (var file in content.Files)
        {
            var icon = TravelRepo.Documents.Mail.IsMail(file.MediaType) ? "mail" : file.MediaType == "application/pdf" ? "file-text" : file.MediaType.StartsWith("image/", StringComparison.Ordinal) ? "image" : "file";
            rows.Children.Add(Ui.Columns("Auto,*", Ui.Icon(icon, 18, "Text.Muted").Margin(0, 0, 12, 0), Ui.V(1, Ui.Text(file.Name, "strong"), Ui.Text(Size(file.Bytes.Length), "caption"))));
        }
        if (content.Files.Count == 0 && content.Text is { } text)
        {
            var shown = string.Join("\n", text.Trim().Split('\n').Take(6));
            rows.Children.Add(Ui.Text(shown.Length > 400 ? shown[..400] + "…" : shown).Also(t => t.TextWrapping = TextWrapping.Wrap));
            if (position is { } at) rows.Children.Add(Ui.Columns("Auto,*", Ui.Icon("map-pin", 16, "Accent").Margin(0, 0, 8, 0), Ui.Text(PlaceEdits.Format(at.Latitude, at.Longitude), "caption")));
        }
        return Ui.Card(rows, 14);
    }

    private static string Size(long bytes) => bytes < 1024 * 1024 ? (bytes / 1024.0).ToString("0", CultureInfo.CurrentCulture) + " KB" : (bytes / 1024.0 / 1024.0).ToString("0.0", CultureInfo.CurrentCulture) + " MB";

    /// <summary>The plan's items by day, and the bookings, to choose where something goes.</summary>
    private Control Chooser(bool includeBookings, Func<Guid, Task> chosen)
    {
        var list = Ui.V(4);
        Button Row(Entity item, string detail, string icon)
        {
            var row = new Button { Content = Ui.Columns("Auto,*", Ui.Icon(icon, 18, "Text.Muted").Margin(0, 0, 12, 0), Ui.V(1, Ui.Text(item.Title, "strong"), detail.Length > 0 ? Ui.Text(detail, "caption") : null)) }.Classed("row");
            AutomationProperties.SetName(row, item.Title);
            row.Click += async (_, _) => { if (choosing) return; choosing = true; try { await Run(() => chosen(item.Id)); } finally { choosing = false; } };
            return row;
        }
        foreach (var day in s.Agenda.Days.Where(d => d.Entries.Count > 0))
        {
            list.Children.Add(Ui.Text(Lines.DayTitle(day.Date, s), "caption").Margin(4, 8, 0, 0));
            foreach (var entry in day.Entries.Where(e => e.Role is AgendaRole.Item or AgendaRole.CheckIn).SelectMany(e => e.IsGroup ? [e, .. e.Steps] : new[] { e }))
                list.Children.Add(Row(entry.Item, TimeText.Local(entry, s.PhoneZone, s.Strings, s.Trip).Primary, Visuals.KindIcon(entry.Kind)));
        }
        if (includeBookings)
        {
            var bookings = s.Trip!.Entities.Values.Where(e => e.Type == "booking").OrderBy(e => e.Title, StringComparer.CurrentCulture).ToArray();
            if (bookings.Length > 0) list.Children.Add(Ui.Text(s.Strings["Bookings"], "caption").Margin(4, 8, 0, 0));
            foreach (var booking in bookings) list.Children.Add(Row(booking, booking.Data["reference"]?.ToString() ?? "", "ticket"));
        }
        return Ui.Card(list, 8);
    }

    private async Task Run(Func<Task> action)
    {
        problem.IsVisible = false;
        try { await action(); }
        catch (DomainException ex) { Show(ex.Code == "asset.large" ? ex.Message : s.Strings.Diagnostic(ex.Code)); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { Show(s.Strings["NetworkError"]); }
    }

    private void Show(string text) { problem.Text = text; problem.IsVisible = true; }
}
