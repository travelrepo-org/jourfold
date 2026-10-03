# Jourfold Desktop UX Specification v0.2

Status: Draft  
Primary platforms: Windows 11 and Linux desktop  
Reference design language: Jourfold UI/UX board selected during product design

## 1. Product character

Jourfold is the reference desktop client for TravelRepo.

The product should feel:
- calm,
- modern,
- information-dense without being cramped,
- travel-oriented rather than developer-oriented,
- native in behavior while still recognizably Jourfold across platforms.

Avoid:
- generic AI/SaaS visual language,
- heavy gradients,
- glassmorphism,
- excessive shadows,
- terminal/Git visual metaphors in the default experience,
- mobile UI simply scaled up to desktop.

## 2. Brand and typography

Product name: Jourfold

Brand mark:
- landscape/route mark forming a clear `J`,
- canonical source is SVG,
- compact icon variant must work at small sizes.

Wordmark:
- based on Outfit,
- customized and converted to SVG paths,
- no runtime dependency on Outfit.

UI font:
- Plus Jakarta Sans,
- bundled locally,
- slightly tighter tracking for headings, tabs, buttons, and compact metadata,
- normal tracking for body and very small text.

Suggested typography:
- Display/H1: 700, -0.02em
- H2: 600, -0.015em
- Body: 400, normal tracking
- Body emphasis: 500
- Buttons: 600, -0.01em
- Tabs/labels: 500, -0.01em
- Metadata: 400, normal tracking

Themes:
- Light
- Dark
- System

Dark mode:
- deep navy foundation,
- not generic neutral black.

Density:
- Comfortable default
- Compact optional

## 3. Application shell

Wide desktop layout:

```text
Navigation sidebar | Main workspace | Detail inspector
```

The right inspector is the default detail surface for timetable-oriented views.

In spatial views such as Map, the inspector may appear as:
- overlay,
- floating card,
- docked panel,
depending on available space.

The inspector may be detachable into a popout where useful.

## 4. Start screen / trip library

Jourfold starts with a local trip library.

Primary sections:
- Recent
- Local trips
- Remote-backed trips
- Optional provider sections such as GitHub

Trip card/list metadata may include:
- title,
- optional cover image,
- effective trip dates,
- location summary,
- last opened,
- last changed,
- sync state,
- remote/provider,
- recent variant activity.

For remote-backed trips, useful activity can include:
- last remote update,
- last newly discovered variant,
- pending remote changes.

Recent is prominent.

The local library is derived application state and is not canonical TravelRepo data.

### 4.1 Trip cover

A trip may define an optional cover image in `travel.yaml` through a reference to a TravelRepo document.

The cover is canonical trip data and may therefore differ between variants.

Jourfold uses the cover in places such as:
- trip library cards,
- Recent,
- trip overview surfaces,
- share/export presentation where appropriate.

If no cover exists, Jourfold uses a restrained generated fallback rather than requiring one.

Per-trip accent colors are not a v1 feature. Jourfold keeps a consistent application palette. A client may derive non-canonical presentation hints from a cover image locally.

## 5. Creating a trip

`New Trip` launches a minimal, skippable wizard.

Minimal fields:
- title

Optional wizard fields:
- content language,
- approximate or fixed dates,
- default timezone,
- initial participants,
- remote/publishing choice.

The user may skip all optional steps and configure them later.

A new trip is always created as a valid local Git repository.

## 6. Opening view

The trip opening view is a local user preference, not repository data.

Options:
- Remember last used
- Timetable
- Map
- List

Default:
- Remember last used

Jourfold may remember temporary per-trip/per-window view state locally without committing it.

## 7. Navigation

Primary sidebar direction:

- Plan
- Map
- Bookings
- Costs
- Tasks
- Files
- People
- History
- Variants

Inbox has a prominent dedicated control and may also appear in navigation.

Advanced Git-specific views are hidden by default.

## 8. Main planning view

Jourfold's primary schedule view is a timetable, not a generic calendar clone.

