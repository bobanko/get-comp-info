@echo off
chcp 65001 >nul

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0view-comp-info.ps1"

echo.
pause
