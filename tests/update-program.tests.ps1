$ErrorActionPreference = 'Stop'
$project = Split-Path -Parent $PSScriptRoot
. (Join-Path $project 'console-ui.ps1')
. (Join-Path $project 'sync-merge.ps1')
. (Join-Path $project 'cloud-client.ps1')
. (Join-Path $project 'tools\release-package.ps1')
Add-Type -AssemblyName System.IO.Compression

function Assert-True([bool]$Value, [string]$Message) {
    if (-not $Value) { throw $Message }
}

function Assert-Throws([scriptblock]$Action, [string]$Pattern, [string]$Message) {
    try { & $Action } catch {
        if ($_.Exception.Message -notlike $Pattern) { throw "$Message (unexpected error: $($_.Exception.Message))" }
        return
    }
    throw "$Message (no error)"
}

# ---------- Test doubles ----------

function Write-Host { }
function Clear-Host { }
function Select-SyncAction {
    param($Title, $Lines, $Items, [switch]$AllowBack, $Section)
    $script:lastPrompt = $Title
    if ($script:choices.Count -eq 0) { throw "Unexpected prompt: $Title" }
    return $script:choices.Dequeue()
}
function Set-Choices([int[]]$Values) {
    $script:choices = New-Object 'System.Collections.Generic.Queue[int]'
    foreach ($value in $Values) { $script:choices.Enqueue($value) }
}

$script:api = 'https://fake.test/api/client'
$script:release = $null
$script:packagePath = $null
$script:corruptDownload = $false
$script:downloads = 0

function Invoke-CloudRequest {
    param([string]$Method, [string]$Url, [string]$Key, $Body)
    if ($Url -eq "$script:api/me") { return [pscustomobject]@{ Status = 200; Body = [pscustomobject]@{ email = 'h@example.com'; name = 'Tester'; role = 'harvester' } } }
    if ($Url -eq "$script:api/program/latest") {
        if ($null -eq $script:release) { return [pscustomobject]@{ Status = 404; Body = $null } }
        return [pscustomobject]@{ Status = 200; Body = $script:release }
    }
    throw "Unexpected request: $Method $Url"
}

function Save-CloudDownload {
    param([string]$Url, [string]$Key, [string]$Path)
    $script:downloads++
    Assert-True ($Url -eq "$script:api/program/$($script:release.version)/package") "Unexpected download URL $Url"
    Copy-Item -LiteralPath $script:packagePath -Destination $Path -Force
    if ($script:corruptDownload) { [IO.File]::AppendAllText($Path, 'x') }
}

# ---------- Helpers ----------

function Write-Text([string]$Path, [string]$Text) {
    New-Item -ItemType Directory -Path (Split-Path -Parent $Path) -Force | Out-Null
    [IO.File]::WriteAllText($Path, $Text)
}

function Read-Text([string]$Path) { return [IO.File]::ReadAllText($Path) }

function New-ProgramCopy([string]$Name) {
    $root = Join-Path $testRoot $Name
    Write-Text (Join-Path $root 'cloud-config.json') "{ `"apiUrl`": `"$script:api`" }"
    Write-Text (Join-Path $root 'cloud-key.txt') 'good-key'
    Write-Text (Join-Path $root 'a.ps1') 'old a'
    Write-Text (Join-Path $root 'old.ps1') 'obsolete'
    Write-Text (Join-Path $root 'run.BAT') 'old bat'
    Write-Text (Join-Path $root 'notes.txt') 'not a program file'
    Write-Text (Join-Path $root 'data\PC-1.json') '{"hostname":"PC-1"}'
    Write-Text (Join-Path $root 'cloud-state.json') '{}'
    Write-Text (Join-Path $root 'sub\inner.ps1') 'subfolders are never touched'
    return $root
}

function Set-Release([string]$Version, [hashtable]$Files) {
    $source = Join-Path $testRoot "source-$Version-$([guid]::NewGuid().ToString('N'))"
    foreach ($name in $Files.Keys) { Write-Text (Join-Path $source $name) $Files[$name] }
    $package = New-ReleasePackage -SourceRoot $source -Files @($Files.Keys) -Version $Version -Commit 'abc123' -Notes @('feat: new menu', 'fix: sync') -OutputPath "$source.zip"
    Set-ReleaseArchive $Version $package.Path
}

function Set-ReleaseArchive([string]$Version, [string]$Path) {
    $script:packagePath = $Path
    $script:release = [pscustomobject]@{
        version = $Version; sha256 = (Get-FileSha256 $Path); size = (Get-Item $Path).Length
        publishedAt = '2026-11-01T10:00:00Z'; commit = 'abc123'; notes = @('feat: new menu', 'fix: sync')
    }
}

