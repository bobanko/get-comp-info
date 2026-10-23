# Cloud client: settings, personal key, HTTP requests, sync state and the sync algorithm.
# Requires console-ui.ps1 and sync-merge.ps1 to be loaded first. Loading this file never starts synchronization.

# The old manual "added to external database" mark is replaced by the computed cloud status.
$script:DeprecatedFields = @('addedToExternalDatabase', 'addedToExternalDatabaseAt')
$script:CloudStatusSynced = 'В облаке'
$script:CloudStatusChanged = 'Изменено'
$script:CloudStatusMissing = 'Нет в облаке'
$script:CloudPageSize = 200

# ---------- Settings and state ----------

function Get-CloudConfig {
    param([string]$Root)
    $path = Join-Path $Root 'cloud-config.json'
    if (-not (Test-Path -LiteralPath $path)) { throw "Не найден файл настроек облака: $path" }
    $config = Read-SyncRecord $path
    $apiUrl = ([string]$config.apiUrl).Trim().TrimEnd('/')
    # Plain http is allowed only for a local development server.
    if ($apiUrl -notmatch '^(https://|http://(localhost|127\.0\.0\.1)(:\d+)?/)') {
        throw "В cloud-config.json нужен адрес API, начинающийся с https://"
    }
    return [pscustomobject]@{ ApiUrl = $apiUrl }
}

function New-OrdinalTable {
    # Record ids are case-sensitive in the cloud, PowerShell hashtables are not by default.
    return [hashtable]::new([StringComparer]::Ordinal)
}

function Read-CloudState {
    param([string]$Root, [string]$ApiUrl)
    $state = [pscustomobject]@{ ApiUrl = $ApiUrl; Cursor = 0; LastSyncAt = $null; Records = (New-OrdinalTable) }
    $path = Join-Path $Root 'cloud-state.json'
    if (-not (Test-Path -LiteralPath $path)) { return $state }
    $saved = Read-SyncRecord $path
    # State of another cloud is meaningless here: start over, local data stays untouched.
    if ([string]$saved.apiUrl -ne $ApiUrl) { return $state }
    $state.Cursor = [long]$saved.cursor
    $state.LastSyncAt = $saved.lastSyncAt
    if ($saved.records) {
        foreach ($property in $saved.records.PSObject.Properties) {
            $state.Records[$property.Name] = @{
                file = [string]$property.Value.file
                revision = [int]$property.Value.revision
                hash = [string]$property.Value.hash
            }
        }
    }
    return $state
}

function Save-CloudState {
    param([string]$Root, $State)
    $records = [ordered]@{}
    foreach ($id in @($State.Records.Keys | Sort-Object)) {
        $entry = $State.Records[$id]
        $records[$id] = [ordered]@{ file = $entry.file; revision = $entry.revision; hash = $entry.hash }
    }
    $document = [ordered]@{
        apiUrl = $State.ApiUrl
        cursor = $State.Cursor
        lastSyncAt = $State.LastSyncAt
        records = [pscustomobject]$records
    }
    Write-SyncJson (Join-Path $Root 'cloud-state.json') ([pscustomobject]$document)
}

# State used by the viewer and the export; an empty state when the cloud is not configured.
function Get-CloudStateForDisplay {
    param([string]$Root)
    try { return Read-CloudState $Root (Get-CloudConfig $Root).ApiUrl }
    catch { return [pscustomobject]@{ ApiUrl = ''; Cursor = 0; LastSyncAt = $null; Records = (New-OrdinalTable) } }
}

# ---------- Records ----------

function Get-CloudRecordId {
    param($Record, [string]$FileName)
    $computerId = $Record.computerId
    if ($computerId -is [string] -and $computerId.Trim() -ne '') { return $computerId }
    # Legacy records without computerId are identified by their file name.
    return [IO.Path]::GetFileNameWithoutExtension($FileName)
}

