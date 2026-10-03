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

Test English and German layouts for important screens, including long labels and place names such as Aachen and New Zealand examples.

Normal automated tests and CI must run without a contributor's GitHub login, OS keyring, or personal smoke repository. Live provider validation is a separate, explicitly configured workflow with disposable data. Never commit credentials or copy a developer's local authentication into CI.

## README

Keep the README concise and factual.

Update it when build or packaging instructions become real. Do not turn it into release marketing copy.

Frontend changes that affect the displayed interface must refresh the affected README screenshots in the same change. Use the capture command in `docs/screenshots.md`, which renders the actual application with synthetic local trip data. Add captures when an important new view needs illustration. Keep captions and alt text accurate.

Inspect the resulting Light and Dark images for clipped content, stale state and accidental private information. Do not substitute mockups or retouched UI for actual application captures. If capture is blocked, record the exact blocker and identify which images are stale.
