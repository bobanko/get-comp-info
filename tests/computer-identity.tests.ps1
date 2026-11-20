$ErrorActionPreference = 'Stop'
$project = Split-Path -Parent $PSScriptRoot
. (Join-Path $project 'console\computer-identity.ps1')
function Assert-True($Value, $Message) { if (-not $Value) { throw $Message } }
$root = Join-Path $PSScriptRoot ('identity-test-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $root | Out-Null
try {
    $uuid = '11111111-2222-3333-4444-555555555555'
    $machine = 'AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE'
    $result = Get-InventoryIdentity -Uuid $uuid -Serial 'SN1' -InstallationId $machine -DataPath $root
    Assert-True ($result.Id -eq $uuid -and $result.Source -eq 'smbiosUuid') 'UUID not preferred'
    $result = Get-InventoryIdentity -Uuid '00000000-0000-0000-0000-000000000000' -Serial 'SN1' -InstallationId $machine -DataPath $root
    Assert-True ($result.Id -eq 'SN1' -and $result.Source -eq 'serial') 'Serial fallback failed'
    $result = Get-InventoryIdentity -Serial 'Default string' -InstallationId $machine -DataPath $root
    Assert-True ($result.Id -eq "WINDOWS-$machine" -and $result.Source -eq 'windowsMachineGuid') 'Windows fallback failed'
    $legacyPath = Join-Path $root 'OLD-PC.json'
    @{ computerId = 'OLD-PC'; computerIdSource = 'hostname'; hostname = 'OLD-PC'; windowsMachineGuid = $machine } | ConvertTo-Json | Set-Content -LiteralPath $legacyPath -Encoding UTF8
    $result = Get-InventoryIdentity -InstallationId $machine -DataPath $root
    Assert-True ($result.Id -eq 'OLD-PC' -and $result.Source -eq 'legacyId' -and $result.Path -eq $legacyPath) 'Existing cloud key was not preserved'
    @{ computerId = 'OLD-PC'; computerIdSource = 'legacyId'; hostname = 'FR-042-NEW'; windowsMachineGuid = $machine } | ConvertTo-Json | Set-Content -LiteralPath $legacyPath -Encoding UTF8
    $result = Get-InventoryIdentity -InstallationId $machine -DataPath $root
    Assert-True ($result.Id -eq 'OLD-PC') 'Rename changed identity'
    @{ hostname = 'BEFORE'; serial = 'SN1' } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $root 'SN1.json') -Encoding UTF8
    $result = Get-InventoryIdentity -Uuid $uuid -Serial 'SN1' -DataPath $root
    Assert-True ($result.Id -eq 'SN1' -and $result.Source -eq 'legacyId') 'Legacy serial key was changed to UUID'
    @{ hostname = 'UNBOUND'; serial = 'Unknown' } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $root 'UNBOUND.json') -Encoding UTF8
    $unbound = @(Find-UnboundLegacyInventory $root 'UNBOUND')
    Assert-True ($unbound.Count -eq 1 -and $unbound[0].Id -eq 'UNBOUND') 'Unbound legacy candidate not detected'
    $result = Get-InventoryIdentity -InstallationId 'BBBBBBBB-BBBB-CCCC-DDDD-EEEEEEEEEEEE' -DataPath $root
    Assert-True ($result.Source -eq 'windowsMachineGuid' -and -not $result.Path) 'Hostname used as automatic fallback'
    $failed = $false
    try { Get-InventoryIdentity -DataPath $root | Out-Null } catch { $failed = $true }
    Assert-True $failed 'Missing identifiers silently accepted'
    @{ computerId = 'DUP'; windowsMachineGuid = $machine } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $root 'DUP.json') -Encoding UTF8
    $failed = $false
    $message = ''
    try { Get-InventoryIdentity -InstallationId $machine -DataPath $root | Out-Null } catch { $failed = $true; $message = $_.Exception.Message }
    Assert-True $failed 'Ambiguous records were merged automatically'
    Assert-True ($message.Contains('https://comp-info.chagear.top/#/devices/DUP') -and $message.Contains('/#/devices/OLD-PC')) "The web cards must be linked: $message"
    Remove-Item -LiteralPath (Join-Path $root 'DUP.json')

    # One record in two files: kept in the file of its id, with the newest collection and the earliest creation.
    @{ computerId = 'OLD-PC'; hostname = 'FR-042-NEW'; windowsMachineGuid = $machine; collectedAt = '2026-11-20T23:10:00+02:00'; createdAt = '2026-11-20T23:10:00+02:00' } |
        ConvertTo-Json | Set-Content -LiteralPath (Join-Path $root 'COPY.json') -Encoding UTF8
    @{ computerId = 'OLD-PC'; computerIdSource = 'legacyId'; hostname = 'FR-042-OLD'; windowsMachineGuid = $machine; comment = 'old note'
        collectedAt = '2026-11-17T11:45:00+02:00'; createdAt = '2026-11-01T09:00:00+02:00' } | ConvertTo-Json | Set-Content -LiteralPath $legacyPath -Encoding UTF8
    $result = Get-InventoryIdentity -InstallationId $machine -DataPath $root
    $kept = Get-Content -LiteralPath $legacyPath -Raw -Encoding UTF8 | ConvertFrom-Json
    Assert-True ($result.Id -eq 'OLD-PC' -and $result.Path -eq $legacyPath -and -not (Test-Path (Join-Path $root 'COPY.json'))) 'One record in two files must be merged'
    Assert-True ($kept.hostname -eq 'FR-042-NEW' -and $kept.comment -eq 'old note' -and $kept.createdAt -eq '2026-11-01T09:00:00+02:00') "Merged values: $($kept | ConvertTo-Json -Compress)"
    Assert-True (@(Get-ChildItem (Join-Path $root '.duplicates') -Recurse -File).Count -eq 2) 'Both files must be kept in .duplicates'
    'Computer identity checks passed.'
}
finally {
    $resolved = [IO.Path]::GetFullPath($root)
    if (-not $resolved.StartsWith([IO.Path]::GetFullPath($PSScriptRoot) + [IO.Path]::DirectorySeparatorChar)) { throw 'Unexpected test directory' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
