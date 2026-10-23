$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'console-ui.ps1')
. (Join-Path $PSScriptRoot 'sync-merge.ps1')
. (Join-Path $PSScriptRoot 'cloud-client.ps1')

try {
    switch (Invoke-ProgramUpdate -Root $PSScriptRoot) {
        # The menu restarts itself on 43 to load the new version.
        'updated' { exit 43 }
        # Cancelled with Esc: return to the menu right away.
        'cancelled' { exit 42 }
        default { exit 0 }
    }
}
catch {
    Write-Host ''
    Write-Host "Обновление не выполнено: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}
