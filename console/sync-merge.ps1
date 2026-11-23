# Shared record comparison and merge functions. Loading this file never starts synchronization.
function Assert-NoLink {
    param([string]$Path)
    $itemPath = [IO.Path]::GetFullPath($Path)
    while ($itemPath) {
        if (Test-Path -LiteralPath $itemPath) {
            $item = Get-Item -LiteralPath $itemPath -Force
            if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) {
                throw "Ссылки на файлы и папки не поддерживаются: $itemPath"
            }
        }
        $itemPath = Split-Path -Parent $itemPath
    }
}

function Get-CanonicalJson {
    param($Value)
    if ($null -eq $Value) { return 'null' }
    if ($Value -is [pscustomobject]) {
        $parts = foreach ($name in @($Value.PSObject.Properties.Name | Sort-Object -CaseSensitive)) {
            (ConvertTo-Json -InputObject $name -Compress) + ':' + (Get-CanonicalJson $Value.$name)
        }
        return '{' + ($parts -join ',') + '}'
    }
    if ($Value -is [array]) {
        $parts = foreach ($item in $Value) { Get-CanonicalJson $item }
        return '[' + ($parts -join ',') + ']'
    }
    return ConvertTo-Json -InputObject $Value -Compress -Depth 100
}

function Get-SyncHash {
    param([string]$Text)
    $sha = [Security.Cryptography.SHA256]::Create()
    try { return [BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($Text))).Replace('-', '') }
    finally { $sha.Dispose() }
}

function Get-RecordHash {
    param($Record)
    return Get-SyncHash (Get-CanonicalJson $Record)
}

function Read-SyncRecord {
    param([string]$Path)
    Assert-NoLink $Path
    $record = Get-Content -LiteralPath $Path -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($null -eq $record -or $record -isnot [pscustomobject]) { throw "Ожидалась запись JSON: $Path" }
    return $record
}

function Get-SyncDifferences {
    param($Local, $Incoming)
    $names = @(@($Local.PSObject.Properties.Name) + @($Incoming.PSObject.Properties.Name) | Sort-Object -Unique)
    foreach ($name in $names) {
        $left = $Local.PSObject.Properties[$name]
        $right = $Incoming.PSObject.Properties[$name]
        if (($null -eq $left) -ne ($null -eq $right) -or
            (Get-CanonicalJson $left.Value) -cne (Get-CanonicalJson $right.Value)) { $name }
    }
}

# A date of a record or of the cloud in UTC, to compare which is newer; [datetime]::MinValue when there is none.
function Get-SyncMoment {
    param($Value)
    if ($Value -is [datetime]) { return $Value.ToUniversalTime() }
    $parsed = [DateTimeOffset]::MinValue
    if ([DateTimeOffset]::TryParse([string]$Value, [Globalization.CultureInfo]::InvariantCulture,
        [Globalization.DateTimeStyles]::AssumeUniversal, [ref]$parsed)) { return $parsed.UtcDateTime }
    return [datetime]::MinValue
}

function Get-SyncFileTime {
    param([string]$Path)
    if ($Path -and (Test-Path -LiteralPath $Path -PathType Leaf)) { return [IO.File]::GetLastWriteTimeUtc($Path) }
    return [datetime]::MinValue
}

# Which version is newer, offered by default (Recency in Cloud.cs, user's choice 21.11.2026): the later collection;
# for the same collection the later change — of the local file or in the cloud. Without dates the local one wins.
function Test-CloudVersionNewer {
    param($Local, [string]$LocalPath, $Cloud, $CloudChangedAt)
    $a = Get-SyncMoment $Local.collectedAt
    $b = Get-SyncMoment $Cloud.collectedAt
    if ($a -ne [datetime]::MinValue -and $b -ne [datetime]::MinValue -and $a -ne $b) { return $b -gt $a }
    return (Get-SyncMoment $CloudChangedAt) -gt (Get-SyncFileTime $LocalPath)
}

# A record deleted in the cloud but changed here: the deletion is newer than the last change of the local file.
function Test-CloudDeletionNewer {
    param([string]$LocalPath, $DeletedAt)
    return (Get-SyncMoment $DeletedAt) -gt (Get-SyncFileTime $LocalPath)
}

