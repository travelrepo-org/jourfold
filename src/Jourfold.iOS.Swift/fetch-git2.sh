#!/bin/zsh
# Downloads libgit2 for iOS from the TravelRepo release this app uses, into Frameworks/ (not in Git). Run it once before building.
set -e
cd "${0:A:h}"; VERSION=0.14.0; SUM=a44eb62282bc529973db2577a70bc443ec517782aaab69775a0304f58e21e22c
mkdir -p Frameworks; ZIP=Frameworks/git2-$VERSION.zip
[[ -f $ZIP ]] || curl -fsSL -o $ZIP https://github.com/travelrepo-org/travelrepo/releases/download/v$VERSION/git2.xcframework.zip
echo "$SUM  $ZIP" | shasum -a 256 -c - >/dev/null || { rm -f $ZIP; echo "the download does not match its checksum"; exit 1; }
rm -rf Frameworks/git2.xcframework; unzip -q $ZIP -d Frameworks
[[ -d Frameworks/git2.xcframework ]] || { echo "no git2.xcframework in the download"; exit 1; }
