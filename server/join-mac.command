#!/bin/bash
# Pokemon co-op: join a friend's server from a Mac.
# Double-click this file (first time: right-click > Open), or run:  bash join-mac.command
cd "$(dirname "$0")" || exit 1

echo
echo "  =============================================="
echo "         POKEMON CO-OP  -  JOIN A SERVER"
echo "  =============================================="
echo

BIN=""
if command -v go >/dev/null 2>&1; then
  go build -o coop-sim-mac ./cmd/coop-sim || { read -r -p "  Build failed. Press Enter to close"; exit 1; }
  BIN=./coop-sim-mac
elif [ "$(uname -m)" = "x86_64" ] && [ -f ./coop-sim-mac-intel ]; then
  BIN=./coop-sim-mac-intel
elif [ -f ./coop-sim-mac ]; then
  BIN=./coop-sim-mac
fi
if [ -z "$BIN" ]; then
  echo "  coop-sim-mac is missing. Put it in this folder (ask your host) or install Go from https://go.dev/dl/"
  read -r -p "  Press Enter to close"
  exit 1
fi
chmod +x "$BIN"
xattr -d com.apple.quarantine "$BIN" 2>/dev/null

ini_get() { grep -i -m1 "^[[:space:]]*$1[[:space:]]*=" coop.ini 2>/dev/null | cut -d= -f2- | sed 's/^[[:space:]]*//; s/[[:space:]]*$//; s/^"\(.*\)"$/\1/'; }

ask_settings() {
  echo "  Ask your host for the address, room code and password."
  echo "  (Press Enter to keep the value in [brackets].)"
  echo
  local a r n p
  read -r -p "  Server address (e.g. 100.67.109.85:4100) [$(ini_get address)]: " a; a=${a:-$(ini_get address)}
  read -r -p "  Room code [$(ini_get room)]: " r; r=${r:-$(ini_get room)}
  read -r -p "  Your trainer name [$(ini_get name)]: " n; n=${n:-$(ini_get name)}
  read -r -p "  Password (Enter if none) [$(ini_get password)]: " p; p=${p:-$(ini_get password)}
  case "$a" in *:[0-9]*) ;; ?*) a="$a:4100" ;; esac
  printf '# Pokemon co-op settings (used by join and the game)\naddress  = %s\nroom     = %s\nname     = %s\npassword = %s\n' "$a" "$r" "$n" "$p" > coop.ini
  echo "  Saved to coop.ini"
  echo
}

if [ ! -f coop.ini ] && [ -f invite-coop.ini ]; then cp invite-coop.ini coop.ini; echo "  Using invite-coop.ini"; fi
if [ -z "$(ini_get address)" ] || [ -z "$(ini_get room)" ] || [ -z "$(ini_get name)" ]; then ask_settings; fi

while true; do
  ADDR=$(ini_get address)
  echo "  Connecting to $ADDR  room $(ini_get room)  as $(ini_get name) ..."
  if nc -z -G 3 "${ADDR%:*}" "${ADDR##*:}" >/dev/null 2>&1; then break; fi
  echo
  echo "  Can't reach $ADDR."
  echo "  - Is the host's server window open?"
  echo "  - Over the internet: is Tailscale on (and did you accept the host's share link)?"
  echo "  - Same Wi-Fi: is the host's Wi-Fi set to Private?"
  echo
  read -r -p "  [R]etry, [C]hange settings, or [Q]uit: " ans
  case "$ans" in [Qq]*) exit 1 ;; [Cc]*) ask_settings ;; esac
done

echo "  Connected! Other players will appear below. Close this window to leave."
echo "  ----------------------------------------------"
"$BIN" -config coop.ini -players 1 -duration 0
echo
read -r -p "  Disconnected. Press Enter to close"
