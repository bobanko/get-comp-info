# Releases a new program version in one go: checks, tests, version bump, commit, tag, push,
# then waits for GitHub Actions to publish it to the cloud. See PROGRAM_UPDATE.md.
[CmdletBinding()]
param(
    # Skip local tests (GitHub Actions runs them anyway).
    [switch]$SkipTests,
    # Do not ask for confirmation before pushing.
    [switch]$Yes,
    # Do not wait for the GitHub Actions run.
    [switch]$NoWait
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
. (Join-Path $root 'console-ui.ps1')

# Runs git in the repository; throws on a non-zero exit code. Output goes to the pipeline.
function Invoke-Git {
    param([string[]]$Arguments)
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try { $output = & git -C $root @Arguments 2>&1 } finally { $ErrorActionPreference = $previous }
    if ($LASTEXITCODE -ne 0) { throw "git $($Arguments -join ' '):`n$(($output | ForEach-Object { "$_" }) -join "`n")" }
    return $output | ForEach-Object { "$_" }
}

function Invoke-PowerShellFile {
    param([string]$Path, [string[]]$Arguments = @())
    # Quiet on success; on failure show the end of the output.
    $output = & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $Path @Arguments 2>&1
    $code = $LASTEXITCODE
    if ($code -ne 0) { $output | Select-Object -Last 20 | ForEach-Object { Write-Host "  $_" -ForegroundColor DarkGray } }
    return $code
}

$versionFile = Join-Path $root 'console-ui.ps1'
$bumped = $false

try {
    Write-ToolboxHeader -Section 'Выпуск версии программы'

    # 1. Repository state: main, nothing uncommitted, in step with GitHub.
    $branch = (Invoke-Git @('rev-parse', '--abbrev-ref', 'HEAD')) -join ''
    if ($branch -ne 'main') { throw "Выпуск делается из ветки main, сейчас: $branch." }
    $changes = @(Invoke-Git @('status', '--porcelain', '--untracked-files=no'))
    if ($changes.Count -gt 0) { throw "Есть незакоммиченные изменения. Закоммитьте их перед выпуском:`n$($changes -join "`n")" }

    Write-Host 'Проверка GitHub...' -ForegroundColor Cyan
    Invoke-Git @('fetch', '--quiet', '--tags', 'origin', 'main') | Out-Null
    $behind = [int]((Invoke-Git @('rev-list', '--count', 'HEAD..origin/main')) -join '')
    if ($behind -gt 0) { throw "В GitHub есть $behind новых коммитов, которых нет здесь. Сначала выполните git pull." }

    $previousTag = (Invoke-Git @('describe', '--tags', '--abbrev=0', '--match', 'v*', 'HEAD')) -join ''
    $newCommits = @(Invoke-Git @('log', '--oneline', '--no-merges', "$previousTag..HEAD"))
    if ($newCommits.Count -eq 0) { throw "С версии $previousTag нет новых коммитов: выпускать нечего." }

    # 2. Tests, before anything changes.
    if (-not $SkipTests) {
        Write-Host 'Тесты...' -ForegroundColor Cyan
        foreach ($test in Get-ChildItem (Join-Path $root 'tests') -Filter '*.tests.ps1') {
            $output = & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $test.FullName 2>&1
            if ($LASTEXITCODE -ne 0) {
                $output | Select-Object -Last 20 | ForEach-Object { Write-Host "  $_" -ForegroundColor DarkGray }
                throw "Не прошёл тест $($test.Name)."
            }
            Write-Host "  $($test.Name)" -ForegroundColor DarkGray
        }
    }

    # 3. New version number.
    $bumpOutput = Invoke-PowerShellFile (Join-Path $PSScriptRoot 'update-version.ps1')
    $version = (Select-String -Path $versionFile -Pattern "^\`$script:toolboxVersion = '([^']+)'" | Select-Object -First 1).Matches[0].Groups[1].Value
    if ($bumpOutput -ne 0 -or -not $version) { throw 'Не удалось поднять версию.' }
    $bumped = $true
    $tag = "v$version"
    if (Invoke-Git @('tag', '--list', $tag)) { throw "Тег $tag уже есть." }
    if (Invoke-Git @('ls-remote', '--tags', 'origin', "refs/tags/$tag")) { throw "Тег $tag уже есть в GitHub." }

    # 4. Build the archive locally: the same check Actions does, before anything is pushed.
    Write-Host "Сборка версии $version..." -ForegroundColor Cyan
    if ((Invoke-PowerShellFile (Join-Path $PSScriptRoot 'build-release.ps1') @('-Version', $version)) -ne 0) {
        throw 'Сборка архива не удалась.'
    }

    Write-Host ''
    Write-Host "Будет выпущена версия $version. Изменения с $($previousTag):" -ForegroundColor Cyan
    $newCommits | ForEach-Object { Write-Host "  $_" }
    Write-Host ''
    if (-not $Yes) {
        $answer = Read-Host 'Выпустить? (y/n)'
        if ($answer -notmatch '^\s*(д|да|y|yes)\s*$') { throw 'Отменено, ничего не отправлено.' }
    }

    # 5. Commit, tag, push in one atomic push: either both arrive or neither.
    Invoke-Git @('add', '--', 'console-ui.ps1') | Out-Null
    Invoke-Git @('commit', '--quiet', '-m', "chore: version $version") | Out-Null
    $bumped = $false
    Invoke-Git @('tag', '-a', $tag, '-m', "Version $version") | Out-Null
    Write-Host 'Отправка в GitHub...' -ForegroundColor Cyan
    try { Invoke-Git @('push', '--atomic', '--quiet', 'origin', 'main', $tag) | Out-Null }
    catch {
        throw "Не удалось отправить в GitHub. Коммит и тег $tag остались локально; повторите: git push --atomic origin main $tag`n$($_.Exception.Message)"
    }
    Write-Host "Отправлено: main и $tag." -ForegroundColor Green

    # 6. Wait for GitHub Actions to publish the version.
    if ($NoWait) { exit 0 }
    $repo = ((Invoke-Git @('remote', 'get-url', 'origin')) -join '') -replace '^.*github\.com[:/]', '' -replace '\.git$', ''
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    $runsUrl = "https://api.github.com/repos/$repo/actions/runs?event=push&branch=$tag&per_page=1"
    Write-Host 'Ждём публикацию в GitHub Actions (обычно 2-4 минуты)...' -ForegroundColor Cyan
    $deadline = (Get-Date).AddMinutes(15)
    $run = $null
    $lastStatus = ''
    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Seconds 20
        try { $run = @((Invoke-RestMethod -Uri $runsUrl -Headers @{ Accept = 'application/vnd.github+json' }).workflow_runs)[0] }
        catch { Write-Host "  GitHub не ответил: $($_.Exception.Message)" -ForegroundColor DarkGray; continue }
        if ($null -eq $run) { continue }
        if ($run.status -ne $lastStatus) { Write-Host "  $($run.status)" -ForegroundColor DarkGray; $lastStatus = $run.status }
        if ($run.status -eq 'completed') { break }
    }

    Write-Host ''
    if ($null -eq $run -or $run.status -ne 'completed') {
        Write-Host "Не дождались результата. Проверьте: https://github.com/$repo/actions" -ForegroundColor Yellow
        exit 2
    }
    if ($run.conclusion -ne 'success') {
        Write-Host "Публикация не удалась ($($run.conclusion)): $($run.html_url)" -ForegroundColor Red
        Write-Host 'Исправьте ошибку и выпустите следующую версию: этот номер уже занят тегом.' -ForegroundColor DarkGray
        exit 1
    }
    Write-Host "Версия $version опубликована и стала текущей." -ForegroundColor Green
    Write-Host 'Флешки получат её через «Обновить программу».' -ForegroundColor DarkGray
}
catch {
    if ($bumped) {
        # The version was raised but not committed: put console-ui.ps1 back.
        & git -C $root checkout -- console-ui.ps1 2>$null
    }
    Write-Host ''
    Write-Host "Выпуск не выполнен: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}
