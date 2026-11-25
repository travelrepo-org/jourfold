import SwiftUI
import UIKit
import CoreText

// Jourfold's tokens (src/Jourfold.UI/Theme/Tokens.axaml) as SwiftUI colours and fonts. Light uses clean neutral
// surfaces with teal accents; Dark uses deep navy rather than black.

extension Color {
    // nonisolated: SwiftUI resolves dynamic colours on its render thread too, for example while the app goes to the
    // background; a main-actor closure then stops the app.
    nonisolated init(light: UInt32, dark: UInt32) {
        self.init(uiColor: UIColor { @Sendable traits in traits.userInterfaceStyle == .dark ? UIColor(hex: dark) : UIColor(hex: light) })
    }

    static let jfAccent = Color(light: 0x0F7B7A, dark: 0x3EC6B0)
    static let jfAccentSoft = Color(light: 0xE0F2F0, dark: 0x123E45)
    static let jfAccentSoftText = Color(light: 0x0A5A59, dark: 0x86E3D3)
    static let jfOnAccent = Color(light: 0xFFFFFF, dark: 0x04211E)
    static let jfApp = Color(light: 0xF3F6F6, dark: 0x09182A)
    static let jfSurface = Color(light: 0xFFFFFF, dark: 0x0E2135)
    static let jfSubtle = Color(light: 0xEEF2F2, dark: 0x172F49)
    static let jfLine = Color(light: 0xE1E7E8, dark: 0x1F3A55)
    static let jfText = Color(light: 0x0F2A3D, dark: 0xE7F0F5)
    static let jfMuted = Color(light: 0x4D6170, dark: 0xA8BACA)
    static let jfSubtleText = Color(light: 0x5F7182, dark: 0x7D93A7)
    static let jfWarning = Color(light: 0x9A5B00, dark: 0xF2B45C)
    static let jfDanger = Color(light: 0xB42318, dark: 0xF2877C)
    static let jfSuccess = Color(light: 0x1C653C, dark: 0x7BD69C)
}

extension UIColor {
    nonisolated convenience init(hex: UInt32) {
        self.init(red: CGFloat((hex >> 16) & 0xFF) / 255, green: CGFloat((hex >> 8) & 0xFF) / 255, blue: CGFloat(hex & 0xFF) / 255, alpha: 1)
    }
}

/// Background, foreground and bar of each kind, as in the tokens.
struct KindColors {
    let bg: Color, fg: Color, bar: Color
}

extension Kind {
    var colors: KindColors {
        switch self {
        case .activity: KindColors(bg: Color(light: 0xE2F3F1, dark: 0x113A3E), fg: Color(light: 0x0A5654, dark: 0x8FE0D4), bar: Color(light: 0x14968F, dark: 0x3EC6B0))
        case .transport: KindColors(bg: Color(light: 0xE5EEFB, dark: 0x15304F), fg: Color(light: 0x1B4C8A, dark: 0xA9C8F2), bar: Color(light: 0x3B7DD8, dark: 0x6EA3EC))
        case .accommodation: KindColors(bg: Color(light: 0xEEE9FA, dark: 0x262A55), fg: Color(light: 0x4C3796, dark: 0xC8B9F2), bar: Color(light: 0x7C5CD6, dark: 0x9F87E8))
        case .food: KindColors(bg: Color(light: 0xFDEEE1, dark: 0x3A2E2A), fg: Color(light: 0x844210, dark: 0xF4C08F), bar: Color(light: 0xE2843A, dark: 0xEC9B57))
        case .sightseeing: KindColors(bg: Color(light: 0xE3F3E8, dark: 0x14372F), fg: Color(light: 0x1C653C, dark: 0x9EDDB4), bar: Color(light: 0x36A464, dark: 0x58BE80))
        case .culture: KindColors(bg: Color(light: 0xF8E7F0, dark: 0x3A2440), fg: Color(light: 0x842B58, dark: 0xF0B0CF), bar: Color(light: 0xC9558E, dark: 0xDC77A8))
        case .nature: KindColors(bg: Color(light: 0xEBF3DE, dark: 0x22352A), fg: Color(light: 0x455F18, dark: 0xC3DE95), bar: Color(light: 0x7DA83A, dark: 0x98C25A))
        case .shopping: KindColors(bg: Color(light: 0xFBE9E9, dark: 0x3B2533), fg: Color(light: 0x872D2D, dark: 0xF2AEAE), bar: Color(light: 0xD9605F, dark: 0xE57E7D))
        case .nightlife: KindColors(bg: Color(light: 0xE8E8F8, dark: 0x22284F), fg: Color(light: 0x383A8A, dark: 0xBDBEF2), bar: Color(light: 0x6366D1, dark: 0x8587E2))
        case .meeting: KindColors(bg: Color(light: 0xE9EEF2, dark: 0x1D3044), fg: Color(light: 0x324657, dark: 0xBCCBD8), bar: Color(light: 0x6B8396, dark: 0x8BA2B4))
        case .freeTime, .other: KindColors(bg: Color(light: 0xEEF1F2, dark: 0x1C3045), fg: Color(light: 0x43515C, dark: 0xC0CCD5), bar: Color(light: 0x9AA7B1, dark: 0x8796A3))
        }
    }
}

