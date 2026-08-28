$ErrorActionPreference = "Stop"

# Keep Russian text and emoji readable in Windows PowerShell consoles.
[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new()
[Console]::InputEncoding = [System.Text.UTF8Encoding]::new()
$emojiNetwork = [char]::ConvertFromUtf32(0x1F5A7)
$emojiCancelled = [char]::ConvertFromUtf32(0x26D4)
$emojiSuccess = [char]::ConvertFromUtf32(0x2705)

$currentIdentity = [Security.Principal.WindowsIdentity]::GetCurrent()
$currentPrincipal = New-Object Security.Principal.WindowsPrincipal($currentIdentity)
$isAdministrator = $currentPrincipal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)

if (-not $isAdministrator) {
    Write-Host "Запрашиваются права администратора..." -ForegroundColor Yellow

    try {
        $elevatedArguments = @(
            "-NoProfile",
            "-ExecutionPolicy", "Bypass",
            "-File", ('"{0}"' -f $PSCommandPath)
        )
        Start-Process -FilePath "powershell.exe" -Verb RunAs -ArgumentList $elevatedArguments
        exit
    }
    catch {
        Write-Host "Права администратора не были предоставлены. Настройки не изменены." -ForegroundColor Red
        exit 1
    }
}

# The gateway belongs to the same subnet; reserve it and only allow usable host IPs.
$locations = @(
    [pscustomobject]@{
        Name             = ([string][char]0x0428 + [char]0x0442 + [char]0x0430 + [char]0x0431)
        ThirdOctet       = 1
        PrefixLength     = 25
        SubnetMask       = "255.255.255.128"
        GatewayOctet     = 129
        DnsServers       = @("10.9.0.1", "10.9.0.2")
        FirstHostOctet   = 130
        LastHostOctet    = 254
    },
    [pscustomobject]@{
        Name             = ([string][char]0x0423 + [char]0x0447 + [char]0x0435 + [char]0x0431 + [char]0x043D + [char]0x044B + [char]0x0439 + " " + [char]0x043A + [char]0x043E + [char]0x0440 + [char]0x043F + [char]0x0443 + [char]0x0441)
        ThirdOctet       = 2
        PrefixLength     = 27
        SubnetMask       = "255.255.255.224"
        GatewayOctet     = 65
        DnsServers       = @("10.9.0.1", "10.9.0.2")
        FirstHostOctet   = 66
        LastHostOctet    = 94
    },
    [pscustomobject]@{
        Name             = ([string][char]0x0411 + [char]0x0443 + [char]0x043D + [char]0x043A + [char]0x0435 + [char]0x0440)
        ThirdOctet       = 3
        PrefixLength     = 28
        SubnetMask       = "255.255.255.240"
        GatewayOctet     = 97
        DnsServers       = @("10.9.0.1", "10.9.0.2")
        FirstHostOctet   = 98
        LastHostOctet    = 110
    }
)

function Select-MenuItem {
    param(
        [string]$Title,
        [object[]]$Items
    )

    if ($Items.Count -eq 0) {
        throw "В меню нет доступных пунктов."
    }

    $selectedIndex = 0

    while ($true) {
        Clear-Host
        Write-Host "`n$Title" -ForegroundColor Cyan
        Write-Host "Стрелки Вверх/Вниз — выбор, Enter — продолжить." -ForegroundColor DarkGray
        Write-Host "`n"

        for ($index = 0; $index -lt $Items.Count; $index++) {
            $item = $Items[$index]
            $marker = if ($index -eq $selectedIndex) { ">" } else { " " }
            $line = "{0} {1}. {2}" -f $marker, ($index + 1), $item.Label

            if ($index -eq $selectedIndex) {
                Write-Host $line -ForegroundColor Cyan
            }
            elseif ($item.Color) {
                Write-Host $line -ForegroundColor $item.Color
            }
            else {
                Write-Host $line
            }
        }

        $key = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
        switch ($key.VirtualKeyCode) {
            38 { # Up arrow
                $selectedIndex = ($selectedIndex - 1 + $Items.Count) % $Items.Count
            }
            40 { # Down arrow
                $selectedIndex = ($selectedIndex + 1) % $Items.Count
            }
            13 { return $Items[$selectedIndex].Value } # Enter
        }

    }
}

function Get-AdaptersInBackground {
    Start-Job -ScriptBlock {
        Get-NetAdapter | Select-Object Name, InterfaceDescription, Status, HardwareInterface, MediaType, ifIndex
    }
}

function Get-AdapterStatusDisplay {
    param([object]$Adapter)

    switch ([string]$Adapter.Status) {
        "Disabled" { return @{ Text = "[Отключён — будет включён]"; Color = "Red" } }
        "Up" { return @{ Text = "[Включён]"; Color = "Green" } }
        "Disconnected" { return @{ Text = "[Включён, нет подключения]"; Color = "Yellow" } }
        default { return @{ Text = "[$($Adapter.Status)]"; Color = "DarkYellow" } }
    }
}

function Confirm-Configuration {
    while ($true) {
        Write-Host "`nEnter — применить настройки; Esc — отменить." -ForegroundColor DarkGray
        $key = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")

        switch ($key.VirtualKeyCode) {
            13 { return $true }
            27 { return $false }
        }
    }
}

