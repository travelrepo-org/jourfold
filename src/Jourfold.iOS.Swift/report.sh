#!/bin/zsh
# Prints what the app on the phone did, as counts and states only (TestReport.swift): DEVICE=… ./report.sh
perl -e 'alarm 90; exec @ARGV' xcrun devicectl device process launch --console --device $DEVICE --terminate-existing ${BUNDLE:-dev.jourfold.app.preview} -- -testlog on -testreport YES 2>&1 | grep REPORT
xcrun devicectl device process launch --device $DEVICE --terminate-existing ${BUNDLE:-dev.jourfold.app.preview} 2>&1 | tail -1
