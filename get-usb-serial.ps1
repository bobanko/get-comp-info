$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "console-ui.ps1")

$disks = @()
$selectedIndex = 0
$refresh = $true
$status = ""
$statusColor = "DarkGray"

while ($true) {
    if ($refresh) {
        $status = ""
        $selectedIndex = 0
        $disks = @()
        try {
            $disks = @(Get-Disk -ErrorAction Stop | Where-Object { $_.BusType -eq "USB" } | Sort-Object Number)
        }
        catch {
            $status = "Не удалось получить список USB-накопителей: {0}" -f $_.Exception.Message
            $statusColor = "Red"
        }
        $refresh = $false
    }

    Clear-Host
    Write-ToolboxHeader -Section " / серийные номера USB-накопителей"
    Write-Host "[Esc] — выйти, [↲ Enter] — обновить, [C] — копировать серийник." -ForegroundColor DarkGray
    if ($disks.Count -gt 1) {
        Write-Host "[▲▼] — выбрать накопитель." -ForegroundColor DarkGray
    }
    Write-Host ""

    if ($disks.Count -eq 0 -and [string]::IsNullOrEmpty($status)) {
        Write-Host "USB-накопители не найдены. Подключите флешку и нажмите [↲ Enter]." -ForegroundColor Yellow
    }

    for ($index = 0; $index -lt $disks.Count; $index++) {
        $disk = $disks[$index]
        $prefix = if ($index -eq $selectedIndex) { ">" } else { " " }
        $color = if ($index -eq $selectedIndex) { "Magenta" } else { "Gray" }
        Write-Host ("{0} Диск {1}: {2}" -f $prefix, $disk.Number, $disk.FriendlyName) -ForegroundColor $color
        Write-Host ("  Объём: {0:N2} ГиБ" -f ($disk.Size / 1GB)) -ForegroundColor DarkGray

        # Use the device serial reported by Windows, not a filesystem volume ID.
        $serial = ([string]$disk.SerialNumber).Trim()
        if ([string]::IsNullOrWhiteSpace($serial)) {
            Write-Host "  Серийный номер: устройство не сообщает номер Windows." -ForegroundColor Yellow
        }
        else {
            Write-Host "  Серийный номер: " -NoNewline
            Write-Host $serial -ForegroundColor Cyan
        }
        Write-Host ""
    }

    if (-not [string]::IsNullOrEmpty($status)) {
        Write-Host $status -ForegroundColor $statusColor
    }

    $key = [Console]::ReadKey($true)
    switch ($key.Key) {
        "Escape" { exit 42 }
        "Enter" { $refresh = $true }
        "UpArrow" {
            if ($disks.Count -gt 0) {
                $selectedIndex = ($selectedIndex - 1 + $disks.Count) % $disks.Count
                $status = ""
            }
        }
        "DownArrow" {
            if ($disks.Count -gt 0) {
                $selectedIndex = ($selectedIndex + 1) % $disks.Count
                $status = ""
            }
        }
        "C" {
            if ($disks.Count -eq 0) { break }
            $serial = ([string]$disks[$selectedIndex].SerialNumber).Trim()
            if ([string]::IsNullOrWhiteSpace($serial)) {
                $status = "У выбранного накопителя нет серийного номера для копирования."
                $statusColor = "Yellow"
            }
            else {
                try {
                    Set-Clipboard -Value $serial -ErrorAction Stop
                    $status = "Серийный номер скопирован: $serial"
                    $statusColor = "Green"
                }
                catch {
                    $status = "Не удалось скопировать серийный номер: {0}" -f $_.Exception.Message
                    $statusColor = "Red"
                }
            }
        }
    }
}
