# Draft: shows which addresses of a subnet answer or appear in the ARP table.
# The subnet is asked at start (or passed as -Subnet 10.20.5 -Start 65 -End 94); no addresses are stored here.
param(
    [string]$Subnet,
    [int]$Start = 1,
    [int]$End = 254
)

. (Join-Path $PSScriptRoot "console-ui.ps1")

Write-ToolboxHeader -Section "IP scanner [draft]"
while ($Subnet -notmatch '^\d{1,3}\.\d{1,3}\.\d{1,3}$') {
    Write-Host 'Первые три октета подсети, например 10.20.5:'
    $Subnet = (Read-Host '>').Trim()
}
if ($Start -lt 1 -or $End -gt 254 -or $Start -gt $End) { throw 'Диапазон последнего октета должен быть внутри 1–254.' }

Write-Host "`nСканирование $Subnet.$Start – $Subnet.$End ...`n" -ForegroundColor Cyan

# Ping every address once to refresh the ARP cache.
$ping = New-Object System.Net.NetworkInformation.Ping
try {
    foreach ($octet in $Start..$End) {
        try { [void]$ping.Send("$Subnet.$octet", 300) } catch { }
    }
}
finally { $ping.Dispose() }

$pattern = [regex]::Escape($Subnet) + '\.(\d+)\s+([\da-f-]{17})'
$occupied = @{}
foreach ($line in (arp -a)) {
    if ($line -match $pattern) { $occupied["$Subnet.$($matches[1])"] = $matches[2] }
}

foreach ($octet in $Start..$End) {
    $ip = "$Subnet.$octet"
    if ($occupied.ContainsKey($ip)) {
        Write-Host ("{0,-18} {1}" -f $ip, "ЗАНЯТ   ($($occupied[$ip]))") -ForegroundColor Red
    } else {
        Write-Host ("{0,-18} {1}" -f $ip, "свободен") -ForegroundColor Green
    }
}
