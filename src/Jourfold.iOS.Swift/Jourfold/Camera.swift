import AVFoundation
import SwiftUI
import UIKit

/// "Take a photo", such as of a receipt: upright, at most 1600 pixels on its longer side, as JPEG.
struct CameraButton: View {
    let title: LocalizedStringKey
    let taken: (Data) -> Void
    @Environment(TripStore.self) private var store
    @State private var open = false

    var body: some View {
        if UIImagePickerController.isSourceTypeAvailable(.camera) {
            Button { Task { await ask() } } label: { Label(title, systemImage: "camera") }
                .fullScreenCover(isPresented: $open) {
                    Camera { image in
                        open = false
                        if let data = image.flatMap(Self.jpeg) { taken(data) }
                    }
                    .ignoresSafeArea()
                }
        }
    }

    private func ask() async {
        guard await AVCaptureDevice.requestAccess(for: .video) else { store.problem = String(localized: "Jourfold may not use the camera. Allow it under Settings → Apps → Jourfold."); return }
        open = true
    }

    static func jpeg(_ image: UIImage) -> Data? {
        let longest = max(image.size.width, image.size.height)
        guard longest > 0 else { return nil }
        let scale = min(1, 1600 / longest), size = CGSize(width: image.size.width * scale, height: image.size.height * scale)
        let format = UIGraphicsImageRendererFormat(); format.scale = 1; format.opaque = true
        return UIGraphicsImageRenderer(size: size, format: format).image { _ in image.draw(in: CGRect(origin: .zero, size: size)) }.jpegData(compressionQuality: 0.8)
    }
}

private struct Camera: UIViewControllerRepresentable {
    let done: (UIImage?) -> Void
    func makeCoordinator() -> Coordinator { Coordinator(done: done) }
    func makeUIViewController(context: Context) -> UIImagePickerController {
        let picker = UIImagePickerController(); picker.sourceType = .camera; picker.delegate = context.coordinator; return picker
    }
    func updateUIViewController(_ controller: UIImagePickerController, context: Context) {}
    final class Coordinator: NSObject, UIImagePickerControllerDelegate, UINavigationControllerDelegate {
        let done: (UIImage?) -> Void
        init(done: @escaping (UIImage?) -> Void) { self.done = done }
        func imagePickerController(_ picker: UIImagePickerController, didFinishPickingMediaWithInfo info: [UIImagePickerController.InfoKey: Any]) { done(info[.originalImage] as? UIImage) }
        func imagePickerControllerDidCancel(_ picker: UIImagePickerController) { done(nil) }
    }
}