Default vertical timetable:
- time on the vertical axis,
- days as columns when space allows,
- adaptive visible-day count,
- zoom from trip overview down to minute-level planning,
- clear visual handling of timezone transitions,
- nested/grouped blocks,
- drag-and-drop scheduling,
- duration resize.

Alternative schedule-oriented views:
- horizontal journey timeline,
- participant lanes,
- compact list.

## 9. Timetable blocks

Blocks are visually compact but rich enough to communicate type and state.

Typical visible information:
- title,
- time,
- type icon,
- place,
- participant avatars/initials where useful,
- status,
- warnings,
- booking state,
- optional route/travel indicator.

Transport blocks may display:
- origin/destination,
- departure local time,
- arrival local time,
- timezone transition,
- duration.

Exact visual density adapts to zoom level and available space.

## 10. Block editing

Support both:
- direct lightweight editing in the timetable,
- full editing in the detail inspector.

Inline editing is appropriate for:
- title,
- basic time,
- duration,
- quick participant assignment,
- simple status changes.

Inspector handles:
- structured components,
- bookings,
- documents,
- comments,
- costs,
- constraints,
- routing/provider data,
- advanced fields.

## 11. Drag, resize, and constraints

Dragging a block changes its planned time.

Resizing changes its duration where the item supports duration.

Jourfold recalculates:
- participant overlaps,
- travel gaps,
- temporal constraints,
- nested parent effects,
- route-dependent warnings where cached/provider data exists.

User intent wins.

Warnings do not prevent a move unless the repository would become structurally invalid.

## 12. Flexible and unscheduled planning

Unscheduled items may contain:
- expected duration,
- preferred day part,
- participants,
- place,
- earliest/latest time,
- dependencies,
- other constraints.

These items remain useful before being placed on the timetable.

Dragging an unscheduled item into the timetable materializes an exact or more specific planned time without discarding its relevant constraints.

## 13. Inbox

Inbox is a prominent collapsible panel.

Default interaction:
- hidden/collapsed when not needed,
- obvious Inbox button,
- can be temporarily expanded,
- can be pinned.

When pinned, it behaves as a persistent workspace panel.

Inbox may contain:
- unscheduled activities,
- notes,
- links,
- documents,
- imported booking material,
- tasks,
- other unclassified items.

Dragging items from Inbox into views is a primary workflow.

## 14. Participants

People are trip-local entities.

Jourfold may display:
- uploaded trip-local avatar,
- avatar imported into the repo from an external identity,
- generated initials,
- no avatar.

No central Jourfold account exists.

A local app identity profile can be used as a template when creating the current user's person entity in a trip.

## 15. Participant timetable mode

Participant grouping uses one lane per person, plus optional Unassigned.

Do not create lanes for participant combinations such as `Alex + Carla`.

A shared item:
- may visually span adjacent participant lanes,
- or may render linked synchronized representations.

There is exactly one underlying schedule item.

Blocks may have:
- no participants,
- one participant,
- multiple participants.

Overlaps are allowed and may produce warnings.

## 16. Map

Map is a first-class main view.

Bidirectional selection:
- selecting a schedule item highlights its mapped place/route,
- selecting a place/route selects or filters related schedule items,
- the shared selection updates the inspector.

Map may show:
- places,
- routes,
- transport legs,
- accommodation,
- activities,
- warnings,
- selected variant context.

Map/provider functionality must degrade gracefully when offline.

## 17. View switching

Primary trip views should be easy to switch without losing context.

Recommended top-level view control:
- Timetable
- Map
- List

Calendar-like alternatives may exist inside Plan rather than becoming an unrelated top-level section.

The exact control may be tabs or a segmented control, depending on platform and available width.

Switching view preserves:
- current date/range where meaningful,
- selected entity,
- current variant,
- filters where meaningful.

## 18. Variants

The current variant is visible near the trip title.

Default presentation:

```text
Current Plan ▾
```

The user-facing variant title is primary.

The real Git branch name is displayed as secondary subdued technical text.

Advanced Mode makes Git naming and refs more prominent.

Creating a variant is available from:
- current variant menu,
- selected schedule range/context,
- history/version points where technically valid.