# An archive built by hand, to simulate a tampered release.
function New-RawZip([string]$Path, [hashtable]$Entries) {
    $stream = [IO.File]::Open($Path, [IO.FileMode]::CreateNew)
    $archive = [IO.Compression.ZipArchive]::new($stream, [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($name in $Entries.Keys) {
            $writer = [IO.StreamWriter]::new($archive.CreateEntry($name).Open())
            try { $writer.Write($Entries[$name]) } finally { $writer.Dispose() }
        }
    }
    finally { $archive.Dispose(); $stream.Dispose() }
}

function Get-Snapshot([string]$Root) {
    return (Get-ChildItem -LiteralPath $Root -Recurse -File | Where-Object { $_.FullName -notlike '*\.update\*' } |
        Sort-Object FullName | ForEach-Object { $_.FullName.Substring($Root.Length) + '=' + (Get-FileSha256 $_.FullName) }) -join "`n"
}

$newFiles = @{ 'a.ps1' = 'new a'; 'b.ps1' = 'new b'; 'run.BAT' = 'new bat'; 'cloud-config.json' = "{ `"apiUrl`": `"$script:api`" }" }

$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('comp-info-update-test-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testRoot | Out-Null
try {
    # ---------- Versions ----------
    Assert-True (Test-NewerProgramVersion '2026.10.24.2' '2026.10.24.1') 'Higher release number is newer'
    Assert-True (Test-NewerProgramVersion '2026.11.01.1' '2026.10.30.9') 'Later date is newer'
    Assert-True (-not (Test-NewerProgramVersion '2026.10.24.10' '2026.10.24.10')) 'Same version is not newer'
    Assert-True (Test-NewerProgramVersion '2026.10.24.10' '2026.10.24.9') 'Release numbers compare as numbers'
    Assert-True (-not (Test-NewerProgramVersion 'garbage' '2026.10.24.1')) 'Invalid versions are never newer'
    Assert-True ((Test-ProgramFileName 'a.ps1') -and (Test-ProgramFileName 'RUN.BAT') -and (Test-ProgramFileName 'cloud-config.json')) 'Program files'
    Assert-True (-not (Test-ProgramFileName 'notes.txt') -and -not (Test-ProgramFileName 'cloud-key.txt') -and -not (Test-ProgramFileName 'sub\a.ps1')) 'Not program files'

    # ---------- A normal update ----------
    Set-Release '2026.11.01.1' $newFiles
    $copy = New-ProgramCopy 'normal'
    Set-Choices @(0)
    $status = Invoke-ProgramUpdate -Root $copy -CurrentVersion '2026.10.24.1'
    Assert-True ($status -eq 'updated') "Expected an update, got $status"
    Assert-True ($script:lastPrompt -eq 'Доступна новая версия программы') 'An update must be offered as a new version'
    Assert-True ((Read-Text "$copy\a.ps1") -eq 'new a' -and (Read-Text "$copy\b.ps1") -eq 'new b' -and (Read-Text "$copy\run.BAT") -eq 'new bat') 'New files must be installed'
    Assert-True (-not (Test-Path "$copy\old.ps1")) 'Obsolete program files must be removed'
    Assert-True ((Read-Text "$copy\notes.txt") -eq 'not a program file') 'Other files must stay'
    Assert-True ((Read-Text "$copy\cloud-key.txt") -eq 'good-key' -and (Test-Path "$copy\cloud-state.json")) 'Key and sync state must stay'
    Assert-True ((Read-Text "$copy\data\PC-1.json") -eq '{"hostname":"PC-1"}' -and (Test-Path "$copy\sub\inner.ps1")) 'Data and subfolders must stay'
    $backup = @(Get-ChildItem "$copy\.update" -Directory -Filter 'backup-2026.10.24.1-*')
    Assert-True ($backup.Count -eq 1) 'A backup of the previous version must be kept'
    Assert-True ((Read-Text "$($backup[0].FullName)\old.ps1") -eq 'obsolete' -and (Read-Text "$($backup[0].FullName)\a.ps1") -eq 'old a') 'Backup must hold the old files'
    Assert-True (@(Get-ChildItem "$copy\.update" -Directory -Filter 'extract-*').Count -eq 0) 'Temporary extraction must be removed'
    Assert-True (@(Get-ChildItem $copy -Filter '*.tmp').Count -eq 0) 'No temporary files may be left behind'

    # ---------- Nothing to do ----------
    $downloads = $script:downloads
    Assert-True ((Invoke-ProgramUpdate -Root $copy -CurrentVersion '2026.11.01.1') -eq 'up-to-date') 'Same version must be up to date'
    Assert-True ($script:downloads -eq $downloads) 'Up-to-date check must not download'
    $script:release = $null
    Assert-True ((Invoke-ProgramUpdate -Root $copy -CurrentVersion '2026.11.01.1') -eq 'no-release') 'Empty cloud must be reported'

    # ---------- Cancel and rollback offer ----------
    Set-Release '2026.11.01.1' $newFiles
    $copy = New-ProgramCopy 'cancel'
    $before = Get-Snapshot $copy
    Set-Choices @(1)
    Assert-True ((Invoke-ProgramUpdate -Root $copy -CurrentVersion '2026.10.24.1') -eq 'cancelled') 'Cancel must be possible'
    Set-Choices @(-1)
    Assert-True ((Invoke-ProgramUpdate -Root $copy -CurrentVersion '2026.12.01.1') -eq 'cancelled') 'Esc must cancel'
    Assert-True ($script:lastPrompt -eq 'Откат программы') 'An older cloud version must be offered as a rollback'
    Assert-True ((Get-Snapshot $copy) -eq $before) 'Cancelling must change nothing'

    # ---------- Broken downloads and tampered archives change nothing ----------
    $copy = New-ProgramCopy 'broken'
    $before = Get-Snapshot $copy
    $script:corruptDownload = $true
    Set-Choices @(0)
    Assert-Throws { Invoke-ProgramUpdate -Root $copy -CurrentVersion '2026.10.24.1' } '*контрольная сумма*' 'A corrupted download must be rejected'
    $script:corruptDownload = $false
    Assert-True (@(Get-ChildItem "$copy\.update" -Filter '*.zip').Count -eq 0) 'A corrupted download must be deleted'

    $tampered = Join-Path $testRoot 'tampered.zip'
    $manifest = @{ version = '2026.11.02.1'; commit = 'x'; notes = @(); files = @(@{ path = 'a.ps1'; sha256 = ('0' * 64); size = 5 }) } | ConvertTo-Json -Depth 5
    New-RawZip $tampered @{ 'manifest.json' = $manifest; 'a.ps1' = 'evil!' }
    Set-ReleaseArchive '2026.11.02.1' $tampered
    Set-Choices @(0)
    Assert-Throws { Invoke-ProgramUpdate -Root $copy -CurrentVersion '2026.10.24.1' } '*не совпадает с manifest.json*' 'A file that differs from the manifest must be rejected'

    $extra = Join-Path $testRoot 'extra.zip'
    $sha = Get-BytesSha256 ([Text.Encoding]::UTF8.GetBytes('ok'))
    $manifest = @{ version = '2026.11.03.1'; commit = 'x'; notes = @(); files = @(@{ path = 'a.ps1'; sha256 = $sha; size = 2 }) } | ConvertTo-Json -Depth 5
    New-RawZip $extra @{ 'manifest.json' = $manifest; 'a.ps1' = 'ok'; 'sub/evil.ps1' = 'x' }
    Set-ReleaseArchive '2026.11.03.1' $extra
    Set-Choices @(0)
    Assert-Throws { Invoke-ProgramUpdate -Root $copy -CurrentVersion '2026.10.24.1' } '*Лишний файл*' 'Files outside the manifest must be rejected'
    Assert-True ((Get-Snapshot $copy) -eq $before) 'Rejected archives must change nothing'

    # ---------- A failure while replacing files restores the previous version ----------
    Set-Release '2026.11.04.1' $newFiles
    $copy = New-ProgramCopy 'rollback'
    $before = Get-Snapshot $copy
    $zip = Join-Path $testRoot 'rollback.zip'
    Copy-Item $script:packagePath $zip
    # Like an editor holding the launcher open: readable, but it cannot be replaced.
    $handle = [IO.File]::Open("$copy\run.BAT", 'Open', 'Read', [IO.FileShare]::Read)
    try {
        Assert-Throws { Install-ProgramRelease -Root $copy -ZipPath $zip -ExpectedVersion '2026.11.04.1' -CurrentVersion '2026.10.24.1' -Attempts 2 } '*прежняя версия восстановлена*' 'A failed install must report the restore'
    }
    finally { $handle.Dispose() }
    Assert-True ((Get-Snapshot $copy) -eq $before) 'A failed install must restore every file and remove added ones'

    # ---------- Development copies are not updated ----------
    $copy = New-ProgramCopy 'dev'
    New-Item -ItemType Directory -Path "$copy\.git" | Out-Null
    Assert-True ((Invoke-ProgramUpdate -Root $copy -CurrentVersion '2026.10.24.1') -eq 'development') 'A Git working copy must not be updated'

    # ---------- Only two backups and archives are kept ----------
    $copy = New-ProgramCopy 'many'
    $current = '2026.10.24.1'
    foreach ($version in '2026.11.05.1', '2026.11.05.2', '2026.11.05.3') {
        Set-Release $version $newFiles
        Set-Choices @(0)
        Assert-True ((Invoke-ProgramUpdate -Root $copy -CurrentVersion $current) -eq 'updated') "Update to $version failed"
        $current = $version
        Start-Sleep -Milliseconds 1100
    }
    Assert-True (@(Get-ChildItem "$copy\.update" -Directory -Filter 'backup-*').Count -eq 2) 'Only the two latest backups must be kept'
    Assert-True (@(Get-ChildItem "$copy\.update" -File -Filter '*.zip').Count -eq 2) 'Only the two latest archives must be kept'

    [Console]::Out.WriteLine('All program update checks passed.')
}
finally {
    $resolved = [IO.Path]::GetFullPath($testRoot)
    $tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    if (-not $resolved.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase) -or
        (Split-Path -Leaf $resolved) -notlike 'comp-info-update-test-*') { throw 'Unsafe test cleanup path' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
