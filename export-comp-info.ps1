param(
    [string]$DataPath = (Join-Path $PSScriptRoot "data"),
    [string]$OutputPath = (Join-Path $PSScriptRoot ("exports\devices_{0}.csv" -f (Get-Date -Format "yyyy-MM-dd_HH-mm-ss-fff"))),
    [char]$Delimiter = ';'
)

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "eset-info.ps1")
. (Join-Path $PSScriptRoot "sync-merge.ps1")
. (Join-Path $PSScriptRoot "cloud-client.ps1")
[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new()
[Console]::InputEncoding = [System.Text.UTF8Encoding]::new()

try {
    if (-not (Test-Path -LiteralPath $DataPath -PathType Container)) {
        throw "Папка с данными не найдена: $DataPath"
    }

    $files = @(Get-ChildItem -LiteralPath $DataPath -Filter '*.json' -File | Sort-Object Name)
    if ($files.Count -eq 0) {
        throw "В папке нет JSON-файлов: $DataPath"
    }

    $cloudState = Get-CloudStateForDisplay $PSScriptRoot

    # Read everything before writing: an invalid record must not produce a partial export.
    $records = @(foreach ($file in $files) {
        try {
            $record = Get-Content -LiteralPath $file.FullName -Raw -Encoding UTF8 | ConvertFrom-Json
            if ($null -eq $record -or $record -isnot [pscustomobject]) {
                throw "Ожидался JSON-объект устройства."
            }
            # The status is computed from the record as saved, before export-only changes.
            $cloudStatus = Get-CloudStatus $record $file.Name $cloudState
            [void](Remove-DeprecatedFields $record)
            Initialize-EsetInventoryField $record
            $record | Add-Member -NotePropertyName cloudStatus -NotePropertyValue $cloudStatus -Force
            $record
        }
        catch {
            throw "$($file.Name): $($_.Exception.Message)"
        }
    })

    # Use the union of fields so optional properties in later files are retained.
    $columns = New-Object 'System.Collections.Generic.List[string]'
    foreach ($record in $records) {
        foreach ($property in $record.PSObject.Properties) {
            if (-not $columns.Contains($property.Name)) {
                $columns.Add($property.Name)
            }
        }
    }
    if ($columns.Count -eq 0) { throw "В записях устройств нет полей для экспорта." }

    $rows = @(foreach ($record in ($records | Sort-Object hostname, serial)) {
        $row = [ordered]@{}
        foreach ($column in $columns) {
            $value = $record.$column
            if ($null -ne $value -and ($value -is [pscustomobject] -or $value -is [System.Collections.IEnumerable] -and $value -isnot [string])) {
                $value = ConvertTo-Json -InputObject $value -Depth 100 -Compress
            }
            $row[$column] = $value
        }
        [pscustomobject]$row
    })

    $destination = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputPath)
    $parent = Split-Path -Parent $destination
    [void][System.IO.Directory]::CreateDirectory($parent)
    $csv = $rows | ConvertTo-Csv -NoTypeInformation -Delimiter $Delimiter
    # CreateNew protects an existing export, including when two launches overlap.
    $stream = [System.IO.File]::Open($destination, [System.IO.FileMode]::CreateNew, [System.IO.FileAccess]::Write)
    try {
        $writer = [System.IO.StreamWriter]::new($stream, [System.Text.UTF8Encoding]::new($true))
        try {
            foreach ($line in $csv) { $writer.WriteLine($line) }
        }
        finally { $writer.Dispose() }
    }
    finally { $stream.Dispose() }

    Write-Host "Экспортировано устройств: $($rows.Count)" -ForegroundColor Green
    Write-Host "CSV: $destination" -ForegroundColor Cyan
}
catch {
    Write-Error "Экспорт не выполнен: $($_.Exception.Message)" -ErrorAction Continue
    exit 1
}
