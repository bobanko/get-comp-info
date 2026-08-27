$ErrorActionPreference = "Stop"

$dataPath = Join-Path $PSScriptRoot "data"
$script:sortMode = "Hostname"
$script:searchTerm = ""
$script:notice = ""

function Get-DisplayValue($value) {
    if ($null -eq $value -or [string]::IsNullOrWhiteSpace([string]$value)) {
        return "(not set)"
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

            $records += [pscustomobject]@{
                FilePath = $file.FullName
                FileName = $file.Name
                Data     = $computer
            }
        }
        catch {
            $warnings += "$($file.Name): $($_.Exception.Message)"
        }
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
        "Serial" { return @($result | Sort-Object { Get-DisplayValue $_.Data.serial }) }
        "CollectedAt" {
            return @($result | Sort-Object {
                $date = [datetime]::MinValue
                [void][datetime]::TryParse([string]$_.Data.collectedAt, [ref]$date)
                $date
            } -Descending)
        }
        default { return @($result | Sort-Object { Get-DisplayValue $_.Data.hostname }) }
    }
}

function Clear-Screen {
    try { [Console]::Clear() } catch { Clear-Host }
}

function Show-List($records, $selectedIndex, $warningCount) {
    Clear-Screen
    Write-Host "Computer inventory viewer" -ForegroundColor Cyan
    Write-Host "Records: $($records.Count) | Sort: $script:sortMode" -ForegroundColor DarkGray
    if ($script:searchTerm) {
        Write-Host "Search: $script:searchTerm" -ForegroundColor Yellow
    }
    if ($warningCount -gt 0) {
        Write-Host "Skipped invalid JSON files: $warningCount" -ForegroundColor Yellow
    }
    if ($script:notice) {
        Write-Host $script:notice -ForegroundColor Green
        $script:notice = ""
    }
    Write-Host ""

    if ($records.Count -eq 0) {
        Write-Host "No records found. Press R to reload or Esc to exit." -ForegroundColor Yellow
        return
    }

    Write-Host "Up/Down: select  Enter: open  /: search  S: sort  R: reload  Esc: exit" -ForegroundColor DarkGray
    Write-Host ""
    Write-Host ("{0,-3} {1,-24} {2,-22} {3}" -f "", "Hostname", "Serial", "User") -ForegroundColor DarkGray

    $pageSize = [Math]::Max(1, [Console]::WindowHeight - 9)
    $pageStart = [Math]::Floor($selectedIndex / $pageSize) * $pageSize
    $pageEnd = [Math]::Min($records.Count - 1, $pageStart + $pageSize - 1)

    for ($index = $pageStart; $index -le $pageEnd; $index++) {
        $record = $records[$index]
        $marker = if ($index -eq $selectedIndex) { ">" } else { " " }
        $line = "{0,-3} {1,-24} {2,-22} {3}" -f $marker,
            (Get-ShortValue $record.Data.hostname 24),
            (Get-ShortValue $record.Data.serial 22),
            (Get-ShortValue $record.Data.username 24)
        Write-Host $line -ForegroundColor $(if ($index -eq $selectedIndex) { "Cyan" } else { "Gray" })
    }

    if ($records.Count -gt $pageSize) {
        Write-Host ""; Write-Host "Showing $($pageStart + 1)-$($pageEnd + 1) of $($records.Count)" -ForegroundColor DarkGray
    }
}

function Edit-Comment($initialValue) {
    $value = if ($null -eq $initialValue) { "" } else { [string]$initialValue }
    $cursor = $value.Length

    while ($true) {
        Clear-Screen
        Write-Host "Edit comment" -ForegroundColor Cyan
        Write-Host "Left/Right: move cursor  Home/End: beginning/end  Enter: save  Esc: cancel" -ForegroundColor DarkGray
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

function Show-Details($records, $selectedIndex) {
    while ($true) {
        $record = $records[$selectedIndex]
        $data = $record.Data
        Clear-Screen
        Write-Host "Computer details ($($selectedIndex + 1) of $($records.Count))" -ForegroundColor Cyan
        Write-Host "Left/Right or Up/Down: previous/next  E/F2: edit comment  Esc: list" -ForegroundColor DarkGray
        Write-Host ""
        Write-Host ("{0,-15} {1}" -f "Hostname:", (Get-DisplayValue $data.hostname))
        Write-Host ("{0,-15} {1}" -f "Serial:", (Get-DisplayValue $data.serial))
        $computerModel = @($data.manufacturer, $data.model | Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) }) -join " "
        Write-Host ("{0,-15} {1}" -f "Computer:", (Get-DisplayValue $computerModel))
        Write-Host ("{0,-15} {1}" -f "User:", (Get-DisplayValue $data.username))
        Write-Host ("{0,-15} {1}" -f "IP:", (Get-DisplayValue $data.ip))
        Write-Host ("{0,-15} {1}" -f "MAC:", (Get-DisplayValue $data.mac))
        Write-Host ""
        Write-Host "Comment:" -ForegroundColor DarkGray
        Write-Host (Get-DisplayValue $data.comment)
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
                if ($key.KeyChar -eq 'e' -or $key.KeyChar -eq 'E') {
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

$selectedIndex = 0
while ($true) {
    $inventory = Get-InventoryRecords
    $records = Get-VisibleRecords $inventory.Records
    if ($selectedIndex -ge $records.Count) { $selectedIndex = [Math]::Max(0, $records.Count - 1) }

    Show-List $records $selectedIndex $inventory.Warnings.Count
    $key = [Console]::ReadKey($true)

    switch ($key.Key) {
        "Escape" { break }
        "UpArrow" { if ($selectedIndex -gt 0) { $selectedIndex-- }; continue }
        "DownArrow" { if ($selectedIndex -lt $records.Count - 1) { $selectedIndex++ }; continue }
        "Enter" {
            if ($records.Count -gt 0) { $selectedIndex = Show-Details $records $selectedIndex }
            continue
        }
        default {
            if ($key.KeyChar -eq '/') {
                Clear-Screen
                $current = if ($script:searchTerm) { " (current: $script:searchTerm)" } else { "" }
                $script:searchTerm = Read-Host "Search hostname, serial, user, model or comment$current (empty clears)"
                $selectedIndex = 0
            }
            elseif ($key.KeyChar -eq 's' -or $key.KeyChar -eq 'S') {
                switch ($script:sortMode) {
                    "Hostname" { $script:sortMode = "Serial" }
                    "Serial" { $script:sortMode = "CollectedAt" }
                    default { $script:sortMode = "Hostname" }
                }
                $selectedIndex = 0
            }
        }
    }
}
