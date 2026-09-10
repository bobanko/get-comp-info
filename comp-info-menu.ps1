$ErrorActionPreference = "Stop"

# The menu is kept in a separate PowerShell process so `exit` in a child script
# returns here instead of closing the launcher.
[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new()
[Console]::InputEncoding = [System.Text.UTF8Encoding]::new()
. (Join-Path $PSScriptRoot "console-ui.ps1")

$menuItems = @(
    [PSCustomObject]@{ Title = "🖥️ Собрать информацию о компьютере"; Script = "get-comp-info.ps1" }
    [PSCustomObject]@{ Title = "🌐 Настроить LAN-адаптер (IP-адрес, маска подсети, DNS)"; Script = "setup-lan-adapter.ps1" }
    [PSCustomObject]@{ Title = "🗃️ Просмотреть сохранённую информацию"; Script = "view-comp-info.ps1" }
    [PSCustomObject]@{ Title = "🔑 Серийные номера USB-накопителей"; Script = "get-usb-serial.ps1" }
    [PSCustomObject]@{ Title = "📤 Экспортировать все устройства в CSV"; Script = "export-comp-info.ps1" }
    [PSCustomObject]@{ Title = "🔍 Сканировать IP-адреса"; Script = "scan-ips.ps1"; IsDraft = $true; IsSeparatedBefore = $true }
    [PSCustomObject]@{ Title = "🔄️ Обновить проект из GitHub"; Script = "update-from-github.ps1"; IsDraft = $true }
    [PSCustomObject]@{ Title = "🚪 Выйти"; Script = $null; IsSeparatedBefore = $true }
)

function Show-Menu {
    param([int]$SelectedIndex)

    Clear-Host
    Write-ToolboxHeader -Section " / главное меню"
    Write-ToolboxHint "[▲▼] — выбрать, [↲ Enter] — открыть."
    Write-Host ""
    Write-Host ""


    for ($index = 0; $index -lt $menuItems.Count; $index++) {
        if ($menuItems[$index].IsSeparatedBefore) {
            Write-Host ""
        }

        $prefix = if ($index -eq $SelectedIndex) { ">" } else { " " }
        $color = if ($index -eq $SelectedIndex) { "Magenta" } else { "Gray" }
        Write-Host ("{0} {1}" -f $prefix, $menuItems[$index].Title) -NoNewline -ForegroundColor $color
        if ($menuItems[$index].IsDraft) {
            Write-Host " [в разработке]" -ForegroundColor Red
        }
        else {
            Write-Host ""
        }
    }
}

function Invoke-MenuScript {
    param([string]$ScriptName)

    $scriptPath = Join-Path $PSScriptRoot $ScriptName
    Clear-Host

    if (-not (Test-Path -LiteralPath $scriptPath -PathType Leaf)) {
        Write-Host "Не найден сценарий: $ScriptName" -ForegroundColor Red
        Write-Host ""
        Write-Host "Нажмите любую клавишу, чтобы вернуться в меню." -ForegroundColor DarkGray
        [Console]::ReadKey($true) | Out-Null
        return
    }

    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $scriptPath
    $exitCode = $LASTEXITCODE

    # Exit code 42 means that the inventory viewer was closed with Esc.
    # Return to the menu immediately instead of asking for another key press.
    if ($exitCode -eq 42) {
        return
    }

    Write-Host ""
    if ($exitCode -eq 0) {
        Write-Host "Сценарий завершён. Нажмите любую клавишу, чтобы вернуться в меню." -ForegroundColor DarkGray
    }
    else {
        Write-Host "Сценарий завершился с кодом ошибки: $exitCode" -ForegroundColor Red
        Write-Host "Нажмите любую клавишу, чтобы вернуться в меню." -ForegroundColor DarkGray
    }
    [Console]::ReadKey($true) | Out-Null
}

$selectedIndex = 0
$shouldExit = $false
while (-not $shouldExit) {
    Show-Menu -SelectedIndex $selectedIndex
    $key = [Console]::ReadKey($true)

    switch ($key.Key) {
        "UpArrow" {
            $selectedIndex = ($selectedIndex - 1 + $menuItems.Count) % $menuItems.Count
        }
        "DownArrow" {
            $selectedIndex = ($selectedIndex + 1) % $menuItems.Count
        }
        "Enter" {
            $item = $menuItems[$selectedIndex]
            if ($null -eq $item.Script) {
                $shouldExit = $true
                continue
            }
            Invoke-MenuScript -ScriptName $item.Script
        }
    }
}
