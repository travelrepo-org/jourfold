# Installing Jourfold

Each Jourfold release on GitHub carries two small install scripts, `install.sh` for Linux and macOS and `install.ps1` for Windows. They download the release for your system, check it against the published SHA-256 checksums and install it for your user account only. No administrator rights are needed.

The commands below install from the official repository, `travelrepo-org/jourfold`. Forks publish their own scripts: the copies attached to a release always name the repository that built them.

## Linux

```sh
curl -fsSL https://github.com/travelrepo-org/jourfold/releases/latest/download/install.sh | bash
```

This installs the latest release to `~/.local/share/jourfold-app`, adds a `jourfold` command to `~/.local/bin` and registers Jourfold in your application menu. Running the same command again updates Jourfold and reports the change, for example `Jourfold was updated from 0.1.1 to 0.2.0.` The previous version is replaced only after the new one has been downloaded, verified and unpacked.

Options go after `bash -s --`:

```sh
# a specific release
curl -fsSL https://github.com/travelrepo-org/jourfold/releases/latest/download/install.sh | bash -s -- --version v0.2.0
# install without an application menu entry
curl -fsSL https://github.com/travelrepo-org/jourfold/releases/latest/download/install.sh | bash -s -- --no-desktop
# remove Jourfold
curl -fsSL https://github.com/travelrepo-org/jourfold/releases/latest/download/install.sh | bash -s -- --uninstall
```

`JOURFOLD_INSTALL_DIR` and `JOURFOLD_BIN_DIR` change where the application and the command go. `JOURFOLD_DOWNLOAD_BASE` downloads from a mirror instead of GitHub; the mirror must serve the archive for the system (`jourfold-linux-x64.tar.gz`, `jourfold-linux-arm64.tar.gz`, `jourfold-osx-arm64.tar.gz` or `jourfold-osx-x64.tar.gz`) and `SHA256SUMS`.

Jourfold needs Git 2.34 or newer. The script tells you if Git is missing and how to install it. `secret-tool` (package `libsecret-tools` on Debian and Ubuntu, `libsecret` elsewhere) is optional and lets Jourfold remember a GitHub connection. Builds are available for x86-64 and ARM64 (aarch64); the script picks the one for your machine.

If you prefer a package, the release also has `.deb` files for Debian and Ubuntu on amd64 and arm64.

## macOS

Open Terminal and run the same command as on Linux:

```sh
curl -fsSL https://github.com/travelrepo-org/jourfold/releases/latest/download/install.sh | bash
```

This installs the latest release as `Jourfold.app` in the `Applications` folder of your home folder (`~/Applications`), where Launchpad and Spotlight find it, and adds a `jourfold` command to `~/.local/bin`. Running the command again updates Jourfold; `--version` and `--uninstall` work as on Linux. Builds are available for Apple silicon and Intel Macs with macOS 14 (Sonoma) or newer.

Jourfold is not notarized by Apple. The script therefore signs the app for your Mac, the way Xcode does for apps built on it, so macOS opens it without a warning. Files downloaded with a browser are checked by Gatekeeper instead: if you unpack `jourfold-osx-arm64.tar.gz` yourself, macOS refuses to open the app until you allow it under **System Settings > Privacy & Security > Open Anyway**.

Jourfold needs Git. If it is missing, the script says so: install Apple's Command Line Tools with `xcode-select --install`, or Git from Homebrew with `brew install git`. GitHub sign-ins are kept in your login keychain. Because the app is signed on each Mac and again after every update, macOS may ask once after an update whether Jourfold may use the stored sign-in; choose **Always Allow**.

## Windows

Open PowerShell and run:

```powershell
irm https://github.com/travelrepo-org/jourfold/releases/latest/download/install.ps1 | iex
```

The script downloads the Jourfold setup for your processor (x64, or ARM64 on Windows on Arm), checks its checksum and runs it silently. Jourfold appears in the Start menu and under **Settings > Apps**, where it can also be uninstalled. Git for Windows is included. Running the command again updates Jourfold, closes a running copy first and reports which version was replaced by which.

Options:

