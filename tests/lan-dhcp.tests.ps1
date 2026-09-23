$ErrorActionPreference = 'Stop'
$project = Split-Path -Parent $PSScriptRoot
$parseErrors = $null
$ast = [Management.Automation.Language.Parser]::ParseFile((Join-Path $project 'setup-lan-adapter.ps1'), [ref]$null, [ref]$parseErrors)
if ($parseErrors.Count) { throw ($parseErrors | Out-String) }
foreach ($name in @('Invoke-NetworkConfiguration', 'Invoke-SelectedNetworkConfiguration')) {
    $definition = $ast.Find({ param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq $name }, $true)
    . ([scriptblock]::Create($definition.Extent.Text))
}
function Assert-True($Value, $Message) { if (-not $Value) { throw $Message } }
$script:calls = New-Object 'System.Collections.Generic.List[string]'
function Get-NetAdapter { param($InterfaceIndex, $ErrorAction) [pscustomobject]@{ Name = 'Test'; Status = 'Disabled'; ifIndex = $InterfaceIndex } }
function Enable-NetAdapter { param([Parameter(ValueFromPipeline)]$InputObject, $Confirm) process { $script:calls.Add('enable') } }
function Get-NetRoute {
    param($PolicyStore, $ErrorAction)
    foreach ($index in @(7, 8)) {
        foreach ($protocol in @('NetMgmt', 'Dhcp')) {
            [pscustomobject]@{ InterfaceIndex = $index; AddressFamily = 'IPv4'; DestinationPrefix = '0.0.0.0/0'; Protocol = $protocol; Store = $PolicyStore }
        }
    }
}
function Get-NetIPAddress {
    param($PolicyStore, $ErrorAction)
    foreach ($index in @(7, 8)) {
        foreach ($family in @('IPv4', 'IPv6')) {
            foreach ($origin in @('Manual', 'Dhcp')) {
                [pscustomobject]@{ InterfaceIndex = $index; AddressFamily = $family; PrefixOrigin = $origin; Store = $PolicyStore }
            }
        }
    }
}
function Remove-NetRoute {
    param([Parameter(ValueFromPipeline)]$InputObject, $Confirm)
    process {
        Assert-True ($InputObject.InterfaceIndex -eq 7 -and $InputObject.Protocol -eq 'NetMgmt') 'Removed an unrelated route'
        $script:calls.Add('route:' + $InputObject.Store)
    }
}
function Remove-NetIPAddress {
    param([Parameter(ValueFromPipeline)]$InputObject, $Confirm)
    process {
        Assert-True ($InputObject.InterfaceIndex -eq 7 -and $InputObject.AddressFamily -eq 'IPv4' -and $InputObject.PrefixOrigin -eq 'Manual') 'Removed an unrelated address'
        $script:calls.Add('ip:' + $InputObject.Store)
    }
}
function Set-NetIPInterface {
    param($InterfaceIndex, $AddressFamily, $Dhcp, $ErrorAction)
    Assert-True ($InterfaceIndex -eq 7 -and $AddressFamily -eq 'IPv4' -and $Dhcp -eq 'Enabled') 'Wrong DHCP target'
    $script:calls.Add('dhcp')
}
function Set-DnsClientServerAddress {
    param($InterfaceIndex, [switch]$ResetServerAddresses, $ErrorAction)
    Assert-True ($InterfaceIndex -eq 7 -and $ResetServerAddresses) 'DNS was not reset on the selected adapter'
    $script:calls.Add('dns')
}
function New-NetIPAddress { throw 'DHCP must not assign a static IP' }
$isAdministrator = $true
Invoke-SelectedNetworkConfiguration -Adapter ([pscustomobject]@{ ifIndex = 7 }) -UseDhcp
Assert-True (($script:calls -join ',') -eq 'enable,route:PersistentStore,ip:PersistentStore,route:ActiveStore,ip:ActiveStore,dhcp,dns') 'Incomplete reset'

# Already automatic / empty configuration should still enable DHCP and reset DNS.
function Get-NetRoute { param($PolicyStore, $ErrorAction) }
function Get-NetIPAddress { param($PolicyStore, $ErrorAction) }
$script:calls.Clear()
Invoke-NetworkConfiguration -InterfaceIndex 7 -UseDhcp
Assert-True (($script:calls -join ',') -eq 'enable,dhcp,dns') 'Empty configuration reset failed'

# Exercise the UAC handoff without launching or changing anything.
$isAdministrator = $false
function Start-Process {
    param($FilePath, $Verb, $WindowStyle, $ArgumentList, [switch]$Wait, [switch]$PassThru)
    Assert-True ($Verb -eq 'RunAs' -and $WindowStyle -eq 'Hidden') 'Wrong elevation options'
    Assert-True ('-ApplyDhcp' -in $ArgumentList -and '-ApplyIpAddress' -notin $ArgumentList) 'Wrong DHCP arguments'
    Assert-True ($ArgumentList[([array]::IndexOf($ArgumentList, '-ApplyInterfaceIndex') + 1)] -eq 7) 'Wrong elevated adapter'
    [pscustomobject]@{ ExitCode = 0 }
}
Invoke-SelectedNetworkConfiguration -Adapter ([pscustomobject]@{ ifIndex = 7 }) -UseDhcp
'DHCP reset checks passed (mocked network commands; no network changes).'
