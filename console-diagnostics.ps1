# Диагностика консоли: почему не видны эмодзи и тормозит отрисовка программы.
# Отчёт сохраняется в diagnostics\console-diag-<ИМЯ_КОМПЬЮТЕРА>.txt рядом с программой.
[CmdletBinding()]
param(
    # Для тестов: ответ на вопрос про эмодзи ('Y' или 'N') без ожидания клавиши.
    [ValidateSet('', 'Y', 'N')]
    [string]$EmojiAnswer = '',
    [string]$OutputDirectory = (Join-Path $PSScriptRoot 'diagnostics'),
    # Для тестов: не ждать нажатия клавиши в конце.
    [switch]$NoPause
)

$ErrorActionPreference = 'Continue'
. (Join-Path $PSScriptRoot 'console-ui.ps1')

$report = [ordered]@{}
function Add-Line([string]$Name, $Value) { $script:report[$Name] = [string]$Value }
function Get-Safe([scriptblock]$Block) { try { & $Block } catch { "ошибка: $($_.Exception.Message)" } }

# The same icons as in the main menu.
$menuIcons = @(
    [pscustomobject]@{ Icon = [char]::ConvertFromUtf32(0x1F5A5) + [char]0xFE0F; Name = 'монитор' }
    [pscustomobject]@{ Icon = [char]::ConvertFromUtf32(0x2601) + [char]0xFE0F; Name = 'облако' }
    [pscustomobject]@{ Icon = [char]::ConvertFromUtf32(0x1F4BB); Name = 'ноутбук' }
    [pscustomobject]@{ Icon = [char]::ConvertFromUtf32(0x1F195); Name = 'NEW' }
    [pscustomobject]@{ Icon = [char]::ConvertFromUtf32(0x1F5C3) + [char]0xFE0F; Name = 'картотека' }
    [pscustomobject]@{ Icon = [char]::ConvertFromUtf32(0x1F4E4); Name = 'лоток' }
)

Clear-Host
Write-ToolboxHeader -Section 'Диагностика консоли'
Write-Host 'Сбор сведений о системе...' -ForegroundColor DarkGray

# ---------- Windows и железо ----------
$nt = Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion' -ErrorAction SilentlyContinue
$os = Get-CimInstance Win32_OperatingSystem -ErrorAction SilentlyContinue
$cs = Get-CimInstance Win32_ComputerSystem -ErrorAction SilentlyContinue
Add-Line 'Компьютер' $env:COMPUTERNAME
Add-Line 'Версия программы' $script:toolboxVersion
Add-Line 'Windows' $os.Caption
Add-Line 'Версия Windows' "$($nt.DisplayVersion) (сборка $($nt.CurrentBuild).$($nt.UBR))"
Add-Line 'Производитель и модель' "$($cs.Manufacturer) $($cs.Model)"
Add-Line 'Процессор' (Get-Safe { (Get-CimInstance Win32_Processor | Select-Object -First 1).Name.Trim() })
Add-Line 'Память, ГБ' ([Math]::Round($cs.TotalPhysicalMemory / 1GB, 1))
Add-Line 'Видеокарта' (Get-Safe { (Get-CimInstance Win32_VideoController | ForEach-Object { "$($_.Name) [драйвер $($_.DriverVersion)]" }) -join '; ' })
Add-Line 'PowerShell' $PSVersionTable.PSVersion

# ---------- В каком терминале открыто окно ----------
$parent = Get-Safe {
    $parentId = (Get-CimInstance Win32_Process -Filter "ProcessId=$PID").ParentProcessId
    (Get-Process -Id $parentId -ErrorAction Stop).ProcessName
}
Add-Line 'Окно открыто в' $(if ($env:WT_SESSION) { 'Windows Terminal' } else { "старой консоли (conhost); родительский процесс: $parent" })

$delegation = (Get-ItemProperty 'HKCU:\Console\%%Startup' -ErrorAction SilentlyContinue).DelegationTerminal
$terminalNames = @{
    '{B23D10C0-E52E-411E-9D5B-C09FDF709C7D}' = 'Узел консоли Windows (старая консоль)'
    '{E12CFF52-A866-4C77-9A90-F570A7AA2C6B}' = 'Терминал Windows'
    '{86633F1F-6454-40EC-89CE-DA4EBA977EE2}' = 'Терминал Windows (предварительная версия)'
    '{00000000-0000-0000-0000-000000000000}' = 'Пусть Windows решает'
}
Add-Line 'Терминал по умолчанию' $(if ($delegation) { if ($terminalNames[$delegation]) { $terminalNames[$delegation] } else { $delegation } } else { 'не задан (Пусть Windows решает)' })
Add-Line 'Windows Terminal установлен' (Get-Safe {
    $package = Get-AppxPackage -Name 'Microsoft.WindowsTerminal*' -ErrorAction Stop | Select-Object -First 1
    if ($package) { "да, версия $($package.Version)" } else { 'нет' }
})
Add-Line 'wt.exe доступен' $(if (Get-Command wt.exe -ErrorAction SilentlyContinue) { 'да' } else { 'нет' })

