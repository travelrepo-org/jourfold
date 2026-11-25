#!/bin/zsh
# Builds for an iPhone and installs as soon as it is reachable: TEAM=<team> DEVICE=<udid> [BUNDLE=<id>] ./device.sh
# A free developer team cannot get the app group, so the app, the share extension and the widgets share a Keychain group
# instead (Device.entitlements). JourfoldGitHubClientId, JourfoldGitHubAppSlug and JourfoldDebugTools as in settings.sh.
set -e
cd "${0:A:h}"; . ./settings.sh
xcodebuild -project Jourfold.xcodeproj -scheme Jourfold -configuration Release -destination "generic/platform=iOS" -derivedDataPath build-device -allowProvisioningUpdates DEVELOPMENT_TEAM=$TEAM CODE_SIGN_ENTITLEMENTS=Device.entitlements JF_BUNDLE_ID=${BUNDLE:-dev.jourfold.app.preview} JF_APP_NAME="${NAME:-Jourfold Preview}" $SETTINGS build > build-device.log 2>&1 || { grep "error:" build-device.log | sort -u; exit 1; }
for i in {1..${TRIES:-20}}; do xcrun devicectl device install app --device $DEVICE build-device/Build/Products/Release-iphoneos/Jourfold.app 2>&1 | grep -q "App installed" && { echo installed; exit 0; }; sleep 12; done
echo "phone not reachable"; exit 1
