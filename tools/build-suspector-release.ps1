# Builds the public standalone SUSpector package. It deliberately contains only SUSpector.exe.
# SUSpector has its own version (app\SUSpector.version) and is released by a tag suspector-v<version>.
[CmdletBinding()]
param(
    [string]$Version,
    [string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
. (Join-Path $root 'console-ui.ps1')
. (Join-Path $PSScriptRoot 'release-package.ps1')

function Invoke-Git {
    param([string[]]$Arguments, [switch]$AllowFailure)
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try { $output = & git -C $root @Arguments 2>$null } finally { $ErrorActionPreference = $previous }
    if ($LASTEXITCODE -ne 0) {
        if ($AllowFailure) { return $null }
        throw "git $($Arguments -join ' ') exited with code $LASTEXITCODE"
    }
    return $output
}

$codeVersion = ([IO.File]::ReadAllText((Join-Path $root 'app\SUSpector.version'))).Trim()
if (-not $Version) { $Version = $codeVersion }
if ($Version -ne $codeVersion) { throw "Version $Version does not match app\SUSpector.version ($codeVersion)." }

$exe = Join-Path $root 'SUSpector.exe'
if (-not (Test-Path -LiteralPath $exe)) { throw 'SUSpector.exe is missing. Run tools\build.BAT first.' }
$built = (Get-Item -LiteralPath $exe).VersionInfo.FileVersion
if ($built -ne $Version) { throw "SUSpector.exe is version $built, expected $Version." }

$commit = ([string](Invoke-Git @('rev-parse', 'HEAD'))).Trim()
# What changed since the previous SUSpector release (before separate tags: since the last common v* tag),
# only commits that touched the programs' code.
$previousTag = Invoke-Git @('describe', '--tags', '--abbrev=0', '--match', 'suspector-v*', '--exclude', "suspector-v$Version", 'HEAD') -AllowFailure
if (-not $previousTag) { $previousTag = Invoke-Git @('describe', '--tags', '--abbrev=0', '--match', 'v*', 'HEAD') -AllowFailure }
$range = if ($previousTag) { @("$(([string]$previousTag).Trim())..HEAD") } else { @('-n', '20', 'HEAD') }
$notes = @(Invoke-Git (@('log', '--pretty=format:%s') + $range + @('--', 'app')) | Where-Object { $_ -and $_ -notmatch '^chore: (SUSpector )?version' })

if (-not $OutputDirectory) { $OutputDirectory = Join-Path $root 'dist' }
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$output = Join-Path $OutputDirectory "SUSpector-$Version.zip"
if (Test-Path -LiteralPath $output) { Remove-Item -LiteralPath $output }

$validator = { param($name) [string]::Equals($name, 'SUSpector.exe', [StringComparison]::OrdinalIgnoreCase) }
$package = New-ReleasePackage -SourceRoot $root -Files @('SUSpector.exe') -Version $Version -Commit $commit -Notes $notes -FileValidator $validator -OutputPath $output
Write-Host "SUSpector ${Version}: $($package.Size) bytes" -ForegroundColor Green
Write-Host $package.Path -ForegroundColor Cyan
Write-Output $package
