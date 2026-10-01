$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "console-ui.ps1")

function Get-UsbDeviceSerialNumber {
    param([string]$PnpDeviceId)

    # USBSTOR's instance contains the USB serial followed by the LUN suffix.
    # Windows-generated instance IDs contain '&' and are not device serials.
    if ($PnpDeviceId -match '^USBSTOR\\[^\\]+\\([^\\]+)$') {
        $serial = $Matches[1] -replace '&\d+$', ''
        if ($serial -notmatch '&') {
            return $serial
        }
    }
    return ""
}

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
            $deviceIds = @{}
            foreach ($device in (Get-CimInstance Win32_DiskDrive -ErrorAction Stop)) {
                $deviceIds[[int]$device.Index] = [string]$device.PNPDeviceID
            }
            foreach ($disk in $disks) {
                $usbSerial = Get-UsbDeviceSerialNumber -PnpDeviceId $deviceIds[[int]$disk.Number]
                $disk | Add-Member -NotePropertyName UsbSerialNumber -NotePropertyValue $usbSerial -Force
            }
        }
        catch {
            $status = "Не удалось получить список USB-накопителей: {0}" -f $_.Exception.Message
            $statusColor = "Red"
        }
        $refresh = $false
    }

    Clear-Host
    Write-ToolboxHeader -Section " / серийные номера USB-накопителей"
    Write-ToolboxHint "[Esc] — выйти, [↲ Enter] — обновить, [C] — копировать серийник."
    if ($disks.Count -gt 1) {
        Write-ToolboxHint "[▲▼] — выбрать накопитель."
    }
    Write-Host ""

    if ($disks.Count -eq 0 -and [string]::IsNullOrEmpty($status)) {
        Write-Host "USB-накопители не найдены." -ForegroundColor Yellow
        Write-ToolboxHint "Подключите флешку и нажмите [↲ Enter]."
    }

    for ($index = 0; $index -lt $disks.Count; $index++) {
        $disk = $disks[$index]
        $prefix = if ($index -eq $selectedIndex) { ">" } else { " " }
        $color = if ($index -eq $selectedIndex) { "Magenta" } else { "Gray" }
        Write-Host ("{0} Диск {1}: {2}" -f $prefix, $disk.Number, $disk.FriendlyName) -ForegroundColor $color
        Write-Host ("  Объём: {0:N2} ГиБ" -f ($disk.Size / 1GB)) -ForegroundColor DarkGray

        $serial = ([string]$disk.UsbSerialNumber).Trim()
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
            $serial = ([string]$disks[$selectedIndex].UsbSerialNumber).Trim()
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
