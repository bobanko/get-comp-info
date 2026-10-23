$ErrorActionPreference = 'Stop'
$project = Split-Path -Parent $PSScriptRoot

function Assert-True([bool]$Value, [string]$Message) {
    if (-not $Value) { throw $Message }
}

# A copy of the program folder, so the default report folder can be checked like a menu launch.
$root = Join-Path ([IO.Path]::GetTempPath()) ('comp-info-diag-test-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $root | Out-Null
try {
    foreach ($name in 'console-diagnostics.ps1', 'console-ui.ps1') { Copy-Item (Join-Path $project $name) $root }

    # No -OutputDirectory, as from the menu; the emoji question is answered by the parameter.
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'console-diagnostics.ps1') -EmojiAnswer N -NoPause | Out-Null
    Assert-True ($LASTEXITCODE -eq 42) "The script must return to the menu with exit code 42, got $LASTEXITCODE"

    $report = Join-Path $root "diagnostics\console-diag-$env:COMPUTERNAME.txt"
    Assert-True (Test-Path -LiteralPath $report) 'The report must be saved in diagnostics next to the script'
    $text = [IO.File]::ReadAllText($report)
    foreach ($field in 'Версия программы', 'Версия Windows', 'Окно открыто в', 'Терминал по умолчанию', 'Экран из 240 вызовов Write-Host, мс', 'Тот же экран одной строкой (ANSI), мс') {
        Assert-True ($text.Contains($field)) "The report must contain '$field'"
    }
    Assert-True ($text -match 'Эмодзи:\s+НЕ видны') 'The emoji answer must be recorded'
    Assert-True ($text -match 'Экран из 240 вызовов Write-Host, мс:\s+\d+') 'The drawing time must be a number'

    # An explicit folder is used as given.
    $custom = Join-Path $root 'custom'
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'console-diagnostics.ps1') -EmojiAnswer Y -NoPause -OutputDirectory $custom | Out-Null
    Assert-True ([IO.File]::ReadAllText((Join-Path $custom "console-diag-$env:COMPUTERNAME.txt")) -match 'Эмодзи:\s+видны') 'An explicit output folder must be used'
    [Console]::Out.WriteLine('Console diagnostics checks passed.')
}
finally {
    Remove-Item -LiteralPath $root -Recurse -Force
}
