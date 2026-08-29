$ErrorActionPreference = "Stop"

# The menu is kept in a separate PowerShell process so `exit` in a child script
# returns here instead of closing the launcher.
[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new()
[Console]::InputEncoding = [System.Text.UTF8Encoding]::new()

$menuItems = @(
    [PSCustomObject]@{ Title = "Собрать информацию о компьютере"; Script = "get-comp-info.ps1" }
    [PSCustomObject]@{ Title = "Настроить LAN-адаптер и изменить IP-адрес"; Script = "setup-lan-adapter.ps1" }
    [PSCustomObject]@{ Title = "Просмотреть сохранённую информацию"; Script = "view-comp-info.ps1" }
    [PSCustomObject]@{ Title = "Выйти"; Script = $null }
)

function Show-Menu {
    param([int]$SelectedIndex)

    Clear-Host
    Write-Host "Стрелки Вверх/Вниз — выбрать, Enter — открыть." -ForegroundColor DarkGray
    Write-Host "Главное меню" -ForegroundColor Cyan
    Write-Host "Выберите действие." -ForegroundColor DarkGray
    Write-Host ""

    for ($index = 0; $index -lt $menuItems.Count; $index++) {
        $prefix = if ($index -eq $SelectedIndex) { ">" } else { " " }
        $color = if ($index -eq $SelectedIndex) { "Cyan" } else { "Gray" }
        Write-Host ("{0} {1}" -f $prefix, $menuItems[$index].Title) -ForegroundColor $color
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
            if ($selectedIndex -gt 0) { $selectedIndex-- }
        }
        "DownArrow" {
            if ($selectedIndex -lt $menuItems.Count - 1) { $selectedIndex++ }
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