function Set-SyncRecordField {
    param($Record, [string]$Name, $Value)
    $Record.PSObject.Properties.Remove($Name)
    $Record | Add-Member -NotePropertyName $Name -NotePropertyValue $Value
}

# Versions of one computer, newest first, as one record (Records.Combine in Store.cs): the newest one with the earliest
# createdAt and the comment and web marks of the newest version that has them. The first object is changed and returned.
function Join-SyncVersions {
    param([object[]]$NewestFirst)
    $result = $NewestFirst[0]
    $first = [datetime]::MaxValue
    $created = $null
    foreach ($record in $NewestFirst) {
        $date = Get-SyncMoment $record.createdAt
        if ($date -ne [datetime]::MinValue -and $date -lt $first) { $first = $date; $created = $record.createdAt }
    }
    if ($null -ne $created) { Set-SyncRecordField $result 'createdAt' $created }
    foreach ($name in @('comment', 'inventoried', 'userId', 'ownerId')) {
        if ([string]::IsNullOrEmpty([string]$result.$name)) {
            foreach ($record in $NewestFirst) {
                if (-not [string]::IsNullOrEmpty([string]$record.$name)) { Set-SyncRecordField $result $name $record.$name; break }
            }
        }
    }
    return $result
}

# Several files of one record (Records.MergeDuplicates in Store.cs): one file stays — the file named after the id when
# it is among them — with the newest collection, the earliest createdAt and the comment and web marks of the newest
# version that has them. All of them are copied to data\.duplicates\<time>\ first. Returns the kept path.
function Merge-DuplicateRecordFiles {
    param([string]$Id, [string[]]$Paths)
    $items = @(foreach ($path in $Paths) {
        $record = Read-SyncRecord $path
        [pscustomobject]@{ Path = $path; Record = $record; Collected = (Get-SyncMoment $record.collectedAt); Changed = (Get-SyncFileTime $path) }
    })
    $ordered = @($items | Sort-Object -Property @{ Expression = 'Collected'; Descending = $true }, @{ Expression = 'Changed'; Descending = $true })
    $result = Join-SyncVersions @($ordered | ForEach-Object { $_.Record })
    $keep = $ordered[0].Path
    $named = ($Id -replace '[<>:"/\\|?*\x00-\x1f]', '_') + '.json'
    foreach ($path in $Paths) { if ((Split-Path -Leaf $path) -eq $named) { $keep = $path } }
    $backup = Join-Path (Join-Path (Split-Path -Parent $keep) '.duplicates') (Get-Date -Format 'yyyyMMdd-HHmmss')
    New-Item -ItemType Directory -Path $backup -Force | Out-Null
    foreach ($path in $Paths) { Copy-Item -LiteralPath $path -Destination (Join-Path $backup (Split-Path -Leaf $path)) -Force }
    Write-SyncJson $keep $result
    foreach ($path in $Paths) { if ($path -ne $keep) { Remove-Item -LiteralPath $path -Force } }
    return $keep
}

function Merge-SyncFields {
    param($Local, $Incoming, [string[]]$IncomingFields)
    $result = $Local | ConvertTo-Json -Depth 100 | ConvertFrom-Json
    foreach ($name in $IncomingFields) {
        $property = $Incoming.PSObject.Properties[$name]
        $result.PSObject.Properties.Remove($name)
        if ($null -ne $property) { $result | Add-Member -NotePropertyName $name -NotePropertyValue $property.Value }
    }
    return $result
}

# Writes JSON through a temporary file so a failure never leaves a half-written record.
function Write-SyncJson {
    param([string]$Path, $Record, [int]$Attempts = 10)
    Assert-NoLink $Path
    $parent = Split-Path -Parent $Path
    New-Item -ItemType Directory -Path $parent -Force | Out-Null
    $temporary = Join-Path $parent ([guid]::NewGuid().ToString('N') + '.tmp')
    [IO.File]::WriteAllText($temporary, ($Record | ConvertTo-Json -Depth 100), [Text.UTF8Encoding]::new($true))
    Move-FileIntoPlace $temporary $Path -Attempts $Attempts
}

