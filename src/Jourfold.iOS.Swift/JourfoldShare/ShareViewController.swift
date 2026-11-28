import UIKit
import SwiftUI
import UniformTypeIdentifiers

/// The share extension: reads the text, link or files another app shares and shows `ShareForm` for them. Files are
/// left in the shared store, from where the app imports them into the trip through TravelRepo.
final class ShareViewController: UIViewController {
    override func viewDidLoad() {
        super.viewDidLoad()
        Task { await load() }
    }

    private func load() async {
        SharedStore.set("seen/share", Data(Date().formatted(.iso8601).utf8))
        var text = ""
        var url: String?
        let id = UUID()
        var files: [String] = []
        var problem: String?
        let items = (extensionContext?.inputItems as? [NSExtensionItem]) ?? []
        for item in items {
            if text.isEmpty, let title = item.attributedContentText?.string { text = title }
            for provider in item.attachments ?? [] {
                // A file (PDF, image, e-mail, Wallet pass, …): copy it into the app group under its name.
                let fileType = [UTType.pdf, .image, .emailMessage, UTType("com.apple.pkpass") ?? .data, .fileURL].first { provider.hasItemConformingToTypeIdentifier($0.identifier) }
                if let fileType, let file = await Self.copy(provider, type: fileType) {
                    if SharedInbox.putFile(file.data, name: file.name, entry: id) { files.append(file.name) }
                    else { problem = String(localized: "“\(file.name)” is too large to share this way. Add it in Jourfold under Files.") }
                    continue
                }
                if url == nil, provider.hasItemConformingToTypeIdentifier(UTType.url.identifier),
                   let u = try? await provider.loadItem(forTypeIdentifier: UTType.url.identifier) as? URL {
                    url = u.absoluteString
                } else if text.isEmpty, provider.hasItemConformingToTypeIdentifier(UTType.plainText.identifier),
                          let t = try? await provider.loadItem(forTypeIdentifier: UTType.plainText.identifier) as? String {
                    text = t
                }
            }
        }
        if text.isEmpty { text = url ?? files.first ?? "" }
        let form = ShareForm(text: text, url: url, files: files, problem: problem, trips: SharedInbox.trips()) { [weak self] entry in
            if var entry { entry.id = id; SharedInbox.put(entry) } else { SharedInbox.clean(id) }
            self?.extensionContext?.completeRequest(returningItems: nil)
        }
        let host = UIHostingController(rootView: form)
        addChild(host)
        host.view.frame = view.bounds
        host.view.autoresizingMask = [.flexibleWidth, .flexibleHeight]
        view.addSubview(host.view)
        host.didMove(toParent: self)
    }

    private static func copy(_ provider: NSItemProvider, type: UTType) async -> (name: String, data: Data)? {
        await withCheckedContinuation { done in
            _ = provider.loadFileRepresentation(forTypeIdentifier: type.identifier) { url, _ in
                guard let url, let data = try? Data(contentsOf: url) else { done.resume(returning: nil); return }
                var name = provider.suggestedName.map { $0 + (url.pathExtension.isEmpty ? "" : ".\(url.pathExtension)") } ?? url.lastPathComponent
                if name.hasSuffix(".\(url.pathExtension).\(url.pathExtension)") { name = String(name.dropLast(url.pathExtension.count + 1)) }
                done.resume(returning: (name, data))
            }
        }
    }
}
