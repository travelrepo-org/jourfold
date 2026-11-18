// Draws the travel day on the Lock Screen and in the Dynamic Island. The texts come from Jourfold, already in the
// user's language; the countdown and the progress move on by themselves, as iOS does not wake Jourfold every minute.
import ActivityKit
import SwiftUI
import WidgetKit

@main
struct JourfoldWidgets: WidgetBundle {
    var body: some Widget { TravelDayActivity() }
}

struct TravelDayActivity: Widget {
    var body: some WidgetConfiguration {
        ActivityConfiguration(for: TravelDayAttributes.self) { context in
            LockScreen(state: context.state)
                .activityBackgroundTint(nil)
                .widgetURL(URL(string: context.state.url))
        } dynamicIsland: { context in
            let state = context.state
            return DynamicIsland {
                DynamicIslandExpandedRegion(.leading) {
                    Vehicle(icon: state.icon, size: 22).padding(.leading, 4)
                }
                DynamicIslandExpandedRegion(.trailing) {
                    Countdown(state: state).font(.title3.weight(.semibold)).padding(.trailing, 4)
                }
                // The title goes below, where it has the island's whole width: between the vehicle and the countdown
                // the time of the arrival would be cut off.
                DynamicIslandExpandedRegion(.bottom) {
                    VStack(alignment: .leading, spacing: 8) {
                        Text(state.title).font(.subheadline.weight(.semibold)).lineLimit(2)
                        if !state.text.isEmpty { Text(state.text).font(.footnote).foregroundStyle(.secondary).lineLimit(2) }
                        Journey(state: state)
                        TicketLink(state: state)
                    }
                    .frame(maxWidth: .infinity, alignment: .leading)
                    // Clear of the island's round corners.
                    .padding(.horizontal, 6)
                    .padding(.bottom, 6)
                }
            } compactLeading: {
                Vehicle(icon: state.icon, size: 15)
            } compactTrailing: {
                Chip(state: state)
            } minimal: {
                Vehicle(icon: state.icon, size: 15)
            }
            .widgetURL(URL(string: state.url))
            .keylineTint(Palette.island)
        }
    }
}

/// Jourfold's accent: the light theme's Accent token on light backgrounds, the dark theme's on dark ones.
enum Palette {
    static let accent = Color(UIColor { $0.userInterfaceStyle == .dark ? UIColor(red: 0x3E / 255, green: 0xC6 / 255, blue: 0xB0 / 255, alpha: 1)
                                                                         : UIColor(red: 0x0F / 255, green: 0x7B / 255, blue: 0x7A / 255, alpha: 1) })
    /// The Dynamic Island is always black, so it takes the dark theme's accent.
    static let island = Color(red: 0x3E / 255, green: 0xC6 / 255, blue: 0xB0 / 255)
}

struct LockScreen: View {
    let state: TravelDayAttributes.ContentState

    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            HStack(alignment: .top, spacing: 12) {
                Vehicle(icon: state.icon, size: 20)
                    .frame(width: 40, height: 40)
                    .background(Circle().fill(Palette.accent.opacity(0.15)))
                VStack(alignment: .leading, spacing: 2) {
                    Text(state.title).font(.subheadline.weight(.semibold)).lineLimit(2)
                    if !state.text.isEmpty { Text(state.text).font(.footnote).foregroundStyle(.secondary).lineLimit(2) }
                }
                Spacer(minLength: 8)
                Countdown(state: state).font(.title3.weight(.semibold))
            }
            Journey(state: state)
            TicketLink(state: state)
        }
        .padding(16)
    }
}

/// The vehicle, from the app's icon name. Decorative: the title names the journey.
struct Vehicle: View {
    let icon: String
    let size: CGFloat

    var body: some View {
        Image(systemName: Vehicle.symbol(icon))
            .font(.system(size: size, weight: .semibold))
            .foregroundStyle(Palette.accent)
            .accessibilityHidden(true)
    }

