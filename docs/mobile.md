# Mobile app

The phone and tablet app runs on Android 10 and newer and on iOS and iPadOS 15 and newer, from the same code. Each
release from 0.8.0 on has a signed APK attached, and from 0.11.0 on an unsigned IPA for iPhone and iPad to sign with an
Apple ID; [Installing Jourfold](install.md) says how to install them. See [iPhone and iPad](#iphone-and-ipad)
for building the iOS app.

## Projects

- `src/Jourfold.UI`: the look shared with the desktop: colour and type tokens, icons, the brand mark, covers, the
  `Ui` factory and the UI strings.
- `src/Jourfold.Mobile`: the phone and tablet interface. `MobileSession` holds the trips on the device and the open
  trip's day plan (TravelRepo's `TripAgenda`); `AppShell` lays out the bottom bar on phones (on iPhone the system's
  tab bar) and a rail from 600 dp; the views are in `Views/`. It is plain .NET and Avalonia, with no Android or iOS code.
- `src/Jourfold.Android`: the Android app. It passes the app's private folder, the built-in Git (libgit2), the clock
  and the phone's time zone to `Jourfold.Mobile` through `MobileHost`, and shows the system splash screen.
- `src/Jourfold.iOS`: the iPhone and iPad app, doing the same with the iOS services (Keychain, EventKit, user
  notifications, Core Location). It builds only on a Mac.
- `src/Jourfold.iOS.Swift`: the native iPhone and iPad app in Swift that is meant to replace `src/Jourfold.iOS`; its
  [README](../src/Jourfold.iOS.Swift/README.md) says how to build it, and [What differs between iOS, Android and the desktop](platform-differences.md)
  compares them. The rest of this page is about the
  .NET app.

## How times read

Today follows the phone's clock and adds the item's local time when its zone differs. Plan, details and bookings show
the item's local time and add the phone's time when the phone is in another zone. Each day says which clock its times
follow ("Times as on your phone (Berlin)", "Local time Tokyo"), and a second time names its place instead of a zone
abbreviation ("Berlin: 01:30 (Wed)"). A journey across zones always names both places. `TimeText` implements these
rules and `MobileTests` checks them.

A tap on that line chooses a second time zone for the trip, from the phone's zone and the zones of the trip and its
items, or under **Another time zone…** any zone named after a place, which a field above the list searches. Its times
then stand quieter under every time in Today and Plan ("01:30", "18:00 +1" when it is another day), and not where both
zones show the same time. It replaces the other zone's time inside the cards ("Berlin: 02:00"), which only shows while
no second time is chosen. The timetable shows it under each hour of its time column, read on the chosen day in that
day's zone; the corner above the hours names both zones by their short names ("JST", "CEST") and chooses the second
time as well. The choice stays on this phone, per trip. Dark mode uses the design
spec's navy tokens, which `Jourfold.UI` shares with the desktop.

An item that runs past midnight, such as a night flight, appears again on the days after it starts: on the day it
ends as its arrival or end, on the days between as running all day. TravelRepo's `TripAgenda` marks these lines as
`AgendaRole.Continued`; Today reads spans itself and shows such an item once.

## Plan, details and long press

The date strip stays at the top while the day scrolls. A sideways swipe moves the day with the finger and slides the
next one in; the strip scrolls on its own. The next day is drawn as soon as the finger lets go, so a second swipe
right after the first moves on another day. The one-day time grid has no date strip: each day's head names it, the
chosen day changes as the glide begins, a finger that touches the days again takes them from where they are, and
nothing is drawn again when they come to rest (only near the end of a long trip's window), so one swipe follows
another at once; the pull for the next day reads the chosen day when the pull starts (`DayPan`). A drag past a short slip that is
still moving when the finger lets go moves at least one day, however unhurried; only one that stopped or turned back
settles on the nearest day. At the end of a day, pulling on upwards lifts the page a little and fades in
"Next day" with an arrow; once it turns to the accent colour, letting go opens the next day. Pulling down at the
start of a day leads to the day before in the same way. The pull only counts when the finger is put down at the end or
the start, so scrolling fast through a day stops there. The same works on the one-day time grid. A tap on an item opens its details at the smallest step (time, place,
booking reference, Navigate). Below the top step the content does not scroll, so a drag anywhere on the sheet moves
it; at the top step the content scrolls, and dragging down at its top lowers the sheet again. Journeys show their
travel time. A long press on a line of the details copies it, such as the flight number, the seats or the booking
reference. On a place, where a tap opens it to be changed, a long press copies its address (otherwise its position,
otherwise its name); the same works on the Places page. Android 13 and later confirm a copy themselves; on older
versions a short notice says what was copied. Only the item happening now or next on the phone's today shows extra context in the plan. A
stay's check-in says how many nights it has, and the night line closes every day, the day of the check-in too.

A long press on an item opens a menu with details, edit, status, Navigate, copy the booking reference, duplicate,
move to another day and delete. A long press counts when the finger is lifted after half a second without having
moved: the item fades a little and the device gives a short vibration once that time is reached, and letting go then
opens the menu or copies the line. A hold that turns into a scroll or a swipe opens nothing. A swipe to the next day that starts on an item is only a swipe. A long press on a trip in the trip list removes it from the phone after a question
that says whether it is still on GitHub. Everything in these menus can also be reached without the gesture.

## Places and the map

Quick Add and the editor choose an item's place by name, a journey's start and end, and create a place when the name
is new; the editor shows the current places and changes only those that were changed, so an inherited place stays
inherited. In the details, a place opens to change its name, address and position; without a place, a button adds
one that goes straight into the item. **More → Places** lists every place with its address or position. The rules
for which field holds a place are in `PlaceEdits` (Jourfold.Application).

Coordinates copied from a map ("-37.0093, 174.7841") or a map link from Google Maps, Apple Maps or OpenStreetMap
fill latitude and longitude when they are pasted into any field of a place, the name and address included. Android's
keyboard inserts its clipboard suggestion without a paste event, so the fields read text that arrives several
characters at once; typed characters do not count. Short links such as maps.app.goo.gl need the network and are
not read. The address search uses the same Nominatim server and rules as the desktop and works only after the user
turned on online maps; it explains what is sent before that. "Where I am" asks Android for the position only when
tapped and requests the location permission at that moment; the position only fills the fields.

Navigate hands the place's position to the maps app; without one it searches for the address, then the name.
Android offers the user's maps and navigation apps for the `geo:` link. iOS has no such link, so on iPhone and iPad
Navigate opens Apple Maps, or, when Google Maps or Waze is installed, asks which of them to open
(`Navigation.AppleApps`, checked with `canOpenURL` and the schemes in `LSApplicationQueriesSchemes`).

The Map tab draws the selected day like the desktop's map (`MapSurface` in Jourfold.UI): numbered pins in the order
of the plan, a dashed line through them and journeys as arcs. It pans with a finger and zooms with two fingers, a
double tap or the buttons. **My location** among the buttons asks for the position only when tapped, like "Where I
am", and shows it as a dot in the middle of the map; it is kept only while the map is open and never saved or sent.
Without online maps it shows a plain grid and works offline; street tiles from
OpenStreetMap come after the user turned them on, with the credit next to the buttons, the same tile cache rules as
the desktop and the cache in the app's private folder. A docked sheet lists the day's stops: dragged down it leaves
only its handle, in the middle it shows the list, and a tapped pin or stop comes first with Navigate and Details. On
tablets the list is a column on the right. The map stays while its trip is open, so it keeps its position and tiles.

## Large screens, foldables and DeX

The layout follows the window, not the device, so one APK serves phones, foldables, tablets, split screen and
Samsung DeX windows, and changes as soon as a window changes size; attaching a keyboard no longer restarts the app.

- Below 600 dp: bottom bar, details as a sheet.
- From 600 dp: a rail on the left; the trip list shows two or three trips in a row.
- From 840 dp: details as a fixed column; Today puts what comes next and tonight's stay beside the day; Plan
  switches between the day list and a timetable when the window is also at least 600 dp high (a phone held sideways
  keeps the list); pages under More share one column of readable width, so the way back stays in the same place.
- From 1200 dp: a sidebar as on the desktop, with the trip, the places with their names, the trip's pages (bookings,
  tasks, costs, places, files, travellers) and where the trip is kept, which folds into the rail with the button in the bottom left corner, the same place
  in both; Plan starts as
  the timetable, with as many days as fit (three, five or seven, so the chosen day is in the middle), each in its own local time and a line at the current
  time. It reads like the desktop's timetable: all-day plans in a strip above the grid, stays as bars over their
  nights in a band below it, check-in and check-out as thin markers instead of blocks (a dashed line at a fixed
  hour, a soft band with "Check-in 15:00–19:00" for the check-in hours when the arrival has no fixed hour, "Check-out
  by 11:00" for the latest departure), and plans as tinted blocks with icon, time and title, dashed for ideas and
  parts of the day. The markers lie behind the plans. All the trip's days lie on one strip (a trip of more than 61
  days shows 61 around the chosen day) that glides sideways with the finger, with momentum, and comes to rest with a
  day in the middle; that day becomes the chosen one. The arrows glide by the days shown; the time column stays.
