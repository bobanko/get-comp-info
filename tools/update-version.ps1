[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$versionFile = Join-Path (Split-Path -Parent $PSScriptRoot) 'console-ui.ps1'
$content = [IO.File]::ReadAllText($versionFile)
$pattern = '(?m)^\$script:toolboxVersion = ''(?<date>\d{4}\.\d{2}\.\d{2})\.(?<revision>\d+)''\r?$'
$matchesFound = [regex]::Matches($content, $pattern)
if ($matchesFound.Count -ne 1) { throw 'Expected exactly one release version in console-ui.ps1.' }
$current = $matchesFound[0]
$releaseDate = Get-Date -Format 'yyyy.MM.dd'
if ([string]::CompareOrdinal($releaseDate, $current.Groups['date'].Value) -lt 0) {
    throw 'The current date is earlier than the recorded release date. Check the system clock.'
}
$revision = if ($current.Groups['date'].Value -eq $releaseDate) {
    [int]$current.Groups['revision'].Value + 1
} else { 1 }
$version = "$releaseDate.$revision"
$replacement = '$script:toolboxVersion = ' + "'$version'"
if ($current.Value.EndsWith("`r")) { $replacement += "`r" }
$updated = $content.Remove($current.Index, $current.Length).Insert($current.Index, $replacement)
[IO.File]::WriteAllText($versionFile, $updated, [Text.UTF8Encoding]::new($true))
Write-Output "Release version: $version"
