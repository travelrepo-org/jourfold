# Jourfold for iOS in Swift

The native iPhone and iPad app, with the functions of `src/Jourfold.Mobile`. It is meant to replace `src/Jourfold.iOS`;
until it does, releases attach the .NET app, and this one is built beside it as "Jourfold Preview"
(`dev.jourfold.app.preview`), so both can be on a device. Requires iOS 26.

Trips are TravelRepo trips. Reading, writing, checking, costs, search, versions, sync and merging all go through
TravelRepoKit (TravelRepo compiled with NativeAOT); the app has no rules of its own for them. `TravelRepoBridge.swift`
is the one place that talks to it.

## Build

Requires Xcode 26.6 or 27, Python 3 and an iOS 26 simulator.

```bash
./fetch-git2.sh
```

downloads libgit2 from the TravelRepo release (checked against its checksum). The Swift package comes from the same
release. Then open `Jourfold.xcodeproj`, or build with `./shoot.sh build`; the scripts take the simulator from `SIM` or
from a local file `.sim` containing `SIM=<id>`.

The version comes from `Directory.Build.props` through `settings.sh`, which every script uses; a build started in Xcode
has the version 0.0.0. `settings.sh` reads the same variables as the other builds: `JourfoldGitHubClientId` and
`JourfoldGitHubAppSlug` for signing in with GitHub (without them everything else works), `JourfoldDistribution` and
`JourfoldDebugTools`.

`TEAM=… DEVICE=… ./device.sh` builds and installs on an iPhone. A free developer team has no app groups, so such builds
share data with the share extension and the widgets through the Keychain (`Device.entitlements`).

`./ipa.sh <file>` makes an unsigned IPA, as `.github/workflows/ios-swift.yml` does. `SharedStore.swift`
finds the app group or the Keychain group such tools sign it with. Not tried yet with any of them.

## Debug tools

Launch arguments (demo data, resetting, opening a page), the self-tests, the test report and "Show the next one now"
under Notifications exist only in builds with `JourfoldDebugTools=true` (`DebugTools.swift`). `shoot.sh`, `selftest.sh`
and `uitest.sh` set it; device builds need it for `report.sh`. The report contains counts and states; contents only of a
trip called "Test".

## Tests

| Command | What |
| --- | --- |
| `./selftest.sh` | Logic and TravelRepo, in the simulator. Deletes the simulator's trips |
| `./uitest.sh [Class[/test]]` | Flows and Apple's accessibility audit. Deletes the simulator's trips; never run it on a phone with real ones |
| `./shots.sh` | Captures of the views, English and German, light and dark |

CI runs the self-test for every change and the UI tests when the workflow is started by hand.

## Texts

English is in the code, German in `tools/strings.py`. After changing a text: build, `python3 tools/strings.py`
(lists what is missing), build again.

## Different from the other apps

[What differs between iOS, Android and the desktop](../../docs/platform-differences.md) lists it, with what to follow up.

## Not tested

Uploading to GitHub from a phone, VoiceOver by ear, the Lock Screen widgets, notifications worked out in the background,
the camera for receipts, Google Maps and Waze, a keyboard, scanning a pass's barcode from the screen, and on the iPad
landscape and split screen.

## Data of the .NET app

Under the same bundle ID (`dev.jourfold.app`) an update finds the .NET app's trips (`trips`), recovery data
(`recovery/<folder>`) and GitHub sign-in, because both use the same places. `DotNetApp.swift` reads its settings once:
name, e-mail, the choices about maps, weather, rates, notifications and calendars, recent currencies, and for each trip who
the user is, the second time zone and the last sync. Its settings file stays, so going back to the .NET app keeps everything it had. Not taken: shares that still wait in its share extension's inbox. To try it, install
the .NET app in the simulator, then this one built with `JF_BUNDLE_ID=dev.jourfold.app`.

## Before it replaces the .NET app

- Try an update with a real sign-in and calendars on a phone.
- Attach its IPA to releases and remove `src/Jourfold.iOS`, `src/Jourfold.iOS.Share` and their parts of `Jourfold.Mobile`.
- Close or accept the differences above, and test what is listed as not tested.
