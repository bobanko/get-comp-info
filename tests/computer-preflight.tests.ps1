$ErrorActionPreference = 'Stop'
$project = Split-Path -Parent $PSScriptRoot
. (Join-Path $project 'computer-management.ps1')
function Assert-True($Value, $Message) { if (-not $Value) { throw $Message } }
function Assert-Throws([scriptblock]$Action, [string]$Message) {
    $failed = $false
    try { & $Action | Out-Null } catch { $failed = $true }
    Assert-True $failed $Message
}
# Fake native API: no real logon or token operations, including the success path.
Add-Type -TypeDefinition @'
using System;
namespace ComputerInventory {
    public static class NativeLogon {
        public static bool Valid = true;
        public static bool Closed = false;
        public static bool LogonUser(string user, string domain, IntPtr password, int type, int provider, out IntPtr token) {
            if (user != "admin-test" || domain != "TEST-PC" || password == IntPtr.Zero || type != 2 || provider != 0)
                throw new Exception("Invalid local logon arguments");
            token = Valid ? new IntPtr(1) : IntPtr.Zero;
            return Valid;
        }
        public static bool CloseHandle(IntPtr token) { Closed = true; return true; }
    }
}
'@
$script:disabled = $false
$script:member = $true
$script:pendingDomain = $false
$script:pendingName = $false
function Get-CimInstance {
    param($ClassName, $Filter, $ErrorAction)
    switch ($ClassName) {
        Win32_UserAccount { [pscustomobject]@{ Name = 'admin-test'; SID = 'S-1-5-21-111-222-333-1001'; Disabled = $script:disabled; Lockout = $false } }
        Win32_Group { Assert-True ($Filter -match 'S-1-5-32-544') 'Admin group selected by translated name'; [pscustomobject]@{ SID = 'S-1-5-32-544' } }
        Win32_ComputerSystem { [pscustomobject]@{ Name = 'TEST-PC'; PartOfDomain = $false; Domain = 'WORKGROUP'; DomainRole = 0 } }
        Win32_OperatingSystem { [pscustomobject]@{ ProductType = 1; LastBootUpTime = [datetime]'2026-11-03T01:00:00Z' } }
        default { throw "Unexpected CIM class $ClassName" }
    }
}
function Get-CimAssociatedInstance {
    param($InputObject, $Association, $ResultClassName, $ErrorAction)
    Assert-True ($Association -eq 'Win32_GroupUser' -and $InputObject.SID -eq 'S-1-5-32-544') 'Wrong group membership query'
    if ($script:member) { [pscustomobject]@{ SID = 'S-1-5-21-111-222-333-1001' } }
}
function Test-Path { param($LiteralPath) return $false }
function Get-ItemProperty {
    param($LiteralPath, $ErrorAction)
    if ($LiteralPath -like '*ActiveComputerName') { return [pscustomobject]@{ ComputerName = 'TEST-PC' } }
    if ($LiteralPath -like '*\ComputerName') { return [pscustomobject]@{ ComputerName = $(if ($script:pendingName) { 'NEW-PC' } else { 'TEST-PC' }) } }
    if ($LiteralPath -like '*\Netlogon') {
        if ($script:pendingDomain) { return [pscustomobject]@{ JoinDomain = 0 } }
        return [pscustomobject]@{}
    }
    throw 'Unexpected registry read'
}
$password = ConvertTo-SecureString 'dummy' -AsPlainText -Force
$credential = [pscredential]::new('.\admin-test', $password)
Assert-True ((Test-LocalAdministratorCredential $credential 'TEST-PC') -eq 'admin-test') 'Valid local admin rejected'
Assert-True ([ComputerInventory.NativeLogon]::Closed) 'Token handle not closed'
[ComputerInventory.NativeLogon]::Valid = $false
Assert-Throws { Test-LocalAdministratorCredential $credential 'TEST-PC' } 'Wrong password accepted'
[ComputerInventory.NativeLogon]::Valid = $true
$script:disabled = $true
Assert-Throws { Test-LocalAdministratorCredential $credential 'TEST-PC' } 'Disabled local account accepted'
$script:disabled = $false; $script:member = $false
Assert-Throws { Test-LocalAdministratorCredential $credential 'TEST-PC' } 'Non-admin account accepted'
$script:member = $true
Assert-Throws { Test-LocalAdministratorCredential ([pscredential]::new('DOMAIN\admin-test', $password)) 'TEST-PC' } 'Domain credential accepted as local'
Assert-Throws { Test-LocalAdministratorCredential ([pscredential]::new('admin-test@example.com', $password)) 'TEST-PC' } 'UPN accepted as local'
$state = Get-ComputerManagementState
Assert-True (-not $state.PendingName -and -not $state.PendingDomain -and -not $state.Receipt) 'Clean state falsely pending'
$script:pendingDomain = $true
Assert-True ((Get-ComputerManagementState).PendingDomain) 'Zero-valued pending domain marker ignored'
$script:pendingName = $true
Assert-True ((Get-ComputerManagementState).PendingName -eq 'NEW-PC') 'External rename not detected'
'Computer preflight checks passed (fake native logon, CIM and registry; no system changes).'
