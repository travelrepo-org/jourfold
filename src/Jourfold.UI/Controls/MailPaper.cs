using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.GestureRecognizers;
using Avalonia.Interactivity;
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
/// wider than the screen, as many are, is shrunk to fit instead of being cut off; on a touch screen two fingers or a
/// double tap make it larger again.
/// </summary>
public static class MailPaper
{
    private const int MaxImageBytes = 5 * 1024 * 1024;

    /// <param name="remote">Whether this e-mail's images may come from the internet.</param>
    /// <param name="fetch">Loads an image from the internet; only asked when <paramref name="remote"/> is true.</param>
    /// <param name="link">A link was tapped that may be opened; the app asks before it opens it.</param>
    /// <param name="blocked">A link was tapped that may not be opened from Jourfold.</param>
    /// <param name="zoomable">Whether two fingers and a double tap zoom the e-mail, for the phone where it is shrunk most.</param>
    public static Control Build(MailMessage mail, bool remote, Func<string, Task<byte[]?>> fetch, Action<string> link, Action blocked, string name, double fontSize = 10, double padding = 20, bool zoomable = false)
    {
        var safe = MailHtml.Sanitize(mail.Html!, remote);
        // Where a double tap zooms, it does not also select a word; the e-mail's Text view is there to copy from.
        var html = new HtmlLabel { Text = safe.Html, IsContextMenuEnabled = false, IsSelectionEnabled = !zoomable, Background = Brushes.White, AutoSize = false, AutoSizeHeightOnly = true };
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
        var fit = new FitWidth(html);
        if (zoomable) Zoomable(fit);
        return new Border { Background = Brushes.White, CornerRadius = new CornerRadius(10), Padding = new Thickness(padding), Child = fit, ClipToBounds = true, BorderThickness = new Thickness(1) }.Res(Border.BorderBrushProperty, "Line");
    }

    /// <summary>
    /// Two fingers zoom the e-mail around the point between them, a double tap zooms in or back, and a zoomed e-mail
    /// moves sideways with one finger. Up and down stays with the page, which scrolls the e-mail as before.
    /// </summary>
    private static void Zoomable(FitWidth fit)
    {
        fit.GestureRecognizers.Add(new PinchGestureRecognizer());
        double? pinchFrom = null;
        fit.AddHandler(InputElement.PinchEvent, (_, e) =>
        {
            pinchFrom ??= fit.Zoom;
            fit.ZoomAt(pinchFrom.Value * e.Scale, e.ScaleOrigin.X);
            e.Handled = true;
        });
        fit.AddHandler(InputElement.PinchEndedEvent, (_, _) => pinchFrom = null);
        fit.DoubleTapped += (_, e) => { fit.ZoomAt(fit.Zoom > 1 ? 1 : 2, e.GetPosition(fit).X); e.Handled = true; };

        // One finger moves a zoomed e-mail sideways; a second finger is the pinch's. The page may take the finger to
        // scroll, so its release need not come here: the first finger of a touch is known by being the primary one.
        IPointer? finger = null; double lastX = 0;
        fit.AddHandler(InputElement.PointerPressedEvent, (_, e) =>
        {
            if (e.Pointer.Type == PointerType.Mouse) return;
            if (e.Pointer.IsPrimary) { finger = e.Pointer; lastX = e.GetPosition(fit).X; } else finger = null;
        }, RoutingStrategies.Tunnel, handledEventsToo: true);
        fit.AddHandler(InputElement.PointerMovedEvent, (_, e) =>
        {
            if (e.Pointer != finger || pinchFrom is not null || fit.Zoom <= 1) return;
            var x = e.GetPosition(fit).X; fit.Pan += lastX - x; lastX = x;
        }, RoutingStrategies.Tunnel, handledEventsToo: true);
        void Lift(IPointer pointer) { if (pointer == finger) finger = null; }
        fit.AddHandler(InputElement.PointerReleasedEvent, (_, e) => Lift(e.Pointer), RoutingStrategies.Tunnel, handledEventsToo: true);
        fit.AddHandler(InputElement.PointerCaptureLostEvent, (_, e) => Lift(e.Pointer), RoutingStrategies.Bubble, handledEventsToo: true);
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
    /// width and shrinks the whole of it to fit. A zoom makes the fitted e-mail larger again without laying it out anew,
    /// and the part shown of its width follows <see cref="Pan"/>.
    /// </summary>
    private sealed class FitWidth : Decorator
    {
        private readonly HtmlLabel html;
        private double natural, scale = 1, view, zoom = 1, pan;

        public FitWidth(HtmlLabel html) { this.html = html; Child = html; ClipToBounds = true; html.RenderTransformOrigin = new RelativePoint(0, 0, RelativeUnit.Absolute); }

        /// <summary>How many times larger than fitting the e-mail is shown, from 1 to 4.</summary>
        public double Zoom { get => zoom; private set { zoom = Math.Clamp(value, 1, 4); InvalidateMeasure(); } }

        /// <summary>How far the zoomed e-mail is moved to the left, in screen units.</summary>
        public double Pan { get => pan; set { pan = value; Place(); } }

        /// <summary>Zooms so that what is at <paramref name="x"/> stays under it.</summary>
        public void ZoomAt(double to, double x)
        {
            var spot = (pan + x) / zoom;
            Zoom = to;
            pan = spot * zoom - x; Place();
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            var width = double.IsFinite(availableSize.Width) ? availableSize.Width : 600;
            html.Measure(new Size(width, double.PositiveInfinity));
            natural = width; scale = 1; view = width;
            if (html.Container is { } container && container.ActualSize.Width > width + 1)
            {
                natural = Math.Ceiling(container.ActualSize.Width);
                html.Measure(new Size(natural, double.PositiveInfinity));
                scale = width / natural;
            }
            Place();
            return new Size(width, html.DesiredSize.Height * scale * zoom);
        }

        private void Place()
        {
            pan = Math.Clamp(pan, 0, Math.Max(0, view * zoom - view));
            var total = scale * zoom;
            html.RenderTransform = total == 1 && pan == 0 ? null : new MatrixTransform(Matrix.CreateScale(total, total) * Matrix.CreateTranslation(-pan, 0));
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            html.Arrange(new Rect(0, 0, natural, html.DesiredSize.Height));
            return finalSize;
        }
    }
}
