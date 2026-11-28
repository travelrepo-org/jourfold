#!/bin/zsh
# Build, install and capture screenshots, with the debug tools.
# Usage: ./shoot.sh build            – build and install
#        ./shoot.sh <name> [light|dark] [en|de] [launch args…]  – launch with args, wait, save screenshots/<name>.png
set -e
cd "${0:A:h}"
JourfoldDebugTools=true; . ./settings.sh
[[ -f .sim ]] && . ./.sim; : ${SIM:?the id of a simulator, see README.md}; export SIM
APP=dev.jourfold.app.preview
if [[ $1 == build ]]; then
  xcodebuild -project Jourfold.xcodeproj -scheme Jourfold -destination "id=$SIM" -derivedDataPath build $SETTINGS build 2>&1 | grep -E "error:|BUILD" | sort -u; (( $pipestatus[1] == 0 )) || exit 1
  xcrun simctl install $SIM build/Build/Products/Debug-iphonesimulator/Jourfold.app
  exit 0
fi
name=$1; theme=${2:-light}; lang=${3:-en}; shift 3 || true
locale=en_GB; [[ $lang == de ]] && locale=de_DE
xcrun simctl ui $SIM appearance $theme
xcrun simctl status_bar $SIM override --time "14:10" --batteryState charged --batteryLevel 100 --wifiBars 3 --cellularBars 4
# At home in Berlin, whatever zone the Mac is in: captures are public.
SIMCTL_CHILD_TZ=Europe/Berlin xcrun simctl launch --terminate-running-process $SIM $APP -AppleLanguages "($lang)" -AppleLocale $locale "$@" >/dev/null
sleep ${WAIT:-4}
mkdir -p screenshots
xcrun simctl io $SIM screenshot screenshots/$name.png >/dev/null 2>&1
echo screenshots/$name.png
