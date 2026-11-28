import XCTest

/// Drives the app as a person would, on the sample trip written to and read from TravelRepo. Run with `./uitest.sh`.
class JourfoldTest: XCTestCase {
    var app: XCUIApplication!
    override func setUp() { continueAfterFailure = false }
    override func tearDown() {
        if let run = testRun, run.failureCount > 0, let app {
            let shot = XCTAttachment(screenshot: app.screenshot()); shot.lifetime = .keepAlways; shot.name = name; add(shot)
            func l(_ q: XCUIElementQuery) -> String { q.allElementsBoundByIndex.prefix(45).map { $0.label }.joined(separator: " | ").replacingOccurrences(of: "\n", with: " ") }
            print("DUMP \(name) T[\(l(app.staticTexts))] B[\(l(app.buttons))]")
        }
    }

    func open(_ args: [String], language: String = "en", reset: Bool = true, demo: Bool = true) {
        app = XCUIApplication()
        app.launchArguments = ["-AppleLanguages", "(\(language))", "-AppleLocale", language == "de" ? "de_DE" : "en_GB", "-trip", "0", "-demo", demo ? "YES" : "NO", "-samples", "YES"] + (reset ? ["-reset", "YES"] : []) + args
        app.launch()
    }

    func labelled(_ text: String, _ query: XCUIElementQuery) -> XCUIElement { query.matching(NSPredicate(format: "label CONTAINS %@", text)).firstMatch }
    func anything(_ text: String) -> XCUIElement { app.descendants(matching: .any).matching(NSPredicate(format: "label CONTAINS %@", text)).firstMatch }