# Replaces $Path with the already written $Temporary file (same folder) and removes $Temporary.
function Move-FileIntoPlace {
    param([string]$Temporary, [string]$Path, [int]$Attempts = 10)
    try {
        for ($attempt = 1; $true; $attempt++) {
            try {
                if (Test-Path -LiteralPath $Path) { [IO.File]::Replace($Temporary, $Path, [NullString]::Value) }
                else { [IO.File]::Move($Temporary, $Path) }
                return
            }
            catch {
                # Antivirus and search indexers briefly open a freshly written file without
                # allowing deletion, which makes Replace fail. Wait a little and try again;
                # as a last resort overwrite the file in place, which such handles still allow.
                if ($attempt -ge $Attempts) {
                    [IO.File]::Copy($Temporary, $Path, $true)
                    return
                }
                Start-Sleep -Milliseconds (50 * $attempt)
            }
        }
    }
    finally {
        if (Test-Path -LiteralPath $Temporary) { Remove-Item -LiteralPath $Temporary -Force -ErrorAction SilentlyContinue }
    }
}

function Get-SyncFieldText {
    param($Record, [string]$Name)
    $property = $Record.PSObject.Properties[$Name]
    if ($null -eq $property) { return '(поле отсутствует)' }
    if ($null -eq $property.Value) { return '(null)' }
    if ($property.Value -is [string] -and $property.Value.Length -eq 0) { return '(пустая строка)' }
    if ($property.Value -is [string]) { return $property.Value }
    return ConvertTo-Json -InputObject $property.Value -Depth 100
}

function Get-SyncFieldLabel {
    param([string]$Name)
    $labels = @{
        computerId = 'Идентификатор компьютера'; computerIdSource = 'Источник идентификатора'
        hostname = 'Имя компьютера'; ip = 'IP-адрес'; mac = 'MAC-адрес'
        serial = 'Серийный номер'; smbiosUuid = 'UUID'; manufacturer = 'Производитель'
        windowsMachineGuid = 'ID установки Windows'; partOfDomain = 'Член домена'; domain = 'Домен / рабочая группа'
        model = 'Модель'; username = 'Учётная запись Windows'; userId = 'Пользователь (по факту), №'; ownerId = 'МОЛ (по ведомости), №'; comment = 'Комментарий'
        windows = 'Windows'; esetInstalled = 'ESET установлен'; collectedAt = 'Дата сбора'; createdAt = 'Дата создания'
    }
    if ($labels.ContainsKey($Name)) { return $labels[$Name] }
    return $Name
}

$script:LocalSideLabel = ([char]::ConvertFromUtf32(0x1F4BB)) + ' Локально'
$script:CloudSideLabel = ([char]::ConvertFromUtf32(0x2601)) + ' В облаке'

