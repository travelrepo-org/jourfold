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
the item's local time and add the phone's time when the phone is in another zone. A journey across zones always names
both zones. `TimeText` implements these rules and `MobileTests` checks them.

## Versions on the phone

Every change is saved to the trip folder right away. Once the trip has been quiet for two minutes, or when the app
goes to the background or the trip is closed, the open changes become a version on their own. Changes left over
when Android stopped the app become a version soon after the trip opens again. Sync and versions also
lets the user create a version with their own description and shows the recent ones. The name and email under
Settings are used for versions made on the phone; they are kept in `settings.json` in the app's private folder,
never in a trip. The phone app does not use Git words such as commit or branch.

## GitHub on the phone

Signing in uses GitHub's device flow, like the desktop: the app shows a code, opens GitHub, and waits until the user
has entered it. The token is kept by the platform's secure storage (on Android encrypted with a key in the Android
Keystore) and handed to the built-in Git for github.com only. A build can name the GitHub App to sign in with,
`-p:JourfoldGitHubClientId=<client ID>`; without one, the user enters the client ID once, as on the desktop.

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

## Screenshots

`eng/MobileScreenshots` renders the real app headless with the sample trip at Sunday 16 May 2027, 10:40 in Tokyo,
at phone size (412 × 915) and as a tablet (1194 × 834):

```sh
dotnet run --project eng/MobileScreenshots -c Release -- artifacts/mobile-screenshots
```

The launcher icon and splash drawable are generated from `assets/branding/mark.svg` with
`python3 eng/android-icons.py assets/branding/mark.svg src/Jourfold.Android/Resources`.
