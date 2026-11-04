$ErrorActionPreference = 'Stop'
$project = Split-Path -Parent $PSScriptRoot
. (Join-Path $project 'computer-management.ps1')
function Assert-True($Value, $Message) { if (-not $Value) { throw $Message } }
$script:admin = $false
$script:launches = 0
function Test-ComputerAdministrator { return $script:admin }
function Start-Process {
    param($FilePath, $Verb, $WindowStyle, $ErrorAction, $ArgumentList, [switch]$Wait)
    Assert-True ($FilePath -eq 'powershell.exe' -and $Verb -eq 'RunAs' -and $WindowStyle -eq 'Normal') 'Wrong elevation command'
    Assert-True (-not $Wait) 'Original menu must not wait for elevated menu to close'
    Assert-True ($ArgumentList[4] -eq '"C:\test folder\comp-info-menu.ps1"') 'Menu path not quoted'
    Assert-True ($ArgumentList[5] -eq '-OpenScript' -and $ArgumentList[6] -eq $script:requestedScript) 'Requested wizard was not forwarded'
    $script:launches++
    if ($script:denyUac) { throw 'UAC cancelled' }
}
foreach ($name in @('set-computer-name.ps1', 'manage-computer-domain.ps1')) {
    $script:requestedScript = $name
    Assert-True (Request-ComputerMenuElevation -MenuPath 'C:\test folder\comp-info-menu.ps1' -OpenScript $name) 'Menu handoff not reported'
}
Assert-True ($script:launches -eq 2) 'Unexpected launch count'
$script:admin = $true
Assert-True (-not (Request-ComputerMenuElevation -MenuPath 'C:\test folder\comp-info-menu.ps1' -OpenScript 'manage-computer-domain.ps1')) 'Elevated menu requested another window'
Assert-True ($script:launches -eq 2) 'Elevated menu launched another window'
$script:admin = $false; $script:denyUac = $true; $script:requestedScript = 'manage-computer-domain.ps1'
$failed = $false
try { Request-ComputerMenuElevation -MenuPath 'C:\test folder\comp-info-menu.ps1' -OpenScript 'manage-computer-domain.ps1' | Out-Null }
catch { $failed = $true }
Assert-True $failed 'UAC cancellation reported a successful handoff'

# Exercise the actual menu dispatch without opening consoles or changing the machine.
$errors = $null
$ast = [Management.Automation.Language.Parser]::ParseFile((Join-Path $project 'comp-info-menu.ps1'), [ref]$null, [ref]$errors)
if ($errors.Count) { throw ($errors | Out-String) }
$definition = $ast.Find({ param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Invoke-MenuScript' }, $true)
. ([scriptblock]::Create($definition.Extent.Text.Replace('$PSScriptRoot', '$project').Replace('[void][Console]::ReadKey($true)', '$script:pauseCalls++')))
function Clear-Host { }
function Test-Path { param($LiteralPath, $PathType) return $true }
function Write-Host { }
function powershell.exe { $script:childCalls++; $global:LASTEXITCODE = 42 }
$script:childCalls = 0; $script:shouldExit = $false; $script:denyUac = $false
function Request-ComputerMenuElevation {
    param($MenuPath, $OpenScript)
    Assert-True ($OpenScript -eq $script:requestedScript) 'Wrong wizard dispatch'
    if ($script:denyUac) { throw 'UAC cancelled' }
    return $script:transfer
}
foreach ($name in @('set-computer-name.ps1', 'manage-computer-domain.ps1')) {
    $script:requestedScript = $name
    $script:childCalls = 0; $script:shouldExit = $false; $script:transfer = $true
    Invoke-MenuScript $name
    Assert-True ($script:shouldExit -and $script:childCalls -eq 0) "Old menu did not exit after successful transfer: $name"
    $script:shouldExit = $false; $script:denyUac = $true; $script:pauseCalls = 0
    Invoke-MenuScript $name
    Assert-True (-not $script:shouldExit -and $script:childCalls -eq 0 -and $script:pauseCalls -eq 1) "UAC cancellation closed the menu or started a wizard: $name"
    $script:denyUac = $false
    $script:shouldExit = $false; $script:transfer = $false
    Invoke-MenuScript $name
    Assert-True (-not $script:shouldExit -and $script:childCalls -eq 1) "Elevated menu did not reuse existing console: $name"
}
Invoke-MenuScript 'quick-ping.ps1'
Assert-True ($script:childCalls -eq 2 -and -not $script:shouldExit) 'Unrelated tools changed their launch behavior'
'Menu elevation checks passed (mocked processes; no UAC prompts or computer changes).'
