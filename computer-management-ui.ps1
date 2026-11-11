. (Join-Path $PSScriptRoot 'console-ui.ps1')
. (Join-Path $PSScriptRoot 'computer-management.ps1')

function Show-ComputerWizardScreen {
    param([string]$Title, [string]$Step, [string[]]$Lines)
    Clear-Host
    Write-ToolboxHeader -Section " / $Title"
    Write-ToolboxHint '[▲▼] — выбрать, [↲ Enter] — продолжить, [Esc] — назад.'
    Write-Host $Step -ForegroundColor DarkGray
    Write-Host ''
    foreach ($line in $Lines) {
        if ($line -match '^(.* -> )(.*)$') {
            Write-Host $Matches[1] -NoNewline -ForegroundColor DarkGray
            Write-Host $Matches[2] -ForegroundColor Cyan
        }
        else { Write-Host $line }
    }
    Write-Host ''
}

function Read-ComputerWizardChoice {
    param([string]$Title, [string]$Step, [string[]]$Lines, [string[]]$Items, [string]$ErrorText)
    $index = 0
    while ($true) {
        Show-ComputerWizardScreen -Title $Title -Step $Step -Lines $Lines
        if ($ErrorText) { Write-Host $ErrorText -ForegroundColor Red; Write-Host '' }
        for ($i = 0; $i -lt $Items.Count; $i++) {
            $marker = if ($i -eq $index) { '>' } else { ' ' }
            $color = if ($i -eq $index) { 'Magenta' } else { 'Gray' }
            Write-Host "$marker $($Items[$i])" -ForegroundColor $color
        }
        switch ([Console]::ReadKey($true).Key) {
            UpArrow { $index = ($index + $Items.Count - 1) % $Items.Count }
            DownArrow { $index = ($index + 1) % $Items.Count }
            Enter { return $index }
            Escape { return -1 }
        }
    }
}

function Read-ComputerWizardText {
    param([string]$Initial)
    $value = $Initial
    Write-Host "> $value" -NoNewline -ForegroundColor Cyan
    while ($true) {
        $key = [Console]::ReadKey($true)
        switch ($key.Key) {
            Enter { Write-Host ''; return [pscustomobject]@{ Back = $false; Value = $value } }
            Escape { Write-Host ''; return [pscustomobject]@{ Back = $true; Value = $null } }
            Backspace { if ($value.Length) { $value = $value.Substring(0, $value.Length - 1); Write-Host "`b `b" -NoNewline } }
            default { if (-not [char]::IsControl($key.KeyChar) -and $value.Length -lt 128) { $value += $key.KeyChar; Write-Host $key.KeyChar -NoNewline } }
        }
    }
}

function Request-ComputerWizardRestart {
    param([string[]]$Lines)
    $choice = Read-ComputerWizardChoice -Title 'Перезагрузка' -Lines $Lines -Items @('Вернуться в меню; перезагрузить позже', 'Перезагрузить сейчас')
    if ($choice -eq 1) { Restart-Computer -ErrorAction Stop }
}

function Confirm-ComputerWizardState {
    param($State)
    $receipt = $State.Receipt
    if ($receipt -and $receipt.Boot -ne $State.Boot) {
        $matches = $State.Name -eq $receipt.Name -and $State.PartOfDomain -eq [bool][int]$receipt.PartOfDomain -and $State.Domain -eq $receipt.Domain
        $connectionError = ''
        if ($matches -and $State.PartOfDomain) {
            try {
                if (-not (Test-ComputerSecureChannel -ErrorAction Stop)) { throw 'Доверительный канал с доменом не подтверждён.' }
            }
            catch { $matches = $false; $connectionError = $_.Exception.Message }
        }
        if ($matches) {
            Clear-ComputerChangeReceipt
            Write-Host 'Изменение после перезагрузки подтверждено.' -ForegroundColor Green
            $State.Receipt = $null
        }
        else {
            $choice = Read-ComputerWizardChoice -Title 'Проверка результата' -Lines @(
                'Ожидаемое состояние не подтверждено. Предыдущее действие могло выполниться частично.',
                "Ожидалось: $($receipt.Name); $($receipt.Domain); член домена: $([bool][int]$receipt.PartOfDomain)",
                "Сейчас: $($State.Name); $($State.Domain); член домена: $($State.PartOfDomain)"
            ) -ErrorText $connectionError -Items @('Вернуться в меню', 'Подтвердить просмотр результата и разрешить повторную настройку')
            if ($choice -ne 1) { return $false }
            Clear-ComputerChangeReceipt
            $State.Receipt = $null
        }
    }
    if ($State.PendingName -or $State.PendingDomain -or $State.Receipt) {
        Request-ComputerWizardRestart -Lines @('Сначала завершите предыдущее изменение перезагрузкой.', "Текущее имя: $($State.Name)", "Ожидающее имя: $($State.PendingName)")
        return $false
    }
    return $true
}

