import PassKit
import PDFKit
import QuickLook
import SwiftUI
import TravelRepoKit
import UniformTypeIdentifiers

/// A document of the trip as TravelRepo stores it: an entity with a name and media type, and its bytes as a blob
/// under `assets/sha256/`.
struct TripDocument: Identifiable, Hashable {
    let id: UUID
    var name: String
    var mediaType: String
    var size: Int?
    /// The plans it is attached to.
    var plans: [UUID]

    var kind: Attachment.Kind { TravelRepoBridge.attachmentKind(mediaType) }
}

extension TravelRepoBridge {
    /// Every document of the trip, with the plans that show it.
    func documents() -> [TripDocument] {
        let plans = entities.values.filter { $0["type"]?.string == "schedule_item" || $0["type"]?.string == "booking" }
        return entities.values.filter { $0["type"]?.string == "document" }.compactMap { data in
            guard let id = data["id"]?.string, let uuid = UUID(uuidString: id) else { return nil }
            let attached = plans.filter { ($0["content"]?.array ?? []).contains { $0["document"]?.string == id || $0["entity"]?.string == id } }.compactMap { $0["id"]?.string.flatMap(UUID.init(uuidString:)) }
            let hash = data["blob"]?["hash"]?.string ?? ""
            return TripDocument(id: uuid, name: data["name"]?.string ?? "", mediaType: data["media_type"]?.string ?? "application/octet-stream",
                                size: resources.values.first { $0.path.hasSuffix(hash) }?.size, plans: attached)
        }.sorted { $0.name.localizedStandardCompare($1.name) == .orderedAscending }
    }

    /// The bytes of a document.
    func documentData(_ id: UUID) async throws -> Data {
        guard let hash = entities[Self.key(id)]?["blob"]?["hash"]?.string, let path = resources.values.first(where: { $0.path.hasSuffix(hash) })?.path else {
            throw AppError.message(String(localized: "The file of this document is missing in the trip."))
        }
        return try await session.readResource(path)
    }

    /// Imports a file into the trip, attached to a plan when one is given.
    @discardableResult
    func importFile(_ data: Data, name: String, mediaType: String? = nil, attachTo plan: UUID? = nil) async throws -> UUID? {
        await flush()
        let id = try await session.importDocument(data, name: name, mediaType: mediaType, attachTo: plan.map(Self.key))
        try await read()
        return UUID(uuidString: id)
    }

    /// Shows a document of the trip with a plan or booking, as TravelRepo writes it when a file is imported to one.
    func attach(_ document: TripDocument, to owner: UUID) async -> Bool {
        await changeCosts {
            guard let data = entities[Self.key(owner)] else { return }
            var content = data["content"]?.array ?? []
            let id = Self.key(document.id)
            guard !content.contains(where: { $0["document"]?.string == id || $0["entity"]?.string == id }) else { return }
            let entry: JSONValue = document.mediaType.hasPrefix("image/") ? ["type": "image", "document": .string(id)] : document.mediaType == "application/pdf" ? ["type": "pdf", "document": .string(id)] : ["type": "reference", "entity": .string(id)]
            content.append(entry)
            try await patch(Self.key(owner), ["content": .array(content)])
        }
    }

    /// Takes a document off a plan; the document stays in the trip's files.
    func detach(_ document: UUID, from plan: UUID) async {
        await flush()
        guard var data = entities[Self.key(plan)] else { return }
        data["content"] = (data["content"]?.array).map { .array($0.filter { $0["document"]?.string != Self.key(document) && $0["entity"]?.string != Self.key(document) }) }
        do { try await session.apply(revision: revision, edits: [Self.key(plan): data]); try await read() } catch { store.problem = Self.message(error) }
    }
}

/// Shows a document of the trip: PDFs and images in the app, e-mails as TravelRepo reads them (scripts and remote
/// images removed), Wallet passes with their fields and "Add to Apple Wallet", anything else with Quick Look.
struct DocumentView: View {
    let tripID: UUID
    let document: UUID
    var name: String
    @Environment(\.dismiss) private var dismiss
    @State private var data: Data?
    @State private var mediaType = ""
    @State private var mail: MailView?
    @State private var pass: WalletPassView?
    @State private var problem: String?
    @State private var file: URL?

