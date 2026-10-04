# Installing Jourfold

Each Jourfold release on GitHub carries two small install scripts. They download the release for your system, check it against the published SHA-256 checksums and install it for your user account only. No administrator rights are needed.

In the commands below, replace `OWNER/REPO` with the GitHub repository that publishes Jourfold. The scripts attached to a release already contain the right repository.

## Linux

```sh
curl -fsSL https://github.com/OWNER/REPO/releases/latest/download/install.sh | bash
```

This installs the latest release to `~/.local/share/jourfold-app`, adds a `jourfold` command to `~/.local/bin` and registers Jourfold in your application menu. Running the same command again updates Jourfold. The previous version is replaced only after the new one has been downloaded, verified and unpacked.

Options go after `bash -s --`:

```sh
# a specific release
curl -fsSL https://github.com/OWNER/REPO/releases/latest/download/install.sh | bash -s -- --version v0.2.0
# install without an application menu entry
curl -fsSL https://github.com/OWNER/REPO/releases/latest/download/install.sh | bash -s -- --no-desktop
# remove Jourfold
curl -fsSL https://github.com/OWNER/REPO/releases/latest/download/install.sh | bash -s -- --uninstall
```

`JOURFOLD_INSTALL_DIR` and `JOURFOLD_BIN_DIR` change where the application and the command go. `JOURFOLD_DOWNLOAD_BASE` downloads from a mirror instead of GitHub; the mirror must serve `jourfold-linux-x64.tar.gz` and `SHA256SUMS`.

Jourfold needs Git 2.34 or newer. The script tells you if Git is missing and how to install it. `secret-tool` (package `libsecret-tools` on Debian and Ubuntu, `libsecret` elsewhere) is optional and lets Jourfold remember a GitHub connection. Builds are available for x86-64.

If you prefer a package, the release also has a `.deb` for Debian and Ubuntu.

## Windows

Open PowerShell and run:

```powershell
irm https://github.com/OWNER/REPO/releases/latest/download/install.ps1 | iex
```

The script downloads the Jourfold setup, checks its checksum and runs it silently. Jourfold appears in the Start menu and under **Settings > Apps**, where it can also be uninstalled. Git for Windows is included. Running the command again updates Jourfold and closes a running copy first.

Options:

```powershell
# a specific release
$env:JOURFOLD_VERSION = 'v0.2.0'; irm https://github.com/OWNER/REPO/releases/latest/download/install.ps1 | iex
# remove Jourfold
& ([scriptblock]::Create((irm https://github.com/OWNER/REPO/releases/latest/download/install.ps1))) -Uninstall
```

Jourfold needs 64-bit Windows 10 or Windows 11. You can also download `jourfold-setup-win-x64.exe` from the release page and run it yourself, or use the portable `jourfold-win-x64.zip`.

## What is kept

Uninstalling removes the application only. Your trip folders (by default `Documents/Jourfold`) and settings stay where they are.

## Reading the script first

Piping a script into a shell runs it straight away. To look at it before running it:

```sh
curl -fsSLO https://github.com/OWNER/REPO/releases/latest/download/install.sh
less install.sh
bash install.sh
```

On Windows, open the `install.ps1` link in a browser, or save it with `irm ... -OutFile install.ps1` and read it before running `.\install.ps1`.

## For maintainers

`.github/workflows/release.yml` runs when a tag such as `v0.2.0` is pushed. It builds and tests on Linux and Windows, packages both platforms with that version, compiles the Windows setup, writes `SHA256SUMS`, puts the repository name into `install.sh` and `install.ps1` in place of `@JOURFOLD_REPOSITORY@`, and publishes everything as a GitHub release. Tags with a suffix, such as `v0.2.0-beta.1`, become pre-releases, which `releases/latest` skips.

The workflow checks out TravelRepo from the repository named in the `TRAVELREPO_REPOSITORY` variable, like the CI workflow does.

To test the Linux script without a release, serve a folder containing `jourfold-linux-x64.tar.gz` and a matching `SHA256SUMS` and point `JOURFOLD_DOWNLOAD_BASE` at it:

```sh
python eng/package.py --rid linux-x64
cd artifacts && sha256sum jourfold-linux-x64.tar.gz > SHA256SUMS && python3 -m http.server 8765 &
JOURFOLD_DOWNLOAD_BASE=http://127.0.0.1:8765 bash install/install.sh
```
