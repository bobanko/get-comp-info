# Shared merge functions. Loading this file never starts synchronization.
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

function Get-SyncMachineKey {
    return [Environment]::MachineName + '\' + [Environment]::UserName
}

function Get-RememberedComputerPath {
    param([string]$UsbRoot)
    $path = Join-Path $UsbRoot 'sync-targets.json'
    Assert-NoLink $path
    if (-not (Test-Path -LiteralPath $path)) { return $null }
    $settings = Read-SyncRecord $path
    $property = $settings.PSObject.Properties[(Get-SyncMachineKey)]
    if ($null -ne $property) { return [string]$property.Value }
    return $null
}

function Save-SyncComputerPath {
    param([string]$UsbRoot, [string]$ComputerRoot)
    $path = Join-Path $UsbRoot 'sync-targets.json'
    Assert-NoLink $path
    $settings = if (Test-Path -LiteralPath $path) { Read-SyncRecord $path } else { [pscustomobject]@{} }
    $settings | Add-Member -NotePropertyName (Get-SyncMachineKey) -NotePropertyValue $ComputerRoot -Force
    Write-SyncJson $path $settings
}

function Get-PendingSyncConflictCount {
    param([string]$LocalRoot)
    $root = Join-Path $LocalRoot 'sync-conflicts'
    Assert-NoLink $root
    if (-not (Test-Path -LiteralPath $root)) { return 0 }
    $seen = @{}
    $count = 0
    foreach ($folder in Get-ChildItem -LiteralPath $root -Directory) {
        Assert-NoLink $folder.FullName
        $fileName = $folder.Name + '.json'
        foreach ($file in Get-ChildItem -LiteralPath $folder.FullName -Filter '*.json' -File) {
            $incoming = Read-SyncRecord $file.FullName
            $key = Get-SyncHash ($fileName.ToLowerInvariant() + ':' + (Get-SyncHash (Get-CanonicalJson $incoming)))
            if ($seen.ContainsKey($key)) { continue }
            $seen[$key] = $true
            $receipt = Join-Path $LocalRoot "sync-state\$key.json"
            Assert-NoLink $receipt
            if (Test-Path -LiteralPath $receipt) { continue }
            $destination = Join-Path $LocalRoot "data\$fileName"
            Assert-NoLink $destination
            if (Test-Path -LiteralPath $destination) {
                $local = Read-SyncRecord $destination
                if (@(Get-SyncDifferences $local $incoming).Count -eq 0) { continue }
            }
            $count++
        }
    }
    return $count
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
        addedToExternalDatabase = 'Внесён во внешнюю базу'
        addedToExternalDatabaseAt = 'Дата внесения во внешнюю базу'
    }
    if ($labels.ContainsKey($Name)) { return $labels[$Name] }
    return $Name
}

