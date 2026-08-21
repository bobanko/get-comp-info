$ErrorActionPreference = "Stop"

# Keep the source compatible with Windows PowerShell when the file is opened
# without a UTF-8 BOM.
$emojiInfo = [char]::ConvertFromUtf32(0x1F4CB)
$emojiWarning = [char]::ConvertFromUtf32(0x26A0)
$emojiFile = [char]::ConvertFromUtf32(0x1F4C4)
$emojiCancelled = [char]::ConvertFromUtf32(0x26D4)
$emojiSaved = [char]::ConvertFromUtf32(0x1F4BE)
$emojiFolder = [char]::ConvertFromUtf32(0x1F4C1)

# ============================================================
# Получение информации о компьютере
# ============================================================

$cs = Get-CimInstance Win32_ComputerSystem
$bios = Get-CimInstance Win32_BIOS
$os = Get-CimInstance Win32_OperatingSystem

$hostname = $env:COMPUTERNAME
$serial = $bios.SerialNumber.Trim()
$manufacturer = $cs.Manufacturer.Trim()
$model = $cs.Model.Trim()

$ip = (Get-NetIPAddress -AddressFamily IPv4 |
    Where-Object {
        $_.IPAddress -notlike '169.*' -and
        $_.InterfaceAlias -notmatch 'Loopback|vEthernet|VPN'
    } |
    Select-Object -First 1).IPAddress

$mac = (Get-NetAdapter |
    Where-Object {
        $_.Status -eq 'Up' -and
        $_.MacAddress
    } |
    Select-Object -First 1 -ExpandProperty MacAddress)

$username = "$env:USERDOMAIN\$env:USERNAME"

# ============================================================
# Формирование объекта
# ============================================================

$computer = [ordered]@{
    hostname    = $hostname
    ip          = $ip
    mac         = $mac
    serial      = $serial
    manufacturer = $manufacturer
    model       = $model
    username    = $username
    windows     = $os.Caption
    collectedAt = (Get-Date).ToString("o")
}

# ============================================================
# Вывод информации
# ============================================================

function Show-ComputerInformation {
    Write-Host ""
    Write-Host "$emojiInfo Computer information:"
    Write-Host ""
    Write-Host "Hostname:     $hostname"
    Write-Host "IP:           $ip"
    Write-Host "MAC:          $mac"
    Write-Host "Serial:       $serial"
    Write-Host "Manufacturer: $manufacturer"
    Write-Host "Model:        $model"
    Write-Host "User:         $username"
    Write-Host "Windows:      $($os.Caption)"
    Write-Host ""
}

function Get-DisplayValue($value) {
    if ($null -eq $value -or [string]::IsNullOrWhiteSpace([string]$value)) {
        return "(not set)"
    }

    return [string]$value
}

function Show-ComputerComparison($previousComputer) {
    $hasChanges = $false
    $fields = @(
        [pscustomobject]@{ Label = "Hostname"; Name = "hostname" },
        [pscustomobject]@{ Label = "IP"; Name = "ip" },
        [pscustomobject]@{ Label = "MAC"; Name = "mac" },
        [pscustomobject]@{ Label = "Serial"; Name = "serial" },
        [pscustomobject]@{ Label = "Manufacturer"; Name = "manufacturer" },
        [pscustomobject]@{ Label = "Model"; Name = "model" },
        [pscustomobject]@{ Label = "User"; Name = "username" },
        [pscustomobject]@{ Label = "Windows"; Name = "windows" }
    )

    Write-Host ""
    Write-Host "$emojiInfo Existing data comparison (previous -> current):"
    Write-Host ""
    Write-Host ("{0,-15} | {1,-30} | {2}" -f "Field", "Previous", "Current")
    Write-Host ("{0,-15}-+-{1,-30}-+-{2}" -f ("-" * 15), ("-" * 30), ("-" * 30))

    foreach ($field in $fields) {
        $previousValue = Get-DisplayValue $previousComputer.($field.Name)
        $currentValue = Get-DisplayValue $computer[$field.Name]
        $color = if ($previousValue -ne $currentValue) { "Yellow" } else { "DarkGray" }

        if ($previousValue -ne $currentValue) {
            $hasChanges = $true
        }

        Write-Host ("{0,-15} | {1,-30} | {2}" -f $field.Label, $previousValue, $currentValue) -ForegroundColor $color
    }

    if ($previousComputer.collectedAt) {
        Write-Host ""
        Write-Host "Previous data collected: $($previousComputer.collectedAt)" -ForegroundColor DarkGray
    }

    Write-Host ""
    Write-Host "Changed values are highlighted in yellow."

    return $hasChanges
}

# ============================================================
# Подготовка файла
# ============================================================

$dataPath = Join-Path $PSScriptRoot "data"

if (-not (Test-Path $dataPath)) {
    New-Item -ItemType Directory -Path $dataPath | Out-Null
}

$filePath = Join-Path $dataPath "$serial.json"

# ============================================================
# Проверка существующего файла
# ============================================================

if (Test-Path $filePath) {

    try {
        $previousComputer = Get-Content -Raw -LiteralPath $filePath | ConvertFrom-Json -ErrorAction Stop
    }
    catch {
        Write-Host ""
        Write-Host "$emojiWarning Cannot read the existing JSON file." -ForegroundColor Yellow
        Write-Host $_.Exception.Message -ForegroundColor Yellow
        exit 1
    }

    Write-Host ""
    Write-Host "$emojiWarning WARNING!" -ForegroundColor Yellow
    Write-Host "$emojiFile File already exists:"
    Write-Host $filePath
    $hasChanges = Show-ComputerComparison $previousComputer

    if (-not $hasChanges) {
        Write-Host ""
        Write-Host "$emojiInfo No changes detected. Existing file was kept." -ForegroundColor Green
        exit
    }
    
    $answer = Read-Host "Overwrite existing file? (Y/N)"

    if ($answer -notmatch '^[Yy\u0414\u0434]$') {
        Write-Host ""
        Write-Host "$emojiCancelled Operation cancelled." -ForegroundColor Yellow
        exit
    }
}
else {
    Show-ComputerInformation
}

# ============================================================
# Сохранение JSON
# ============================================================

$json = $computer | ConvertTo-Json -Depth 5

Set-Content -Path $filePath -Value $json -Encoding UTF8

Write-Host ""
Write-Host "$emojiSaved Saved successfully!" -ForegroundColor Green
Write-Host ""
Write-Host "$emojiFolder File:"
Write-Host $filePath
