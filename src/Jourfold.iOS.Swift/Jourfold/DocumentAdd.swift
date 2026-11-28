import AVFoundation
import PhotosUI
import SwiftUI
import TravelRepoKit
import UniformTypeIdentifiers
import VisionKit

/// Adding a document in Quick Add: a file from Files, a scan made with the camera (saved as a PDF), or a confirmation
/// e-mail copied to the clipboard. Each goes into the trip through TravelRepo's import.
struct DocumentAddSection: View {
    let tripID: UUID
    /// Where everything goes when it was opened from a plan or a booking; otherwise the person chooses.
    var target: UUID? = nil
    let done: () -> Void
    @State private var chosen = ""
    @State private var photo: PhotosPickerItem?
    @Environment(TripStore.self) private var store
    @State private var importing = false
    @State private var scanning = false
    @State private var busy = false

    var body: some View {
        // Each presentation hangs on one button: on a section, SwiftUI gives it to every row, and they get in each other's way.
        if target == nil, let trip = store.trips.first(where: { $0.id == tripID }) {
            Section {
                Picker("Add to", selection: $chosen) {
                    Text("Only the trip's files").tag("")
                    Text("New booking").tag("new")
                    Section("Plans") { ForEach(trip.items.filter { $0.when.isScheduled }.sorted { ($0.when.start ?? .distantFuture) < ($1.when.start ?? .distantFuture) }) { i in
                        Text((i.when.start.map { Fmt.shortDay($0, i.when.zone) + " · " } ?? "") + i.title).tag(TravelRepoBridge.key(i.id))
                    } }
                    Section("Bookings") { ForEach(trip.bookings) { Text($0.title).tag(TravelRepoBridge.key($0.id)) } }
                }
                .pickerStyle(.navigationLink)
            }
        }
        Section {
            Button { importing = true } label: { Label("Choose a file", systemImage: "folder") }
                .fileImporter(isPresented: $importing, allowedContentTypes: [.item], allowsMultipleSelection: true) { result in
                    guard case .success(let urls) = result else { return }
                    Task {
                        for url in urls {
                            let scoped = url.startAccessingSecurityScopedResource()
                            defer { if scoped { url.stopAccessingSecurityScopedResource() } }
                            guard let data = try? Data(contentsOf: url) else { continue }
                            await add(data, name: url.lastPathComponent, type: UTType(filenameExtension: url.pathExtension)?.preferredMIMEType)
                        }
                        done()
                    }
                }
            if VNDocumentCameraViewController.isSupported {
                Button { Task { await scan() } } label: { Label("Scan a document", systemImage: "doc.viewfinder") }
                    .fullScreenCover(isPresented: $scanning) {
                        DocumentScanner { result in
                            scanning = false
                            switch result {
                            case .success(nil): TestReport.note("scan cancelled")
                            case .failure(let error):
                                TestReport.note("scan failed \((error as NSError).domain) \((error as NSError).code)")
                                store.problem = String(localized: "The scan did not work: \(error.localizedDescription)")
                            case .success(let pdf?):
                                TestReport.note("scan made, \(pdf.count) bytes")
                                Task {
                                    await add(pdf, name: String(localized: "Scan \(Date().formatted(date: .abbreviated, time: .shortened))").replacingOccurrences(of: "/", with: "-").replacingOccurrences(of: ":", with: ".") + ".pdf", type: "application/pdf")
                                    done()
                                }
                            }
                        }
                        .ignoresSafeArea()
                    }
            }
            PhotosPicker(selection: $photo, matching: .images) { Label("Choose a photo", systemImage: "photo") }
                .onChange(of: photo) { _, item in
                    guard let item else { return }
                    photo = nil
                    Task {
                        guard let data = try? await item.loadTransferable(type: Data.self), let jpeg = UIImage(data: data)?.jpegData(compressionQuality: 0.8) else { store.problem = String(localized: "The photo could not be read."); return }
                        await add(jpeg, name: String(localized: "Photo") + ".jpg", type: "image/jpeg"); done()
                    }
                }
            Button { Task { await paste() } } label: { Label("Paste a confirmation e-mail", systemImage: "doc.on.clipboard") }
                .disabled(!UIPasteboard.general.hasStrings || busy)
        } footer: { Footnote(target == nil ? "The file is kept with the trip. With a plan or booking chosen, it also shows there." : "The file is kept with the trip and shows here.") }
    }

    private func scan() async {
        guard await AVCaptureDevice.requestAccess(for: .video) else {
            TestReport.note("scan without camera access")
            store.problem = String(localized: "Jourfold may not use the camera. Allow it under Settings → Apps → Jourfold."); return
        }
        TestReport.note("scan opened")
        scanning = true
    }

    /// The clipboard's text as a document: an e-mail with its headers stays one, other text becomes a text file.
    private func paste() async {
        guard let text = UIPasteboard.general.string, !text.isEmpty else { return }
        let looksLikeMail = text.range(of: "^(From|Subject|Date):", options: [.regularExpression, .anchored]) != nil
        let subject = text.split(separator: "\n").first { $0.hasPrefix("Subject:") }.map { $0.dropFirst(8).trimmingCharacters(in: .whitespaces) }
        let name = (subject ?? String(localized: "Confirmation")) + (looksLikeMail ? ".eml" : ".txt")
        await add(Data(text.utf8), name: name, type: looksLikeMail ? "message/rfc822" : "text/plain")
        done()
    }