    static func symbol(_ icon: String) -> String {
        switch icon {
        case "plane": return "airplane"
        case "train-front": return "train.side.front.car"
        case "bus": return "bus.fill"
        case "car", "car-taxi-front": return "car.fill"
        case "ship": return "ferry.fill"
        case "bike": return "bicycle"
        case "footprints": return "figure.walk"
        default: return "point.topleft.down.to.point.bottomright.curvepath"
        }
    }
}

/// The time left until the departure or arrival, counted by iOS itself.
struct Countdown: View {
    let state: TravelDayAttributes.ContentState

    var body: some View {
        if let until = state.until {
            // A timer's text takes all the width it is offered, so it gets the width of the longest it can be.
            Text(timerInterval: Date.now...max(until, Date.now), countsDown: true)
                .monospacedDigit()
                .lineLimit(1)
                .minimumScaleFactor(0.7)
                .multilineTextAlignment(.trailing)
                .frame(width: until.timeIntervalSinceNow >= 36_000 ? 96 : until.timeIntervalSinceNow >= 3_600 ? 84 : 64, alignment: .trailing)
                .accessibilityLabel(state.timeLeftLabel)
        }
    }
}

/// The Dynamic Island's short text: the time of the departure or arrival, and in the last hour the time left, as on
/// Android's status bar chip.
struct Chip: View {
    let state: TravelDayAttributes.ContentState

    var body: some View {
        Group {
            if let from = state.countdownFrom, let until = state.until, Date.now >= from {
                Text(timerInterval: Date.now...max(until, Date.now), countsDown: true)
                    .accessibilityLabel(state.timeLeftLabel)
            } else if let chip = state.chip {
                Text(chip)
            }
        }
        .font(.footnote.weight(.semibold))
        .monospacedDigit()
        .lineLimit(1)
        .minimumScaleFactor(0.7)
        .multilineTextAlignment(.trailing)
        .frame(width: 46, alignment: .trailing)
        .foregroundStyle(Palette.island)
    }
}

/// The way from the departure to the arrival while under way: the vehicle in a bubble at the start, a pin at the
/// destination, the bar between them moved on by iOS, and the two times below on the phone's clock. Nothing before it
/// leaves. A vehicle riding along the bar, as on Android, would only move when Jourfold runs, so the ends stay put.
struct Journey: View {
    let state: TravelDayAttributes.ContentState

    var body: some View {
        if let from = state.from, let until = state.until, until > from {
            VStack(spacing: 4) {
                HStack(spacing: 6) {
                    Bubble(symbol: Vehicle.symbol(state.icon), filled: true)
                    ProgressView(timerInterval: from...until, countsDown: false) { EmptyView() } currentValueLabel: { EmptyView() }
                        .tint(Palette.accent)
                        .accessibilityLabel(state.progressLabel)
                    Bubble(symbol: "mappin", filled: false)
                }
                HStack {
                    Text(from, style: .time)
                    Spacer(minLength: 8)
                    Text(until, style: .time)
                }
                .font(.caption2.weight(.medium))
                .monospacedDigit()
                .foregroundStyle(.secondary)
                .accessibilityHidden(true)
            }
        }
    }
}

/// A round end of the way: the vehicle filled in the accent, the destination outlined. Decorative.
struct Bubble: View {
    let symbol: String
    let filled: Bool

    var body: some View {
        Image(systemName: symbol)
            .font(.system(size: 11, weight: .bold))
            .foregroundStyle(filled ? Color(UIColor.systemBackground) : Palette.accent)
            .frame(width: 24, height: 24)
            .background(Circle().fill(filled ? Palette.accent : Palette.accent.opacity(0.18)))
            .overlay(Circle().strokeBorder(Palette.accent, lineWidth: filled ? 0 : 1.5))
            .accessibilityHidden(true)
    }
}

/// "Show ticket", when the journey has one.
struct TicketLink: View {
    let state: TravelDayAttributes.ContentState

    var body: some View {
        if let link = state.ticketUrl.flatMap(URL.init(string:)), let label = state.ticketLabel {
            Link(destination: link) {
                Label(label, systemImage: "ticket")
                    .font(.footnote.weight(.semibold))
                    .foregroundStyle(Palette.accent)
            }
        }
    }
}
