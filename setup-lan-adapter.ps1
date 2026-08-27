$ErrorActionPreference = "Stop"

$currentIdentity = [Security.Principal.WindowsIdentity]::GetCurrent()
$currentPrincipal = New-Object Security.Principal.WindowsPrincipal($currentIdentity)
$isAdministrator = $currentPrincipal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)

if (-not $isAdministrator) {
    Write-Host "Requesting administrator privileges..." -ForegroundColor Yellow

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
        Write-Host "Administrator privileges were not granted. The configuration was not changed." -ForegroundColor Red
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

function Read-MenuChoice {
    param(
        [string]$Prompt,
        [int]$Maximum
    )

    do {
        $answer = Read-Host $Prompt
    } while ($answer -notmatch '^\d+$' -or [int]$answer -lt 1 -or [int]$answer -gt $Maximum)

    return [int]$answer
}

Write-Host "`nSelect location:" -ForegroundColor Cyan
for ($index = 0; $index -lt $locations.Count; $index++) {
    Write-Host ("  {0}. {1}" -f ($index + 1), $locations[$index].Name)
}
$location = $locations[(Read-MenuChoice -Prompt "Location number" -Maximum $locations.Count) - 1]

# 20 is the usual network; 21 and 22 use the same host portion and settings.
$subnetOptions = @(20, 21, 22)
$subnetInput = Read-Host "Network segment: 20 (default), 21, or 22"
if ([string]::IsNullOrWhiteSpace($subnetInput)) {
    $secondOctet = 20
}
elseif ($subnetInput -match '^\d+$' -and [int]$subnetInput -in $subnetOptions) {
    $secondOctet = [int]$subnetInput
}
else {
    Write-Host "Invalid network segment. Use 20, 21, or 22." -ForegroundColor Red
    exit 1
}

$prefix = "10.{0}.{1}" -f $secondOctet, $location.ThirdOctet
$gateway = "{0}.{1}" -f $prefix, $location.GatewayOctet

$adapters = @(Get-NetAdapter | Where-Object { $_.Status -ne "Disabled" } | Sort-Object -Property ifIndex)
if ($adapters.Count -eq 0) {
    throw "No enabled network adapters were found."
}

Write-Host "`nSelect network adapter:" -ForegroundColor Cyan
for ($index = 0; $index -lt $adapters.Count; $index++) {
    $adapter = $adapters[$index]
    Write-Host ("  {0}. {1} ({2}, {3})" -f ($index + 1), $adapter.Name, $adapter.InterfaceDescription, $adapter.Status)
}
$adapter = $adapters[(Read-MenuChoice -Prompt "Adapter number" -Maximum $adapters.Count) - 1]

do {
    $lastOctet = Read-Host "`nLast IP octet ($($location.FirstHostOctet)-$($location.LastHostOctet))"
} while ($lastOctet -notmatch '^\d+$' -or [int]$lastOctet -lt $location.FirstHostOctet -or [int]$lastOctet -gt $location.LastHostOctet)

$ipAddress = "{0}.{1}" -f $prefix, $lastOctet

Write-Host "`nThe following configuration will be applied:" -ForegroundColor Yellow
Write-Host "  Location: $($location.Name)"
Write-Host "  Adapter:  $($adapter.Name)"
Write-Host "  IP:       $ipAddress"
Write-Host "  Mask:     $($location.SubnetMask)"
Write-Host "  Gateway:  $gateway"
Write-Host "  DNS:      $($location.DnsServers -join ', ')"

$confirmation = Read-Host "Continue? (Y/N)"
$yesAnswers = @("Y", "y", [string][char]0x0414, [string][char]0x0434)
if ($confirmation -notin $yesAnswers) {
    Write-Host "Operation cancelled." -ForegroundColor Yellow
    exit
}

# Remove all existing IPv4 addresses and default gateways from the selected adapter.
Get-NetIPAddress -InterfaceIndex $adapter.ifIndex -AddressFamily IPv4 -ErrorAction SilentlyContinue |
    Remove-NetIPAddress -Confirm:$false
Get-NetRoute -InterfaceIndex $adapter.ifIndex -AddressFamily IPv4 -DestinationPrefix "0.0.0.0/0" -ErrorAction SilentlyContinue |
    Remove-NetRoute -Confirm:$false

New-NetIPAddress -InterfaceIndex $adapter.ifIndex -IPAddress $ipAddress -PrefixLength $location.PrefixLength -DefaultGateway $gateway | Out-Null
Set-DnsClientServerAddress -InterfaceIndex $adapter.ifIndex -ServerAddresses $location.DnsServers

Write-Host "`nNetwork configuration successfully applied." -ForegroundColor Green
Get-NetIPConfiguration -InterfaceIndex $adapter.ifIndex | Format-List InterfaceAlias, IPv4Address, IPv4DefaultGateway, DNSServer
