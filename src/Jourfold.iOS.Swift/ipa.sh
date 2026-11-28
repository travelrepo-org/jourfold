#!/bin/zsh
# Builds for devices without signing and packs an IPA to sign with an Apple ID: ./ipa.sh <file.ipa>
set -e
out=${1:A}; cd "${0:A:h}"; . ./settings.sh
xcodebuild -project Jourfold.xcodeproj -scheme Jourfold -configuration Release -destination "generic/platform=iOS" -derivedDataPath build-ipa CODE_SIGNING_ALLOWED=NO $SETTINGS build | grep -E "error:|warning: .*(deprecat|never)|BUILD" | sort -u
work=$(mktemp -d); mkdir $work/Payload; cp -R build-ipa/Build/Products/Release-iphoneos/Jourfold.app $work/Payload/
(cd $work && zip -qry $out Payload); rm -rf $work; ls -l $out
