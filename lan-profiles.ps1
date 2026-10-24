# Network profiles for the LAN adapter setup and quick ping.
# They are kept only in the cloud (web page «Сеть»); synchronization saves them to network-profiles.json
# next to the program, so the setup works offline. Loading this file only reads that cache.

function ConvertTo-SubnetMask {
    param([int]$PrefixLength)
    $mask = [uint32]0
    for ($bit = 0; $bit -lt $PrefixLength; $bit++) { $mask = $mask -bor ([uint32]1 -shl (31 - $bit)) }
    return (@(24, 16, 8, 0) | ForEach-Object { ($mask -shr $_) -band 255 }) -join '.'
}

# Returns @{ Missing; Locations; SubnetOptions; FirstOctet; DomainCheckHost; Revision; UpdatedAt }.
function Read-NetworkProfiles {
    param([string]$Path)
    $empty = [pscustomobject]@{ Missing = $true; Locations = @(); SubnetOptions = @(); FirstOctet = 10; DomainCheckHost = $null; Revision = $null; UpdatedAt = $null }
    if (-not (Test-Path -LiteralPath $Path)) { return $empty }
    try {
        $record = Get-Content -LiteralPath $Path -Raw -Encoding UTF8 | ConvertFrom-Json
        $value = $record.value
        $locations = @(foreach ($item in @($value.locations)) {
            $location = [pscustomobject]@{
                Name           = [string]$item.name
                ThirdOctet     = [int]$item.thirdOctet
                PrefixLength   = [int]$item.prefixLength
                SubnetMask     = ConvertTo-SubnetMask ([int]$item.prefixLength)
                GatewayOctet   = [int]$item.gatewayOctet
                DnsServers     = @($item.dnsServers | ForEach-Object { [string]$_ })
                FirstHostOctet = [int]$item.firstHostOctet
                LastHostOctet  = [int]$item.lastHostOctet
            }
            if ($item.segments) { $location | Add-Member -NotePropertyName SubnetOptions -NotePropertyValue @($item.segments | ForEach-Object { [int]$_ }) }
            $location
        })
        if ($locations.Count -eq 0) { return $empty }
        return [pscustomobject]@{
            Missing         = $false
            Locations       = $locations
            SubnetOptions   = @($value.segments | ForEach-Object { [int]$_ })
            FirstOctet      = [int]$value.firstOctet
            DomainCheckHost = if ($value.domainCheckHost) { [string]$value.domainCheckHost } else { $null }
            Revision        = $record.revision
            UpdatedAt       = $record.updatedAt
        }
    }
    catch {
        return $empty
    }
}

$networkProfiles = Read-NetworkProfiles (Join-Path $PSScriptRoot 'network-profiles.json')
$locations = $networkProfiles.Locations
$subnetOptions = $networkProfiles.SubnetOptions
$networkFirstOctet = $networkProfiles.FirstOctet

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
        if ($octets[0] -eq $networkFirstOctet -and $octets[1] -in $segments -and
            $octets[2] -eq $location.ThirdOctet -and
            $octets[3] -ge $location.GatewayOctet -and $octets[3] -le $location.LastHostOctet) {
            return $true
        }
    }
    return $false
}
