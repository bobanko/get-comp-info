# Adds FlashStashBee to the Microsoft Defender exclusions ON THIS computer, so your own utility is not
# flagged by a false positive (background copying from inserted flash drives is a behaviour the heuristics
# react to). Needs administrator rights. This is only about local exclusions for your own program — it does
# not turn off protection and does not hide anything.
#
# Add:     right click -> "Run with PowerShell" (or from an admin console):
#   powershell -NoProfile -ExecutionPolicy Bypass -File allow-in-defender.ps1
# Remove:
#   powershell -NoProfile -ExecutionPolicy Bypass -File allow-in-defender.ps1 -Remove
#
# By default it excludes the exe itself (bin\FlashStashBee.exe), the FlashStashBee.exe process and the
# archive folder (%TEMP%\FlashStashBee). If the exe or the folder is elsewhere, pass -ExePath and -BaseDir.

param(
    [string]$ExePath = (Join-Path (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)) 'bin\FlashStashBee.exe'),
    [string]$BaseDir = (Join-Path $env:TEMP 'FlashStashBee'),
    [switch]$Remove
)

$ErrorActionPreference = 'Stop'

$admin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
    [Security.Principal.WindowsBuiltinRole]::Administrator)
if (-not $admin) {
    Write-Warning 'Нужны права администратора. Запустите PowerShell «от имени администратора» и повторите.'
    exit 1
}
if (-not (Get-Command Add-MpPreference -ErrorAction SilentlyContinue)) {
    Write-Warning 'Microsoft Defender не найден (возможно, стоит сторонний антивирус — добавьте исключения в нём).'
    exit 1
}

if ($Remove) {
    Remove-MpPreference -ExclusionPath $ExePath -ErrorAction SilentlyContinue
    Remove-MpPreference -ExclusionPath $BaseDir -ErrorAction SilentlyContinue
    Remove-MpPreference -ExclusionProcess 'FlashStashBee.exe' -ErrorAction SilentlyContinue
    Write-Output 'Исключения Defender для FlashStashBee убраны.'
}
else {
    Add-MpPreference -ExclusionPath $ExePath
    Add-MpPreference -ExclusionPath $BaseDir
    Add-MpPreference -ExclusionProcess 'FlashStashBee.exe'
    Write-Output ('Добавлены исключения Defender:')
    Write-Output ('  файл:    ' + $ExePath)
    Write-Output ('  папка:   ' + $BaseDir)
    Write-Output ('  процесс: FlashStashBee.exe')
    Write-Output 'Проверить: Get-MpPreference | Select-Object -Expand ExclusionPath'
}
