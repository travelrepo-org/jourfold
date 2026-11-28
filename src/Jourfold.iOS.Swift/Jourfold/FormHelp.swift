import PhotosUI
import SwiftUI
import UniformTypeIdentifiers

/// Under a field that is still needed, after the person tried to save: what is missing, with a sign and in words.
struct Missing: View {
    let text: LocalizedStringKey
    let when: Bool
    var body: some View {
        if when { Label(text, systemImage: "exclamationmark.circle.fill").font(.jf(.footnote, .semibold)).foregroundStyle(Color.jfDanger).transition(.opacity) }
    }
}

/// A file chosen while something is being added; it goes into the trip once there is something to put it with.
struct PendingFile: Identifiable {
    let id = UUID()
    var name: String
    var type: String?
    var data: Data
}

struct PendingFilesSection: View {
    @Binding var files: [PendingFile]
    @Environment(TripStore.self) private var store
    @State private var importing = false
    @State private var photo: PhotosPickerItem?

    var body: some View {
        Section {
            ForEach(files) { file in
                HStack {
                    Label(file.name, systemImage: "doc").lineLimit(1)
                    Spacer()
                    Button { withAnimation { files.removeAll { $0.id == file.id } } } label: { Image(systemName: "minus.circle").frame(minWidth: 44, minHeight: 44) }
                        .buttonStyle(.borderless).tint(.jfMuted).accessibilityLabel(Text("Remove \(file.name)"))
                }
            }
            Button { importing = true } label: { Label("Choose a file", systemImage: "folder") }
                .fileImporter(isPresented: $importing, allowedContentTypes: [.item], allowsMultipleSelection: true) { result in
                    guard case .success(let urls) = result else { return }
                    for url in urls {
                        let scoped = url.startAccessingSecurityScopedResource()
                        defer { if scoped { url.stopAccessingSecurityScopedResource() } }
                        guard let data = try? Data(contentsOf: url) else { store.problem = String(localized: "The file could not be read."); continue }
                        files.append(.init(name: url.lastPathComponent, type: UTType(filenameExtension: url.pathExtension)?.preferredMIMEType, data: data))
                    }
                }
            PhotosPicker(selection: $photo, matching: .images) { Label("Choose a photo", systemImage: "photo") }
                .onChange(of: photo) { _, item in
                    guard let item else { return }
                    photo = nil
                    Task {
                        guard let data = try? await item.loadTransferable(type: Data.self), let jpeg = UIImage(data: data)?.jpegData(compressionQuality: 0.8) else { store.problem = String(localized: "The photo could not be read."); return }
                        files.append(.init(name: String(localized: "Photo") + ".jpg", type: "image/jpeg", data: jpeg))
                    }
                }
            CameraButton(title: "Take a photo") { files.append(.init(name: String(localized: "Photo") + ".jpg", type: "image/jpeg", data: $0)) }
        } header: { Overline("Tickets and files") }
    }

    /// Puts the files with what was just added; one that fails is said, the others still go.
    static func attach(_ files: [PendingFile], to id: UUID, _ bridge: TravelRepoBridge) async {
        TestReport.note("attaching \(files.count) files")
        for file in files {
            do { try await bridge.importFile(file.data, name: file.name, mediaType: file.type, attachTo: id) }
            catch { bridge.store.problem = TravelRepoBridge.message(error) }
        }
    }
}

/// Amounts as people type them, with a comma or a point, as TravelRepo takes them.
extension String {
    var amountText: String { trimmingCharacters(in: .whitespaces).replacingOccurrences(of: ",", with: ".") }
}
