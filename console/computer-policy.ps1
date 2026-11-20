# Unit code stays a string to preserve the leading zero. Configure per unit here.
function Get-ComputerPolicy {
    param([string]$Root = (Split-Path -Parent $PSScriptRoot))
    return [pscustomobject]@{
        UnitCode = '042'
        DomainName = 'gu.gvardia.mvs'
        WorkgroupName = 'WORKGROUP'
        InternetPrefix = 'IF'
        DomainPrefix = 'FR'
    }
}
