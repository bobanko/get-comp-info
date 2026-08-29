@echo off
chcp 65001 >nul

start "" powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0comp-info-menu.ps1"
exit /b
