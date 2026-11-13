# Changelog

All notable changes to Jourfold. Generated from `docs/releases` by `python eng/changelog.py`; do not edit by hand.

## 0.11.1 “Funky Ferret” – 2026-11-12

### Highlights

- Phones and tablets save your changes as versions again and send them with the next sync. Before, a trip copied from GitHub kept its changes on the phone, while sync said it was up to date. Changes that waited on the phone are sent now.

### Changes

- Versions are saved under the email the trip has for you ("This is me"), otherwise the one under Settings, otherwise your GitHub account's public email, otherwise GitHub's private address for your account.
- On phones and tablets, the settings open from the gear at the top of the trip list, too.
- On phones and tablets, the costs by category show each category's part in percent and as a bar, as on the computer.

### Fixes

- When a change cannot be saved as a version, Sync and versions says why and the sync says so, instead of reporting the trip as synced.
- On the computer, the mouse wheel over a date, an amount or a closed list such as the currency scrolls the page instead of changing the value.
- On phones and tablets, a finger between two columns of a date or time wheel no longer scrolls the form behind it.
- On Android with the back, home and recent buttons, the last button of a form or the details, such as Save or Edit, is no longer half hidden behind them.
- Hiding the keyboard no longer makes the details jump, and the page no longer stays half its height with no bottom bar after leaving a trip while typing.
- Opening a trip again right after leaving it, or tapping it twice, no longer says it is open elsewhere.

## 0.11.0 “Frosty Ferret” – 2026-11-12

### Highlights

- Jourfold runs on iPhone and iPad, with the same trips, Today, plan, map, costs and packing lists as on Android, sync with GitHub, reminders and the trips in the calendar. It is not in the App Store. Each release has the app attached, to install with your Apple ID through AltStore, SideStore or Sideloadly.

### Changes

- On phones and tablets, "Show everything" on the map shows a long route whole, such as Frankfurt to Tokyo, and keeps it clear of the map's buttons.
- The timeline names a day's time zone by the town where its first plan starts, such as Frankfurt am Main, as the list does, instead of the city the zone is named after.
- Time ranges such as 16:00–18:00 and durations such as 11 h 15 min no longer break across two lines.
- On an iPad with a keyboard, ⌘C, ⌘X, ⌘V, ⌘A, ⌘Z and ⇧⌘Z work in text fields, and shortcuts are shown with ⌘.
- On iPhone and iPad, "Navigate" opens Apple Maps, and the calendar settings say whether the trips' calendars are kept in iCloud or another online account.

## 0.10.0 “Oceanic Osprey” – 2026-11-11

### Highlights

- Costs are reworked. A trip has one currency for its totals, its budget and who owes whom. What a card charged stays exact; everything else is estimated with the day's rates and marked as such. A booking costs what its payments come to, paid or still to pay with a due date.
- Expenses are divided equally, by amounts, by shares or in percent. Who owes whom shows one line per person, and "Pay back…" settles it with the fewest payments.
- Phones and tablets have the full Costs page too, with budgets, splits, paying back and the search. Today lists payments that are due soon.

### Changes

- Adding an expense asks for everything at once, on the computer and on phones and tablets. That covers the amount and what the card charged, whether it is still to pay with a due date or the day it was paid, and who paid. It also covers what it belongs to, the category, the split and a receipt. It can only be added once the split adds up.
- A trip has one budget, and more budgets can be limited to a category, a person or a collection. When you change the trip currency, Jourfold offers to convert budgets in the old one. A budget that cannot be converted yet is shown in its own currency.
- Payments are listed by day. A search finds them by name, category, the people involved, and the booking or place they belong to.
- Costs can be exported on the computer as PDF, as CSV with a chosen separator, or as text, for everyone or for the part of chosen people.
- When a trip has costs from an older version, Jourfold offers to bring them up to date and lists what changes. Booking prices become payments still to pay, and nothing that was paid, owed or paid back changes. If you put it off, the Costs page offers it again.
- Amounts are shown with the decimals of their currency, so yen have none. You are marked "(You)" everywhere, and the German texts consistently say "Sie".
- On phones and tablets, amounts open the number keyboard. Tapping beside a field or scrolling the page closes the keyboard.
- On phones and tablets, the GitHub sign-in code can be copied again with "Copy code" or by holding it, for when GitHub's own sign-in has replaced the clipboard.

### Fixes

- Attached e-mails keep their line endings, whatever Git is set to convert. On a Mac set to convert them, every other copy of the trip reported the e-mail as damaged and refused all changes. The computer that attached the e-mail repairs the trip with its next saved version, for example when you agree to update the costs. Other copies are repaired by downloading the trip again.
- Phones and tablets explain a failed change instead of closing, for example "This is me" in a trip with errors. Unexpected errors are written to a log on the device and nothing is sent. Today shows a card when the open trip has errors.
- The place search reads a hotel network's sign-in page as a network problem instead of failing, and leaves out results without a name or position.
- Street maps load on the first draw. Before, they could stay empty until something drew the map again.
- The currency picker shows the yen sign correctly, and a booking's reference is labelled "Booking number".

