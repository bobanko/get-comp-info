# Builds dist\comp-info-<version>.zip from the program files tracked in Git.
# Used by .github/workflows/release.yml; can be run locally to check a release.
[CmdletBinding()]
param(
    # Expected version, e.g. from the Git tag. Must match console-ui.ps1.
    [string]$Version,
    [string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
. (Join-Path $root 'console-ui.ps1')
. (Join-Path $root 'sync-merge.ps1')
. (Join-Path $root 'cloud-client.ps1')
. (Join-Path $PSScriptRoot 'release-package.ps1')

function Invoke-Git {
    param([string[]]$Arguments, [switch]$AllowFailure)
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try { $output = & git -C $root @Arguments 2>$null } finally { $ErrorActionPreference = $previous }
    if ($LASTEXITCODE -ne 0) {
        if ($AllowFailure) { return $null }
        throw "git $($Arguments -join ' ') завершился с кодом $LASTEXITCODE"
    }
    return $output
}

$codeVersion = $script:toolboxVersion
if (-not $Version) { $Version = $codeVersion }
if ($Version -ne $codeVersion) {
    throw "Версия $Version не совпадает с версией в console-ui.ps1 ($codeVersion). Запустите tools/update-version.BAT и закоммитьте изменение."
}

$files = @(Invoke-Git @('ls-files') | Where-Object { $_ -notmatch '/' -and (Test-ProgramFileName $_) })
$commit = ([string](Invoke-Git @('rev-parse', 'HEAD'))).Trim()

# "Что нового": commit subjects since the previous version tag (or the last 20 commits).
$previousTag = Invoke-Git @('describe', '--tags', '--abbrev=0', '--match', 'v*', 'HEAD^') -AllowFailure
$range = if ($previousTag) { @("$(([string]$previousTag).Trim())..HEAD") } else { @('-n', '20', 'HEAD') }
$notes = @(Invoke-Git (@('log', '--pretty=format:%s') + $range) | Where-Object { $_ -and $_ -notmatch '^chore: version' })

if (-not $OutputDirectory) { $OutputDirectory = Join-Path $root 'dist' }
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$output = Join-Path $OutputDirectory "comp-info-$Version.zip"
if (Test-Path -LiteralPath $output) { Remove-Item -LiteralPath $output }

$package = New-ReleasePackage -SourceRoot $root -Files $files -Version $Version -Commit $commit -Notes $notes -OutputPath $output
Write-Host "Версия ${Version}: $($package.Files.Count) файлов, $($package.Size) байт" -ForegroundColor Green
Write-Host $package.Path -ForegroundColor Cyan
Write-Output $package