- Where the timetable of several days does not fit, the switch offers the same grid for one day, under the date
  strip: the day in the middle with a part of the day before and after at the edges, on the same gliding strip, so
  a look at the next day does not change the day yet. The date strip follows the day that comes to rest in the middle. The grids always run from midnight to midnight, so a night
  flight or an early start is reachable; they open at the first plan of the days in view (a journey from the day before
  counts from midnight), on today at the hour before now, and otherwise at eight. The list stays the first choice, and the choice is kept apart from the larger screens', so a foldable
  can show the list closed and the timetable open.

With a mouse, rows, places and cards show where the pointer is (only then, so a tapped row does not stay marked),
and a right click opens the same menu as a long press. A hardware keyboard has Ctrl+N for Quick Add, Ctrl+K or
Ctrl+F for the search, Ctrl+1 to Ctrl+4 for the four places, the arrow keys for the previous and next day in Plan and
Map, and Escape to go back.

Back is Android's back button or gesture. iOS has none, so on iPhone and iPad a swipe to the right that starts at
the left edge of the screen (within 24 points) does the same (`IosBackGesture`, `AppShell.GoBack`): a menu or sheet
closes, details go back, a page goes back to where it was opened from, then to Today and to the trip list. A swipe
that starts further in still moves the days; once the edge swipe is recognised, the touch is taken from the page, so
the day stays. On the map with nothing over it, a drag from the edge pans the map instead
(`AppShell.EdgeSwipeGoesBack`); its sheets and menus still close with the swipe. The back buttons in the app stay as
they are. It is a pan that starts at the edge rather than iOS's
edge-pan recogniser, which the simulator's touches do not trigger.

## The bar and its third button

The bottom bar, and the rail from 600 dp, has Today, Plan, the plus and More in fixed places. The button between the
plus and More opens the **Costs** unless something else is chosen under **Settings → Third button of the bar**: the
map, the bookings, the tasks or the packing lists. The choice stays on the device (`bar.third` in the settings file).
A page opened with that button is a place like Today: it has no way back of its own, back leads to Today, and its
button is marked wherever the page was opened from. While the map is not the third button, **More** lists it, and
back from it returns to More. The sidebar on wide screens keeps Today, Plan, Map and More, since it lists the trip's
pages below them.

On iPhone the bottom bar is iOS's own tab bar (`IosTabBar` behind `ISystemBar`, set as `MobileHost.Bar`): from iOS 26
it floats as Liquid Glass over the page, which scrolls on beneath it and ends with room for it, and it shrinks to the
marked place while the page is scrolled down and opens again at the top or with a tap. The plus is the middle tab,
captioned **Add**; it opens Quick Add and is never marked. The system draws the material, the highlight of the marked
place, VoiceOver, Larger Text and the settings Reduce Transparency, Increase Contrast and Reduce Motion. The shell
still decides the places and which one is marked; the tab bar only reports taps, and it steps aside for the keyboard,
sheets, menus and questions as the drawn bar does. Older iOS versions get the classic tab bar. Android, the desktop
and the rail from 600 dp, also on the iPad, keep the bar Jourfold draws itself.

## Search

The magnifier in the trip's header, on Today, Plan and More, opens the search with the field ready to type in.
TravelRepo's `TripSearch` does the finding, the same as on the computer and in the MCP `search` tool; the app only
shows the results (`SearchView`, and `SearchTexts` in Jourfold.UI, which the computer shares).

