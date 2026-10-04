#!/usr/bin/env bash
# Install or update Jourfold for the current user on Linux.
#
#   curl -fsSL https://github.com/OWNER/REPO/releases/latest/download/install.sh | bash
#
# Options (append after `bash -s --` when piping):
#   --version vX.Y.Z   install a specific release instead of the latest
#   --uninstall        remove Jourfold; trips and settings are kept
#   --no-desktop       do not register the application launcher
#
# Environment:
#   JOURFOLD_REPO           GitHub repository (owner/name) to download from
#   JOURFOLD_DOWNLOAD_BASE  alternative base URL for the release files (mirrors, testing)
#   JOURFOLD_INSTALL_DIR    application directory (default: ~/.local/share/jourfold-app)
#   JOURFOLD_BIN_DIR        directory for the `jourfold` command (default: ~/.local/bin)
#
# Nothing is installed system-wide and sudo is never used.
set -euo pipefail

# Release builds replace this marker with the repository that published them.
DEFAULT_REPO="@JOURFOLD_REPOSITORY@"
ARCHIVE=""
tmp=""

say() { printf '%s\n' "$*"; }
usage() {
  cat <<'USAGE'
Install or update Jourfold for the current user.
  --version vX.Y.Z   install a specific release instead of the latest
  --uninstall        remove Jourfold; trips and settings are kept
  --no-desktop       do not register the application launcher
Environment: JOURFOLD_REPO, JOURFOLD_DOWNLOAD_BASE, JOURFOLD_INSTALL_DIR, JOURFOLD_BIN_DIR
USAGE
}
fail() { printf 'Jourfold installer: %s\n' "$*" >&2; exit 1; }
have() { command -v "$1" >/dev/null 2>&1; }

