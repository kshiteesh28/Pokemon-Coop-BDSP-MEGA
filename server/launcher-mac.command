#!/bin/bash
# Builds (if Go is installed) and opens the Pokemon Co-op app on a Mac.
# Double-click (first time: right-click > Open), or run: bash launcher-mac.command
cd "$(dirname "$0")" || exit 1
if command -v go >/dev/null 2>&1; then
  echo "Building Pokemon Co-op..."
  go build -o "Pokemon Co-op" ./cmd/coop-launcher || { read -r -p "Build failed. Press Enter to close"; exit 1; }
fi
if [ ! -f "Pokemon Co-op" ]; then
  echo "\"Pokemon Co-op\" is missing. Install Go from https://go.dev/dl/ or download the app from GitHub Actions."
  read -r -p "Press Enter to close"
  exit 1
fi
chmod +x "Pokemon Co-op"
xattr -d com.apple.quarantine "Pokemon Co-op" 2>/dev/null
exec "./Pokemon Co-op"
