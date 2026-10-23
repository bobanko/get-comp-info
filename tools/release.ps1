# Publishes the current program version from this computer with an admin's personal key.
# Same checks as .github/workflows/release.yml, without GitHub Actions. See PROGRAM_UPDATE.md.
[CmdletBinding()]
param([switch]$SkipTests)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
. (Join-Path $root 'console-ui.ps1')
. (Join-Path $root 'sync-merge.ps1')
. (Join-Path $root 'cloud-client.ps1')

try {
    Write-ToolboxHeader -Section 'Выпуск версии программы'
    $version = $script:toolboxVersion

    # The archive is built from the working tree, so it must match a commit.
    $changes = @(& git -C $root status --porcelain --untracked-files=no)
    if ($changes.Count -gt 0) { throw "Есть незакоммиченные изменения. Закоммитьте их перед выпуском:`n$($changes -join "`n")" }

    if (-not $SkipTests) {
        Write-Host 'Тесты...' -ForegroundColor Cyan
        foreach ($test in Get-ChildItem (Join-Path $root 'tests') -Filter '*.tests.ps1') {
            & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $test.FullName | Out-Null
            if ($LASTEXITCODE -ne 0) { throw "Не прошёл тест $($test.Name). Запустите его отдельно, чтобы увидеть ошибку." }
            Write-Host "  $($test.Name)" -ForegroundColor DarkGray
        }
    }

    Write-Host "Сборка версии $version..." -ForegroundColor Cyan
    $package = & (Join-Path $PSScriptRoot 'build-release.ps1') -Version $version | Select-Object -Last 1

    Enable-CloudTls
    $config = Get-CloudConfig $root
    $connection = Connect-Cloud $root $config.ApiUrl
    if ($null -eq $connection) { Write-Host 'Выпуск отменён.' -ForegroundColor Yellow; exit 42 }
    if ($connection.User.role -ne 'admin') { throw "Публиковать версии может только администратор, роль ключа: $($connection.User.role)." }

    Write-Host "Публикация в облако от имени $($connection.User.email)..." -ForegroundColor Cyan
    $request = New-CloudWebRequest 'POST' "$($config.ApiUrl)/program" $connection.Key
    $request.ContentType = 'application/zip'
    $request.Accept = 'application/json'
    $response = Get-CloudWebResponse $request ([IO.File]::ReadAllBytes($package.Path))
    try {
        $status = [int]$response.StatusCode
        $body = Read-CloudResponseBody $response
    }
    finally { $response.Dispose() }
    if ($status -ne 201) { throw (Get-CloudErrorMessage ([pscustomobject]@{ Status = $status; Body = $body })) }

    Write-Host ''
    Write-Host "Версия $($body.version) опубликована и стала текущей." -ForegroundColor Green
    Write-Host 'Флешки получат её через «Обновить программу».' -ForegroundColor DarkGray
    $tag = "v$version"
    if (-not (& git -C $root tag --list $tag)) {
        Write-Host "Не забудьте тег: git tag $tag; git push origin main $tag" -ForegroundColor DarkGray
    }
}
catch {
    Write-Host ''
    Write-Host "Выпуск не выполнен: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}
