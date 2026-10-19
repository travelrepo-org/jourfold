using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Jourfold.Infrastructure;
using TravelRepo.Core;
using TravelRepo.Documents;

namespace Jourfold.Desktop;

/// <summary>
/// One window that shows attached files next to the plan: PDFs page by page with zoom and search, e-mails with their
/// headers, text and attachments, and images. It does not block the main window and reuses itself for the next file,
/// so it can stay open on a second screen. Nothing is loaded from the network.
/// </summary>
public sealed class DocumentWindow : Window
{
    private static DocumentWindow? open;
    private readonly MainWindow main;
    private MainViewModel M => main.Model;
    private Localization S => M.Strings;
    private sealed record Shown(Entity? Document, string Name, string Media, byte[] Bytes);
    private readonly Stack<Shown> history = new();
    private Shown? current;
    private readonly ContentControl body = new();
    private readonly TextBlock heading = new() { FontSize = 16, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock subheading = new() { FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly StackPanel tools = new() { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
    private readonly Button back;

    // PDF state
    private PdfiumDocument? pdf;
    private (double Width, double Height)[] pageSizes = [];
    private readonly List<(Image Image, Canvas Overlay, Border Frame)> pages = [];
    private ScrollViewer? pageScroll;
    private double zoom = 1;
    private int renderGeneration;
    private IReadOnlyList<PdfMatch> matches = [];
    private int matchIndex;
    private TextBox? search;
    private TextBlock? matchLabel;

    private DocumentWindow(MainWindow main)
    {
        this.main = main;
        Title = "Jourfold"; Icon = main.Icon; MinWidth = 520; MinHeight = 420;
        Width = double.TryParse(main.Model.Store.Get("viewer.width"), System.Globalization.CultureInfo.InvariantCulture, out var w) ? Math.Clamp(w, 520, 3000) : 900;
        Height = double.TryParse(main.Model.Store.Get("viewer.height"), System.Globalization.CultureInfo.InvariantCulture, out var h) ? Math.Clamp(h, 420, 3000) : 760;
        RequestedThemeVariant = main.RequestedThemeVariant;
        this.Res(BackgroundProperty, "Bg.App");
        back = Ui.IconButton("arrow-left", S["Back"], "small"); back.IsVisible = false; back.Click += (_, _) => GoBack();
        var openExternal = Ui.Button(S["OpenWithSystem"], "external-link", "compact"); openExternal.Click += (_, _) => OpenExternally();
        var close = Ui.IconButton("x", S["Close"], "small"); close.Click += (_, _) => Close();
        var titles = Ui.V(1, heading.Res(TextBlock.ForegroundProperty, "Text"), subheading.Res(TextBlock.ForegroundProperty, "Text.Muted"));
        var bar = new Border { Padding = new Thickness(14, 10), BorderThickness = new Thickness(0, 0, 0, 1), Child = Ui.Columns("Auto,*,Auto,Auto,Auto", back.Margin(0, 0, 8, 0), titles, tools.Margin(12, 0), openExternal, close.Margin(6, 0, 0, 0)) }
            .Res(Border.BackgroundProperty, "Bg.Surface").Res(Border.BorderBrushProperty, "Line");
        var layout = new DockPanel(); DockPanel.SetDock(bar, Dock.Top); layout.Children.Add(bar); layout.Children.Add(body);
        Content = layout;
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && FocusManager?.GetFocusedElement() is not TextBox) { Close(); e.Handled = true; }
            if (e.Key == Key.F && e.KeyModifiers == KeyModifiers.Control && search is not null) { search.Focus(); search.SelectAll(); e.Handled = true; }
            if (e.KeyModifiers == KeyModifiers.Control && e.Key is Key.OemPlus or Key.Add && pdf is not null) { SetZoom(zoom * 1.25); e.Handled = true; }
            if (e.KeyModifiers == KeyModifiers.Control && e.Key is Key.OemMinus or Key.Subtract && pdf is not null) { SetZoom(zoom / 1.25); e.Handled = true; }
        };
        Closing += (_, _) => { M.Store.Set("viewer.width", Width.ToString(System.Globalization.CultureInfo.InvariantCulture)); M.Store.Set("viewer.height", Height.ToString(System.Globalization.CultureInfo.InvariantCulture)); };
        Closed += (_, _) => { pdf?.Dispose(); pdf = null; if (open == this) open = null; };
        SizeChanged += (_, e) => { if (pdf is not null && Math.Abs(e.NewSize.Width - e.PreviousSize.Width) > 40) RenderPages(); };
    }

    /// <summary>The window, if it is open. Tests and screenshots use it.</summary>
    public static DocumentWindow? Current => open;

    /// <summary>Shows a document of the trip, opening the window or reusing it.</summary>
    public static void Show(MainWindow main, Entity document)
    {
        if (DocumentPreviews.Bytes(main.Model, document) is not { } bytes) { main.Model.Interaction.Toast(main.Model.Strings["CannotOpen"]); return; }
        if (open is null) { open = new DocumentWindow(main); open.Show(); }
        open.history.Clear(); open.Display(new(document, document.Title, DocumentPreviews.Media(document), bytes));
        open.Activate();
    }

    private void Display(Shown shown)
    {
        current = shown; back.IsVisible = history.Count > 0; ToolTip.SetTip(back, history.Count > 0 ? S["BackToMail"] : null);
        pdf?.Dispose(); pdf = null; pages.Clear(); matches = []; tools.Children.Clear(); search = null; matchLabel = null; pageScroll = null; zoom = 1;
        heading.Text = shown.Name; subheading.Text = shown.Media; Title = shown.Name + " – Jourfold";
        Control view;
        try
        {
            view = shown.Media switch
            {
                "application/pdf" when PdfiumDocument.IsAvailable => PdfView(shown.Bytes),
                Mail.MediaType => MailView(shown),
                var type when type.StartsWith("image/", StringComparison.Ordinal) => ImageView(shown.Bytes),
                _ => Unsupported()
            };
        }
        catch (Exception ex) when (ex is InvalidDataException or DomainException or ArgumentException or NotSupportedException) { view = Unsupported(); }
        body.Content = view;
    }

    private void GoBack() { if (history.TryPop(out var previous)) Display(previous); }

    private Control Unsupported()
    {
        var openExternal = Ui.Button(S["OpenWithSystem"], "external-link", "primary"); openExternal.Click += (_, _) => OpenExternally();
        return new Border { Padding = new Thickness(40), Child = Ui.EmptyState("file", S["CannotShowHere"], S["CannotShowHereBody"], openExternal) };
    }

    private void OpenExternally()
    {
        if (current is null) return;
        try
        {
            var folder = Path.Combine(M.Store.Root, "previews", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
            var name = Mail.SafeName(Path.HasExtension(current.Name) ? current.Name : current.Name + MediaTypes.Extension(current.Media));
            var path = Path.Combine(folder, name); File.WriteAllBytes(path, current.Bytes); M.Interaction.Open(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { M.Interaction.Toast(S["CannotOpen"]); }
    }

    // Images ----------------------------------------------------------------------------------------------

    private static Control ImageView(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        var image = new Image { Source = new Bitmap(stream), Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, Margin = new Thickness(24) };
        return new ScrollViewer { Content = image, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto }.Res(BackgroundProperty, "Bg.Subtle");
    }

    // PDF ---------------------------------------------------------------------------------------------------

    private Control PdfView(byte[] bytes)
    {
        pdf = PdfiumDocument.Open(bytes);
        var count = pdf.PageCount; pageSizes = Enumerable.Range(0, count).Select(pdf.PageSize).ToArray();
        subheading.Text = "PDF · " + (count == 1 ? S["OnePage"] : string.Format(S["PageCount"], count)) + " · " + Formats.Bytes(bytes.Length);
        var zoomOut = Ui.IconButton("zoom-out", S["ZoomOut"], "small"); zoomOut.Click += (_, _) => SetZoom(zoom / 1.25);
        var zoomIn = Ui.IconButton("zoom-in", S["ZoomIn"], "small"); zoomIn.Click += (_, _) => SetZoom(zoom * 1.25);
        var fit = Ui.Button(S["FitWidth"], null, "compact"); fit.Click += (_, _) => SetZoom(1);
        search = new TextBox { Watermark = S["SearchInDocument"], Width = 200 }.Classed("search"); AutomationProperties.SetName(search, S["SearchInDocument"]);
        matchLabel = new TextBlock { FontSize = 12, VerticalAlignment = VerticalAlignment.Center, MinWidth = 60 }; matchLabel.Res(TextBlock.ForegroundProperty, "Text.Muted");
        var previous = Ui.IconButton("chevron-up", S["PreviousMatch"], "small"); previous.Click += (_, _) => Step(-1);
        var next = Ui.IconButton("chevron-down", S["NextMatch"], "small"); next.Click += (_, _) => Step(1);
        search.KeyDown += (_, e) => { if (e.Key == Key.Enter) { if (e.KeyModifiers == KeyModifiers.Shift) Step(-1); else if (matches.Count > 0 && search.Tag as string == search.Text) Step(1); else Find(); e.Handled = true; } };
        tools.Children.Add(search); tools.Children.Add(matchLabel); tools.Children.Add(previous); tools.Children.Add(next); tools.Children.Add(Ui.H(0, zoomOut, fit, zoomIn).Margin(8, 0, 0, 0));

        var panel = new StackPanel { Spacing = 16, Margin = new Thickness(24), HorizontalAlignment = HorizontalAlignment.Center };
        for (var i = 0; i < count; i++)
        {
            var image = new Image { Stretch = Stretch.Fill }; var overlay = new Canvas { IsHitTestVisible = false };
            var frame = new Border { Background = Brushes.White, BoxShadow = BoxShadows.Parse("0 2 10 0 #330F2A3D"), Child = new Panel { Children = { image, overlay } } };
            AutomationProperties.SetName(frame, string.Format(S["PageOf"], i + 1, count));
            pages.Add((image, overlay, frame)); panel.Children.Add(frame);
        }
        pageScroll = new ScrollViewer { Content = panel, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto };
        pageScroll.Res(BackgroundProperty, "Bg.Subtle");
        Control view = pageScroll;
        if (count > 1)
        {
            var thumbs = new StackPanel { Spacing = 10, Margin = new Thickness(12) };
            for (var i = 0; i < count; i++)
            {
                var index = i; var thumb = new Image { Width = 84, Height = 84 * pageSizes[i].Height / Math.Max(1, pageSizes[i].Width), Stretch = Stretch.Fill };
                var button = new Button { Content = Ui.V(4, new Border { Background = Brushes.White, Child = thumb, BorderThickness = new Thickness(1) }.Res(Border.BorderBrushProperty, "Line"), Ui.Text((i + 1).ToString(System.Globalization.CultureInfo.CurrentCulture), "caption").Also(t => t.HorizontalAlignment = HorizontalAlignment.Center)), Padding = new Thickness(6), Background = Brushes.Transparent, BorderThickness = new Thickness(0) };
                AutomationProperties.SetName(button, string.Format(S["PageOf"], i + 1, count));
                button.Click += (_, _) => ScrollTo(index, 0);
                thumbs.Children.Add(button);
                _ = RenderThumbnail(index, thumb);
            }
            var side = new Border { Width = 124, BorderThickness = new Thickness(0, 0, 1, 0), Child = new ScrollViewer { Content = thumbs } }.Res(Border.BorderBrushProperty, "Line").Res(Border.BackgroundProperty, "Bg.Surface");
            var grid = new DockPanel(); DockPanel.SetDock(side, Dock.Left); grid.Children.Add(side); grid.Children.Add(pageScroll); view = grid;
        }
        Dispatcher.UIThread.Post(RenderPages, DispatcherPriority.Loaded);
        return view;
    }

    private async Task RenderThumbnail(int index, Image target)
    {
        var document = pdf; if (document is null) return;
        var scaling = RenderScaling;
        try { var image = await Task.Run(() => document.Render(index, 84 * scaling / Math.Max(1, pageSizes[index].Width))); if (pdf == document) target.Source = DocumentPreviews.ToBitmap(image, scaling); }
        catch (Exception ex) when (ex is InvalidDataException or ObjectDisposedException) { }
    }

    private double FitScale
    {
        get
        {
            var available = (pageScroll?.Bounds.Width is > 0 and var w ? w : ClientSize.Width - (pages.Count > 1 ? 124 : 0)) - 64;
            return Math.Clamp(available / Math.Max(1, pageSizes.Max(p => p.Width)), 0.2, 4);
        }
    }

    private void SetZoom(double value) { zoom = Math.Clamp(value, 0.25, 4); RenderPages(); }

    /// <summary>Lays out every page at the current zoom right away and renders them in the background, first to last.</summary>
    private async void RenderPages()
    {
        var document = pdf; if (document is null || pages.Count == 0) return;
        var generation = ++renderGeneration; var scale = FitScale * zoom; var scaling = RenderScaling;
        for (var i = 0; i < pages.Count; i++) { pages[i].Image.Width = pageSizes[i].Width * scale; pages[i].Image.Height = pageSizes[i].Height * scale; }
        Highlight();
        for (var i = 0; i < pages.Count; i++)
        {
            var index = i;
            try
            {
                var image = await Task.Run(() => document.Render(index, scale * scaling));
                if (generation != renderGeneration || pdf != document) return;
                pages[index].Image.Source = DocumentPreviews.ToBitmap(image, scaling);
            }
            catch (Exception ex) when (ex is InvalidDataException or ObjectDisposedException) { return; }
        }
    }

    private void Find()
    {
        if (pdf is null || search is null || matchLabel is null) return;
        matches = pdf.Find(search.Text ?? ""); matchIndex = 0; search.Tag = search.Text;
        matchLabel.Text = string.IsNullOrWhiteSpace(search.Text) ? "" : matches.Count == 0 ? S["NotFound"] : string.Format(S["MatchOf"], 1, matches.Count);
        Highlight(); if (matches.Count > 0) Reveal();
    }

    private void Step(int delta)
    {
        if (matches.Count == 0) { Find(); return; }
        matchIndex = (matchIndex + delta + matches.Count) % matches.Count;
        if (matchLabel is not null) matchLabel.Text = string.Format(S["MatchOf"], matchIndex + 1, matches.Count);
        Highlight(); Reveal();
    }

    private void Highlight()
    {
        var scale = FitScale * zoom;
        foreach (var page in pages) page.Overlay.Children.Clear();
        for (var i = 0; i < matches.Count; i++)
            foreach (var (left, top, right, bottom) in matches[i].Rectangles)
            {
                var mark = new Border { Width = Math.Max(2, (right - left) * scale), Height = Math.Max(2, (bottom - top) * scale), CornerRadius = new CornerRadius(2), Opacity = i == matchIndex ? 0.55 : 0.28 }.Res(Border.BackgroundProperty, i == matchIndex ? "Accent" : "Warning");
                Canvas.SetLeft(mark, left * scale); Canvas.SetTop(mark, top * scale); pages[matches[i].Page].Overlay.Children.Add(mark);
            }
    }

    private void Reveal() { if (matches.Count > 0 && matches[matchIndex].Rectangles.FirstOrDefault() is var first) ScrollTo(matches[matchIndex].Page, first.Top * FitScale * zoom); }

    private void ScrollTo(int page, double offset)
    {
        if (pageScroll is null || page >= pages.Count) return;
        var top = pages[page].Frame.TranslatePoint(default, (Visual)pageScroll.Content!)?.Y ?? 0;
        pageScroll.Offset = new Vector(pageScroll.Offset.X, Math.Max(0, top + offset - 80));
    }

    // E-mail ------------------------------------------------------------------------------------------------

    /// <summary>Downloads an image from the internet for an e-mail; tests replace it. Returns null when it fails.</summary>
    public static Func<string, Task<byte[]?>> FetchImage { get; set; } = DownloadImageAsync;
    private static readonly HttpClient imageHttp = new() { Timeout = TimeSpan.FromSeconds(15) };
    private const int MaxImageBytes = 5 * 1024 * 1024;
    // E-mails whose images from the internet the person chose to load, until the window closes.
    private readonly HashSet<string> remoteAllowed = [];

    private static async Task<byte[]?> DownloadImageAsync(string url)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url); OnlineIdentity.Apply(request);
            using var response = await imageHttp.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            if (!response.IsSuccessStatusCode || response.Content.Headers.ContentType?.MediaType?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) != true || response.Content.Headers.ContentLength > MaxImageBytes) return null;
            var bytes = await response.Content.ReadAsByteArrayAsync();
            return bytes.Length > MaxImageBytes ? null : bytes;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException or UriFormatException) { return null; }
    }

