#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
UPSTREAM_DIR="${SCRIPT_DIR}/../PKLumiHex-upstream"
RID="${1:-osx-arm64}"
OUT_DIR="${SCRIPT_DIR}/dist"
PUBLISH_DIR="${OUT_DIR}/publish-${RID}"
APP_BUNDLE="${OUT_DIR}/PKLumiHex.app"

if [ ! -d "${UPSTREAM_DIR}/PKHeX.Core" ]; then
  git clone --depth 1 https://github.com/TalonSabre/PKLumiHex.git "${UPSTREAM_DIR}"
fi

dotnet publish "${SCRIPT_DIR}/PKLumiHex.Mac.csproj" \
  -c Release \
  -r "${RID}" \
  --self-contained true \
  -o "${PUBLISH_DIR}"

rm -rf "${APP_BUNDLE}"
mkdir -p "${APP_BUNDLE}/Contents/MacOS" "${APP_BUNDLE}/Contents/Resources"
cp "${SCRIPT_DIR}/Info.plist" "${APP_BUNDLE}/Contents/Info.plist"
cp -R "${PUBLISH_DIR}/"* "${APP_BUNDLE}/Contents/MacOS/"
chmod +x "${APP_BUNDLE}/Contents/MacOS/PKLumiHex"

if command -v codesign >/dev/null 2>&1; then
  codesign --force --deep --sign - "${APP_BUNDLE}"
fi

echo "Built ${APP_BUNDLE}"
