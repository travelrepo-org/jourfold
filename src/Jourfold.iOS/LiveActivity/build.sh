#!/bin/sh
# Builds the travel day's Live Activity for Jourfold.iOS (called by the project, see BuildLiveActivity):
#
# - JourfoldLive.framework, the bridge the .NET app calls to start, update and end the activity (iOS 15 and newer; it
#   does nothing before iOS 16.2),
# - JourfoldLiveActivity.appex, the widget extension that draws it on the Lock Screen and in the Dynamic Island.
#
# Usage: build.sh <iphoneos|iphonesimulator> <out> <app id> <version> <build> <widget: true|false> [profile]
# With a provisioning profile for <app id>.LiveActivity, the extension gets it and its entitlements, for a signed build
# on a device. Xcode is the one DEVELOPER_DIR names, as for the rest of the build.
set -eu

sdk="$1"; out="$2"; app="$3"; version="$4"; build="$5"; widget="$6"; profile="${7:-}"
here="$(cd "$(dirname "$0")" && pwd)"

# Nothing to do when neither the sources nor the arguments changed.
stamp="$(cat "$here/build.sh" "$here/TravelDayAttributes.swift" "$here/JourfoldLive.swift" "$here/Widget/TravelDayWidget.swift" ${profile:+"$profile"} | shasum | cut -d' ' -f1) $*"
if [ -f "$out/stamp" ] && [ "$(cat "$out/stamp")" = "$stamp" ]; then exit 0; fi
rm -rf "$out"; mkdir -p "$out"

path="$(xcrun --sdk "$sdk" --show-sdk-path)"
sdkversion="$(xcrun --sdk "$sdk" --show-sdk-version)"
if [ "$sdk" = iphonesimulator ]; then suffix=-simulator; platform=iPhoneSimulator; else suffix=; platform=iPhoneOS; fi

# The bridge. ActivityKit is linked weakly, so the app still starts on iOS 15.
framework="$out/JourfoldLive.framework"
mkdir -p "$framework"
xcrun --sdk "$sdk" swiftc -emit-library -O -swift-version 5 -module-name JourfoldLive \
  -target "arm64-apple-ios15.0$suffix" -sdk "$path" \
  -Xlinker -install_name -Xlinker @rpath/JourfoldLive.framework/JourfoldLive \
  -Xlinker -weak_framework -Xlinker ActivityKit \
  -o "$framework/JourfoldLive" "$here/TravelDayAttributes.swift" "$here/JourfoldLive.swift"
cat > "$framework/Info.plist" << EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleIdentifier</key><string>$app.JourfoldLive</string>
  <key>CFBundleExecutable</key><string>JourfoldLive</string>
  <key>CFBundleName</key><string>JourfoldLive</string>
  <key>CFBundlePackageType</key><string>FMWK</string>
  <key>CFBundleInfoDictionaryVersion</key><string>6.0</string>
  <key>CFBundleShortVersionString</key><string>$version</string>
  <key>CFBundleVersion</key><string>$build</string>
  <key>CFBundleSupportedPlatforms</key><array><string>$platform</string></array>
  <key>MinimumOSVersion</key><string>15.0</string>
</dict>
</plist>
EOF

if [ "$widget" = true ]; then
  # The extension. Live Activities came with iOS 16.1; ActivityContent, which the bridge uses, with 16.2.
  appex="$out/widget/JourfoldLiveActivity.appex"
  mkdir -p "$appex"
  xcrun --sdk "$sdk" swiftc -O -swift-version 5 -parse-as-library -application-extension -module-name JourfoldLiveActivity \
    -target "arm64-apple-ios16.2$suffix" -sdk "$path" \
    -Xlinker -e -Xlinker _NSExtensionMain \
    -o "$appex/JourfoldLiveActivity" "$here/TravelDayAttributes.swift" "$here/Widget/TravelDayWidget.swift"
  cat > "$appex/Info.plist" << EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleIdentifier</key><string>$app.LiveActivity</string>
  <key>CFBundleExecutable</key><string>JourfoldLiveActivity</string>
  <key>CFBundleName</key><string>JourfoldLiveActivity</string>
  <key>CFBundleDisplayName</key><string>Jourfold</string>
  <key>CFBundlePackageType</key><string>XPC!</string>
  <key>CFBundleInfoDictionaryVersion</key><string>6.0</string>
  <key>CFBundleDevelopmentRegion</key><string>en</string>
  <key>CFBundleShortVersionString</key><string>$version</string>
  <key>CFBundleVersion</key><string>$build</string>
  <key>CFBundleSupportedPlatforms</key><array><string>$platform</string></array>
  <key>DTPlatformName</key><string>$sdk</string>
  <key>DTSDKName</key><string>$sdk$sdkversion</string>
  <key>DTPlatformVersion</key><string>$sdkversion</string>
  <key>MinimumOSVersion</key><string>16.2</string>
  <key>UIDeviceFamily</key><array><integer>1</integer><integer>2</integer></array>
  <key>NSExtension</key>
  <dict>
    <key>NSExtensionPointIdentifier</key><string>com.apple.widgetkit-extension</string>
  </dict>
</dict>
</plist>
EOF
  # The project signs the extension with these entitlements: the profile's for a device, none for the simulator and
  # unsigned builds.
  entitlements="$out/widget/JourfoldLiveActivity.entitlements"
  if [ -n "$profile" ]; then
    cp "$profile" "$appex/embedded.mobileprovision"
    security cms -D -i "$profile" > "$out/profile.plist"
    plutil -extract Entitlements xml1 -o "$entitlements" "$out/profile.plist"
  else
    printf '%s\n' '<?xml version="1.0" encoding="UTF-8"?>' '<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">' '<plist version="1.0"><dict/></plist>' > "$entitlements"
  fi
fi

printf '%s' "$stamp" > "$out/stamp"
