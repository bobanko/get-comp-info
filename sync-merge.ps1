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
    param([string]$Path, $Record)
    Assert-NoLink $Path
    $parent = Split-Path -Parent $Path
    New-Item -ItemType Directory -Path $parent -Force | Out-Null
    $temporary = Join-Path $parent ([guid]::NewGuid().ToString('N') + '.tmp')
    [IO.File]::WriteAllText($temporary, ($Record | ConvertTo-Json -Depth 100), [Text.UTF8Encoding]::new($true))
    if (Test-Path -LiteralPath $Path) { [IO.File]::Replace($temporary, $Path, [NullString]::Value) }
    else { [IO.File]::Move($temporary, $Path) }
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
        model = 'Модель'; username = 'Пользователь'; comment = 'Комментарий'
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
    param([string]$Title, [object[]]$Lines, [string[]]$Items, [switch]$AllowBack, [string]$Section = 'Синхронизация с облаком — разбор изменений')
    $selected = 0
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
function Resolve-SyncRecord {
    param($Local, $Cloud, [string]$Title)
    $fields = @(Get-SyncDifferences $Local $Cloud)
    $lines = @(Get-SyncComparisonLines $Local $Cloud)
    while ($true) {
        $action = Select-SyncAction $Title $lines @('Выбрать значения по полям', 'Оставить версию из облака', 'Отправить локальную версию', 'Отложить')
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
                $pick = Select-SyncAction "$Title | Поле $($index + 1) из $($fields.Count): $(Get-SyncFieldLabel $name)" $detail @('Оставить локальное значение', 'Взять значение из облака') -AllowBack
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