# Table "Field | left | right": differing rows yellow, equal rows gray, newer of two dates green.
function Get-SyncComparisonLines {
    param(
        $Local,
        $Incoming,
        [string[]]$Fields,
        [string]$LeftLabel = $script:LocalSideLabel,
        [string]$RightLabel = $script:CloudSideLabel,
        [string]$RightColor = 'Magenta'
    )
    if (-not $Fields) {
        $Fields = @(@($Local.PSObject.Properties.Name) + @($Incoming.PSObject.Properties.Name) | Select-Object -Unique)
    }
    $changed = @(Get-SyncDifferences $Local $Incoming)
    $labelWidth = 26
    $valueWidth = 30
    try {
        $width = [Console]::WindowWidth
        if ($width -gt 40) { $valueWidth = [Math]::Max(8, [int][Math]::Floor(($width - $labelWidth - 7) / 2)) }
    } catch { }
    $format = '{0,-' + $labelWidth + '} | {1,-' + $valueWidth + '} | {2}'
    [pscustomobject]@{
        Text = $format -f 'Поле', $LeftLabel, $RightLabel
        Color = 'Gray'
        Parts = @(
            [pscustomobject]@{ Text = ('{0,-' + $labelWidth + '} | ') -f 'Поле'; Color = 'Gray' }
            [pscustomobject]@{ Text = ('{0,-' + $valueWidth + '}') -f $LeftLabel; Color = 'Cyan' }
            [pscustomobject]@{ Text = ' | '; Color = 'Gray' }
            [pscustomobject]@{ Text = $RightLabel; Color = $RightColor }
        )
    }
    [pscustomobject]@{ Text = ('-' * $labelWidth) + '-+-' + ('-' * $valueWidth) + '-+-' + ('-' * $valueWidth); Color = 'DarkGray' }
    foreach ($name in $Fields) {
        $columns = @(
            @(Split-SyncTableCell (Get-SyncFieldLabel $name) $labelWidth),
            @(Split-SyncTableCell (Get-SyncFieldText $Local $name) $valueWidth),
            @(Split-SyncTableCell (Get-SyncFieldText $Incoming $name) $valueWidth)
        )
        $height = ($columns | ForEach-Object { $_.Count } | Measure-Object -Maximum).Maximum
        $color = if ($changed -contains $name) { 'Yellow' } else { 'DarkGray' }
        $localColor = $incomingColor = $color
        if ($changed -contains $name -and $name -match 'At$') {
            $localDate = $incomingDate = [DateTimeOffset]::MinValue
            $culture = [Globalization.CultureInfo]::InvariantCulture
            $styles = [Globalization.DateTimeStyles]::AssumeUniversal
            if ([DateTimeOffset]::TryParse([string]$Local.$name, $culture, $styles, [ref]$localDate) -and
                [DateTimeOffset]::TryParse([string]$Incoming.$name, $culture, $styles, [ref]$incomingDate)) {
                if ($localDate -gt $incomingDate) { $localColor = 'Green'; $incomingColor = 'Red' }
                elseif ($incomingDate -gt $localDate) { $localColor = 'Red'; $incomingColor = 'Green' }
            }
        }
        for ($row = 0; $row -lt $height; $row++) {
            $cells = foreach ($column in $columns) {
                if ($row -lt $column.Count) { $column[$row] } else { '' }
            }
            [pscustomobject]@{
                Text = $format -f $cells[0], $cells[1], $cells[2]
                Color = $color
                Parts = @(
                    [pscustomobject]@{ Text = ('{0,-' + $labelWidth + '} | ') -f $cells[0]; Color = $color }
                    [pscustomobject]@{ Text = ('{0,-' + $valueWidth + '}') -f $cells[1]; Color = $localColor }
                    [pscustomobject]@{ Text = ' | '; Color = $color }
                    [pscustomobject]@{ Text = $cells[2]; Color = $incomingColor }
                )
            }
        }
    }
    [pscustomobject]@{ Text = 'Изменённые значения выделены жёлтым.'; Color = 'Gray' }
    [pscustomobject]@{ Text = 'Для разных дат: более новая — зелёным, более старая — красным.'; Color = 'Gray' }
}

# Menu item text with "локальн…" and "облак…" words colored like the table columns.
function Write-SyncActionItem {
    param([string]$Text, [string]$Color)
    $offset = 0
    foreach ($match in [regex]::Matches($Text, '(?i)локальн\w*|облак\w*')) {
        Write-Host $Text.Substring($offset, $match.Index - $offset) -NoNewline -ForegroundColor $Color
        $isLocal = $match.Value -match '^(?i)локальн'
        $sourceColor = if ($isLocal) { 'Cyan' } else { 'Magenta' }
        $icon = if ($isLocal) { [char]::ConvertFromUtf32(0x1F4BB) } else { [char]::ConvertFromUtf32(0x2601) }
        Write-Host "$icon $($match.Value)" -NoNewline -ForegroundColor $sourceColor
        $offset = $match.Index + $match.Length
    }
    Write-Host $Text.Substring($offset) -ForegroundColor $Color
}

function Split-SyncTableCell {
    param([string]$Text, [int]$Width)
    foreach ($line in ($Text -replace "`t", '    ' -split '\r?\n')) {
        if ($line.Length -eq 0) { ''; continue }
        for ($offset = 0; $offset -lt $line.Length; $offset += $Width) {
            $line.Substring($offset, [Math]::Min($Width, $line.Length - $offset))
        }
    }
}

