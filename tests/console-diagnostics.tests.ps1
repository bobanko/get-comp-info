$ErrorActionPreference = 'Stop'
$project = Split-Path -Parent $PSScriptRoot

function Assert-True([bool]$Value, [string]$Message) {
    if (-not $Value) { throw $Message }
}

$output = Join-Path ([IO.Path]::GetTempPath()) ('comp-info-diag-test-' + [guid]::NewGuid().ToString('N'))
try {
    # Runs the real script; the emoji question is answered by the parameter instead of a key press.
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $project 'console-diagnostics.ps1') -EmojiAnswer N -NoPause -OutputDirectory $output | Out-Null
    Assert-True ($LASTEXITCODE -eq 42) "The script must return to the menu with exit code 42, got $LASTEXITCODE"

    $report = Join-Path $output "console-diag-$env:COMPUTERNAME.txt"
    Assert-True (Test-Path -LiteralPath $report) 'The report must be saved'
    $text = [IO.File]::ReadAllText($report)
    foreach ($field in 'Версия Windows', 'Окно открыто в', 'Терминал по умолчанию', 'Экран из 240 вызовов Write-Host, мс', 'Тот же экран одной строкой (ANSI), мс') {
        Assert-True ($text.Contains($field)) "The report must contain '$field'"
    }
    Assert-True ($text -match 'Эмодзи:\s+НЕ видны') 'The emoji answer must be recorded'
    Assert-True ($text -match 'Экран из 240 вызовов Write-Host, мс:\s+\d+') 'The drawing time must be a number'
    [Console]::Out.WriteLine('Console diagnostics checks passed.')
}
finally {
    if (Test-Path -LiteralPath $output) { Remove-Item -LiteralPath $output -Recurse -Force }
}
