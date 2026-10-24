$ErrorActionPreference = 'Stop'
$project = Split-Path -Parent $PSScriptRoot
. (Join-Path $project 'lan-profiles.ps1')

function Assert-True([bool]$Value, [string]$Message) {
    if (-not $Value) { throw $Message }
}

$root = Join-Path ([IO.Path]::GetTempPath()) ('comp-info-lan-test-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $root | Out-Null
try {
    $expected = @{ 24 = '255.255.255.0'; 25 = '255.255.255.128'; 27 = '255.255.255.224'; 28 = '255.255.255.240'; 30 = '255.255.255.252' }
    foreach ($length in $expected.Keys) {
        Assert-True ((ConvertTo-SubnetMask $length) -eq $expected[$length]) "Wrong mask for /$length"
    }

    # No cache yet: only DHCP can be offered.
    $missing = Read-NetworkProfiles (Join-Path $root 'absent.json')
    Assert-True ($missing.Missing -and $missing.Locations.Count -eq 0) 'A missing cache must give no locations'
    [IO.File]::WriteAllText((Join-Path $root 'broken.json'), '{broken')
    Assert-True (Read-NetworkProfiles (Join-Path $root 'broken.json')).Missing 'A broken cache must be treated as missing'

    # A cache as saved by synchronization (made-up addresses).
    $cache = Join-Path $root 'network-profiles.json'
    [IO.File]::WriteAllText($cache, @'
{
  "key": "network", "revision": 3, "updatedAt": "2026-10-24T10:00:00Z", "updatedBy": "admin@example.com",
  "value": {
    "firstOctet": 10, "segments": [20, 21], "domainCheckHost": "dc.example.local",
    "locations": [
      { "name": "Главный корпус", "thirdOctet": 5, "prefixLength": 25, "gatewayOctet": 129, "firstHostOctet": 130, "lastHostOctet": 254, "dnsServers": ["10.9.0.1", "10.9.0.2"] },
      { "name": "Склад", "thirdOctet": 7, "prefixLength": 28, "gatewayOctet": 97, "firstHostOctet": 98, "lastHostOctet": 110, "dnsServers": ["10.9.0.1"], "segments": [22] }
    ]
  }
}
'@, [Text.UTF8Encoding]::new($false))
    $profiles = Read-NetworkProfiles $cache
    Assert-True (-not $profiles.Missing -and $profiles.Locations.Count -eq 2 -and $profiles.Revision -eq 3) 'The cache must be loaded'
    $main = $profiles.Locations[0]
    Assert-True ($main.Name -eq 'Главный корпус' -and $main.SubnetMask -eq '255.255.255.128' -and $main.DnsServers.Count -eq 2) 'Location fields must be converted'
    Assert-True ($null -eq $main.PSObject.Properties['SubnetOptions']) 'Locations without own segments use the common ones'
    Assert-True (@($profiles.Locations[1].SubnetOptions) -join ',' -eq '22') 'Own segments must be kept'
    Assert-True ($profiles.DomainCheckHost -eq 'dc.example.local' -and (@($profiles.SubnetOptions) -join ',') -eq '20,21') 'Common settings must be loaded'

    # Test-ConfiguredLanAddress works from the loaded profiles.
    $locations = $profiles.Locations
    $subnetOptions = $profiles.SubnetOptions
    $networkFirstOctet = $profiles.FirstOctet
    Assert-True (Test-ConfiguredLanAddress '10.20.5.140') 'An address of a known location must be recognized'
    Assert-True (Test-ConfiguredLanAddress '10.21.5.254') 'Any common segment must be recognized'
    Assert-True (Test-ConfiguredLanAddress '10.22.7.100') 'Own segments of a location must be recognized'
    Assert-True (-not (Test-ConfiguredLanAddress '10.20.7.100')) 'Common segments do not apply to a location with own segments'
    Assert-True (-not (Test-ConfiguredLanAddress '10.20.5.100')) 'Addresses below the gateway are not ours'
    Assert-True (-not (Test-ConfiguredLanAddress '192.168.1.10')) 'Other networks are not ours'
    Assert-True (-not (Test-ConfiguredLanAddress 'garbage')) 'Invalid addresses are not ours'

    # No addresses in the program itself.
    foreach ($name in 'lan-profiles.ps1', 'setup-lan-adapter.ps1', 'quick-ping.ps1', 'scan-ips.ps1') {
        $text = [IO.File]::ReadAllText((Join-Path $project $name))
        Assert-True ($text -notmatch '\b10\.\d{1,3}\.\d{1,3}\.\d{1,3}\b') "$name must not contain internal IPv4 addresses"
    }
    [Console]::Out.WriteLine('LAN profile checks passed.')
}
finally {
    Remove-Item -LiteralPath $root -Recurse -Force
}
