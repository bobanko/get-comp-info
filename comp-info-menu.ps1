$ErrorActionPreference = "Stop"

# The menu is kept in a separate PowerShell process so `exit` in a child script
# returns here instead of closing the launcher.
[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new()
[Console]::InputEncoding = [System.Text.UTF8Encoding]::new()
. (Join-Path $PSScriptRoot "console-ui.ps1")

$menuItems = @(
    [PSCustomObject]@{ Title = "🖥️ Собрать информацию о компьютере"; Script = "get-comp-info.ps1" }
    [PSCustomObject]@{ Title = "🌐 Настроить LAN-адаптер (IP-адрес, маска подсети, DNS)"; Script = "setup-lan-adapter.ps1" }
    [PSCustomObject]@{ Title = "📡 Быстрый ping (свой IP, шлюз, DNS, домен/интернет)"; Script = "quick-ping.ps1" }
    [PSCustomObject]@{ Title = "🗃️ Просмотреть сохранённую информацию"; Script = "view-comp-info.ps1" }
    [PSCustomObject]@{ Title = "🔑 Серийные номера USB-накопителей"; Script = "get-usb-serial.ps1" }
    [PSCustomObject]@{ Title = "📤 Экспортировать все устройства в CSV"; Script = "export-comp-info.ps1" }
    [PSCustomObject]@{ Title = "☁️ Синхронизировать с облаком"; Script = "sync-cloud.ps1" }
    [PSCustomObject]@{ Title = "⬆️ Обновить программу"; Script = "update-program.ps1" }
    [PSCustomObject]@{ Title = "🚪 Выйти"; Script = $null; IsSeparatedBefore = $true }
)

foreach ($item in $menuItems) {
    if ($item.IsDraft) {
        $item | Add-Member -NotePropertyName IsDisabled -NotePropertyValue $true -Force
    }
}

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
        $color = if ($menuItems[$index].IsDisabled) { "DarkGray" } elseif ($index -eq $SelectedIndex) { "Magenta" } else { "Gray" }
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
    # Exit code 43: the program was updated, this menu still runs the old code.
    if ($exitCode -eq 43) {
        Write-Host "Нажмите любую клавишу, чтобы перезапустить меню с новой версией." -ForegroundColor DarkGray
        [Console]::ReadKey($true) | Out-Null
        $script:restartMenu = $true
        return
    }
    if ($exitCode -eq 0) {
        Write-Host "Сценарий завершён. Нажмите любую клавишу, чтобы вернуться в меню." -ForegroundColor DarkGray
    }
    else {
        Write-Host "Сценарий завершился с кодом ошибки: $exitCode" -ForegroundColor Red
        Write-Host "Нажмите любую клавишу, чтобы вернуться в меню." -ForegroundColor DarkGray
    }
    [Console]::ReadKey($true) | Out-Null
    return
}

$selectedIndex = 0
$script:restartMenu = $false
$shouldExit = $false
while (-not $shouldExit) {
    if ($menuItems[$selectedIndex].IsDisabled) { $selectedIndex = 0 }
    Show-Menu -SelectedIndex $selectedIndex
    $key = [Console]::ReadKey($true)

    switch ($key.Key) {
        "UpArrow" {
            do { $selectedIndex = ($selectedIndex - 1 + $menuItems.Count) % $menuItems.Count } while ($menuItems[$selectedIndex].IsDisabled)
        }
        "DownArrow" {
            do { $selectedIndex = ($selectedIndex + 1) % $menuItems.Count } while ($menuItems[$selectedIndex].IsDisabled)
        }
        "Enter" {
            $item = $menuItems[$selectedIndex]
            if ($item.IsDisabled) { continue }
            if ($null -eq $item.Script) {
                $shouldExit = $true
                continue
            }
            Invoke-MenuScript -ScriptName $item.Script
            if ($script:restartMenu) {
                # Run the freshly installed menu in this window, then close this old one.
                & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot "comp-info-menu.ps1")
                exit $LASTEXITCODE
            }
        }
    }
}
