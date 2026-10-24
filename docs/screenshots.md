# Screenshots

These images are captures of the real `App` and `MainWindow`, rendered by Avalonia Headless with Skia. They have no operating-system window frame and do not show native file dialogs, popups, desktop integration or screen-reader behaviour.

The capture tool creates the same sample trip that **Explore a sample trip** offers in the application ("Spring in Japan", fixed to May 2027 for repeatable images) and an empty "Weekend in Aachen" trip. Place names are real; times, bookings, references and prices are invented. Everything is written to a temporary directory with its own settings and Git configuration and removed afterwards. No network service is contacted; the map shows the offline view with its opt-in prompt, and the weather comes from a built-in synthetic service with invented values, seen the evening before the plan's first day.

## Regenerate

From the Jourfold repository, with .NET SDK 10.0.401, Git 2.34 or newer and the sibling TravelRepo checkout:

```sh
dotnet restore eng/Screenshots/Screenshots.csproj --locked-mode
dotnet run --project eng/Screenshots -c Release --no-restore -- docs/screenshots
```

This writes the fourteen images below, at 1440 × 900 pixels except the document window at 1100 × 820. Two options help when checking changes:

- `--all` adds every main view, dialogs (palette, schedule editor, share, Create Version, new trip, the open-source notices, Connect an AI assistant, the export dialog for a plan and for packing lists, deleting a linked stay, a Git problem with its fix, accommodation details and the reminders, weather settings, What's new and the version history), variants with comparison and conflict resolution, plus `itinerary.pdf`, `itinerary-full.pdf` (every part and the map), `packing-lists.pdf` and `itinerary.html`. Use it with a scratch directory, not `docs/screenshots`.
- `--lang de` captures the German interface.

Open the images and check text, clipping, theme and selection before committing them. Do not edit or retouch captures.

## Captures

The timetable with the Inbox and the inspector, in light and dark themes. Stays are the band of nights at the bottom:

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

A packing list with groups, who brings what, what each person has packed, and one item nobody has taken yet:

![Packing view for Japan with the list chip, filters for all, mine and unassigned, items grouped into documents, electronics, clothes and food, and chips for Alex and Sam with check marks where they have packed](screenshots/packing.png)

A booking with its confirmation e-mail attached, and the drop area for more:

![Bookings page with a ryokan booking selected; the inspector lists the attached confirmation e-mail under Confirmations and files](screenshots/booking-confirmation.png)

The document window with the confirmation e-mail and with its PDF attachment, searched for a time:

![Document window showing the e-mail's sender, date and attached PDF, a note that one remote image was not loaded with a button to load it, and the message in its original layout](screenshots/viewer-mail.png)

![Document window showing the attached PDF confirmation with the search result for 15:00 highlighted](screenshots/viewer-pdf.png)

The weather of an outdoor activity with rain in the forecast, and the day forecasts above the timetable:

![Timetable with weather icons and temperatures in each day header; the inspector shows Kegon Falls with the outdoor estimate, an hourly forecast, a rain warning, sunrise and sunset and the data source](screenshots/weather.png)

Quick Add:

![Quick Add sheet with item types, title, place, date, time, duration and people](screenshots/quick-add.png)

Settings:

![Settings dialog with categories on the left and general preferences on the right](screenshots/settings.png)

About Jourfold, with the example plugin loaded:

![About window with the Jourfold logo, version and build, license and source sections for Jourfold and TravelRepo, the loaded example plugin and the open-source notices](screenshots/about.png)
