#!/bin/zsh
# Builds, runs the self-test in the simulator and prints the failures and the count.
cd "${0:A:h}"; [[ -f .sim ]] && . ./.sim; : ${SIM:?the id of a simulator, see README.md}; export SIM; L=${TMPDIR:-/tmp}/jourfold-selftest.log
./shoot.sh build || exit 1; rm -f $L; (xcrun simctl launch --console-pty --terminate-running-process $SIM dev.jourfold.app.preview -selftest YES -demo YES > $L 2>&1 &)
for i in $(seq 1 300); do grep -q "self-test done" $L 2>/dev/null && break; perl -e 'select undef,undef,undef,2'; done
echo "passed: $(grep -cE '^PASS' $L)"; grep -E "FAIL|self-test done" $L | cut -c1-300
grep -q "self-test done, 0 failures" $L