// MARK: - Plus Jakarta Sans, scaled with Dynamic Type

enum Fonts {
    static func register() {
        for url in Bundle.main.urls(forResourcesWithExtension: "ttf", subdirectory: nil) ?? [] {
            CTFontManagerRegisterFontsForURL(url as CFURL, .process, nil)
        }
        // Navigation and tab bar titles live in UIKit, so they take the font through appearance proxies.
        // A transparent appearance keeps iOS 26's glass bar and only changes the fonts.
        let large = UIFontMetrics(forTextStyle: .largeTitle).scaledFont(for: UIFont(name: "PlusJakartaSans-Bold", size: 32)!)
        let inline = UIFontMetrics(forTextStyle: .headline).scaledFont(for: UIFont(name: "PlusJakartaSans-SemiBold", size: 17)!)
        let appearance = UINavigationBarAppearance()
        appearance.configureWithTransparentBackground()
        appearance.largeTitleTextAttributes = [.font: large, .foregroundColor: UIColor { @Sendable traits in traits.userInterfaceStyle == .dark ? UIColor(hex: 0xE7F0F5) : UIColor(hex: 0x0F2A3D) }]
        appearance.titleTextAttributes = [.font: inline, .foregroundColor: UIColor { @Sendable traits in traits.userInterfaceStyle == .dark ? UIColor(hex: 0xE7F0F5) : UIColor(hex: 0x0F2A3D) }]
        let nav = UINavigationBar.appearance()
        nav.standardAppearance = appearance
        nav.scrollEdgeAppearance = appearance
        nav.compactAppearance = appearance
        let item = UITabBarItem.appearance()
        let tab = UIFont(name: "PlusJakartaSans-SemiBold", size: 10)!
        item.setTitleTextAttributes([.font: tab], for: .normal)
        item.setTitleTextAttributes([.font: tab], for: .selected)
        UISegmentedControl.appearance().setTitleTextAttributes([.font: UIFont(name: "PlusJakartaSans-SemiBold", size: 13)!], for: .normal)
    }
}

enum JFWeight { case regular, medium, semibold, bold
    var name: String {
        switch self {
        case .regular: "PlusJakartaSans-Regular"
        case .medium: "PlusJakartaSans-Medium"
        case .semibold: "PlusJakartaSans-SemiBold"
        case .bold: "PlusJakartaSans-Bold"
        }
    }
}

extension Font {
    /// Jourfold's UI font at the size of a text style, scaled with Larger Text.
    static func jf(_ style: Font.TextStyle, _ weight: JFWeight = .regular) -> Font {
        let size: CGFloat = switch style {
        case .largeTitle: 32
        case .title: 26
        case .title2: 21
        case .title3: 19
        case .headline: 16
        case .body: 16
        case .callout: 15
        case .subheadline: 14
        case .footnote: 13
        case .caption: 12
        case .caption2: 11
        @unknown default: 16
        }
        let w: JFWeight = style == .headline && weight == .regular ? .semibold : weight
        return .custom(w.name, size: size, relativeTo: style)
    }
}

// MARK: - Shared modifiers

extension View {
    /// The calm app background behind lists and scroll views.
    func jfBackground() -> some View {
        scrollContentBackground(.hidden).background(Color.jfApp.ignoresSafeArea())
    }
}

struct TripBadge: View {
    let trip: Trip
    var size: CGFloat = 44
    var body: some View {
        let initials = trip.title.split(separator: " ").filter { $0.first?.isUppercase == true }.prefix(2).map { String($0.prefix(1)) }.joined()
        RoundedRectangle(cornerRadius: size * 0.24, style: .continuous)
            .fill(LinearGradient(colors: [Color(hue: trip.colorHue, saturation: 0.55, brightness: 0.62), Color(hue: trip.colorHue, saturation: 0.35, brightness: 0.85)], startPoint: .top, endPoint: .bottom))
            .frame(width: size, height: size)
            .overlay(Text(initials).font(.custom(JFWeight.bold.name, size: size * 0.36)).foregroundStyle(.white))
            .accessibilityHidden(true)
    }
}

