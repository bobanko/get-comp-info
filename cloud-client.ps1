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

function New-CloudWebRequest {
    param([string]$Method, [string]$Url, [string]$Key)
    $request = [Net.HttpWebRequest]::Create($Url)
    $request.Method = $Method
    $request.Timeout = 60000
    $request.ReadWriteTimeout = 60000
    $request.UserAgent = "comp-info/$script:toolboxVersion"
    $request.Headers['Authorization'] = "Bearer $Key"
    $proxy = [Net.WebRequest]::GetSystemWebProxy()
    $proxy.Credentials = [Net.CredentialCache]::DefaultNetworkCredentials
    $request.Proxy = $proxy
    return $request
}

# Sends the request; error statuses come back as responses, only an unreachable cloud throws.
function Get-CloudWebResponse {
    param($Request, [byte[]]$Body)
    try {
        if ($null -ne $Body) {
            $Request.ContentLength = $Body.Length
            $stream = $Request.GetRequestStream()
            try { $stream.Write($Body, 0, $Body.Length) } finally { $stream.Dispose() }
        }
        return $Request.GetResponse()
    }
    catch {
        $exception = $_.Exception
        while ($null -ne $exception -and $exception -isnot [Net.WebException]) { $exception = $exception.InnerException }
        if ($null -eq $exception -or $null -eq $exception.Response) {
            $reason = if ($null -ne $exception) { $exception.Message } else { $_.Exception.Message }
            throw "Облако недоступно ($($Request.RequestUri)): $reason"
        }
        return $exception.Response
    }
}

function Read-CloudResponseBody {
    param($Response)
    $reader = [IO.StreamReader]::new($Response.GetResponseStream(), [Text.Encoding]::UTF8)
    try { $text = $reader.ReadToEnd() } finally { $reader.Dispose() }
    if ([string]::IsNullOrWhiteSpace($text)) { return $null }
    try { return $text | ConvertFrom-Json } catch { return $null }
}

# Returns @{ Status; Body }. Throws only when the cloud cannot be reached at all.
# HttpWebRequest is used instead of Invoke-RestMethod to always decode responses as UTF-8.
function Invoke-CloudRequest {
    param([string]$Method, [string]$Url, [string]$Key, $Body)
    $request = New-CloudWebRequest $Method $Url $Key
    $request.Accept = 'application/json'
    $bytes = $null
    if ($null -ne $Body) {
        $bytes = [Text.Encoding]::UTF8.GetBytes((ConvertTo-Json -InputObject $Body -Depth 100 -Compress))
        $request.ContentType = 'application/json; charset=utf-8'
    }
    $response = Get-CloudWebResponse $request $bytes
    try { return [pscustomobject]@{ Status = [int]$response.StatusCode; Body = (Read-CloudResponseBody $response) } }
    finally { $response.Dispose() }
}

