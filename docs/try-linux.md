# Try Jourfold on another Linux PC

Use `jourfold-linux-x64.tar.gz` for an Intel/AMD 64-bit CachyOS machine with GNOME. It includes the application, .NET runtime, fonts and plugin host. You do not need the .NET SDK, a source checkout or GitHub CLI to run it. Use the tar archive on CachyOS; the `.deb` package targets Debian/Ubuntu.

This is an implementation build under acceptance validation. Ubuntu GNOME and KDE checks are recorded; CachyOS has not yet been verified. Start with a disposable test trip.

## 1. Copy the package

On the development machine, the package is at:

```text
<jourfold-checkout>/artifacts/jourfold-linux-x64.tar.gz
```

Copy it into `~/Downloads` on the main PC using the VM's shared folder or your usual file-transfer tool. If the VM is reachable by SSH, this command runs on the main PC; replace `YOUR_VM_ADDRESS` with the VM's hostname or address:

```sh
scp YOUR_USER@YOUR_VM_ADDRESS:<jourfold-checkout>/artifacts/jourfold-linux-x64.tar.gz ~/Downloads/
```

Copy only the package. The VM's validation launcher refers to paths on that VM. App settings, trip folders and OS credentials are separate from the application package.

For the usability revision verified on 2026-09-30, `sha256sum ~/Downloads/jourfold-linux-x64.tar.gz` should report:

```text
43a09a0e03c780cd77f9ca4f7dfb16977f36582bb636a798ecbe9688217a8573
```

Later builds have different hashes; use the accompanying `artifacts/release-artifacts.json` from that build.

## 2. Prepare CachyOS GNOME

Git 2.34 or newer must be on PATH. Jourfold's current Avalonia 11 build uses X11; a GNOME Wayland session runs it through XWayland. GNOME Keyring provides credential storage, accessed through `secret-tool` from Arch's `libsecret` package. See the [Avalonia Linux guide](https://docs.avaloniaui.net/docs/platform-specific-guides/linux) and [Arch libsecret file list](https://archlinux.org/packages/core/x86_64/libsecret/files/).

On an up-to-date CachyOS installation, install any missing packages:

```sh
sudo pacman -S --needed git libsecret gnome-keyring xorg-xwayland libx11 libice libsm fontconfig glib2
git --version
command -v secret-tool
```

Launch from a terminal inside your normal GNOME desktop session. If GNOME Keyring was newly installed, log out and back in before trying persistent GitHub login.

## 3. Extract and launch

Use a fresh directory for this build so files from older versions cannot remain mixed in:

```sh
mkdir -p ~/Applications/jourfold-test
tar -xzf ~/Downloads/jourfold-linux-x64.tar.gz -C ~/Applications/jourfold-test
~/Applications/jourfold-test/jourfold/Jourfold.Desktop
```

The archive preserves the executable bit. If your transfer/extraction tool removed it, run `chmod +x ~/Applications/jourfold-test/jourfold/Jourfold.Desktop`.

For separate test settings, launch with:

```sh
JOURFOLD_DATA_HOME="$HOME/.local/share/Jourfold-test" \
  "$HOME/Applications/jourfold-test/jourfold/Jourfold.Desktop"
```

Use that same command on subsequent runs to see the same Recent list and settings. This redirects settings and caches; trip content stays in each trip's folder. Credentials remain in the operating system's Jourfold credential store, and recovery journals have their own location.

### Add Jourfold to GNOME's application grid

Run this once from the extracted build:

```sh
~/Applications/jourfold-test/jourfold/Jourfold.Desktop --install-desktop
```

This registers the launcher and icon for your user account, without sudo. Launch **Jourfold** from GNOME's application grid and pin that launcher to the dash if desired. If you move the extracted folder, rerun the command from its new location. A previously pinned launcher may need to be unpinned and pinned again.

## 4. First local test

No GitHub login or collaborator is needed for these steps:

1. Choose **New trip** and create a trip called “Main PC test”. Use synthetic details.
2. Use **Quick add** to add an activity. Find it in **Inbox**, select it and choose **Schedule in plan** in the inspector, or drag it onto the timetable. Scroll to its time if needed.
3. Change its title and leave the field to save. Check **Ctrl+Z**, then **Ctrl+Shift+Z**.
4. Choose **Create version**, review the changes and confirm. Open **History** and inspect the entry.
5. Close Jourfold, launch it again and reopen the trip from Recent. Confirm the activity and version remain.
6. In **Settings**, try Light/Dark themes, German/English and larger text. Check your normal monitor scaling and keyboard navigation with Tab, Shift+Tab, Enter and Escape.
7. Export PDF and calendar files and open them with your normal viewers. Try the file picker and an attachment from a disposable local file.

The [user guide](user-guide.md) explains variants, people, files and the remaining planning tools.

## 5. Test GitHub on this machine

The VM's authorization is not transferred with the archive. Jourfold performs its own browser device authorization on each machine; a `gh auth login` session is not required.

1. Choose **Settings → Connections → Connect GitHub** and enter the public Client ID for your development GitHub App. This is the Client ID, not a secret or private key.
2. Enter Jourfold's displayed code on GitHub's device page, approve access, then dismiss the code dialog in Jourfold so it completes authorization.
3. Choose **Settings → Connections → Discover GitHub**, select the private smoke trip that the App can access, and choose an empty local destination folder.
4. Change a synthetic activity, create a version, then use **Sync**. Verify the new version from the VM or GitHub. Jourfold can also sync committed versions in the background; use only the disposable fixture for this test.
5. Restart Jourfold and repeat discovery or sync to check credential persistence. A session-only credential notice means the secure store was unavailable. Expired App authorization requires reconnecting.

For the existing development setup, use the already installed App and smoke repository. There is no need to create another App or invite a collaborator to test your own account on a second PC. The [App setup guide](github-app-setup.md) covers new installations and permissions. The source-code repositories and their CI credentials are separate from this trip-data test.

## Report the result

The most useful report includes whether launch, editing/reopening, GitHub authorization/sync, scaling and file dialogs passed. For failures, include the steps, expected result, actual result, terminal error and a screenshot if helpful. Record the package hash and:

```sh
cat /etc/os-release
gnome-shell --version
git --version
printenv XDG_SESSION_TYPE
```

Also note your display scaling and whether you used one or multiple monitors. Share only relevant output and synthetic trip screenshots. A main-PC test supplements the automated suite; Windows, full accessibility and hosted CI gates remain tracked in [acceptance status](acceptance.md).