$console = Get-ItemProperty 'HKCU:\Console' -ErrorAction SilentlyContinue
Add-Line 'Прежняя версия консоли (legacy)' $(if ($null -ne $console.ForceV2 -and $console.ForceV2 -eq 0) { 'ВКЛЮЧЕНА' } else { 'выключена' })
Add-Line 'Шрифт консоли' $(if ($console.FaceName) { $console.FaceName } else { 'по умолчанию' })
Add-Line 'Поддержка ANSI-цветов' (Get-Safe { $Host.UI.SupportsVirtualTerminal })
Add-Line 'Размер окна' (Get-Safe { "$([Console]::WindowWidth) x $([Console]::WindowHeight)" })

# ---------- Скорость отрисовки ----------
# Как рисует программа: много отдельных цветных фрагментов Write-Host.
Clear-Host
$colors = 'Gray', 'Cyan', 'Green', 'Yellow', 'DarkGray'
$timer = [Diagnostics.Stopwatch]::StartNew()
for ($row = 0; $row -lt 30; $row++) {
    for ($cell = 0; $cell -lt 8; $cell++) { Write-Host ('ячейка {0,2}:{1}  ' -f $row, $cell) -NoNewline -ForegroundColor $colors[$cell % 5] }
    Write-Host ''
}
$timer.Stop()
$writeHostMs = $timer.ElapsedMilliseconds

# Тот же экран одной строкой с ANSI-цветами.
$escape = [char]27
$ansi = @{ Gray = '37'; Cyan = '96'; Green = '92'; Yellow = '93'; DarkGray = '90' }
$builder = New-Object Text.StringBuilder
[void]$builder.Append("$escape[H$escape[2J")
for ($row = 0; $row -lt 30; $row++) {
    for ($cell = 0; $cell -lt 8; $cell++) { [void]$builder.Append("$escape[$($ansi[$colors[$cell % 5]])m" + ('ячейка {0,2}:{1}  ' -f $row, $cell)) }
    [void]$builder.Append("$escape[0m`r`n")
}
$timer = [Diagnostics.Stopwatch]::StartNew()
[Console]::Write($builder.ToString())
$timer.Stop()
$singleWriteMs = $timer.ElapsedMilliseconds

$timer = [Diagnostics.Stopwatch]::StartNew()
for ($i = 0; $i -lt 10; $i++) { Clear-Host }
$timer.Stop()
Add-Line 'Экран из 240 вызовов Write-Host, мс' $writeHostMs
Add-Line 'Тот же экран одной строкой (ANSI), мс' $singleWriteMs
Add-Line '10 очисток экрана Clear-Host, мс' $timer.ElapsedMilliseconds

# ---------- Эмодзи: проверка глазами ----------
if ($EmojiAnswer -eq '') {
    Clear-Host
    Write-ToolboxHeader -Section 'Диагностика консоли'
    Write-ToolboxHint '[Y] — значки видны, [N] — вместо них квадратики или знаки вопроса.'
    Write-Host 'Посмотрите на значки из меню программы:'
    Write-Host ''
    Write-Host ('   ' + (($menuIcons | ForEach-Object { $_.Icon }) -join '   '))
    Write-Host ''
    Write-Host ('Должны быть видны цветные значки: ' + (($menuIcons | ForEach-Object { $_.Name }) -join ', ') + '.') -ForegroundColor DarkGray
    while ($EmojiAnswer -eq '') {
        switch ([Console]::ReadKey($true).Key) {
            'Y' { $EmojiAnswer = 'Y' }
            'N' { $EmojiAnswer = 'N' }
        }
    }
}
Add-Line 'Эмодзи' $(if ($EmojiAnswer -eq 'Y') { 'видны' } else { 'НЕ видны' })

# ---------- Результат ----------
$lines = @("Диагностика консоли COMP-INFO, $(Get-Date -Format 'yyyy-MM-dd HH:mm')", '') + @($report.Keys | ForEach-Object { '{0,-40} {1}' -f ($_ + ':'), $report[$_] })
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$file = Join-Path $OutputDirectory "console-diag-$env:COMPUTERNAME.txt"
[IO.File]::WriteAllLines($file, $lines, [Text.UTF8Encoding]::new($true))

Clear-Host
Write-ToolboxHeader -Section 'Диагностика консоли'
$lines | Select-Object -Skip 2 | ForEach-Object { Write-Host $_ }
Write-Host ''
Write-Host "Отчёт сохранён: $file" -ForegroundColor Green
if (-not $NoPause) {
    Write-ToolboxHint 'Нажмите любую клавишу, чтобы вернуться.'
    [void][Console]::ReadKey($true)
}
# Return straight to the menu without its extra "press any key".
exit 42