## 0.9.0 “Swift Salamander” – 2026-11-10

### Highlights

- A plan can be exported in another time zone, for example to read it at home, and can show each time in more time zones below it. The plan's second time zone is added on its own until you choose otherwise.
- Weather and exchange rates say when they were fetched, and a click or tap there fetches them again now, on the computer, phone and tablet.

### Changes

- On phones and tablets, a long press opens its menu when you lift your finger. Once you have held long enough, the item fades a little and the device vibrates briefly. A hold that turns into scrolling no longer opens a menu.
- The timetable on tablets keeps its place when it is drawn again for the same days, for example after a tap on a plan.
- The export dialog names its two time zone searches differently for screen readers.

### Fixes

- On a Mac, the window's header no longer fails to build with "Requested value '⌘Z' was not found".
- German dates have a full stop after the day, such as "Freitag, 25. Dezember", in the app on all devices and in exports.
- On phones and tablets, an amount such as 1.234 is read as one thousand two hundred thirty-four, and 1.234,56 is accepted. Before, 1.234 was saved as 1.23.
- Time and date fields in German no longer show the English words "hour" and "minute".
- Tablets that can make calls are called tablets, not phones.
- Tapping on a page that is scrolled down no longer makes its top flash up for a moment.

## 0.8.0 “Zealous Zander” – 2026-11-05

### Highlights

- Jourfold for Android phones and tablets. Today shows what comes next, the plan reads in local time, and the map, tickets, packing lists and costs work offline. Everything can be edited on the road and synced with the trip on GitHub. Every release on GitHub has the APK for it.
- Show ticket opens a plan's boarding pass, and Wallet passes appear with their barcode. A place can keep its name and address in the local script and show them large as a taxi card.
- Costs has a currency converter with the European Central Bank's reference rates. They are fetched only after you agree, at most once a day.

### Changes

- Journeys show how long they take, across time zones, and the layover to the next journey or from the one before.
- Linking a plan to a booking, or a booking to its plans, changes both sides, so a plan moved to another booking leaves the old one. Groups take in an existing plan, and a step can leave its group.
- Expenses list their files, such as a receipt photographed on the phone, and take new ones like bookings do.
- Details opened from other details get a back arrow, also on Alt+Left.
- The packing line suggests people and groups while you type @ or #, and 2x sets the quantity. A name nobody in the trip has is no longer kept in the title: Jourfold says so and offers to add the person.
- Packing items move to another list from their menu or details.
- Plan and packing list exports can include comments.
- Finding trips on GitHub shows how far the search is and can be cancelled. When Jourfold's App may not use any repository yet, Jourfold leads to the page where you choose them.
- Connected AI assistants can see layovers, put plans into groups, link bookings, read and add costs, convert currencies with the rates Jourfold downloaded, and rename, delete and change packing lists.

### Fixes

- A click on a check box, a person's chip, "Who brings it?" or Copy inside a row no longer also opens the row's details.
- Zooming far into the map on a day with a long flight no longer makes Jourfold stop responding.

## 0.7.1 “Knockabout Kudu” – 2026-10-26

### Highlights

- Connecting to GitHub no longer needs a GitHub App of your own. Jourfold signs in with its own App, and you choose on GitHub which repositories it may use.

### Changes

- If you entered the client ID of your own GitHub App before, Settings → Sharing shows it and lets you switch to Jourfold's App. Advanced mode offers your own App again.

## 0.7.0 “Keen Kudu” – 2026-10-26

### Highlights

- Jourfold runs on macOS. The same one-line install command as on Linux installs it on Apple silicon and Intel Macs with macOS 14 or newer.
- Screen readers on Linux now read Jourfold, including which status, category, view or filter is chosen and who the people in a trip are.

### Changes

- On a Mac, shortcuts use Cmd, GitHub sign-ins are kept in the keychain, reminders appear in the notification centre and trip links open in Jourfold.
- Jourfold draws only when something changes, which saves power while it is open.
- Windows 11 shows the Jourfold window with rounded corners.
- When a coordinate needs both values, screen readers read that with the field.

## 0.6.0 “Oceanic Osprey” – 2026-10-24

### Highlights

- Packing lists with groups, who brings what and who has already packed it. Everyone ticks off their own items, and things nobody has taken yet stand out.
- Jourfold knows who you are in each trip, from your GitHub login or e-mail address. When that is not clear, it asks once.
- Export the trip plan the way you need it, for everyone or one person, for the whole trip or chosen days, detailed or compact, as PDF, web page, calendar file or text.
- The new button next to Share gets you the trip PDF, the packing lists, a calendar file or the plan as text in one step, and invites people.

### Changes

