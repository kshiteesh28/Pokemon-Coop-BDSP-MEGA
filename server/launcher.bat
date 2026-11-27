@echo off
rem Builds (if Go is installed) and opens the Pokemon Co-op app.
cd /d "%~dp0"
where go >nul 2>nul
if %errorlevel%==0 (
  echo Building Pokemon Co-op...
  go build -ldflags "-H=windowsgui" -o "Pokemon Co-op.exe" ./cmd/coop-launcher
  if errorlevel 1 (
    echo Build failed. See the error above.
    pause
    exit /b 1
  )
)
if not exist "Pokemon Co-op.exe" (
  echo "Pokemon Co-op.exe" is missing. Install Go from https://go.dev/dl/ or download the app from GitHub Actions.
  pause
  exit /b 1
)
start "" "Pokemon Co-op.exe"
