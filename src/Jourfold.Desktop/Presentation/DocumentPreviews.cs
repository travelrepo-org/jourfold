using System.Collections.Concurrent;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Jourfold.Infrastructure;
using TravelRepo.Core;
using TravelRepo.Documents;

namespace Jourfold.Desktop;

/// <summary>
/// Reads attached files for display: e-mails, PDF thumbnails and short descriptions. Results are cached by content
/// hash, so a file is parsed or rendered once per session; PDF thumbnails are also kept in the local cache folder.
/// </summary>
public static class DocumentPreviews
{
    private static readonly ConcurrentDictionary<string, MailMessage?> Mails = new();
    private static readonly ConcurrentDictionary<string, int> PageCounts = new();

    public static string Media(Entity document) => document.Data["media_type"]?.ToString() ?? "application/octet-stream";
    public static string Hash(Entity document) => document.Data["blob"]?["hash"]?.ToString() ?? "";
    public static bool IsPdf(Entity document) => Media(document) == "application/pdf";
    public static bool IsMail(Entity document) => Mail.IsMail(Media(document));
    public static bool IsImage(Entity document) => Media(document).StartsWith("image/", StringComparison.Ordinal);
    /// <summary>Whether Jourfold shows the file itself instead of handing it to another program.</summary>
    public static bool CanView(Entity document) => IsMail(document) || IsImage(document) || IsPdf(document) && PdfiumDocument.IsAvailable;

    public static byte[]? Bytes(MainViewModel m, Entity document)
    {
        try { return m.Workspace is { } w ? File.ReadAllBytes(w.Repository.DocumentPath(document)) : null; }
        catch (Exception ex) when (ex is DomainException or IOException or UnauthorizedAccessException) { return null; }
    }

    public static MailMessage? MailOf(MainViewModel m, Entity document)
    {
        if (!IsMail(document)) return null;
        return Mails.GetOrAdd(Hash(document), _ => Bytes(m, document) is { } bytes ? Read(bytes) : null);
    }

    public static MailMessage? Read(byte[] bytes)
    {
        try { return Mail.Read(bytes); } catch (DomainException) { return null; }
    }

    /// <summary>The subject of an e-mail, otherwise the file name.</summary>
    public static string Title(MainViewModel m, Entity document) => MailOf(m, document)?.Subject is { Length: > 0 } subject ? subject : document.Title;

    /// <summary>A short line under the title: sender and date of an e-mail, or the type, pages and size of a file.</summary>
    public static string Summary(MainViewModel m, Entity document)
    {
        var s = m.Strings;
        if (MailOf(m, document) is { } mail)
            return string.Join(" · ", new[] { s["EmailMessage"], mail.From.FirstOrDefault() is { } from ? from.Name ?? from.Address : null, mail.Date is { } date ? Formats.ShortDate(NodaTime.LocalDate.FromDateTime(date.LocalDateTime)) : null, mail.Attachments.Count == 1 ? s["AttachmentCountOne"] : mail.Attachments.Count > 1 ? string.Format(s["AttachmentCount"], mail.Attachments.Count) : null }.Where(x => !string.IsNullOrEmpty(x)));
        var size = m.Workspace is { } w && TryPath(w, document) is { } path && File.Exists(path) ? Formats.Bytes(new FileInfo(path).Length) : null;
        var pages = IsPdf(document) ? PageCount(m, document) : 0;
        return string.Join(" · ", new[] { IsPdf(document) ? "PDF" : Media(document).Split('/').LastOrDefault()?.ToUpperInvariant(), pages > 1 ? string.Format(s["PageCount"], pages) : null, size }.Where(x => !string.IsNullOrEmpty(x)));
    }

    private static string? TryPath(Jourfold.Application.Workspace w, Entity document) { try { return w.Repository.DocumentPath(document); } catch (DomainException) { return null; } }

    public static int PageCount(MainViewModel m, Entity document) => PageCounts.GetOrAdd(Hash(document), _ =>
    {
        if (!PdfiumDocument.IsAvailable || Bytes(m, document) is not { } bytes) return 0;
        try { using var pdf = PdfiumDocument.Open(bytes); return pdf.PageCount; } catch (InvalidDataException) { return 0; }
    });

    /// <summary>The first page of a PDF, <paramref name="width"/> pixels wide, from the local cache when it was rendered before.</summary>
    public static Bitmap? PdfThumbnail(MainViewModel m, Entity document, int width)
    {
        if (!IsPdf(document) || Hash(document).Length != 64) return null;
        var cache = Path.Combine(m.Store.Root, "thumbnails", Hash(document) + "-" + width + ".png");
        try
        {
            if (File.Exists(cache)) return new Bitmap(cache);
            if (!PdfiumDocument.IsAvailable || Bytes(m, document) is not { } bytes) return null;
            using var pdf = PdfiumDocument.Open(bytes); if (pdf.PageCount == 0) return null;
            var (pageWidth, _) = pdf.PageSize(0);
            var bitmap = ToBitmap(pdf.Render(0, width / Math.Max(1, pageWidth)), 1);
            Directory.CreateDirectory(Path.GetDirectoryName(cache)!); bitmap.Save(cache, PngBitmapEncoderOptions.Default);
            return bitmap;
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>Turns rendered pixels into a bitmap. <paramref name="scaling"/> is the screen's render scaling, so the bitmap is sharp at its logical size.</summary>
    public static WriteableBitmap ToBitmap(PdfPageImage image, double scaling)
    {
        var bitmap = new WriteableBitmap(new PixelSize(image.Width, image.Height), new Vector(96 * scaling, 96 * scaling), PixelFormat.Bgra8888, AlphaFormat.Premul);
        using var buffer = bitmap.Lock();
        for (var row = 0; row < image.Height; row++)
            System.Runtime.InteropServices.Marshal.Copy(image.Pixels, row * image.Stride, buffer.Address + row * buffer.RowBytes, Math.Min(image.Stride, buffer.RowBytes));
        return bitmap;
    }
}
