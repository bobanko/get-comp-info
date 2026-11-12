@echo off
chcp 65001 >nul

rem Console menu. The window version is comp-info.exe next to this file.
start "" powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0comp-info-menu.ps1"
exit /b
