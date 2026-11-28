#!/bin/zsh
# Runs the UI tests: ./uitest.sh [test …]. Xcode 26.6's runner cannot connect on a Mac whose developer
# mode is off; RUNNER=<another Xcode>/Contents/Developer runs the tests with that one.
cd "${0:A:h}"; JourfoldDebugTools=true; . ./settings.sh; [[ -f .sim ]] && . ./.sim; : ${SIM:?the id of a simulator, see README.md}; export SIM; L=${TMPDIR:-/tmp}/jourfold-uitest-${SIM:0:4}.log
[[ -n $NOBUILD ]] || xcodebuild build-for-testing -project Jourfold.xcodeproj -scheme Jourfold -destination "id=$SIM" -derivedDataPath build $SETTINGS > $L 2>&1 || { grep "error:" $L | sort -u; exit 1; }
only=(); for t in "$@"; do only+=(-only-testing:JourfoldUITests/$t); done
DEVELOPER_DIR=${RUNNER:-${DEVELOPER_DIR:-$(xcode-select -p)}} xcodebuild test-without-building -xctestrun build/Build/Products/*.xctestrun(om[1]) -destination "id=$SIM" $only > $L 2>&1
grep -E "error:|DUMP|Test Case.*(passed|failed)|TEST EXECUTE|encountered an error" $L | sed -E 's/.*\/(JourfoldUITests\/)/\1/' | cut -c1-1500
grep -q "TEST EXECUTE SUCCEEDED" $L
