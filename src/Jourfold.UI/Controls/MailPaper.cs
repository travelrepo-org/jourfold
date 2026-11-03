using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Jourfold.Application;
using TheArtOfDev.HtmlRenderer.Avalonia;
using TravelRepo.Documents;

namespace Jourfold.UI.Controls;

/// <summary>
/// An e-mail in its original layout, the same on the desktop and the phone, cleaned by TravelRepo's
/// <see cref="MailHtml"/>. It sits on white paper in both themes, because e-mails are designed for a white background.
/// Images come from the message itself, and from the internet only after the person allowed it. An e-mail laid out
/// wider than the screen, as many are, is shrunk to fit instead of being cut off.
/// </summary>
public static class MailPaper
{
    private const int MaxImageBytes = 5 * 1024 * 1024;

    /// <param name="remote">Whether this e-mail's images may come from the internet.</param>
    /// <param name="fetch">Loads an image from the internet; only asked when <paramref name="remote"/> is true.</param>
    /// <param name="link">A link was tapped that may be opened; the app asks before it opens it.</param>
    /// <param name="blocked">A link was tapped that may not be opened from Jourfold.</param>
    public static Control Build(MailMessage mail, bool remote, Func<string, Task<byte[]?>> fetch, Action<string> link, Action blocked, string name, double fontSize = 10, double padding = 20)
    {
        var safe = MailHtml.Sanitize(mail.Html!, remote);
        var html = new HtmlLabel { Text = safe.Html, IsContextMenuEnabled = false, IsSelectionEnabled = true, Background = Brushes.White, AutoSize = false, AutoSizeHeightOnly = true };
        html.BaseStylesheet = $"body {{ font-family: 'Plus Jakarta Sans', sans-serif; font-size: {fontSize.ToString(System.Globalization.CultureInfo.InvariantCulture)}pt; color: #1f2933; margin: 0; }} a {{ color: #0b6e6e; }} img {{ max-width: 100%; }}";
        html.StylesheetLoad += (_, e) => e.Event.SetStyleSheet = "";
        html.ImageLoad += (_, e) =>
        {
            var load = e.Event; load.Handled = true; var src = load.Src?.Trim() ?? "";
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
                _ = fetch(src).ContinueWith(t => Dispatcher.UIThread.Post(() => { if (t.Result is { } bytes && Decode(bytes) is { } bitmap) load.Callback(bitmap); else load.Callback(); }), TaskScheduler.Default);
            else load.Callback();
        };
        // The renderer's own link click does not reliably come through on a touch screen, so a tap asks it which link
        // is there; its own handling is switched off, so it never opens a link by itself.
        html.LinkClicked += (_, e) => e.Event.Handled = true;
        html.Tapped += (_, e) =>
        {
            if (html.Container?.GetLinkAt(e.GetPosition(html))?.Trim() is not { Length: > 0 } target) return;
            e.Handled = true;
            if (MailHtml.IsOpenableLink(target)) link(target); else blocked();
        };
        AutomationProperties.SetName(html, name + ": " + (mail.Text.Length > 300 ? mail.Text[..300] : mail.Text));
        return new Border { Background = Brushes.White, CornerRadius = new CornerRadius(10), Padding = new Thickness(padding), Child = new FitWidth(html), ClipToBounds = true, BorderThickness = new Thickness(1) }.Res(Border.BorderBrushProperty, "Line");
    }

    /// <summary>An image from the internet for an e-mail, at most 5 MB and only when it is one; null otherwise.</summary>
    public static async Task<byte[]?> DownloadImageAsync(HttpClient http, string url)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url); OnlineIdentity.Apply(request);
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            if (!response.IsSuccessStatusCode || response.Content.Headers.ContentType?.MediaType?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) != true || response.Content.Headers.ContentLength > MaxImageBytes) return null;
            var bytes = await response.Content.ReadAsByteArrayAsync();
            return bytes.Length > MaxImageBytes ? null : bytes;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException or UriFormatException) { return null; }
    }

    private static Bitmap? Decode(byte[] bytes)
    {
        try { using var stream = new MemoryStream(bytes); return new Bitmap(stream); }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException or InvalidOperationException) { return null; }
    }

    /// <summary>
    /// Lays the e-mail out in the width there is; when its tables or images are wider than that, lays it out at their
    /// width and shrinks the whole of it to fit.
    /// </summary>
    private sealed class FitWidth : Decorator
    {
        private readonly HtmlLabel html;
        private double natural, scale = 1;

        public FitWidth(HtmlLabel html) { this.html = html; Child = html; html.RenderTransformOrigin = new RelativePoint(0, 0, RelativeUnit.Absolute); }

        protected override Size MeasureOverride(Size availableSize)
        {
            var width = double.IsFinite(availableSize.Width) ? availableSize.Width : 600;
            html.Measure(new Size(width, double.PositiveInfinity));
            natural = width; scale = 1;
            if (html.Container is { } container && container.ActualSize.Width > width + 1)
            {
                natural = Math.Ceiling(container.ActualSize.Width);
                html.Measure(new Size(natural, double.PositiveInfinity));
                scale = width / natural;
            }
            html.RenderTransform = scale < 1 ? new ScaleTransform(scale, scale) : null;
            return new Size(width, html.DesiredSize.Height * scale);
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            html.Arrange(new Rect(0, 0, natural, html.DesiredSize.Height));
            return finalSize;
        }
    }
}