# Downloads a file; an error status becomes an exception with the cloud's message.
function Save-CloudDownload {
    param([string]$Url, [string]$Key, [string]$Path)
    $response = Get-CloudWebResponse (New-CloudWebRequest 'GET' $Url $Key) $null
    try {
        if ([int]$response.StatusCode -ne 200) {
            throw (Get-CloudErrorMessage ([pscustomobject]@{ Status = [int]$response.StatusCode; Body = (Read-CloudResponseBody $response) }))
        }
        $output = [IO.File]::Create($Path)
        try { $response.GetResponseStream().CopyTo($output) } finally { $output.Dispose() }
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
        NewVersion = $null
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

    # A newer program version is only a hint: failing to check it must not fail the sync.
    try {
        $release = Get-CloudProgramRelease $config.ApiUrl $connection.Key
        if ($release -and (Test-NewerProgramVersion $release.version $script:toolboxVersion)) { $counts.NewVersion = [string]$release.version }
    }
    catch { }
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
    if ($Counts.NewVersion) {
        Write-Host ''
        Write-Host "Доступна новая версия программы $($Counts.NewVersion): пункт меню «Обновить программу»." -ForegroundColor Cyan
    }
}

# ---------- Program update ----------

# Files that make up the program: scripts and launchers in the root, plus the cloud settings.
# Must match isProgramFile in cloud/src/releases.ts.
function Test-ProgramFileName {
    param([string]$Name)
    return $Name -ceq 'cloud-config.json' -or $Name -match '^[^\\/:*?"<>|\x00-\x1f]+\.(ps1|bat|cmd)$'
}

function Get-ProgramFiles {
    param([string]$Root)
    return @(Get-ChildItem -LiteralPath $Root -File | Where-Object { Test-ProgramFileName $_.Name })
}

function Get-FileSha256 {
    param([string]$Path)
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function ConvertTo-ProgramVersion {
    param([string]$Text)
    $version = $null
    if ([version]::TryParse([string]$Text, [ref]$version)) { return $version }
    return $null
}

function Test-NewerProgramVersion {
    param([string]$Candidate, [string]$Current)
    $candidateVersion = ConvertTo-ProgramVersion $Candidate
    $currentVersion = ConvertTo-ProgramVersion $Current
    return $null -ne $candidateVersion -and $null -ne $currentVersion -and $candidateVersion -gt $currentVersion
}

# Current release in the cloud, or $null when nothing has been published yet.
function Get-CloudProgramRelease {
    param([string]$ApiUrl, [string]$Key)
    $response = Invoke-CloudRequest 'GET' "$ApiUrl/program/latest" $Key $null
    if ($response.Status -eq 200) { return $response.Body }
    if ($response.Status -eq 404) { return $null }
    throw (Get-CloudErrorMessage $response)
}

# Puts the backed-up program files back and removes files the failed update added.
function Restore-ProgramBackup {
    param([string]$Root, [string]$Backup, [string[]]$NewNames)
    $backupFiles = @(Get-ChildItem -LiteralPath $Backup -File)
    $backupNames = @($backupFiles | ForEach-Object { $_.Name })
    foreach ($name in $NewNames) {
        $path = Join-Path $Root $name
        if ($backupNames -notcontains $name -and (Test-Path -LiteralPath $path)) { Remove-Item -LiteralPath $path -Force }
    }
    foreach ($file in $backupFiles) {
        $target = Join-Path $Root $file.Name
        # A file the update never managed to replace is already the old one.
        if ((Test-Path -LiteralPath $target) -and (Get-FileSha256 $target) -eq (Get-FileSha256 $file.FullName)) { continue }
        Copy-Item -LiteralPath $file.FullName -Destination $target -Force
    }
}

# Keeps the two most recent backups and downloaded archives in .update.
function Remove-OldUpdateFiles {
    param([string]$UpdateRoot, [int]$Keep = 2)
    @(Get-ChildItem -LiteralPath $UpdateRoot -Directory -Filter 'backup-*' | Sort-Object LastWriteTime -Descending | Select-Object -Skip $Keep) |
        ForEach-Object { Remove-Item -LiteralPath $_.FullName -Recurse -Force -ErrorAction SilentlyContinue }
    @(Get-ChildItem -LiteralPath $UpdateRoot -File -Filter '*.zip' | Sort-Object LastWriteTime -Descending | Select-Object -Skip $Keep) |
        ForEach-Object { Remove-Item -LiteralPath $_.FullName -Force -ErrorAction SilentlyContinue }
}

# Installs a downloaded, already checksum-verified release archive into $Root.
# Nothing in $Root changes unless every file matches manifest.json; on a failure while
# replacing files the previous version is restored from the backup.
function Install-ProgramRelease {
    param([string]$Root, [string]$ZipPath, [string]$ExpectedVersion, [string]$CurrentVersion, [int]$Attempts = 10)
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $updateRoot = Join-Path $Root '.update'
    $extract = Join-Path $updateRoot ('extract-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $updateRoot -Force | Out-Null
    try {
        [IO.Compression.ZipFile]::ExtractToDirectory($ZipPath, $extract)
        $manifestPath = Join-Path $extract 'manifest.json'
        if (-not (Test-Path -LiteralPath $manifestPath)) { throw 'В архиве нет manifest.json.' }
        $manifest = Read-SyncRecord $manifestPath
        if ([string]$manifest.version -ne $ExpectedVersion) { throw "Архив содержит версию $($manifest.version), ожидалась $ExpectedVersion." }

        $files = @($manifest.files)
        $names = @($files | ForEach-Object { [string]$_.path })
        if ($files.Count -eq 0) { throw 'В manifest.json нет файлов.' }
        foreach ($file in $files) {
            $name = [string]$file.path
            $source = Join-Path $extract $name
            if (-not (Test-ProgramFileName $name)) { throw "Недопустимый файл в архиве: $name" }
            if (-not (Test-Path -LiteralPath $source -PathType Leaf)) { throw "В архиве нет файла $name." }
            if ((Get-Item -LiteralPath $source).Length -ne [long]$file.size -or (Get-FileSha256 $source) -ne ([string]$file.sha256).ToLowerInvariant()) {
                throw "Файл $name в архиве не совпадает с manifest.json."
            }
        }
        foreach ($entry in Get-ChildItem -LiteralPath $extract -Recurse -File) {
            $relative = $entry.FullName.Substring($extract.Length + 1)
            if ($relative -ne 'manifest.json' -and $names -notcontains $relative) { throw "Лишний файл в архиве: $relative" }
        }

        $backup = Join-Path $updateRoot ("backup-$CurrentVersion-" + (Get-Date -Format 'yyyyMMdd-HHmmss'))
        New-Item -ItemType Directory -Path $backup -Force | Out-Null
        foreach ($existing in Get-ProgramFiles $Root) { Copy-Item -LiteralPath $existing.FullName -Destination $backup }

        try {
            foreach ($file in $files) {
                $target = Join-Path $Root $file.path
                $temporary = Join-Path $Root ([guid]::NewGuid().ToString('N') + '.tmp')
                Copy-Item -LiteralPath (Join-Path $extract $file.path) -Destination $temporary
                Move-FileIntoPlace $temporary $target -Attempts $Attempts
                if ((Get-FileSha256 $target) -ne ([string]$file.sha256).ToLowerInvariant()) { throw "Файл $($file.path) записан с ошибкой." }
            }
            $removed = @(foreach ($old in Get-ProgramFiles $Root) {
                if ($names -notcontains $old.Name) {
                    Remove-Item -LiteralPath $old.FullName -Force
                    $old.Name
                }
            })
        }
        catch {
            $reason = $_.Exception.Message
            try { Restore-ProgramBackup $Root $backup $names }
            catch { throw "Обновление не удалось ($reason), и прежнюю версию восстановить не получилось: $($_.Exception.Message). Файлы прежней версии лежат в $backup" }
            throw "Обновление не удалось, прежняя версия восстановлена: $reason"
        }

        Remove-OldUpdateFiles $updateRoot
        return [pscustomobject]@{ Version = $ExpectedVersion; Installed = $files.Count; Removed = $removed; Backup = $backup }
    }
    finally {
        if (Test-Path -LiteralPath $extract) { Remove-Item -LiteralPath $extract -Recurse -Force -ErrorAction SilentlyContinue }
    }
}

# The menu item "Обновить программу". Returns updated | up-to-date | cancelled | no-release | development.
function Invoke-ProgramUpdate {
    param([string]$Root, [string]$CurrentVersion = $script:toolboxVersion)
    if (Test-Path -LiteralPath (Join-Path $Root '.git')) {
        Write-Host 'Это копия программы для разработки (есть папка .git). Обновляйте её через Git.' -ForegroundColor Yellow
        return 'development'
    }
    Enable-CloudTls
    $config = Get-CloudConfig $Root
    $connection = Connect-Cloud $Root $config.ApiUrl
    if ($null -eq $connection) { return 'cancelled' }

    Clear-Host
    Write-ToolboxHeader -Section 'Обновление программы'
    Write-Host 'Проверка версии в облаке...' -ForegroundColor DarkGray
    $release = Get-CloudProgramRelease $config.ApiUrl $connection.Key
    if ($null -eq $release) {
        Write-Host 'В облаке ещё нет опубликованных версий программы.' -ForegroundColor Yellow
        return 'no-release'
    }
    $version = [string]$release.version
    $current = ConvertTo-ProgramVersion $CurrentVersion
    $latest = ConvertTo-ProgramVersion $version
    if ($null -ne $current -and $null -ne $latest -and $current -eq $latest) {
        Write-Host "Установлена последняя версия: $CurrentVersion" -ForegroundColor Green
        return 'up-to-date'
    }

    $isRollback = $null -ne $current -and $null -ne $latest -and $latest -lt $current
    $lines = New-Object 'System.Collections.Generic.List[object]'
    $lines.Add("Установлена версия:  $CurrentVersion")
    $published = if ($release.publishedAt) { ' от ' + ([DateTimeOffset]::Parse([string]$release.publishedAt).LocalDateTime.ToString('yyyy-MM-dd HH:mm')) } else { '' }
    $lines.Add("Версия в облаке:     $version$published")
    if ($isRollback) {
        $lines.Add('')
        $lines.Add([pscustomobject]@{ Text = 'Администратор сделал текущей более старую версию: установка будет откатом.'; Color = 'Yellow' })
    }
    $notes = @($release.notes | Where-Object { $_ })
    if ($notes.Count -gt 0) {
        $lines.Add('')
        $lines.Add([pscustomobject]@{ Text = 'Что нового:'; Color = 'Cyan' })
        foreach ($note in ($notes | Select-Object -First 15)) { $lines.Add("  • $note") }
        if ($notes.Count -gt 15) { $lines.Add([pscustomobject]@{ Text = "  и ещё $($notes.Count - 15)"; Color = 'DarkGray' }) }
    }
    $title = if ($isRollback) { 'Откат программы' } else { 'Доступна новая версия программы' }
    $choice = Select-SyncAction $title $lines.ToArray() @("Установить версию $version", 'Отмена') -AllowBack -Section 'Обновление программы'
    if ($choice -ne 0) { return 'cancelled' }

    Clear-Host
    Write-ToolboxHeader -Section 'Обновление программы'
    Write-Host "Загрузка версии $version..." -ForegroundColor Cyan
    $updateRoot = Join-Path $Root '.update'
    New-Item -ItemType Directory -Path $updateRoot -Force | Out-Null
    $zip = Join-Path $updateRoot "comp-info-$version.zip"
    Save-CloudDownload "$($config.ApiUrl)/program/$version/package" $connection.Key $zip
    if ((Get-FileSha256 $zip) -ne ([string]$release.sha256).ToLowerInvariant()) {
        Remove-Item -LiteralPath $zip -Force
        throw 'Архив повреждён при загрузке: контрольная сумма не совпадает. Запустите обновление ещё раз.'
    }

    Write-Host 'Установка...' -ForegroundColor Cyan
    $result = Install-ProgramRelease $Root $zip $version $CurrentVersion
    Write-Host ''
    Write-Host "Программа обновлена до версии $version." -ForegroundColor Green
    Write-Host "Файлов установлено: $($result.Installed)" -ForegroundColor DarkGray
    if ($result.Removed.Count -gt 0) { Write-Host "Удалены устаревшие файлы: $($result.Removed -join ', ')" -ForegroundColor DarkGray }
    Write-Host "Прежняя версия сохранена в $($result.Backup)" -ForegroundColor DarkGray
    return 'updated'
}
