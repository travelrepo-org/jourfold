import SwiftUI
import MapKit

/// A still map of one place with a pin, for cards and sheets. A live `Map` inside a scrolling page resets the
/// navigation bar's title font on iOS 26 and costs more, so previews use a snapshot.
struct MapSnapshot: View {
    let coordinate: CLLocationCoordinate2D
    var meters: Double = 900
    var symbol: String = "mappin"
    var tint: Color = .jfAccent
    @Environment(\.colorScheme) private var scheme
    @State private var image: UIImage?
    @AppStorage(MapConsent.key) private var online = ""

    var body: some View {
        GeometryReader { geo in
            ZStack {
                Color.jfSubtle
                if let image { Image(uiImage: image).resizable().scaledToFill().transition(.opacity) }
                Image(systemName: symbol)
                    .font(.system(size: 14, weight: .bold))
                    .foregroundStyle(.white)
                    .frame(width: 30, height: 30)
                    .background(tint, in: Circle())
                    .overlay(Circle().strokeBorder(.white, lineWidth: 2))
                    .shadow(color: .black.opacity(0.2), radius: 3, y: 1)
            }
            .task(id: "\(geo.size.width)x\(geo.size.height)-\(scheme)-\(online)-\(coordinate.latitude),\(coordinate.longitude)") { await render(size: geo.size) }
        }
        .clipped()
        .accessibilityHidden(true)
    }

    private func render(size: CGSize) async {
        guard online == "on" else { image = nil; return }
        guard size.width > 0, size.height > 0 else { return }
        let options = MKMapSnapshotter.Options()
        options.region = MKCoordinateRegion(center: coordinate, latitudinalMeters: meters, longitudinalMeters: meters)
        options.size = size
        options.traitCollection = UITraitCollection(userInterfaceStyle: scheme == .dark ? .dark : .light)
        options.pointOfInterestFilter = .excludingAll
        if let snap = try? await MKMapSnapshotter(options: options).start(), !Task.isCancelled {
            withAnimation(.easeOut(duration: 0.2)) { image = snap.image }
        }
    }
}
