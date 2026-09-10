$ErrorActionPreference = "Stop"
[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new()
[Console]::InputEncoding = [System.Text.UTF8Encoding]::new()
. (Join-Path $PSScriptRoot "console-ui.ps1")

# Keep Russian text and emoji readable in Windows PowerShell.
$emojiInfo = [char]::ConvertFromUtf32(0x1F4CB)
$emojiWarning = [char]::ConvertFromUtf32(0x26A0)
$emojiFile = [char]::ConvertFromUtf32(0x1F4C4)
$emojiCancelled = [char]::ConvertFromUtf32(0x26D4)
$emojiSaved = [char]::ConvertFromUtf32(0x1F4BE)
$emojiFolder = [char]::ConvertFromUtf32(0x1F4C1)
$emojiLoading = [char]::ConvertFromUtf32(0x23F3)

$text = @{
    Header           = "💻 Сбор информации о компьютере"
    Controls         = "[↲ Enter] — продолжить, [Esc] — вернуться в главное меню."
    Loading          = "⌛ Собираем информацию о компьютере..."
    InfoTitle        = "Информация о компьютере:"
    NotSet           = "(не указано)"
    Overwrite        = "[↲ Enter] — перезаписать файл, [Esc] — вернуться в главное меню."
    CommentPrompt    = "Комментарий (необязательно): "
    ComputerIdLabel = "ID компьютера"
    HostnameLabel    = "Имя ПК"
    SerialLabel      = "Серийный №"
    ComputerLabel    = "Компьютер"
    UserLabel        = "Пользователь"
    CommentLabel     = "Комментарий"
    ComparisonHeader = "💻 Сбор информации о компьютере / сравнение"
    Field            = "Поле"
    Previous         = "Предыдущее"
    Current          = "Текущее"
    PreviousTime     = "Предыдущий сбор: "
    ChangedHint      = "Изменённые значения выделены жёлтым."
    ReadError        = "Не удалось прочитать существующий JSON-файл."
    Warning          = "ВНИМАНИЕ!"
    ExistingFile     = "Файл уже существует:"
    NoChanges        = "Изменений не обнаружено. Существующий файл сохранён."
    Cancelled        = "Операция отменена."
    Saved            = "Данные успешно сохранены!"
    File             = "Файл:"
}

function Show-ToolHeader {
    Write-ToolboxHeader -Section $text.Header.Trim()
    Write-ToolboxHint $text.Controls
    Write-Host ""
}

# ============================================================
# Получение информации о компьютере
# ============================================================

Clear-Host
Write-ToolboxHeader -Section $text.Header
Write-Host ""
Write-Host $text.Loading -ForegroundColor DarkGray

$cs = Get-CimInstance Win32_ComputerSystem
$bios = Get-CimInstance Win32_BIOS
$systemProduct = Get-CimInstance Win32_ComputerSystemProduct
$os = Get-CimInstance Win32_OperatingSystem

$hostname = $env:COMPUTERNAME
$serial = ([string]$bios.SerialNumber).Trim()
$smbiosUuid = [string]$systemProduct.UUID
$manufacturer = $cs.Manufacturer.Trim()
$model = $cs.Model.Trim()

function Test-UsableSerialNumber($value) {
    if ([string]::IsNullOrWhiteSpace([string]$value)) {
        return $false
    }

    $normalized = ([string]$value).Trim()
    $placeholderValues = @(
        "To be filled by O.E.M.",
        "To Be Filled By O.E.M.",
        "Default string",
        "System Serial Number",
        "None",
        "Unknown",
        "0"
    )

    return $normalized -notin $placeholderValues
}

function Get-NormalizedSmbiosUuid($value) {
    if ([string]::IsNullOrWhiteSpace([string]$value)) {
        return $null
    }

    try {
        $uuid = [guid]([string]$value).Trim()
    }
    catch {
        return $null
    }

    $normalized = $uuid.ToString().ToUpperInvariant()
    if ($uuid -eq [guid]::Empty -or $normalized -eq "FFFFFFFF-FFFF-FFFF-FFFF-FFFFFFFFFFFF") {
        return $null
    }

    return $normalized
}

$smbiosUuid = Get-NormalizedSmbiosUuid $smbiosUuid
if ($smbiosUuid) {
    $computerId = $smbiosUuid
    $computerIdSource = "smbiosUuid"
}
elseif (Test-UsableSerialNumber $serial) {
    $computerId = $serial
    $computerIdSource = "serial"
}
else {
    $computerId = $hostname
    $computerIdSource = "hostname"
}

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

$username = $env:USERNAME
$comment = ""

# ============================================================
# Формирование объекта
# ============================================================

$computer = [ordered]@{
    computerId       = $computerId
    computerIdSource = $computerIdSource
    hostname    = $hostname
    ip          = $ip
    mac         = $mac
    serial      = $serial
    smbiosUuid  = $smbiosUuid
    manufacturer = $manufacturer
    model       = $model
    username    = $username
    comment     = $comment
    windows     = $os.Caption
    collectedAt = (Get-Date).ToString("o")
}

# ============================================================
# Вывод информации
# ============================================================

function Show-ComputerInformation {
    Write-Host ""
    Write-Host "$emojiInfo $($text.InfoTitle)"
    Write-Host ""
    Write-Host "Hostname:     $hostname"
    Write-Host "Computer ID:  $computerId ($computerIdSource)"
    Write-Host "IP:           $ip"
    Write-Host "MAC:          $mac"
    Write-Host "Serial:       $serial"
    Write-Host "Computer:     $manufacturer $model"
    Write-Host "User:         $username"
    Write-Host "OS:           $($os.Caption)"
    Write-Host ""
}

function Get-DisplayValue($value) {
    if ($null -eq $value -or [string]::IsNullOrWhiteSpace([string]$value)) {
        return $text.NotSet
    }

    return [string]$value
}

function Get-ComputerDisplayValue($computer) {
    $parts = @()

    foreach ($propertyName in @("manufacturer", "model")) {
        $value = $computer.$propertyName
        if ($null -ne $value -and -not [string]::IsNullOrWhiteSpace([string]$value)) {
            $parts += [string]$value
        }
    }

    return Get-DisplayValue ($parts -join " ")
}

function Confirm-Overwrite {
    while ($true) {
        $key = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
        switch ($key.VirtualKeyCode) {
            13 { return $true }
            27 { return $null }
        }
    }
}

function Read-OptionalComment($initialValue = "") {
    Write-Host $text.CommentPrompt -NoNewline
    $value = [string]$initialValue
    Write-Host $value -NoNewline

    while ($true) {
        # Read the Unicode character directly from the console, preserving the active layout.
        $key = [Console]::ReadKey($true)
        switch ($key.Key) {
            Enter {
                Write-Host ""
                return [pscustomobject]@{ Cancelled = $false; Value = $value }
            }
            Escape {
                Write-Host ""
                return [pscustomobject]@{ Cancelled = $true; Value = $null }
            }
            Backspace {
                if ($value.Length -gt 0) {
                    $value = $value.Substring(0, $value.Length - 1)
                    Write-Host "`b `b" -NoNewline
                }
            }
            default {
                if (-not [char]::IsControl($key.KeyChar)) {
                    $value += [string]$key.KeyChar
                    Write-Host ([string]$key.KeyChar) -NoNewline
                }
            }
        }
    }
}

function Show-ComputerComparison($previousComputer, $controls = $text.Overwrite) {
    $hasChanges = $false
    $fields = @(
        [pscustomobject]@{ Label = $text.ComputerIdLabel; Name = "computerId" },
        [pscustomobject]@{ Label = $text.HostnameLabel; Name = "hostname" },
        [pscustomobject]@{ Label = "IP"; Name = "ip" },
        [pscustomobject]@{ Label = "MAC"; Name = "mac" },
        [pscustomobject]@{ Label = $text.SerialLabel; Name = "serial" },
        [pscustomobject]@{ Label = $text.ComputerLabel; Name = "computer" },
        [pscustomobject]@{ Label = $text.UserLabel; Name = "username" },
        [pscustomobject]@{ Label = $text.CommentLabel; Name = "comment" },
        [pscustomobject]@{ Label = "Windows"; Name = "windows" }
    )

    Clear-Host
    Write-ToolboxHeader -Section $text.ComparisonHeader.Trim()
    Write-ToolboxHint $controls
    Write-Host ""
    Write-Host ("{0,-15} | {1,-30} | {2}" -f $text.Field, $text.Previous, $text.Current)
    Write-Host ("{0,-15}-+-{1,-30}-+-{2}" -f ("-" * 15), ("-" * 30), ("-" * 30))

    foreach ($field in $fields) {
        if ($field.Name -eq "computer") {
            $previousValue = Get-ComputerDisplayValue $previousComputer
            $currentValue = Get-ComputerDisplayValue $computer
        }
        else {
            $previousValue = Get-DisplayValue $previousComputer.($field.Name)
            $currentValue = Get-DisplayValue $computer[$field.Name]
        }
        $color = if ($previousValue -ne $currentValue) { "Yellow" } else { "DarkGray" }

        if ($previousValue -ne $currentValue) {
            $hasChanges = $true
        }

        Write-Host ("{0,-15} | {1,-30} | {2}" -f $field.Label, $previousValue, $currentValue) -ForegroundColor $color
    }

    if ($previousComputer.collectedAt) {
        Write-Host ""
        Write-Host "$($text.PreviousTime)$($previousComputer.collectedAt)" -ForegroundColor DarkGray
    }

    Write-Host ""
    Write-Host $text.ChangedHint

    return $hasChanges
}

# ============================================================
# Подготовка файла
# ============================================================

$dataPath = Join-Path $PSScriptRoot "data"

if (-not (Test-Path $dataPath)) {
    New-Item -ItemType Directory -Path $dataPath | Out-Null
}

$safeComputerId = $computerId -replace '[<>:"/\\|?*]', '_'
$safeComputerId = $safeComputerId.Trim().TrimEnd('.')
$filePath = Join-Path $dataPath "$safeComputerId.json"

# ============================================================
# Проверка существующего файла
# ============================================================

# Older inventory files were named after the serial number instead of the UUID.
if (-not (Test-Path -LiteralPath $filePath) -and (Test-UsableSerialNumber $serial)) {
    $safeSerial = ($serial -replace '[<>:"/\\|?*]', '_').Trim().TrimEnd('.')
    $legacyFilePath = Join-Path $dataPath "$safeSerial.json"
    if (Test-Path -LiteralPath $legacyFilePath) {
        $filePath = $legacyFilePath
    }
}

$previousComputer = $null
if (Test-Path $filePath) {

    try {
        $previousComputer = Get-Content -Raw -LiteralPath $filePath | ConvertFrom-Json -ErrorAction Stop
        if ($null -eq $previousComputer) { throw "The JSON file is empty." }
    }
    catch {
        Write-Host ""
        Write-Host "$emojiWarning $($text.ReadError)" -ForegroundColor Yellow
        Write-Host $_.Exception.Message -ForegroundColor Yellow
        exit 1
    }

    $computer.comment = $previousComputer.comment
    [void](Show-ComputerComparison $previousComputer $text.Controls)
}
else {
    Clear-Host
    Show-ToolHeader
    Show-ComputerInformation
}

# Show the comparison before asking for input when this computer is already saved.
$commentInput = Read-OptionalComment $computer.comment
if ($commentInput.Cancelled) {
    exit 42
}
$comment = $commentInput.Value
$computer.comment = $comment

if ($null -ne $previousComputer) {
    $hasChanges = Show-ComputerComparison $previousComputer

    if (-not $hasChanges) {
        Write-Host ""
        Write-Host "$emojiInfo $($text.NoChanges)" -ForegroundColor Green
        exit
    }
    
    $shouldOverwrite = Confirm-Overwrite
    if ($null -eq $shouldOverwrite) {
        Write-Host ""
        Write-Host "$emojiCancelled $($text.Cancelled)" -ForegroundColor Yellow
        exit 42
    }

}

# ============================================================
# Сохранение JSON
# ============================================================

# Stamp the actual save time, after the user has confirmed the overwrite.
$savedAt = (Get-Date).ToString("o")
$computer.collectedAt = $savedAt
$computer.createdAt = $savedAt
if ($null -ne $previousComputer) {
    if (-not [string]::IsNullOrWhiteSpace([string]$previousComputer.createdAt)) {
        $computer.createdAt = $previousComputer.createdAt
    }
    elseif (-not [string]::IsNullOrWhiteSpace([string]$previousComputer.collectedAt)) {
        # Legacy records only have the date of their last collection.
        $computer.createdAt = $previousComputer.collectedAt
    }
}

$json = $computer | ConvertTo-Json -Depth 5

Set-Content -Path $filePath -Value $json -Encoding UTF8

Write-Host ""
Write-Host "$emojiSaved $($text.Saved)" -ForegroundColor Green
Write-Host ""
Write-Host "$emojiFolder $($text.File)"
Write-Host $filePath
