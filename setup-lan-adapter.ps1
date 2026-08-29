$ErrorActionPreference = "Stop"

# Keep Russian text and emoji readable in Windows PowerShell consoles.
[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new()
[Console]::InputEncoding = [System.Text.UTF8Encoding]::new()
. (Join-Path $PSScriptRoot "console-ui.ps1")
$emojiNetwork = [char]::ConvertFromUtf32(0x1F310)
$emojiSuccess = [char]::ConvertFromUtf32(0x2705)

try {
    $Host.UI.RawUI.BackgroundColor = [ConsoleColor]::Black
    $Host.UI.RawUI.ForegroundColor = [ConsoleColor]::Gray
    Clear-Host
}
catch {
    # Continue with the host defaults if console colours are unavailable.
}

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
        $elevatedProcess = Start-Process -FilePath "powershell.exe" -Verb RunAs -ArgumentList $elevatedArguments -Wait -PassThru
        exit $elevatedProcess.ExitCode
    }
    catch {
        Write-Host "Права администратора не были предоставлены. Настройки не изменены." -ForegroundColor Red
        exit 1
    }
}

# The gateway belongs to the same subnet; reserve it and only allow usable host IPs.
$locations = @(
    [pscustomobject]@{
        Name             = "Корпус А"
        ThirdOctet       = 1
        PrefixLength     = 25
        SubnetMask       = "255.255.255.128"
        GatewayOctet     = 129
        DnsServers       = @("10.9.0.1", "10.9.0.2")
        FirstHostOctet   = 130
        LastHostOctet    = 254
    },
    [pscustomobject]@{
        Name             = "Корпус Б"
        ThirdOctet       = 2
        PrefixLength     = 27
        SubnetMask       = "255.255.255.224"
        GatewayOctet     = 65
        DnsServers       = @("10.9.0.1", "10.9.0.2")
        FirstHostOctet   = 66
        LastHostOctet    = 94
    },
    [pscustomobject]@{
        Name             = "Корпус В"
        ThirdOctet       = 3
        PrefixLength     = 28
        SubnetMask       = "255.255.255.240"
        GatewayOctet     = 97
        DnsServers       = @("10.9.0.1", "10.9.0.2")
        FirstHostOctet   = 98
        LastHostOctet    = 110
    }
)

function Show-ToolHeader {
    Write-ToolboxHeader -Section "$emojiNetwork Настройка LAN-адаптера"
    Write-Host ""
}