    private func add(_ data: Data, name: String, type: String?) async {
        busy = true; defer { busy = false }
        guard let bridge = TravelRepoBridge.all[tripID] else { return }
        do {
            var to = target ?? UUID(uuidString: chosen)
            if target == nil, chosen == "new" {
                let title = (name as NSString).deletingPathExtension
                to = (try await bridge.create("booking", ["title": .string(title), "status": "confirmed"])).flatMap(UUID.init(uuidString:))
                // Further files of the same choice belong to the same booking.
                if let to { chosen = TravelRepoBridge.key(to) }
            }
            try await bridge.importFile(data, name: name, mediaType: type, attachTo: to)
            UINotificationFeedbackGenerator().notificationOccurred(.success)
        } catch { store.problem = TravelRepoBridge.message(error) }
    }
}

/// Adding a file, scan or photo to a plan or booking.
struct AddDocumentSheet: View {
    let tripID: UUID
    let target: UUID
    @Environment(\.dismiss) private var dismiss
    var body: some View {
        NavigationStack {
            JFForm { DocumentAddSection(tripID: tripID, target: target) { dismiss() } }
                .jfBackground()
                .navigationTitle("Add a file or scan")
                .navigationBarTitleDisplayMode(.inline)
                .toolbar { ToolbarItem(placement: .cancellationAction) { Button(role: .cancel) { dismiss() } } }
        }
        .presentationDetents([.medium, .large])
    }
}

/// Puts a file that is already in the trip with a plan or booking.
struct AttachSheet: View {
    let tripID: UUID
    let document: TripDocument
    @Environment(\.dismiss) private var dismiss
    @Environment(TripStore.self) private var store
    var body: some View {
        let trip = store.trips.first { $0.id == tripID }
        NavigationStack {
            JFList {
                Section {
                    ForEach((trip?.items ?? []).filter { $0.when.isScheduled }.sorted { ($0.when.start ?? .distantFuture) < ($1.when.start ?? .distantFuture) }) { i in
                        Button { attach(i.id) } label: { LabeledContent { Text(i.when.start.map { Fmt.shortDay($0, i.when.zone) } ?? "") } label: { Text(i.title).foregroundStyle(Color.jfText) } }
                    }
                } header: { Overline("Plans") }
                Section { ForEach(trip?.bookings ?? []) { x in Button { attach(x.id) } label: { Text(x.title).foregroundStyle(Color.jfText) } } } header: { Overline("Bookings") }
            }
            .listStyle(.insetGrouped)
            .jfBackground()
            .navigationTitle(document.name)
            .navigationBarTitleDisplayMode(.inline)
            .toolbar { ToolbarItem(placement: .cancellationAction) { Button(role: .cancel) { dismiss() } } }
        }
    }
    private func attach(_ id: UUID) {
        Task { if await TravelRepoBridge.all[tripID]?.attach(document, to: id) == true { dismiss() } }
    }
}

/// VisionKit's document camera; the pages come back as one PDF.
private struct DocumentScanner: UIViewControllerRepresentable {
    let done: (Result<Data?, Error>) -> Void
    func makeCoordinator() -> Coordinator { Coordinator(done: done) }
    func makeUIViewController(context: Context) -> VNDocumentCameraViewController {
        let c = VNDocumentCameraViewController(); c.delegate = context.coordinator; return c
    }
    func updateUIViewController(_ controller: VNDocumentCameraViewController, context: Context) {}

    final class Coordinator: NSObject, VNDocumentCameraViewControllerDelegate {
        let done: (Result<Data?, Error>) -> Void
        init(done: @escaping (Result<Data?, Error>) -> Void) { self.done = done }
        func documentCameraViewControllerDidCancel(_ controller: VNDocumentCameraViewController) { done(.success(nil)) }
        func documentCameraViewController(_ controller: VNDocumentCameraViewController, didFailWithError error: Error) { done(.failure(error)) }
        func documentCameraViewController(_ controller: VNDocumentCameraViewController, didFinishWith scan: VNDocumentCameraScan) {
            guard scan.pageCount > 0 else { done(.success(nil)); return }
            let bounds = CGRect(origin: .zero, size: scan.imageOfPage(at: 0).size)
            let pdf = UIGraphicsPDFRenderer(bounds: bounds).pdfData { context in
                for i in 0..<scan.pageCount {
                    let page = scan.imageOfPage(at: i)
                    context.beginPage(withBounds: CGRect(origin: .zero, size: page.size), pageInfo: [:])
                    // As JPEG: a page of the camera as it is makes a PDF of tens of megabytes.
                    (page.jpegData(compressionQuality: 0.7).flatMap(UIImage.init(data:)) ?? page).draw(in: CGRect(origin: .zero, size: page.size))
                }
            }
            done(.success(pdf))
        }
    }
}
