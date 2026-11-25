import XCTest
/// A menu that opens while the keyboard is up; on an iPad this once stopped the app.
final class KeyboardTests: JourfoldTest {
    func testExpenseMenuWithKeyboard() { open(["-sheet", "expense"]); type("Amount", "3"); tap("Currency,"); sleep(2); XCTAssertTrue(app.state == .runningForeground); app.buttons["JPY"].tap(); sleep(2); XCTAssertTrue(app.state == .runningForeground) }
    func testBudgetMenuOnly() { open(["-sheet", "budget"]); shows("New budget", 15); tap("Person, Everyone"); sleep(2); XCTAssertTrue(app.state == .runningForeground) }
    func testBudgetTitleOnly() { open(["-sheet", "budget"]); type("Title", "Food"); sleep(2); XCTAssertTrue(app.state == .runningForeground) }
    func testBudgetBothFields() { open(["-sheet", "budget"]); type("Title", "Food"); type("Amount in EUR", "30"); sleep(2); XCTAssertTrue(app.state == .runningForeground) }
    func testTripBothFields() { open(["-page", "converter"]); type("Amount", "3"); sleep(2); XCTAssertTrue(app.state == .runningForeground) }
    func testBookingFields() { open(["-sheet", "booking"]); type("Provider", "Ex"); type("Amount still to pay", "30"); sleep(2); tap("Status,"); sleep(2); XCTAssertTrue(app.state == .runningForeground) }
    func testBudgetAll() { open(["-sheet", "budget"]); type("Title", "Food"); type("Amount in EUR", "30"); tap("Category, All"); app.buttons["Food"].firstMatch.tap(); sleep(2); XCTAssertTrue(app.state == .runningForeground); tap("Person, Everyone"); sleep(1); app.buttons["Sam"].firstMatch.tap(); sleep(1); done(); sleep(2); XCTAssertTrue(app.state == .runningForeground) }
}
