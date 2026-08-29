try {
    [Console]::OutputEncoding = [System.Text.UTF8Encoding]::new()
    [Console]::InputEncoding = [System.Text.UTF8Encoding]::new()
}
catch {
    # Continue with the host defaults if console encoding cannot be configured.
}

function Write-ToolboxHeader {
    param(
        [Parameter(Mandatory)]
        [string]$Section
    )

    $colors = @("Red", "Yellow", "Green", "Cyan", "Blue", "Magenta")
    $brand = "CHAGEAR ⚙️ ТУЛБОКС"

    for ($index = 0; $index -lt $brand.Length; $index++) {
        Write-Host $brand[$index] -NoNewline -ForegroundColor $colors[$index % $colors.Count]
    }

    Write-Host (" " + $Section) -ForegroundColor Cyan
}
