@echo off
if "%~1"=="" (
    powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0sync-usb.ps1" -UsbPath "E:\COMP-INFO"
) else (
    powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0sync-usb.ps1" %*
)
set "sync_exit=%errorlevel%"
pause
exit /b %sync_exit%