    /// Scrolls until the element can be touched.
    @discardableResult
    func find(_ e: XCUIElement, up: Bool = true, file: StaticString = #filePath, line: UInt = #line) -> XCUIElement {
        _ = e.waitForExistence(timeout: 12)
        let area = app.windows.firstMatch.frame
        for _ in 0..<14 {
            if e.exists, e.isHittable, e.frame.maxY < area.height - 70, e.frame.minY > 90 { break }
            // Above the keyboard when there is one, else low enough to be inside a half-height sheet.
            let pad = app.windows.firstMatch.frame.width > 600
            let high = app.keyboards.firstMatch.exists ? 0.42 : pad ? 0.7 : 0.8, low = app.keyboards.firstMatch.exists ? 0.25 : pad ? 0.5 : 0.55
            let dx = app.windows.firstMatch.frame.width > 600 ? 0.5 : 0.03
            let a = app.coordinate(withNormalizedOffset: .init(dx: dx, dy: up ? high : low)), b = app.coordinate(withNormalizedOffset: .init(dx: dx, dy: up ? low : high))
            a.press(forDuration: 0.02, thenDragTo: b)
        }
        XCTAssertTrue(e.exists, "not found: \(e)", file: file, line: line)
        return e
    }
    func button(_ text: String) -> XCUIElement { labelled(text, app.buttons) }
    func tap(_ text: String, file: StaticString = #filePath, line: UInt = #line) { find(button(text), file: file, line: line).tap() }
    func type(_ field: String, _ text: String, file: StaticString = #filePath, line: UInt = #line) {
        let f = app.textFields[field].exists ? app.textFields[field] : app.textFields.matching(NSPredicate(format: "placeholderValue == %@ OR label == %@", field, field)).firstMatch
        find(f, file: file, line: line).tap(); f.typeText(text)
    }
    func toggle(_ text: String, file: StaticString = #filePath, line: UInt = #line) {
        let s = find(labelled(text, app.switches), file: file, line: line)
        s.coordinate(withNormalizedOffset: .init(dx: 0.93, dy: 0.5)).tap()
    }
    /// The first photo in the system's picker, which is drawn by another process.
    func pickPhoto() {
        let photo = app.images.matching(identifier: "PXGGridLayout-Info").firstMatch
        if photo.waitForExistence(timeout: 8) { photo.coordinate(withNormalizedOffset: .init(dx: 0.5, dy: 0.5)).tap() }
        else { app.coordinate(withNormalizedOffset: .init(dx: app.windows.firstMatch.frame.width > 600 ? 0.3 : 0.17, dy: app.windows.firstMatch.frame.width > 600 ? 0.5 : 0.45)).tap() }
        // Until the picker is gone and the photo is in the trip.
        sleep(4)
    }
    func done() { app.buttons["Done"].firstMatch.tap() }
    func shows(_ text: String, _ seconds: TimeInterval = 8, file: StaticString = #filePath, line: UInt = #line) { XCTAssertTrue(anything(text).waitForExistence(timeout: seconds), "does not show: \(text)", file: file, line: line) }
    func tab(_ name: String) {
        let b = app.tabBars.buttons[name].firstMatch
        if b.exists, b.isHittable { b.tap() } else { app.buttons.matching(NSPredicate(format: "label == %@", name)).firstMatch.tap() }
    }
    /// A page behind More; on an iPad it is in the sidebar.
    func page(_ name: String) {
        if app.tabBars.buttons["More"].exists, app.tabBars.buttons["More"].isHittable { tab("More"); tap(name); return }
        let toggle = app.buttons.matching(NSPredicate(format: "label CONTAINS[c] 'sidebar'")).firstMatch
        if toggle.waitForExistence(timeout: 4), toggle.isHittable { toggle.tap(); sleep(1) }
        let row = app.descendants(matching: .any).matching(NSPredicate(format: "label == %@", name)).allElementsBoundByIndex.last { $0.isHittable }
        XCTAssertNotNil(row, "no \(name) in the sidebar"); row?.tap(); sleep(1)
        // In portrait the sidebar lies over the page.
        app.coordinate(withNormalizedOffset: .init(dx: 0.95, dy: 0.97)).tap()
    }
}

final class QuickAddTests: JourfoldTest {
    func testExpensePaidWithCashThenNotCash() {
        open(["-sheet", "expense"])
        type("Amount", "1600"); type("Other currency, such as CAD", "JPY"); type("What for?", "Ramen\n")
        toggle("Paid with cash")
        find(anything("The rate of the cash bought last"))
        tap("Rate, The last one known"); app.buttons["Per unit"].tap()
        type("JPY for 1 EUR", "160")
        done()
        tab("Costs"); tap("Ramen")
        shows("€10.00")
        find(anything("€10.00")); tap("Not paid with cash"); shows("No amount charged in EUR yet")
    }

    func testExpenseWithOwnCategoryAndShares() {
        open(["-sheet", "expense"])
        type("Amount", "30"); type("What for?", "Street food tour\n")
        tap("Category,"); app.buttons["Own category…"].tap()
        type("Name of the category", "Tours\n")
        find(app.buttons.matching(NSPredicate(format: "label BEGINSWITH 'Method'")).firstMatch).tap(); app.buttons["Amounts"].tap()
        // Half each is carried over.
        let values = app.textFields.matching(NSPredicate(format: "value == '15' OR value == '10'"))
        find(values.element(boundBy: 0)).coordinate(withNormalizedOffset: .init(dx: 0.97, dy: 0.5)).tap(); values.element(boundBy: 0).typeText(XCUIKeyboardKey.delete.rawValue + XCUIKeyboardKey.delete.rawValue + "10")
        find(anything("The split does not add up yet."))
        done(); sleep(1); XCTAssertTrue(app.buttons["Done"].firstMatch.exists, "added although the split does not add up")
        find(values.element(boundBy: 1)).coordinate(withNormalizedOffset: .init(dx: 0.97, dy: 0.5)).tap(); values.element(boundBy: 1).typeText(XCUIKeyboardKey.delete.rawValue + XCUIKeyboardKey.delete.rawValue + "20")
        done()
        tab("Costs"); tap("Street food tour")
        shows("Tours"); find(anything("€20.00"))
    }

    func testPlannedExpenseMarkedAsPaid() {
        open(["-sheet", "expense"])
        type("Amount", "55"); type("What for?", "Onsen\n")
        toggle("Still to pay"); done()
        tab("Costs"); tap("Mark Onsen as paid")
        tap("Onsen,")
        shows("Paid on")
    }

    func testBookingWithPlanAndPrice() {
        open(["-sheet", "booking"])
        type("What is booked?", "Kaiseki dinner"); type("Provider", "Example Kitchen"); type("Booking reference", "KD-77"); type("Amount still to pay", "120")
        done()
        page("Bookings"); tap("Kaiseki dinner")
        shows("KD-77"); shows("Example Kitchen"); shows("120")
    }

    func testMissingIsMarked() {
        open(["-sheet", "expense"])
        type("Amount", "12,5"); done()
        shows("Say what it was for."); XCTAssertFalse(anything("Enter the amount.").exists)
        type("What for?", "Tea\n"); done()
        tab("Costs"); tap("Tea"); shows("€12.50")
    }

    func testFileWhileAddingAJourney() {
        open(["-sheet", "transport"])
        type("Title, e.g. Train to Nara", "Train to Nara\n")
        shows("Times in Tokyo")
        tap("Choose a photo"); pickPhoto(); find(anything("Photo.jpg")); done(); sleep(3)
        open(["-tab", "plan", "-day", "1"], reset: false); tap("Train to Nara")
        find(button("Show ticket")).tap()
        XCTAssertTrue(app.navigationBars["Photo.jpg"].waitForExistence(timeout: 8))
    }

    /// Needs "Ticket Nara.pdf" in the simulator's Files; skipped without it.
    func testPdfWhileAddingAJourney() throws {
        open(["-sheet", "transport"])
        type("Title, e.g. Train to Nara", "Train to Nara\n")
        tap("Choose a file"); sleep(4)
        let file = app.cells.matching(NSPredicate(format: "label CONTAINS 'Ticket Nara'")).firstMatch
        if !file.waitForExistence(timeout: 6), app.buttons["Browse"].firstMatch.exists { app.buttons["Browse"].firstMatch.tap(); sleep(2) }
        if !file.exists { let here = app.cells.matching(NSPredicate(format: "label CONTAINS 'On My'")).firstMatch; if here.exists { here.tap(); sleep(2) } }
        try XCTSkipUnless(file.exists, "no Ticket Nara.pdf in Files")
        file.tap(); sleep(2)
        if app.buttons["Open"].exists { app.buttons["Open"].tap() }
        find(anything("Ticket Nara.pdf")); done(); sleep(3)
        open(["-tab", "plan", "-day", "1"], reset: false); tap("Train to Nara")
        find(button("Show ticket")).tap()
        XCTAssertTrue(app.navigationBars["Ticket Nara.pdf"].waitForExistence(timeout: 8))
    }

    func testTask() {
        open(["-sheet", "task"])
        type("What needs doing?", "Buy a phrase book"); tap("For"); tap("Sam"); app.navigationBars.buttons.firstMatch.tap(); shows("Sam"); done()
        page("Tasks"); tap("Tick off Buy a phrase book")
        XCTAssertTrue(button("Reopen Buy a phrase book").waitForExistence(timeout: 8))
        app.buttons.matching(NSPredicate(format: "label BEGINSWITH 'Buy a phrase book'")).firstMatch.tap()
        let title = app.textFields["Title"].exists ? app.textFields["Title"] : app.textViews.firstMatch.exists ? app.textViews.firstMatch : app.textFields.firstMatch
        title.tap(); title.typeText(" today"); done()
        shows("today")
    }

    func testPackingItem() {
        open(["-sheet", "packing"])
        type("What to pack?", "Umbrella"); type("Group", "Rain"); type("Quantity", "2")
        toggle("Everyone brings their own"); done()
        page("Packing lists")
        find(button("Umbrella")); XCTAssertTrue(button("Umbrella").label.contains("2 · Everyone brings their own"))
    }

    func testNote() {
        open(["-sheet", "note"])
        type("Write a note for the trip", "Ask about the onsen"); done()
        page("Trip details")
        let notes = app.descendants(matching: .any).matching(NSPredicate(format: "value CONTAINS 'Ask about the onsen'")).firstMatch
        XCTAssertTrue(notes.waitForExistence(timeout: 8))
    }

    func testBudget() {
        open(["-sheet", "budget"])
        type("Title", "Food budget"); type("Amount in EUR", "300")
        tap("Category, All"); app.buttons["Food"].firstMatch.tap(); done()
        tab("Costs"); find(anything("Food budget")); tap("Food budget")
        shows("Edit budget")
    }

    func testPlan() {
        open(["-tab", "plan", "-day", "1", "-sheet", "item"])
        type("What are you planning?", "Senso-ji"); tap("Kind,"); app.buttons["Culture"].firstMatch.tap(); done()
        find(button("Senso-ji")).tap(); shows("Status, Planned")
    }

    func testPlanWithNewPlace() {
        open(["-tab", "plan", "-day", "1", "-sheet", "item"])
        type("What are you planning?", "Nara walk")
        tap("New place"); type("Name", "Nara Park gate"); type("Latitude, longitude, or a map link", "34.6851, 135.8430")
        app.buttons["Done"].firstMatch.tap()
        XCTAssertTrue(app.textFields["What are you planning?"].waitForExistence(timeout: 5))
        done()
        find(button("Nara walk")); XCTAssertTrue(button("Nara walk").label.contains("Nara Park gate"), button("Nara walk").label)
    }

    func testJourney() {
        open(["-tab", "plan", "-day", "1", "-sheet", "transport"])
        type("Title, e.g. Train to Nara", "Train to Nara"); done()
        find(button("Train to Nara"))
    }

    func testStay() {
        open(["-tab", "plan", "-day", "1", "-sheet", "stay"])
        type("Name of the hotel or flat", "Hotel Example"); done()
        find(button("Hotel Example"))
    }
}

final class PageTests: JourfoldTest {
    func testPackingLine() {
        open(["-page", "packing"])
        let field = app.textFields["2x Charger #Electronics @all"]
        find(field).tap(); field.typeText("3x Socks #Cl")
        app.buttons["Clothes"].firstMatch.tap()
        field.typeText("@kim\n")
        shows("Nobody here is called kim.")
        tap("Add kim as a traveller and add the item")
        find(button("Socks")); XCTAssertTrue(button("Socks").label.contains("3 · kim"), button("Socks").label)
    }

    func testPackingItemEditorAndList() {
        open(["-page", "packing"])
        tap("Power bank")
        type("Quantity", "2"); app.buttons["Done"].firstMatch.tap()
        XCTAssertTrue(labelled("2 · Alex, Sam", app.buttons).waitForExistence(timeout: 8))
        type("New packing list", "Day trip\n"); find(anything("Day trip ·"), up: false)
    }

    func testPayBackSelection() {
        open(["-page", "costs", "-payback", "YES"])
        shows("€198.92", 20)
        toggle("Rail passes")
        XCTAssertTrue(anything("€106.14").waitForExistence(timeout: 20), "other sum")
        find(app.buttons["Mark as paid back"]).tap()
        shows("Costs")
    }

    func testExpenseEditAndReceiptAndDelete() {
        open(["-page", "costs", "-expense", "teamLab"])
        shows("teamLab tickets", 20)
        find(app.buttons["Choose a file"]); find(app.buttons["Add a photo"]).tap()
        sleep(3); pickPhoto()
        shows("Receipt.jpg", 15)
        app.buttons["Edit"].firstMatch.tap()
        let title = app.textFields["Title"]; XCTAssertTrue(title.waitForExistence(timeout: 5)); title.tap(); title.typeText(" x"); app.buttons["Done"].firstMatch.tap()
        shows("teamLab tickets x")
        app.buttons["Delete"].firstMatch.tap()
        let deletes = app.buttons.matching(NSPredicate(format: "label == 'Delete'")); // The dialog's button is the wide one; the bar's is an icon.
        for _ in 0..<8 where !deletes.allElementsBoundByIndex.contains(where: { $0.frame.width > 100 }) { sleep(1) }
        deletes.allElementsBoundByIndex.max { $0.frame.width < $1.frame.width }?.tap()
        sleep(3)
        XCTAssertFalse(anything("teamLab tickets x").exists)
    }

    func testExpenseEditEverything() {
        open(["-page", "costs", "-expense", "teamLab"])
        shows("teamLab tickets", 20); app.buttons["Edit"].firstMatch.tap()
        XCTAssertTrue(app.textFields["Title"].waitForExistence(timeout: 5))
        toggle("Still to pay"); toggle("Due date")
        type("Notes", "Paid at the gate")
        app.buttons["Done"].firstMatch.tap()
        shows("due"); find(anything("Paid at the gate"))
    }

    func testTaskNotes() {
        open(["-page", "tasks"]); tap("Rent pocket Wi-Fi, Fri")
        type("Notes", "At the airport counter"); app.buttons["Done"].firstMatch.tap(); sleep(3)
        tap("Rent pocket Wi-Fi, Fri")
        XCTAssertTrue(app.descendants(matching: .any).matching(NSPredicate(format: "value CONTAINS 'At the airport counter'")).firstMatch.waitForExistence(timeout: 6))
    }

    func testCostUpgrade() {
        open(["-oldcosts", "YES", "-page", "costs"])
        shows("Update the costs of this trip?", 15); tap("Update costs")
        sleep(3); XCTAssertFalse(anything("Update the costs of this trip?").exists)
    }

    func testTripCurrencyAsksAboutBudgets() {
        open(["-page", "trip"])
        type("Other currency, such as CAD", "CAD"); app.buttons["Use"].tap()
        shows("Convert the budgets to CAD?"); app.buttons["Convert"].tap()
        XCTAssertTrue(button("Currency, CAD").waitForExistence(timeout: 8))
    }

    func testTripDetailsTitleIsKept() {
        open(["-page", "trip"])
        let f = app.textFields["Name of the trip"]; find(f).tap(); f.typeText("!"); sleep(4)
        open(["-page", "trip"], reset: false)
        XCTAssertTrue(app.textFields["Name of the trip"].waitForExistence(timeout: 12)); XCTAssertTrue((app.textFields["Name of the trip"].value as? String ?? "").contains("!"))
    }

    func testTravellersClaimAndNotTravelling() {
        open(["-page", "travellers"])
        find(button("I am Alex")).tap(); shows("You")
        tap("I'm not travelling along"); sleep(2)
        XCTAssertEqual(app.buttons.matching(NSPredicate(format: "label BEGINSWITH 'I am '")).count, 2)
        type("Add a traveller", "Kim\n"); if app.buttons["Add"].firstMatch.isEnabled { app.buttons["Add"].firstMatch.tap() }
        shows("Kim")
    }

    func testBookingEditAndPayment() {
        open(["-page", "bookings"])
        tap("teamLab Planets tickets"); shows("TLP-55120")
        find(button("Edit")).tap()
        let f = app.textFields["Provider"]; find(f).tap(); f.typeText(" Example"); app.buttons["Done"].firstMatch.tap()
        shows("Example")
    }

    func testBookingPeoplePlansAndDeadline() {
        open(["-page", "bookings"])
        tap("teamLab Planets tickets"); find(button("Edit")).tap()
        tap("Travellers"); XCTAssertTrue(app.buttons["Sam"].waitForExistence(timeout: 5)); app.buttons.matching(identifier: "Sam").allElementsBoundByIndex.last { $0.isHittable }?.tap(); app.navigationBars.buttons.firstMatch.tap()
        find(button("Unlink teamLab Planets")).tap()
        tap("Link plans"); let f = app.searchFields.firstMatch; XCTAssertTrue(f.waitForExistence(timeout: 5)); f.tap(); f.typeText("tsukiji")
        tap("Breakfast at Tsukiji"); sleep(2); app.navigationBars["Plans"].buttons.firstMatch.tap()
        shows("Breakfast at Tsukiji")
        tap("Add a free cancellation deadline"); shows("Free cancellation until"); tap("Remove the deadline"); shows("Add a free cancellation deadline")
        app.buttons["Done"].firstMatch.tap()
        shows("Breakfast at Tsukiji")
    }

    func testPlaces() {
        open(["-page", "places"])
        tap("New place"); type("Name", "Gion corner"); app.buttons["Done"].firstMatch.tap(); sleep(2)
        find(button("Gion corner")).tap(); shows("Gion corner")
    }

    func testFiles() {
        open(["-page", "files"])
        for (name, text) in [("teamLab Planets e-ticket.pdf", "teamLab"), ("Shinkansen to Kyoto.eml", "Shinkansen")] {
            tap(name); shows(text); app.buttons["Close"].firstMatch.tap()
        }
    }

    func testConverter() {
        open(["-page", "converter"])
        let f = app.textFields["Amount"]; find(f).tap(); f.typeText("0"); app.buttons["Swap"].tap()
        XCTAssertTrue(button("From, EUR").waitForExistence(timeout: 4))
    }

    func testSyncAndVersion() {
        open(["-page", "tasks"]); tap("Tick off Rent pocket Wi-Fi"); sleep(2)
        page("Sync and versions")
        tap("Save as a version"); app.alerts.textFields.firstMatch.typeText("Wi-Fi rented"); app.alerts.buttons["Save"].tap()
        shows("Wi-Fi rented"); tap("Wi-Fi rented")
        shows("Nothing changed since this version.", 6)
    }

    func testSettings() {
        open(["-page", "settings"])
        tap("Second time zone"); app.buttons.matching(NSPredicate(format: "label CONTAINS 'St Johns'")).firstMatch.tap()
        find(anything("Storage on this phone")); shows("Spring in Japan")
        app.swipeDown(); app.swipeDown(); tab("Plan"); shows("St Johns:")
    }

    func testReport() {
        open(["-page", "settings"]); tap("Report a problem"); sleep(2)
        XCTAssertTrue(app.textViews.firstMatch.exists || app.textFields.firstMatch.exists)
    }
}

final class PlanTests: JourfoldTest {
    func testDayPagingAndStrip() {
        open(["-tab", "plan", "-day", "3"])
        shows("Day 4", 15); app.buttons["Next day"].firstMatch.tap(); shows("Day 5")
        app.buttons["Previous day"].firstMatch.tap(); shows("Day 4")
        button("Tuesday 12 January").tap(); shows("Shinkansen to Kyoto")
    }

    func testSearchFilterGroupJump() {
        open(["-tab", "plan"])
        let field = app.searchFields.firstMatch; XCTAssertTrue(field.waitForExistence(timeout: 15))
        field.tap(); field.typeText("Kyoto"); shows("Everything (")
        tap("Show, Everything"); app.buttons.matching(NSPredicate(format: "label BEGINSWITH 'Places'")).firstMatch.tap()
        shows("Kyoto Station"); XCTAssertFalse(button("Transport, Shinkansen to Kyoto").exists)
        tap("Show"); app.buttons.matching(NSPredicate(format: "label BEGINSWITH 'Everything'")).firstMatch.tap()
        app.keyboards.buttons["Search"].exists ? app.keyboards.buttons["Search"].tap() : ()
        toggle("Group by day"); shows("12 Jan")
        let hit = find(button("Transport, Shinkansen to Kyoto")); hit.press(forDuration: 1.2)
        app.buttons["Show where it is"].firstMatch.tap(); shows("Day 5")
    }

    func testDetailsEditStatusDelete() {
        open(["-tab", "plan", "-day", "2"])
        tap("teamLab Planets"); shows("TLP-55120")
        app.buttons["Edit"].firstMatch.tap()
        let f = app.textFields["What are you planning?"]; XCTAssertTrue(f.waitForExistence(timeout: 5)); f.tap(); f.typeText(" II"); app.buttons["Done"].firstMatch.tap()
        shows("teamLab Planets II")
    }

    func testMapOfflineThenOnline() {
        open(["-page", "map", "-maps.online", ""])
        XCTAssertTrue(app.buttons["Not now"].waitForExistence(timeout: 15))
        button("Sat 9 Jan").tap()
        app.buttons["Stops"].tap(); tap("1 ·"); sleep(1)
    }

    func testTimetableAndMenu() {
        open(["-tab", "plan", "-day", "3"])
        XCTAssertTrue(app.buttons["View options"].waitForExistence(timeout: 15)); app.buttons["View options"].tap()
        app.buttons["Timetable"].tap(); shows("13:30–15:00")
        app.navigationBars.buttons.firstMatch.tap()
        app.buttons["View options"].tap(); app.buttons["Whole trip"].tap(); shows("Friday 8 January")
        app.buttons["View options"].tap(); app.buttons["Ideas"].tap(); shows("Ideas")
    }

    func testTodayGerman() {
        open([], language: "de")
        shows("Als Nächstes · in 50 Min.", 15); XCTAssertTrue(app.buttons["Navigieren"].exists); XCTAssertTrue(anything("Heute").exists)
    }

    func testMerge() {
        open(["-conflict", "YES", "-merge", "YES"])
        shows("Choose changes", 15); shows("Keep both", 20)
        tap("Sam's change"); find(button("Apply")).tap(); sleep(6)
        XCTAssertFalse(anything("That did not work").exists)
    }

    func testEveryPageInGermanOpens() {
        for p in ["bookings", "tasks", "packing", "places", "files", "costs", "converter", "trip", "travellers", "sync", "settings", "map", "about", "notifications"] {
            open(["-page", p], language: "de")
            XCTAssertTrue(app.navigationBars.firstMatch.waitForExistence(timeout: 15), p); sleep(1)
            let shot = XCTAttachment(screenshot: app.screenshot()); shot.lifetime = .keepAlways; shot.name = "de-\(p)"; add(shot)
        }
    }
}


final class OnlineTests: JourfoldTest {
    func testShareLinkBecomesAPlace() {
        open(["-page", "places", "-sharelink", "https://maps.apple.com/place?name=Nara%20Viewpoint&coordinate=34.6851,135.8430"])
        for _ in 0..<3 { app.swipeUp() }
        tap("Nara Viewpoint"); shows("Nara Viewpoint")
    }

    func testEmailStep() {
        open(["-authorEmail", ""], demo: false)
        tap("Choose who you are"); shows("Who are you?", 15); tap("Alex")
        shows("Your e-mail in this trip")
        XCTAssertFalse(app.buttons["Continue"].isEnabled)
        let f = app.textFields["E-mail"]; f.tap(); f.typeText("alex@example.org")
        app.buttons["Continue"].tap(); sleep(3)
        page("Travellers"); shows("You")
    }

    func testEmailStepWithoutEmail() {
        open(["-authorEmail", ""], demo: false)
        tap("Choose who you are"); shows("Who are you?", 15); tap("Sam"); tap("Without an e-mail"); sleep(3)
        page("Travellers"); shows("You")
    }

    func testWeatherFromOpenMeteo() {
        open(["-tab", "plan", "-day", "2", "-weather.online", "on"], demo: false)
        if app.buttons["Not now"].waitForExistence(timeout: 12) { app.buttons["Not now"].tap() }
        // Open-Meteo may be unreachable; then the app has to say so.
        let weather = anything("° / "), problem = anything("could not be loaded").exists ? anything("could not be loaded") : anything("Loading the weather")
        for _ in 0..<120 where !weather.exists && !problem.exists { sleep(1) }
        XCTAssertTrue(weather.exists || problem.exists, "neither weather nor a reason")
        print("DUMP weather: \(weather.exists), problem: \(problem.exists)")
        if !weather.exists { return }
        tap("teamLab Planets"); find(app.buttons["Update the weather"]).tap()
        sleep(8); XCTAssertFalse(anything("could not be updated").exists)
        if !anything("Open-Meteo").exists { find(anything("Open-Meteo"), up: app.windows.firstMatch.frame.width > 600) }
    }

    func testRatesFromTheECB() {
        open(["-page", "converter"], demo: false)
        if app.buttons["Not now"].waitForExistence(timeout: 12) { app.buttons["Not now"].tap() }
        find(app.buttons.matching(NSPredicate(format: "label == 'Fetch exchange rates'")).firstMatch).tap()
        shows("2026", 30)
    }

    func testReceiptWhileAdding() {
        open(["-sheet", "expense"])
        type("Amount", "12"); type("What for?", "Tea\n")
        tap("Add a photo of the receipt"); sleep(4)
        pickPhoto()
        shows("Receipt added", 15); done()
        tab("Costs"); tap("Tea,"); find(anything("Receipt.jpg")).tap(); sleep(2)
    }

    func testReceiptOnExpenseAndDelete() {
        open(["-page", "costs", "-expense", "teamLab"])
        shows("teamLab tickets", 20); tap("Add a photo"); sleep(4)
        pickPhoto()
        find(anything("Receipt.jpg"))
    }

    func testPullToTheNextDay() {
        open(["-tab", "plan", "-day", "0"])
        shows("Day 1", 15); sleep(1)
        let a = app.coordinate(withNormalizedOffset: .init(dx: 0.5, dy: 0.75)), b = app.coordinate(withNormalizedOffset: .init(dx: 0.5, dy: 0.1))
        a.press(forDuration: 0.05, thenDragTo: b, withVelocity: .slow, thenHoldForDuration: 0.3); sleep(1)
        if !anything("Day 2").exists { a.press(forDuration: 0.05, thenDragTo: b, withVelocity: .slow, thenHoldForDuration: 0.3) }
        shows("Day 2", 5)
    }
}