    private Control MailView(Shown shown)
    {
        var mail = Mail.Read(shown.Bytes);
        heading.Text = mail.Subject is { Length: > 0 } subject ? subject : shown.Name; subheading.Text = S["EmailMessage"] + " · " + shown.Name;
        Title = heading.Text + " – Jourfold";
        var headers = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), RowSpacing = 4, ColumnSpacing = 14 };
        void Header(string label, string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            headers.RowDefinitions.Add(new RowDefinition(GridLength.Auto)); var row = headers.RowDefinitions.Count - 1;
            headers.Children.Add(Ui.Text(label, "caption").Row(row));
            headers.Children.Add(new SelectableTextBlock { Text = value, TextWrapping = TextWrapping.Wrap }.Col(1).Row(row));
        }
        Header(S["MailFrom"], string.Join(", ", mail.From)); Header(S["MailTo"], string.Join(", ", mail.To)); Header(S["MailCc"], string.Join(", ", mail.Cc));
        Header(S["MailDate"], mail.Date is { } date ? Formats.LongDay(NodaTime.LocalDate.FromDateTime(date.LocalDateTime)) + " · " + Formats.Time(NodaTime.LocalTime.FromTimeOnly(TimeOnly.FromDateTime(date.LocalDateTime))) : null);
        var content = Ui.V(16, Ui.Card(headers, 16));
        // The original layout when the e-mail has HTML, unless the person prefers the plain text.
        var original = mail.Html is { Length: > 0 } && M.Store.Get("viewer.mail") != "text";
        if (mail.Html is { Length: > 0 })
            tools.Children.Add(ViewToolbar.Segmented([("original", S["MailOriginal"], (string?)"mail"), ("text", S["MailText"], "file-text")], original ? "original" : "text", v => { M.Store.Set("viewer.mail", v); Display(shown); }));
        var key = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(shown.Bytes));
        var allowed = remoteAllowed.Contains(key);
        if (mail.RemoteImages > 0)
        {
            var note = allowed ? S["RemoteImagesLoaded"] : mail.RemoteImages == 1 ? S["RemoteImageBlocked"] : string.Format(S["RemoteImagesBlocked"], mail.RemoteImages);
            var load = original && !allowed ? Ui.Button(S["LoadImages"], "image", "compact").Also(b => { ToolTip.SetTip(b, S["LoadImagesHint"]); b.Click += (_, _) => { remoteAllowed.Add(key); Display(shown); }; }) : null;
            content.Children.Add(Ui.Columns("Auto,*,Auto", Ui.Icon("shield", 14, "Text.Subtle"), Ui.Text(note, "caption").Also(t => t.TextWrapping = TextWrapping.Wrap).Margin(8, 0), load));
        }
        if (mail.Attachments.Count > 0) content.Children.Add(Attachments(shown, mail));
        content.Children.Add(original ? MailPaper(mail, allowed) : Ui.Card(new SelectableTextBlock { Text = mail.Text.Length > 0 ? mail.Text : S["MailWithoutText"], TextWrapping = TextWrapping.Wrap, FontSize = 13.5, LineHeight = 20 }, 20));
        return new ScrollViewer { Content = new Border { MaxWidth = 860, Padding = new Thickness(24), Child = content, HorizontalAlignment = HorizontalAlignment.Center } }.Res(BackgroundProperty, "Bg.Subtle");
    }

    /// <summary>
    /// The e-mail in its original layout, cleaned by TravelRepo's <see cref="MailHtml"/>. It sits on white paper in
    /// both themes, because e-mails are designed for a white background. Images come from the message itself, and
    /// from the internet only after the person allowed it; links open in the browser after showing where they go.
    /// </summary>
    private Control MailPaper(MailMessage mail, bool remote)
    {
        var safe = MailHtml.Sanitize(mail.Html!, remote);
        var html = new TheArtOfDev.HtmlRenderer.Avalonia.HtmlLabel { Text = safe.Html, IsContextMenuEnabled = false, IsSelectionEnabled = true, Background = Brushes.White, AutoSize = false, AutoSizeHeightOnly = true };
        html.BaseStylesheet = "body { font-family: 'Plus Jakarta Sans', sans-serif; font-size: 10pt; color: #1f2933; margin: 0; } a { color: #0b6e6e; } img { max-width: 100%; }";
        html.StylesheetLoad += (_, e) => e.Event.SetStyleSheet = "";
        html.ImageLoad += (_, e) =>
        {
            var load = e.Event; load.Handled = true; var src = load.Src?.Trim() ?? "";
            Bitmap? Decode(byte[] bytes) { try { using var stream = new MemoryStream(bytes); return new Bitmap(stream); } catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException or InvalidOperationException) { return null; } }
            if (src.StartsWith("cid:", StringComparison.OrdinalIgnoreCase))
            {
                var id = src[4..].Trim('<', '>');
                if (mail.InlineImages?.FirstOrDefault(i => string.Equals(i.ContentId, id, StringComparison.OrdinalIgnoreCase)) is { } image && Decode(image.Content) is { } bitmap) load.Callback(bitmap); else load.Callback();
            }
            else if (src.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase) && src.IndexOf(";base64,", StringComparison.OrdinalIgnoreCase) is var comma and > 0)
            {
                byte[]? bytes = null; try { bytes = Convert.FromBase64String(src[(comma + 8)..]); } catch (FormatException) { }
                if (bytes is not null && Decode(bytes) is { } bitmap) load.Callback(bitmap); else load.Callback();
            }
            else if (remote && (src.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || src.StartsWith("http://", StringComparison.OrdinalIgnoreCase)))
                _ = FetchImage(src).ContinueWith(t => Dispatcher.UIThread.Post(() => { if (t.Result is { } bytes && Decode(bytes) is { } bitmap) load.Callback(bitmap); else load.Callback(); }), TaskScheduler.Default);
            else load.Callback();
        };
        html.LinkClicked += async (_, e) =>
        {
            e.Event.Handled = true; var link = e.Event.Link?.Trim();
            if (!MailHtml.IsOpenableLink(link)) { M.Interaction.Toast(S["MailLinkBlocked"]); return; }
            if (await M.Interaction.ConfirmAsync(S["OpenLinkTitle"], string.Format(S["OpenLinkBody"], link), S["OpenLink"])) M.Interaction.Open(link!);
        };
        AutomationProperties.SetName(html, S["MailOriginal"] + ": " + (mail.Text.Length > 300 ? mail.Text[..300] : mail.Text));
        return new Border { Background = Brushes.White, CornerRadius = new CornerRadius(10), Padding = new Thickness(20), Child = html, ClipToBounds = true, BorderThickness = new Thickness(1) }.Res(Border.BorderBrushProperty, "Line");
    }

    private Control Attachments(Shown shown, MailMessage mail)
    {
        var list = Ui.V(4);
        // Files can be added when the e-mail itself belongs to the trip; they are attached where the e-mail is.
        var owners = shown.Document is { } document && M.Trip is { } trip ? trip.All.Where(e => e.Type != "document" && TripCommands.AttachedDocuments(trip, e).Any(d => d.Id == document.Id)).Select(e => e.Id).ToArray() : [];
        foreach (var attachment in mail.Attachments)
        {
            var media = attachment.MediaType == "application/octet-stream" ? MediaTypes.FromFileName(attachment.FileName) : attachment.MediaType;
            var icon = media == "application/pdf" ? "file-text" : Mail.IsMail(media) ? "mail" : media.StartsWith("image/", StringComparison.Ordinal) ? "image" : "file";
            var view = Ui.Button(S["OpenFile"], "eye", "compact");
            view.Click += (_, _) => { if (current is not null) history.Push(current); Display(new(null, attachment.FileName, media, attachment.Content)); };
            var add = Ui.Button(S["AddToTrip"], "plus", "compact"); add.IsVisible = shown.Document is not null;
            add.Click += async (_, _) => await M.AddDocumentAsync(attachment.FileName, media, attachment.Content, owners);
            AutomationProperties.SetName(view, S["OpenFile"] + ": " + attachment.FileName); AutomationProperties.SetName(add, S["AddToTrip"] + ": " + attachment.FileName);
            list.Children.Add(Ui.Columns("Auto,*,Auto", Ui.Icon(icon, 16, "Text.Subtle"), Ui.V(1, Ui.Line(attachment.FileName, "title"), Ui.Text(Formats.Bytes(attachment.Content.Length), "caption")).Margin(10, 0), Ui.H(6, view, add)));
        }
        var all = Ui.Button(S["AddAllToTrip"], "plus", "compact"); all.IsVisible = shown.Document is not null && mail.Attachments.Count > 1;
        all.Click += async (_, _) => { foreach (var a in mail.Attachments) await M.AddDocumentAsync(a.FileName, a.MediaType == "application/octet-stream" ? MediaTypes.FromFileName(a.FileName) : a.MediaType, a.Content, owners); };
        return Ui.Card(Ui.V(10, Ui.Columns("*,Auto", Ui.Text(S["Attachments"], "strong"), all), list), 16);
    }
}
