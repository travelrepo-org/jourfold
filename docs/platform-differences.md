# What differs between iOS, Android and the desktop

A list to close the gaps from later, not a promise to. It compares what people can do, as of Jourfold 0.16.1 and
TravelRepo 0.14.0:

- **iOS**: the native app in `src/Jourfold.iOS.Swift` ("Jourfold Preview"). The .NET app for iOS in `src/Jourfold.iOS` has
  what Android has, except where [Mobile app](mobile.md) says otherwise.
- **Android**: `src/Jourfold.Mobile` with `src/Jourfold.Android`.
- **Desktop**: `src/Jourfold.Desktop` on Windows, Linux and macOS.

All three read and write the same trips through TravelRepo. What it decides is the same everywhere: validation, days and
nights, costs, splits and who owes whom, search, versions, merging, packing lists, weather. Taken from the code and these
documents; **not checked by using** the three side by side. "Yes" means the function is there, not that it looks the same.

## Trips, sharing and versions

| | iOS | Android | Desktop |
| --- | --- | --- | --- |
| New trip | title, dates, zone, currency, traveller | no: sample trip or from GitHub | title, dates, zone, travellers, folder, sharing |
| Open a trip folder | yes, copies it into the app | no | yes, works in it |
| Duplicate a trip | yes | no | no |
| GitHub: sign in, get, share, invite | yes | yes | yes |
| Other Git servers | no | no | yes |
| Sync while away | on opening, after two quiet minutes, on leaving, pull to sync | same | in the background |
| Choose between conflicting changes | yes, with keep both | yes, with keep both | yes, side by side |
| Compare with a version and go back to it | yes | no, lists versions | yes |
| Variants | shows the name only | no | create, compare, merge, delete |
| Repair after an interrupted Git step | stale lock, unfinished step, unfinished saving | no | yes, with technical details |
| Undo | no, versions only | no, versions only | yes |
| Recently removed trips | moved to the app's recovery folder | deleted | never deleted |

## Planning

| | iOS | Android | Desktop |
| --- | --- | --- | --- |
| Day list, timetable, map | yes | yes | yes |
| Timetable: several days, stays as bars, check-in hours | several days and stays; no band for check-in hours | yes | yes |
| Swipe sideways between days | no, rows have actions there; buttons and pulling | yes | no, scrolls |
| Drag a plan to another time | no | no | yes |
| Drag an idea to a day | yes | no, menu | yes |
| Times: exact, approximate, part of the day, all day, none | yes | yes | yes, and a time window |
| Second time zone per trip | yes | yes | yes |
| Columns by person | no | no | yes |
| Groups and steps | see, add a step, take in and out | and make a group in Quick Add | yes |
| Stay: hours, access, breakfast, parking | edit nights, hours and rooms; the rest is shown | most of it | yes |
| Layover between journeys | in notifications | yes | yes |
| Set outdoors or indoors for the weather | no | no | yes |
| Weather service to choose | Open-Meteo only | Open-Meteo only | four services |
| Comments | yes | no | yes |
| Inbox | ideas | ideas | yes, with notes and loose files |
| Reminder of one's own on a plan | yes | no | no |

## Maps and places

| | iOS | Android | Desktop |
| --- | --- | --- | --- |
| Map | Apple Maps after consent; without it pins and arcs, offline | OpenStreetMap after consent; grid offline | OpenStreetMap after consent; grid offline |
| Address search | Apple Maps | Nominatim | Nominatim, other servers possible |
| Position from a map link, short links on request | yes | yes | yes |
| Navigate | Apple Maps, Google Maps, Waze | every installed app | opens the web |
| Where I am | yes | yes | no |
| Taxi card, at full brightness | yes | yes | yes, window |

## Costs

| | iOS | Android | Desktop |
| --- | --- | --- | --- |
| Expenses, splits, budgets, paying back, cash, rates | yes | yes | yes |
| Fees calculator | yes | yes | yes |
| Values carried over when the split changes | yes | yes | not checked |
| Receipt | camera, photo, file, scan | camera, file | file |
| Export | text | no | PDF, CSV, text |
| Convert budgets after changing the currency | yes | yes | yes |
| Remove a currency from the recent ones | no, the latest four | yes | yes |

## Files and tickets

| | iOS | Android | Desktop |
| --- | --- | --- | --- |
| Show PDF, pictures, e-mails, Wallet passes with barcode | yes | PDF opens in another app | yes |
| E-mail in its original layout, loading images | cleaned text layout, no remote images | yes | yes |
| Scan a paper document | yes, as PDF | no | no |
| Add a pass to the phone's wallet | Apple Wallet | opens in another app | no |
| Files from other apps | share sheet, Open in Jourfold | share sheet | drag and drop |
| Share several files with a trip not open | yes | yes | – |
| Tickets on the app's icon and in notifications | yes | yes | no |

## On the way

| | iOS | Android | Desktop |
| --- | --- | --- | --- |
| Notifications when the app is closed | yes, planned ahead by iOS | yes, with exact alarms | no |
| When to leave | one lead time for all | from the walking time | – |
| Travel day | Live Activity and Dynamic Island, starts when the app is opened | Live Update, updates by itself | – |
| Kinds of notification to switch | no | yes, channels | one switch |
| Time-sensitive level | asked for; needs an entitlement the builds lack | yes | – |
| Home Screen widgets | next plans, today, costs and budgets | no | – |
| Trips in the system calendar | yes; entries changed there stay changed until the plan changes | yes, read only | exports a file |
| Today | follows the place of the trip | follows the phone's clock | – |

## Search, settings and the rest

| | iOS | Android | Desktop |
| --- | --- | --- | --- |
| Search | TravelRepo's, from Plan | and from Today and More, best match, by day or kind | and commands |
| Keyboard | ⌘N, ⌘1–4, ⌘R | Ctrl+N, K, F, 1–4, arrows, Escape | full |
| Language, light and dark, larger text | system's | app and system | app |
| High contrast, density | system's contrast | system's | own settings |
| Update hint | GitHub builds | GitHub builds | GitHub builds |
| Problem report | a text to look through, leave parts out of and share | e-mail with the log | text for the clipboard |
| AI assistant (MCP), plugins | no | no | yes |
| Code name of the version | no | yes | yes |
| Oldest system | iOS 26 | Android 10 | see [Installing](install.md) |

## To follow up

In iOS, the order of use on a trip:

1. When to leave from the walking time, as `TravelNotices` in `Jourfold.Mobile` does; today one lead time holds for all
   but flights. Better still, move these rules into TravelRepo, so that no app has its own.
2. Search from Today and More, with the best match and grouping by day.
3. Write calendar entries again that were changed in the calendar app.
4. Band for check-in hours in the timetable; making a group in Quick Add; outdoors or indoors.
5. E-mails in their original layout, with loading images on request.
6. Costs as PDF and CSV.
7. `auto-version` instead of `assistant-version` for automatic versions, and the suggested e-mail addresses: both need
   TravelRepo's Swift interface.

In Android: a new trip without GitHub, comparing with a version and going back, repairing after an interrupted Git step,
exporting costs, showing PDFs, comments, widgets, scanning a document, reminders of one's own.

On the desktop: duplicating a trip, scheduled notifications while closed, receipts from a camera.
