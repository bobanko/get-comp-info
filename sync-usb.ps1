[CmdletBinding()]
param([string]$UsbPath = 'E:\COMP-INFO', [switch]$ReviewConflicts)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'console-ui.ps1')
. (Join-Path $PSScriptRoot 'sync-merge.ps1')

function Copy-Verified {
    param([string]$Source, [string]$Destination)
    Assert-NoLink $Source
    Assert-NoLink $Destination
    $sourceHash = (Get-FileHash -LiteralPath $Source -Algorithm SHA256).Hash
    Copy-Item -LiteralPath $Source -Destination $Destination -Force
    if ((Get-FileHash -LiteralPath $Destination -Algorithm SHA256).Hash -ne $sourceHash) {
        throw "Не удалось проверить копию: $Destination"
    }
}

try {
    Write-ToolboxHeader -Section 'Синхронизация с USB'
    if ($ReviewConflicts) {
        Write-Host 'Загружаю сохранённые конфликты. Подождите...' -ForegroundColor Yellow
    }
    else {
        Write-Host 'Синхронизация началась. Проверяю папки и собираю список файлов...' -ForegroundColor Yellow
    }
    Write-Host ''
    if ([string]::IsNullOrWhiteSpace($UsbPath)) {
        $UsbPath = 'E:\COMP-INFO'
    }
    $UsbPath = $UsbPath.Trim().Trim('"')
    if (-not [IO.Path]::IsPathRooted($UsbPath) -or $UsbPath -match '^[A-Za-z]:[^\\/]') {
        throw 'Укажите абсолютный путь к папке.'
    }
    $localRoot = [IO.Path]::GetFullPath($PSScriptRoot).TrimEnd('\', '/')
    $usbRoot = [IO.Path]::GetFullPath($UsbPath).TrimEnd('\', '/')
    if ($usbRoot -eq [IO.Path]::GetPathRoot($UsbPath).TrimEnd('\', '/')) {
        throw 'Укажите папку программы, а не корень диска.'
    }
    if ($usbRoot -eq $localRoot -or
        $usbRoot.StartsWith($localRoot + '\', [StringComparison]::OrdinalIgnoreCase) -or
        $localRoot.StartsWith($usbRoot + '\', [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Локальная папка и папка флешки должны быть раздельными и не содержать друг друга.'
    }
    Assert-NoLink $localRoot
    if (-not $ReviewConflicts) { Assert-NoLink $usbRoot }
    $localData = Join-Path $localRoot 'data'
    $usbData = Join-Path $usbRoot 'data'
    $conflictRoot = Join-Path $localRoot 'sync-conflicts'
    foreach ($path in @($localData, $conflictRoot)) { Assert-NoLink $path }
    if (-not $ReviewConflicts) { Assert-NoLink $usbData }

    # Only the portable application's root scripts are deployed.
    $programFiles = @(Get-ChildItem -LiteralPath $localRoot -File | Where-Object {
        $_.Extension -in @('.ps1', '.bat', '.cmd')
    })
    if ($ReviewConflicts) { $programFiles = @() }
    foreach ($file in $programFiles) {
        Assert-NoLink $file.FullName
        Assert-NoLink (Join-Path $usbRoot $file.Name)
    }
    if (-not $ReviewConflicts) { New-Item -ItemType Directory -Path $usbRoot -Force | Out-Null }
    New-Item -ItemType Directory -Path $localData -Force | Out-Null
    $counts = @{ Added = 0; Unchanged = 0; Merged = 0; Kept = 0; Deferred = 0 }
    $updated = 0

    $sources = @()
    if (-not $ReviewConflicts -and (Test-Path -LiteralPath $usbData)) {
        $sources += @(Get-ChildItem -LiteralPath $usbData -Filter '*.json' -File | Sort-Object Name | ForEach-Object {
            [pscustomobject]@{ Path = $_.FullName; FileName = $_.Name; Archived = $false }
        })
    }
    if (Test-Path -LiteralPath $conflictRoot) {
        foreach ($folder in Get-ChildItem -LiteralPath $conflictRoot -Directory | Sort-Object Name) {
            Assert-NoLink $folder.FullName
            $sources += @(Get-ChildItem -LiteralPath $folder.FullName -Filter '*.json' -File | Sort-Object LastWriteTime | ForEach-Object {
                [pscustomobject]@{ Path = $_.FullName; FileName = $folder.Name + '.json'; Archived = $true }
            })
        }
    }
    $seen = @{}
    Write-Host 'Сравниваю записи и обрабатываю изменения...' -ForegroundColor Yellow
    foreach ($source in $sources) {
        $key = $source.FileName + ':' + (Get-SyncHash (Get-CanonicalJson (Read-SyncRecord $source.Path)))
        if ($seen.ContainsKey($key)) { continue }
        $seen[$key] = $true
        $status = Import-SyncRecord -Source $source.Path -Destination (Join-Path $localData $source.FileName) -LocalRoot $localRoot -Archived:$source.Archived
        $counts[$status]++
    }

    # Update code only after all collected records have been copied successfully.
    if (-not $ReviewConflicts) {
        Write-Host 'Проверяю и обновляю файлы программы на флешке...' -ForegroundColor Yellow
    }
    foreach ($file in $programFiles) {
        $destination = Join-Path $usbRoot $file.Name
        if ((Test-Path -LiteralPath $destination) -and
            (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash -eq
            (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash) { continue }
        Copy-Verified $file.FullName $destination
        $updated++
    }
    Write-Host ''
    Write-Host 'Готово. Итоги:' -ForegroundColor Green
    $statistics = [ordered]@{
        'Добавлено записей' = $counts.Added
        'Обновлено записей' = $counts.Merged
        'Оставлено локальных' = $counts.Kept
        'Без изменений / уже разобрано' = $counts.Unchanged
        'Отложено' = $counts.Deferred
    }
    if (-not $ReviewConflicts) { $statistics['Обновлено файлов программы'] = $updated }
    foreach ($entry in $statistics.GetEnumerator()) {
        Write-Host ('  {0,-30} {1}' -f ($entry.Key + ':'), $entry.Value)
    }
    Write-Host ''
    if (-not $ReviewConflicts) { Write-Host 'Данные на флешке сохранены.' }
    if ($counts.Deferred -gt 0) { Write-Host 'Чтобы продолжить, выберите в главном меню «Разобрать конфликты синхронизации». Флешка не нужна.' -ForegroundColor Yellow }
    if ($counts.Merged -gt 0) { Write-Host 'Предыдущие локальные версии сохранены в sync-backups.' }
    exit 0
} catch {
    Write-Error "Синхронизация остановлена: $($_.Exception.Message)" -ErrorAction Continue
    exit 1
}
