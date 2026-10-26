# Changelog

All notable changes to Jourfold. Generated from `docs/releases` by `python eng/changelog.py`; do not edit by hand.

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

## 0.3.1 “Zesty Zebra” – 2026-10-04

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

## 0.1.1 “Nomadic Nightingale” – 2026-10-04

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
