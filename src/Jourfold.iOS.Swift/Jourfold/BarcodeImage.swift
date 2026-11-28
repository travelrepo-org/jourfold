import CoreImage.CIFilterBuiltins
import SwiftUI

/// The barcode of a ticket, drawn by iOS and large enough to scan: QR, Aztec, PDF417 and Code 128, as Wallet passes have.
enum BarcodeImage {
    static func make(_ message: String, format: String, encoding: String) -> UIImage? {
        let set: String.Encoding = ["utf-8", "utf8"].contains(encoding.lowercased()) ? .utf8 : .isoLatin1
        guard let data = message.data(using: set) ?? message.data(using: .utf8) else { return nil }
        let name = format.lowercased()
        let filter: CIFilter?
        if name.contains("qr") { let f = CIFilter.qrCodeGenerator(); f.message = data; f.correctionLevel = "M"; filter = f }
        else if name.contains("aztec") { let f = CIFilter.aztecCodeGenerator(); f.message = data; filter = f }
        else if name.contains("pdf417") { let f = CIFilter.pdf417BarcodeGenerator(); f.message = data; filter = f }
        else if name.contains("128") { let f = CIFilter.code128BarcodeGenerator(); f.message = message.data(using: .ascii) ?? data; f.quietSpace = 7; filter = f }
        else { return nil }
        guard let image = filter?.outputImage else { return nil }
        // Whole pixels per module, so the edges stay sharp.
        let scale = max(1, (1200 / max(image.extent.width, image.extent.height)).rounded(.down))
        let large = image.transformed(by: CGAffineTransform(scaleX: scale, y: scale))
        return CIContext().createCGImage(large, from: large.extent).map { UIImage(cgImage: $0) }
    }
}