function Get-SafeRecordFileName {
    param([string]$Id)
    # Only characters Windows forbids are replaced. Dots and spaces stay: a legacy record is
    # identified by its file name, so the name must give back exactly the same id.
    $safe = $Id -replace '[<>:"/\\|?*\x00-\x1f]', '_'
    return "$safe.json"
}

function Remove-DeprecatedFields {
    param($Record)
    $removed = $false
    foreach ($name in $script:DeprecatedFields) {
        if ($null -ne $Record.PSObject.Properties[$name]) {
            $Record.PSObject.Properties.Remove($name)
            $removed = $true
        }
    }
    return $removed
}

# Status of a local record relative to the last synchronization of this copy.
function Get-CloudStatus {
    param($Record, [string]$FileName, $State)
    $entry = $State.Records[(Get-CloudRecordId $Record $FileName)]
    if ($null -eq $entry) { return $script:CloudStatusMissing }
    if ($entry.hash -eq (Get-RecordHash $Record)) { return $script:CloudStatusSynced }
    return $script:CloudStatusChanged
}

function Get-CloudStatusColor {
    param([string]$Status)
    switch ($Status) {
        $script:CloudStatusSynced { return 'Green' }
        $script:CloudStatusChanged { return 'Yellow' }
        default { return 'DarkYellow' }
    }
}

# ---------- HTTP ----------

