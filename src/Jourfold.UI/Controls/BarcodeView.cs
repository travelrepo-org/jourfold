using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using ZXing;
using ZXing.Common;

namespace Jourfold.UI.Controls;

/// <summary>
/// A barcode to scan from the screen, such as a boarding pass: QR, Aztec, PDF417 or Code 128, drawn in black on white
/// with a quiet zone, as large as the width allows. Scanners need the contrast, so it ignores the theme.
/// </summary>
public sealed class BarcodeView : Control
{
    private readonly BitMatrix matrix;

    /// <param name="format">qr, aztec, pdf417 or code128, as <see cref="TravelRepo.Documents.WalletBarcode"/> names them.</param>
    public BarcodeView(string message, string format, string encoding = "iso-8859-1")
    {
        var kind = format switch { "qr" => BarcodeFormat.QR_CODE, "aztec" => BarcodeFormat.AZTEC, "pdf417" => BarcodeFormat.PDF_417, _ => BarcodeFormat.CODE_128 };
        var hints = new Dictionary<EncodeHintType, object> { [EncodeHintType.CHARACTER_SET] = encoding, [EncodeHintType.MARGIN] = 2 };
        matrix = new MultiFormatWriter().encode(message, kind, 0, 0, hints);
        OneDimensional = kind == BarcodeFormat.CODE_128;
    }

    /// <summary>A barcode of bars only, which is drawn as tall as it is a fifth of its width.</summary>
    public bool OneDimensional { get; }

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsInfinity(availableSize.Width) ? 320 : availableSize.Width;
        var height = OneDimensional ? width / 5 : width * matrix.Height / matrix.Width;
        return new Size(width, Math.Min(height, double.IsInfinity(availableSize.Height) ? height : availableSize.Height));
    }

    public override void Render(DrawingContext context)
    {
        var bounds = Bounds;
        context.FillRectangle(Brushes.White, new Rect(bounds.Size));
        var module = Math.Min(bounds.Width / matrix.Width, OneDimensional ? double.MaxValue : bounds.Height / matrix.Height);
        var left = (bounds.Width - module * matrix.Width) / 2;
        var rows = OneDimensional ? 1 : matrix.Height; var rowHeight = OneDimensional ? bounds.Height : module;
        var top = OneDimensional ? 0 : (bounds.Height - module * matrix.Height) / 2;
        for (var y = 0; y < rows; y++)
            for (var x = 0; x < matrix.Width; x++)
            {
                if (!matrix[x, y]) continue;
                // Neighbouring modules are drawn as one, so there are no hairlines between them.
                var run = 1; while (x + run < matrix.Width && matrix[x + run, y]) run++;
                context.FillRectangle(Brushes.Black, new Rect(left + x * module, top + y * rowHeight, run * module, rowHeight));
                x += run - 1;
            }
    }
}
