# Screenshots

These images are captures of the real `App` and `MainWindow`, rendered by Avalonia Headless with Skia. They have no operating-system window frame and do not show native file dialogs, popups, desktop integration or screen-reader behaviour.

The capture tool creates the same sample trip that **Explore a sample trip** offers in the application ("Spring in Japan", fixed to May 2027 for repeatable images) and an empty "Weekend in Aachen" trip. Place names are real; times, bookings, references and prices are invented. Everything is written to a temporary directory with its own settings and Git configuration and removed afterwards. No network service is contacted; the map shows the offline view with its opt-in prompt.

## Regenerate

From the Jourfold repository, with .NET SDK 10.0.401, Git 2.34 or newer and the sibling TravelRepo checkout:

```sh
dotnet restore eng/Screenshots/Screenshots.csproj --locked-mode
dotnet run --project eng/Screenshots -c Release --no-restore -- docs/screenshots
```

This writes the nine images below at 1440 × 900 pixels. Two options help when checking changes:

- `--all` adds every main view, dialogs (palette, schedule editor, share, Create Version, new trip, the open-source notices, Connect an AI assistant), variants with comparison and conflict resolution, plus `itinerary.pdf` and `itinerary.html`. Use it with a scratch directory, not `docs/screenshots`.
- `--lang de` captures the German interface.

Open the images and check text, clipping, theme and selection before committing them. Do not edit or retouch captures.

## Captures

The timetable with the Inbox and the inspector, in light and dark themes:

![Timetable for a sample trip to Japan with the Inbox open and an activity selected](screenshots/planning-light.png)

![The same view in the dark theme](screenshots/planning-dark.png)

The trip library:

![Library with a sample trip, an empty trip and a card for creating a new one](screenshots/library.png)

The day-by-day list:

![List view grouped by day with times, places, people and status](screenshots/list.png)

The map before online maps are enabled. Places and routes come from the trip's own coordinates:

![Offline map with places, train and flight routes and the prompt to show a street map](screenshots/map.png)

Costs per currency with budgets:

![Costs page with totals for euros and yen, budget progress and the list of expenses](screenshots/costs.png)

Quick Add:

![Quick Add sheet with item types, title, place, date, time, duration and people](screenshots/quick-add.png)

Settings:

![Settings dialog with categories on the left and general preferences on the right](screenshots/settings.png)

About Jourfold, with the example plugin loaded:

![About window with the Jourfold logo, version and build, license and source sections for Jourfold and TravelRepo, the loaded example plugin and the open-source notices](screenshots/about.png)
