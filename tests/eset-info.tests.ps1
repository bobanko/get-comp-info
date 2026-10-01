$ErrorActionPreference = 'Stop'
$project = Split-Path -Parent $PSScriptRoot
. (Join-Path $project 'eset-info.ps1')
function Assert-True($Value, $Message) { if (-not $Value) { throw $Message } }

function Get-CimInstance {
    param($ClassName, $Filter, $ErrorAction)
    Assert-True ($ClassName -eq 'Win32_Service' -and $Filter -eq "Name='ekrn'") 'Wrong service query'
    if ($script:failQuery) { throw 'Access denied' }
    $script:services | Where-Object Name -eq 'ekrn'
}
$script:failQuery = $false
foreach ($state in @('Running', 'Stopped')) {
    $script:services = @([pscustomobject]@{ Name = 'ekrn'; State = $state })
    $result = Test-EsetInstalled
    Assert-True ($result -is [bool] -and $result) "ESET $state must count as installed"
}
$script:services = @([pscustomobject]@{ Name = 'EraAgent'; State = 'Running' })
$result = Test-EsetInstalled
Assert-True ($result -is [bool] -and -not $result) 'Management Agent alone is not antivirus'
$script:services = @()
Assert-True ((Test-EsetInstalled) -eq $false) 'No ESET must return false'
$script:failQuery = $true
$threw = $false
try { Test-EsetInstalled } catch { $threw = $true }
Assert-True $threw 'Query failure must not be recorded as false'

foreach ($json in @('{"hostname":"old"}', '{"esetInstalled":null}', '{"esetInstalled":false}', '{"esetInstalled":true}')) {
    $record = $json | ConvertFrom-Json
    $original = $record.esetInstalled
    Initialize-EsetInventoryField $record
    Initialize-EsetInventoryField $record
    Assert-True ($null -ne $record.PSObject.Properties['esetInstalled']) 'Legacy field missing'
    $roundTrip = $record | ConvertTo-Json | ConvertFrom-Json
    Assert-True ($roundTrip.esetInstalled -eq $original) 'Boolean/null changed during normalization'
}
Assert-True ((Get-EsetDisplayValue $null) -ne (Get-EsetDisplayValue $false)) 'Unknown and absent must differ'

# Exercise the actual exporter with legacy, true and false records.
$temp = Join-Path ([IO.Path]::GetTempPath()) ('eset-tests-' + [guid]::NewGuid())
[void][IO.Directory]::CreateDirectory($temp)
try {
    '{"hostname":"old"}' | Set-Content (Join-Path $temp 'old.json') -Encoding UTF8
    '{"hostname":"yes","esetInstalled":true}' | Set-Content (Join-Path $temp 'yes.json') -Encoding UTF8
    '{"hostname":"no","esetInstalled":false}' | Set-Content (Join-Path $temp 'no.json') -Encoding UTF8
    $csvPath = Join-Path $temp 'inventory.csv'
    & (Join-Path $project 'export-comp-info.ps1') -DataPath $temp -OutputPath $csvPath
    $rows = @(Import-Csv $csvPath -Delimiter ';')
    Assert-True (($rows | Where-Object hostname -eq 'old').esetInstalled -eq '') 'Legacy CSV must be empty'
    Assert-True (($rows | Where-Object hostname -eq 'yes').esetInstalled -eq 'True') 'True CSV lost'
    Assert-True (($rows | Where-Object hostname -eq 'no').esetInstalled -eq 'False') 'False CSV lost'
}
finally {
    # Delete only the named files created by this test.
    foreach ($name in @('old.json', 'yes.json', 'no.json', 'inventory.csv')) {
        Remove-Item -LiteralPath (Join-Path $temp $name) -Force -ErrorAction SilentlyContinue
    }
    [IO.Directory]::Delete($temp)
}
Write-Host 'ESET inventory tests passed.' -ForegroundColor Green