- It finds plans, bookings, costs, places, tasks, packing lists and their items, notes by title and files by name, by
  title, place, address, journey ends, provider, connection and booking number, people, amount, and category in the
  app's language ("Essen" finds food). Case and accents do not matter, a beginning of a word is enough, and one typo
  is forgiven in words of five letters or more. Every word must be found.
- Note texts are searched only with **Also search notes**, which the device remembers.
- The **best match** stands on top, larger, with its day and time. Below, results are grouped **by day**, in trip
  order (during the trip today first, then the coming days, then the past ones), and what has no day by kind; or
  **by kind** throughout. Chips limit the results to one kind. Each result shows the kind's icon, the title with the
  words found marked (bold and underlined, in the accent colour), where it was found when the title did not match
  ("Booking no.: GR-1187"), and the day or time.
- Typing only draws the results again, after a pause of 150 ms, so the field keeps the focus and the keyboard.
- A tap opens the result's details over the search (a place's form, a packing item's page, a file's viewer). Closing
  them returns to the same words, results and position. **Show in the plan** (or in the bookings, the costs, the
  tasks, on the map) in the details, or holding a result, jumps there: a plan to its day, framed in the accent
  colour and scrolled into view; a booking or payment to its page with the row unfolded; a place to the map on the
  day of a plan there. Back returns to the search.

Back, by gesture, Escape or the button at the top of a page, returns to where something was opened from. A page
opened from Today, such as the packing lists, goes back to Today; one opened from More, the rail or the sidebar goes
back to More. Details opened from other details, such as a stay's booking, a booking's plans or a group's steps, get
an arrow next to the close button that leads back to them; the close button always closes. Cancelling the editor
returns to the details it was opened from, and deleting a booking opened from its stay returns to the stay.
Layouts that follow a foldable's hinge are not built yet.

## Sharing to Jourfold

Other apps share to Jourfold through the system share sheet. Files such as tickets, PDFs, e-mails and images are
read right away and go to an item of the plan or a booking, or only into the trip's files (**More → Files**); files
above 25 MB are left to the desktop. A place from a maps app opens as a new place or as the place of an item, with
its name, address and position taken from the shared text. A short link such as maps.app.goo.gl carries no
position; on request the app asks the maps service where it leads, and sends nothing else. Other text becomes a
note or goes to the end of an item's notes. Shared before a trip is open, the trip list asks which trip it belongs
to. In the details, **Attach a file** picks files on the device.

The list of items and bookings to attach to has a search field. It finds an item by its title, time, day, notes and
the name or address of its places, and a booking by its title, reference and provider; several words must all match.
During the trip the list starts at today, with **Whole trip** to show the days before; a search always looks through
the whole trip.

On iPhone and iPad, Jourfold is in the share sheet of other apps through a share extension (`src/Jourfold.iOS.Share`,
a .NET app extension with a bundle ID of its own, `<ApplicationId>.Share`). It takes what Android's share intent
takes: files (tickets, PDFs, e-mails, images, up to ten), a link such as a place from a maps app, and text. An
extension cannot open its app, so it keeps what was shared in an inbox in the app group the two share
(`group.<ApplicationId>`, `SharedInbox`): each share a folder with its files as they came and the text, marked as
complete once written. It shows a card that says so and closes with **Done**; it reads, sends and changes nothing
else. The next time Jourfold comes on screen it takes the inbox, oldest first, and goes on as on Android
(`IosSharedInbox`): with a trip open the share sheet opens, otherwise the trip list asks for the trip; text only
goes with a share without files. Tickets, PDFs, e-mails, images and text files also still come in through **Open in
Jourfold** (`CFBundleDocumentTypes`), where iOS hands over a copy.

The extension needs a provisioning profile with the app group on a device. A signed device build includes it only
with `-p:ShareExtensionProvision=<profile>`, and without one builds the app as before, without the extension and
without the app group. Builds for the simulator always include it. Unsigned builds, such as the workflow's IPA for
sideloading, include it only with `-p:IncludeShareExtension=true`: whether a free Apple ID (Personal Team) and
AltStore, SideStore and Sideloadly sign the extension and its app group is not known yet, and the IPA must stay
installable. Until someone has tried it, the IPA from GitHub has no share extension; files still come in through
**Open in Jourfold** there.

## Files

Files of an item open on their own page. E-mails (`.eml`) show sender, recipients, date and attachments, read by
TravelRepo's `Mail` without a network, then the message in its original layout, as on the desktop (`MailPaper` in
`Jourfold.UI`, cleaned by TravelRepo's `MailHtml`). An e-mail laid out wider than the phone is shrunk to fit instead
of being cut off; two fingers or a double tap zoom it up to four times, and one finger moves a zoomed e-mail
sideways while up and down still scrolls the page. **Text** shows the plain text instead; the choice is kept on the phone. Images from the internet
load only after **Load images**, for that e-mail until the app closes, and a tapped link asks before it opens in the
browser. Images show as they are. PDFs and other files go to an app on the phone that can show them: on Android through a `FileProvider` that only
exposes a copy in the app's `shared` folder, replaced by the next file.

## Versions on the phone

Every change is saved to the trip folder right away. Once the trip has been quiet for two minutes, or when the app
goes to the background or the trip is closed, the open changes become a version on their own. Changes left over
when Android stopped the app become a version soon after the trip opens again. Sync and versions also
lets the user create a version with their own description and shows the recent ones. The phone app does not use
Git words such as commit or branch.

Versions are saved under a name and email that everyone who can open the trip sees (`CommitIdentity` in
`Jourfold.Application`). The email is the first of: the one the trip has for the person the user is ("This is me"),
the one under Settings, the one the GitHub account shows publicly, and GitHub's private address for commits
(`ID+login@users.noreply.github.com`). Without any of them, versions wait on the phone, and Sync and versions says
how to give a name and email instead of reporting the trip as synced.

