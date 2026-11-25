#!/bin/zsh
# Captures the views of the app with the sample trip, for a pull request: run ./shoot.sh build first, or on an iPad
# SIM=<id> PREFIX=pad- ./shots.sh. The images land in screenshots/, which is not in Git (docs/screenshots.md).
cd "${0:A:h}"; export WAIT=6
shot() { local n=$1 t=$2 l=$3; shift 3; ./shoot.sh $PREFIX$n-$t-$l $t $l -demo YES -reset YES -samples YES "$@"; }
for t l in light en dark de; do
  shot trips $t $l
  shot today $t $l -trip 0
  shot plan $t $l -trip 0 -tab plan -day 2
  shot timetable $t $l -trip 0 -timetable YES -day 2
  shot details $t $l -trip 0 -sheet detail -item "Shinkansen to Kyoto"
  shot add $t $l -trip 0 -sheet quickadd
  shot map $t $l -trip 0 -page map
  shot more $t $l -trip 0 -tab more
  for p in costs bookings packing tasks places files travellers sync settings; do shot $p $t $l -trip 0 -page $p; done
done
