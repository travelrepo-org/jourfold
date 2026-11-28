import CoreLocation
import SwiftUI

/// Whether Apple Maps may be asked for map pictures; nothing is loaded before the person says yes once.
enum MapConsent {
    static let key = "maps.online"
}

/// The stops of the trip or of a day without any map service: numbered pins where the places are, and the journeys
/// between them as arcs. Works offline and sends nothing.
struct OfflineMap: View {
    let stops: [(number: Int, place: Place, kind: Kind)]
    let journeys: [(from: CLLocationCoordinate2D, to: CLLocationCoordinate2D)]
    @Binding var selection: UUID?

    var body: some View {
        GeometryReader { geo in
            let points = Self.project(stops.map(\.place.coordinate), journeys.flatMap { [$0.from, $0.to] }, into: geo.size)
            ZStack {
                Color.jfSubtle
                Path { p in
                    for (i, _) in journeys.enumerated() {
                        let a = points.lines[i * 2], b = points.lines[i * 2 + 1]
                        p.move(to: a)
                        // Bent to one side, so the way there and the way back do not lie on each other.
                        p.addQuadCurve(to: b, control: CGPoint(x: (a.x + b.x) / 2 + (b.y - a.y) * 0.2, y: (a.y + b.y) / 2 - (b.x - a.x) * 0.2))
                    }
                }
                .stroke(Color.jfAccent.opacity(0.7), style: StrokeStyle(lineWidth: 2, lineCap: .round, dash: [5, 5]))
                .accessibilityHidden(true)
                // Stops at the same place share a pin.
                ForEach(Array(stops.enumerated()).filter { i, s in stops.firstIndex { Self.same($0.place, s.place) } == i }, id: \.element.place.id) { i, s in
                    let here = stops.filter { Self.same($0.place, s.place) }
                    let numbers = here.map { "\($0.number)" }.joined(separator: "·")
                    Button { selection = s.place.id } label: {
                        VStack(spacing: 2) {
                            Text(verbatim: numbers).padding(.horizontal, here.count > 1 ? 8 : 0).font(.jf(.footnote, .bold)).foregroundStyle(.white)
                                .frame(minWidth: 28, minHeight: 28).background(s.kind.colors.bar, in: Capsule()).overlay(Capsule().strokeBorder(.white, lineWidth: 2))
                            if stops.count <= 12 { Text(s.place.name).font(.jf(.caption, .semibold)).foregroundStyle(Color.jfText).lineLimit(1).frame(maxWidth: 120) }
                        }
                        .frame(minWidth: 44, minHeight: 44)
                    }
                    .buttonStyle(.plain)
                    .accessibilityLabel(Text(verbatim: here.map { "\($0.number), \($0.place.name)" }.joined(separator: "; ")))
                    .position(points.stops[i])
                }
            }
        }
    }

    private static func same(_ a: Place, _ b: Place) -> Bool { abs(a.latitude - b.latitude) < 0.00001 && abs(a.longitude - b.longitude) < 0.00001 }

    /// Mercator, scaled to fit. Longitudes are unwrapped around their widest gap, so a trip across the date line stays together.
    static func project(_ stops: [CLLocationCoordinate2D], _ lines: [CLLocationCoordinate2D], into size: CGSize) -> (stops: [CGPoint], lines: [CGPoint]) {
        let all = stops + lines
        guard !all.isEmpty else { return ([], []) }
        let sorted = all.map(\.longitude).sorted()
        var start = sorted[0], widest = sorted[0] + 360 - sorted[sorted.count - 1]
        for (a, b) in zip(sorted, sorted.dropFirst()) where b - a > widest { widest = b - a; start = b }
        func flat(_ c: CLLocationCoordinate2D) -> CGPoint {
            let lon = c.longitude < start ? c.longitude + 360 : c.longitude
            let lat = min(max(c.latitude, -85), 85) * .pi / 180
            return CGPoint(x: lon, y: -log(tan(.pi / 4 + lat / 2)) * 180 / .pi)
        }
        let flats = all.map(flat)
        let minX = flats.map(\.x).min()!, maxX = flats.map(\.x).max()!, minY = flats.map(\.y).min()!, maxY = flats.map(\.y).max()!
        let margin = 60.0
        let scale = min(max(size.width - margin * 2, 1) / max(maxX - minX, 0.02), max(size.height - margin * 2, 1) / max(maxY - minY, 0.02))
        let points = flats.map { CGPoint(x: size.width / 2 + ($0.x - (minX + maxX) / 2) * scale, y: size.height / 2 + ($0.y - (minY + maxY) / 2) * scale) }
        return (Array(points.prefix(stops.count)), Array(points.suffix(lines.count)))
    }
}

/// Asks once whether to show Apple's maps, saying what is sent.
struct MapOffer: View {
    @AppStorage(MapConsent.key) private var online = ""
    var body: some View {
        if online.isEmpty {
            VStack(alignment: .leading, spacing: 8) {
                Label("Show Apple Maps?", systemImage: "map").font(.jf(.body, .semibold))
                Text("Apple then learns which areas you look at. Without it, the stops are shown as below, offline.").font(.jf(.footnote)).foregroundStyle(Color.jfMuted)
                HStack {
                    Button("Show maps") { online = "on" }.buttonStyle(.bordered)
                    Button("Not now") { online = "off" }
                }
            }
            .padding(14).frame(maxWidth: .infinity, alignment: .leading)
            .background(Color.jfSurface, in: RoundedRectangle(cornerRadius: 14, style: .continuous))
            .overlay(RoundedRectangle(cornerRadius: 14, style: .continuous).strokeBorder(Color.jfLine))
            .padding(.horizontal, 16)
        }
    }
}