function Assert-InventoryReadyForRename {
    $system = Get-CimInstance Win32_ComputerSystem -ErrorAction Stop
    $uuid = (Get-CimInstance Win32_ComputerSystemProduct -ErrorAction Stop).UUID
    $serial = (Get-CimInstance Win32_BIOS -ErrorAction Stop).SerialNumber
    $identity = Get-InventoryIdentity -Uuid $uuid -Serial $serial -InstallationId (Get-WindowsInstallationId) -DataPath (Join-Path $PSScriptRoot 'data')
    if (-not $identity.Path) {
        $legacy = @(Find-UnboundLegacyInventory -DataPath (Join-Path $PSScriptRoot 'data') -Hostname $system.Name)
        if ($legacy.Count) { throw 'Есть старая запись без устойчивого ID. Сначала выполните «Собрать информацию о компьютере» и подтвердите её привязку, затем изменяйте имя.' }
    }
}

function Invoke-ComputerManagementWizard {
    param([ValidateSet('Rename','Domain')][string]$Mode)
    $policy = Get-ComputerPolicy
    Assert-ComputerPolicy $policy
    $state = Get-ComputerManagementState
    if (-not (Confirm-ComputerWizardState $state)) { return }
    Assert-ComputerChangeAllowed $state
    if ($state.PartOfDomain -and $state.Domain -ne $policy.DomainName) { throw 'Компьютер состоит в другом домене. Автоматический перенос не поддерживается.' }
    $operation = 'Rename'
    $prefix = $policy.InternetPrefix
    $name = ''
    $domainCredential = $null
    $localCredential = $null
    $localName = ''
    $errorText = ''
    $step = 0
    try {
        while ($true) {
            $title = if ($Mode -eq 'Rename') { 'Имя компьютера' } else { 'Домен' }
            $context = @("Сейчас: $($state.Name)", "Домен / рабочая группа: $($state.Domain); член домена: $($state.PartOfDomain)")
            switch ($step) {
                0 {
                    if ($Mode -eq 'Domain') {
                        $action = if ($state.PartOfDomain) { "Вывести из $($policy.DomainName)" } else { "Ввести в $($policy.DomainName)" }
                        $choice = Read-ComputerWizardChoice -Title $title -Step 'Шаг 1: действие' -Lines $context -Items @($action, 'Вернуться в главное меню')
                        if ($choice -ne 0) { return }
                        $operation = if ($state.PartOfDomain) { 'Leave' } else { 'Join' }
                        $prefix = $policy.DomainPrefix
                        $step = if ($operation -eq 'Leave') { 2 } else { 1 }
                    }
                    else {
                        $prefixes = if ($state.PartOfDomain) { @($policy.DomainPrefix) } else { @($policy.InternetPrefix, $policy.DomainPrefix) }
                        if ($prefixes.Count -eq 1) { $prefix = $prefixes[0]; $step = 1; continue }
                        $choice = Read-ComputerWizardChoice -Title $title -Step 'Шаг 1: назначение сети' -Lines $context -Items @("$($policy.InternetPrefix) — интернет", "$($policy.DomainPrefix) — локальная доменная сеть", 'Вернуться в главное меню')
                        if ($choice -notin @(0,1)) { return }
                        $prefix = $prefixes[$choice]; $step = 1
                    }
                }
                1 {
                    if (-not $name -or -not $name.StartsWith($prefix + '-')) {
                        $ip = (Get-NetIPAddress -AddressFamily IPv4 -ErrorAction SilentlyContinue | Where-Object {
                            $_.IPAddress -notmatch '^(127\.|169\.254\.)' -and $_.InterfaceAlias -notmatch 'Loopback|vEthernet|VPN'
                        } | Select-Object -First 1).IPAddress
                        $name = Get-SuggestedComputerName -CurrentName $state.Name -Prefix $prefix -Policy $policy -IpAddress $ip
                    }
                    $start = "$prefix-$($policy.UnitCode)-"
                    Show-ComputerWizardScreen -Title $title -Step 'Шаг 2: обозначение компьютера' -Lines @(
                        "Префикс: $start", 'Введите фамилию, отдел с номером или другое обозначение.',
                        "Максимум $((15 - $start.Length)) символов после транслитерации. Полное имя: $name"
                    )
                    if ($errorText) { Write-Host $errorText -ForegroundColor Red; $errorText = '' }
                    $inputValue = Read-ComputerWizardText -Initial $name.Substring($start.Length)
                    if ($inputValue.Back) {
                        if ($Mode -eq 'Rename' -and $state.PartOfDomain) { return }
                        $step = 0; continue
                    }
                    $candidate = $start + (ConvertTo-ComputerDesignation $inputValue.Value)
                    $errorText = Get-ComputerNameIssue -Name $candidate -Prefix $prefix -Policy $policy
                    if ($errorText) { continue }
                    $name = $candidate
                    $step = 3
                }
                2 {
                    $choice = Read-ComputerWizardChoice -Title $title -Step 'Шаг 2: локальный вход' -Lines @(
                        'Потребуется пароль включённого локального администратора.',
                        'Доменные профили не преобразуются в локальные; текущее имя сохранится.'
                    ) -ErrorText $errorText -Items @('Проверить локальный вход', 'Назад')
                    $errorText = ''
                    if ($choice -ne 0) { $step = 0; continue }
                    $localCredential = Get-Credential -Message 'Локальный администратор: .\имя. Пароль не сохраняется.'
                    if ($null -eq $localCredential) { $step = 0; continue }
                    try { $localName = Test-LocalAdministratorCredential -Credential $localCredential -ComputerName $state.Name }
                    catch { $errorText = $_.Exception.Message; continue }
                    $step = 3
                }
                3 {
                    if ($operation -eq 'Rename' -and -not $state.PartOfDomain) { $step = 4; continue }
                    $choice = Read-ComputerWizardChoice -Title $title -Step 'Шаг 3: доменные учётные данные' -Lines @(
                        "Домен: $($policy.DomainName)", 'Нужна учётная запись с правом выполнения операции; пароль не сохраняется.'
                    ) -ErrorText $errorText -Items @('Проверить доступность домена и ввести учётные данные', 'Назад')
                    $errorText = ''
                    if ($choice -ne 0) { $step = if ($operation -eq 'Leave') { 2 } else { 1 }; continue }
                    try { [void](Test-ComputerDomainConnection $policy.DomainName) }
                    catch { $errorText = $_.Exception.Message; continue }
                    $domainCredential = Get-Credential -Message "Учётная запись для $($policy.DomainName)"
                    if ($null -eq $domainCredential) { continue }
                    $step = 4
                }
                4 {
                    $targetName = if ($operation -eq 'Leave') { $state.Name } else { $name }
                    $targetDomain = if ($operation -eq 'Join') { $policy.DomainName } elseif ($operation -eq 'Leave') { $policy.WorkgroupName } else { $state.Domain }
                    $choice = Read-ComputerWizardChoice -Title $title -Step 'Последний шаг: проверьте изменения' -Lines @(
                        "Действие: $(switch ($operation) { Rename { 'Переименование' } Join { 'Ввод в домен' } Leave { 'Вывод из домена' } })",
                        "Имя: $($state.Name) -> $targetName", "Домен / рабочая группа: $($state.Domain) -> $targetDomain",
                        "Локальный вход: $localName", 'Потребуется перезагрузка. Новое состояние будет проверено при следующем открытии мастера.'
                    ) -Items @('Применить изменения', 'Назад')
                    if ($choice -ne 0) { $step = if ($operation -eq 'Leave') { 2 } else { 1 }; continue }
                    if (-not (Test-ComputerAdministrator)) {
                        $elevated = Start-ComputerManagement -Elevate
                        if (-not $elevated) { return }
                    }
                    if ($operation -ne 'Leave') { Assert-InventoryReadyForRename }
                    if (Invoke-ComputerManagementChange -Operation $operation -Name $name -Policy $policy -DomainCredential $domainCredential -LocalCredential $localCredential) {
                        Request-ComputerWizardRestart -Lines @(
                            'Windows подтвердила команду. Для завершения нужна перезагрузка.',
                            "Ожидаемое имя: $targetName; домен / группа: $targetDomain",
                            'После перезагрузки откройте этот раздел для проверки, затем соберите сведения о компьютере.'
                        )
                    }
                    else { Write-Host 'Компьютер уже имеет это имя. Изменения не требуются.' -ForegroundColor Green }
                    return
                }
            }
        }
    }
    finally { $domainCredential = $null; $localCredential = $null }
}
