$ErrorActionPreference = 'Stop'
$project = Split-Path -Parent $PSScriptRoot
. (Join-Path $project 'computer-management-ui.ps1')
function Assert-True($Value, $Message) { if (-not $Value) { throw $Message } }
function Assert-Throws([scriptblock]$Action, [string]$Message) {
    $failed = $false
    try { & $Action | Out-Null } catch { $failed = $true }
    Assert-True $failed $Message
}
# The domain is fixed and does not depend on cached cloud profiles.
$profilesRoot = Join-Path ([IO.Path]::GetTempPath()) ('policy-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $profilesRoot | Out-Null
Assert-True ((Get-ComputerPolicy -Root $profilesRoot).DomainName -eq 'gu.gvardia.mvs') 'Fixed domain missing'
Set-Content -LiteralPath (Join-Path $profilesRoot 'network-profiles.json') -Encoding UTF8 -Value '{"value":{"domainCheckHost":"10.0.0.1"}}'
Assert-True ((Get-ComputerPolicy -Root $profilesRoot).DomainName -eq 'gu.gvardia.mvs') 'Cached profile changed fixed domain'
Set-Content -LiteralPath (Join-Path $profilesRoot 'network-profiles.json') -Encoding UTF8 -Value '{"value":{"domainCheckHost":"dc.corp.example"}}'
Assert-True ((Get-ComputerPolicy -Root $profilesRoot).DomainName -eq 'gu.gvardia.mvs') 'Cached host changed fixed domain'
Set-Content -LiteralPath (Join-Path $profilesRoot 'network-profiles.json') -Encoding UTF8 -Value '{"value":{"domainCheckHost":"dc.corp.example","domainName":"Corp.Example"}}'
$readPolicy = ${function:Get-ComputerPolicy}
function Get-ComputerPolicy { & $readPolicy -Root $profilesRoot }
$policy = Get-ComputerPolicy
Assert-True ($policy.DomainName -eq 'gu.gvardia.mvs') 'Configured domain changed'
foreach ($suffix in @('A','TOC3','213-251','PETRENKO')) {
    Assert-True ($null -eq (Get-ComputerNameIssue "FR-042-$suffix" FR $policy)) "Valid name rejected: $suffix"
}
foreach ($name in @('FR-042-','FR-042--A','FR-042-A-','FR-042-ABCDEFGHI','FR-042-ИВАНОВ','FR-042-A.B','FR-042-A B','IF-042-A','FR-043-A','fr-042-test')) {
    Assert-True ([bool](Get-ComputerNameIssue $name FR $policy)) "Invalid name accepted: $name"
}
Assert-True ((ConvertTo-ComputerDesignation 'Петренко') -eq 'PETRENKO') 'Surname transliteration failed'
Assert-True ((ConvertTo-ComputerDesignation 'відділ 3') -eq 'VIDDIL-3') 'Ukrainian transliteration failed'
Assert-True ((Get-SuggestedComputerName 'IF-042-TOC3' FR $policy) -eq 'FR-042-TOC3') 'Suffix was not preserved'
Assert-True ((Get-SuggestedComputerName 'DESKTOP-X' FR $policy '' '10.1.213.251') -eq 'FR-042-213-251') 'IP suggestion failed'
Assert-True ((Get-SuggestedComputerName 'DESKTOP-X' IF $policy 'Петренко') -eq 'IF-042-PETRENKO') 'Meaningful suggestion failed'
Assert-True ((Get-SuggestedComputerName 'DESKTOP-X' FR $policy 'ДужеДовгеПрізвище').Length -le 15) 'Long suggestion not shortened'
$otherPolicy = [pscustomobject]@{ UnitCode = '007'; DomainName = 'test.example'; WorkgroupName = 'WORKGROUP'; InternetPrefix = 'INT'; DomainPrefix = 'LOC' }
Assert-True ((Get-SuggestedComputerName 'DESKTOP-X' LOC $otherPolicy '12345678') -eq 'LOC-007-1234567') 'Configurable unit/prefix failed'

# All system-changing entry points are replaced before exercising operation logic.
$script:calls = [Collections.Generic.List[string]]::new()
$script:state = [pscustomobject]@{ Name = 'IF-042-TOC3'; Domain = 'WORKGROUP'; PartOfDomain = $false; DomainRole = 0; ProductType = 1; Boot = 'boot1'; PendingName = $null; PendingDomain = $false; Receipt = $null }
$script:admin = $true
$script:edition = 'Professional'
$script:commandSuccess = $true
$script:localValid = $true
$script:domainValid = $true
function Test-ComputerAdministrator { return $script:admin }
function Get-ComputerManagementState { return $script:state }
function Get-ItemProperty { param($LiteralPath, $ErrorAction) [pscustomobject]@{ EditionID = $script:edition } }
function Set-ComputerChangeReceipt {
    param($State, $Operation, $Name, $Domain, $PartOfDomain)
    $script:calls.Add("receipt:$Operation")
    $script:state.Receipt = [pscustomobject]@{ Boot = $State.Boot; Name = $Name; Domain = $Domain; PartOfDomain = [int]$PartOfDomain }
}
function Clear-ComputerChangeReceipt { $script:calls.Add('clear'); $script:state.Receipt = $null }
function Test-ComputerDomainConnection { param($Domain) if (-not $script:domainValid) { throw 'DNS unavailable' }; $script:calls.Add("domain:$Domain"); return 'dc.test' }
function Test-LocalAdministratorCredential { param($Credential, $ComputerName) if (-not $script:localValid -or -not $Credential) { throw 'Invalid local credential' }; $script:calls.Add('local'); return 'local-admin' }
function Add-Computer {
    param($DomainName, $Credential, $NewName, [switch]$Force, [switch]$PassThru, $ErrorAction, [switch]$Restart)
    Assert-True (-not $Restart -and $DomainName -eq 'gu.gvardia.mvs' -and $Credential -and $NewName -eq 'FR-042-TOC3') 'Wrong join arguments'
    $script:calls.Add('join'); [pscustomobject]@{ HasSucceeded = $script:commandSuccess }
}
function Remove-Computer {
    param($UnjoinDomainCredential, $WorkgroupName, [switch]$Force, [switch]$PassThru, $ErrorAction, [switch]$Restart)
    Assert-True (-not $Restart -and $WorkgroupName -eq 'WORKGROUP' -and $UnjoinDomainCredential) 'Wrong leave arguments'
    $script:calls.Add('leave'); [pscustomobject]@{ HasSucceeded = $script:commandSuccess }
}
function Rename-Computer {
    param($NewName, $DomainCredential, [switch]$Force, [switch]$PassThru, $ErrorAction, [switch]$Restart)
    Assert-True (-not $Restart -and $NewName -match '^FR-042-') 'Wrong rename arguments'
    if ($script:state.PartOfDomain) { Assert-True ([bool]$DomainCredential) 'Missing rename domain credential' }
    $script:calls.Add('rename'); [pscustomobject]@{ HasSucceeded = $script:commandSuccess }
}
$credential = [pscredential]::new('test', (ConvertTo-SecureString 'dummy-test-password' -AsPlainText -Force))
Assert-True (Invoke-ComputerManagementChange Join 'FR-042-TOC3' $policy $credential) 'Join failed'
Assert-True (($script:calls -join ',') -eq 'domain:gu.gvardia.mvs,receipt:Join,join') 'Join preflight/order failed'
Assert-Throws { Invoke-ComputerManagementChange Rename 'FR-042-TOC4' $policy } 'Second pending operation was allowed'
$script:state.Receipt = $null; $script:calls.Clear()
$script:edition = 'Core'
Assert-Throws { Invoke-ComputerManagementChange Join 'FR-042-TOC3' $policy $credential } 'Home edition allowed'
Assert-True ($script:calls.Count -eq 0) 'Home changed the computer'
$script:edition = 'Professional'
$script:domainValid = $false
Assert-Throws { Invoke-ComputerManagementChange Join 'FR-042-TOC3' $policy $credential } 'Offline join allowed'
Assert-True ($script:calls.Count -eq 0) 'Offline join wrote receipt or changed computer'
$script:domainValid = $true
$script:state.PartOfDomain = $true; $script:state.Domain = $policy.DomainName; $script:state.Name = 'FR-042-TOC3'
$script:localValid = $false
Assert-Throws { Invoke-ComputerManagementChange Leave '' $policy $credential $credential } 'Leave allowed without verified local login'
Assert-True ($script:calls.Count -eq 0) 'Failed local login changed computer'
$script:localValid = $true
Assert-True (Invoke-ComputerManagementChange Leave '' $policy $credential $credential) 'Leave failed'
Assert-True (($script:calls -join ',') -eq 'local,domain:gu.gvardia.mvs,receipt:Leave,leave') 'Leave preflight/order failed'
$script:state.Receipt = $null; $script:calls.Clear()
Assert-Throws { Invoke-ComputerManagementChange Rename 'IF-042-TOC3' $policy $credential } 'IF name allowed while joined'
Assert-True ($script:calls.Count -eq 0) 'Invalid rename changed computer'
Assert-True (Invoke-ComputerManagementChange Rename 'FR-042-TOC4' $policy $credential) 'Domain rename failed'
$script:state.Receipt = $null; $script:calls.Clear()
$script:state.Domain = 'other.example'
Assert-Throws { Invoke-ComputerManagementChange Leave '' $policy $credential $credential } 'Foreign domain leave allowed'
$script:state.Domain = $policy.DomainName; $script:state.DomainRole = 5
Assert-Throws { Invoke-ComputerManagementChange Rename 'FR-042-TOC4' $policy $credential } 'DC rename allowed'
$script:state.DomainRole = 1; $script:state.PendingName = 'FR-042-PENDING'
Assert-Throws { Invoke-ComputerManagementChange Rename 'FR-042-TOC4' $policy $credential } 'External pending rename allowed'
$script:state.PendingName = $null; $script:admin = $false
Assert-Throws { Invoke-ComputerManagementChange Rename 'FR-042-TOC4' $policy $credential } 'Non-admin change allowed'
$script:admin = $true; $script:commandSuccess = $false
Assert-Throws { Invoke-ComputerManagementChange Rename 'FR-042-TOC4' $policy $credential } 'Failed native command reported success'
Assert-True ([bool]$script:state.Receipt) 'Receipt lost on command failure'

# Post-reboot matching and explicit acknowledgement of a partial failure.
function Write-Host { }
function Test-ComputerSecureChannel { param($ErrorAction) return $script:trustValid }
$script:trustValid = $true
function Read-ComputerWizardChoice { return $script:choice }
function Request-ComputerWizardRestart { $script:calls.Add('offer-restart') }
$script:state.Boot = 'boot2'; $script:state.Name = 'FR-042-TOC4'
Assert-True (Confirm-ComputerWizardState $script:state) 'Matching reboot result rejected'
Assert-True ($null -eq $script:state.Receipt) 'Successful receipt was not cleared'
$script:state.Receipt = [pscustomobject]@{ Boot = 'boot1'; Name = $script:state.Name; Domain = $policy.DomainName; PartOfDomain = 1 }
$script:trustValid = $false; $script:choice = 0
Assert-True (-not (Confirm-ComputerWizardState $script:state)) 'Broken domain trust was reported as success'
Assert-True ([bool]$script:state.Receipt) 'Broken trust receipt lost'
$script:trustValid = $true
$script:state.Receipt = [pscustomobject]@{ Boot = 'boot1'; Name = 'FR-042-FAIL'; Domain = $policy.DomainName; PartOfDomain = 1 }
$script:choice = 0
Assert-True (-not (Confirm-ComputerWizardState $script:state)) 'Mismatch ignored'
Assert-True ([bool]$script:state.Receipt) 'Mismatch receipt cleared without acknowledgement'
$script:choice = 1
Assert-True (Confirm-ComputerWizardState $script:state) 'Explicit acknowledgement failed'

# Opening the wizard does not request UAC; elevation is deferred to the confirmed change.
$script:admin = $false
function Start-Process {
    param($FilePath, $Verb, $WindowStyle, $ArgumentList, [switch]$Wait, [switch]$PassThru)
    Assert-True ($Verb -eq 'RunAs' -and $WindowStyle -eq 'Normal' -and $Wait) 'Bad interactive UAC options'
    Assert-True (($ArgumentList -join ' ') -notmatch 'password|dummy-test') 'Credential in process arguments'
    Assert-True ($ArgumentList[-1] -eq '"C:\test folder\set-computer-name.ps1"') 'Script path not quoted'
    [pscustomobject]@{ ExitCode = 0 }
}
Assert-True (Start-ComputerManagement 'C:\test folder\set-computer-name.ps1') 'Wizard should open without an early UAC prompt'
$script:admin = $true

# Drive the complete wizard with queued choices instead of a physical console.
function Read-ComputerWizardChoice {
    param($Title, $Step, $Lines, $Items, $ErrorText)
    if ($script:choices.Count -eq 0) { throw "Unexpected wizard step: $Step" }
    if ($Step -like 'Последний*') { $script:summary = $Lines -join '|'; $script:calls.Add('summary') }
    return $script:choices.Dequeue()
}
function Show-ComputerWizardScreen { }
function Read-ComputerWizardText {
    param($Initial)
    if ($script:inputs.Count -eq 0) { throw 'Unexpected wizard text input' }
    return $script:inputs.Dequeue()
}
function Get-NetIPAddress { param($AddressFamily, $ErrorAction) [pscustomobject]@{ IPAddress = '10.1.213.251'; InterfaceAlias = 'Ethernet' } }
function Get-Credential { param($Message) return $credential }
function Assert-InventoryReadyForRename { $script:calls.Add('identity-check') }
function Invoke-ComputerManagementChange {
    param($Operation, $Name, $Policy, $DomainCredential, $LocalCredential)
    $script:calls.Add("apply:$Operation`:$Name")
    if ($Operation -ne 'Rename') { Assert-True ([bool]$DomainCredential) 'Wizard lost domain credential' }
    if ($Operation -eq 'Leave') { Assert-True ([bool]$LocalCredential) 'Wizard lost verified local credential' }
    return $true
}
function Set-WizardInput([int[]]$Choices, [object[]]$Inputs) {
    $script:choices = [Collections.Generic.Queue[int]]::new()
    foreach ($item in $Choices) { $script:choices.Enqueue($item) }
    $script:inputs = [Collections.Generic.Queue[object]]::new()
    foreach ($item in $Inputs) { $script:inputs.Enqueue($item) }
    $script:calls.Clear()
    $script:state.Receipt = $null; $script:state.PendingName = $null
}
$script:state.PartOfDomain = $false; $script:state.Domain = 'WORKGROUP'; $script:state.Name = 'DESKTOP-X'
Set-WizardInput @(0,0) @([pscustomobject]@{ Back = $false; Value = 'Петренко' })
Invoke-ComputerManagementWizard Rename
Assert-True (($script:calls -join ',') -eq 'summary,identity-check,apply:Rename:IF-042-PETRENKO,offer-restart') 'Rename wizard flow failed'
Assert-True ($script:summary -match 'IF-042-PETRENKO') 'Transliterated result missing from summary'
Set-WizardInput @(0,0,0) @([pscustomobject]@{ Back = $false; Value = 'TOC3' })
Invoke-ComputerManagementWizard Domain
Assert-True (($script:calls -join ',') -eq 'domain:gu.gvardia.mvs,summary,identity-check,apply:Join:FR-042-TOC3,offer-restart') 'Join wizard flow failed'
# Escape on summary goes back to the text step; then back to network selection.
Set-WizardInput @(0,-1,2) @([pscustomobject]@{ Back = $false; Value = 'TOC3' }, [pscustomobject]@{ Back = $true; Value = $null })
Invoke-ComputerManagementWizard Rename
Assert-True (($script:calls -join ',') -eq 'summary') 'Back navigation applied changes'
$script:state.PartOfDomain = $true; $script:state.Domain = $policy.DomainName; $script:state.Name = 'FR-042-TOC3'
Set-WizardInput @(0,0,0,0) @()
Invoke-ComputerManagementWizard Domain
Assert-True (($script:calls -join ',') -eq 'local,domain:gu.gvardia.mvs,summary,apply:Leave:,offer-restart') 'Leave wizard flow failed'
Assert-True ($script:summary -match 'FR-042-TOC3 -> FR-042-TOC3' -and $script:summary -match 'WORKGROUP') 'Leave unexpectedly renamed computer'
'Computer management checks passed (mocked system commands; no computer changes).'
