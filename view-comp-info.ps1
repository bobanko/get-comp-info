$ErrorActionPreference = "Stop"

. (Join-Path $PSScriptRoot "console-ui.ps1")
. (Join-Path $PSScriptRoot "eset-info.ps1")

$dataPath = Join-Path $PSScriptRoot "data"
$script:sortMode = "Hostname"
$script:sortDescending = $false
$script:searchTerm = ""
$script:notice = ""
$script:listColumnWidths = @{
    Marker = 3
    Hostname = 16
    IP = 7
    Serial = 22
    User = 12
    Collected = 16
    DB = 10
    ESET = 14
    Comment = 40
}

function Get-IdentityUuid($value) {
    $uuid = [guid]::Empty
    if ([guid]::TryParse(([string]$value).Trim(), [ref]$uuid) -and
        $uuid -ne [guid]::Empty -and $uuid.ToString() -ne "ffffffff-ffff-ffff-ffff-ffffffffffff") {
        return $uuid.ToString()
    }
    return ""
}

function Get-IdentitySerial($value) {
    $serial = ([string]$value).Trim()
    if ($serial -in @("", "To be filled by O.E.M.", "Default string", "System Serial Number", "None", "Unknown", "0")) {
        return ""
    }
    return $serial
}

function Get-CurrentComputerIdentity {
    $uuid = ""
    $serial = ""
    # Read hardware identifiers once; the viewer still works when CIM is unavailable.
    try { $uuid = Get-IdentityUuid (Get-CimInstance Win32_ComputerSystemProduct -ErrorAction Stop).UUID } catch { }
    try { $serial = Get-IdentitySerial (Get-CimInstance Win32_BIOS -ErrorAction Stop).SerialNumber } catch { }
    return [pscustomobject]@{
        Uuid = $uuid
        Serial = $serial
        Hostname = ([string]$env:COMPUTERNAME).Trim()
    }
}

function Test-CurrentComputer($data) {
    $uuid = Get-IdentityUuid $data.smbiosUuid
    if (-not $uuid -and $data.computerIdSource -eq "smbiosUuid") {
        $uuid = Get-IdentityUuid $data.computerId
    }
    if ($uuid -and $script:currentComputer.Uuid) {
        return $uuid -eq $script:currentComputer.Uuid
    }

    $serial = Get-IdentitySerial $data.serial
    if (-not $serial -and $data.computerIdSource -eq "serial") {
        $serial = Get-IdentitySerial $data.computerId
    }
    if ($serial -and $script:currentComputer.Serial) {
        return $serial -eq $script:currentComputer.Serial
    }

    $hostname = ([string]$data.hostname).Trim()
    return $hostname -ne "" -and $hostname -eq $script:currentComputer.Hostname
}

function Get-DisplayValue($value) {
    if ($null -eq $value -or [string]::IsNullOrWhiteSpace([string]$value)) {
        return "not set"
    }

    return [string]$value
}

function Get-ShortValue($value, $length) {
    $text = Get-DisplayValue $value
    if ($text.Length -le $length) {
        return $text
    }

    return $text.Substring(0, [Math]::Max(0, $length - 3)) + "..."
}

function Get-ShortIpAddress($value) {
    $text = Get-DisplayValue $value
    $octets = $text -split '\.'

    if ($octets.Count -eq 4) {
        return $octets[-2..-1] -join "."
    }

    return $text
}

function Get-IpSortValue($value) {
    $octets = ([string]$value).Trim() -split '\.'
    if ($octets.Count -ne 4) { return [long]::MaxValue }

    $number = 0L
    foreach ($octet in $octets) {
        if ($octet -notmatch '^\d{1,3}$' -or [int]$octet -gt 255) {
            return [long]::MaxValue
        }
        $number = $number * 256 + [int]$octet
    }
    return $number
}

function Get-DisplayDate($value) {
    $date = [datetime]::MinValue
    if ([datetime]::TryParse([string]$value, [ref]$date)) {
        return $date.ToString("yyyy-MM-dd HH:mm")
    }

    return "(not set)"
}

function Test-AddedToExternalDatabase($data) {
    return $null -ne $data.PSObject.Properties["addedToExternalDatabase"] -and [bool]$data.addedToExternalDatabase
}

function Get-ExternalDatabaseStatus($data) {
    if (Test-AddedToExternalDatabase $data) {
        return "Added"
    }

    return "Pending"
}

function Format-ListCell($value, $column) {
    $width = $script:listColumnWidths[$column]
    return (Get-ShortValue $value $width).PadRight($width) + " "
}

