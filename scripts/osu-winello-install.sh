#!/usr/bin/env bash
# One-line Companella! install for osu-winello:
#   curl -fsSL https://raw.githubusercontent.com/leyna64/companella/live/scripts/osu-winello-install.sh | bash
set -euo pipefail

# osu-winello's defaults. It puts its bin folder on PATH only in shell rc files, which `curl | bash` does not read
export XDG_DATA_HOME="${XDG_DATA_HOME:-$HOME/.local/share}"
export BINDIR="${BINDIR:-$HOME/.local/bin}"
PATH="$PATH:$BINDIR"

osuwine="$(command -v osu-wine || true)"
if [ -z "$osuwine" ]; then
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
# --silent: without it Setup starts Companella! and osu-wine waits until it is closed
"$osuwine" --wine "$tmp/CompanellaSetup.exe" --silent >/dev/null 2>&1 || {
	echo "The Companella! installer failed. Close osu! and try again." >&2
	exit 1
}

# Companella!'s install hook writes the same launcher, but inside Wine it can fail without a word, so write it here.
# Keep in sync with Companella/Services/Platform/OsuWinelloSetup.cs.
launcher="$XDG_DATA_HOME/companella/osu-winello.sh"
exe='C:\users\'"$USER"'\AppData\Local\Companella\Companella!.exe'
update='C:\users\'"$USER"'\AppData\Local\Companella\Update.exe'
mkdir -p "$XDG_DATA_HOME/companella" "$XDG_DATA_HOME/applications"
cat >"$launcher" <<EOF
#!/usr/bin/env bash
# Written by Companella!'s osu-winello install. Starts osu! through osu-wine when it is not running,
# then Companella! in the same Wine prefix (osu-wine restarts Wine if osu! is not running yet).
pgrep -x 'osu!.exe' >/dev/null || {
	'$osuwine' --wine '$update' --update 'https://updates.erdbee.re/companella' >/dev/null 2>&1
	'$osuwine' >/dev/null 2>&1 &
	for _ in \$(seq 60); do pgrep -x 'osu!.exe' >/dev/null && break; sleep 1; done
}
pgrep -x 'Companella!.exe' >/dev/null || exec '$osuwine' --wine '$exe'
EOF
cat >"$XDG_DATA_HOME/applications/companella-osu-winello.desktop" <<EOF
[Desktop Entry]
Type=Application
Name=Companella! (osu-winello)
Comment=Start osu! and Companella! through osu-winello
Exec=bash "$launcher"
Terminal=false
Categories=Game;
EOF

# Turns off the startup "Restart osu!?": restarting osu! ends the osu-winello Wine session and freezes Companella!
prefix="${WINEPREFIX:-$XDG_DATA_HOME/wineprefixes/osu-wineprefix}"
settings="$prefix/drive_c/users/$USER/AppData/Roaming/Companella/settings.json"
mkdir -p "$(dirname "$settings")"
if [ ! -s "$settings" ]; then
	printf '{\n  "osuWinello": true\n}\n' >"$settings"
elif grep -q '"osuWinello"' "$settings"; then
	sed -i 's/"osuWinello": *false/"osuWinello": true/' "$settings"
else
	sed -i '0,/{/s//{\n  "osuWinello": true,/' "$settings"
fi

echo "Done. Open 'Companella! (osu-winello)' from your app menu, or run: bash $launcher"
# CompanellaSetup.exe opened directly installs into ~/.wine, where Companella! cannot see osu-winello's osu!
if [ -d "$HOME/.wine/drive_c/users/$USER/AppData/Local/Companella" ]; then
	echo "Note: another Companella! is installed outside osu-winello. Its 'Companella!' shortcut cannot connect to osu!; use 'Companella! (osu-winello)'."
fi
