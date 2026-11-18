#!/usr/bin/env bash
# One-line Companella! install for osu-winello:
#   curl -fsSL https://raw.githubusercontent.com/leyna64/companella/live/scripts/osu-winello-install.sh | bash
set -euo pipefail

if ! command -v osu-wine >/dev/null 2>&1; then
	echo "osu-wine was not found. Install osu-winello first: https://github.com/NelloKudo/osu-winello" >&2
	exit 1
fi

# The installer silently does nothing while osu! is running
if pgrep -x 'osu!.exe' >/dev/null; then
	echo "osu! is running. Close osu!, then run this again." >&2
	exit 1
fi

tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT

echo "Downloading Companella!..."
curl -fL --progress-bar -o "$tmp/CompanellaSetup.exe" https://updates.erdbee.re/companella/CompanellaSetup.exe

echo "Installing Companella! into the osu-winello Wine prefix..."
touch "$tmp/started"
# --silent: without it Setup starts Companella! and osu-wine waits until it is closed
osu-wine --wine "$tmp/CompanellaSetup.exe" --silent >/dev/null 2>&1 || {
	echo "The Companella! installer failed. Close osu! and try again." >&2
	exit 1
}

# Rewritten by Companella!'s install hook on every install, at the same default path osu-wine uses
launcher="${XDG_DATA_HOME:-$HOME/.local/share}/companella/osu-winello.sh"
if [ ! "$launcher" -nt "$tmp/started" ]; then
	echo "Install finished, but the app menu entry was not created. Run this to create it:" >&2
	echo "  osu-wine --wine '${XDG_DATA_HOME:-$HOME/.local/share}/wineprefixes/osu-wineprefix/drive_c/users/$USER/AppData/Local/Companella/Companella!.exe' --osu-winello" >&2
	exit 1
fi

echo "Done. Open 'Companella! (osu-winello)' from your app menu."
