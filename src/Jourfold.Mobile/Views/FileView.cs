using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using TravelRepo.Core;
using TravelRepo.Documents;

namespace Jourfold.Mobile.Views;

/// <summary>
/// A file of the trip on its own page, above everything else. E-mails show their headers, their text and their
/// attachments, so a booking can be read again on the road without a network; images show as they are. Other files,
/// such as PDFs, go to an app on the phone that can show them.
/// </summary>
public static class FileView
{
    public static Control Build(MobileSession s, ShownFile file)
    {
        var str = s.Strings;
        var back = Ui.IconButton("chevron-left", str["Back"]); back.Click += (_, _) => s.CloseFile();
        var title = file.Name;
        Control body;
        if (Mail.IsMail(file.MediaType) && Read(file.Bytes) is { } mail) { title = mail.Subject is { Length: > 0 } subject ? subject : file.Name; body = MailBody(s, mail, file.Bytes); }
        else if (file.MediaType == WalletPass.MediaType && WalletPass.Read(file.Bytes, str.Language) is { } pass) { title = pass.Description ?? file.Name; body = PassBody(pass); }
        else if (file.MediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase) && Picture(file.Bytes) is { } picture)
            body = new Image { Source = picture, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, HorizontalAlignment = HorizontalAlignment.Center }.Margin(16);
        else body = Elsewhere(s, file);
        var open = s.Host.OpenFile is null ? null : Ui.IconButton("external-link", str["OpenInOtherApp"]);
        if (open is not null) open.Click += async (_, _) => await s.OpenElsewhereAsync(file);
        var header = Ui.Columns("Auto,*,Auto", back, Ui.V(1, Ui.Line(title, "title"), Ui.Line(Kind(file, str), "caption")).Margin(4, 0), open).Margin(4, 6, 8, 6);
        var layout = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*") };
        layout.Children.Add(header);
        if (s.FileProblem is { } problem) layout.Children.Add(Ui.Text(problem, "caption").Also(t => t.Res(TextBlock.ForegroundProperty, "Danger")).Margin(20, 0, 20, 8).Row(1));
        layout.Children.Add(new ScrollViewer { Content = body, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled }.Row(2));
        var page = new Border { Child = layout }.Res(Border.BackgroundProperty, "Bg.App");
        AutomationProperties.SetName(page, title);
        return page;
    }

    /// <summary>A Wallet pass, laid out for the gate (<see cref="Jourfold.UI.Controls.PassCard"/>).</summary>
    private static Control PassBody(WalletPass pass) => Jourfold.UI.Controls.PassCard.Build(pass).Margin(16, 8, 16, 24);

    private static string Kind(ShownFile file, Localization str) =>
        (Mail.IsMail(file.MediaType) ? str["EmailMessage"] : file.MediaType == "application/pdf" ? "PDF" : file.MediaType == WalletPass.MediaType ? str["WalletPass"] : file.MediaType.StartsWith("image/", StringComparison.Ordinal) ? str["Image"] : str["File"])
        + " · " + Formats.Bytes(file.Bytes.Length);

    private static MailMessage? Read(byte[] bytes) { try { return Mail.Read(bytes); } catch (DomainException) { return null; } }

    private static Bitmap? Picture(byte[] bytes)
    {
        try { using var stream = new MemoryStream(bytes); return new Bitmap(stream); }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException or InvalidOperationException) { return null; }
    }

    /// <summary>
    /// Who wrote to whom and when, the attachments, then the e-mail in its original layout or as text, as on the
    /// desktop. The text is selectable, to copy a code from it.
    /// </summary>
    private static Control MailBody(MobileSession s, MailMessage mail, byte[] bytes)
    {
        var str = s.Strings;
        var headers = Ui.V(6);
        void Header(string label, string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            headers.Children.Add(Ui.V(0, Ui.Text(label, "caption"), new SelectableTextBlock { Text = value, TextWrapping = TextWrapping.Wrap }));
        }
        Header(str["MailFrom"], string.Join(", ", mail.From)); Header(str["MailTo"], string.Join(", ", mail.To)); Header(str["MailCc"], string.Join(", ", mail.Cc));
        Header(str["MailDate"], mail.Date is { } date ? date.ToLocalTime().ToString(str["DayShortFormat"] + " yyyy · HH:mm", CultureInfo.GetCultureInfo(str.Language)) : null);
        var page = Ui.V(12, Ui.Card(headers, 14));
        if (mail.Attachments.Count > 0)
        {
            var list = Ui.V(0);
            foreach (var attachment in mail.Attachments)
            {
                var media = attachment.MediaType == "application/octet-stream" ? MediaTypes.FromFileName(attachment.FileName) : attachment.MediaType;
                var icon = media == "application/pdf" ? "file-text" : Mail.IsMail(media) ? "mail" : media.StartsWith("image/", StringComparison.Ordinal) ? "image" : "file";
                var row = new Button { Content = Ui.Columns("Auto,*,Auto", Ui.Icon(icon, 20, "Text.Muted").Margin(0, 0, 14, 0), Ui.V(1, Ui.Line(attachment.FileName, "strong"), Ui.Text(Formats.Bytes(attachment.Content.Length), "caption")), Ui.Icon("chevron-right", 18, "Text.Subtle")) }.Classed("row");
                AutomationProperties.SetName(row, attachment.FileName);
                var shown = new ShownFile(attachment.FileName, media, attachment.Content);
                // E-mails and images open here; anything else goes to an app that can show it.
                row.Click += async (_, _) => { if (Mail.IsMail(media) || media.StartsWith("image/", StringComparison.Ordinal)) s.ShowFile(shown); else await s.OpenElsewhereAsync(shown); };
                if (list.Children.Count > 0) list.Children.Add(Ui.Divider());
                list.Children.Add(row);
            }
            page.Children.Add(Ui.V(6, Ui.Text(str["Attachments"], "overline").Margin(4, 0), new Border { Child = list, ClipToBounds = true }.Classed("card")));
        }
        var original = mail.Html is { Length: > 0 } && s.MailView == "original";
        if (mail.Html is { Length: > 0 })
        {
            var views = new WrapPanel { ItemSpacing = 6, LineSpacing = 6 };
            foreach (var (view, label, icon) in new[] { ("original", str["MailOriginal"], "mail"), ("text", str["MailText"], "file-text") })
            {
                var chip = Ui.Choice(label, icon, s.MailView == view, "chip");
                chip.Click += (_, _) => s.MailView = view;
                views.Children.Add(chip);
            }
            page.Children.Add(views);
        }
        var key = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes));
        var allowed = s.RemoteImagesAllowed.Contains(key);
        if (original && mail.RemoteImages > 0)
        {
            var note = allowed ? str["RemoteImagesLoaded"] : mail.RemoteImages == 1 ? str["RemoteImageBlocked"] : string.Format(CultureInfo.CurrentCulture, str["RemoteImagesBlocked"], mail.RemoteImages);
            var notice = Ui.V(8, Ui.Columns("Auto,*", Ui.Icon("shield", 16, "Text.Subtle").Margin(0, 2, 10, 0), Ui.Text(note, "caption").Also(t => t.TextWrapping = TextWrapping.Wrap)));
            if (!allowed)
            {
                var load = Ui.Button(str["LoadImages"], "image", "soft", "compact").Also(b => b.HorizontalAlignment = HorizontalAlignment.Left);
                load.Click += (_, _) => { s.RemoteImagesAllowed.Add(key); s.Redraw(); };
                notice.Children.Add(load);
            }
            page.Children.Add(notice.Margin(4, 0));
        }
        if (original)
        {
            Control? paper = null;
            paper = Jourfold.UI.Controls.MailPaper.Build(mail, allowed, url => Jourfold.UI.Controls.MailPaper.DownloadImageAsync(s.MapsHttp, url),
                link =>
                {
                    var top = TopLevel.GetTopLevel(paper);
                    s.Confirm(string.Format(CultureInfo.CurrentCulture, str["OpenLinkBody"], link), str["OpenLink"], async () => { if (top?.Launcher is { } launcher) await launcher.LaunchUriAsync(new Uri(link)); });
                },
                () => s.ShowFileProblem(str["MailLinkBlocked"]), str["MailOriginal"], fontSize: 11, padding: 4, zoomable: true);
            // Nearly the whole width of the phone, so a wide e-mail needs to shrink less.
            page.Children.Add(paper.Margin(-12, 0));
        }
        else page.Children.Add(Ui.Card(new SelectableTextBlock { Text = mail.Text.Length > 0 ? mail.Text : str["MailWithoutText"], TextWrapping = TextWrapping.Wrap, LineHeight = 22 }, 16));
        return page.Margin(16, 4, 16, 32);
    }

    /// <summary>A file the app cannot show itself, with the way to an app that can.</summary>
    private static Control Elsewhere(MobileSession s, ShownFile file)
    {
        var text = Ui.Text(s.Host.OpenFile is null ? s.Strings["CannotShowFile"] : s.Strings["OpenFileElsewhereHint"], "muted");
        var page = Ui.V(14, Ui.Icon(file.MediaType == "application/pdf" ? "file-text" : "file", 40, "Text.Muted").Also(i => i.HorizontalAlignment = HorizontalAlignment.Left), text);
        if (s.Host.OpenFile is not null)
        {
            var open = Ui.Button(s.Strings["OpenInOtherApp"], "external-link", "primary").Also(b => b.HorizontalAlignment = HorizontalAlignment.Stretch);
            open.Click += async (_, _) => await s.OpenElsewhereAsync(file);
            page.Children.Add(open);
        }
        return page.Margin(20, 16);
    }
}