```powershell
# a specific release
$env:JOURFOLD_VERSION = 'v0.2.0'; irm https://github.com/travelrepo-org/jourfold/releases/latest/download/install.ps1 | iex
# remove Jourfold
& ([scriptblock]::Create((irm https://github.com/travelrepo-org/jourfold/releases/latest/download/install.ps1))) -Uninstall
```

Set `JOURFOLD_ARCH` to `x64` or `arm64` to choose a build yourself. Jourfold needs 64-bit Windows 10 or Windows 11. You can also download `jourfold-setup-win-x64.exe` or `jourfold-setup-win-arm64.exe` from the release page and run it yourself, or use the portable `.zip` archives.

## What is kept

Uninstalling removes the application only. Your trip folders (by default `Documents/Jourfold`) and settings stay where they are.

## Reading the script first

Piping a script into a shell runs it straight away. To look at it before running it:

```sh
curl -fsSLO https://github.com/travelrepo-org/jourfold/releases/latest/download/install.sh
less install.sh
bash install.sh
```

On Windows, open the `install.ps1` link in a browser, or save it with `irm ... -OutFile install.ps1` and read it before running `.\install.ps1`.

## For maintainers

Releases are made by hand in GitHub, and publishing one starts `.github/workflows/release.yml`:

1. Make sure `Version` in `Directory.Build.props` on `main` is the version you want, and that the TravelRepo release `v<TravelRepoVersion>` is published.
2. In GitHub, draft a new release with the tag `v<Version>` (for example `v0.2.0`) on `main`, write the notes, tick **Set as a pre-release** and publish it.
3. If you leave the title empty, the workflow names the release after its code name, for example `Jourfold 0.1.0 “Nomadic Nightingale”`. It checks that the tag matches `Version` and that the TravelRepo tag exists, then builds Jourfold against exactly that TravelRepo tag. It tests, packages x64 and ARM64 for Linux, Windows and macOS, compiles the Windows setups, writes `SHA256SUMS`, puts the repository name into `install.sh` and `install.ps1` in place of `@JOURFOLD_REPOSITORY@`, and attaches everything to the release.

Because it starts as a pre-release, the one-line installers keep using the previous release until the files are attached. Then the workflow turns it into a full release and marks it as latest. Publishing directly as a full release also works; the workflow then keeps the previous release as latest while it builds. If a check or build fails, the workflow turns the release back into a draft and the run log says why. Fix the problem and publish the draft again. Tags with a suffix (for example `v0.2.0-beta.1`, with the same `Version` in `Directory.Build.props`) stay pre-releases. `releases/latest` skips them, so install them with `--version`. "Run workflow" rebuilds the files of an already published release.

A ruleset lets only organisation admins create, move or delete `v*` tags. The workflow checks out TravelRepo from the repository named in the `TRAVELREPO_REPOSITORY` variable.

The macOS packages are built on GitHub's Apple silicon runner, which tests the ARM64 build and cross-compiles the Intel one. `eng/package.py` can also build them on Linux; only a build on a Mac signs the bundle, and the install script signs it again on the Mac it installs to. The app icon `assets/branding/jourfold.icns` is generated from the mark with `dotnet run eng/macos-icon.cs -- assets/branding/mark.svg assets/branding/jourfold.icns`.

Linux and Windows ARM64 packages are cross-compiled on x64 runners, so the tests do not run on ARM64 by default. To build and test them on ARM64 machines, set the repository variables `LINUX_ARM64_RUNNER` (for example `ubuntu-24.04-arm`) and `WINDOWS_ARM64_RUNNER` (for example `windows-11-arm`). GitHub provides these runners for public repositories; private repositories may need paid larger runners.

To test the Linux script without a release, serve a folder containing `jourfold-linux-x64.tar.gz` and a matching `SHA256SUMS` and point `JOURFOLD_DOWNLOAD_BASE` at it:

```sh
python eng/package.py --rid linux-x64
cd artifacts && sha256sum jourfold-linux-x64.tar.gz > SHA256SUMS && python3 -m http.server 8765 &
JOURFOLD_DOWNLOAD_BASE=http://127.0.0.1:8765 bash install/install.sh
```
