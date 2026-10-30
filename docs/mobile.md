# Mobile app

The phone and tablet app is in development. Android comes first; iOS and iPadOS will use the same code once there is
a Mac to build on.

## Projects

- `src/Jourfold.UI`: the look shared with the desktop: colour and type tokens, icons, the brand mark, covers, the
  `Ui` factory and the UI strings.
- `src/Jourfold.Mobile`: the phone and tablet interface. `MobileSession` holds the trips on the device and the open
  trip's day plan (TravelRepo's `TripAgenda`); `AppShell` lays out the bottom bar on phones and a rail from 600 dp;
  the views are in `Views/`. It is plain .NET and Avalonia, with no Android or iOS code.
- `src/Jourfold.Android`: the Android app. It passes the app's private folder, the built-in Git (libgit2), the clock
  and the phone's time zone to `Jourfold.Mobile` through `MobileHost`, and shows the system splash screen.

## How times read

Today follows the phone's clock and adds the item's local time when its zone differs. Plan, details and bookings show
the item's local time and add the phone's time when the phone is in another zone. Each day says which clock its times
follow ("Times as on your phone (Berlin)", "Local time Tokyo"), and a second time names its place instead of a zone
abbreviation ("Berlin: 01:30 (Wed)"). A journey across zones always names both places. `TimeText` implements these
rules and `MobileTests` checks them.

A tap on that line chooses a second time zone for the trip, from the phone's zone and the zones of the trip and its
items. Its times then stand quieter under every time in Today and Plan ("01:30", "18:00 +1" when it is another day),
and not where both zones show the same time. The choice stays on this phone, per trip. Dark mode uses the design
spec's navy tokens, which `Jourfold.UI` shares with the desktop.

An item that runs past midnight, such as a night flight, appears again on the days after it starts: on the day it
ends as its arrival or end, on the days between as running all day. TravelRepo's `TripAgenda` marks these lines as
`AgendaRole.Continued`; Today reads spans itself and shows such an item once.

## Plan, details and long press

The date strip stays at the top while the day scrolls. A sideways swipe moves the day with the finger and slides the
next one in; the strip scrolls on its own. A tap on an item opens its details at the smallest step (time, place,
booking reference, Navigate). Below the top step the content does not scroll, so a drag anywhere on the sheet moves
it; at the top step the content scrolls, and dragging down at its top lowers the sheet again. Journeys show their
travel time. A long press on a line of the details copies it, such as the flight number, the seats, the
address or the booking reference. Android 13 and later confirm a copy themselves; on older versions a short notice
says what was copied. Only the item happening now or next on the phone's today shows extra context in the plan. A
stay's check-in says how many nights it has; the night line under the day appears only on the other nights.

A long press on an item opens a menu with details, edit, status, Navigate, copy the booking reference, duplicate,
move to another day and delete. A long press on a trip in the trip list removes it from the phone after a question
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

The Map tab draws the selected day like the desktop's map (`MapSurface` in Jourfold.UI): numbered pins in the order
of the plan, a dashed line through them and journeys as arcs. It pans with a finger and zooms with two fingers, a
double tap or the buttons. Without online maps it shows a plain grid and works offline; street tiles from
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
  switches between the day list and a timetable; pages under More keep a readable width.
- From 1200 dp: a sidebar as on the desktop, with the trip, the places with their names, the trip's pages (bookings,
  tasks, costs, places, files, travellers) and where the trip is kept, which folds into the rail; Plan starts as
  the timetable, with as many days as fit (three to seven), each in its own local time and a line at the current
  time. It reads like the desktop's timetable: all-day plans in a strip above the grid, stays as bars over their
  nights in a band below it, check-in and check-out as thin markers instead of blocks (a dashed line at a fixed
  hour, a soft band with "Check-in 15:00–19:00" for the check-in hours when the arrival has no fixed hour, "Check-out
  by 11:00" for the latest departure), and plans as tinted blocks with icon, time and title, dashed for ideas and
  parts of the day.

With a mouse, rows, places and cards show where the pointer is (only then, so a tapped row does not stay marked),
and a right click opens the same menu as a long press. A hardware keyboard has Ctrl+N for Quick Add, Ctrl+1 to
Ctrl+4 for the four places, the arrow keys for the previous and next day in Plan and Map, and Escape to go back.
Layouts that follow a foldable's hinge are not built yet.

## Sharing to Jourfold

Other apps share to Jourfold through the system share sheet. Files such as tickets, PDFs, e-mails and images are
read right away and go to an item of the plan or a booking, or only into the trip's files (**More → Files**); files
above 25 MB are left to the desktop. A place from a maps app opens as a new place or as the place of an item, with
its name, address and position taken from the shared text. A short link such as maps.app.goo.gl carries no
position; on request the app asks the maps service where it leads, and sends nothing else. Other text becomes a
note or goes to the end of an item's notes. Shared before a trip is open, the trip list asks which trip it belongs
to. In the details, **Attach a file** picks files on the device.

## Files

Files of an item open on their own page. E-mails (`.eml`) show sender, recipients, date, attachments and their text,
read by TravelRepo's `Mail` without a network; the original HTML layout is still desktop only. Images show as they
are. PDFs and other files go to an app on the phone that can show them: on Android through a `FileProvider` that only
exposes a copy in the app's `shared` folder, replaced by the next file.

## Versions on the phone

Every change is saved to the trip folder right away. Once the trip has been quiet for two minutes, or when the app
goes to the background or the trip is closed, the open changes become a version on their own. Changes left over
when Android stopped the app become a version soon after the trip opens again. Sync and versions also
lets the user create a version with their own description and shows the recent ones. The name and email under
Settings are used for versions made on the phone; they are kept in `settings.json` in the app's private folder,
never in a trip. The phone app does not use Git words such as commit or branch.

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

## Building for Android

Needs the .NET `android` workload, the Android SDK with an NDK (r28 or later) and CMake, and TravelRepo checked out
beside this repository on a branch that has `TravelRepo.Git.LibGit2`.

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
code. `.github/workflows/android.yml` builds an APK signed with the project's release key when a tag such as
`v0.7.1-android.3` is pushed (the part before `-android` has to match the version in `Directory.Build.props`). The
job runs in the `release` environment, which holds the key and needs a maintainer's approval, and keeps the APK as a
workflow artifact for 30 days. The version code is the version times 1000 plus the run number (0.7.1 becomes
701000 + run), so each build updates the one before.

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

## Screenshots

`eng/MobileScreenshots` renders the real app headless with the sample trip at Sunday 16 May 2027, 10:40 in Tokyo,
at phone size (412 × 915) and as a tablet (1194 × 834):

```sh
dotnet run --project eng/MobileScreenshots -c Release -- artifacts/mobile-screenshots
```

The launcher icon and splash drawable are generated from `assets/branding/mark.svg` with
`python3 eng/android-icons.py assets/branding/mark.svg src/Jourfold.Android/Resources`.
