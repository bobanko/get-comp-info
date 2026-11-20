@echo off
rem Builds the programs (WPF on .NET Framework 4.8, WINDOWS_APP.md) with the .NET SDK, then the checks of app\tests
rem with the C# compiler that is part of Windows. Each program has its own version:
rem   comp-info.exe - the toolbox; version in console-ui.ps1, shared with the console version (scripts and BAT)
rem   SUSpector.exe - standalone security check for users; version in app\SUSpector.version
rem   youSBee.exe   - standalone USB serial numbers; version in app\youSBee.version
rem Usage: tools\build.BAT [toolbox] [suspector] [yousbee]   - no arguments builds all three. The checks always run.
rem The programs need nothing on the computers they run on; the computer that builds them needs the .NET SDK.
setlocal
cd /d "%~dp0.."
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
  echo csc.exe not found. .NET Framework 4.x is required.
  exit /b 1
)
if not exist obj mkdir obj

:arguments
if "%~1"=="" goto chosen
if /i "%~1"=="toolbox" (set "BUILD_TOOLBOX=1") else if /i "%~1"=="suspector" (set "BUILD_SUSPECTOR=1") else if /i "%~1"=="yousbee" (set "BUILD_YOUSBEE=1") else (
  echo Unknown program: %~1. Use toolbox, suspector or yousbee.
  exit /b 1
)
shift
goto arguments
:chosen
if not defined BUILD_TOOLBOX if not defined BUILD_SUSPECTOR if not defined BUILD_YOUSBEE (
  set "BUILD_TOOLBOX=1"
  set "BUILD_SUSPECTOR=1"
  set "BUILD_YOUSBEE=1"
)

rem The toolbox version is the one of the console version (console-ui.ps1), so both report the same release.
for /f "tokens=2 delims='" %%i in ('findstr /b /c:"$script:toolboxVersion = " console-ui.ps1') do set "VER=%%i"
if "%VER%"=="" (
  echo Could not read the version from console-ui.ps1.
  exit /b 1
)
set /p SUSVER=<app\SUSpector.version
set /p YSBVER=<app\youSBee.version
if "%SUSVER%"=="" ( echo Could not read app\SUSpector.version. & exit /b 1 )
if "%YSBVER%"=="" ( echo Could not read app\youSBee.version. & exit /b 1 )
call :version obj\Version.cs "CHAGEAR Toolbox" %VER%
call :version obj\SuspectorVersion.cs "SUSpector" %SUSVER%
call :version obj\YouSBeeVersion.cs "youSBee" %YSBVER%

rem The core (C# 5, no window code): built into the programs by their projects in app\wpf and into the checks here.
set "REFS=/r:System.Management.dll /r:System.Web.Extensions.dll /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll /r:System.DirectoryServices.AccountManagement.dll"
set "CORE=obj\Version.cs app\src\Json.cs app\src\Store.cs app\src\LooseSearch.cs app\src\Identity.cs app\src\Machine.cs app\src\Network.cs app\src\Export.cs app\src\Collector.cs app\src\Cloud.cs app\src\Shell.cs app\src\SystemChanges.cs app\src\Security.cs app\src\DocumentSearch.cs app\src\UsbList.cs app\src\SecurityFiles.cs app\src\DeviceList.cs app\src\ComputerForms.cs app\src\IniSettings.cs app\src\Lang.cs app\src\Strings.cs"

call :dotnet || exit /b 1
if defined BUILD_TOOLBOX call :wpf comp-info %VER% || exit /b 1
if defined BUILD_SUSPECTOR call :wpf SUSpector %SUSVER% || exit /b 1
if defined BUILD_YOUSBEE call :wpf youSBee %YSBVER% || exit /b 1

"%CSC%" /nologo /optimize+ /codepage:65001 /target:exe /out:obj\tests.exe /resource:app\trigger-words.txt,CompInfo.trigger-words.txt %REFS% %CORE% app\src\SuspectorUpdate.cs app\tests\Tests.cs app\tests\FakeCloud.cs
if errorlevel 1 exit /b 1
obj\tests.exe app\tests
if errorlevel 1 exit /b 1
echo Build OK.
exit /b 0

rem ---------- Subroutines ----------

rem A running exe cannot be overwritten, but Windows lets it be renamed: it keeps running from obj\.
:free
del /q obj\%1.previous.exe >nul 2>&1
if exist %1.exe move /y %1.exe obj\%1.previous.exe >nul 2>&1
if exist %1.exe (
  echo %1.exe is busy. Close the program and run the build again.
  exit /b 1
)
exit /b 0

rem One program: app\wpf\<name>\<name>.csproj, built to obj\wpf\<name>-out and copied to the root as <name>.exe.
rem The <name>-wpf.exe of the time of the move to WPF is deleted.
:wpf
call :free %1 || exit /b 1
"%DOTNET%" build app\wpf\%1\%1.csproj -c Release -o obj\wpf\%1-out -nologo -v q -clp:NoSummary
if errorlevel 1 exit /b 1
copy /y obj\wpf\%1-out\%1.exe %1.exe >nul || exit /b 1
if exist %1-wpf.exe del /q %1-wpf.exe >nul 2>&1
echo Built %1.exe %2
exit /b 0

rem Finds dotnet.exe of the .NET SDK: in PATH, installed for all users, or per user (dotnet-install.ps1).
:dotnet
set DOTNET_CLI_TELEMETRY_OPTOUT=1
set DOTNET_NOLOGO=1
set "DOTNET="
for /f "delims=" %%i in ('where dotnet 2^>nul') do if not defined DOTNET set "DOTNET=%%i"
if not defined DOTNET if exist "%ProgramFiles%\dotnet\dotnet.exe" set "DOTNET=%ProgramFiles%\dotnet\dotnet.exe"
if not defined DOTNET if exist "%LOCALAPPDATA%\Microsoft\dotnet\dotnet.exe" set "DOTNET=%LOCALAPPDATA%\Microsoft\dotnet\dotnet.exe"
if defined DOTNET exit /b 0
echo .NET SDK not found: it is needed to build the programs (https://dotnet.microsoft.com/download).
exit /b 1

rem Version information of one program: file, title (in quotes), version.
:version
> %1 echo using System.Reflection;
>> %1 echo [assembly: AssemblyTitle("%~2")]
rem Without it .NET runs the program in 4.0 compatibility mode (e.g. Uri drops trailing dots of record ids).
>> %1 echo [assembly: System.Runtime.Versioning.TargetFramework(".NETFramework,Version=v4.8", FrameworkDisplayName = ".NET Framework 4.8")]
>> %1 echo [assembly: AssemblyProduct("%~2")]
rem Shown in the file properties; SignPath checks the product name and version of every signed file (CODE_SIGNING.md).
>> %1 echo [assembly: AssemblyCompany("CHAGEAR")]
>> %1 echo [assembly: AssemblyCopyright("Copyright (c) 2026 bobanko. GPL-3.0-or-later")]
>> %1 echo [assembly: AssemblyFileVersion("%3")]
>> %1 echo [assembly: AssemblyInformationalVersion("%3")]
>> %1 echo namespace CompInfo { public static class AppVersion { public const string Text = "%3"; } }
exit /b 0