Variants may be nested.

Merged variants are archived by default rather than deleted.

## 19. Variant comparison

Comparison is semantic and view-specific.

Timetable:
- overlay where useful,
- side-by-side where width permits,
- changed/added/removed blocks visually differentiated.

Map:
- overlay routes/places where meaningful.

Structured entity/detail views:
- side-by-side field comparison.

Comparison must never be reduced to raw YAML or Git diffs in normal mode.

## 20. Variant merge

Merge is entity/component aware.

Conflict UI uses domain language.

Examples:
- Use Current
- Use Variant
- Keep Both
- Edit Result

Binary media:
- choose ours,
- choose theirs,
- keep both.

No normal-user conflict-marker editing.

Conflict-free semantic merge:
- small non-modal notice,
- short grace period,
- Review and Cancel actions,
- automatic apply after the grace period,
- visible entry in History.

## 21. History

Default History is a human-readable activity/version timeline.

Examples:
- Alex changed the Tokyo hotel.
- Carla added a train to Kyoto.
- Jourfold merged `Cheaper flights`.
- External Git change by Alex.

Each entry may expand to show:
- version message,
- changed entities,
- author mapping,
- exact timestamp,
- provider/sync context.

Advanced Mode additionally shows:
- commit hash,
- parent commits,
- branch/ref,
- raw Git author/committer,
- trailers.

## 22. Create Version

There is no normal Save button.

Autosave status is subtle:
- Saved locally
- Unsaved local write in progress
- Local changes

Primary explicit version action:
- Create Version

Dialog:
- automatically suggested human-readable version message,
- editable message,
- semantic summary of changes,
- optional expanded detail,
- create action.

Application-generated commit trailers remain independent of the visible message.

## 23. Sync status

Default mode uses human language:
- Synced
- Local changes
- Syncing
- Remote changes available
- Needs attention

Advanced Mode adds Git meaning:
- working tree dirty,
- ahead/behind counts,
- branch,
- remote,
- fetch/push state,
- commit IDs where useful.

No destructive automatic Git behavior.

## 24. Share

The visible primary action adapts to provider capability.

Examples:
- GitHub repository: Invite or Manage access
- Generic remote: Share
- Local-only trip: Publish
- Selected variant: Share variant

GitHub may:
- invite collaborators,
- search users,
- inspect privacy,
- manage access where permitted.

Generic Git may:
- copy clone URL,
- copy HTTPS/SSH URL,
- copy setup instructions,
- copy/open Jourfold deep link,
- optionally show QR code later.

Sharing a generic Git URL never implies that repository access has been granted.

Local-only trip may offer:
- Add remote
- Publish to GitHub
- Export archive

## 25. Files

Files/Documents is a first-class view.

It presents deduplicated document entities rather than raw blob storage.

Features:
- preview,
- search,
- related entities,
- tags,
- duplicate/reference count,
- add/replace,
- open externally,
- show technical blob details in Advanced Mode.

## 26. Search and command palette

`Ctrl+K` opens a combined command/search palette.

It can:
- search trip content,
- navigate,
- run commands,
- create entities,
- switch views,
- switch variants,
- find people/places/bookings/files,
- expose advanced commands when Advanced Mode is enabled.

## 27. Quick add

Quick Add is keyboard-accessible and context aware.

Initial common entries:
- Activity
- Transport
- Accommodation
- Task
- Note
- Booking
- Place
- Person
- Expense
- Collection

Transport then chooses:
- Flight
- Train
- Bus
- Car
- Taxi/Rideshare
- Ferry/Ship
- Bicycle
- Walking
- Other

## 28. Warnings and errors

Severity:
- Info
- Warning
- Blocking error

Travel quality and feasibility concerns are usually warnings.

Examples:
- insufficient travel gap,
- overlapping participant,
- missing booking data,
- unavailable route cache,
- public repository warning.

Blocking errors are reserved for repository integrity or unsafe operations.

Warnings are visible but should not create constant modal interruption.

## 29. Public repository safety

For GitHub or providers that expose visibility:

Private is the default for newly published trips.

Opening or publishing a public trip triggers a prominent warning explaining that travel repositories may contain sensitive:
- schedules,
- names,
- booking references,
- documents,
- photographs,
- accommodation details.

Third-party clients cannot be forced to follow this rule, but Jourfold and reusable provider libraries should expose the privacy state and safe defaults.

## 30. Advanced Mode

Advanced Mode exposes technical details without changing core data semantics.

Possible additions:
- branch names,
- refs,
- commit hashes,
- remotes,
- repository path,
- schema version,
- Git operation status,
- raw validation detail,
- plugin/provider diagnostics,
- custom entity/component editors,
- direct repository maintenance tools.

Advanced Mode must not be required for ordinary travel planning.

## 31. Platform behavior

Jourfold has one design language but uses native conventions.

Windows:
- Windows 11 window behavior,
- platform file pickers,
- sensible titlebar integration,
- keyboard conventions,
- taskbar integration.

Linux:
- GNOME and KDE are first-class supported environments,
- system scaling and themes respected,
- portal/native file pickers where appropriate,
- no fake GTK or Qt skin,
- keyboard conventions remain natural.

## 32. Keyboard and accessibility

Desktop is mouse/keyboard first.

Required:
- full keyboard navigation,
- visible focus,
- logical tab order,
- command palette,
- common shortcuts,
- screen reader semantics,
- high-contrast compatibility,
- reduced-motion behavior,
- text scaling.

Shortcut architecture uses commands so bindings can later be customized.

## 33. Localization

Source language: English.

v1 ships:
- English
- German

All UI strings are localizable resources.

Trip content is not automatically translated.

## 34. Application settings

Likely categories:

### General
- language
- default opening view
- density
- theme
- date/time display
- units display

### Identity
- local default travel identity
- Git identity suggestions

### Git
- detected Git executable
- Git backend diagnostics
- synchronization preferences
- credential behavior

### Providers
- GitHub accounts/access modes
- future providers

### Plugins
- installed plugins
- enable/disable
- permissions/declarations
- diagnostics

### Advanced
- Advanced Mode
- repository/debug tooling
- schema diagnostics

## 35. User settings vs trip data

Must remain local user settings:
- default opening view,
- UI density,
- theme,
- locale,
- display units,
- date/time formatting,
- window layout,
- preferred Git executable,
- preferred share remote,
- local identity defaults.

Must remain trip data:
- participants,
- places,
- schedule,
- documents,
- bookings,
- tasks,
- costs,
- variants,
- comments,
- semantic metadata.

## 36. UI autonomy rule

Jourfold should make sensible defaults and avoid asking configuration questions simply because options exist.

Preference dialogs are for users who care.

A normal user should be able to:
- create a trip,
- add items,
- create a version,
- create a variant,
- sync/share,
without configuring the application first.


## 37. Undo and redo

Local undo/redo is a v1 requirement.

Undo/redo is separate from Git history.

Requirements:
- ordinary domain edits participate in the undo stack,
- undo must work across normal multi-field edits where the action is logically one operation,
- redo is available after undo until invalidated by a new edit,
- destructive actions should remain recoverable locally until a version is created where practical,
- creating a Git version does not erase the user's ability to inspect history, but the local undo stack may establish a sensible checkpoint,
- remote synchronization or external filesystem changes may invalidate local undo entries; Jourfold must explain this rather than applying unsafe inverse operations.

Undo/redo uses domain commands or reversible application operations rather than attempting to reverse arbitrary Git commits.

## 38. Multiple windows and repository write ownership

Jourfold v1 supports multiple application windows.

Different trips may be open and editable in different windows simultaneously.

The same repository may have only one Jourfold write-owning workspace per application instance.

If the user attempts to open the same trip again:
- focus the existing writable window by default,
- optionally allow an additional read-only view if useful,
- never create two independent Jourfold writers for the same working tree in v1.

External programs may still modify the repository. Jourfold detects such changes through its normal external-change handling.

This limitation is a v1 client concurrency rule, not a TravelRepo format restriction.
