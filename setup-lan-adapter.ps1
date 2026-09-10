[CmdletBinding()]
param(
    [switch]$ApplyConfiguration,
    [int]$ApplyInterfaceIndex,
    [string]$ApplyIpAddress,
    [int]$ApplyPrefixLength,
    [string]$ApplyGateway,
    [string]$ApplyDnsServers,
    [string]$ApplyErrorFile
)

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

function Invoke-NetworkConfiguration {
    param(
        [int]$InterfaceIndex,
        [string]$IpAddress,
        [int]$PrefixLength,
        [string]$Gateway,
        [string[]]$DnsServers
    )

    try {
        $networkAdapter = Get-NetAdapter -InterfaceIndex $InterfaceIndex -ErrorAction Stop
    }
    catch {
        throw "Не удалось получить адаптер с индексом ${InterfaceIndex}: $($_.Exception.Message)"
    }

    if ($networkAdapter.Status -eq "Disabled") {
        try {
            $networkAdapter | Enable-NetAdapter -Confirm:$false -ErrorAction Stop
        }
        catch {
            throw "Не удалось включить адаптер '$($networkAdapter.Name)': $($_.Exception.Message)"
        }
    }

    try {
        Get-NetIPAddress -InterfaceIndex $InterfaceIndex -AddressFamily IPv4 -ErrorAction SilentlyContinue |
            Remove-NetIPAddress -Confirm:$false -ErrorAction Stop
    }
    catch {
        throw "Не удалось удалить прежний IPv4-адрес адаптера '$($networkAdapter.Name)': $($_.Exception.Message)"
    }

    try {
        Get-NetRoute -InterfaceIndex $InterfaceIndex -AddressFamily IPv4 -DestinationPrefix "0.0.0.0/0" -ErrorAction SilentlyContinue |
            Remove-NetRoute -Confirm:$false -ErrorAction Stop
    }
    catch {
        throw "Не удалось удалить прежний маршрут по умолчанию адаптера '$($networkAdapter.Name)': $($_.Exception.Message)"
    }

    try {
        New-NetIPAddress -InterfaceIndex $InterfaceIndex -IPAddress $IpAddress -PrefixLength $PrefixLength -DefaultGateway $Gateway -ErrorAction Stop | Out-Null
    }
    catch {
        throw "Не удалось назначить адрес $IpAddress/$PrefixLength и шлюз $Gateway адаптеру '$($networkAdapter.Name)': $($_.Exception.Message)"
    }

    try {
        Set-DnsClientServerAddress -InterfaceIndex $InterfaceIndex -ServerAddresses $DnsServers -ErrorAction Stop
    }
    catch {
        throw "IP-адрес назначен, но не удалось установить DNS $($DnsServers -join ', '): $($_.Exception.Message)"
    }
}

if ($ApplyConfiguration) {
    try {
        Invoke-NetworkConfiguration -InterfaceIndex $ApplyInterfaceIndex -IpAddress $ApplyIpAddress -PrefixLength $ApplyPrefixLength -Gateway $ApplyGateway -DnsServers ($ApplyDnsServers -split ',')
        exit 0
    }
    catch {
        $errorDetails = $_.Exception.Message
        if (-not [string]::IsNullOrWhiteSpace($ApplyErrorFile)) {
            try {
                [IO.File]::WriteAllText($ApplyErrorFile, $errorDetails, [Text.UTF8Encoding]::new($false))
            }
            catch {
                # The parent will still report the elevated process exit code.
            }
        }
        exit 1
    }
}

