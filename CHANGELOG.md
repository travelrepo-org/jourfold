# Changelog

All notable changes to Jourfold. Generated from `docs/releases` by `python eng/changelog.py`; do not edit by hand.

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