/// A small status pill that never relies on colour alone: it always has an icon and a word.
struct StatusPill: View {
    let status: Status
    var body: some View {
        Label { Text(status.label) } icon: { Image(systemName: status.symbol).imageScale(.small) }
            .font(.jf(.caption, .semibold))
            .labelStyle(.titleAndIcon)
            .lineLimit(1)
            .fixedSize()
            .padding(.horizontal, 8).padding(.vertical, 3)
            .foregroundStyle(foreground)
            .background(background, in: Capsule())
    }
    private var foreground: Color {
        switch status {
        case .confirmed, .done: .jfSuccess
        case .reserved: .jfWarning
        case .cancelled: .jfDanger
        default: .jfMuted
        }
    }
    private var background: Color {
        switch status {
        case .confirmed, .done: Color.jfSuccess.opacity(0.12)
        case .reserved: Color.jfWarning.opacity(0.12)
        case .cancelled: Color.jfDanger.opacity(0.12)
        default: Color.jfSubtle
        }
    }
}

struct PersonAvatar: View {
    let person: Person
    var size: CGFloat = 28
    var body: some View {
        Circle().fill(Color(hue: person.name == "Alex" ? 0.48 : person.name == "Sam" ? 0.08 : TravelRepoBridge.hue(person.id), saturation: 0.35, brightness: 0.9))
            .frame(width: size, height: size)
            .overlay(Text(person.initials).font(.custom(JFWeight.bold.name, size: size * 0.42)).foregroundStyle(Color(light: 0x0F2A3D, dark: 0x0F2A3D)))
            .accessibilityLabel(person.name)
    }
}

struct Overline: View {
    let text: LocalizedStringKey
    init(_ text: LocalizedStringKey) { self.text = text }
    var body: some View {
        Text(text).font(.jf(.footnote, .semibold)).foregroundStyle(Color.jfMuted).textCase(nil)
    }
}

/// A List whose rows sit on Jourfold's surface colour (navy in Dark) instead of the system grey.
/// Rows that set their own `listRowBackground` keep it.
struct JFList<Content: View>: View {
    @ViewBuilder var content: Content
    var body: some View {
        List { Group { content }.listRowBackground(Color.jfSurface) }
    }
}

/// A Form with Jourfold's surfaces, like `JFList`.
struct JFForm<Content: View>: View {
    @ViewBuilder var content: Content
    var body: some View {
        Form { Group { content }.listRowBackground(Color.jfSurface) }
    }
}

/// Side by side, or below each other at the accessibility text sizes.
struct FlexStack<Content: View>: View {
    var spacing: CGFloat = 10
    @ViewBuilder var content: Content
    @Environment(\.dynamicTypeSize) private var size
    var body: some View {
        if size.isAccessibilitySize { VStack(alignment: .leading, spacing: spacing) { content } } else { HStack(spacing: spacing) { content } }
    }
}

/// The explanation under a section.
struct Footnote: View {
    let text: LocalizedStringKey
    init(_ text: LocalizedStringKey) { self.text = text }
    var body: some View { Text(text).font(.jf(.footnote)).foregroundStyle(Color.jfMuted).textCase(nil) }
}

/// A section with Jourfold's heading.
struct JFSection<Content: View>: View {
    let title: LocalizedStringKey
    @ViewBuilder let content: Content
    init(_ title: LocalizedStringKey, @ViewBuilder content: () -> Content) { self.title = title; self.content = content() }
    var body: some View { Section { content } header: { Overline(title) } }
}

extension Image {
    /// A weather symbol that is visible on a light surface too; the multicolour ones have white clouds.
    static func weather(_ symbol: String) -> some View {
        Image(systemName: symbol).symbolRenderingMode(.hierarchical)
            .foregroundStyle(symbol.hasPrefix("sun") ? Color(light: 0xB45309, dark: 0xFBBF24) : Color(light: 0x2F6690, dark: 0x9CC4E8))
    }
}

extension View {
    /// The keyboard for amounts. An iPad has no number pad of its own, and switching its keyboard to numbers while a
    /// menu opens in a sheet makes SwiftUI stop (iOS 26.5); there the plain keyboard takes the digits.
    func decimalKeyboard() -> some View { keyboardType(UIDevice.current.userInterfaceIdiom == .pad ? .default : .decimalPad) }
}
