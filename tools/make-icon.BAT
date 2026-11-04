@echo off
rem Builds app\app.ico (icon of comp-info.exe) from app\icon-source.png (the bearded gear):
rem sizes up to 32 px are a plain gear; app\brand.png (plain gear, window header) and app\face.png (the picture, «О программе»).
rem Optional argument: a PNG preview of all sizes.
setlocal
cd /d "%~dp0.."
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist obj mkdir obj
"%CSC%" /nologo /optimize+ /codepage:65001 /target:exe /out:obj\MakeIcon.exe /r:System.Drawing.dll tools\MakeIcon.cs
if errorlevel 1 exit /b 1
obj\MakeIcon.exe app\icon-source.png app\app.ico %1 --gear-small --gear-png=app\brand.png --face-png=app\face.png