function Invoke-SelectedNetworkConfiguration {
    param(
        [object]$Adapter,
        [string]$IpAddress,
        [int]$PrefixLength,
        [string]$Gateway,
        [string[]]$DnsServers
    )

    if ($isAdministrator) {
        Invoke-NetworkConfiguration -InterfaceIndex $Adapter.ifIndex -IpAddress $IpAddress -PrefixLength $PrefixLength -Gateway $Gateway -DnsServers $DnsServers
        return
    }

    Write-Host "Запрашиваются права администратора для применения настроек..." -ForegroundColor Yellow
    $errorFile = Join-Path ([IO.Path]::GetTempPath()) ("setup-lan-adapter-{0}.error" -f [guid]::NewGuid().ToString("N"))
    $elevatedArguments = @(
        "-NoProfile",
        "-ExecutionPolicy", "Bypass",
        "-File", ('"{0}"' -f $PSCommandPath),
        "-ApplyConfiguration",
        "-ApplyInterfaceIndex", $Adapter.ifIndex,
        "-ApplyIpAddress", ('"{0}"' -f $IpAddress),
        "-ApplyPrefixLength", $PrefixLength,
        "-ApplyGateway", ('"{0}"' -f $Gateway),
        "-ApplyDnsServers", ('"{0}"' -f ($DnsServers -join ',')),
        "-ApplyErrorFile", ('"{0}"' -f $errorFile)
    )

    try {
        $elevatedProcess = Start-Process -FilePath "powershell.exe" -Verb RunAs -WindowStyle Hidden -ArgumentList $elevatedArguments -Wait -PassThru
        if ($elevatedProcess.ExitCode -ne 0) {
            $errorDetails = if (Test-Path -LiteralPath $errorFile) {
                Get-Content -LiteralPath $errorFile -Raw -Encoding UTF8
            }
            else {
                "Повышенный процесс завершился с кодом $($elevatedProcess.ExitCode), но не передал описание ошибки."
            }
            throw $errorDetails.Trim()
        }
    }
    finally {
        if (Test-Path -LiteralPath $errorFile) {
            Remove-Item -LiteralPath $errorFile -Force -ErrorAction SilentlyContinue
        }
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
    [pscustomobject]@{
        Name             = "Корпус Г"
        ThirdOctet       = 4
        PrefixLength     = 29
        SubnetMask       = "255.255.255.248"
        GatewayOctet     = 65
        DnsServers       = @("10.9.0.1", "10.9.0.2")
        FirstHostOctet   = 66
        LastHostOctet    = 70
    }
)

function Show-ToolHeader {
    param([string]$WizardStep)

    Write-ToolboxHeader -Section "$emojiNetwork Настройка LAN-адаптера" -WizardStep $WizardStep
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
        Show-ToolHeader -WizardStep $Step
        Write-ToolboxHint "[▲▼] — выбор, [↲ Enter] — продолжить, $EscapeHint"
        Show-WizardSelection
        Write-Host ""
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
        Show-ToolHeader -WizardStep "Шаг 4 из 5"
        Write-ToolboxHint "[↲ Enter] — подтвердить, [Backspace] — исправить, [Esc] — назад."
        Show-WizardSelection
        Write-Host ""
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
    Show-ToolHeader -WizardStep "Шаг 5 из 5"
    Write-ToolboxHint "[↲ Enter] — применить настройки, [Esc] — назад."
    Show-WizardSelection
    Write-Host ""
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
$script:wizardSelection = [ordered]@{
    Location    = $null
    SecondOctet = $null
    Adapter     = $null
    IpAddress   = $null
}

function Show-WizardSelection {
    if ($null -eq $script:wizardSelection.Location) {
        return
    }

    $location = $script:wizardSelection.Location
    Write-Host "Корпус: $($location.Name) $($location.ThirdOctet)" -ForegroundColor DarkGray

    if ($null -ne $script:wizardSelection.SecondOctet) {
        Write-Host "Сегмент сети: $($script:wizardSelection.SecondOctet)" -ForegroundColor DarkGray
    }
    if ($null -ne $script:wizardSelection.Adapter) {
        Write-Host "Адаптер: $($script:wizardSelection.Adapter.Name)" -ForegroundColor DarkGray
    }
    if (-not [string]::IsNullOrWhiteSpace($script:wizardSelection.IpAddress)) {
        Write-Host "IP-адрес: $($script:wizardSelection.IpAddress)" -ForegroundColor DarkGray
    }
}

:locationSelection while ($true) {
    $location = Select-MenuItem -Step "Шаг 1 из 5" -Description "Выберите корпус." -Items $locationMenuItems -EscapeHint "[Esc] — вернуться в главное меню."
    if ($null -eq $location) {
        # The launcher recognizes this as a normal direct return to its menu.
        exit 42
    }

    $script:wizardSelection.Location = $location
    $script:wizardSelection.SecondOctet = $null
    $script:wizardSelection.Adapter = $null
    $script:wizardSelection.IpAddress = $null

    :segmentSelection while ($true) {
        $secondOctet = Select-MenuItem -Step "Шаг 2 из 5" -Description "Выберите сегмент сети." -Items $subnetMenuItems
        if ($null -eq $secondOctet) {
            $script:wizardSelection.SecondOctet = $null
            $script:wizardSelection.Adapter = $null
            $script:wizardSelection.IpAddress = $null
            continue locationSelection
        }

        $script:wizardSelection.SecondOctet = $secondOctet
        $script:wizardSelection.Adapter = $null
        $script:wizardSelection.IpAddress = $null

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
        Show-ToolHeader -WizardStep "Шаг 3 из 5"
        :adapterSelection while ($true) {
            $hasSingleAdapter = $adapterMenuItems.Count -eq 1
            if ($hasSingleAdapter) {
                $adapter = $adapterMenuItems[0].Value
                Write-Host "`nИспользуется единственный доступный адаптер:"
                Write-Host "  $($adapterMenuItems[0].Label)" -NoNewline
                Write-Host (" " + $adapterMenuItems[0].StatusText) -ForegroundColor $adapterMenuItems[0].StatusColor
            }
            else {
                $adapter = Select-MenuItem -Step "Шаг 3 из 5" -Description "Выберите сетевой адаптер." -Items $adapterMenuItems
                if ($null -eq $adapter) {
                    $script:wizardSelection.Adapter = $null
                    $script:wizardSelection.IpAddress = $null
                    continue segmentSelection
                }
            }

            $script:wizardSelection.Adapter = $adapter
            $script:wizardSelection.IpAddress = $null

            Clear-Host
            :ipSelection while ($true) {
                $lastOctet = Read-LastIpOctet -FirstHostOctet $location.FirstHostOctet -LastHostOctet $location.LastHostOctet
                if ($null -eq $lastOctet) {
                    $script:wizardSelection.IpAddress = $null
                    if ($hasSingleAdapter) {
                        $script:wizardSelection.Adapter = $null
                        continue segmentSelection
                    }

                    continue adapterSelection
                }

                $ipAddress = "{0}.{1}" -f $prefix, $lastOctet
                $script:wizardSelection.IpAddress = $ipAddress
                Show-ConfigurationSummary -Location $location -SecondOctet $secondOctet -Adapter $adapter -IpAddress $ipAddress -Gateway $gateway
                if (Confirm-Configuration) {
                    break locationSelection
                }

            }
        }
    }
}

try {
    Invoke-SelectedNetworkConfiguration -Adapter $adapter -IpAddress $ipAddress -PrefixLength $location.PrefixLength -Gateway $gateway -DnsServers $location.DnsServers
}
catch {
    Write-Host "`nНастройки сети не были применены: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}

Write-Host "`n$emojiSuccess Настройки сети успешно применены." -ForegroundColor Green
$appliedConfiguration = Get-NetIPConfiguration -InterfaceIndex $adapter.ifIndex
Write-Host "  Адаптер:  $($appliedConfiguration.InterfaceAlias)"
Write-Host "  IPv4:     $($appliedConfiguration.IPv4Address.IPAddress)"
Write-Host "  Шлюз:     $($appliedConfiguration.IPv4DefaultGateway.NextHop)"
$appliedDnsServers = @($appliedConfiguration.DNSServer.ServerAddresses)
for ($dnsIndex = 0; $dnsIndex -lt $appliedDnsServers.Count; $dnsIndex++) {
    Write-Host "  DNS $($dnsIndex + 1):    $($appliedDnsServers[$dnsIndex])" -ForegroundColor Cyan
}
