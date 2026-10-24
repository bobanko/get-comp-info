$ErrorActionPreference = 'Stop'
$project = Split-Path -Parent $PSScriptRoot
. (Join-Path $project 'console-ui.ps1')
. (Join-Path $project 'sync-merge.ps1')
. (Join-Path $project 'cloud-client.ps1')

function Assert-True([bool]$Value, [string]$Message) {
    if (-not $Value) { throw $Message }
}

# ---------- Test doubles: console and the cloud API ----------

function Write-Host { }
function Clear-Host { }
function Select-SyncAction {
    param($Title, $Lines, $Items, [switch]$AllowBack, $Section)
    if ($script:choices.Count -eq 0) { throw "Unexpected prompt: $Title" }
    return $script:choices.Dequeue()
}
function Set-Choices([int[]]$Values) {
    $script:choices = New-Object 'System.Collections.Generic.Queue[int]'
    foreach ($value in $Values) { $script:choices.Enqueue($value) }
}
function Read-MaskedInput {
    if ($script:typedKeys.Count -eq 0) { throw 'Unexpected key prompt' }
    return $script:typedKeys.Dequeue()
}
function Set-TypedKeys([object[]]$Values) {
    $script:typedKeys = New-Object 'System.Collections.Generic.Queue[object]'
    foreach ($value in $Values) { $script:typedKeys.Enqueue($value) }
}

$script:api = 'https://fake.test/api/client'

function Reset-Server {
    $script:server = @{
        Devices = [hashtable]::new([StringComparer]::Ordinal)
        Change = 0
        Keys = @{ 'good-key' = @{ email = 'h@example.com'; name = 'Tester'; role = 'harvester' } }
        Requests = 0
        Network = $null
    }
}

function New-Response([int]$Status, $Body) {
    $json = if ($null -eq $Body) { $null } else { ConvertTo-Json -InputObject $Body -Depth 100 }
    return [pscustomobject]@{ Status = $Status; Body = if ($json) { $json | ConvertFrom-Json } else { $null } }
}

function Get-ServerDevice([string]$Id) {
    $device = $script:server.Devices[$Id]
    return @{
        id = $Id; revision = $device.revision; deleted = $device.deleted
        updatedAt = '2026-10-23T10:00:00Z'; updatedBy = $device.updatedBy
        data = ($device.data | ConvertFrom-Json)
    }
}

function Write-ServerDevice([string]$Id, $Data, [bool]$Deleted, [string]$By) {
    $current = $script:server.Devices[$Id]
    $script:server.Change++
    $script:server.Devices[$Id] = @{
        revision = if ($current) { $current.revision + 1 } else { 1 }
        data = (ConvertTo-Json -InputObject $Data -Depth 100 -Compress)
        deleted = $Deleted; changeId = $script:server.Change; updatedBy = $By
    }
}

