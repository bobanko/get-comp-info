[CmdletBinding()]
param(
    # Whose version to raise: the toolbox (console-ui.ps1, also the console version), SUSpector or youSBee.
    [ValidateSet('toolbox', 'suspector', 'yousbee')]
    [string]$Program = 'toolbox'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

# Next release number YYYY.MM.DD.N: N grows within a day and starts from 1 on a new day.
function Get-NextVersion {
    param([string]$Current)
    if ($Current -notmatch '^(?<date>\d{4}\.\d{2}\.\d{2})\.(?<revision>\d+)$') { throw "Unexpected version: $Current" }
    $releaseDate = Get-Date -Format 'yyyy.MM.dd'
    if ([string]::CompareOrdinal($releaseDate, $Matches['date']) -lt 0) {
        throw 'The current date is earlier than the recorded release date. Check the system clock.'
    }
    $revision = if ($Matches['date'] -eq $releaseDate) { [int]$Matches['revision'] + 1 } else { 1 }
    return "$releaseDate.$revision"
}

if ($Program -eq 'toolbox') {
    $versionFile = Join-Path $root 'console-ui.ps1'
    $content = [IO.File]::ReadAllText($versionFile)
    $pattern = '(?m)^\$script:toolboxVersion = ''(?<version>\d{4}\.\d{2}\.\d{2}\.\d+)''\r?$'
    $matchesFound = [regex]::Matches($content, $pattern)
    if ($matchesFound.Count -ne 1) { throw 'Expected exactly one release version in console-ui.ps1.' }
    $current = $matchesFound[0]
    $version = Get-NextVersion $current.Groups['version'].Value
    $replacement = '$script:toolboxVersion = ' + "'$version'"
    if ($current.Value.EndsWith("`r")) { $replacement += "`r" }
    $updated = $content.Remove($current.Index, $current.Length).Insert($current.Index, $replacement)
    [IO.File]::WriteAllText($versionFile, $updated, [Text.UTF8Encoding]::new($true))
}
else {
    $name = if ($Program -eq 'suspector') { 'SUSpector' } else { 'youSBee' }
    $versionFile = Join-Path $root "app\$name.version"
    $version = Get-NextVersion ([IO.File]::ReadAllText($versionFile).Trim())
    [IO.File]::WriteAllText($versionFile, "$version`r`n", [Text.UTF8Encoding]::new($false))
}
Write-Output "Release version: $version"