The first time a trip opens on the phone and the user is not recognised in it, a sheet asks **Who are you in this
trip?** (`TripWelcome` in `Jourfold.Application`, `TripWelcomeView`): the trip's people, adding oneself, or not being
part of the trip; **Not now** closes it and the card on Today keeps the question. When the trip has no email for the
chosen person, or for a new one, the sheet asks for it, filled in from Settings or the GitHub account, with GitHub's
private address as an option, and saves it with the GitHub login on that person. Without an email under Settings, a
check box (on by default) keeps it there for other trips. The sample trip never asks. Settings open from the trip list (the gear at
the top) and under More; they are kept in `settings.json` in the app's private folder, never in a trip.

The APK and IPA from GitHub releases ask GitHub once a day at start for the latest release, with only the app's name
and version, and show a card above the trips when there is a newer one: "View release" opens its page, "Skip this
version" stays quiet until the next one, and "Stop checking for updates" turns it off and says where to turn it back
on (Settings → Updates). **Check now** there asks at once, also with the daily look turned off, and says under the
button what came of it: a newer release with "View release", the latest version, or that GitHub could not be reached
(`UpdateCheck.CheckNowAsync`); a version skipped before is offered again. Nothing is downloaded or installed. Builds from F-Droid, Google Play or the App Store never
ask and show no such setting.

Settings → About Jourfold opens a page as on the desktop: the installed version (a preview's too) with its code name,
Jourfold's and TravelRepo's licenses to read in the app, links to the website and the source code, and the open-source
notices. The About row under Settings shows the version and code name too ("0.13.0 · Scenic Seal"); `CodeNames` lives
in `Jourfold.UI` for both apps.

Today counts down to the trip ("The trip starts tomorrow. Time to pack!" the day before), has a word on the first and
the last day, and after the trip shows it in numbers: days, plans, places, what it cost and per person and day, with
**Pay back…** while something is still open between the travellers. The Costs page shows what the trip comes to per
person and day, paid and still to pay together, once everything can be counted in the trip currency.

Removing a trip from the phone deletes its folder. A trip that is only on the phone is gone after that; the question
before says so.

## GitHub on the phone

Signing in uses GitHub's device flow, like the desktop: the app shows a code, opens GitHub, and waits until the user
has entered it. While the user is in the browser, Android may freeze or stop the app: requests lost that way do not
end the sign-in, and the waiting code is kept in secure storage so the next start goes on waiting. The token is kept by the platform's secure storage (on Android encrypted with a key in the Android
Keystore) and handed to the built-in Git for github.com only. Signed builds from CI sign in with the project's GitHub App; other
builds can name their own with `-p:JourfoldGitHubClientId=<client ID>`, and without one the user enters the client ID
once, as on the desktop (see `providers.md`).

A signed-in phone can put a trip on GitHub as a private repository and get a trip someone shared from the trip
list. Shared trips sync when they open and after each automatic version. When both sides changed different things,
the changes are combined on the phone. When both changed the same thing, the phone keeps its own state and asks the
user to open the trip on a computer, where both can be compared. Without a network the app says so quietly and
tries again after the next change.

## On the road without a computer