# Mirrors cloud/src/devices.ts putDevice and listChanges.
function Invoke-CloudRequest {
    param([string]$Method, [string]$Url, [string]$Key, $Body)
    $script:server.Requests++
    if ($script:offline) { throw "Облако недоступно ($Url): test" }
    $user = $script:server.Keys[$Key]
    if (-not $user) { return New-Response 401 @{ error = @{ code = 'invalid_key'; message = 'Ключ неверный или отозван.' } } }
    # Parse the URL the way HttpWebRequest sends it, including .NET path normalization.
    $path = ([Uri]$Url).PathAndQuery.Substring(([Uri]$script:api).AbsolutePath.Length)
    if ($Method -eq 'GET' -and $path -eq '/me') { return New-Response 200 $user }
    if ($Method -eq 'GET' -and $path -match '^/devices\?since=(\d+)&limit=(\d+)$') {
        $since = [long]$Matches[1]; $limit = [int]$Matches[2]
        $changed = @($script:server.Devices.Keys | Where-Object { $script:server.Devices[$_].changeId -gt $since } |
            Sort-Object { $script:server.Devices[$_].changeId })
        $page = @($changed | Select-Object -First $limit)
        $cursor = if ($page.Count) { $script:server.Devices[$page[-1]].changeId } else { $since }
        return New-Response 200 @{ cursor = $cursor; hasMore = ($changed.Count -gt $limit); devices = @($page | ForEach-Object { Get-ServerDevice $_ }) }
    }
    if ($Method -eq 'PUT' -and $path -match '^/devices/(.+)$') {
        $id = [Uri]::UnescapeDataString($Matches[1])
        $request = ConvertTo-Json -InputObject $Body -Depth 100 | ConvertFrom-Json
        $data = $request.data
        [void](Remove-DeprecatedFields $data)
        if ($data.computerId -and $data.computerId -ne $id) {
            return New-Response 400 @{ error = @{ code = 'id_mismatch'; message = 'computerId не совпадает.' } }
        }
        $current = $script:server.Devices[$id]
        if ($current) {
            if ($current.deleted) {
                if (-not $request.restore) { return New-Response 410 @{ error = @{ code = 'deleted'; message = 'Удалена.' }; current = (Get-ServerDevice $id) } }
            }
            else {
                if ((Get-CanonicalJson $data) -ceq (Get-CanonicalJson ($current.data | ConvertFrom-Json))) {
                    return New-Response 200 @{ revision = $current.revision; changed = $false; device = (Get-ServerDevice $id) }
                }
                if ($request.baseRevision -ne $current.revision) {
                    return New-Response 409 @{ error = @{ code = 'conflict'; message = 'Конфликт.' }; current = (Get-ServerDevice $id) }
                }
            }
        }
        Write-ServerDevice $id $data $false $user.email
        $device = Get-ServerDevice $id
        return New-Response 200 @{ revision = $device.revision; changed = $true; device = $device }
    }
    if ($Method -eq 'GET' -and $path -eq '/settings/network') {
        if ($null -eq $script:server.Network) { return New-Response 404 @{ error = @{ code = 'no_settings'; message = 'Не заданы.' } } }
        return New-Response 200 $script:server.Network
    }
    return New-Response 404 @{ error = @{ code = 'not_found'; message = 'Неизвестный адрес API.' } }
}

# ---------- Helpers ----------

function New-ProgramCopy([string]$Name) {
    $root = Join-Path $testRoot $Name
    New-Item -ItemType Directory -Path (Join-Path $root 'data') -Force | Out-Null
    [IO.File]::WriteAllText((Join-Path $root 'cloud-config.json'), "{ `"apiUrl`": `"$script:api/`" }")
    [IO.File]::WriteAllText((Join-Path $root 'cloud-key.txt'), "good-key`r`n")
    return $root
}

function Write-Record([string]$Root, [string]$File, [string]$Json) {
    Write-SyncJson (Join-Path $Root "data\$File") ($Json | ConvertFrom-Json)
}

function Read-Record([string]$Root, [string]$File) {
    return Read-SyncRecord (Join-Path $Root "data\$File")
}

function Get-Status([string]$Root, [string]$File) {
    $state = Get-CloudStateForDisplay $Root
    return Get-CloudStatus (Read-Record $Root $File) $File $state
}

function Sync([string]$Root, [int[]]$Choices = @()) {
    Set-Choices $Choices
    $counts = Invoke-CloudSync -Root $Root
    Assert-True ($script:choices.Count -eq 0) "Not all prepared choices were used ($($script:choices.Count) left)"
    return $counts
}

