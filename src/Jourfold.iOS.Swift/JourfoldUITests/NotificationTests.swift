import XCTest

/// Tapping a notification, with the app in front, behind, and not running; and the sheets that hang on lists.
final class NotificationTests: JourfoldTest {
    private var springboard: XCUIApplication { XCUIApplication(bundleIdentifier: "com.apple.springboard") }

    private func showOne() {
        open(["-page", "notifications"])
        find(app.buttons["Show the next one now"]).tap()
        let allow = springboard.alerts.buttons.element(boundBy: 1)
        if allow.waitForExistence(timeout: 3) { allow.tap(); sleep(1); app.activate(); if app.buttons["Show the next one now"].isHittable { app.buttons["Show the next one now"].tap() } }
    }
    private func tapNotification() {
        let banner = springboard.descendants(matching: .any).matching(identifier: "NotificationShortLookView").firstMatch
        if banner.waitForExistence(timeout: 9), banner.isHittable { banner.tap(); return }
        // Already gone: from the Notification Centre.
        let top = springboard.coordinate(withNormalizedOffset: .init(dx: 0.2, dy: 0.005)), low = springboard.coordinate(withNormalizedOffset: .init(dx: 0.2, dy: 0.7))
        top.press(forDuration: 0.1, thenDragTo: low); sleep(2)
        let cell = springboard.descendants(matching: .any).matching(NSPredicate(format: "identifier == 'NotificationShortLookView' OR label CONTAINS 'Leave for'")).firstMatch
        if !cell.waitForExistence(timeout: 5) { print("DUMP SB " + springboard.debugDescription.prefix(2500).replacingOccurrences(of: "\n", with: " ")) }
        XCTAssertTrue(cell.exists, "no notification"); cell.tap()
    }

    func testTapInFront() { showOne(); tapNotification(); sleep(4); XCTAssertEqual(app.state, .runningForeground) }
    func testTapBehind() { showOne(); XCUIDevice.shared.press(.home); tapNotification(); sleep(4); XCTAssertEqual(app.state, .runningForeground) }
    func testTapWhenNotRunning() { showOne(); XCUIDevice.shared.press(.home); sleep(1); app.terminate(); tapNotification(); sleep(10); XCTAssertEqual(app.state, .runningForeground) }

    func testSheetsFromDetails() {
        open(["-tab", "plan", "-day", "2"])
        tap("teamLab Planets"); shows("TLP-55120")
        tap("Add a payment"); XCTAssertTrue(app.textFields["Amount"].waitForExistence(timeout: 6))
    }
    func testSheetsFromSearch() {
        open(["-tab", "plan"])
        let field = app.searchFields.firstMatch; XCTAssertTrue(field.waitForExistence(timeout: 15)); field.tap(); field.typeText("teamLab\n")
        find(button("teamLab Planets tickets")).tap(); shows("TLP-55120"); sleep(1)
    }
    func testOneReceipt() {
        open(["-page", "costs", "-expense", "teamLab"])
        shows("teamLab tickets", 20); tap("Add a photo"); sleep(4)
        app.coordinate(withNormalizedOffset: .init(dx: 0.17, dy: 0.45)).tap()
        find(anything("Receipt.jpg")); sleep(4)
        XCTAssertEqual(app.buttons.matching(NSPredicate(format: "label CONTAINS 'Receipt.jpg'")).count, 1)
    }
}

final class FileTests: JourfoldTest {

    func testPhotoToANewBooking() {
        open(["-sheet", "document"])
        app.staticTexts["Add to"].firstMatch.tap(); app.buttons["New booking"].firstMatch.tap()
        tap("Choose a photo"); pickPhoto()
        page("Bookings"); tap("Photo"); shows("Photo.jpg")
    }
    func testPhotoToAPlan() {
        open(["-tab", "plan", "-day", "2"])
        tap("Senso-ji and Asakusa"); tap("Add a file or scan"); tap("Choose a photo"); pickPhoto()
        find(anything("Photo.jpg"))
    }
    func testFileToABooking() {
        open(["-page", "files"])
        find(button("Deer crackers.png")).press(forDuration: 1.2)
        app.buttons["Add to a plan or booking"].firstMatch.tap()
        find(app.buttons["Tea ceremony"].firstMatch).tap(); sleep(3)
        XCTAssertTrue(button("Deer crackers.png").label.contains("Tea ceremony"), button("Deer crackers.png").label)
    }
    func testBookingNotes() {
        open(["-page", "bookings"]); tap("Tea ceremony"); find(button("Edit")).tap()
        type("Notes", "Arrive early"); app.buttons["Done"].firstMatch.tap()
        shows("Arrive early")
    }
}
