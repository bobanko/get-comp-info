[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot "console-ui.ps1")

function Invoke-Git {
    param(
        [Parameter(Mandatory)]
        [string[]]$Arguments
    )

    & git @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Git exited with code ${LASTEXITCODE}: git $($Arguments -join ' ')"
    }
}

try {
    Write-ToolboxHeader -Section 'GitHub update [draft]'
    Write-Host ''
    $projectRoot = Split-Path -Parent $PSCommandPath
    Set-Location -LiteralPath $projectRoot

    Invoke-Git @('rev-parse', '--is-inside-work-tree') | Out-Null

    $changes = @(& git status --porcelain --untracked-files=all)
    if ($LASTEXITCODE -ne 0) {
        throw 'Could not check local changes.'
    }

    $dataChanges = @(& git status --porcelain --untracked-files=all -- data)
    if ($LASTEXITCODE -ne 0) {
        throw 'Could not check changes in the data folder.'
    }

    if ($changes.Count -ne $dataChanges.Count) {
        Write-Host 'Sync cancelled: there are local changes outside the data folder.' -ForegroundColor Yellow
        Write-Host 'Commit, discard, or stash those changes before running this script.' -ForegroundColor Yellow
        Write-Host ''
        Write-Host $changes
        exit 2
    }

    $branch = (& git branch --show-current).Trim()
    if ([string]::IsNullOrWhiteSpace($branch)) {
        throw 'The current branch is not defined (detached HEAD).'
    }

    if ($dataChanges.Count -gt 0) {
        Write-Host 'Saving collected computer information locally...' -ForegroundColor Cyan
        Invoke-Git @('add', '--', 'data')

        $commitMessage = "Collected computer information $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
        Invoke-Git @('commit', '-m', $commitMessage)
    }

    Write-Host "Downloading updates for branch '$branch'..." -ForegroundColor Cyan
    Invoke-Git @('pull', '--rebase', 'origin', $branch)

    Write-Host 'Uploading collected computer information to GitHub...' -ForegroundColor Cyan
    Invoke-Git @('push', 'origin', $branch)

    Write-Host ''
    Write-Host 'Synchronization completed successfully.' -ForegroundColor Green
}
catch {
    Write-Host ''
    Write-Host "Synchronization was not completed: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host 'If Git asks for credentials, enter your GitHub username and use the PAT as the password.' -ForegroundColor Yellow
    exit 1
}
