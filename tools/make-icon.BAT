@echo off
rem Builds app\app.ico (icon of comp-info.exe) from the web icon cloud\public\icon-180.png.
rem Optional argument: a PNG preview of all sizes.
setlocal
cd /d "%~dp0.."
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist obj mkdir obj
"%CSC%" /nologo /optimize+ /codepage:65001 /target:exe /out:obj\MakeIcon.exe /r:System.Drawing.dll tools\MakeIcon.cs
if errorlevel 1 exit /b 1
obj\MakeIcon.exe cloud\public\icon-180.png app\app.ico %1