function Get-AdapterPriority {
    param([object]$Adapter)

    # The usual wired adapter should be first, while preserving all other choices.
    if ($Adapter.Name -ieq "Ethernet") { return 0 }
    if ($Adapter.HardwareInterface -and $Adapter.MediaType -eq "802.3") { return 1 }
    if ($Adapter.Name -match "(?i)^Ethernet") { return 2 }
    return 3
}

# Loading adapters can take noticeable time on some PCs, so start before the first menu.
$adapterJob = Get-AdaptersInBackground

$locationMenuItems = foreach ($item in $locations) {
    [pscustomobject]@{
        Label      = "{0} (10.20.{1}.{2}/{3})" -f $item.Name, $item.ThirdOctet, ($item.GatewayOctet - 1), $item.PrefixLength
        Value      = $item
    }
}
$location = Select-MenuItem -Title "$emojiNetwork Выберите корпус" -Items $locationMenuItems

# 20 is the usual network; 21 and 22 use the same host portion and settings.
$subnetOptions = @(20, 21, 22)
$subnetMenuItems = foreach ($subnet in $subnetOptions) {
    [pscustomobject]@{
        Label = "Сегмент $subnet"
        Value = $subnet
    }
}

# Keep a visual gap between the completed location block and the segment menu.
Write-Host "`n"
$secondOctet = Select-MenuItem -Title "Выберите сегмент сети" -Items $subnetMenuItems

$prefix = "10.{0}.{1}" -f $secondOctet, $location.ThirdOctet
$gateway = "{0}.{1}" -f $prefix, $location.GatewayOctet

$adapters = @(Receive-Job -Job $adapterJob -Wait -AutoRemoveJob |
    Sort-Object @{ Expression = { Get-AdapterPriority $_ } },
                @{ Expression = { $_.Status -eq "Disabled" } },
                @{ Expression = { $_.Name } })
if ($adapters.Count -eq 0) {
    throw "Сетевые адаптеры не найдены."
}

$adapterMenuItems = foreach ($item in $adapters) {
    $status = Get-AdapterStatusDisplay -Adapter $item
    [pscustomobject]@{
        Label      = "{0} ({1}) {2}" -f $item.Name, $item.InterfaceDescription, $status.Text
        Color      = $status.Color
        Value      = $item
    }
}
$adapter = Select-MenuItem -Title "Выберите сетевой адаптер" -Items $adapterMenuItems

do {
    $lastOctet = Read-Host "`nПоследний октет IP-адреса ($($location.FirstHostOctet)-$($location.LastHostOctet))"
} while ($lastOctet -notmatch '^\d+$' -or [int]$lastOctet -lt $location.FirstHostOctet -or [int]$lastOctet -gt $location.LastHostOctet)

$ipAddress = "{0}.{1}" -f $prefix, $lastOctet

Write-Host "`nБудут применены следующие изменения:" -ForegroundColor Yellow
Write-Host "  Корпус:   $($location.Name)" -ForegroundColor Cyan
Write-Host "  Сегмент:  $secondOctet" -ForegroundColor Cyan
Write-Host "  Адаптер:  $($adapter.Name)" -ForegroundColor Cyan
Write-Host "  IP:       $ipAddress" -ForegroundColor Cyan
Write-Host "  Маска:    $($location.SubnetMask)" -ForegroundColor Cyan
Write-Host "  Шлюз:     $gateway" -ForegroundColor Cyan
Write-Host "`nОбщие параметры (без изменений):" -ForegroundColor DarkGray
Write-Host "  DNS:      $($location.DnsServers -join ', ')" -ForegroundColor DarkGray

if (-not (Confirm-Configuration)) {
    Write-Host "$emojiCancelled Операция отменена." -ForegroundColor Yellow
    exit
}

if ($adapter.Status -eq "Disabled") {
    Write-Host "Включение адаптера '$($adapter.Name)'..." -ForegroundColor Yellow
    Enable-NetAdapter -Name $adapter.Name -Confirm:$false
}

# Remove all existing IPv4 addresses and default gateways from the selected adapter.
Get-NetIPAddress -InterfaceIndex $adapter.ifIndex -AddressFamily IPv4 -ErrorAction SilentlyContinue |
    Remove-NetIPAddress -Confirm:$false
Get-NetRoute -InterfaceIndex $adapter.ifIndex -AddressFamily IPv4 -DestinationPrefix "0.0.0.0/0" -ErrorAction SilentlyContinue |
    Remove-NetRoute -Confirm:$false

New-NetIPAddress -InterfaceIndex $adapter.ifIndex -IPAddress $ipAddress -PrefixLength $location.PrefixLength -DefaultGateway $gateway | Out-Null
Set-DnsClientServerAddress -InterfaceIndex $adapter.ifIndex -ServerAddresses $location.DnsServers

Write-Host "`n$emojiSuccess Настройки сети успешно применены." -ForegroundColor Green
$appliedConfiguration = Get-NetIPConfiguration -InterfaceIndex $adapter.ifIndex
Write-Host "  Адаптер:  $($appliedConfiguration.InterfaceAlias)"
Write-Host "  IPv4:     $($appliedConfiguration.IPv4Address.IPAddress)"
Write-Host "  Шлюз:     $($appliedConfiguration.IPv4DefaultGateway.NextHop)"
Write-Host "  DNS:      $($appliedConfiguration.DNSServer.ServerAddresses -join ', ')" -ForegroundColor DarkGray