main() {
  local version="latest" uninstall=0 desktop=1
  while [ $# -gt 0 ]; do
    case "$1" in
      --version) [ $# -ge 2 ] || fail "--version needs a value, for example v0.2.0"; version="$2"; shift 2 ;;
      --version=*) version="${1#*=}"; shift ;;
      --uninstall) uninstall=1; shift ;;
      --no-desktop) desktop=0; shift ;;
      -h|--help) usage; exit 0 ;;
      *) fail "unknown option: $1" ;;
    esac
  done

  local data="${XDG_DATA_HOME:-$HOME/.local/share}"
  local dir="${JOURFOLD_INSTALL_DIR:-$data/jourfold-app}"
  local bin="${JOURFOLD_BIN_DIR:-$HOME/.local/bin}"
  [ -n "${HOME:-}" ] || fail "HOME is not set."

  if [ "$uninstall" -eq 1 ]; then
    uninstall_app "$dir" "$bin" "$data"
    return
  fi

  [ "$(uname -s)" = "Linux" ] || fail "this installer is for Linux. On Windows use install.ps1."
  case "$(uname -m)" in
    x86_64|amd64) ARCHIVE="jourfold-linux-x64.tar.gz" ;;
    aarch64|arm64) ARCHIVE="jourfold-linux-arm64.tar.gz" ;;
    *) fail "Jourfold builds are available for x86-64 and ARM64; this machine is $(uname -m)." ;;
  esac
  have tar || fail "tar is required."
  have sha256sum || fail "sha256sum is required to verify the download."
  have curl || have wget || fail "curl or wget is required."

  local base
  if [ -n "${JOURFOLD_DOWNLOAD_BASE:-}" ]; then
    base="${JOURFOLD_DOWNLOAD_BASE%/}"
  else
    local repo="${JOURFOLD_REPO:-$DEFAULT_REPO}"
    case "$repo" in
      @*|"") fail "this copy of the installer does not name a repository. Use the install.sh attached to a Jourfold release, or set JOURFOLD_REPO=owner/name." ;;
      */*) ;;
      *) fail "JOURFOLD_REPO must look like owner/name, not '$repo'." ;;
    esac
    if [ "$version" = "latest" ]; then base="https://github.com/$repo/releases/latest/download"
    else base="https://github.com/$repo/releases/download/$version"; fi
  fi

  tmp="$(mktemp -d "${TMPDIR:-/tmp}/jourfold-install.XXXXXX")"
  trap 'rm -rf "$tmp"' EXIT

  say "Downloading Jourfold ($version)..."
  download "$base/$ARCHIVE" "$tmp/$ARCHIVE"
  download "$base/SHA256SUMS" "$tmp/SHA256SUMS"

  say "Verifying the download..."
  grep -E "[[:space:]]\*?$ARCHIVE\$" "$tmp/SHA256SUMS" > "$tmp/expected" || fail "SHA256SUMS does not list $ARCHIVE."
  (cd "$tmp" && sha256sum -c --status expected) || fail "checksum mismatch. The download is incomplete or was altered; nothing was installed."

  say "Installing to $dir..."
  mkdir -p "$tmp/unpacked"
  tar -xzf "$tmp/$ARCHIVE" -C "$tmp/unpacked"
  [ -x "$tmp/unpacked/jourfold/Jourfold.Desktop" ] || fail "the archive does not contain the Jourfold application."
  mkdir -p "$(dirname "$dir")"
  # Replace the previous version only after the new one is complete.
  rm -rf "$dir.new"
  mv "$tmp/unpacked/jourfold" "$dir.new"
  if [ -d "$dir" ]; then rm -rf "$dir.old"; mv "$dir" "$dir.old"; fi
  mv "$dir.new" "$dir"
  rm -rf "$dir.old"

  mkdir -p "$bin"
  ln -sfn "$dir/Jourfold.Desktop" "$bin/jourfold"

  if [ "$desktop" -eq 1 ]; then
    "$dir/Jourfold.Desktop" --install-desktop >/dev/null || say "Note: the application launcher could not be registered. Run '$bin/jourfold --install-desktop' later."
    have update-desktop-database && update-desktop-database "$data/applications" >/dev/null 2>&1 || true
    have gtk-update-icon-cache && gtk-update-icon-cache -q -t "$data/icons/hicolor" >/dev/null 2>&1 || true
  fi

  say ""
  if [ "$desktop" -eq 1 ]; then say "Jourfold is installed. Start it from your application menu or run: jourfold"
  else say "Jourfold is installed. Start it with: jourfold"; fi
  check_dependencies "$bin"
}

download() {
  if have curl; then curl -fsSL --retry 3 -o "$2" "$1"
  else wget -q -O "$2" "$1"; fi || fail "could not download $1"
}

check_dependencies() {
  case ":$PATH:" in
    *":$1:"*) ;;
    *) say "Add $1 to your PATH to use the 'jourfold' command in a terminal." ;;
  esac
  if ! have git; then
    say ""
    say "Jourfold needs Git 2.34 or newer to store trip versions. Install it with your package manager, for example:"
    say "  sudo apt install git     (Debian, Ubuntu)"
    say "  sudo dnf install git     (Fedora)"
    say "  sudo pacman -S git       (Arch, CachyOS, Manjaro)"
  else
    local v major minor
    v="$(git --version | sed -E 's/[^0-9]*([0-9]+)\.([0-9]+).*/\1 \2/')"
    major="${v% *}"; minor="${v#* }"
    if [ "$major" -lt 2 ] || { [ "$major" -eq 2 ] && [ "$minor" -lt 34 ]; }; then
      say "Your Git is older than 2.34. Please update it before using Jourfold."
    fi
  fi
  if ! have secret-tool; then
    say "Optional: install 'secret-tool' (libsecret) so a GitHub connection is remembered between sessions."
  fi
}

uninstall_app() {
  local dir="$1" bin="$2" data="$3"
  [ -L "$bin/jourfold" ] && rm -f "$bin/jourfold"
  rm -rf "$dir" "$dir.new" "$dir.old"
  if [ -f "$data/applications/jourfold.desktop" ] && grep -qF "$dir/" "$data/applications/jourfold.desktop" 2>/dev/null; then
    rm -f "$data/applications/jourfold.desktop" "$data/icons/hicolor/256x256/apps/jourfold.png"
  fi
  say "Jourfold was removed. Your trip folders and settings were not touched."
}

main "$@"
