@echo off
chcp 65001 >nul
start "" powershell.exe -NoProfile -ExecutionPolicy Bypass -NoExit -File "%~dp0update-version.ps1"
exit /b
