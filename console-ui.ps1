try {
    [Console]::OutputEncoding = [System.Text.UTF8Encoding]::new()
    [Console]::InputEncoding = [System.Text.UTF8Encoding]::new()
}
catch {
    # Continue with the host defaults if console encoding cannot be configured.
}

# Release version. Update with tools/update-version.ps1 before distributing a release.
$script:toolboxVersion = '2026.09.27.1'

function Write-ToolboxHeader {
    param(
        [Parameter(Mandatory)]
        [string]$Section,
        [string]$WizardStep
    )

    $colors = @("Red", "Yellow", "Green", "Cyan", "Blue", "Magenta")
    $brand = "CHAGEAR ⚙️ ТУЛБОКС"

    for ($index = 0; $index -lt $brand.Length; $index++) {
        Write-Host $brand[$index] -NoNewline -ForegroundColor $colors[$index % $colors.Count]
    }

    Write-Host ("  v" + $script:toolboxVersion) -ForegroundColor DarkGray
    Write-Host $Section -NoNewline -ForegroundColor Cyan
    if (-not [string]::IsNullOrWhiteSpace($WizardStep)) {
        Write-Host (" | " + $WizardStep) -ForegroundColor Cyan
    }
    else {
        Write-Host ""
    }
}

function Write-ToolboxHint {
    param(
        [Parameter(Mandatory)]
        [string]$Text
    )

    foreach ($part in [regex]::Split($Text, '(\[[^\]]+\])')) {
        if ($part.Length -eq 0) { continue }
        $color = if ($part -match '^\[[^\]]+\]$') { 'Yellow' } else { 'DarkGray' }
        Write-Host $part -NoNewline -ForegroundColor $color
    }
    Write-Host ''
}
