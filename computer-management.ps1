# Shared helpers; dot-sourcing never performs a system operation.
. (Join-Path $PSScriptRoot 'computer-policy.ps1')
. (Join-Path $PSScriptRoot 'computer-identity.ps1')

function Assert-ComputerPolicy {
    param($Policy)
    if ($Policy.UnitCode -cnotmatch '^\d{3}$' -or $Policy.InternetPrefix -cnotmatch '^[A-Z]{2,3}$' -or
        $Policy.DomainPrefix -cnotmatch '^[A-Z]{2,3}$' -or $Policy.InternetPrefix -eq $Policy.DomainPrefix -or
        $Policy.DomainName -notmatch '^(?=.{1,253}$)[a-z0-9]+(?:[.-][a-z0-9]+)*\.[a-z0-9]+$' -or
        $Policy.WorkgroupName -cnotmatch '^[A-Z][A-Z0-9-]{0,14}$') { throw 'Некорректная конфигурация имени или домена.' }
}

function ConvertTo-ComputerDesignation {
    param([string]$Text)
    $letters = 'АБВГҐДЕЁЄЖЗИІЇЙКЛМНОПРСТУФХЦЧШЩЪЫЬЭЮЯ'
    $latin = @('A','B','V','G','G','D','E','E','YE','ZH','Z','I','I','YI','Y','K','L','M','N','O','P','R','S','T','U','F','KH','TS','CH','SH','SHCH','','Y','','E','YU','YA')
    $result = foreach ($character in $Text.Trim().ToUpperInvariant().ToCharArray()) {
        $index = $letters.IndexOf($character)
        if ($index -ge 0) { $latin[$index] } elseif ([char]::IsWhiteSpace($character)) { '-' } else { [string]$character }
    }
    return ($result -join '')
}

function Get-ComputerNameIssue {
    param([string]$Name, [string]$Prefix, $Policy)
    Assert-ComputerPolicy $Policy
    if ($Prefix -notin @($Policy.InternetPrefix, $Policy.DomainPrefix)) { return 'Неизвестный тип сети.' }
    if ($Name.Length -gt 15) { return 'Полное имя должно содержать не больше 15 символов.' }
    $start = [regex]::Escape("$Prefix-$($Policy.UnitCode)-")
    if ($Name -cnotmatch "^${start}[A-Z0-9](?:[A-Z0-9-]*[A-Z0-9])?$") {
        return "Нужно имя $Prefix-$($Policy.UnitCode)-...: латиница, цифры и дефис; окончание без дефиса по краям."
    }
    return $null
}

function Get-SuggestedComputerName {
    param([string]$CurrentName, [string]$Prefix, $Policy, [string]$Designation, [string]$IpAddress)
    Assert-ComputerPolicy $Policy
    $suffix = ''
    if ($CurrentName -match '^[A-Z]{2,3}-\d{3}-(.+)$') { $suffix = ConvertTo-ComputerDesignation $Matches[1] }
    if (-not $suffix -and $Designation) { $suffix = ConvertTo-ComputerDesignation $Designation }
    if (-not $suffix -and $IpAddress -match '^\d{1,3}\.\d{1,3}\.(\d{1,3})\.(\d{1,3})$') { $suffix = "$($Matches[1])-$($Matches[2])" }
    if (-not $suffix) { $suffix = 'PC' }
    $suffix = ($suffix -replace '[^A-Z0-9-]', '').Trim('-')
    if (-not $suffix) { $suffix = 'PC' }
    $start = "$Prefix-$($Policy.UnitCode)-"
    $suffix = $suffix.Substring(0, [Math]::Min($suffix.Length, 15 - $start.Length)).TrimEnd('-')
    return $start + $suffix
}

