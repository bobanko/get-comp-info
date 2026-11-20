# Identity helpers. Loading this file does not collect or change anything.
# Merge-DuplicateRecordFiles: one record found in several files is kept in one.
. (Join-Path $PSScriptRoot 'sync-merge.ps1')

# The card of a record in the web interface: the site of the cloud API of cloud-config.json (Records.CardUrl).
function Get-RecordCardUrl {
    param([string]$DataPath, [string]$Id)
    $site = 'https://comp-info.chagear.top'
    try {
        $config = Get-Content -LiteralPath (Join-Path (Split-Path -Parent $DataPath) 'cloud-config.json') -Raw -Encoding UTF8 | ConvertFrom-Json
        $site = ([Uri][string]$config.apiUrl).GetLeftPart([UriPartial]::Authority)
    }
    catch { }
    return "$site/#/devices/$([Uri]::EscapeDataString($Id))"
}

function Test-UsableSerialNumber($Value) {
    if ([string]::IsNullOrWhiteSpace([string]$Value)) { return $false }
    $value = ([string]$Value).Trim()
    return $value -notin @('To be filled by O.E.M.', 'Default string', 'System Serial Number', 'None', 'Unknown', '0')
}

function Get-NormalizedSmbiosUuid($Value) {
    $parsed = [guid]::Empty
    if (-not [guid]::TryParse([string]$Value, [ref]$parsed) -or $parsed -eq [guid]::Empty -or
        $parsed.ToString() -eq 'ffffffff-ffff-ffff-ffff-ffffffffffff') { return $null }
    return $parsed.ToString().ToUpperInvariant()
}

function Get-WindowsInstallationId {
    # Explicit registry view gives 32- and 64-bit PowerShell the same identifier.
    $view = if ([Environment]::Is64BitOperatingSystem) { [Microsoft.Win32.RegistryView]::Registry64 } else { [Microsoft.Win32.RegistryView]::Registry32 }
    $registry = [Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::LocalMachine, $view)
    try {
        $key = $registry.OpenSubKey('SOFTWARE\Microsoft\Cryptography')
        if ($null -eq $key) { throw 'Не удалось прочитать идентификатор установки Windows.' }
        try { $id = Get-NormalizedSmbiosUuid $key.GetValue('MachineGuid') } finally { $key.Dispose() }
    }
    finally { $registry.Dispose() }
    if (-not $id) { throw 'Идентификатор установки Windows отсутствует. Сбор остановлен: hostname как ID не используется.' }
    return $id
}

function Get-InventoryIdentity {
    param([string]$Uuid, [string]$Serial, [string]$InstallationId, [string]$DataPath)
    $uuid = Get-NormalizedSmbiosUuid $Uuid
    $installationId = Get-NormalizedSmbiosUuid $InstallationId
    $source = if ($uuid) { 'smbiosUuid' } elseif (Test-UsableSerialNumber $Serial) { 'serial' } else { 'windowsMachineGuid' }
    $id = switch ($source) { 'smbiosUuid' { $uuid } 'serial' { $Serial.Trim() } default {
        if (-not $installationId) { throw 'Нет устойчивого идентификатора компьютера.' }
        'WINDOWS-' + $installationId
    } }
    $newId = $id
    $found = @(if (Test-Path -LiteralPath $DataPath) {
        foreach ($file in Get-ChildItem -LiteralPath $DataPath -Filter '*.json' -File) {
            try { $record = Get-Content -LiteralPath $file.FullName -Encoding UTF8 -Raw | ConvertFrom-Json }
            catch { throw "Не удалось проверить запись $($file.Name): $($_.Exception.Message)" }
            $recordUuid = Get-NormalizedSmbiosUuid $record.smbiosUuid
            $sameInstallation = $installationId -and (Get-NormalizedSmbiosUuid $record.windowsMachineGuid) -eq $installationId -and
                (-not $uuid -or -not $recordUuid -or $uuid -eq $recordUuid)
            $sameUuid = $uuid -and (Get-NormalizedSmbiosUuid $record.smbiosUuid) -eq $uuid
            # Do not match by serial when a record has a different valid UUID.
            $sameSerial = (Test-UsableSerialNumber $Serial) -and $record.serial -eq $Serial -and
                (-not $uuid -or -not (Get-NormalizedSmbiosUuid $record.smbiosUuid))
            $recordId = if ($record.computerId) { [string]$record.computerId } else { $file.BaseName }
            if ($sameInstallation -or $sameUuid -or $sameSerial -or $recordId -eq $id) {
                [pscustomobject]@{ Id = $recordId; Path = $file.FullName; Record = $record }
            }
        }
    })
    if ($found.Count -gt 1 -and @($found | Where-Object { $_.Id -cne $found[0].Id }).Count -eq 0) {
        # One record in several files: one file is kept (Merge-DuplicateRecordFiles), the others go to data\.duplicates.
        $kept = Merge-DuplicateRecordFiles $found[0].Id @($found | ForEach-Object { $_.Path })
        $found = @([pscustomobject]@{ Id = $found[0].Id; Path = $kept; Record = (Read-SyncRecord $kept) })
    }
    if ($found.Count -gt 1) {
        $list = ($found | ForEach-Object { "$($_.Id) ($(if ($_.Record.hostname) { $_.Record.hostname } else { '?' }))" }) -join '; '
        $links = ($found | ForEach-Object { $_.Id } | Select-Object -Unique | ForEach-Object { Get-RecordCardUrl $DataPath $_ }) -join "`n"
        throw "Найдено несколько записей этого компьютера: $list. Удалите лишнюю в веб-интерфейсе (карточка записи → «Удалить»), затем синхронизируйте базу.`n$links"
    }
    $path = $null
    if ($found.Count -eq 1) {
        $id = $found[0].Id
        $path = $found[0].Path
        # Preserve the immutable cloud key, even if it originally came from a hostname.
        if ($found[0].Record.computerIdSource -in @('hostname','legacyId') -or $id -ne $newId) { $source = 'legacyId' }
    }
    return [pscustomobject]@{ Id = $id; Source = $source; Path = $path; InstallationId = $installationId }
}

function Find-UnboundLegacyInventory {
    param([string]$DataPath, [string]$Hostname)
    if (-not (Test-Path -LiteralPath $DataPath)) { return }
    foreach ($file in Get-ChildItem -LiteralPath $DataPath -Filter '*.json' -File) {
        $record = Get-Content -LiteralPath $file.FullName -Encoding UTF8 -Raw | ConvertFrom-Json
        if ($record.hostname -eq $Hostname -and -not $record.windowsMachineGuid -and
            -not (Get-NormalizedSmbiosUuid $record.smbiosUuid) -and -not (Test-UsableSerialNumber $record.serial)) {
            [pscustomobject]@{ Path = $file.FullName; Record = $record; Id = $(if ($record.computerId) { $record.computerId } else { $file.BaseName }) }
        }
    }
}
