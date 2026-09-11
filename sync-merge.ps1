# Shared merge functions. Loading this file never starts synchronization.
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
    return ConvertTo-Json -InputObject $property.Value -Depth 100
}

function Get-SyncFieldLabel {
    param([string]$Name)
    $labels = @{
        computerId = 'Идентификатор компьютера'; computerIdSource = 'Источник идентификатора'
        hostname = 'Имя компьютера'; ip = 'IP-адрес'; mac = 'MAC-адрес'
        serial = 'Серийный номер'; smbiosUuid = 'UUID'; manufacturer = 'Производитель'
        model = 'Модель'; username = 'Пользователь'; comment = 'Комментарий'
        windows = 'Windows'; collectedAt = 'Дата сбора'; createdAt = 'Дата создания'
        addedToExternalDatabase = 'Внесён во внешнюю базу'
        addedToExternalDatabaseAt = 'Дата внесения во внешнюю базу'
    }
    if ($labels.ContainsKey($Name)) { return $labels[$Name] }
    return $Name
}

function Select-SyncAction {
    param([string]$Title, [string[]]$Lines, [string[]]$Items, [switch]$AllowBack)
    $selected = 0
    while ($true) {
        Clear-Host
        Write-ToolboxHeader -Section 'Синхронизация — разбор изменений'
        $hint = '[▲▼] — выбор, [↲ Enter] — подтвердить.'
        if ($AllowBack) { $hint += ' [Esc] — назад.' }
        Write-ToolboxHint $hint
        Write-Host $Title -ForegroundColor Cyan
        Write-Host ''
        foreach ($line in $Lines) { Write-Host $line }
        Write-Host ''
        for ($i = 0; $i -lt $Items.Count; $i++) {
            $marker = if ($i -eq $selected) { '>' } else { ' ' }
            $color = if ($i -eq $selected) { 'Cyan' } else { 'Gray' }
            Write-Host "$marker $($Items[$i])" -ForegroundColor $color
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
    $lines = @("Сбор на компьютере: $($Local.collectedAt)", "Сбор входящей версии: $($Incoming.collectedAt)", 'Отличающиеся поля:') + @($fields | ForEach-Object { '  ' + (Get-SyncFieldLabel $_) })
    while ($true) {
        $action = Select-SyncAction $title $lines @('Выбрать значения по полям', 'Принять запись с флешки', 'Оставить локальную запись', 'Отложить')
        if ($action -eq 3) { return $null }
        $result = $Local
        if ($action -eq 1) { $result = $Incoming }
        if ($action -eq 0) {
            $chosen = @{}
            $index = 0
            while ($index -lt $fields.Count) {
                $name = $fields[$index]
                $detail = @("На компьютере: $(Get-SyncFieldText $Local $name)", "С флешки: $(Get-SyncFieldText $Incoming $name)")
                $pick = Select-SyncAction "$title | Поле $($index + 1) из $($fields.Count): $(Get-SyncFieldLabel $name)" $detail @('Оставить локальное значение', 'Принять значение с флешки') -AllowBack
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
        $summary = foreach ($name in $fields) {
            "$(Get-SyncFieldLabel $name): $(Get-SyncFieldText $result $name)"
        }
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
