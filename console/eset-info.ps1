# Shared inventory helpers; loading this file does not query the computer.
function Test-EsetInstalled {
    # Query installed services, not running processes. An installed but stopped
    # antivirus still counts; the ESET Management Agent alone does not.
    try {
        $services = @(Get-CimInstance -ClassName Win32_Service -Filter "Name='ekrn'" -ErrorAction Stop)
        return [bool]($services.Count -gt 0)
    }
    catch {
        throw "ESET installation check failed: $($_.Exception.Message)"
    }
}

function Initialize-EsetInventoryField($Record) {
    if ($null -eq $Record.PSObject.Properties['esetInstalled']) {
        $Record | Add-Member -NotePropertyName esetInstalled -NotePropertyValue $null
    }
}

function Get-EsetDisplayValue($Value) {
    if ($null -eq $Value) { return 'Не проверено' }
    if ($Value -eq $true) { return 'Установлен' }
    return 'Не установлен'
}
