# Release smoke validation

Run the automated `Jourfold.Tests` suite in Release. `ReleaseDomainWorkflow` creates an offline trip, participants, a place, a timezone-crossing flight, a flexible activity, an image, a task and a booking. It versions, branches, edits, semantically merges, archives, pushes and fetches through a real bare remote, rebuilds search and exports PDF/ICS. `ShellLoadsAndTimetableDropWritesCanonicalTime` exercises actual timetable drop, move and resize handlers with Avalonia headless input. `PluginHostUsesVersionedRpcOutOfProcess` runs a separate host process.

The scripted tests are evidence for the operations they exercise. They do not substitute for the following native release checks.

## Windows 11 x64

Install the generated installer on a clean machine without Git. Confirm bundled MinGit resolution. Repeat with compatible Git installed. Create a trip, add/edit every entity type, use keyboard-only navigation, drag and resize schedule blocks, attach and open image/PDF files, create a version and variant, compare and merge. Export PDF/ICS and open both with independent readers. Reopen offline. Check 100%, 150% and 200% scaling, Light/Dark/System, English/German, screen reader navigation and multiple trip windows.

## GNOME and KDE

Install the Debian package or extract the tar archive with Git installed. Repeat the same workflow on both environments. Check system file pickers, titlebar controls, keyboard focus, theme changes, scaling, credentials with a Secret Service provider, and session-only credentials when no secure store is available. X11/XWayland is the release baseline.

## GitHub App

Register a GitHub App and enable device flow. Set the public client ID in Jourfold Settings when connecting. Grant metadata, contents write and administration permissions appropriate to repository creation and collaborator management. Install the app for selected test repositories. Complete device authorization, discover a fixture by its `travel.yaml`, publish a disposable private test trip, verify the returned privacy state, and invite a consenting test collaborator. Verify the public warning using an intentionally public non-sensitive fixture. Remove disposable test resources after review.

No live provider operation should use real itinerary or booking data for release testing. The automated provider tests use HTTP test doubles and never claim live-service verification.

Record OS/version, artifact hash, Git version, desktop environment, display scaling, commands and observed failures in the acceptance matrix before release.

## Recorded local validation

The [verification record](verification.md) contains the 2026-09-29 clean builds, test results, package checksums, native GNOME/KDE checks and their limits. The [2026-09-30 validation report](live-github-validation.md) records the subsequent clean builds and live GitHub checks. The full native checklist and collaborator invitation remain pending. For a first run on another Linux PC, use [the portable-build guide](try-linux.md).