function Enable-CloudTls {
    # Windows PowerShell 5.1 does not always offer TLS 1.2 by default.
    [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
}

# Returns @{ Status; Body }. Throws only when the cloud cannot be reached at all.
# HttpWebRequest is used instead of Invoke-RestMethod to always decode responses as UTF-8.
function Invoke-CloudRequest {
    param([string]$Method, [string]$Url, [string]$Key, $Body)
    $request = [Net.HttpWebRequest]::Create($Url)
    $request.Method = $Method
    $request.Accept = 'application/json'
    $request.Timeout = 60000
    $request.ReadWriteTimeout = 60000
    $request.UserAgent = "comp-info/$script:toolboxVersion"
    $request.Headers['Authorization'] = "Bearer $Key"
    $proxy = [Net.WebRequest]::GetSystemWebProxy()
    $proxy.Credentials = [Net.CredentialCache]::DefaultNetworkCredentials
    $request.Proxy = $proxy

    $response = $null
    try {
        if ($null -ne $Body) {
            $bytes = [Text.Encoding]::UTF8.GetBytes((ConvertTo-Json -InputObject $Body -Depth 100 -Compress))
            $request.ContentType = 'application/json; charset=utf-8'
            $request.ContentLength = $bytes.Length
            $stream = $request.GetRequestStream()
            try { $stream.Write($bytes, 0, $bytes.Length) } finally { $stream.Dispose() }
        }
        $response = $request.GetResponse()
    }
    catch {
        $exception = $_.Exception
        while ($null -ne $exception -and $exception -isnot [Net.WebException]) { $exception = $exception.InnerException }
        if ($null -eq $exception -or $null -eq $exception.Response) {
            $reason = if ($null -ne $exception) { $exception.Message } else { $_.Exception.Message }
            throw "Облако недоступно ($Url): $reason"
        }
        $response = $exception.Response
    }

    try {
        $reader = [IO.StreamReader]::new($response.GetResponseStream(), [Text.Encoding]::UTF8)
        try { $text = $reader.ReadToEnd() } finally { $reader.Dispose() }
        $parsed = $null
        if (-not [string]::IsNullOrWhiteSpace($text)) {
            try { $parsed = $text | ConvertFrom-Json } catch { $parsed = $null }
        }
        return [pscustomobject]@{ Status = [int]$response.StatusCode; Body = $parsed }
    }
    finally { $response.Dispose() }
}

function Get-CloudErrorMessage {
    param($Response)
    if ($Response.Body -and $Response.Body.error -and $Response.Body.error.message) {
        return [string]$Response.Body.error.message
    }
    return "Облако ответило кодом $($Response.Status)."
}

# ---------- Personal key ----------

function Get-CloudKeyFromFile {
    param([string]$Root)
    $path = Join-Path $Root 'cloud-key.txt'
    if (-not (Test-Path -LiteralPath $path)) { return $null }
    foreach ($line in [IO.File]::ReadAllLines($path)) {
        if ($line.Trim() -ne '') { return $line.Trim() }
    }
    return $null
}

function Save-CloudKeyToFile {
    param([string]$Root, [string]$Key)
    $path = Join-Path $Root 'cloud-key.txt'
    Assert-NoLink $path
    [IO.File]::WriteAllText($path, $Key + [Environment]::NewLine, [Text.UTF8Encoding]::new($false))
}

# Hidden input: '*' per character, Backspace edits, Esc returns $null.
function Read-MaskedInput {
    $value = New-Object Text.StringBuilder
    while ($true) {
        $key = [Console]::ReadKey($true)
        switch ($key.Key) {
            'Enter' { Write-Host ''; return $value.ToString() }
            'Escape' { Write-Host ''; return $null }
            'Backspace' {
                if ($value.Length -gt 0) {
                    [void]$value.Remove($value.Length - 1, 1)
                    Write-Host "`b `b" -NoNewline
                }
            }
            default {
                if (-not [char]::IsControl($key.KeyChar)) {
                    [void]$value.Append($key.KeyChar)
                    Write-Host '*' -NoNewline
                }
            }
        }
    }
}

function Read-CloudKeyFromConsole {
    param([string]$ErrorText)
    Clear-Host
    Write-ToolboxHeader -Section 'Синхронизация с облаком' -WizardStep 'Ключ доступа'
    Write-ToolboxHint '[↲ Enter] — подтвердить, [Esc] — отменить синхронизацию.'
    Write-Host 'Введите личный ключ доступа. Его создают в веб-интерфейсе облака.'
    Write-Host ''
    if ($ErrorText) {
        Write-Host $ErrorText -ForegroundColor Red
        Write-Host ''
    }
    Write-Host '> ' -NoNewline
    return Read-MaskedInput
}

# Checks the key with GET /me. Returns @{ Key; User } or $null when the user cancels.
function Connect-Cloud {
    param([string]$Root, [string]$ApiUrl)
    $fileKey = Get-CloudKeyFromFile $Root
    $errorText = $null
    if ($fileKey) {
        $response = Invoke-CloudRequest 'GET' "$ApiUrl/me" $fileKey $null
        if ($response.Status -eq 200) { return @{ Key = $fileKey; User = $response.Body } }
        if ($response.Status -ne 401) { throw (Get-CloudErrorMessage $response) }
        $errorText = 'Ключ из cloud-key.txt не подошёл: ' + (Get-CloudErrorMessage $response)
    }
    while ($true) {
        $key = Read-CloudKeyFromConsole $errorText
        if ($null -eq $key) { return $null }
        $key = $key.Trim()
        if ($key -eq '') { $errorText = 'Ключ не может быть пустым.'; continue }
        $response = Invoke-CloudRequest 'GET' "$ApiUrl/me" $key $null
        if ($response.Status -eq 401) { $errorText = Get-CloudErrorMessage $response; continue }
        if ($response.Status -ne 200) { throw (Get-CloudErrorMessage $response) }

        $saveItem = if ($fileKey) { 'Заменить ключ в cloud-key.txt' } else { 'Сохранить ключ в этой копии программы' }
        $lines = @(
            "Ключ подошёл: $($response.Body.name) <$($response.Body.email)>, роль $($response.Body.role).",
            '',
            [pscustomobject]@{ Text = 'Тот, кто получит флешку с сохранённым ключом, сможет синхронизироваться от вашего имени.'; Color = 'Yellow' },
            [pscustomobject]@{ Text = 'Если такая флешка потеряется, сразу отзовите ключ в веб-интерфейсе.'; Color = 'Yellow' }
        )
        $choice = Select-SyncAction 'Сохранить ключ, чтобы не вводить его каждый раз?' $lines @($saveItem, 'Не сохранять') -Section 'Синхронизация с облаком'
        if ($choice -eq 0) { Save-CloudKeyToFile $Root $key }
        return @{ Key = $key; User = $response.Body }
    }
}

# ---------- Synchronization ----------

function New-CloudSyncCounts {
    return [pscustomobject]@{
        PushedNew = 0; PushedUpdated = 0; PushedSame = 0
        ReceivedNew = 0; ReceivedUpdated = 0; Deleted = 0; Deferred = 0
        Errors = New-Object 'System.Collections.Generic.List[string]'
    }
}

# Reads data\*.json and drops deprecated fields from files. Returns @{ ById; Skipped }:
# records sharing one id are skipped entirely until the user keeps only one of the files.
function Get-LocalCloudRecords {
    param([string]$DataPath, $Counts)
    $byId = New-OrdinalTable
    $duplicates = New-OrdinalTable
    if (-not (Test-Path -LiteralPath $DataPath)) { New-Item -ItemType Directory -Path $DataPath | Out-Null }
    foreach ($file in @(Get-ChildItem -LiteralPath $DataPath -Filter '*.json' -File | Sort-Object Name)) {
        try {
            $record = Read-SyncRecord $file.FullName
            if (Remove-DeprecatedFields $record) { Write-SyncJson $file.FullName $record }
        }
        catch {
            $Counts.Errors.Add("$($file.Name): $($_.Exception.Message)")
            continue
        }
        $id = Get-CloudRecordId $record $file.Name
        if ($byId.ContainsKey($id) -or $duplicates.ContainsKey($id)) {
            if ($byId.ContainsKey($id)) { $duplicates[$id] = @($byId[$id].File); $byId.Remove($id) }
            $duplicates[$id] += $file.Name
            continue
        }
        $byId[$id] = @{ Id = $id; Path = $file.FullName; File = $file.Name; Record = $record; Hash = (Get-RecordHash $record) }
    }
    foreach ($id in $duplicates.Keys) {
        $Counts.Errors.Add("Несколько файлов с идентификатором ${id}: $($duplicates[$id] -join ', '). Оставьте один и повторите синхронизацию.")
    }
    return [pscustomobject]@{ ById = $byId; Skipped = $duplicates }
}

function Set-LocalCloudRecord {
    param($Context, [string]$Id, [string]$Path, $Record, [int]$Revision)
    Write-SyncJson $Path $Record
    $saved = Read-SyncRecord $Path
    $hash = Get-RecordHash $saved
    $file = Split-Path -Leaf $Path
    $Context.Local[$Id] = @{ Id = $Id; Path = $Path; File = $file; Record = $saved; Hash = $hash }
    $Context.State.Records[$Id] = @{ file = $file; revision = $Revision; hash = $hash }
}

function Remove-LocalCloudRecord {
    param($Context, [string]$Id)
    $item = $Context.Local[$Id]
    if ($null -ne $item -and (Test-Path -LiteralPath $item.Path)) { Remove-Item -LiteralPath $item.Path }
    $Context.Local.Remove($Id)
    $Context.State.Records.Remove($Id)
}

function Get-CloudRecordTitle {
    param($Item)
    $hostname = [string]$Item.Record.hostname
    if ($hostname -eq '') { $hostname = $Item.Id }
    return "$hostname — $($Item.File)"
}

function Get-DeviceUrl {
    param($Context, [string]$Id)
    # .NET Framework silently drops trailing dots from URL path segments ("O.E.M." -> "O.E.M"),
    # so dots are escaped too; the server decodes them back.
    return "$($Context.ApiUrl)/devices/$([Uri]::EscapeDataString($Id).Replace('.', '%2E'))"
}

# Uploads one local record, resolving conflicts and cloud deletions with the user.
function Send-CloudRecord {
    param($Context, $Item)
    $entry = $Context.State.Records[$Item.Id]
    $baseRevision = if ($null -ne $entry) { $entry.revision } else { $null }
    $record = $Item.Record
    $restore = $false
    while ($true) {
        $response = Invoke-CloudRequest 'PUT' (Get-DeviceUrl $Context $Item.Id) $Context.Key @{
            baseRevision = $baseRevision; data = $record; restore = $restore
        }
        switch ($response.Status) {
            200 {
                $device = $response.Body.device
                Set-LocalCloudRecord $Context $Item.Id $Item.Path $device.data ([int]$device.revision)
                if (-not $response.Body.changed) { $Context.Counts.PushedSame++ }
                elseif ([int]$device.revision -eq 1) { $Context.Counts.PushedNew++ }
                else { $Context.Counts.PushedUpdated++ }
                return
            }
            409 {
                $current = $response.Body.current
                $title = "$(Get-CloudRecordTitle $Item) | в облаке изменил(а) $($current.updatedBy), $($current.updatedAt)"
                $resolution = Resolve-SyncRecord $record $current.data $title
                switch ($resolution.Action) {
                    'defer' { $Context.Counts.Deferred++; return }
                    'cloud' {
                        Set-LocalCloudRecord $Context $Item.Id $Item.Path $current.data ([int]$current.revision)
                        $Context.Counts.ReceivedUpdated++
                        return
                    }
                    default {
                        # Local or merged version: send it on top of the cloud revision just reviewed.
                        $record = $resolution.Record
                        $baseRevision = [int]$current.revision
                    }
                }
            }
            410 {
                $current = $response.Body.current
                $lines = @(
                    "Запись удалена в облаке: $($current.updatedBy), $($current.updatedAt).",
                    'Локальная копия изменена после последней синхронизации.'
                )
                $choice = Select-SyncAction (Get-CloudRecordTitle $Item) $lines @('Восстановить запись в облаке', 'Удалить локальную запись', 'Отложить')
                if ($choice -eq 0) { $restore = $true; continue }
                if ($choice -eq 1) { Remove-LocalCloudRecord $Context $Item.Id; $Context.Counts.Deleted++; return }
                $Context.Counts.Deferred++
                return
            }
            { $_ -in 400, 413 } {
                $Context.Counts.Errors.Add("$($Item.File): $(Get-CloudErrorMessage $response)")
                return
            }
            default { throw "$($Item.File): $(Get-CloudErrorMessage $response)" }
        }
    }
}

# Downloads cloud changes since the saved cursor. Locally changed records that were not sent stay as they are.
function Receive-CloudChanges {
    param($Context, [switch]$Full)
    $since = if ($Full) { 0 } else { $Context.State.Cursor }
    do {
        $response = Invoke-CloudRequest 'GET' "$($Context.ApiUrl)/devices?since=$since&limit=$script:CloudPageSize" $Context.Key $null
        if ($response.Status -ne 200) { throw (Get-CloudErrorMessage $response) }
        foreach ($device in @($response.Body.devices)) {
            $id = [string]$device.id
            if ($Context.Skipped.ContainsKey($id)) { continue }
            $item = $Context.Local[$id]
            $entry = $Context.State.Records[$id]
            $locallyChanged = $null -ne $item -and ($null -eq $entry -or $entry.hash -ne $item.Hash)
            if ($device.deleted) {
                if ($null -eq $item) { $Context.State.Records.Remove($id) }
                elseif (-not $locallyChanged) { Remove-LocalCloudRecord $Context $id; $Context.Counts.Deleted++ }
                continue
            }
            if ($locallyChanged) { continue }
            if ($null -ne $item) {
                $path = $item.Path
            }
            else {
                $fileName = Get-SafeRecordFileName $id
                $path = Join-Path $Context.DataPath $fileName
                if (Test-Path -LiteralPath $path) {
                    $Context.Counts.Errors.Add("${fileName}: файл занят другой записью, запись $id из облака не загружена.")
                    continue
                }
                # The saved file must be recognized as the same record on the next run.
                if ((Get-CloudRecordId $device.data $fileName) -cne $id) {
                    $Context.Counts.Errors.Add("Запись $id из облака не загружена: без computerId её нельзя сохранить в файл с тем же идентификатором.")
                    continue
                }
            }
            $isNew = $null -eq $item
            $isDifferent = $isNew -or (Get-RecordHash $device.data) -ne $item.Hash
            if ($isDifferent) {
                Set-LocalCloudRecord $Context $id $path $device.data ([int]$device.revision)
                if ($isNew) { $Context.Counts.ReceivedNew++ } else { $Context.Counts.ReceivedUpdated++ }
            }
            else {
                $Context.State.Records[$id] = @{ file = $item.File; revision = [int]$device.revision; hash = $item.Hash }
            }
        }
        $since = [long]$response.Body.cursor
        $Context.State.Cursor = $since
        Save-CloudState $Context.Root $Context.State
    } while ($response.Body.hasMore)
}

# Full synchronization of the program copy in $Root. Returns the counts, or $null when cancelled.
function Invoke-CloudSync {
    param([string]$Root)
    Enable-CloudTls
    $config = Get-CloudConfig $Root
    $connection = Connect-Cloud $Root $config.ApiUrl
    if ($null -eq $connection) { return $null }

    Clear-Host
    Write-ToolboxHeader -Section 'Синхронизация с облаком'
    Write-Host "Облако: $($config.ApiUrl)" -ForegroundColor DarkGray
    Write-Host "Пользователь: $($connection.User.name) <$($connection.User.email)>, роль $($connection.User.role)" -ForegroundColor DarkGray
    Write-Host ''

    $counts = New-CloudSyncCounts
    $dataPath = Join-Path $Root 'data'
    $local = Get-LocalCloudRecords $dataPath $counts
    $context = [pscustomobject]@{
        Root = $Root; DataPath = $dataPath; ApiUrl = $config.ApiUrl; Key = $connection.Key
        State = (Read-CloudState $Root $config.ApiUrl); Local = $local.ById; Skipped = $local.Skipped; Counts = $counts
    }

    Write-Host 'Отправка изменений...' -ForegroundColor Cyan
    foreach ($id in @($context.Local.Keys | Sort-Object)) {
        $item = $context.Local[$id]
        $entry = $context.State.Records[$id]
        if ($null -ne $entry -and $entry.hash -eq $item.Hash) { continue }
        Send-CloudRecord $context $item
        Save-CloudState $Root $context.State
    }

    # A record deleted locally comes back with a full download: local deletion never deletes in the cloud.
    $missing = @($context.State.Records.Keys | Where-Object { -not $context.Local.ContainsKey($_) -and -not $context.Skipped.ContainsKey($_) })
    Clear-Host
    Write-ToolboxHeader -Section 'Синхронизация с облаком'
    Write-Host 'Загрузка базы из облака...' -ForegroundColor Cyan
    Receive-CloudChanges $context -Full:($missing.Count -gt 0)

    $context.State.LastSyncAt = (Get-Date).ToString('o')
    Save-CloudState $Root $context.State
    return $counts
}

function Show-CloudSyncSummary {
    param($Counts)
    Write-Host ''
    Write-Host 'Синхронизация завершена.' -ForegroundColor Green
    Write-Host ''
    $rows = @(
        @('Отправлено новых', $Counts.PushedNew),
        @('Отправлено изменённых', $Counts.PushedUpdated),
        @('Совпало с облаком', $Counts.PushedSame),
        @('Получено новых из облака', $Counts.ReceivedNew),
        @('Обновлено из облака', $Counts.ReceivedUpdated),
        @('Удалено по облаку', $Counts.Deleted),
        @('Конфликтов отложено', $Counts.Deferred)
    )
    foreach ($row in $rows) {
        $color = if ($row[0] -eq 'Конфликтов отложено' -and $row[1] -gt 0) { 'Yellow' } elseif ($row[1] -gt 0) { 'Cyan' } else { 'DarkGray' }
        Write-Host ('{0,-28} {1}' -f $row[0], $row[1]) -ForegroundColor $color
    }
    if ($Counts.Errors.Count -gt 0) {
        Write-Host ''
        Write-Host "Ошибки ($($Counts.Errors.Count)):" -ForegroundColor Red
        foreach ($message in $Counts.Errors) { Write-Host "  $message" -ForegroundColor Red }
    }
}