    var body: some View {
        NavigationStack {
            Group {
                if let problem {
                    ContentUnavailableView(problem, systemImage: "doc.questionmark")
                } else if let mail {
                    MailContent(mail: mail)
                } else if let pass {
                    PassContent(pass: pass, data: data)
                } else if let data, mediaType == "application/pdf" {
                    PDFContent(data: data).ignoresSafeArea(edges: .bottom)
                } else if let data, mediaType.hasPrefix("image/"), let image = UIImage(data: data) {
                    ScrollView([.horizontal, .vertical]) { Image(uiImage: image).resizable().scaledToFit().frame(minWidth: 300) }
                        .accessibilityLabel(Text(name))
                } else if let file {
                    QuickLookContent(url: file)
                } else {
                    ProgressView()
                }
            }
            .frame(maxWidth: .infinity, maxHeight: .infinity)
            .background(Color.jfApp)
            .navigationTitle(name)
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .topBarLeading) { Button(role: .close) { dismiss() } }
                if let file { ToolbarItem(placement: .topBarTrailing) { ShareLink(item: file) { Image(systemName: "square.and.arrow.up") }.accessibilityLabel(Text("Share")) } }
            }
        }
        .task { await load() }
        .modifier(BrightScreen(on: pass != nil || (data != nil && mediaType.hasPrefix("image/"))))
    }

    private func load() async {
        guard let bridge = TravelRepoBridge.all[tripID] else { return }
        mediaType = bridge.entities[TravelRepoBridge.key(document)]?["media_type"]?.string ?? ""
        do {
            let bytes = try await bridge.documentData(document)
            data = bytes
            let url = FileManager.default.temporaryDirectory.appendingPathComponent("Shared", isDirectory: true).appendingPathComponent(name)
            try? FileManager.default.createDirectory(at: url.deletingLastPathComponent(), withIntermediateDirectories: true)
            try bytes.write(to: url)
            file = url
            if mediaType == "message/rfc822" { mail = try await bridge.session.readMail(TravelRepoBridge.key(document)) }
            if mediaType == "application/vnd.apple.pkpass" { pass = try await bridge.session.readPass(TravelRepoBridge.key(document), language: Locale.current.language.languageCode?.identifier) }
        } catch { problem = TravelRepoBridge.message(error) }
    }
}

private struct PDFContent: UIViewRepresentable {
    let data: Data
    func makeUIView(context: Context) -> PDFView {
        let view = PDFView(); view.autoScales = true; view.backgroundColor = .clear; view.document = PDFDocument(data: data); return view
    }
    func updateUIView(_ view: PDFView, context: Context) {}
}

private struct QuickLookContent: UIViewControllerRepresentable {
    let url: URL
    func makeCoordinator() -> Source { Source(url: url) }
    func makeUIViewController(context: Context) -> QLPreviewController { let c = QLPreviewController(); c.dataSource = context.coordinator; return c }
    func updateUIViewController(_ controller: QLPreviewController, context: Context) {}
    final class Source: NSObject, QLPreviewControllerDataSource {
        let url: URL
        init(url: URL) { self.url = url }
        func numberOfPreviewItems(in controller: QLPreviewController) -> Int { 1 }
        func previewController(_ controller: QLPreviewController, previewItemAt index: Int) -> QLPreviewItem { url as NSURL }
    }
}

/// An e-mail as TravelRepo cleaned it: who, when, the text and its attachments.
private struct MailContent: View {
    let mail: MailView
    var body: some View {
        List {
            Section {
                if let subject = mail.subject { Text(subject).font(.jf(.headline)) }
                LabeledContent("From", value: mail.from.map { $0.name ?? $0.address }.joined(separator: ", "))
                if !mail.to.isEmpty { LabeledContent("To", value: mail.to.map { $0.name ?? $0.address }.joined(separator: ", ")) }
                if let date = mail.date.flatMap({ ISO8601DateFormatter().date(from: $0) }) { LabeledContent("Sent", value: date.formatted(date: .abbreviated, time: .shortened)) }
            }
            Section { Text(mail.text).font(.jf(.callout)).textSelection(.enabled) }
            if !mail.attachments.isEmpty {
                Section {
                    ForEach(mail.attachments, id: \.name) { a in Label(a.name, systemImage: "paperclip") }
                } header: { Overline("Attachments") }
            }
        }
        .scrollContentBackground(.hidden)
    }
}