function Write-ListHeader($title, $sortMode) {
    $color = if ($script:sortMode -eq $sortMode) { "Cyan" } else { "DarkGray" }
    $label = $title
    if ($script:sortMode -eq $sortMode) {
        $arrow = if ($script:sortDescending) { [char]0x2193 } else { [char]0x2191 }
        $label = "$title $arrow"
    }
    Write-Host (Format-ListCell $label $title) -NoNewline -ForegroundColor $color
}

function Switch-ListSort {
    param([switch]$Reverse)

    $modes = @('Hostname', 'IP', 'Serial', 'Username', 'CollectedAt', 'DB')
    $stateCount = $modes.Count * 2
    $state = [array]::IndexOf($modes, $script:sortMode) * 2 + [int]$script:sortDescending
    $step = if ($Reverse) { -1 } else { 1 }
    $state = ($state + $step + $stateCount) % $stateCount
    $script:sortMode = $modes[[int][Math]::Floor($state / 2)]
    $script:sortDescending = ($state % 2) -eq 1
}
function Get-InventoryRecords {
    $records = @()
    $warnings = @()

    if (-not (Test-Path -LiteralPath $dataPath)) {
        return [pscustomobject]@{ Records = @(); Warnings = @("Folder data was not found: $dataPath") }
    }

    foreach ($file in Get-ChildItem -LiteralPath $dataPath -Filter "*.json" -File) {
        try {
            $computer = Get-Content -Raw -LiteralPath $file.FullName | ConvertFrom-Json -ErrorAction Stop
            if ($null -eq $computer) {
                throw "The JSON file is empty."
            }

            Initialize-EsetInventoryField $computer
            $records += [pscustomobject]@{
                FilePath = $file.FullName
                FileName = $file.Name
                Data     = $computer
                HasIpConflict = $false
            }
        }
        catch {
            $warnings += "$($file.Name): $($_.Exception.Message)"
        }
    }

    # Compare full addresses across the inventory before search and pagination.
    $ipCounts = @{}
    foreach ($record in $records) {
        $ip = ([string]$record.Data.ip).Trim()
        if ($ip) { $ipCounts[$ip]++ }
    }
    foreach ($record in $records) {
        $ip = ([string]$record.Data.ip).Trim()
        $record.HasIpConflict = $ip -ne "" -and $ipCounts[$ip] -gt 1
    }

    return [pscustomobject]@{ Records = @($records); Warnings = @($warnings) }
}

function Get-VisibleRecords($records) {
    $result = @($records)

    if (-not [string]::IsNullOrWhiteSpace($script:searchTerm)) {
        $needle = $script:searchTerm.Trim()
        $result = @($result | Where-Object {
            $haystack = @(
                $_.Data.hostname, $_.Data.serial, $_.Data.username,
                $_.Data.manufacturer, $_.Data.model, $_.Data.comment
            ) -join " `n"
            $haystack.IndexOf($needle, [System.StringComparison]::OrdinalIgnoreCase) -ge 0
        })
    }

    switch ($script:sortMode) {
        "IP" { return @($result | Sort-Object -Descending:$script:sortDescending { Get-IpSortValue $_.Data.ip }, { Get-DisplayValue $_.Data.hostname }) }
        "Username" { return @($result | Sort-Object -Descending:$script:sortDescending { [string]::IsNullOrWhiteSpace([string]$_.Data.username) }, { [string]$_.Data.username }, { Get-DisplayValue $_.Data.hostname }) }
        "DB" { return @($result | Sort-Object -Descending:$script:sortDescending { Test-AddedToExternalDatabase $_.Data }, { Get-DisplayValue $_.Data.hostname }) }
        "Serial" { return @($result | Sort-Object -Descending:$script:sortDescending { Get-DisplayValue $_.Data.serial }) }
        "CollectedAt" {
            return @($result | Sort-Object -Descending:$script:sortDescending {
                $date = [datetime]::MinValue
                [void][datetime]::TryParse([string]$_.Data.collectedAt, [ref]$date)
                $date
            })
        }
        default { return @($result | Sort-Object -Descending:$script:sortDescending { Get-DisplayValue $_.Data.hostname }) }
    }
}

function Clear-Screen {
    try { [Console]::Clear() } catch { Clear-Host }
}

