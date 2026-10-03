# Jourfold

A local travel planner built with Avalonia and the TravelRepo SDK. GPL-3.0-or-later. Trip files remain ordinary Git repositories and can be used independently of Jourfold.

## Screenshots

The planning view with a synthetic weekend itinerary, unscheduled ideas in the Inbox, and the selected activity's details.

![Jourfold in Light theme: a three-day Aachen timetable, Inbox and activity inspector](docs/screenshots/planning-light.png)

The same itinerary in Dark theme.

![Jourfold in Dark theme showing the same timetable, Inbox and activity inspector](docs/screenshots/planning-dark.png)

[Date/time editing and settings screenshots](docs/screenshots.md#forms).

Captured from the actual application with Avalonia's headless renderer. [Refresh the screenshots](docs/screenshots.md).

## Try the Linux build

The portable archive includes the .NET runtime. See [testing on another Linux PC](docs/try-linux.md) for transfer, CachyOS/GNOME setup and a first test. GitHub is optional for local planning.

## Development

Requires .NET SDK 10.0.401 and Git for development. Place TravelRepo at `../travelrepo` for workspace builds.

```sh
dotnet build Jourfold.sln
dotnet test Jourfold.sln
dotnet run --project src/Jourfold.Desktop
```

See the [user guide](docs/user-guide.md), [build and test instructions](docs/development.md), [release smoke checklist](docs/release-smoke.md), [provider setup](docs/providers.md), and [plugin API](docs/plugins.md).

This is an implementation build, not an accepted v1 release. [Acceptance status](docs/acceptance.md) records verification and remaining work. No analytics or automatic uploads are included.