$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('comp-info-cloud-test-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testRoot | Out-Null
try {
    # ---------- Pure helpers ----------
    $left = '{"a":{"z":1,"b":2},"list":[1,2],"comment":"A"}' | ConvertFrom-Json
    $right = '{ "comment":"A", "list":[1,2], "a":{"b":2,"z":1} }' | ConvertFrom-Json
    Assert-True ((Get-RecordHash $left) -eq (Get-RecordHash $right)) 'Key order must not change the hash'
    Assert-True (@(Get-SyncDifferences ('{}' | ConvertFrom-Json) ('{"a":null}' | ConvertFrom-Json)).Count -eq 1) 'Missing and null differ'
    Assert-True ((Get-CloudRecordId ('{"computerId":"UUID-1"}' | ConvertFrom-Json) 'x.json') -ceq 'UUID-1') 'computerId is the id'
    Assert-True ((Get-CloudRecordId ('{"computerId":""}' | ConvertFrom-Json) 'SERIAL1.json') -ceq 'SERIAL1') 'Legacy id is the file name'
    Assert-True ((Get-SafeRecordFileName 'a/b:c.') -ceq 'a_b_c..json') 'Only forbidden file name characters are replaced'
    $merged = Merge-SyncFields $left ('{}' | ConvertFrom-Json) @('comment')
    Assert-True ($null -eq $merged.PSObject.Properties['comment']) 'Taking a missing field removes it'
    # A file held open the way antivirus scanners do (no delete sharing) must still be updated.
    $lockedPath = Join-Path $testRoot 'locked.json'
    Write-SyncJson $lockedPath ('{"v":1}' | ConvertFrom-Json)
    $handle = [IO.File]::Open($lockedPath, 'Open', 'Read', [IO.FileShare]::ReadWrite)
    try { Write-SyncJson $lockedPath ('{"v":2}' | ConvertFrom-Json) -Attempts 2 }
    finally { $handle.Dispose() }
    Assert-True ((Read-SyncRecord $lockedPath).v -eq 2) 'A file locked by a scanner must still be written'
    Assert-True (@(Get-ChildItem $testRoot -Filter '*.tmp').Count -eq 0) 'No temporary files may be left behind'
    $table = @(Get-SyncComparisonLines ('{"hostname":"PC","ip":"1"}' | ConvertFrom-Json) ('{"hostname":"PC","ip":"2"}' | ConvertFrom-Json))
    Assert-True (@($table | Where-Object Color -eq 'Yellow').Count -eq 1) 'Only differing rows are highlighted'

    # ---------- First upload from the flash drive ----------
    Reset-Server
    $flash = New-ProgramCopy 'flash'
    Write-Record $flash 'UUID-1.json' '{"computerId":"UUID-1","hostname":"PC-1","ip":"10.0.0.1","comment":"","addedToExternalDatabase":true,"addedToExternalDatabaseAt":"2026-08-28"}'
    Write-Record $flash 'SERIAL2.json' '{"hostname":"OLD-PC","serial":"SERIAL2","comment":"legacy"}'
    Assert-True ((Get-Status $flash 'UUID-1.json') -eq 'Нет в облаке') 'New record must not be in the cloud yet'

    $counts = Sync $flash
    Assert-True ($counts.PushedNew -eq 2 -and $counts.Errors.Count -eq 0) "Expected two new records: $($counts | Out-String)"
    Assert-True ($script:server.Devices.ContainsKey('SERIAL2')) 'Legacy record must use the file name as id'
    Assert-True ($null -eq (Read-Record $flash 'UUID-1.json').PSObject.Properties['addedToExternalDatabase']) 'Deprecated fields must be removed locally'
    Assert-True (-not $script:server.Devices['UUID-1'].data.Contains('addedToExternalDatabase')) 'Deprecated fields must not reach the cloud'
    Assert-True ((Get-Status $flash 'UUID-1.json') -eq 'В облаке') 'Uploaded record must be in the cloud'

    $requests = $script:server.Requests
    $counts = Sync $flash
    Assert-True ($counts.PushedNew + $counts.PushedUpdated + $counts.PushedSame -eq 0) 'Unchanged records must not be sent again'
    Assert-True ($script:server.Requests - $requests -eq 4) 'A quiet sync is a key check, the change feed, network profiles and a version check'
    Assert-True ($counts.NetworkProfiles -eq 'missing' -and -not (Test-Path (Join-Path $flash 'network-profiles.json'))) 'No profiles in the cloud means no cache'

    # ---------- The computer downloads everything ----------
    $pc = New-ProgramCopy 'pc'
    $counts = Sync $pc
    Assert-True ($counts.ReceivedNew -eq 2) 'The computer must receive both records'
    Assert-True ((Read-Record $pc 'SERIAL2.json').comment -eq 'legacy') 'Legacy record must be saved under its id'
    Assert-True ((Get-Status $pc 'UUID-1.json') -eq 'В облаке') 'Downloaded record is in sync'

    # ---------- Edit on the computer, deliver to the flash drive ----------
    $record = Read-Record $pc 'UUID-1.json'
    $record.comment = 'выдан бухгалтерии'
    Write-SyncJson (Join-Path $pc 'data\UUID-1.json') $record
    Assert-True ((Get-Status $pc 'UUID-1.json') -eq 'Изменено') 'Local edit must show as changed'
    $counts = Sync $pc
    Assert-True ($counts.PushedUpdated -eq 1) 'Edited record must be uploaded'
    Assert-True ($script:server.Devices['UUID-1'].revision -eq 2) 'Upload must create revision 2'
    $counts = Sync $flash
    Assert-True ($counts.ReceivedUpdated -eq 1) 'The flash drive must receive the edit'
    Assert-True ((Read-Record $flash 'UUID-1.json').comment -eq 'выдан бухгалтерии') 'Cyrillic text must survive the round trip'

    # ---------- Conflict resolved field by field ----------
    $onFlash = Read-Record $flash 'UUID-1.json'; $onFlash.ip = '10.0.0.99'; Write-SyncJson (Join-Path $flash 'data\UUID-1.json') $onFlash
    $onPc = Read-Record $pc 'UUID-1.json'; $onPc.comment = 'перенесён'; Write-SyncJson (Join-Path $pc 'data\UUID-1.json') $onPc
    [void](Sync $pc)
    # Fields in order: comment, ip. Take the cloud comment, keep the local IP, confirm.
    $counts = Sync $flash @(0, 1, 0, 0)
    Assert-True ($counts.PushedUpdated -eq 1) 'Merged record must be uploaded'
    $cloud = $script:server.Devices['UUID-1'].data | ConvertFrom-Json
    Assert-True ($cloud.ip -eq '10.0.0.99' -and $cloud.comment -eq 'перенесён') 'Both changes must be kept after the merge'
    Assert-True ((Get-Status $flash 'UUID-1.json') -eq 'В облаке') 'Merged record is in sync'

    # ---------- Deferred conflict stays local, then the cloud version wins ----------
    Write-ServerDevice 'UUID-1' (($script:server.Devices['UUID-1'].data | ConvertFrom-Json) | Add-Member -NotePropertyName model -NotePropertyValue 'web edit' -PassThru) $false 'editor@example.com'
    $onFlash = Read-Record $flash 'UUID-1.json'; $onFlash.comment = 'offline note'; Write-SyncJson (Join-Path $flash 'data\UUID-1.json') $onFlash
    $counts = Sync $flash @(3)
    Assert-True ($counts.Deferred -eq 1) 'Expected a deferred conflict'
    Assert-True ((Read-Record $flash 'UUID-1.json').comment -eq 'offline note') 'Deferred local change must not be overwritten by the download'
    Assert-True ((Get-Status $flash 'UUID-1.json') -eq 'Изменено') 'Deferred record stays changed'
    $counts = Sync $flash @(1, 0)
    Assert-True ($counts.ReceivedUpdated -eq 1) 'Taking the cloud version counts as an update from the cloud'
    $local = Read-Record $flash 'UUID-1.json'
    Assert-True ($local.model -eq 'web edit' -and $local.comment -eq 'перенесён') 'Local record must equal the cloud version'
    Assert-True ((Get-Status $flash 'UUID-1.json') -eq 'В облаке') 'Record taken from the cloud is in sync'

    # ---------- Sending the local version over a newer cloud one ----------
    Write-ServerDevice 'UUID-1' (($script:server.Devices['UUID-1'].data | ConvertFrom-Json) | Add-Member -NotePropertyName username -NotePropertyValue 'web' -Force -PassThru) $false 'editor@example.com'
    $onFlash = Read-Record $flash 'UUID-1.json'; $onFlash.comment = 'final'; Write-SyncJson (Join-Path $flash 'data\UUID-1.json') $onFlash
    $revision = $script:server.Devices['UUID-1'].revision
    [void](Sync $flash @(2, 0))
    Assert-True ($script:server.Devices['UUID-1'].revision -eq $revision + 1) 'Local version must be sent as a new revision'
    Assert-True ((($script:server.Devices['UUID-1'].data | ConvertFrom-Json).comment) -eq 'final') 'Local version must win'

    # ---------- Deletions in the cloud ----------
    Write-ServerDevice 'SERIAL2' ($script:server.Devices['SERIAL2'].data | ConvertFrom-Json) $true 'admin@example.com'
    $counts = Sync $pc
    Assert-True ($counts.Deleted -eq 1 -and -not (Test-Path (Join-Path $pc 'data\SERIAL2.json'))) 'Unchanged deleted record must be removed locally'
    $onFlash = Read-Record $flash 'SERIAL2.json'; $onFlash.comment = 'still here'; Write-SyncJson (Join-Path $flash 'data\SERIAL2.json') $onFlash
    $counts = Sync $flash @(0)
    Assert-True (-not $script:server.Devices['SERIAL2'].deleted) 'Restoring must undelete the cloud record'
    Assert-True ((($script:server.Devices['SERIAL2'].data | ConvertFrom-Json).comment) -eq 'still here') 'Restored record keeps the local data'
    $counts = Sync $pc
    Assert-True ($counts.ReceivedNew -eq 1 -and (Test-Path (Join-Path $pc 'data\SERIAL2.json'))) 'Restored record must come back to the computer'

    # ---------- A locally deleted file comes back ----------
    Remove-Item -LiteralPath (Join-Path $pc 'data\UUID-1.json')
    $counts = Sync $pc
    Assert-True (Test-Path (Join-Path $pc 'data\UUID-1.json')) 'Local deletion must not delete in the cloud'
    Assert-True (-not $script:server.Devices['UUID-1'].deleted) 'Cloud record must stay'

    # ---------- Broken and duplicate files do not stop the others ----------
    [IO.File]::WriteAllText((Join-Path $pc 'data\broken.json'), '{broken')
    Write-Record $pc 'copy-of-uuid.json' '{"computerId":"UUID-1","hostname":"PC-1 copy","addedToExternalDatabase":true}'
    Write-Record $pc 'NEW-3.json' '{"computerId":"NEW-3","hostname":"PC-3"}'
    $counts = Sync $pc
    Assert-True ($counts.PushedNew -eq 1) 'Valid record must still be uploaded'
    Assert-True ($counts.Errors.Count -eq 2) "Expected errors for the broken and duplicate files: $($counts.Errors -join '; ')"
    Assert-True (@($counts.Errors | Where-Object { $_ -like '*UUID-1*' }).Count -eq 1) 'Duplicate id must be reported'
    Assert-True ($null -eq (Read-Record $pc 'copy-of-uuid.json').PSObject.Properties['addedToExternalDatabase']) 'Deprecated fields must be removed even from records that are not sent'
    Remove-Item -LiteralPath (Join-Path $pc 'data\broken.json'), (Join-Path $pc 'data\copy-of-uuid.json')

    # ---------- Ids that .NET would otherwise change in the URL ----------
    $odd = New-ProgramCopy 'odd'
    Write-Record $odd 'To be filled by O.E.M..json' '{"hostname":"MSI-PC","serial":"To be filled by O.E.M."}'
    $counts = Sync $odd
    Assert-True ($script:server.Devices.ContainsKey('To be filled by O.E.M.')) 'Trailing dot must reach the server'
    Assert-True ($counts.PushedNew -eq 1) 'Record with a trailing dot must be uploaded'
    Assert-True (-not (Test-Path -LiteralPath (Join-Path $odd 'data\To be filled by O.E.M.json'))) 'Record with a trailing dot must not come back as a second file'
    Assert-True ((Get-Status $odd 'To be filled by O.E.M..json') -eq 'В облаке') 'Record with a trailing dot is in sync'
    $oddCopy = New-ProgramCopy 'odd-copy'
    [void](Sync $oddCopy)
    Assert-True ((Get-Status $oddCopy 'To be filled by O.E.M..json') -eq 'В облаке') 'Downloaded legacy record must keep its id in the file name'
    $counts = Sync $oddCopy
    Assert-True ($counts.PushedNew -eq 0) 'Downloaded legacy record must not be uploaded again as a new one'
    $url = Get-DeviceUrl ([pscustomobject]@{ ApiUrl = $script:api }) 'A. b/c\d?e%f.'
    Assert-True ([Net.HttpWebRequest]::Create($url).RequestUri.AbsoluteUri -ceq $url) ".NET must keep the device URL as built: $url"
    Write-ServerDevice 'To be filled by O.E.M.' ($script:server.Devices['To be filled by O.E.M.'].data | ConvertFrom-Json) $true 'admin@example.com'

    # ---------- Network profiles are cached for offline LAN setup ----------
    $script:server.Network = @{ key = 'network'; revision = 1; updatedAt = '2026-10-24T10:00:00Z'; updatedBy = 'admin@example.com'
        value = @{ firstOctet = 10; segments = @(20); domainCheckHost = $null
            locations = @(@{ name = 'Корпус'; thirdOctet = 5; prefixLength = 25; gatewayOctet = 129; firstHostOctet = 130; lastHostOctet = 254; dnsServers = @('10.9.0.1') }) } }
    $counts = Sync $pc
    $cache = Join-Path $pc 'network-profiles.json'
    Assert-True ($counts.NetworkProfiles -eq 'updated' -and (Read-SyncRecord $cache).value.locations[0].name -eq 'Корпус') 'Profiles must be saved to the cache'
    $written = (Get-Item $cache).LastWriteTimeUtc
    Start-Sleep -Milliseconds 50
    Assert-True ((Sync $pc).NetworkProfiles -eq 'unchanged' -and (Get-Item $cache).LastWriteTimeUtc -eq $written) 'Unchanged profiles must not be rewritten'
    $script:server.Network.revision = 2
    $script:server.Network.value.segments = @(20, 21)
    Assert-True ((Sync $pc).NetworkProfiles -eq 'updated' -and (@((Read-SyncRecord $cache).value.segments) -join ',') -eq '20,21') 'Changed profiles must replace the cache'
    $script:server.Network = $null
    Assert-True ((Sync $pc).NetworkProfiles -eq 'missing' -and (Test-Path $cache)) 'The cache is kept when the cloud has no profiles'

    # ---------- Paging ----------
    $script:CloudPageSize = 2
    $fresh = New-ProgramCopy 'fresh'
    $counts = Sync $fresh
    Assert-True ($counts.ReceivedNew -eq 3) 'Paged download must fetch every record'
    $script:CloudPageSize = 200

    # ---------- Personal key from the console ----------
    $typed = New-ProgramCopy 'typed'
    Remove-Item -LiteralPath (Join-Path $typed 'cloud-key.txt')
    Set-TypedKeys @('wrong-key', 'good-key')
    $counts = Sync $typed @(0)
    Assert-True ((Get-CloudKeyFromFile $typed) -eq 'good-key') 'Accepted key must be saved on request'
    Assert-True ($counts.ReceivedNew -eq 3) 'Sync must continue after the key is accepted'
    Remove-Item -LiteralPath (Join-Path $typed 'cloud-key.txt')
    Set-TypedKeys @('good-key')
    [void](Sync $typed @(1))
    Assert-True (-not (Test-Path (Join-Path $typed 'cloud-key.txt'))) 'Key must not be saved when declined'
    Set-TypedKeys @($null)
    Assert-True ($null -eq (Invoke-CloudSync -Root $typed)) 'Esc must cancel the sync'
    [IO.File]::WriteAllText((Join-Path $typed 'cloud-key.txt'), 'revoked-key')
    Set-TypedKeys @('good-key')
    [void](Sync $typed @(0))
    Assert-True ((Get-CloudKeyFromFile $typed) -eq 'good-key') 'A rejected key file must be replaceable'

    # ---------- Another cloud address resets the state, local data stays ----------
    [IO.File]::WriteAllText((Join-Path $flash 'cloud-config.json'), '{ "apiUrl": "https://other.test/api/client" }')
    Assert-True ((Get-Status $flash 'UUID-1.json') -eq 'Нет в облаке') 'State of another cloud must not be used'
    Assert-True (Test-Path (Join-Path $flash 'data\UUID-1.json')) 'Local data must stay'

    # ---------- Offline ----------
    $script:offline = $true
    $failed = $false
    try { [void](Sync $pc) } catch { $failed = $_.Exception.Message -like 'Облако недоступно*' }
    Assert-True $failed 'Unreachable cloud must stop with a clear message'
    $script:offline = $false

    # ---------- Export shows the cloud status ----------
    foreach ($name in @('export-comp-info.ps1', 'eset-info.ps1', 'sync-merge.ps1', 'cloud-client.ps1', 'console-ui.ps1')) {
        Copy-Item -LiteralPath (Join-Path $project $name) -Destination $pc
    }
    Write-Record $pc 'OLD-4.json' '{"computerId":"OLD-4","hostname":"PC-4","addedToExternalDatabase":true}'
    $csvPath = Join-Path $testRoot 'export.csv'
    $output = & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $pc 'export-comp-info.ps1') -OutputPath $csvPath 2>&1
    Assert-True ($LASTEXITCODE -eq 0) ($output | Out-String)
    $rows = @(Import-Csv -LiteralPath $csvPath -Delimiter ';' -Encoding UTF8)
    Assert-True (($rows | Where-Object computerId -eq 'NEW-3').cloudStatus -eq 'В облаке') 'Export must show synced records'
    Assert-True (($rows | Where-Object computerId -eq 'OLD-4').cloudStatus -eq 'Нет в облаке') 'Export must show records missing in the cloud'
    Assert-True ($null -eq $rows[0].PSObject.Properties['addedToExternalDatabase']) 'Deprecated fields must not be exported'

    [Console]::Out.WriteLine('All cloud sync checks passed.')
}
finally {
    $resolved = [IO.Path]::GetFullPath($testRoot)
    $tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    if (-not $resolved.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase) -or
        (Split-Path -Leaf $resolved) -notlike 'comp-info-cloud-test-*') { throw 'Unsafe test cleanup path' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
