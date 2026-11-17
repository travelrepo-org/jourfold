# Jourfold repository instructions

These rules apply to the Jourfold repository in addition to the workspace-level `AGENTS.md`.

## Boundary

Jourfold is a client of TravelRepo.

Do not duplicate TravelRepo domain rules in:
- view models,
- converters,
- views,
- local database code,
- provider-specific UI.

If the rule describes canonical trip data, validation, semantic comparison, merging, Git behavior, or provider capability, it probably belongs in TravelRepo.

## UI architecture

Use Avalonia and CommunityToolkit.Mvvm as specified.

Keep views thin.

View models orchestrate application commands and presentation state. They do not become an alternate domain model.

Use a shared design system for:
- typography,
- spacing,
- radii,
- borders,
- color tokens,
- density,
- motion,
- focus states.

## Visual direction

Follow `../../brand/BRAND.md`.

The selected UI reference is a design-language reference, not a pixel-perfect blueprint.

Do not drift into:
- generic AI startup styling,
- excessive gradients,
- glassmorphism,
- oversized mobile controls on desktop,
- heavy card shadows everywhere.

Jourfold should stay compact, calm, and travel-oriented.

## Typography

Use Plus Jakarta Sans for the application UI.

The Jourfold wordmark is an SVG asset based on Outfit and does not rely on runtime font rendering.

Do not hard-code arbitrary font substitutions in individual views.

## Localization

No normal user-facing string should be hard-coded into XAML or code.

English is the source language.

German ships as the first complete second localization.

Trip content is not automatically translated.

## Accessibility

Keyboard access and screen-reader semantics are release requirements.

Do not ship controls that only work with a mouse.

Do not communicate warnings or state using color alone.

Honor scaling and reduced-motion behavior where available.

## Platform behavior

Windows and Linux are first-class targets.

Support both GNOME and KDE environments through correct desktop integration and standards rather than fake toolkit skins.

Keep platform-specific behavior behind services.

## Local state

Canonical travel data never belongs in Jourfold's local SQLite database.

Local storage may contain:
- Recent trips,
- caches,
- search indexes,
- thumbnails,
- settings,
- recovery state,
- provider metadata that is not secret.

Deleting local app state must not destroy trip content.

## Accounts and people

Do not invent a central Jourfold user account.

TravelRepo people are trip-local.

Jourfold may maintain a local identity template and map or instantiate it per trip.

## Privacy

v1 contains no behavioral analytics or automatic crash upload.

Never send travel data, logs, or diagnostics without an explicit user action.

The issue-report flow must show the user what will leave the device.

## Example data

Agents: read this file again before each task; it changes.

No real travel data ever appears in tests, fixtures, samples, examples, screenshots, comments, code, documentation, pull requests, reviews, commit messages or release notes. That covers real trips, people, places someone went to, bookings, amounts, card charges and dates.

Examples use only these places: the cities below and the places in and around them, such as their stations, airports, hotels, sights and day trips close by.

| Country | Cities | Time zone |
| --- | --- | --- |
| Germany | Hamburg, Berlin, Frankfurt | Europe/Berlin |
| France | Paris, Lyon, Nice | Europe/Paris |
| Japan | Tokyo, Kyoto, Osaka, Nikko, Nara | Asia/Tokyo |
| New Zealand | Auckland, Christchurch, Queenstown, Picton, Wellington | Pacific/Auckland |
| Canada | Montreal, Toronto, Ottawa | America/Toronto |
| Canada | Vancouver | America/Vancouver |
| Canada | St. John's | America/St_Johns |

Together they cover the cases that need a place: daylight saving time in Germany, France and Canada, the opposite season in New Zealand and none in Japan, with their ambiguous and invalid local times; the date line between New Zealand or Japan and Canada; several time zones in one country (Canada); an offset of half an hour (St. John's, UTC−3:30); and journeys by plane, train, car and ferry (Picton–Wellington for ferries). UTC, or an offset zone such as `Etc/GMT+3`, is fine where no place is meant, and so is a point that is not a destination, such as one near the pole for the midnight sun.

Currencies are EUR, JPY (no minor unit), NZD, CAD and USD (for example a booking paid online in dollars). KWD is used only where three decimals are needed, such as rounding tests. A test that needs more currencies than these, such as one that fills a list of recent currencies, uses ISO codes that belong to no country: XTS (reserved for testing), XXX, XAU and XAG.

People are made up: the sample trip's Alex and Sam, or other first names that belong to nobody in particular. Never a real person, account or e-mail address; e-mail addresses and web addresses use the reserved example domains (`example.com`, `example.org`, `.example`, `.invalid`).

- The default example is the sample trip "Spring in Japan" (Jourfold's `src/Jourfold.Application/SampleTrip.cs`): Alex and Sam fly from Frankfurt to Tokyo and travel on to Nikko, Kyoto, Nara and Osaka, paying in EUR and JPY. An edge case it does not cover is set on that trip where it fits, otherwise in the places above.
- When someone describes a problem or a feature with an example from a real trip, an agent translates it into the matching case in the places above before writing anything down. It keeps what matters about the example, such as an odd amount, a rounding case or a time zone change, and replaces the names, places, amounts and dates.
- This applies to existing tests, fixtures and documentation too: anything outside these places, currencies and people is moved into them when it is touched.

## Git UX

Normal users see:
- variants,
- versions,
- history,
- sync,
- share,
- publish.

Advanced Mode may expose:
- branches,
- refs,
- remotes,
- commit hashes,
- raw Git status.

Do not force normal users into Git terminology.

## Testing

Critical flows require headless UI or suitable integration tests.

Test both Light and Dark themes for key views where practical.

Test English and German layouts for important screens, including long labels and the sample trip's long place names, such as Arashiyama Bamboo Grove and Fushimi Inari Taisha.

Normal automated tests and CI must run without a contributor's GitHub login, OS keyring, or personal smoke repository. Live provider validation is a separate, explicitly configured workflow with disposable data. Never commit credentials or copy a developer's local authentication into CI.

## Pull requests that change the interface

A pull request that changes what the interface shows has before-and-after images in its description, so a reviewer can compare without building it.

- Show each affected view, for each platform it affects (desktop, phone, tablet), as it looks on the base branch and with the change.
- Keep everything else the same: the same synthetic trip and state, the same window or screen size, theme and language. When colours change, show Light and Dark.
- Use only synthetic data, such as the sample trip or test fixtures, never a person's real trip, names, e-mail addresses or accounts. This applies even when someone has shared real data to discuss a problem.
- Capture the real application, headless or on an emulator or device. Do not use mockups or retouched images.
- A new view has no "before"; say so and show the result. If capturing is blocked, name the blocker.
- The images go on the `pr-screenshots` branch, not into the change itself. `docs/screenshots.md` describes how.

These images are for review. The README screenshots below still follow their own rule.

## README

Keep the README concise and factual.

Update it when build or packaging instructions become real. Do not turn it into release marketing copy.

Frontend changes that affect the displayed interface must refresh the affected README screenshots in the same change. Use the capture command in `docs/screenshots.md`, which renders the actual application with synthetic local trip data. Add captures when an important new view needs illustration. Keep captions and alt text accurate.

Inspect the resulting Light and Dark images for clipped content, stale state and accidental private information. Do not substitute mockups or retouched UI for actual application captures. If capture is blocked, record the exact blocker and identify which images are stale.
