# Building Jourfold

Install .NET SDK 10.0.401, Git and Python 3. Check out TravelRepo into `../travelrepo` for local development. Jourfold references its public projects; when the sibling is absent, it resolves the TravelRepo packages named by `TravelRepoVersion` in `Directory.Build.props` from configured NuGet sources.

```sh
dotnet restore --locked-mode
dotnet build Jourfold.sln -c Release --no-restore
dotnet test Jourfold.sln -c Release --no-build
dotnet run --project src/Jourfold.Desktop
dotnet format Jourfold.sln --verify-no-changes --no-restore
python3 eng/notices.py
python3 eng/package.py --rid linux-x64     # also linux-arm64, win-x64, win-arm64
```

On Windows, compile the installer after publishing: `ISCC /DAppVersion=<version> [/DArch=arm64] eng/windows.iss`. ARM64 packages can be built on x64 machines. Packaging includes the self-contained runtime, fonts and notices, branding, plugin host, and pinned SHA-256-verified MinGit on Windows. Linux packages depend on Git; the tar archive expects compatible Git on PATH. Build/package outputs go to `artifacts/` and are ignored by Git.

Tests include application undo/redo, exclusive write ownership, external edits, a real repository smoke workflow, PDF/ICS export, SQLite search, plugin process RPC, and Avalonia headless input. Tests write only temporary repositories. They do not replace native GNOME, KDE and Windows validation; follow `release-smoke.md`.

## Versions

`Directory.Build.props` holds the only copy of the Jourfold version (`Version`), the TravelRepo package version it builds against (`TravelRepoVersion`) and the public `RepositoryUrl`. Assemblies, Linux packages, the Windows installer and the About window all read them from there. `python3 eng/version.py` prints the version, and `python3 eng/version.py --check-tag v1.2.3` fails unless the tag matches it and a TravelRepo checkout beside Jourfold has the expected version. The release workflow runs that check before building anything. Builds from a Git checkout add the source commit to the informational version, which the About window shows as the build.

To release, set `Version` on `main`, then publish a GitHub release with the tag `v<Version>`. The release workflow checks the tag, builds against the TravelRepo tag `v<TravelRepoVersion>` and attaches the packages; see [installing Jourfold](install.md#for-maintainers).

## Local state

`JOURFOLD_DATA_HOME` may redirect local settings, previews and search for development. Canonical data is always in the selected trip directory. Recovery journals live separately under the user's local application data. Deleting local search/settings does not delete trip content.

GitHub Actions requires the repository variable `TRAVELREPO_REPOSITORY` to point to the TravelRepo repository (`travelrepo-org/travelrepo` for the official repositories). Both repositories can run their own CI; neither requires a parent Git repository.

If the sibling TravelRepo source repository is private, its checkout also needs separately configured CI read access. The current workflow does not configure that cross-repository credential; the default Actions token is scoped to the current repository. This is a hosted CI setup gate, separate from authentication to trip repositories.

## Automated tests and live GitHub validation

The checked-in test suites use temporary Git repositories and mocked HTTP responses at the GitHub boundary. They do not require a GitHub login, an App token, an OS keyring entry or a personal smoke repository. Dependency restore and audit still need their configured package feeds.

The 2026-09-30 live GitHub probes were run separately from `.tools/` in the bootstrap workspace, outside both product repositories. They are not shipped with Jourfold and are not invoked by either CI workflow. The checked-in [validation report](live-github-validation.md) records their results; it is not an executable test.

To repeat live validation, follow [GitHub App setup](github-app-setup.md) and the [release smoke checklist](release-smoke.md) with a separately authorized account and disposable trip data. Each machine authorizes Jourfold independently. Any future automated live-service workflow needs explicit opt-in and dedicated credentials; it must not depend on a contributor's desktop session.

## Interface structure

`src/Jourfold.Desktop` is organised by role:

- `Theme/` holds the design system. `Tokens.axaml` defines colours for light and dark (deep navy) themes, radii and the per-category schedule colours. `Controls.axaml` styles Fluent controls and defines the button, text and surface classes used everywhere. High contrast overrides the same token keys at window level.
- `Theme/Icons.axaml` and `Theme/Brand.axaml` are generated. Icons come from [Lucide](https://lucide.dev) `lucide-static` 1.51.0 (ISC, see `licenses/lucide`): download the SVGs you need into a folder and run `python3 eng/icons.py <folder> src/Jourfold.Desktop/Theme/Icons.axaml`. Brand drawings are generated from the canonical SVGs with `python3 eng/brand.py assets/branding src/Jourfold.Desktop/Theme/Brand.axaml`.
- `Controls/` contains the `Icon` control, the `Ui` factory used by code-built views, and inline `Editors` for the inspector.
- `Views/` builds the shell, library, timetable, list, map, collection pages, history, variants and inspector from the view model. Views never pick colours directly; they reference tokens.
- `Dialogs/` implements `IInteraction` with in-window sheets. Tests replace it with `TestInteraction`.
- `MainViewModel*.cs` is split by concern: trips, editing, versions, sync and settings. Domain rules stay in TravelRepo; entity building for Quick Add and the sample trip live in `Jourfold.Application`.

Strings live in `Strings/en.json` and `Strings/de.json`. Both files must have the same keys; a test checks parity.

## UI screenshots

Run the standalone [screenshot capture tool](screenshots.md) after frontend changes. It uses a synthetic local trip and does not connect to GitHub. Screenshot capture is separate from the default test suite and CI.

## Additional verification notes

`AcceptanceFlowTests` exercises reviewed recovery from malformed entity files, attachment/Markdown undo, trip-local identity mapping, accommodation/comment editing, schedule comparison tabs, public-repository warnings through the real provider with a mocked HTTP boundary, text scaling/high contrast, and both outcomes of the automatic merge grace period using real divergent Git remotes. `ReleaseIntegrityTests` in TravelRepo covers arbitrary-precision extension values, ordered/Markdown merge changes, generated timezone round-trips and invalid-result rejection.

The Linux desktop entry and Windows installer register `jourfold://` links. Link handling asks before cloning and uses normal provider/server access. A link never grants access by itself.

When changing between sibling source references and packaged TravelRepo dependencies, run `dotnet restore --force-evaluate --source <travelrepo-package-feed> --source https://api.nuget.org/v3/index.json` once to regenerate locks for that dependency graph. Normal source checkout CI uses its committed lock files with `--locked-mode`. The package-only build is verified in an isolated copy with no TravelRepo checkout.

Publishing uses `obj/packages.publish.lock.json` in each project so selecting a runtime does not rewrite source dependency locks. After packaging, normal `dotnet restore --locked-mode` must still pass. See [the recorded verification run](verification.md) for results and unresolved release gates.