function Show-List($records, $selectedIndex, $warningCount) {
    Clear-Screen
    Write-ToolboxHeader -Section "Computer inventory viewer"
    $direction = if ($script:sortDescending) { "DESC" } else { "ASC" }
    Write-Host "Records: $($records.Count) | Sort: $script:sortMode $direction" -ForegroundColor DarkGray
    if ($script:searchTerm) {
        Write-Host "Search: $script:searchTerm" -ForegroundColor Yellow
    }
    if ($warningCount -gt 0) {
        Write-Host "Skipped invalid JSON files: $warningCount" -ForegroundColor Yellow
    }
    Write-Host ""

    if ($records.Count -eq 0) {
        Write-ToolboxHint "No records found. Press [R] to reload or [Esc] to exit."
        return
    }

    Write-ToolboxHint "[↑↓]: select  [↲ Enter]: open  [A]: toggle external DB status  [/]: search  [S]/[Shift+S]: sort next/previous  [R]: reload  [Esc]: exit"
    Write-Host "* — текущий компьютер" -ForegroundColor Green
    Write-Host (" " * ($script:listColumnWidths.Marker + 1)) -NoNewline -ForegroundColor DarkGray
    Write-ListHeader "Hostname" "Hostname"
    Write-ListHeader "IP" "IP"
    Write-ListHeader "Serial" "Serial"
    Write-ListHeader "User" "Username"
    Write-ListHeader "Collected" "CollectedAt"
    Write-ListHeader "DB" "DB"
    Write-Host (Format-ListCell 'ESET' 'ESET') -NoNewline -ForegroundColor DarkGray
    Write-Host "Comment" -ForegroundColor DarkGray

    # Reserve two lines at the bottom for a possible status message.
    $pageSize = [Math]::Max(1, [Console]::WindowHeight - 10)
    $pageStart = [Math]::Floor($selectedIndex / $pageSize) * $pageSize
    $pageEnd = [Math]::Min($records.Count - 1, $pageStart + $pageSize - 1)

    for ($index = $pageStart; $index -le $pageEnd; $index++) {
        $record = $records[$index]
        $isCurrentComputer = Test-CurrentComputer $record.Data
        $marker = if ($index -eq $selectedIndex) { ">" } else { " " }
        if ($isCurrentComputer) { $marker += "*" }
        $rowColor = if ($isCurrentComputer) { "Green" } elseif ($index -eq $selectedIndex) { "Cyan" } else { "Gray" }
        Write-Host ($marker.PadRight($script:listColumnWidths.Marker) + " " + (Format-ListCell $record.Data.hostname "Hostname")) -NoNewline -ForegroundColor $rowColor
        $ipColor = if ($record.HasIpConflict) { "Red" } elseif ([string]::IsNullOrWhiteSpace([string]$record.Data.ip)) { "DarkGray" } else { $rowColor }
        Write-Host (Format-ListCell (Get-ShortIpAddress $record.Data.ip) "IP") -NoNewline -ForegroundColor $ipColor
        $line = @(
            (Format-ListCell $record.Data.serial "Serial")
            (Format-ListCell $record.Data.username "User")
            (Format-ListCell (Get-DisplayDate $record.Data.collectedAt) "Collected")
        ) -join ""
        Write-Host $line -NoNewline -ForegroundColor $rowColor
        Write-Host (Format-ListCell (Get-ExternalDatabaseStatus $record.Data) "DB") -NoNewline -ForegroundColor $(if (Test-AddedToExternalDatabase $record.Data) { "Green" } else { "DarkYellow" })
        Write-Host (Format-ListCell (Get-EsetDisplayValue $record.Data.esetInstalled) 'ESET') -NoNewline -ForegroundColor $(if ($null -eq $record.Data.esetInstalled) { 'DarkGray' } elseif ($record.Data.esetInstalled) { 'Green' } else { 'Yellow' })
        Write-Host (Get-ShortValue $record.Data.comment $script:listColumnWidths.Comment) -ForegroundColor $rowColor
    }

    if ($records.Count -gt $pageSize) {
        Write-Host ""; Write-Host "Showing $($pageStart + 1)-$($pageEnd + 1) of $($records.Count)" -ForegroundColor DarkGray
    }

    if ($script:notice) {
        Write-Host ""
        Write-Host $script:notice -ForegroundColor Green
        $script:notice = ""
    }
}