function Select-MenuItem {
    param(
        [string]$Step,
        [string]$Description,
        [object[]]$Items,
        [string]$EscapeHint = "[Esc] — назад."
    )

    if ($Items.Count -eq 0) {
        throw "В меню нет доступных пунктов."
    }

    $selectedIndex = 0

    while ($true) {
        Clear-Host
        Show-ToolHeader
        Write-Host "[▲▼] — выбор, [↲ Enter] — продолжить, $EscapeHint" -ForegroundColor DarkGray
        Write-Host $Step -ForegroundColor Cyan
        Write-Host $Description
        Write-Host ""

        for ($index = 0; $index -lt $Items.Count; $index++) {
            $item = $Items[$index]
            $marker = if ($index -eq $selectedIndex) { ">" } else { " " }
            $line = "{0} {1}. {2}" -f $marker, ($index + 1), $item.Label

            if ($item.StatusText) {
                if ($index -eq $selectedIndex) {
                    Write-Host ">" -NoNewline -ForegroundColor Cyan
                }
                else {
                    Write-Host " " -NoNewline
                }

                Write-Host (" {0}. {1} " -f ($index + 1), $item.Label) -NoNewline
                Write-Host $item.StatusText -ForegroundColor $item.StatusColor
            }
            elseif ($index -eq $selectedIndex) {
                Write-Host $line -ForegroundColor Cyan
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
            27 { return $null } # Escape
        }

    }
}

function Read-LastIpOctet {
    param(
        [int]$FirstHostOctet,
        [int]$LastHostOctet
    )

    $validationError = $null
    while ($true) {
        Clear-Host
        Show-ToolHeader
        Write-Host "[↲ Enter] — подтвердить, [Backspace] — исправить, [Esc] — назад." -ForegroundColor DarkGray
        Write-Host "Шаг 4 из 5" -ForegroundColor Cyan
        Write-Host "Введите последний октет IP-адреса ($FirstHostOctet-$LastHostOctet)."
        if ($validationError) {
            Write-Host $validationError -ForegroundColor Red
        }
        Write-Host ""
        Write-Host "> " -NoNewline
        $value = ""

        while ($true) {
            $key = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
            $submitted = $false

            switch ($key.VirtualKeyCode) {
                27 {
                    Write-Host ""
                    return $null
                }
                8 {
                    if ($value.Length -gt 0) {
                        $value = $value.Substring(0, $value.Length - 1)
                        Write-Host "`b `b" -NoNewline
                    }
                }
                13 { $submitted = $true }
                default {
                    if ([string]$key.Character -match '^\d$') {
                        $value += [string]$key.Character
                        Write-Host ([string]$key.Character) -NoNewline
                    }
                }
            }

            if ($submitted) {
                Write-Host ""
                if ($value -match '^\d+$' -and [int]$value -ge $FirstHostOctet -and [int]$value -le $LastHostOctet) {
                    return [int]$value
                }

                $validationError = "Введите число от $FirstHostOctet до $LastHostOctet."
                break
            }
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
        $key = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")

        switch ($key.VirtualKeyCode) {
            13 { return $true }
            27 { return $null }
        }
    }
}

function Show-ConfigurationSummary {
    param(
        [object]$Location,
        [int]$SecondOctet,
        [object]$Adapter,
        [string]$IpAddress,
        [string]$Gateway
    )

    Clear-Host
    Show-ToolHeader
    Write-Host "[↲ Enter] — применить настройки, [Esc] — назад." -ForegroundColor DarkGray
    Write-Host "Шаг 5 из 5" -ForegroundColor Cyan
    Write-Host "Будут применены следующие изменения:" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "  Корпус:   $($Location.Name) [$($Location.ThirdOctet)]" -ForegroundColor DarkGray
    Write-Host "  Сегмент:  $SecondOctet" -ForegroundColor DarkGray
    Write-Host "  Адаптер:  $($Adapter.Name)" -ForegroundColor DarkGray
    Write-Host "  IP:       $IpAddress" -ForegroundColor Cyan
    Write-Host "  Маска:    $($Location.SubnetMask)" -ForegroundColor Cyan
    Write-Host "  Шлюз:     $Gateway" -ForegroundColor Cyan
    for ($dnsIndex = 0; $dnsIndex -lt $Location.DnsServers.Count; $dnsIndex++) {
        Write-Host "  DNS $($dnsIndex + 1):    $($Location.DnsServers[$dnsIndex])" -ForegroundColor Cyan
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
        Label      = "{0,-16} [{1}]" -f $item.Name, $item.ThirdOctet
        Value      = $item
    }
}
# 20 is the usual network; 21 and 22 use the same host portion and settings.
$subnetOptions = @(20, 21, 22)
$subnetMenuItems = foreach ($subnet in $subnetOptions) {
    [pscustomobject]@{
        Label = "$subnet"
        Value = $subnet
    }
}

$adapterMenuItems = $null

:locationSelection while ($true) {
    $location = Select-MenuItem -Step "$emojiNetwork Шаг 1 из 5" -Description "Выберите корпус." -Items $locationMenuItems -EscapeHint "[Esc] — вернуться в главное меню."
    if ($null -eq $location) {
        # The launcher recognizes this as a normal direct return to its menu.
        exit 42
    }

    :segmentSelection while ($true) {
        $secondOctet = Select-MenuItem -Step "Шаг 2 из 5" -Description "Выберите сегмент сети." -Items $subnetMenuItems
        if ($null -eq $secondOctet) {
            continue locationSelection
        }

        $prefix = "10.{0}.{1}" -f $secondOctet, $location.ThirdOctet
        $gateway = "{0}.{1}" -f $prefix, $location.GatewayOctet

        if ($null -eq $adapterMenuItems) {
            $adapters = @(Receive-Job -Job $adapterJob -Wait -AutoRemoveJob |
                Where-Object { $_.HardwareInterface -and $_.MediaType -eq "802.3" } |
                Sort-Object @{ Expression = { Get-AdapterPriority $_ } },
                            @{ Expression = { $_.Status -eq "Disabled" } },
                            @{ Expression = { $_.Name } })
            if ($adapters.Count -eq 0) {
                throw "Проводные сетевые адаптеры не найдены."
            }

            $adapterMenuItems = @(
                foreach ($item in $adapters) {
                    $status = Get-AdapterStatusDisplay -Adapter $item
                    [pscustomobject]@{
                        Label       = "{0} ({1})" -f $item.Name, $item.InterfaceDescription
                        StatusText  = $status.Text
                        StatusColor = $status.Color
                        Value       = $item
                    }
                }
            )
        }

        Clear-Host
        Show-ToolHeader
        :adapterSelection while ($true) {
            $hasSingleAdapter = $adapterMenuItems.Count -eq 1
            if ($hasSingleAdapter) {
                $adapter = $adapterMenuItems[0].Value
                Write-Host "`nШаг 3 из 5 — используется единственный доступный адаптер:"
                Write-Host "  $($adapterMenuItems[0].Label)" -NoNewline
                Write-Host (" " + $adapterMenuItems[0].StatusText) -ForegroundColor $adapterMenuItems[0].StatusColor
            }
            else {
                $adapter = Select-MenuItem -Step "Шаг 3 из 5" -Description "Выберите сетевой адаптер." -Items $adapterMenuItems
                if ($null -eq $adapter) {
                    continue segmentSelection
                }
            }

            Clear-Host
            :ipSelection while ($true) {
                $lastOctet = Read-LastIpOctet -FirstHostOctet $location.FirstHostOctet -LastHostOctet $location.LastHostOctet
                if ($null -eq $lastOctet) {
                    if ($hasSingleAdapter) {
                        continue segmentSelection
                    }

                    continue adapterSelection
                }

                $ipAddress = "{0}.{1}" -f $prefix, $lastOctet
                Show-ConfigurationSummary -Location $location -SecondOctet $secondOctet -Adapter $adapter -IpAddress $ipAddress -Gateway $gateway
                if (Confirm-Configuration) {
                    break locationSelection
                }

            }
        }
    }
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
$appliedDnsServers = @($appliedConfiguration.DNSServer.ServerAddresses)
for ($dnsIndex = 0; $dnsIndex -lt $appliedDnsServers.Count; $dnsIndex++) {
    Write-Host "  DNS $($dnsIndex + 1):    $($appliedDnsServers[$dnsIndex])" -ForegroundColor Cyan
}
