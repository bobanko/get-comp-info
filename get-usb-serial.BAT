@echo off
chcp 65001 >nul

start "" powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0get-usb-serial.ps1"
exit /b
