# Unit code stays a string to preserve the leading zero. Configure per unit here.
# The domain is not kept in the public repository: it comes from the cloud network profiles
# (web page «Сеть», field «Домен», or else «Проверка домена»), cached in network-profiles.json by synchronization.
function Get-ComputerPolicy {
    param([string]$Root = $PSScriptRoot)
    $domain = $null
    $path = Join-Path $Root 'network-profiles.json'
    if (Test-Path -LiteralPath $path) {
        try {
            $value = (Get-Content -LiteralPath $path -Raw -Encoding UTF8 | ConvertFrom-Json).value
            $domain = if ($value.domainName) { [string]$value.domainName } elseif ($value.domainCheckHost) { [string]$value.domainCheckHost } else { $null }
        }
        catch { $domain = $null }
    }
    # An IP address in «Проверка домена» is not a domain name.
    if ($domain -and $domain -match '^[\d.]+$') { $domain = $null }
    if (-not $domain) {
        throw 'Домен не задан: синхронизируйтесь с облаком, чтобы загрузить профили сети (в веб-интерфейсе — страница «Сеть», поле «Домен»).'
    }
    return [pscustomobject]@{
        UnitCode = '042'
        DomainName = $domain.Trim().ToLowerInvariant()
        WorkgroupName = 'WORKGROUP'
        InternetPrefix = 'IF'
        DomainPrefix = 'FR'
    }
}