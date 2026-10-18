using System.Runtime.InteropServices;

namespace Jourfold.Infrastructure;

/// <summary>A rendered PDF page: 32-bit BGRA pixels with premultiplied alpha, row by row.</summary>
public sealed record PdfPageImage(int Width, int Height, int Stride, byte[] Pixels);

/// <summary>A found text passage: its page and the rectangles it covers, in points from the page's top left corner.</summary>
public sealed record PdfMatch(int Page, IReadOnlyList<(double Left, double Top, double Right, double Bottom)> Rectangles);

/// <summary>
/// Renders PDF pages with PDFium, the PDF engine of Chromium, shipped as a native library per platform. PDFium is not
/// thread-safe, so every call takes one process-wide lock. Documents are read from memory; nothing is written to disk
/// and PDF JavaScript is not run.
/// </summary>
public sealed class PdfiumDocument : IDisposable
{
    private static readonly Lock Gate = new();
    private static bool initialized;
    private readonly GCHandle pinned;
    private IntPtr document;

    private PdfiumDocument(GCHandle pinned, IntPtr document) { this.pinned = pinned; this.document = document; }

    /// <summary>Whether the PDFium library can be loaded on this system.</summary>
    public static bool IsAvailable
    {
        get
        {
            try { lock (Gate) { Initialize(); } return true; }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException) { return false; }
        }
    }

    private static void Initialize() { if (initialized) return; Native.FPDF_InitLibrary(); initialized = true; }

    /// <summary>Opens a PDF from its bytes, or throws <see cref="InvalidDataException"/> when it cannot be read (for example when it is encrypted).</summary>
    public static PdfiumDocument Open(byte[] bytes)
    {
        lock (Gate)
        {
            Initialize();
            // PDFium reads from the buffer for as long as the document is open, so it stays pinned.
            var handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
            var document = Native.FPDF_LoadMemDocument(handle.AddrOfPinnedObject(), bytes.Length, null);
            if (document == IntPtr.Zero) { handle.Free(); throw new InvalidDataException("PDF error " + Native.FPDF_GetLastError()); }
            return new PdfiumDocument(handle, document);
        }
    }

    public int PageCount { get { lock (Gate) { return Native.FPDF_GetPageCount(Handle); } } }

    /// <summary>The size of a page in points (1/72 inch).</summary>
    public (double Width, double Height) PageSize(int index)
    {
        lock (Gate)
        {
            var page = LoadPage(index);
            try { return (Native.FPDF_GetPageWidthF(page), Native.FPDF_GetPageHeightF(page)); }
            finally { Native.FPDF_ClosePage(page); }
        }
    }

    /// <summary>Renders a page at <paramref name="scale"/> pixels per point on white.</summary>
    public PdfPageImage Render(int index, double scale)
    {
        lock (Gate)
        {
            var page = LoadPage(index);
            try
            {
                var width = Math.Clamp((int)Math.Round(Native.FPDF_GetPageWidthF(page) * scale), 1, 8000); var height = Math.Clamp((int)Math.Round(Native.FPDF_GetPageHeightF(page) * scale), 1, 8000);
                var bitmap = Native.FPDFBitmap_Create(width, height, 1);
                if (bitmap == IntPtr.Zero) throw new InvalidDataException("The page is too large to show.");
                try
                {
                    Native.FPDFBitmap_FillRect(bitmap, 0, 0, width, height, 0xFFFFFFFF);
                    Native.FPDF_RenderPageBitmap(bitmap, page, 0, 0, width, height, 0, RenderAnnotations | RenderLcdText);
                    var stride = Native.FPDFBitmap_GetStride(bitmap); var pixels = new byte[stride * height];
                    Marshal.Copy(Native.FPDFBitmap_GetBuffer(bitmap), pixels, 0, pixels.Length);
                    return new(width, height, stride, pixels);
                }
                finally { Native.FPDFBitmap_Destroy(bitmap); }
            }
            finally { Native.FPDF_ClosePage(page); }
        }
    }

    /// <summary>Finds <paramref name="text"/> on every page, ignoring case.</summary>
    public IReadOnlyList<PdfMatch> Find(string text)
    {
        var result = new List<PdfMatch>();
        if (string.IsNullOrWhiteSpace(text)) return result;
        lock (Gate)
        {
            var count = Native.FPDF_GetPageCount(Handle);
            for (var index = 0; index < count && result.Count < 500; index++)
            {
                var page = LoadPage(index); var height = Native.FPDF_GetPageHeightF(page); var textPage = Native.FPDFText_LoadPage(page);
                try
                {
                    if (textPage == IntPtr.Zero) continue;
                    var search = Native.FPDFText_FindStart(textPage, text, 0, 0);
                    try
                    {
                        while (Native.FPDFText_FindNext(search) && result.Count < 500)
                        {
                            var start = Native.FPDFText_GetSchResultIndex(search); var length = Native.FPDFText_GetSchCount(search);
                            var rectangles = new List<(double, double, double, double)>();
                            var rects = Native.FPDFText_CountRects(textPage, start, length);
                            for (var r = 0; r < rects; r++)
                                if (Native.FPDFText_GetRect(textPage, r, out var left, out var top, out var right, out var bottom)) rectangles.Add((left, height - top, right, height - bottom));
                            result.Add(new(index, rectangles));
                        }
                    }
                    finally { Native.FPDFText_FindClose(search); }
                }
                finally { if (textPage != IntPtr.Zero) Native.FPDFText_ClosePage(textPage); Native.FPDF_ClosePage(page); }
            }
        }
        return result;
    }

    private IntPtr Handle => document != IntPtr.Zero ? document : throw new ObjectDisposedException(nameof(PdfiumDocument));

    private IntPtr LoadPage(int index)
    {
        var page = Native.FPDF_LoadPage(Handle, index);
        return page != IntPtr.Zero ? page : throw new InvalidDataException("Page " + (index + 1) + " cannot be read.");
    }

    public void Dispose()
    {
        lock (Gate)
        {
            if (document == IntPtr.Zero) return;
            Native.FPDF_CloseDocument(document); document = IntPtr.Zero; pinned.Free();
        }
    }

    private const int RenderAnnotations = 0x01, RenderLcdText = 0x02;

    private static class Native
    {
        private const string Library = "pdfium";
        [DllImport(Library)] public static extern void FPDF_InitLibrary();
        [DllImport(Library)] public static extern uint FPDF_GetLastError();
        [DllImport(Library, CharSet = CharSet.Ansi)] public static extern IntPtr FPDF_LoadMemDocument(IntPtr data, int size, string? password);
        [DllImport(Library)] public static extern void FPDF_CloseDocument(IntPtr document);
        [DllImport(Library)] public static extern int FPDF_GetPageCount(IntPtr document);
        [DllImport(Library)] public static extern IntPtr FPDF_LoadPage(IntPtr document, int index);
        [DllImport(Library)] public static extern void FPDF_ClosePage(IntPtr page);
        [DllImport(Library)] public static extern float FPDF_GetPageWidthF(IntPtr page);
        [DllImport(Library)] public static extern float FPDF_GetPageHeightF(IntPtr page);
        [DllImport(Library)] public static extern IntPtr FPDFBitmap_Create(int width, int height, int alpha);
        [DllImport(Library)] public static extern void FPDFBitmap_FillRect(IntPtr bitmap, int left, int top, int width, int height, uint color);
        [DllImport(Library)] public static extern IntPtr FPDFBitmap_GetBuffer(IntPtr bitmap);
        [DllImport(Library)] public static extern int FPDFBitmap_GetStride(IntPtr bitmap);
        [DllImport(Library)] public static extern void FPDFBitmap_Destroy(IntPtr bitmap);
        [DllImport(Library)] public static extern void FPDF_RenderPageBitmap(IntPtr bitmap, IntPtr page, int x, int y, int width, int height, int rotate, int flags);
        [DllImport(Library)] public static extern IntPtr FPDFText_LoadPage(IntPtr page);
        [DllImport(Library)] public static extern void FPDFText_ClosePage(IntPtr textPage);
        [DllImport(Library, CharSet = CharSet.Unicode)] public static extern IntPtr FPDFText_FindStart(IntPtr textPage, string text, uint flags, int startIndex);
        [DllImport(Library)][return: MarshalAs(UnmanagedType.Bool)] public static extern bool FPDFText_FindNext(IntPtr search);
        [DllImport(Library)] public static extern int FPDFText_GetSchResultIndex(IntPtr search);
        [DllImport(Library)] public static extern int FPDFText_GetSchCount(IntPtr search);
        [DllImport(Library)] public static extern void FPDFText_FindClose(IntPtr search);
        [DllImport(Library)] public static extern int FPDFText_CountRects(IntPtr textPage, int start, int count);
        [DllImport(Library)][return: MarshalAs(UnmanagedType.Bool)] public static extern bool FPDFText_GetRect(IntPtr textPage, int index, out double left, out double top, out double right, out double bottom);
    }
}
