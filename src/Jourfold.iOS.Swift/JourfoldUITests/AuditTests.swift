import XCTest

/// Apple's accessibility audit on every page and form, and a look for controls of an odd size. `JF_LARGE` in the
/// launch arguments' content size makes the text very large.
final class AuditTests: JourfoldTest {
    static let pages: [(String, [String])] = [
        ("today", []), ("plan", ["-tab", "plan", "-day", "3"]), ("ideas", ["-ideas", "YES"]), ("timetable", ["-timetable", "YES", "-day", "3"]),
        ("costs", ["-page", "costs"]), ("bookings", ["-page", "bookings"]), ("tasks", ["-page", "tasks"]), ("packing", ["-page", "packing"]), ("places", ["-page", "places"]),
        ("files", ["-page", "files"]), ("converter", ["-page", "converter"]), ("trip", ["-page", "trip"]), ("travellers", ["-page", "travellers"]), ("sync", ["-page", "sync"]),
        ("settings", ["-page", "settings"]), ("map", ["-page", "map"]), ("about", ["-page", "about"]), ("notifications", ["-page", "notifications"]), ("more", ["-tab", "more"]),
        ("detail", ["-sheet", "detail"]), ("expense", ["-page", "costs", "-expense", "teamLab"]), ("payback", ["-page", "costs", "-payback", "YES"]), ("quickadd", ["-sheet", "quickadd"]),
    ] + ["item", "transport", "stay", "expense", "booking", "task", "packing", "note", "document", "budget"].map { ("add-\($0)", ["-sheet", $0]) }

    func audit(_ extra: [String], language: String, tag: String) throws {
        continueAfterFailure = true
        for (name, args) in Self.pages {
            open(args + extra, language: language)
            _ = app.buttons.firstMatch.waitForExistence(timeout: 15); sleep(5)
            let shot = XCTAttachment(screenshot: app.screenshot()); shot.lifetime = .keepAlways; shot.name = "\(tag)-\(name)"; add(shot)
            for b in app.buttons.allElementsBoundByIndex.prefix(60) where b.exists && b.frame.height > 130 && b.frame.width < 200 && b.isHittable { print("DUMP \(tag) \(name) TALL \(b.label.prefix(40)) \(b.frame)") }
            do {
                try app.performAccessibilityAudit(for: [.contrast, .elementDetection, .hitRegion, .sufficientElementDescription, .dynamicType, .textClipped, .trait]) { @Sendable found in
                    nonisolated(unsafe) let issue = found
                    return MainActor.assumeIsolated {
                    let label = issue.element.map { "\($0.elementType.rawValue) '\($0.label.prefix(50))' \(Int($0.frame.minX)),\(Int($0.frame.minY)) \(Int($0.frame.width))x\(Int($0.frame.height))" } ?? "-"
                    print("DUMP \(tag) \(name) | \(issue.compactDescription) | \(label)")
                    return true
                } }
            } catch { print("DUMP \(tag) \(name) audit error \(error)") }
        }
    }

    func testLightEnglish() throws { try audit([], language: "en", tag: "en") }
    func testPadGerman() throws { try audit([], language: "de", tag: "pad") }
    func testLargeGerman() throws { try audit(["-UIPreferredContentSizeCategoryName", "UICTContentSizeCategoryAccessibilityL"], language: "de", tag: "xl") }
}
