#!/usr/bin/env sh
# Starts Jourfold on a running Android emulator or device with the sample trip open and checks that it works:
#
#   eng/android-smoke.sh <apk> <output-directory>
#
# The APK is installed over any earlier Jourfold, then started with the extra open-sample (MainActivity). The app makes
# the sample trip through Git and writes the result to the system log under the tag "Jourfold": a version shows that
# libgit2 loads and works. The check fails when that line does not come within three minutes, when it reports no
# version, or when the app is no longer running afterwards. The screen and the log are kept in the output directory.
#
# Used by .github/workflows/android-check.yml; locally it uses the device adb picks, or the one in ANDROID_SERIAL.
set -eu
APK=$1
OUT=$2
PACKAGE=dev.jourfold.app
mkdir -p "$OUT"

adb wait-for-device
adb install -r "$APK" > /dev/null
adb logcat -c
activity=$(adb shell cmd package resolve-activity --brief "$PACKAGE" | tail -n 1 | tr -d '\r')
adb shell am start -W -n "$activity" --ez open-sample true > /dev/null

result=""
for _ in $(seq 1 90); do
  result=$(adb logcat -d -s Jourfold:I | grep 'open-sample:' | tail -n 1 || true)
  [ -n "$result" ] && break
  sleep 2
done
# The first frames of the opened trip, as a picture to look at.
sleep 5
adb exec-out screencap -p > "$OUT/screen.png" || true
adb logcat -d > "$OUT/logcat.txt" || true

fail() {
  echo "::error::$1"
  grep -E 'AndroidRuntime|FATAL|Jourfold|mono|DOTNET' "$OUT/logcat.txt" | tail -n 60 || true
  exit 1
}
[ -n "$result" ] || fail "Jourfold did not report on the sample trip within three minutes."
echo "$result"
case "$result" in *"open-sample: version "*) ;; *) fail "The sample trip has no version: Git did not work." ;; esac
adb shell pidof "$PACKAGE" > /dev/null || fail "Jourfold is no longer running."
echo "Jourfold runs, and the sample trip has a version."
