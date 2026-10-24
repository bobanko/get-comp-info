[CmdletBinding()]
param(
    [int]$InterfaceIndex,
    [switch]$PauseAtEnd
)

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "console-ui.ps1")
. (Join-Path $PSScriptRoot "lan-profiles.ps1")

function Invoke-QuickPing {
    param([string]$Label, [string]$Address)

    Write-Host ("{0}: {1}" -f $Label, $Address)
    if ([string]::IsNullOrWhiteSpace($Address)) {
        Write-Host "❌ [ОШИБКА] Адрес не настроен." -ForegroundColor Red
        return $false
    }

    $ping = New-Object System.Net.NetworkInformation.Ping
    try {
        Write-Host '  ⏳ [ПИНГ] Отправка запроса, ожидание ответа ' -NoNewline -ForegroundColor Yellow
        $timer = [Diagnostics.Stopwatch]::StartNew()
        $pendingReply = $ping.SendPingAsync($Address, 2000)
        while (-not $pendingReply.IsCompleted) {
            Write-Host '.' -NoNewline -ForegroundColor Yellow
            Start-Sleep -Milliseconds 250
        }
        Write-Host (" {0:N1} с" -f $timer.Elapsed.TotalSeconds) -ForegroundColor DarkGray
        $reply = $pendingReply.GetAwaiter().GetResult()
        if ($reply.Status -eq [System.Net.NetworkInformation.IPStatus]::Success) {
            Write-Host ("  ✅ [ОТВЕТИЛ] {0}, {1} мс, TTL={2}" -f $reply.Address, $reply.RoundtripTime, $reply.Options.Ttl) -ForegroundColor Green
            return $true
        }
        Write-Host ("❌ [НЕТ ОТВЕТА] {0}" -f $reply.Status) -ForegroundColor Red
        return $false
    }
    catch {
        Write-Host ("❌ [ОШИБКА] {0}" -f $_.Exception.GetBaseException().Message) -ForegroundColor Red
        return $false
    }
    finally {
        $ping.Dispose()
    }
}

function Invoke-QuickPingSequence {
    Write-Host '🔎 [ПОДГОТОВКА] Определение активного адаптера и основного маршрута...' -ForegroundColor Yellow
    $interfaces = @(Get-NetIPInterface -AddressFamily IPv4 | Where-Object { $_.ConnectionState -eq 'Connected' })
    $routes = @(Get-NetRoute -AddressFamily IPv4 -DestinationPrefix '0.0.0.0/0' -ErrorAction SilentlyContinue |
        Where-Object { $_.InterfaceIndex -in $interfaces.InterfaceIndex } |
        Sort-Object @{ Expression = {
            $route = $_
            $ipInterface = $interfaces | Where-Object { $_.InterfaceIndex -eq $route.InterfaceIndex } | Select-Object -First 1
            [long]$route.RouteMetric + [long]$ipInterface.InterfaceMetric
        } })

    if ($InterfaceIndex) {
        $selectedIndex = $InterfaceIndex
        $route = $routes | Where-Object { $_.InterfaceIndex -eq $selectedIndex } | Select-Object -First 1
    }
    else {
        $route = $routes | Select-Object -First 1
        $selectedIndex = if ($route) { $route.InterfaceIndex } else { ($interfaces | Sort-Object InterfaceMetric | Select-Object -First 1).InterfaceIndex }
    }
    if (-not $selectedIndex) { throw 'Не найдено активное IPv4-подключение.' }

    Write-Host '🔎 [ПОДГОТОВКА] Чтение текущего IP-адреса и DNS-серверов...' -ForegroundColor Yellow
    $config = Get-NetIPConfiguration -InterfaceIndex $selectedIndex
    $localAddress = Get-NetIPAddress -InterfaceIndex $selectedIndex -AddressFamily IPv4 |
        Where-Object { $_.AddressState -eq 'Preferred' -and -not $_.SkipAsSource } |
        Select-Object -First 1 -ExpandProperty IPAddress
    $dnsServers = @((Get-DnsClientServerAddress -InterfaceIndex $selectedIndex -AddressFamily IPv4).ServerAddresses |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -Unique)

    Write-Host ("Адаптер: {0} (индекс {1})" -f $config.InterfaceAlias, $selectedIndex) -ForegroundColor Cyan
    Write-Host 'Один запрос на узел, ожидание ответа до 2 секунд. Остановка при первом сбое.' -ForegroundColor DarkGray
    Write-Host ''

    if (-not (Invoke-QuickPing -Label 'Свой IP' -Address $localAddress)) { return $false }
    if (-not (Invoke-QuickPing -Label 'Основной шлюз' -Address $route.NextHop)) { return $false }
    if ($dnsServers.Count -eq 0) {
        Write-Host 'DNS: ' -NoNewline
        Write-Host '❌ [ОШИБКА] DNS-серверы IPv4 не настроены.' -ForegroundColor Red
        return $false
    }
    for ($i = 0; $i -lt $dnsServers.Count; $i++) {
        if (-not (Invoke-QuickPing -Label ("DNS {0}" -f ($i + 1)) -Address $dnsServers[$i])) { return $false }
    }
    # The domain host comes from the cloud network profiles (cached by synchronization).
    if ($networkProfiles.DomainCheckHost -and (Test-ConfiguredLanAddress -Address $localAddress)) {
        return (Invoke-QuickPing -Label 'Домен' -Address $networkProfiles.DomainCheckHost)
    }
    return (Invoke-QuickPing -Label 'Интернет' -Address '1.1.1.1')
}

Write-ToolboxHeader -Section ' / быстрый ping'
Write-Host ''
$exitCode = 1
try {
    if (Invoke-QuickPingSequence) {
        Write-Host "`n✅ Все узлы ответили." -ForegroundColor Green
        $exitCode = 0
    }
    else {
        Write-Host "`n🛑 Проверка остановлена. Следующие узлы не проверялись." -ForegroundColor Yellow
    }
}
catch {
    Write-Host ("❌ [ОШИБКА] {0}" -f $_.Exception.Message) -ForegroundColor Red
}
if ($PauseAtEnd) {
    Write-Host ''
    Write-ToolboxHint '[Enter] — закрыть.'
    Read-Host | Out-Null
}
exit $exitCode
