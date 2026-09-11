$ErrorActionPreference = 'Stop'
$project = Split-Path -Parent $PSScriptRoot
. (Join-Path $project 'sync-merge.ps1')
$tokens = $null
$errors = $null
$ast = [Management.Automation.Language.Parser]::ParseFile((Join-Path $project 'sync-usb.ps1'), [ref]$tokens, [ref]$errors)
if ($errors.Count) { throw ($errors | Out-String) }
foreach ($name in @('Copy-Verified')) {
    $definition = $ast.Find({ param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq $name }, $true)
    . ([scriptblock]::Create($definition.Extent.Text))
}
function Assert-True([bool]$Value, [string]$Message) {
    if (-not $Value) { throw $Message }
}
function Select-SyncAction {
    param($Title, $Lines, $Items, [switch]$AllowBack)
    if ($script:choices.Count -eq 0) { throw "Unexpected prompt: $Title" }
    return $script:choices.Dequeue()
}
function Set-Choices([int[]]$Values) {
    $script:choices = New-Object 'System.Collections.Generic.Queue[int]'
    foreach ($value in $Values) { $script:choices.Enqueue($value) }
}
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('comp-info-merge-test-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testRoot | Out-Null
try {
    $left = '{"a":{"z":1,"b":2},"list":[1,2],"comment":"A"}' | ConvertFrom-Json
    $right = '{ "comment":"A", "list":[1,2], "a":{"b":2,"z":1} }' | ConvertFrom-Json
    Assert-True ((Get-CanonicalJson $left) -ceq (Get-CanonicalJson $right)) 'Property order should not matter'
    Assert-True (@(Get-SyncDifferences $left $right).Count -eq 0) 'Formatting should not create differences'
    $right.comment = 'a'
    Assert-True (@(Get-SyncDifferences $left $right).Count -eq 1) 'String case must matter'
    $missing = '{}' | ConvertFrom-Json
    $nullField = '{"a":null}' | ConvertFrom-Json
    Assert-True (@(Get-SyncDifferences $missing $nullField).Count -eq 1) 'Missing and null differ'
    Assert-True ((Get-CanonicalJson @(1,2)) -cne (Get-CanonicalJson @(2,1))) 'Array order must matter'
    $merged = Merge-SyncFields $left $missing @('comment')
    Assert-True ($null -eq $merged.PSObject.Properties['comment']) 'An explicit incoming deletion should remove the field'

    $localRoot = Join-Path $testRoot 'local'
    $usbRoot = Join-Path $testRoot 'usb'
    New-Item -ItemType Directory -Path (Join-Path $localRoot 'data'), $usbRoot | Out-Null
    $destination = Join-Path $localRoot 'data\pc.json'
    $source = Join-Path $usbRoot 'pc.json'
    $local = '{"computerId":"id1","hostname":"PC","ip":"old","comment":"local"}' | ConvertFrom-Json
    $incoming = '{"computerId":"id1","hostname":"PC","ip":"new","comment":"usb"}' | ConvertFrom-Json
    $table = @(Get-SyncComparisonLines $local $incoming)
    Assert-True (@($table | Where-Object Color -eq 'Yellow').Count -eq 2) 'Only differing rows should be highlighted'
    Assert-True (@($table | Where-Object { $_.Color -eq 'DarkGray' -and $_.Text -match 'PC' }).Count -eq 1) 'Equal rows should remain visible in gray'
    $parts = @(Split-SyncTableCell ('A' * 75) 30)
    Assert-True (($parts -join '') -eq ('A' * 75) -and $parts.Count -eq 3) 'Wrapping must preserve long values'
    Write-SyncJson $destination $local
    Write-SyncJson $source $incoming
    # Per-field choices: retain local comment, take incoming IP, confirm.
    Set-Choices @(0,0,1,0)
    $status = Import-SyncRecord $source $destination $localRoot
    Assert-True ($status -eq 'Merged') 'Expected field merge'
    $record = Read-SyncRecord $destination
    Assert-True ($record.ip -eq 'new' -and $record.comment -eq 'local') 'Field merge lost a choice'
    $backups = @(Get-ChildItem (Join-Path $localRoot 'sync-backups') -Recurse -Filter '*.json')
    Assert-True ($backups.Count -eq 1) 'Expected one backup'
    Assert-True ((Get-CanonicalJson (Read-SyncRecord $backups[0].FullName)) -ceq (Get-CanonicalJson $local)) 'Backup must preserve original'
    Set-Choices @()
    Assert-True ((Import-SyncRecord $source $destination $localRoot) -eq 'Unchanged') 'Resolved version should not prompt again'
    Assert-True ((Read-SyncRecord $source).comment -eq 'usb') 'USB record should stay unchanged'

    $incoming.comment = 'next'
    Write-SyncJson $source $incoming
    Set-Choices @(3)
    Assert-True ((Import-SyncRecord $source $destination $localRoot) -eq 'Deferred') 'Expected defer'
    $archive = Join-Path $localRoot ('sync-conflicts\pc\' + (Get-SyncHash (Get-CanonicalJson $incoming)) + '.json')
    Assert-True (Test-Path -LiteralPath $archive) 'Deferred record must be available offline'
    Assert-True ((Get-PendingSyncConflictCount $localRoot) -eq 1) 'Count must exclude resolved archives'
    Copy-Item -LiteralPath $archive -Destination (Join-Path (Split-Path -Parent $archive) 'duplicate.json')
    Assert-True ((Get-PendingSyncConflictCount $localRoot) -eq 1) 'Duplicate archives must count once'
    Set-Choices @(2,0)
    Assert-True ((Import-SyncRecord $archive $destination $localRoot -Archived) -eq 'Kept') 'Expected keep local'
    Set-Choices @()
    Assert-True ((Import-SyncRecord $source $destination $localRoot) -eq 'Unchanged') 'Keep decision should be remembered'
    Assert-True ((Get-PendingSyncConflictCount $localRoot) -eq 0) 'Resolved conflicts must disappear from count'

    $incoming.comment = 'third'
    Write-SyncJson $source $incoming
    # Return from confirmation, then accept incoming and confirm.
    Set-Choices @(2,-1,1,0)
    Assert-True ((Import-SyncRecord $source $destination $localRoot) -eq 'Merged') 'Confirmation back navigation failed'
    Assert-True ((Read-SyncRecord $destination).comment -eq 'third') 'Whole-record acceptance failed'

    $newDestination = Join-Path $localRoot 'data\new.json'
    Set-Choices @()
    Assert-True ((Import-SyncRecord $source $newDestination $localRoot) -eq 'Added') 'New records should be automatic'
    $differentIdentity = '{"computerId":"other"}' | ConvertFrom-Json
    Write-SyncJson $source $differentIdentity
    $failed = $false
    try { Import-SyncRecord $source $destination $localRoot | Out-Null } catch { $failed = $true }
    Assert-True $failed 'Different identities should fail'
    [IO.File]::WriteAllText($source, '{broken')
    $failed = $false
    try { Import-SyncRecord $source $destination $localRoot | Out-Null } catch { $failed = $true }
    Assert-True $failed 'Invalid JSON should fail'

    # Run the actual entry point in isolated folders, with no interactive differences.
    foreach ($name in @('sync-usb.ps1','sync-merge.ps1','console-ui.ps1')) {
        Copy-Item -LiteralPath (Join-Path $project $name) -Destination (Join-Path $localRoot $name)
    }
    $portableData = Join-Path $usbRoot 'data'
    New-Item -ItemType Directory -Path $portableData | Out-Null
    Write-SyncJson (Join-Path $portableData 'fresh.json') $local
    $output = & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $localRoot 'sync-usb.ps1') -UsbPath $usbRoot 2>&1
    Assert-True ($LASTEXITCODE -eq 0) ($output | Out-String)
    Assert-True (Test-Path (Join-Path $localRoot 'data\fresh.json')) 'Entry point did not import'
    Assert-True (Test-Path (Join-Path $usbRoot 'sync-merge.ps1')) 'Merge helper must be deployed'
    $offlinePath = Join-Path $testRoot 'unavailable-usb'
    $output = & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $localRoot 'sync-usb.ps1') -ReviewConflicts -UsbPath $offlinePath 2>&1
    Assert-True ($LASTEXITCODE -eq 0) ($output | Out-String)
    Assert-True (-not (Test-Path $offlinePath)) 'Offline review must not create a USB folder'
    Write-Host 'All merge and isolated integration checks passed.'
}
finally {
    $resolved = [IO.Path]::GetFullPath($testRoot)
    $tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    if (-not $resolved.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase) -or
        (Split-Path -Leaf $resolved) -notlike 'comp-info-merge-test-*') { throw 'Unsafe test cleanup path' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
