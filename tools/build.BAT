@echo off
rem Builds the programs with the C# compiler that is part of Windows (.NET Framework 4.x, nothing to install),
rem then runs the checks of app\tests. Each program has its own version:
rem   comp-info.exe - the toolbox; version in console-ui.ps1, shared with the console version (scripts and BAT)
rem   SUSpector.exe - standalone security check for users; version in app\SUSpector.version
rem   youSBee.exe   - standalone USB serial numbers; version in app\youSBee.version
rem Usage: tools\build.BAT [toolbox] [suspector] [yousbee]   - no arguments builds all three. The checks always run.
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

set "REFS=/r:System.Management.dll /r:System.Web.Extensions.dll /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll /r:System.DirectoryServices.AccountManagement.dll"
set "CORE=obj\Version.cs app\src\Json.cs app\src\Store.cs app\src\Identity.cs app\src\Machine.cs app\src\Network.cs app\src\Export.cs app\src\Collector.cs app\src\Cloud.cs app\src\Shell.cs app\src\SystemChanges.cs app\src\Security.cs app\src\DocumentSearch.cs app\src\Lang.cs app\src\Strings.cs"
set "UI=app\src\Theme.cs app\src\Ui.cs app\src\MainForm.cs app\src\CollectPage.cs app\src\DatabasePage.cs app\src\ToolPages.cs app\src\UsbPage.cs app\src\FileChips.cs app\src\SecurityPage.cs app\src\SearchPage.cs app\src\CloudPages.cs app\src\SystemPages.cs app\src\AboutPage.cs app\src\Program.cs"
set "SUSPECTOR_CORE=obj\SuspectorVersion.cs app\src\Json.cs app\src\Store.cs app\src\Identity.cs app\src\Machine.cs app\src\Network.cs app\src\Shell.cs app\src\SystemChanges.cs app\src\Security.cs app\src\SuspectorUpdate.cs app\src\Lang.cs app\src\Strings.cs"
set "SUSPECTOR_UI=app\src\Theme.cs app\src\Ui.cs app\src\Standalone.cs app\src\FileChips.cs app\src\SecurityPage.cs app\src\SuspectorUpdateDialog.cs app\src\Suspector.cs"
set "YOUSBEE_CORE=obj\YouSBeeVersion.cs app\src\Json.cs app\src\Store.cs app\src\Identity.cs app\src\Machine.cs app\src\Network.cs app\src\Shell.cs app\src\Lang.cs app\src\Strings.cs"
set "YOUSBEE_UI=app\src\Theme.cs app\src\Ui.cs app\src\Standalone.cs app\src\UsbPage.cs app\src\YouSBee.cs"
set "WINFORMS=/r:System.Windows.Forms.dll /r:System.Drawing.dll"

if not defined BUILD_TOOLBOX goto suspector
call :free comp-info || exit /b 1
"%CSC%" /nologo /optimize+ /codepage:65001 /target:winexe /out:comp-info.exe /win32manifest:app\app.manifest /win32icon:app\app.ico /resource:app\trigger-words.txt,CompInfo.trigger-words.txt /resource:app\brand.png,CompInfo.brand.png /resource:app\face.png,CompInfo.face.png /resource:app\face-ru.png,CompInfo.face-ru.png ^
  %REFS% %WINFORMS% %CORE% %UI%
if errorlevel 1 exit /b 1
echo Built comp-info.exe %VER%

:suspector
if not defined BUILD_SUSPECTOR goto yousbee
call :free SUSpector || exit /b 1
"%CSC%" /nologo /optimize+ /codepage:65001 /target:winexe /main:CompInfo.SuspectorProgram /out:SUSpector.exe /win32manifest:app\app.manifest /win32icon:app\suspector.ico /resource:app\suspector.png,CompInfo.suspector.png /resource:app\trigger-words.txt,CompInfo.trigger-words.txt ^
  %REFS% %WINFORMS% %SUSPECTOR_CORE% %SUSPECTOR_UI%
if errorlevel 1 exit /b 1
echo Built SUSpector.exe %SUSVER%

:yousbee
if not defined BUILD_YOUSBEE goto checks
call :free youSBee || exit /b 1
"%CSC%" /nologo /optimize+ /codepage:65001 /target:winexe /main:CompInfo.YouSBeeProgram /out:youSBee.exe /win32manifest:app\app.manifest /win32icon:app\yousbee.ico /resource:app\yousbee.png,CompInfo.yousbee.png ^
  %REFS% %WINFORMS% %YOUSBEE_CORE% %YOUSBEE_UI%
if errorlevel 1 exit /b 1
echo Built youSBee.exe %YSBVER%

:checks
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