function Edit-Comment($initialValue) {
    $value = if ($null -eq $initialValue) { "" } else { [string]$initialValue }
    $cursor = $value.Length

    while ($true) {
        Clear-Screen
        Write-ToolboxHeader -Section "Edit comment"
        Write-ToolboxHint "[←→]: move cursor  [Home]/[End]: beginning/end  [↲ Enter]: save  [Esc]: cancel"
        Write-Host ""
        Write-Host "Comment: $value"

        $promptLength = 9
        try {
            [Console]::CursorLeft = [Math]::Min([Console]::WindowWidth - 1, $promptLength + $cursor)
            [Console]::CursorTop = 3
        } catch { }

        $key = [Console]::ReadKey($true)
        switch ($key.Key) {
            "Enter" { return [pscustomobject]@{ Saved = $true; Value = $value } }
            "Escape" { return [pscustomobject]@{ Saved = $false; Value = $initialValue } }
            "LeftArrow" { if ($cursor -gt 0) { $cursor-- }; break }
            "RightArrow" { if ($cursor -lt $value.Length) { $cursor++ }; break }
            "Home" { $cursor = 0; break }
            "End" { $cursor = $value.Length; break }
            "Backspace" {
                if ($cursor -gt 0) {
                    $value = $value.Remove($cursor - 1, 1)
                    $cursor--
                }
                break
            }
            "Delete" {
                if ($cursor -lt $value.Length) { $value = $value.Remove($cursor, 1) }
                break
            }
            default {
                if (-not [char]::IsControl($key.KeyChar)) {
                    $value = $value.Insert($cursor, [string]$key.KeyChar)
                    $cursor++
                }
            }
        }
    }
}

function Save-Comment($record, $comment) {
    if ($null -eq $record.Data.PSObject.Properties["comment"]) {
        $record.Data | Add-Member -MemberType NoteProperty -Name "comment" -Value $comment
    }
    else {
        $record.Data.comment = $comment
    }
    $json = $record.Data | ConvertTo-Json -Depth 10
    Set-Content -LiteralPath $record.FilePath -Value $json -Encoding UTF8
}

function Save-ExternalDatabaseStatus($record, $isAdded) {
    if ($null -eq $record.Data.PSObject.Properties["addedToExternalDatabase"]) {
        $record.Data | Add-Member -MemberType NoteProperty -Name "addedToExternalDatabase" -Value $isAdded
    }
    else {
        $record.Data.addedToExternalDatabase = $isAdded
    }

    if ($isAdded) {
        $timestamp = (Get-Date).ToString("o")
        if ($null -eq $record.Data.PSObject.Properties["addedToExternalDatabaseAt"]) {
            $record.Data | Add-Member -MemberType NoteProperty -Name "addedToExternalDatabaseAt" -Value $timestamp
        }
        else {
            $record.Data.addedToExternalDatabaseAt = $timestamp
        }
    }
    elseif ($null -ne $record.Data.PSObject.Properties["addedToExternalDatabaseAt"]) {
        $record.Data.addedToExternalDatabaseAt = $null
    }

    $json = $record.Data | ConvertTo-Json -Depth 10
    Set-Content -LiteralPath $record.FilePath -Value $json -Encoding UTF8
}

