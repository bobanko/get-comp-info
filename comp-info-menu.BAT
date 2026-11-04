@echo off
chcp 65001 >nul

rem The window version is used when it is next to this file; otherwise the console menu.
if exist "%~dp0comp-info.exe" (
  start "" "%~dp0comp-info.exe"
  exit /b
)
start "" powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0comp-info-menu.ps1"
exit /b
