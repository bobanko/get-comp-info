$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.UTF8Encoding]::new()
[Console]::InputEncoding = [Text.UTF8Encoding]::new()
. (Join-Path $PSScriptRoot 'computer-management-ui.ps1')
try {
    if (Start-ComputerManagement -ScriptPath $PSCommandPath) { Invoke-ComputerManagementWizard -Mode Rename }
}
catch {
    Write-Host $_.Exception.Message -ForegroundColor Red
    Write-Host 'Если команда Windows уже запускалась, результат может быть частичным. Перезагрузите компьютер и откройте мастер для проверки.' -ForegroundColor Yellow
    Write-Host 'Нажмите любую клавишу для возврата в меню.'
    [void][Console]::ReadKey($true)
    exit 1
}