function Test-ComputerAdministrator {
    $principal = [Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Start-ComputerManagement {
    param([string]$ScriptPath)
    if (Test-ComputerAdministrator) { return $true }
    # This is an interactive wizard: it needs a visible elevated console.
    $process = Start-Process -FilePath 'powershell.exe' -Verb RunAs -WindowStyle Normal -Wait -PassThru -ArgumentList @(
        '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', ('"' + $ScriptPath + '"'))
    if ($process.ExitCode -notin @(0, 42)) { throw "Окно настройки завершилось с кодом $($process.ExitCode)." }
    return $false
}

function Request-ComputerMenuElevation {
    param(
        [Parameter(Mandatory)][string]$MenuPath,
        [Parameter(Mandatory)][ValidateSet('set-computer-name.ps1', 'manage-computer-domain.ps1')][string]$OpenScript
    )
    if (Test-ComputerAdministrator) { return $false }
    # Hand the entire menu to the elevated window. Do not keep the old menu waiting.
    Start-Process -FilePath 'powershell.exe' -Verb RunAs -WindowStyle Normal -ErrorAction Stop -ArgumentList @(
        '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', ('"' + $MenuPath + '"'), '-OpenScript', $OpenScript
    ) | Out-Null
    return $true
}

function Get-ComputerManagementState {
    $system = Get-CimInstance Win32_ComputerSystem -ErrorAction Stop
    $os = Get-CimInstance Win32_OperatingSystem -ErrorAction Stop
    $active = (Get-ItemProperty -LiteralPath 'HKLM:\SYSTEM\CurrentControlSet\Control\ComputerName\ActiveComputerName' -ErrorAction Stop).ComputerName
    $configured = (Get-ItemProperty -LiteralPath 'HKLM:\SYSTEM\CurrentControlSet\Control\ComputerName\ComputerName' -ErrorAction Stop).ComputerName
    $netlogon = Get-ItemProperty -LiteralPath 'HKLM:\SYSTEM\CurrentControlSet\Services\Netlogon' -ErrorAction Stop
    $receiptPath = 'HKLM:\SYSTEM\CurrentControlSet\Control\ComputerInventoryCollector\PendingChange'
    $receipt = if (Test-Path -LiteralPath $receiptPath) { Get-ItemProperty -LiteralPath $receiptPath -ErrorAction Stop } else { $null }
    return [pscustomobject]@{
        Name = [string]$system.Name; Domain = [string]$system.Domain; PartOfDomain = [bool]$system.PartOfDomain
        DomainRole = [int]$system.DomainRole; ProductType = [int]$os.ProductType
        Boot = $os.LastBootUpTime.ToUniversalTime().ToString('o')
        PendingName = $(if ($active -ne $configured) { [string]$configured } else { $null })
        PendingDomain = [bool]($netlogon.PSObject.Properties['JoinDomain'] -or $netlogon.PSObject.Properties['AvoidSpnSet'])
        Receipt = $receipt
    }
}

function Set-ComputerChangeReceipt {
    param($State, [string]$Operation, [string]$Name, [string]$Domain, [bool]$PartOfDomain)
    $path = 'HKLM:\SYSTEM\CurrentControlSet\Control\ComputerInventoryCollector\PendingChange'
    New-Item -Path $path -Force -ErrorAction Stop | Out-Null
    foreach ($entry in @{ Boot = $State.Boot; Operation = $Operation; Name = $Name; Domain = $Domain; PartOfDomain = [int]$PartOfDomain }.GetEnumerator()) {
        New-ItemProperty -LiteralPath $path -Name $entry.Key -Value $entry.Value -PropertyType String -Force -ErrorAction Stop | Out-Null
    }
}

function Clear-ComputerChangeReceipt {
    Remove-Item -LiteralPath 'HKLM:\SYSTEM\CurrentControlSet\Control\ComputerInventoryCollector\PendingChange' -Force -ErrorAction Stop
}

function Assert-ComputerChangeAllowed {
    param($State)
    if ($State.DomainRole -ge 4) { throw 'Изменения на контроллере домена не поддерживаются.' }
    if ($State.PendingName -or $State.PendingDomain -or $State.Receipt) { throw 'Есть незавершённое изменение. Сначала перезагрузите компьютер и проверьте результат.' }
}

function Test-ComputerDomainConnection {
    param([string]$Domain)
    $srv = @(Resolve-DnsName -Name "_ldap._tcp.dc._msdcs.$Domain" -Type SRV -DnsOnly -ErrorAction Stop |
        Where-Object { $_.NameTarget })
    if ($srv.Count -eq 0) { throw 'DNS не вернул контроллеры домена. Проверьте DNS LAN-адаптера.' }
    $details = & "$env:SystemRoot\System32\nltest.exe" "/dsgetdc:$Domain" '/force' 2>&1
    if ($LASTEXITCODE -ne 0) { throw "Контроллер домена недоступен. Проверьте LAN, DNS и время Windows. $($details -join ' ')" }
    return ($srv.NameTarget -join ', ')
}

function Test-LocalAdministratorCredential {
    param([pscredential]$Credential, [string]$ComputerName)
    if ($null -eq $Credential) { throw 'Локальная учётная запись не указана.' }
    $parts = $Credential.UserName -split '\\'
    $name = $parts[-1]
    if (($parts.Count -gt 1 -and ($parts.Count -ne 2 -or $parts[0] -notin @('.', $ComputerName))) -or
        $name.Contains('@')) { throw 'Укажите локальную учётную запись: .\имя или ИМЯ-ПК\имя.' }
    $account = @(Get-CimInstance Win32_UserAccount -Filter 'LocalAccount=True' -ErrorAction Stop | Where-Object { $_.Name -eq $name -and -not $_.Disabled -and -not $_.Lockout })
    $group = Get-CimInstance Win32_Group -Filter "LocalAccount=True AND SID='S-1-5-32-544'" -ErrorAction Stop
    $members = @(Get-CimAssociatedInstance -InputObject $group -Association Win32_GroupUser -ResultClassName Win32_UserAccount -ErrorAction Stop)
    if ($account.Count -ne 1 -or $account[0].SID -notin $members.SID) { throw 'Нужен включённый локальный пользователь, непосредственно входящий в группу администраторов.' }
    if (-not ('ComputerInventory.NativeLogon' -as [type])) {
        Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
namespace ComputerInventory {
    public static class NativeLogon {
        [DllImport("advapi32.dll", SetLastError=true, CharSet=CharSet.Unicode)]
        public static extern bool LogonUser(string user, string domain, IntPtr password, int type, int provider, out IntPtr token);
        [DllImport("kernel32.dll", SetLastError=true)]
        public static extern bool CloseHandle(IntPtr handle);
    }
}
'@
    }
    $password = [Runtime.InteropServices.Marshal]::SecureStringToGlobalAllocUnicode($Credential.Password)
    $token = [IntPtr]::Zero
    try {
        if (-not [ComputerInventory.NativeLogon]::LogonUser($name, $ComputerName, $password, 2, 0, [ref]$token)) {
            $code = [Runtime.InteropServices.Marshal]::GetLastWin32Error()
            throw "Локальный вход не подтверждён (код $code). Проверьте пароль и право локального входа."
        }
    }
    finally {
        if ($token -ne [IntPtr]::Zero) { [void][ComputerInventory.NativeLogon]::CloseHandle($token) }
        [Runtime.InteropServices.Marshal]::ZeroFreeGlobalAllocUnicode($password)
    }
    return $name
}

function Invoke-ComputerManagementChange {
    param([ValidateSet('Rename','Join','Leave')][string]$Operation, [string]$Name, $Policy,
        [pscredential]$DomainCredential, [pscredential]$LocalCredential)
    Assert-ComputerPolicy $Policy
    if (-not (Test-ComputerAdministrator)) { throw 'Нужны права локального администратора.' }
    $state = Get-ComputerManagementState
    Assert-ComputerChangeAllowed $state
    if ($Operation -ne 'Leave') {
        $prefix = if ($Name.StartsWith($Policy.DomainPrefix + '-')) { $Policy.DomainPrefix } else { $Policy.InternetPrefix }
        $issue = Get-ComputerNameIssue -Name $Name -Prefix $prefix -Policy $Policy
        if ($issue) { throw $issue }
    }
    if ($state.PartOfDomain -and $state.Domain -ne $Policy.DomainName) { throw 'Компьютер состоит в другом домене. Автоматический перенос не поддерживается.' }
    if ($Operation -eq 'Rename' -and $state.PartOfDomain -and $prefix -ne $Policy.DomainPrefix) { throw 'Сначала выведите компьютер из домена, затем назначайте имя интернет-сети.' }
    if ($Operation -eq 'Rename' -and $state.Name -eq $Name) { return $false }
    if ($Operation -eq 'Join') {
        if ($state.PartOfDomain) { throw 'Компьютер уже состоит в домене.' }
        if ($prefix -ne $Policy.DomainPrefix) { throw 'Для домена требуется префикс доменной сети.' }
        if ($state.ProductType -eq 1) {
            $edition = (Get-ItemProperty -LiteralPath 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion' -ErrorAction Stop).EditionID
            if ($edition -notmatch '^(Professional|Enterprise|Education|IoTEnterprise|Ultimate)') { throw 'Эта редакция Windows не поддерживается для ввода в домен.' }
        }
    }
    if ($Operation -eq 'Leave' -and -not $state.PartOfDomain) { throw 'Компьютер уже находится в рабочей группе.' }
    if ($Operation -eq 'Leave') { [void](Test-LocalAdministratorCredential -Credential $LocalCredential -ComputerName $state.Name) }
    if ($Operation -ne 'Rename' -or $state.PartOfDomain) {
        if ($null -eq $DomainCredential) { throw 'Нужны доменные учётные данные.' }
        [void](Test-ComputerDomainConnection $Policy.DomainName)
    }
    $targetName = if ($Operation -eq 'Leave') { $state.Name } else { $Name }
    $targetDomain = if ($Operation -eq 'Join') { $Policy.DomainName } elseif ($Operation -eq 'Leave') { $Policy.WorkgroupName } else { $state.Domain }
    $targetMember = if ($Operation -eq 'Join') { $true } elseif ($Operation -eq 'Leave') { $false } else { $state.PartOfDomain }
    # Write BEFORE the system command: a partial failure must not permit a second change.
    Set-ComputerChangeReceipt -State $state -Operation $Operation -Name $targetName -Domain $targetDomain -PartOfDomain $targetMember
    $arguments = @{ Force = $true; PassThru = $true; ErrorAction = 'Stop' }
    switch ($Operation) {
        'Join' {
            $arguments.DomainName = $Policy.DomainName; $arguments.Credential = $DomainCredential
            if ($state.Name -ne $Name) { $arguments.NewName = $Name }
            $result = Add-Computer @arguments
        }
        'Leave' {
            $arguments.UnjoinDomainCredential = $DomainCredential; $arguments.WorkgroupName = $Policy.WorkgroupName
            $result = Remove-Computer @arguments
        }
        'Rename' {
            $arguments.NewName = $Name
            if ($state.PartOfDomain) { $arguments.DomainCredential = $DomainCredential }
            $result = Rename-Computer @arguments
        }
    }
    if (-not $result.HasSucceeded) { throw 'Windows не подтвердила изменение. Перезагрузите компьютер и проверьте состояние.' }
    return $true
}
