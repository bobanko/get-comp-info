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
    [pscustomobject]@{
        Name             = "Корпус Д"
        ThirdOctet       = 5
        SubnetOptions    = @(20)
        PrefixLength     = 25
        SubnetMask       = "255.255.255.128"
        GatewayOctet     = 129
        DnsServers       = @("10.9.0.1", "10.9.0.2")
        FirstHostOctet   = 130
        LastHostOctet    = 254
    }
)

$subnetOptions = @(20, 21, 22)

function Test-ConfiguredLanAddress {
    param([string]$Address)

    $parsedAddress = $null
    if (-not [System.Net.IPAddress]::TryParse($Address, [ref]$parsedAddress) -or
        $parsedAddress.AddressFamily -ne [System.Net.Sockets.AddressFamily]::InterNetwork) {
        return $false
    }

    $octets = $parsedAddress.GetAddressBytes()
    foreach ($location in $locations) {
        $segments = if ($location.SubnetOptions) { $location.SubnetOptions } else { $subnetOptions }
        if ($octets[0] -eq 10 -and $octets[1] -in $segments -and
            $octets[2] -eq $location.ThirdOctet -and
            $octets[3] -ge $location.GatewayOctet -and $octets[3] -le $location.LastHostOctet) {
            return $true
        }
    }
    return $false
}