Phone and tablet edit everything needed while travelling: a journey's departure and arrival, each in its own zone
("Local time at departure" offers the zone it has, the starting place's, the trip's and the phone's), carrier, number,
terminals, gate and seat; a stay's first night, nights, check-in and check-out hours, rooms and access; booking
numbers (a booking is added when there is none); tasks with a due day. When a sync finds that both sides changed the
same thing, the sync waits and Sync → "Choose what applies" lists each change with this device's and GitHub's
version, and "keep both" for items; the choices are merged and sent (`MergeAnswers`, shared with the desktop).

Bookings are added under More → Bookings or in Quick Add and edited with their provider, number, status, price,
free cancellation, travellers and the plans they are for; linking a plan updates both sides (`TripCommands.LinkBooking`).
An expense names who paid it, its currency (any three-letter code), whether it is still an estimate and the plan or
booking it belongs to; TravelRepo has no splitting of costs, so several payers are simply listed. A plan is for
everyone, chosen people or, as a step, the same people as its group; a stay names its guests, a task who does it.
More → Trip details changes the title and the first and last day; without dates the trip follows its plans. More →
Travellers adds a person.

A group, such as a day tour, is added in Quick Add. Its details list the steps and add a new one or take in an
existing plan; a step's details name its group and take it out again (`TripCommands.Nest`, `Unnest`). Unfolded in the
plan, a step opens its own details.

"Me" works as on the desktop (`TripMe`): chosen per trip on the device under More → Travellers, or found by GitHub
login and e-mail; choosing saves the user's e-mail and GitHub name on that person, so their other devices recognise
them. Today asks once when nobody matches.

## Notifications

Settings → Notifications → "Remind me on the trip" (off until turned on, asks for the permission; "On time to the
minute" asks Android for exact alarms). `TravelNotices` works out from the plan alone, offline: departures (three
hours before a flight, 45 minutes before other journeys), changes between journeys (TravelRepo's `Connections`:
the next journey leaves within 24 hours from the place the last one arrived, airports of one city count as one
place), arrivals with the local time and what comes next, the stay on its arrival day with its hours and access, the
check-out the evening before, when to leave for a plan (walking time from the distance to the place before, with a
detour factor; 20 minutes when it is too far to walk or unknown), free cancellation two days before it ends, tasks
on the morning they are due and the packing list the evening before the trip. Only plans that concern the user
count: those naming them, and those naming nobody.

On Android each kind has a channel. The travel day is one ongoing notification with a countdown to the departure,
the progress to the arrival, or the time left during a change; Android 16 promotes it to a Live Update. `NoticeRunner`
shows what is due once, and an alarm wakes it for the next notice; it also runs after a restart, when the time or
time zone changes (an arrival up to three hours early is shown on the zone change after landing), and after edits
and syncs. A tap opens the plan, "Show ticket" its first ticket.

**Live Updates.** On Android 16 and newer the travel day's notification asks to be a Live Update: it uses the
progress style while a journey is under way (a bar in the accent colour with the vehicle riding along, `ic_live_*`,
drawn by `eng/android-icons.py` from the app's transport icons), carries the short text for the status bar chip
(`LiveNotice.Chip`: the minutes left in the last hour, as "45 min", otherwise the departure or arrival time), has no
chronometer next to the title, and requests promotion; the manifest has `POST_PROMOTED_NOTIFICATIONS`. The system decides: it shows the
chip only while Jourfold is not on the screen, the user can turn Live Updates off per app, and a maker's Android may
show them elsewhere (such as Samsung's Now Bar) or not at all. Samsung's One UI 8 shows Live Updates only for
Samsung's own apps and apps it approved, unless "Live notifications for all apps" is on in the developer options;
on a Samsung phone the settings say so (`LiveByMakerOnly`), and the notification itself stays an ordinary one with
progress. All of this needs "Remind me on the trip" to be on;
allowing notifications in the system alone shows nothing. Under that switch the settings say what the system allows
(`ISystemNotices.Live`), offer the system page when Live Updates are turned off for Jourfold, and, in builds with
`-p:JourfoldDebugTools=true` only, have **Show an example**: a train half way there that arrives in a minute. The start is kept in the settings (`live.example`), and
`NoticeRunner` shows it in place of the travel day and wakes every ten seconds to move it on, also when Jourfold is
asleep; at the arrival the system removes it (`setTimeoutAfter`) and a real journey comes back. A line says whether
Android made it a Live Update (`LivePromoted`, read from the posted notification's flags). While a real journey is
under way the notification is drawn again every minute in the last hour and otherwise as the progress moves a step,
every one to five minutes (`LiveNotice.Redraw`). Checked on an Android 14 emulator, where it is an ongoing
notification with countdown and moving progress, and on a Galaxy S26 Ultra with One UI's developer option on, where
it is a pill in the status bar and the Now Bar. The pill shows only what the plan says: there are no live delays or gates.

**Swiped away.** Swiping the travel day's notification away hides it for its part of the day: the countdown to a
departure, the way to an arrival or a change each have a key (`LiveNotice.Key`, the journey and the time it counts
towards). Android reports the swipe through the notification's delete intent; `NoticeRunner.Dismiss` writes the key
to a file of its own (`live-dismissed.txt`, not the settings the app keeps in memory), and the runner then leaves that
part out and wakes only for the next one, when the notification comes back. Removed by its timeout or by Jourfold, it
does not count as swiped away.

iOS does not wake apps at a set time. There `NoticeRunner` hands the coming notices (up to 60, the soonest first) to
the system ahead (`ISystemNotices.Schedule`), and iOS shows them by itself, also when Jourfold is not running; they
are worked out again whenever Jourfold comes on screen, after edits and syncs, and in a background refresh when iOS
allows one. Departures are announced like the other notices, and there are no kinds to choose from.

**Live Activities.** On an iPhone with iOS 16.2 or newer the travel day is a Live Activity on the Lock Screen and in
the Dynamic Island, with the same `LiveNotice` as Android's notification: the title and text, the vehicle, the
countdown to the departure or arrival, while under way the progress between the vehicle and a pin at the destination
with the times of both ends, and "Show ticket". The compact Dynamic Island
shows `LiveNotice.Chip`, the time, and from `LiveNotice.CountdownFrom` (the last hour) the time left. iOS does not wake
Jourfold every minute, so the countdown and the progress move on by themselves (`Text(timerInterval:)`,
`ProgressView(timerInterval:)`), and the next part of the day, such as the way to the arrival after the departure,
appears when Jourfold runs again: on screen, after edits and syncs, or in a background refresh. Without that the
activity stays at zero after its time until iOS ends it, after eight hours at most. There is no push server; nothing
leaves the device. A tap opens the plan (`jourfold://notice?trip=…&item=…`), "Show ticket" its ticket. iOS reports an
activity the user swiped away the same as one it removed itself, such as after an update of the app, so unlike
Android it comes back the next time Jourfold runs while the journey is on; Live Activities can be turned off for
Jourfold in the Settings app. Jourfold works the notices out once it is on screen, also after a cold start, as iOS
starts a Live Activity only then. The settings word it as a Live Activity (`ISystemNotices.LiveTexts`), say when it
is turned off for Jourfold, open Jourfold's page in the Settings app to allow it, and, in builds with `-p:JourfoldDebugTools=true`, have **Show an example**; on
an iPad or before iOS 16.2 the part is not there.

ActivityKit is Swift only, so `src/Jourfold.iOS/LiveActivity` has a small Swift bridge with an Objective-C interface
(`JourfoldLive.framework`, called by `IosLiveActivities`) and a WidgetKit extension that draws the activity
(`JourfoldLiveActivity.appex`, iOS 16.2 and newer); both share `TravelDayAttributes.swift`, and the texts come from
`en.json` and `de.json` through the activity's content. `LiveActivity/build.sh` builds both with `swiftc` from the Xcode
the build uses (target `BuildLiveActivity` in the project). The extension has a bundle ID of its own,
`<ApplicationId>.LiveActivity`, and on a device needs a provisioning profile for it: a signed device build includes
it only with `-p:LiveActivityProvision=path/to/profile.mobileprovision` and otherwise builds the app without Live
Activities, as before. Builds for the simulator and unsigned builds (the workflow's IPA) always include it;
whether sideloading tools sign the extension along with the app is not tried yet.

The ticket shortcuts are quick actions on the app icon on iOS too (`IosShortcuts`, `UIApplication.ShortcutItems`): a
long press on the icon lists the tickets of the next journeys, and one opens its plan and ticket. Avalonia's scene
delegate neither answers quick actions nor passes on the one that started the app, so while the app launches Jourfold
adds `windowScene:performActionForShortcutItem:completionHandler:` to its Objective-C class and wraps its
`scene:willConnectToSession:options:`, whose options carry a quick action that started the app. Without that class,
as in another version of Avalonia, the app runs as before, without shortcuts. Like on Android, the shortcuts only
list journeys within the next day that have a ticket.

Departures, changes and arrivals are time-sensitive notifications on iOS (`InterruptionLevel.TimeSensitive`, with the
entitlement `com.apple.developer.usernotifications.time-sensitive`): they come through a Focus, as Android's travel
day channel shows them at the top of the screen. The other notices keep the normal level. A user can turn this off in
the Settings app under Notifications, Jourfold, Time Sensitive Notifications. The entitlement is only in builds made
with `-p:TimeSensitiveNotices=true` and in the simulator: whether a free Apple ID (Personal Team) and AltStore,
SideStore and Sideloadly can sign it is not known yet, so the IPA from GitHub leaves it out, and iOS shows the same
notices at the normal level there. Once someone has installed a build with it that way, it can be on everywhere.

## Problem reports

An unexpected error shows "Something went wrong" and is written to `logs/errors.log` on the device. The card offers
**Send error by e-mail**: Jourfold opens the user's mail app with a few lines to fill in and what any app may read about
the device without a permission (app version and channel, maker and model, system and API level, build, screen size
and density, processor, device type, language), and attaches a copy of the log (`MobileSession.SendProblemByMailAsync`).
The user sees and edits everything in the mail app and sends it themselves; Jourfold sends nothing. The address is
the public contact of the GitHub account that publishes Jourfold (`SupportContact`, as on the desktop), looked up
only when the button is tapped; without one the mail opens without an address. On Android the report goes to the apps
that take `mailto:` links (`<queries>` in the manifest), with the log through the FileProvider; on iOS to the Mail
compose sheet, or as a `mailto:` link without attachment when no mail account is set up. Expected failures, such as a
trip TravelRepo will not change, say why and have no button.

## Tickets

A plan's tickets are the PDFs, images and Wallet passes (`.pkpass`) attached to it or to its booking. "Show ticket"
sits in the details, on Today's next plan, in notifications and, for journeys within a day, as shortcuts on the app's
icon. A Wallet pass is read by TravelRepo's `WalletPass` and shown by Jourfold itself, offline and without a wallet
app: the barcode (QR, Aztec, PDF417 or Code 128, drawn with ZXing.Net) large on white, its code as text, and the
pass's fields; "open in another app" hands it to a wallet app where one is installed. Passes and pictures are shown
at full screen brightness. Passes reach the trip through "share to Jourfold" or "attach file".

## Local script and taxi card

A place can keep its name and address in the local script (TravelRepo `local_name`, `address.local`), entered on
the desktop or the phone; choosing a search result fills them when the place is written differently there (the
search server answers in the place's own language when none is asked for). "Taxi card" on a place, and on a plan
whose place, stay or destination has an address, shows the local name and address large at full brightness, the
usual spelling and the phone number below.

## Packing, money, weather and sharing

- **Packing lists** (More → Packing lists): the user's items to tick off, what others bring, what nobody brings yet,
  and a line per list to add items with "@name", "@all", "#group" and "2x" as on the desktop, with the same
  chips that put them in (the keyboard has "@" and "#" a layer away), the same suggestions while typing and the same refusal of names nobody in the trip has (with a way to add them). Groups are headings in each
  list. **Mine**, **All** and **Nobody yet** filter the lists; once the phone knows who the user is, it starts with
  Mine and then keeps the last choice. New lists are added under the lists, by name; the new list's first line then
  has the focus. Enter adds an item and keeps the keyboard open for the next one. The pencil next to a list renames
  or deletes it (items also on another list stay there). An item's arrow, or a tap on someone else's item, opens
  its page: title, group, quantity, who brings it and the list it is on, saved together; who has packed it, ticked
  at once for any of the people who bring it; and Delete. "I'll bring it" takes on an item nobody brings yet. Before the trip, Today starts
  with "Before the trip": the packing lists, with how much is left to pack or a way to start the first list, and the
  open tasks. More says the same, and a notification comes the evening before.
- **Currency converter** (More): works offline with the European Central Bank's reference rates of the last sync.
  Fetching asks once; afterwards the rates come with every sync and when the converter opens, and nothing about the
  trip is sent. Rates fetched in the last 12 hours count as new, because the bank publishes on working days only, and
  a failed attempt waits 30 minutes, so an offline phone does not keep trying. Below the converter it says whose
  rates of which day they are, then when the phone last checked for new ones; a tap there checks again now, at most
  once a minute, and keeps the last rates when that fails. When the newest rates are of an earlier day than the
  check, as on a weekend, it says that the bank publishes on working days around 16:00 Frankfurt time. It converts
  into the trip currency, from the first other currency the trip suggests. The chips are, always in this order, the trip
  currency, the local one where the phone is (from the phone's time zone, which phones set from the network when
  travelling, so no location is needed), the currencies of its costs and the phone region's, then other currencies
  chosen lately (the last four, kept on the phone), each with an x that removes it from the list (a long press does
  too). One that moves up because the trip now has costs in it loses its x there, keeps its place in the list and
  comes back with it when those costs are gone. The
  expense form offers the same chips in the same order; choosing one only moves the mark. No currency is offered only because the bank quotes
  its rates against the euro.
- **Costs** (More → Costs): the total and budget in the trip currency, who owes whom (one line per person), payments
  still to pay with **Mark as paid**, budgets, categories per person under their total (for everyone, or for the
  people chosen), and the payments by day, each with the icon of its category before its title, with a search over name,
  category, people and what they belong to. The title of a new expense suggests its category while it is typed
  ("Taxi" is transport, "Restaurant" food; TravelRepo's `ExpenseCategories.SuggestFromTitle`), marked on the chips
  with a line that says so; it is kept unless another is chosen, and what the expense belongs to says more than its title. **Add expense** and the plus on that page start an expense: the day it was
  paid, its currency (chips as on the converter, including the ones chosen lately there; **Other** suggests codes while
  typing and takes one with Enter), what the card charged in the trip currency, who paid, what it belongs to, how it is split (equally, by amounts,
  shares or percent; only a split that adds up can be saved), a category or a new one. **Pay back…** is the pay
  function: whose open items, which to settle now, and what that comes to. Budgets are added and changed there, with
  what they apply to. Payments due within two weeks show on Today. Amounts and split parts open the number pad
  (with both decimal comma and point). Choosing a chip below the title closes the keyboard, so it does not cover
  the rest of the form; editing opens without the keyboard. A payment in another currency than the trip's shows, in
  its details and the booking's, what the card charged, or what it comes to with the reference rates, marked ≈ with
  their day. Paid payments carry a card symbol next to their amount on the Costs page and in a booking's
  payments: green when exact in the trip currency (paid in it, or charged), yellow while estimated; screen readers
  say which. In the expense form, **Paid with** chooses **Card** (what was charged) or **Cash** bought before (the
  rate it cost, "1 EUR = 160.5 JPY", offered from the last cash in that currency or the reference rate, saying which);
  the calculator next to it opens below and works the rate out from what was received and what the account was
  charged, fees included (`RateCalculator` in `Jourfold.UI`, shared with the computer). Cash counts exactly at that
  rate and shows a green bank note. The converter page has the same calculator under **Check the fees**, with the currencies chosen above, as its hint says. The trip currency is shown on More and changed under More → Trip details; budgets in the old currency
  are then offered to be converted, each listed with its old and new amount, and stay as they are otherwise. Not on
  the phone yet, as a possible later addition: exporting costs as PDF or CSV, which the computer does.
- **Expenses with a receipt**: Quick Add → Expense → "Photograph the receipt" uses the phone's camera app (Jourfold
  needs no camera permission); the photo is turned upright, made at most 1600 pixels on the longer side and attached
  to the expense, which the user paid unless the form says otherwise. There is no text recognition (ML Kit would
  rule out F-Droid).
- **Weather**: after a yes in Today or Settings, as on the desktop, each day's forecast shows in Today and Plan, and
  what it means for a plan on Today's next plan and in the details. Forecasts are kept on the device; warnings
  become notifications on the morning of the plan. The day's weather and the details say when the forecast was
  fetched; a tap there fetches it again now, and a notice says so when that fails.
- **Share a day** (Plan → share): the day as plain text with times, places and OpenStreetMap links, for people
  without Jourfold.

## Trips in the calendar app

Settings → Calendar → "Show trips in the calendar app" (off until turned on, asks for the calendar permission) puts
each trip on the device into the system calendar as a calendar of its own, to read only; "One calendar per kind"
splits each trip into getting around, stays, food and plans, which calendar apps show and hide one by one. Entries
come from TravelRepo's `TripCalendar` with stays as check-in and check-out: a single moment at a fixed time, a block
for the check-in or check-out hours, all day when the time is not known. Cancelled plans are left out; ideas and plans
not yet booked are tentative. The calendars follow every edit (after two quiet seconds) and every sync, a trip removed
from the device takes its calendar along, and turning the option off removes them all.

On Android the calendars belong to an account "Jourfold" (`AndroidCalendar`, `CalendarAccountService`), written as
its sync adapter and never uploaded. Only what changed is written: each entry keeps a fingerprint in `sync_data1`.
The account's sync is on, because "Sync now" in the system settings asks only accounts whose sync is on, but it has
no schedule, and `CalendarSyncService` acts only on syncs the user asked for (`SYNC_EXTRAS_MANUAL`: "Sync now", a
calendar app's refresh). Such a sync takes newer versions from GitHub only as a fast-forward (`GitRepository.PullAsync`):
it never sends and never combines, and it leaves alone a trip with changes or versions of its own, one where both
sides changed, and the trip open in the app. Then it writes the calendars. Syncs Android starts on its own do nothing.

The AOSP calendar app in the emulator images stops with any event (also one it would write itself); check entries
with `adb shell content query --uri content://com.android.calendar/events`.

On iPhone and iPad (`IosCalendar`, EventKit) the calendars are made in the device's own calendars, or in iCloud when
the device keeps none, as when iCloud calendars are on. iOS cannot mark a calendar read-only, so the settings say
that changes made in the calendar app are replaced: each time the calendars are written, entries that differ from the
plan are written again. Each entry carries its key as its URL (`jourfold-entry:`) and is compared with the plan, so
only what differs is written; `calendars.json` beside the trips keeps the calendars Jourfold made. There is no "Sync
now"; the calendars follow edits and syncs in the app.

## Building for Android

Needs the .NET `android` workload, the Android SDK with an NDK (r28 or later) and CMake, and TravelRepo checked out
beside this repository (0.7.0 or newer, which has `TravelRepo.Git.LibGit2`).

1. Build libgit2 for Android once, from the TravelRepo repository:

   ```sh
   git clone https://github.com/libgit2/libgit2.git /tmp/libgit2 && git -C /tmp/libgit2 checkout 5853918c4c6a7b12f8becf4bd11ff4362ebb9020
   ANDROID_NDK=~/Android/Sdk/ndk/28.2.13676358 PATH=~/Android/Sdk/cmake/3.31.6/bin:$PATH \
     ../travelrepo/eng/build-libgit2-android.sh /tmp/libgit2 artifacts/libgit2-android
   ```

2. Build and install the app on an emulator or a phone:

   ```sh
   dotnet build src/Jourfold.Android -c Debug -p:RuntimeIdentifier=android-x64 -p:EmbedAssembliesIntoApk=true
   adb install -r src/Jourfold.Android/bin/Debug/net10.0-android/android-x64/dev.jourfold.app-Signed.apk
   ```

   Use `android-arm64` for a phone. Release builds precompile everything (`AndroidEnableProfiledAot` is off), which
   starts the app about a third faster on old phones.

The Android project is not in `Jourfold.sln`, so the desktop build and CI do not need the Android workload.

## Signed builds

Android installs an update only when it is signed with the same key as the installed app and has a higher version
code. `.github/workflows/android.yml` builds an APK signed with the project's release key. The job runs in the
`release` environment, which holds the key and needs a maintainer's approval, and checks the certificate before it
keeps the APK.

- A published release runs it through `release.yml`, which attaches `jourfold-<version>-android.apk` to the release.
- A tag such as `v0.8.0-android.3` builds a preview for testers (`0.8.0-preview.3`) and keeps it as a workflow
  artifact for 30 days. The part before `-android` has to match the version in `Directory.Build.props`.

The version code is the version times 1000 plus a build number: the run number for previews and 999 for the release,
so 0.8.0 previews are 800000 + run and the release is 800999. Each build updates the one before, and a release
updates every preview of its version.

The release certificate's SHA-256 fingerprint, to compare with `apksigner verify --print-certs`:

```
A8:31:A2:52:AB:E3:D2:56:BC:E5:CC:0C:6F:00:4A:C3:00:57:D2:B9:29:D9:F8:26:37:C9:35:99:6C:C2:6E:54
```

To sign on a computer that holds the key instead, build the APK without signing and sign it there:

```sh
JourfoldGitHubClientId=<client ID> JourfoldGitHubAppSlug=<slug> dotnet publish src/Jourfold.Android -c Release -o artifacts/android \
  -p:AndroidBuildNumber=<n> -p:ApplicationDisplayVersion=<version>-preview.<n>
# artifacts/android/dev.jourfold.app.apk is not signed yet
zipalign -P 16 -f 4 dev.jourfold.app.apk aligned.apk
apksigner sign --ks <keystore> --ks-key-alias jourfold --out jourfold.apk aligned.apk
apksigner verify --print-certs jourfold.apk
```

`zipalign -P 16` keeps the native libraries aligned for devices with 16 KB pages; `apksigner` asks for the password.
Pick a higher `AndroidBuildNumber` for every build.

Builds from a computer are signed with that computer's debug key. Android refuses to install one over a signed build
and the other way round; switching means uninstalling first, which removes the trips on the phone that are not on
GitHub.

## iPhone and iPad

The iOS project is not in `Jourfold.sln`, and Linux and Windows cannot build it. `.github/workflows/ios.yml` builds it
on a Mac runner for changes to the mobile code: it builds libgit2 as `git2.xcframework`
(`eng/build-libgit2-ios.sh` in TravelRepo), starts the app on an iPhone and an iPad simulator with the sample trip open
(the `--open-sample` launch argument), checks that the trip has a version, keeps screenshots of both, and builds
`jourfold-<version>-ios-<run>-unsigned.ipa` for devices.

To build on a Mac (the .NET SDK from `eng/sdk.json`, CMake, and TravelRepo beside this repository): each .NET for iOS
needs one Xcode, so install the workload the workflow uses with the Xcode it names, for example
`dotnet workload install ios --version 10.0.401` with Xcode 26.6 (`sudo xcode-select -s /Applications/Xcode_26.6.app`).
Then:

```sh
git clone https://github.com/libgit2/libgit2.git /tmp/libgit2 && git -C /tmp/libgit2 checkout 5853918c4c6a7b12f8becf4bd11ff4362ebb9020
../travelrepo/eng/build-libgit2-ios.sh /tmp/libgit2 artifacts/libgit2-ios
dotnet build src/Jourfold.iOS -c Debug -r iossimulator-arm64 -t:Run
```

`eng/ios-icons.py` makes the app icon and the launch screen's mark from `assets/branding/mark.svg`. The icon has a
dark and a tinted variant beside the light one, so it follows the home screen's Customize setting (dark, tinted and,
since iOS 26, clear). An Icon Composer `.icon` file with layers is not used: actool of Xcode 26.6 compiles one, but
.NET for iOS 26.5 passes only asset catalogs to actool; .NET for iOS 27 does pass it and needs Xcode 27.

**On a device without the Apple Developer Program.** A free Apple ID can sign apps for its own devices, with limits:
the app stops starting after 7 days until it is signed and installed again (the trips stay as long as the app is not
deleted), and at most three such apps per device.

- With a Mac: sign in with the Apple ID in Xcode (Settings → Accounts), connect the device, turn on Developer Mode on
  it (Settings → Privacy & Security), and build with a bundle ID of your own, because the ID must be unique per Apple
  ID: `dotnet build src/Jourfold.iOS -c Release -r ios-arm64 -p:ApplicationId=dev.jourfold.app.<name> -t:Run`. If the
  build finds no provisioning profile, create an empty iOS app with the same bundle ID in Xcode and run it on the
  device once; Xcode then makes the profile. Trust the developer on the device under General → VPN & Device
  Management.
- Without a Mac: sideloading tools such as AltStore, SideStore or Sideloadly sign the unsigned IPA with the Apple ID
  and install it: `jourfold-<version>-ios-unsigned.ipa` from a release, or for changes not released yet the IPA the
  workflow keeps for each run. SideStore renews the signature on the device every 7 days.

**What iOS draws itself.** On iPhones the bottom bar is iOS's own tab bar (`IosTabBar`). The shared app also hands
these to the system through `MobileHost`, so VoiceOver, Larger Text and the system's language come with them; Android
keeps the app's own:

- Text follows Larger Text in the Settings app, also while the app runs (`TextScale`): the theme's sizes are the
  `Size.*` resources and sizes set in code go through `TextScale.Size`. Only text grows, up to 1.65 times the
  standard, where the date strip and the timetable's rows still hold it.
- Date and time fields open `UIDatePicker` in a sheet with Cancel and Done (`SystemPickers`, `IosPickers`).
- Long-press menus are action sheets and questions are alerts (`IosDialogs`). Menus with a search field or twelve
  entries and more stay the app's own.
- Sheets float 8 from the screen's edges below their top detent, as iOS 26 shows its own.
- The buttons at the trip's top are round. The search stays there: an iPhone's tab bar shows five tabs at most and
  counts a search tab among them, and the four places and the plus already fill it.
- Changing the day, adding from Quick Add and a sync the user asked for give a light haptic (`Feel`).

Today of a shared trip can be pulled down to sync on every phone.

**On a device.** The simulator runs .NET with a just-in-time compiler; a device compiles ahead of time. LibGit2Sharp
passes delegates to libgit2 without `MonoPInvokeCallback`, which ahead-of-time code cannot call back, so the project
turns on the interpreter (`UseInterpreter`, all assemblies still compiled ahead of time). A Release build ran on an
iPhone: signing in with GitHub, cloning a shared trip, Today, Plan and Map, editing and sync work. Not tried on a device
yet: notifications, the calendar and the camera beyond starting them, and ⌘ keys on an iPad with a hardware keyboard.
The Live Activity ran on an iPhone, signed with a profile of its own for the extension (`LiveActivityProvision`).

## Screenshots

`eng/MobileScreenshots` renders the real app headless with the sample trip at Sunday 16 May 2027, 10:40 in Tokyo,
at phone size (412 × 915) and as a tablet (1194 × 834):

```sh
dotnet run --project eng/MobileScreenshots -c Release -- artifacts/mobile-screenshots
```

The launcher icon and splash drawable are generated from `assets/branding/mark.svg` with
`python3 eng/android-icons.py assets/branding/mark.svg src/Jourfold.Android/Resources`. The script also draws the
themed icon (`ic_launcher_monochrome`, the adaptive icon's `<monochrome>` layer), which needs cairosvg and Pillow: on
Android 13 and newer with "Themed icons" turned on, the home screen tints it in its own colours, as other apps' icons.
The mark's three parts keep three strengths, and the road and pin are left out, as in iOS's tinted icon. Without the
setting, and on Android 10 to 12, the coloured icon stays.