function Show-Details($records, $selectedIndex) {
    while ($true) {
        $record = $records[$selectedIndex]
        $data = $record.Data
        Clear-Screen
        Write-ToolboxHeader -Section "Computer details ($($selectedIndex + 1) of $($records.Count))"
        Write-ToolboxHint "[←→]/[↑↓]: previous/next  [E]/[F2]: edit comment  [A]: toggle external DB status  [Esc]: list"
        Write-Host ""
        Write-Host ("{0,-15} {1}" -f "Hostname:", (Get-DisplayValue $data.hostname))
        Write-Host ("{0,-15} {1}" -f "Serial:", (Get-DisplayValue $data.serial))
        $computerModel = @($data.manufacturer, $data.model | Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) }) -join " "
        Write-Host ("{0,-15} {1}" -f "Computer:", (Get-DisplayValue $computerModel))
        Write-Host ("{0,-15} {1}" -f "User:", (Get-DisplayValue $data.username))
        Write-Host ("{0,-15} {1}" -f "IP:", (Get-DisplayValue $data.ip))
        Write-Host ("{0,-15} {1}" -f "MAC:", (Get-DisplayValue $data.mac))
        Write-Host ("{0,-15} {1}" -f "ESET:", (Get-EsetDisplayValue $data.esetInstalled))
        Write-Host ("{0,-15} {1}" -f "Created:", (Get-DisplayDate $data.createdAt))
        Write-Host ("{0,-15} {1}" -f "Collected:", (Get-DisplayDate $data.collectedAt))
        Write-Host ""
        Write-Host "Comment:" -ForegroundColor DarkGray
        Write-Host (Get-DisplayValue $data.comment)
        Write-Host ""
        Write-Host "External database:" -NoNewline -ForegroundColor DarkGray
        Write-Host (" " + (Get-ExternalDatabaseStatus $data)) -ForegroundColor $(if (Test-AddedToExternalDatabase $data) { "Green" } else { "DarkYellow" })
        if (Test-AddedToExternalDatabase $data) {
            Write-Host ("Marked at: " + (Get-DisplayValue $data.addedToExternalDatabaseAt)) -ForegroundColor DarkGray
        }
        Write-Host ""
        Write-Host "File: $($record.FileName)" -ForegroundColor DarkGray

        $key = [Console]::ReadKey($true)
        switch ($key.Key) {
            "Escape" { return $selectedIndex }
            "LeftArrow" { if ($selectedIndex -gt 0) { $selectedIndex-- }; break }
            "UpArrow" { if ($selectedIndex -gt 0) { $selectedIndex-- }; break }
            "RightArrow" { if ($selectedIndex -lt $records.Count - 1) { $selectedIndex++ }; break }
            "DownArrow" { if ($selectedIndex -lt $records.Count - 1) { $selectedIndex++ }; break }
            "F2" {
                $edit = Edit-Comment $data.comment
                if ($edit.Saved) {
                    try {
                        Save-Comment $record $edit.Value
                        $script:notice = "Comment saved: $($record.FileName)"
                    }
                    catch {
                        $script:notice = "Could not save comment: $($_.Exception.Message)"
                    }
                }
                break
            }
            default {
                if ($key.Key -eq [ConsoleKey]::A) {
                    $newStatus = -not (Test-AddedToExternalDatabase $data)
                    try {
                        Save-ExternalDatabaseStatus $record $newStatus
                        $script:notice = if ($newStatus) { "Marked as added to external database: $($record.FileName)" } else { "External database mark removed: $($record.FileName)" }
                    }
                    catch {
                        $script:notice = "Could not save external database status: $($_.Exception.Message)"
                    }
                }
                elseif ($key.Key -eq [ConsoleKey]::E) {
                    $edit = Edit-Comment $data.comment
                    if ($edit.Saved) {
                        try {
                            Save-Comment $record $edit.Value
                            $script:notice = "Comment saved: $($record.FileName)"
                        }
                        catch {
                            $script:notice = "Could not save comment: $($_.Exception.Message)"
                        }
                    }
                }
            }
        }
    }
}

$script:currentComputer = Get-CurrentComputerIdentity
$selectedIndex = 0
$returnToMenu = $false
while (-not $returnToMenu) {
    $inventory = Get-InventoryRecords
    $records = Get-VisibleRecords $inventory.Records
    if ($selectedIndex -ge $records.Count) { $selectedIndex = [Math]::Max(0, $records.Count - 1) }

    Show-List $records $selectedIndex $inventory.Warnings.Count
    $key = [Console]::ReadKey($true)

    switch ($key.Key) {
        "Escape" { $returnToMenu = $true; break }
        "UpArrow" { if ($selectedIndex -gt 0) { $selectedIndex-- }; continue }
        "DownArrow" { if ($selectedIndex -lt $records.Count - 1) { $selectedIndex++ }; continue }
        "Enter" {
            if ($records.Count -gt 0) { $selectedIndex = Show-Details $records $selectedIndex }
            continue
        }
        default {
            if ($key.Key -eq [ConsoleKey]::A -and $records.Count -gt 0) {
                $record = $records[$selectedIndex]
                $newStatus = -not (Test-AddedToExternalDatabase $record.Data)
                try {
                    Save-ExternalDatabaseStatus $record $newStatus
                    $script:notice = if ($newStatus) { "Marked as added to external database: $($record.FileName)" } else { "External database mark removed: $($record.FileName)" }
                }
                catch {
                    $script:notice = "Could not save external database status: $($_.Exception.Message)"
                }
            }
            elseif ($key.Key -eq [ConsoleKey]::Oem2) {
                Clear-Screen
                $current = if ($script:searchTerm) { " (current: $script:searchTerm)" } else { "" }
                $script:searchTerm = Read-Host "Search hostname, serial, user, model or comment$current (empty clears)"
                $selectedIndex = 0
            }
            elseif ($key.Key -eq [ConsoleKey]::S) {
                Switch-ListSort -Reverse:(($key.Modifiers -band [ConsoleModifiers]::Shift) -ne 0)
                $selectedIndex = 0
            }
        }
    }
}

if ($returnToMenu) {
    # The launcher recognizes this as a normal direct return to its menu.
    exit 42
}
