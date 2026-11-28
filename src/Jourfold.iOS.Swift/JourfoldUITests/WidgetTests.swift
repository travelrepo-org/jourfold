import XCTest

/// The widget in the gallery and on the Home Screen, with its setting.
final class WidgetTests: JourfoldTest {
    private var springboard: XCUIApplication { XCUIApplication(bundleIdentifier: "com.apple.springboard") }
    private func shot(_ name: String) { let s = XCTAttachment(screenshot: XCUIScreen.main.screenshot()); s.lifetime = .keepAlways; s.name = name; add(s) }

    /// Puts the widget of this name in this size (0 small, 1 medium, 2 large) on the Home Screen.
    private func put(_ name: String, size: Int) {
        XCUIDevice.shared.press(.home); sleep(2)
        springboard.coordinate(withNormalizedOffset: .init(dx: 0.5, dy: 0.8)).press(forDuration: 1.8); sleep(1)
        let edit = springboard.buttons.matching(NSPredicate(format: "label IN {'Edit', 'Bearbeiten'}")).firstMatch
        if edit.waitForExistence(timeout: 4) { edit.tap() }
        let addWidget = springboard.buttons.matching(NSPredicate(format: "label CONTAINS[c] 'Widget hinzu' OR label CONTAINS[c] 'Add Widget'")).firstMatch
        XCTAssertTrue(addWidget.waitForExistence(timeout: 5), "no add widget"); addWidget.tap(); sleep(3)
        let search = springboard.searchFields.firstMatch
        XCTAssertTrue(search.waitForExistence(timeout: 6)); search.tap(); search.typeText("Jourfold"); sleep(3)
        springboard.descendants(matching: .any).matching(NSPredicate(format: "label CONTAINS 'Jourfold' AND NOT (label CONTAINS[c] 'search')")).allElementsBoundByIndex.last { $0.isHittable }?.tap(); sleep(3)
        for _ in 0..<(name == "Costs" ? 3 + size : size) { springboard.swipeLeft(); sleep(1) }
        shot("gallery-\(name)-\(size)")
        springboard.buttons.matching(NSPredicate(format: "label CONTAINS 'Widget hinzu' OR label CONTAINS 'Add Widget'")).firstMatch.tap(); sleep(2)
        XCUIDevice.shared.press(.home); sleep(3)
    }

    private func set(_ row: String, _ value: String) {
        let widget = springboard.icons.matching(NSPredicate(format: "label CONTAINS 'Jourfold Preview'")).allElementsBoundByIndex.first { $0.frame.height > 300 }
        XCTAssertNotNil(widget, "no large widget"); widget?.press(forDuration: 1.4); sleep(1)
        springboard.buttons.matching(NSPredicate(format: "label CONTAINS[c] 'Widget bearbeiten' OR label CONTAINS[c] 'Edit Widget'")).firstMatch.tap(); sleep(3)
        springboard.buttons.matching(NSPredicate(format: "label CONTAINS %@", row)).firstMatch.tap(); sleep(2)
        let choice = springboard.descendants(matching: .any).matching(NSPredicate(format: "label == %@", value)).firstMatch
        XCTAssertTrue(choice.waitForExistence(timeout: 5), "no \(value)"); choice.tap(); sleep(2); shot("set-\(value)")
        // Outside the card, as a person closes it.
        springboard.coordinate(withNormalizedOffset: .init(dx: 0.5, dy: 0.9)).tap(); sleep(8); shot("after-\(value)")
    }

    func testPlansWidget() {
        app = XCUIApplication(); app.launchArguments = ["-AppleLanguages", "(de)", "-demo", "NO", "-reset", "YES", "-samples", "YES"]; app.launch(); sleep(12)
        put("Plans", size: 2); shot("plans")
        set("nächsten", "Nur heute")
        set("Alle Reisen", "Spring in Japan")
    }
    func testPlansSmallAndMedium() {
        app = XCUIApplication(); app.launchArguments = ["-AppleLanguages", "(de)", "-demo", "NO"]; app.launch(); sleep(8)
        put("Plans", size: 1); shot("plans1"); put("Plans", size: 0); shot("plans0"); put("Costs", size: 2); shot("costs2")
    }
    func testCostsWidget() {
        app = XCUIApplication(); app.launchArguments = ["-AppleLanguages", "(de)", "-demo", "NO"]; app.launch(); sleep(8)
        put("Costs", size: 1); shot("costs")
    }
}

/// What a widget opens, from a cold start.
final class LinkTests: JourfoldTest {
    func testTodayOpensTheTripThatIsOn() { open(["-trip", "9", "-openurl", "jourfold://today"]); shows("Day 2 of 8", 20) }
    func testCosts() { open(["-trip", "9", "-openurl", "jourfold://costs"]); shows("Paid per currency", 20) }
    func testGoneTripFallsBack() { open(["-trip", "9", "-openurl", "jourfold://plan/01a1fe00-0000-7000-8000-000000000001"]); shows("Day 2 of 8", 20) }
}

/// Files, tickets and quick actions from outside the app.
final class OutsideTests: JourfoldTest {
    func testFileOpenedInJourfold() {
        open(["-openfile", "YES"])
        XCTAssertTrue(app.staticTexts["Add to Jourfold"].waitForExistence(timeout: 15)); shows("Kyoto Railway Museum ticket.pdf")
        app.buttons.matching(NSPredicate(format: "label == 'Done'")).firstMatch.tap(); sleep(3)
        page("Files"); shows("Kyoto Railway Museum ticket.pdf")
    }
    func testTicketShortcut() {
        open(["-ticketsahead", "YES"]); sleep(5)
        XCUIDevice.shared.press(.home); sleep(2)
        let board = XCUIApplication(bundleIdentifier: "com.apple.springboard")
        // Widgets have the same name as the app's icon, but are larger.
        func find() -> XCUIElement? { board.icons.matching(identifier: "Jourfold Preview").allElementsBoundByIndex.first { $0.frame.height < 130 && $0.isHittable } }
        var found = find()
        for _ in 0..<6 where found == nil { board.swipeLeft(); sleep(1); found = find() }
        guard let icon = found else { return XCTFail("no icon") }
        icon.press(forDuration: 1.3)
        let ticket = board.buttons.matching(NSPredicate(format: "label BEGINSWITH 'Ticket: '")).firstMatch
        if !ticket.waitForExistence(timeout: 6) { print("DUMP icon \(icon.exists) \(icon.frame) \(board.buttons.allElementsBoundByIndex.map { $0.label })"); let s = XCTAttachment(screenshot: XCUIScreen.main.screenshot()); s.lifetime = .keepAlways; add(s) }
        XCTAssertTrue(ticket.exists, "no ticket"); ticket.tap()
        XCTAssertTrue(app.navigationBars.matching(NSPredicate(format: "identifier CONTAINS 'Boarding pass' OR identifier CONTAINS 'Shinkansen'")).firstMatch.waitForExistence(timeout: 12))
    }
}