/// A Wallet pass: its fields, the barcode's text, and adding it to Apple Wallet.
private struct PassContent: View {
    let pass: WalletPassView
    let data: Data?
    @State private var adding: PKPass?
    var body: some View {
        List {
            Section {
                if let organization = pass.organization { Text(organization).font(.jf(.headline)) }
                if let description = pass.description { Text(description).foregroundStyle(Color.jfMuted) }
            }
            Section { ForEach(Array(pass.fields.enumerated()), id: \.offset) { _, f in LabeledContent(f.label ?? f.group, value: f.value) } }
            if let barcode = pass.barcode, let image = BarcodeImage.make(barcode.message, format: barcode.format, encoding: barcode.encoding) {
                Section {
                    Image(uiImage: image).interpolation(.none).resizable().scaledToFit().frame(maxWidth: 320, maxHeight: 320).frame(maxWidth: .infinity).padding(16)
                        .accessibilityLabel(Text("Barcode of the ticket"))
                }
                // White in dark mode too, or scanners do not read it.
                .listRowBackground(Color.white)
            }
            if let barcode = pass.barcode { Section { Text(barcode.altText ?? barcode.message).font(.system(.body, design: .monospaced)) } header: { Overline("Code") } }
            if let data, let pkPass = try? PKPass(data: data), PKAddPassesViewController.canAddPasses() {
                Section { Button { adding = pkPass } label: { Label("Add to Apple Wallet", systemImage: "wallet.pass") } }
            }
        }
        .scrollContentBackground(.hidden)
        .sheet(item: Binding(get: { adding.map(IdentifiedPass.init) }, set: { adding = $0?.pass })) { item in AddPassView(pass: item.pass) }
    }
    struct IdentifiedPass: Identifiable { let pass: PKPass; var id: String { pass.serialNumber } }
}

private struct AddPassView: UIViewControllerRepresentable {
    let pass: PKPass
    func makeUIViewController(context: Context) -> UIViewController { PKAddPassesViewController(pass: pass) ?? UIViewController() }
    func updateUIViewController(_ controller: UIViewController, context: Context) {}
}

/// Picks a file in Files and imports it into the trip, attached to a plan when given.
struct ImportFileModifier: ViewModifier {
    @Binding var isPresented: Bool
    let tripID: UUID
    var plan: UUID?
    @Environment(TripStore.self) private var store
    func body(content: Content) -> some View {
        content.fileImporter(isPresented: $isPresented, allowedContentTypes: [.item], allowsMultipleSelection: true) { result in
            guard case .success(let urls) = result else { return }
            Task {
                for url in urls {
                    let scoped = url.startAccessingSecurityScopedResource()
                    defer { if scoped { url.stopAccessingSecurityScopedResource() } }
                    do {
                        let data = try Data(contentsOf: url)
                        let type = UTType(filenameExtension: url.pathExtension)?.preferredMIMEType
                        try await TravelRepoBridge.all[tripID]?.importFile(data, name: url.lastPathComponent, mediaType: type, attachTo: plan)
                    } catch { store.problem = TravelRepoBridge.message(error) }
                }
            }
        }
    }
}

extension View {
    func importFile(isPresented: Binding<Bool>, tripID: UUID, plan: UUID? = nil) -> some View {
        modifier(ImportFileModifier(isPresented: isPresented, tripID: tripID, plan: plan))
    }
}

/// The trip's files: every document, which plans show it, opening, sharing, adding and removing.
struct FilesView: View {
    let tripID: UUID
    @Environment(TripStore.self) private var store
    @Environment(AppNavigation.self) private var nav
    @State private var showing: TripDocument?
    @State private var importing = false
    @State private var attaching: TripDocument?
    @State private var removing: TripDocument?

