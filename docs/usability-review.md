# Native usability review and revision

Date: 2026-09-30. The first CachyOS/GNOME user trial exposed release-blocking usability defects that domain and headless tests had missed. The earlier acceptance percentage overstated desktop readiness. This revision addresses the reported interactions; it is not an accepted v1 release.

## Changes and evidence

| Area | Status | Change and verification |
| --- | --- | --- |
| Form activation | PASS on Ubuntu GNOME; BLOCKED on CachyOS retest | Application forms now stay inside the owning window. Calendar/search popups also use the X11 overlay. A native GNOME 46 check opened, typed into and cancelled New trip, then opened Settings. `_NET_ACTIVE_WINDOW` remained the same Jourfold window throughout. No repeated native form windows are created. |
| Keyboard focus | PASS for tested flows | Forms receive focus after layout, trap tab navigation, support Escape, and restore focus only while the owner is active. Headless keyboard regression passes. Full screen-reader/keyboard audit remains a separate release gate. |
| Inbox click | PASS | Clicking selects; drag starts after pointer movement exceeds the threshold. Real headless pointer input covers selection without starting a drag. |
| Application icon | PASS for packaging/registration; BLOCKED on CachyOS overview retest | Window icon is set, WM_CLASS is Jourfold, and `--install-desktop` registers a user launcher and icon for portable Linux builds. Packaged executable registration was exercised in an isolated data directory. |
| Dates and times | PASS | Calendar and time pickers edit both endpoints in one form. Timezones are searchable valid choices. Invalid ranges stay in the form; DST gaps are explained and repeated times offer offsets. Tests cover overnight ranges and preserving a day-part item's own timezone. |
| Other structured values | PASS for revised editors | Language, timezone and currency use searchable choices. Amounts, coordinates, distance and duration use numeric controls. Generated timestamps are read-only. Existing schema/domain validation remains authoritative. |
| Initial participants | PASS | New trip starts with an empty list. Add participant opens a name form with Save/Cancel; removal is explicit. Headless test verifies cancellation and creation. |
| Assigned participants | PASS | Available and assigned lists have explicit transfer buttons and Save/Cancel. Editing writes the complete selection as one undoable operation. |
| Settings | PASS | Category navigation on the left, settings on the right, immediate preference changes. Language labels read English/Deutsch; derived sharing labels refresh too. |
| Trip location | PASS | New trip asks for an empty destination folder. It no longer forces the Documents/Jourfold location. Existing trips remain where they are. |
| Save as | PASS | Copies local edits, untracked files, attachments, history, variants, remotes and local Git configuration; opens the independent copy. Real Git tests verify preservation and destination/link safeguards. Linked worktrees and shared object stores need a standalone clone first. |
| Contextual creation | PASS for local/headless checks | Content views have Add actions. Plan has empty-slot Add and item Delete context actions, plus drag-to-create with the selected time span prefilled. Save creates one undoable activity. |
| Zoom | PASS for implementation/render checks | Explicit minus/plus controls and percentage choices replace cycling. Calendar navigation has a date picker. Stale events from replaced controls cannot reset the current view. |
| Lists, labels, tooltips | PASS for implementation/render checks | List surfaces follow the application theme. Navigation is less heavy, activity blocks show time ranges, common buttons have tooltips, and “Add myself to this trip” replaces the ambiguous identity action. |
| Reference-level visual polish | NOT COMPLETE | Navigation, lists and forms are more coherent, but the full visual richness and refinement of the concept board have not been matched. Further design iteration remains. |

## Verification

- Both repositories: deleted source/test/sample bin and obj directories, locked restore, Release build, formatting verification, tests, notices and dependency audit.
- TravelRepo: **37 tests passed**, including repository-copy integrity and rejection of incomplete/unsafe copies; nine NuGet packages rebuilt.
- Jourfold: the clean run passed 40 tests. A final full run passed **41 tests**, adding day-part timezone preservation and isolating shared view preferences in the UI fixtures.
- Linux tar.gz, Debian package and Windows portable ZIP rebuilt. Source dependency lock files remained unchanged, and post-package locked restores passed.
- Packaged Linux `--install-desktop` invocation and expected launcher/icon files passed in an isolated directory.
- Actual UI captures refreshed and inspected in both themes, including date/time and settings forms. See [screenshots](screenshots.md).
- Native Ubuntu GNOME focus observations are recorded with the build evidence. This was an X11 GNOME 46 session; it does not verify CachyOS/GNOME Wayland, taskbar pinning on that machine, or Windows behavior.

Build evidence is in `artifacts/verification/2026-09-30-usability/` and package checksums in `artifacts/release-artifacts.json`. Build outputs remain ignored by Git. The [acceptance matrix](acceptance.md) retains the platform, accessibility, collaborator and hosted CI release gates.

## Retest on CachyOS

Copy the rebuilt tar archive, extract into a fresh application directory and run `Jourfold.Desktop --install-desktop` from that directory. Launch Jourfold from GNOME's application grid. Existing trip folders and app settings do not need to be moved or deleted.

First verify that ordinary clicks, opening/cancelling forms, calendar/time popups and selecting Inbox items keep the app in front. Then create a disposable trip in a chosen folder, schedule an activity without typing a date format, assign participants, and exercise Save as. Check the copied trip's current edits and history. Record GNOME version, X11/Wayland, scaling and any remaining problem. The [Linux guide](try-linux.md) has setup details.
