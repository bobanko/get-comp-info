$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'console-ui.ps1')
. (Join-Path $PSScriptRoot 'sync-merge.ps1')
. (Join-Path $PSScriptRoot 'cloud-client.ps1')

try {
    $counts = Invoke-CloudSync -Root $PSScriptRoot
    if ($null -eq $counts) {
        # Cancelled with Esc while entering the key: return to the menu right away.
        exit 42
    }
    Show-CloudSyncSummary $counts
    if ($counts.Errors.Count -gt 0) { exit 1 }
}
catch {
    Write-Host ''
    Write-Host "Синхронизация не завершена: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host 'Уже обработанные записи сохранены. После устранения причины запустите синхронизацию ещё раз.' -ForegroundColor DarkGray
    exit 1
}