    var body: some View {
        let trip = store.trip(tripID)
        let documents = { _ = store.travelRepoReads; return TravelRepoBridge.all[tripID]?.documents() ?? [] }()
        JFList {
            if documents.isEmpty {
                Section { Text("No files yet. Tickets, booking e-mails and PDFs you add show here and with their plans.").foregroundStyle(Color.jfMuted) }
            }
            ForEach(documents) { d in
                Button { showing = d } label: {
                    HStack(spacing: 12) {
                        Image(systemName: FileIcon.symbol(d.kind)).foregroundStyle(Color.jfAccent).frame(width: 28).accessibilityHidden(true)
                        VStack(alignment: .leading, spacing: 2) {
                            Text(d.name).font(.jf(.body)).foregroundStyle(Color.jfText).lineLimit(2)
                            let plans = d.plans.compactMap { trip.item($0)?.title ?? trip.booking($0)?.title }
                            Text(plans.isEmpty ? String(localized: "Not with a plan") : plans.joined(separator: ", ")).font(.jf(.footnote)).foregroundStyle(Color.jfMuted)
                        }
                        Spacer()
                        if let size = d.size { Text(ByteCountFormatter.string(fromByteCount: Int64(size), countStyle: .file)).font(.jf(.caption)).foregroundStyle(Color.jfSubtleText) }
                    }
                    .contentShape(Rectangle())
                }
                .buttonStyle(.plain)
                .contextMenu {
                    Button { showing = d } label: { Label("Open", systemImage: "doc.text.magnifyingglass") }
                    Button { attaching = d } label: { Label("Add to a plan or booking", systemImage: "paperclip") }
                    if let plan = d.plans.first { Button { nav.detail = plan } label: { Label("Open plan", systemImage: "calendar") } }
                    Divider()
                    Button(role: .destructive) { removing = d } label: { Label("Delete", systemImage: "trash") }
                }
                .swipeActions { Button(role: .destructive) { removing = d } label: { Label("Delete", systemImage: "trash") } }
            }
        }
        .listStyle(.insetGrouped)
        .jfBackground()
        .navigationTitle("Files")
        .toolbar { ToolbarItem(placement: .topBarTrailing) { Button { importing = true } label: { Image(systemName: "plus") }.accessibilityLabel(Text("Add a file")) } }
        .sheet(item: $attaching) { AttachSheet(tripID: tripID, document: $0) }
        .sheet(isPresented: $importing) { NavigationStack { JFForm { DocumentAddSection(tripID: tripID) { importing = false } }.jfBackground().navigationTitle("Add a file or scan").navigationBarTitleDisplayMode(.inline).toolbar { ToolbarItem(placement: .cancellationAction) { Button(role: .cancel) { importing = false } } } } }
        .sheet(item: $showing) { d in DocumentView(tripID: tripID, document: d.id, name: d.name) }
        .task {
            // For screenshots: `-document <name>` opens that file.
            if let name = DebugTools.value("document") { showing = documents.first { $0.name.hasPrefix(name) } }
        }
        .deleteConfirmation(tripID: tripID, ids: removing.map { [$0.id] }, title: removing?.name) { removing = nil }
    }
}

enum FileIcon {
    static func symbol(_ k: Attachment.Kind) -> String {
        switch k {
        case .walletPass: "wallet.pass"
        case .pdf: "doc.richtext"
        case .image: "photo"
        case .note: "note.text"
        case .link: "link"
        case .mail: "envelope"
        case .file: "doc"
        }
    }
}

/// Asks before deleting, with what TravelRepo's delete preview says goes along (comments, references, files no
/// longer used), and deletes through TravelRepo.
struct DeleteConfirmation: ViewModifier {
    let tripID: UUID
    let ids: [UUID]?
    let title: String?
    let done: () -> Void
    @Environment(TripStore.self) private var store
    @State private var preview: DeletionPreview?

    func body(content: Content) -> some View {
        content
            .task(id: ids) {
                preview = nil
                guard let ids, let bridge = TravelRepoBridge.all[tripID] else { return }
                preview = try? await bridge.session.deletePreview(ids.map(TravelRepoBridge.key))
            }
            .confirmationDialog(Text("Delete “\(title ?? "")”?"), isPresented: Binding(get: { ids != nil }, set: { if !$0 { done() } }), titleVisibility: .visible) {
                Button("Delete", role: .destructive) {
                    guard let ids, let bridge = TravelRepoBridge.all[tripID] else { return }
                    Task {
                        await bridge.flush()
                        do { _ = try await bridge.session.deleteEntities(ids.map(TravelRepoBridge.key)); try await bridge.read() } catch { store.problem = TravelRepoBridge.message(error) }
                        done()
                    }
                }
            } message: {
                Text(summary)
            }
    }

    private var summary: String {
        guard let preview else { return String(localized: "It goes from the trip. An earlier version still has it.") }
        var lines: [String] = []
        if !preview.dependents.isEmpty { lines.append(String(localized: "Also goes: \(preview.dependents.map(\.title).joined(separator: ", "))")) }
        if !preview.updated.isEmpty { lines.append(String(localized: "No longer linked from: \(preview.updated.map(\.title).joined(separator: ", "))")) }
        lines.append(String(localized: "An earlier version still has it."))
        return lines.joined(separator: "\n")
    }
}

extension View {
    func deleteConfirmation(tripID: UUID, ids: [UUID]?, title: String?, done: @escaping () -> Void) -> some View {
        modifier(DeleteConfirmation(tripID: tripID, ids: ids, title: title, done: done))
    }
}
