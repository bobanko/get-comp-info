@echo off
chcp 65001 >nul
rem Opens a new window the same way as comp-info-menu.BAT, so Windows picks the same terminal.
start "" powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0console-diagnostics.ps1"
exit /b