# Arrow-key menu. Returns the selected index, or -1 for Esc when -AllowBack is set.
function Select-SyncAction {
    param([string]$Title, [object[]]$Lines, [string[]]$Items, [switch]$AllowBack, [string]$Section = 'Синхронизация с облаком — разбор изменений',
        [int]$Selected = 0)
    # The item chosen at the start (the newer version), Enter takes it.
    $selected = [Math]::Max(0, [Math]::Min($Selected, $Items.Count - 1))
    while ($true) {
        Clear-Host
        Write-ToolboxHeader -Section $Section
        $hint = '[▲▼] — выбор, [↲ Enter] — подтвердить.'
        if ($AllowBack) { $hint += ' [Esc] — назад.' }
        Write-ToolboxHint $hint
        Write-Host $Title -ForegroundColor Cyan
        Write-Host ''
        foreach ($line in $Lines) {
            if ($line -is [string]) { Write-Host $line }
            elseif ($line.Parts) {
                foreach ($part in $line.Parts) { Write-Host $part.Text -NoNewline -ForegroundColor $part.Color }
                Write-Host ''
            }
            else { Write-Host $line.Text -ForegroundColor $line.Color }
        }
        Write-Host ''
        for ($i = 0; $i -lt $Items.Count; $i++) {
            $marker = if ($i -eq $selected) { '>' } else { ' ' }
            $color = if ($i -eq $selected) { 'Cyan' } else { 'Gray' }
            Write-SyncActionItem -Text "$marker $($Items[$i])" -Color $color
        }
        switch ([Console]::ReadKey($true).Key) {
            'UpArrow' { $selected = ($selected - 1 + $Items.Count) % $Items.Count }
            'DownArrow' { $selected = ($selected + 1) % $Items.Count }
            'Enter' { return $selected }
            'Escape' { if ($AllowBack) { return -1 } }
        }
    }
}

# Conflict between the local record and the cloud version.
# Returns @{ Action = 'cloud' | 'local' | 'merged' | 'defer'; Record = <result or $null> }.
# -CloudNewer: the cloud version is newer (Test-CloudVersionNewer); the newer version is chosen at the start.
function Resolve-SyncRecord {
    param($Local, $Cloud, [string]$Title, [switch]$CloudNewer)
    $fields = @(Get-SyncDifferences $Local $Cloud)
    $newer = if ($CloudNewer) { 'Новее версия из облака — она выбрана сначала.' } else { 'Новее локальная версия — она выбрана сначала.' }
    $lines = @(Get-SyncComparisonLines $Local $Cloud) + @('', $newer)
    while ($true) {
        $action = Select-SyncAction $Title $lines @('Выбрать значения по полям', 'Оставить версию из облака', 'Отправить локальную версию', 'Отложить') -Selected $(if ($CloudNewer) { 1 } else { 2 })
        if ($action -eq 3) { return @{ Action = 'defer'; Record = $null } }
        $result = $null
        $kind = $null
        if ($action -eq 1) { $result = $Cloud; $kind = 'cloud' }
        if ($action -eq 2) { $result = $Local; $kind = 'local' }
        if ($action -eq 0) {
            $chosen = @{}
            $index = 0
            while ($index -lt $fields.Count) {
                $name = $fields[$index]
                $detail = @(Get-SyncComparisonLines $Local $Cloud -Fields @($name))
                $pick = Select-SyncAction "$Title | Поле $($index + 1) из $($fields.Count): $(Get-SyncFieldLabel $name)" $detail @('Оставить локальное значение', 'Взять значение из облака') -AllowBack -Selected $(if ($CloudNewer) { 1 } else { 0 })
                if ($pick -eq -1) {
                    if ($index -eq 0) { break }
                    $index--
                    continue
                }
                $chosen[$name] = $pick
                $index++
            }
            if ($index -lt $fields.Count) { continue }
            $cloudFields = @($fields | Where-Object { $chosen[$_] -eq 1 })
            $result = Merge-SyncFields $Local $Cloud $cloudFields
            $kind = 'merged'
        }
        $summary = @(Get-SyncComparisonLines $Cloud $result -LeftLabel $script:CloudSideLabel -RightLabel 'После сохранения' -RightColor 'Gray')
        $confirm = Select-SyncAction "$Title | Итоговые значения" @($summary) @('Сохранить решение', 'Назад к выбору') -AllowBack
        if ($confirm -eq 0) { return @{ Action = $kind; Record = $result } }
    }
}