function Get-SyncComparisonLines {
    param($Local, $Incoming, [string[]]$Fields, [string]$RightLabel = 'С флешки')
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
    $computerHeader = ([char]::ConvertFromUtf32(0x1F4BB)) + ' На компьютере'
    $incomingHeader = if ($RightLabel -eq 'С флешки') { ([char]::ConvertFromUtf32(0x1F4BE)) + ' ' + $RightLabel } else { $RightLabel }
    [pscustomobject]@{
        Text = $format -f 'Поле', $computerHeader, $incomingHeader
        Color = 'Gray'
        Parts = @(
            [pscustomobject]@{ Text = ('{0,-' + $labelWidth + '} | ') -f 'Поле'; Color = 'Gray' }
            [pscustomobject]@{ Text = ('{0,-' + $valueWidth + '}') -f $computerHeader; Color = 'Cyan' }
            [pscustomobject]@{ Text = ' | '; Color = 'Gray' }
            [pscustomobject]@{ Text = $incomingHeader; Color = if ($RightLabel -eq 'С флешки') { 'Magenta' } else { 'Gray' } }
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

function Write-SyncActionItem {
    param([string]$Text, [string]$Color)
    $offset = 0
    foreach ($match in [regex]::Matches($Text, '(?i)компьютер\w*|флешк\w*')) {
        Write-Host $Text.Substring($offset, $match.Index - $offset) -NoNewline -ForegroundColor $Color
        $isComputer = $match.Value -match '^компьютер'
        $sourceColor = if ($isComputer) { 'Cyan' } else { 'Magenta' }
        $icon = if ($isComputer) { [char]::ConvertFromUtf32(0x1F4BB) } else { [char]::ConvertFromUtf32(0x1F4BE) }
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

function Select-SyncAction {
    param([string]$Title, [object[]]$Lines, [string[]]$Items, [switch]$AllowBack)
    $selected = 0
    while ($true) {
        Clear-Host
        Write-ToolboxHeader -Section 'Синхронизация — разбор изменений'
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

function Resolve-SyncRecord {
    param($Local, $Incoming, [string]$FileName)
    $fields = @(Get-SyncDifferences $Local $Incoming)
    $title = "$($Local.hostname) — $FileName"
    $lines = @(Get-SyncComparisonLines $Local $Incoming)
    while ($true) {
        $action = Select-SyncAction $title $lines @('Выбрать значения по полям', 'Принять запись с флешки', 'Оставить запись на компьютере', 'Отложить')
        if ($action -eq 3) { return $null }
        $result = $Local
        if ($action -eq 1) { $result = $Incoming }
        if ($action -eq 0) {
            $chosen = @{}
            $index = 0
            while ($index -lt $fields.Count) {
                $name = $fields[$index]
                $detail = @(Get-SyncComparisonLines $Local $Incoming -Fields @($name))
                $pick = Select-SyncAction "$title | Поле $($index + 1) из $($fields.Count): $(Get-SyncFieldLabel $name)" $detail @('Оставить значение на компьютере', 'Принять значение с флешки') -AllowBack
                if ($pick -eq -1) {
                    if ($index -eq 0) { break }
                    $index--
                    continue
                }
                $chosen[$name] = $pick
                $index++
            }
            if ($index -lt $fields.Count) { continue }
            $incomingFields = @($fields | Where-Object { $chosen[$_] -eq 1 })
            $result = Merge-SyncFields $Local $Incoming $incomingFields
        }
        $summary = @(Get-SyncComparisonLines $Local $result -RightLabel 'После сохранения')
        $confirm = Select-SyncAction "$title | Итоговые значения" @($summary) @('Сохранить решение', 'Назад к выбору') -AllowBack
        if ($confirm -eq 0) { return $result }
    }
}

function Import-SyncRecord {
    param([string]$Source, [string]$Destination, [string]$LocalRoot, [switch]$Archived)
    $incoming = Read-SyncRecord $Source
    $fileName = Split-Path -Leaf $Destination
    $incomingHash = Get-SyncHash (Get-CanonicalJson $incoming)
    $key = Get-SyncHash ($fileName.ToLowerInvariant() + ':' + $incomingHash)
    $receipt = Join-Path $LocalRoot "sync-state\$key.json"
    Assert-NoLink $receipt
    if (Test-Path -LiteralPath $receipt) { return 'Unchanged' }
    Assert-NoLink $Destination
    if (-not (Test-Path -LiteralPath $Destination)) {
        if ($Archived) {
            $choice = Select-SyncAction $fileName @('Локальная запись отсутствует. Восстановить сохранённую версию?', ($incoming | ConvertTo-Json -Depth 100)) @('Восстановить', 'Отложить')
            if ($choice -ne 0) { return 'Deferred' }
        }
        Copy-Verified $Source $Destination
        Write-SyncJson $receipt ([pscustomobject]@{ fileName = $fileName; incomingHash = $incomingHash; resolvedAt = (Get-Date).ToString('o') })
        return 'Added'
    }
    $local = Read-SyncRecord $Destination
    if ($local.computerId -and $incoming.computerId -and $local.computerId -ne $incoming.computerId) {
        throw "Разные идентификаторы компьютера в $fileName. Объединение остановлено."
    }
    $differences = @(Get-SyncDifferences $local $incoming)
    if ($differences.Count -eq 0) {
        Write-SyncJson $receipt ([pscustomobject]@{ fileName = $fileName; incomingHash = $incomingHash; resolvedAt = (Get-Date).ToString('o') })
        return 'Unchanged'
    }

    # Preserve incoming data even when the USB drive is unavailable on the next run.
    $archive = Join-Path $LocalRoot ('sync-conflicts\' + [IO.Path]::GetFileNameWithoutExtension($fileName) + '\' + $incomingHash + '.json')
    Assert-NoLink $archive
    if (-not (Test-Path -LiteralPath $archive)) {
        New-Item -ItemType Directory -Path (Split-Path -Parent $archive) -Force | Out-Null
        Copy-Verified $Source $archive
    }
    $result = Resolve-SyncRecord $local $incoming $fileName
    if ($null -eq $result) { return 'Deferred' }
    # Refuse to overwrite a record edited while the user was reviewing it.
    if ((Get-CanonicalJson (Read-SyncRecord $Destination)) -cne (Get-CanonicalJson $local)) {
        throw "Запись $fileName изменилась во время сравнения. Повторите синхронизацию."
    }
    $changed = (Get-CanonicalJson $result) -cne (Get-CanonicalJson $local)
    if ($changed) {
        $backup = Join-Path $LocalRoot ('sync-backups\' + [IO.Path]::GetFileNameWithoutExtension($fileName) + '\' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [guid]::NewGuid().ToString('N') + '.json')
        Assert-NoLink $backup
        New-Item -ItemType Directory -Path (Split-Path -Parent $backup) -Force | Out-Null
        Copy-Verified $Destination $backup
        Write-SyncJson $Destination $result
    }
    Write-SyncJson $receipt ([pscustomobject]@{ fileName = $fileName; incomingHash = $incomingHash; resolvedAt = (Get-Date).ToString('o') })
    if ($changed) { return 'Merged' }
    return 'Kept'
}
