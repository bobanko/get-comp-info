$subnet = "10.20.2"
$start = 65
$end = 94
. (Join-Path $PSScriptRoot "console-ui.ps1")

Write-ToolboxHeader -Section "IP scanner [draft]"

Write-Host "`nСканирование $subnet.$start – $subnet.$end ...`n" -ForegroundColor Cyan

# Быстро пингуем все адреса (чтобы обновить ARP-кэш)
65..94 | ForEach-Object {
    $ip = "$subnet.$_"
    Test-Connection -ComputerName $ip -Count 1 -Quiet -TimeoutSeconds 1 | Out-Null
}

# Смотрим ARP-таблицу
$arp = arp -a | Select-String "$subnet\."

$occupied = @{}
foreach ($line in $arp) {
    if ($line -match "($subnet\.\d+)\s+([\da-f-]+)") {
        $occupied[$matches[1]] = $matches[2]
    }
}

# Выводим результат
65..94 | ForEach-Object {
    $ip = "$subnet.$_"
    if ($occupied.ContainsKey($ip)) {
        Write-Host ("{0,-18} {1}" -f $ip, "ЗАНЯТ   ($($occupied[$ip]))") -ForegroundColor Red
    } else {
        Write-Host ("{0,-18} {1}" -f $ip, "свободен") -ForegroundColor Green
    }
}
