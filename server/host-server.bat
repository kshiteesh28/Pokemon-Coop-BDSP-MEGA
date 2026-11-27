@echo off
rem Double-click to host the Pokemon co-op server.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\host-server.ps1" %*
