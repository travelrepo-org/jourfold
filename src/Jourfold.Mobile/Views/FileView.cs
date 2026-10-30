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
        if (Mail.IsMail(file.MediaType) && Read(file.Bytes) is { } mail) { title = mail.Subject is { Length: > 0 } subject ? subject : file.Name; body = MailBody(s, mail); }
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

    /// <summary>
    /// A Wallet pass as the gate needs it: the barcode large on white, its code in text below, then the pass's fields,
    /// the most important first. It works offline and without a wallet app.
    /// </summary>
    private static Control PassBody(WalletPass pass)
    {
        var page = Ui.V(14);
        if (pass.Organization is { Length: > 0 } organization) page.Children.Add(Ui.Text(organization, "overline"));
        var primary = pass.Fields.Where(f => f.Group == "primary").ToArray();
        if (primary.Length > 0)
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions(string.Join(',', primary.Select(_ => "*"))) };
            for (var i = 0; i < primary.Length; i++) row.Children.Add(Ui.V(0, Ui.Text(primary[i].Label ?? "", "caption"), Ui.Text(primary[i].Value, "h1")).Col(i));
            page.Children.Add(row);
        }
        if (pass.Barcode is { } code)
        {
            Control barcode;
            try { barcode = new Jourfold.UI.Controls.BarcodeView(code.Message, code.Format, code.Encoding); }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or ZXing.WriterException) { barcode = Ui.Text(code.Message, "strong"); }
            page.Children.Add(new Border { Child = barcode, Background = Brushes.White, Padding = new Thickness(12), CornerRadius = new CornerRadius(12) });
            if (code.AltText is { Length: > 0 } alt) page.Children.Add(Ui.Text(alt, "strong").Also(t => t.HorizontalAlignment = HorizontalAlignment.Center));
        }
        var details = pass.Fields.Where(f => f.Group is "header" or "secondary" or "auxiliary").ToArray();
        if (details.Length > 0)
        {
            var flow = Ui.Flow(20);
            foreach (var field in details) flow.Children.Add(Ui.V(0, Ui.Text(field.Label ?? "", "caption"), new SelectableTextBlock { Text = field.Value, FontWeight = FontWeight.SemiBold }));
            page.Children.Add(flow);
        }
        foreach (var field in pass.Fields.Where(f => f.Group == "back"))
            page.Children.Add(Ui.V(2, Ui.Text(field.Label ?? "", "caption"), new SelectableTextBlock { Text = field.Value, TextWrapping = TextWrapping.Wrap }));
        return page.Margin(16, 8, 16, 24);
    }

    private static string Kind(ShownFile file, Localization str) =>
        (Mail.IsMail(file.MediaType) ? str["EmailMessage"] : file.MediaType == "application/pdf" ? "PDF" : file.MediaType == WalletPass.MediaType ? str["WalletPass"] : file.MediaType.StartsWith("image/", StringComparison.Ordinal) ? str["Image"] : str["File"])
        + " · " + Formats.Bytes(file.Bytes.Length);

    private static MailMessage? Read(byte[] bytes) { try { return Mail.Read(bytes); } catch (DomainException) { return null; } }

    private static Bitmap? Picture(byte[] bytes)
    {
        try { using var stream = new MemoryStream(bytes); return new Bitmap(stream); }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException or InvalidOperationException) { return null; }
    }

    /// <summary>Who wrote to whom and when, the attachments, then the text. The text is selectable, to copy a code from it.</summary>
    private static Control MailBody(MobileSession s, MailMessage mail)
    {
        var str = s.Strings;
        var headers = Ui.V(6);
        void Header(string label, string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            headers.Children.Add(Ui.V(0, Ui.Text(label, "caption"), new SelectableTextBlock { Text = value, TextWrapping = TextWrapping.Wrap }));
        }
        Header(str["MailFrom"], string.Join(", ", mail.From)); Header(str["MailTo"], string.Join(", ", mail.To)); Header(str["MailCc"], string.Join(", ", mail.Cc));
        Header(str["MailDate"], mail.Date is { } date ? date.ToLocalTime().ToString("ddd d MMM yyyy · HH:mm", CultureInfo.GetCultureInfo(str.Language)) : null);
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
        if (mail.RemoteImages > 0 || mail.TextFromHtml) page.Children.Add(Ui.Text(str["MailShownAsText"], "caption").Margin(4, 0));
        page.Children.Add(Ui.Card(new SelectableTextBlock { Text = mail.Text.Length > 0 ? mail.Text : str["MailWithoutText"], TextWrapping = TextWrapping.Wrap, LineHeight = 22 }, 16));
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