- The packing page holds several lists. Quick add understands @name, @all and #group, and the filters show all items, your own or those nobody brings yet.
- Packing lists can be exported as PDF, text or CSV, for everyone in one file or as one file per person in a folder.
- The trip PDF shows each day on a time line in the app's colours, with check-in, check-out and the nights of each stay. Every page names the time zone, and times in another zone show their own.
- The trip PDF can include a map and attach PDFs, images and e-mails as appendix pages. The map uses street map tiles only when online maps are on.
- The export dialog remembers your last choices for each trip, and Copy as text puts the plan on the clipboard.
- Connected AI assistants can read and edit packing lists and get the plan or the packing lists as text.
- In the sample trip you are the organizer, and it now has a packing list.

### Fixes

- Choosing "This is me" no longer changes who you are in your other trips.

## 0.5.0 “Polar Pelican” – 2026-10-20

### Highlights

- Weather for every day and activity, from forecasts within two weeks to climate averages further ahead. It is off until you turn it on, and only rounded coordinates are sent.
- Warnings about rain, wind, heat or frost before outdoor activities. Jourfold estimates what happens outdoors, and you can set it for each activity.
- E-mails open in their original layout. Images from the internet load only when you choose, and links show where they go before they open.
- Problems can be reported by e-mail. You review the report first and send it from your own mail program.

### Changes

- Weather comes from Open-Meteo by default. MET Norway, the US National Weather Service and Visual Crossing with your own key can be chosen under Settings, Weather.
- Connected AI assistants can read the weather Jourfold has already downloaded for the trip. They are told it is not live and cannot make Jourfold download anything.
- Every error message has a Report button that prepares the report with its details.
- When a stay is selected, its check-in and check-out times are drawn above the activities.
- About Jourfold links to the website jourfold.dev.

### Fixes

- Stays in the band of nights can be selected with a click again.
- A tooltip no longer takes the click meant for the item below it.
- A stay whose time differs from check-in and check-out shows the warning once, with its fix and in your language.
- A trip folder that was moved or deleted while open is explained instead of making Jourfold fail.

## 0.4.0 “Zippy Zebra” – 2026-10-19

### Highlights

- Stays are shown as nights in a band below the timetable. Nights entered one by one can be merged into one stay, and nights without a place to stay stand out.
- Confirmation e-mails and PDFs open inside Jourfold, next to the plan. Drop them onto a booking or a stay.
- The bell reminds you of free cancellation deadlines, tasks due, late arrivals and unconfirmed bookings, also as a system notification.
- Variants can be taken over into the main plan, deleted with a 30-day recycle bin, and cleaned up when someone deleted them online.
- Deleting an item that others link to asks first and offers to delete places and files nothing else uses.
- Sync problems are explained in plain words, with safe fixes and technical details to copy.

### Changes

- Accommodation details hold check-in hours, how to get in, free cancellation, breakfast, parking and arrival notes. Places have phone, e-mail and website.
- The variants page shows each variant once, with where it lives and how it differs from its online copy and from the main plan.
- Lists, tiles, people, files and map pins share one right-click menu.
- Map labels can be shown when there is room, always, or never.
- The version appears next to the logo, and About Jourfold has a version history.
- Quick Add no longer stores check-in and check-out hours you did not enter.

### Fixes

- Lists, the inspector and the map no longer jump when they are rebuilt or when something is selected.
- Sync follows the branch a variant tracks instead of uploading a copy under another name.
- Files in the trip folder that are not part of the trip no longer count as unsaved changes.
- Deleting a place keeps the stays that used it.

## 0.3.1 “Zappy Zebra” – 2026-10-04

### Fixes

- Lists keep their scroll position when an item is selected or the trip changes outside Jourfold.
- Long notices and rows of buttons in the inspector wrap instead of being cut off.

## 0.3.0 “Zesty Zebra” – 2026-10-04

### Highlights

- AI assistants that support MCP can read and change a trip through Jourfold.

### Changes

- The timetable has one-day arrows and stays in place when an item gets a new time.
- The install scripts report a new install, an update or a reinstall with the versions.

## 0.2.0 “Uplifted Umbrellabird” – 2026-10-04

### Highlights

- A second timezone can be shown in the timetable and the list.

### Changes

- Transport times are entered in local time at each end, and timezones can be found by country.
- Place coordinates are saved as a pair, and coordinates pasted from a map fill both fields.
- Map tiles and search follow the OpenStreetMap usage policies.

## 0.1.1 “Noble Nightingale” – 2026-10-04

### Changes

- Each feature release has a code name.
- New trips can be published to GitHub from the wizard, and the connected GitHub account is shown and can be disconnected.

### Fixes

- Quick Add no longer crashes when switching the type or toggling options.

## 0.1.0 “Nomadic Nightingale” – 2026-10-04

### Highlights

- The first release of Jourfold, a desktop travel planner on the open TravelRepo format.

### Changes

- Timetable, list and map, bookings, costs, tasks, files, versions, variants and sharing through Git.
- Install packages for Linux and Windows on x64 and ARM64, with one-line install scripts.
