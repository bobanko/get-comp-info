[CmdletBinding()]
param([string]$UsbPath)

$ErrorActionPreference = 'Stop'

function Assert-NoLink {
    param([string]$Path)
    $itemPath = [IO.Path]::GetFullPath($Path)
    while ($itemPath) {
        if (Test-Path -LiteralPath $itemPath) {
            $item = Get-Item -LiteralPath $itemPath -Force
            if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) {
                throw "Links are not supported: $itemPath"
            }
        }
        $itemPath = Split-Path -Parent $itemPath
    }
}

function Copy-Verified {
    param([string]$Source, [string]$Destination)
    Assert-NoLink $Source
    Assert-NoLink $Destination
    $sourceHash = (Get-FileHash -LiteralPath $Source -Algorithm SHA256).Hash
    Copy-Item -LiteralPath $Source -Destination $Destination -Force
    if ((Get-FileHash -LiteralPath $Destination -Algorithm SHA256).Hash -ne $sourceHash) {
        throw "Copy verification failed: $Destination"
    }
}

try {
    if ([string]::IsNullOrWhiteSpace($UsbPath)) {
        $UsbPath = Read-Host 'USB project folder (example: E:\get-comp-info)'
    }
    if ([string]::IsNullOrWhiteSpace($UsbPath)) { throw 'USB path is required.' }
    $UsbPath = $UsbPath.Trim().Trim('"')
    if (-not [IO.Path]::IsPathRooted($UsbPath) -or $UsbPath -match '^[A-Za-z]:[^\\/]') {
        throw 'Enter an absolute folder path.'
    }
    $localRoot = [IO.Path]::GetFullPath($PSScriptRoot).TrimEnd('\', '/')
    $usbRoot = [IO.Path]::GetFullPath($UsbPath).TrimEnd('\', '/')
    if ($usbRoot -eq [IO.Path]::GetPathRoot($UsbPath).TrimEnd('\', '/')) {
        throw 'Choose a project folder, not the drive root.'
    }
    if ($usbRoot -eq $localRoot -or
        $usbRoot.StartsWith($localRoot + '\', [StringComparison]::OrdinalIgnoreCase) -or
        $localRoot.StartsWith($usbRoot + '\', [StringComparison]::OrdinalIgnoreCase)) {
        throw 'The local and USB folders must be separate and must not contain each other.'
    }
    Assert-NoLink $localRoot
    Assert-NoLink $usbRoot
    $localData = Join-Path $localRoot 'data'
    $usbData = Join-Path $usbRoot 'data'
    $conflictRoot = Join-Path $localRoot 'sync-conflicts'
    foreach ($path in @($localData, $usbData, $conflictRoot)) { Assert-NoLink $path }

    # Only the portable application's root scripts are deployed.
    $programFiles = @(Get-ChildItem -LiteralPath $localRoot -File | Where-Object {
        $_.Extension -in @('.ps1', '.bat', '.cmd')
    })
    foreach ($file in $programFiles) {
        Assert-NoLink $file.FullName
        Assert-NoLink (Join-Path $usbRoot $file.Name)
    }
    New-Item -ItemType Directory -Path $usbRoot -Force | Out-Null
    New-Item -ItemType Directory -Path $localData -Force | Out-Null
    $added = 0
    $unchanged = 0
    $conflicts = 0
    $updated = 0

    if (Test-Path -LiteralPath $usbData) {
        foreach ($file in Get-ChildItem -LiteralPath $usbData -Filter '*.json' -File) {
            Assert-NoLink $file.FullName
            $destination = Join-Path $localData $file.Name
            Assert-NoLink $destination
            if (-not (Test-Path -LiteralPath $destination)) {
                Copy-Verified $file.FullName $destination
                $added++
            } else {
                $usbHash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
                if ($usbHash -eq (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash) {
                    $unchanged++
                } else {
                    # Keep conflicts outside data so the viewer does not show duplicates.
                    $folder = Join-Path $conflictRoot $file.BaseName
                    Assert-NoLink $folder
                    New-Item -ItemType Directory -Path $folder -Force | Out-Null
                    $copy = Join-Path $folder ($usbHash + '.json')
                    Copy-Verified $file.FullName $copy
                    Write-Warning "Conflict: $($file.Name). Local record retained; USB version: $copy"
                    $conflicts++
                }
            }
        }
    }

    # Update code only after all collected records have been copied successfully.
    foreach ($file in $programFiles) {
        $destination = Join-Path $usbRoot $file.Name
        if ((Test-Path -LiteralPath $destination) -and
            (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash -eq
            (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash) { continue }
        Copy-Verified $file.FullName $destination
        $updated++
    }
    Write-Host "Done. New records: $added; unchanged: $unchanged; conflicts: $conflicts; program files updated: $updated."
    Write-Host 'USB records were retained. No files were deleted.'
    if ($conflicts -gt 0) { Write-Host "Review USB versions in: $conflictRoot" }
    exit 0
} catch {
    Write-Error "Sync stopped: $($_.Exception.Message)" -ErrorAction Continue
    exit 1
}
