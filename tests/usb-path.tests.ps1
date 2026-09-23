$ErrorActionPreference = 'Stop'
$project = Split-Path -Parent $PSScriptRoot
$parseErrors = $null
$ast = [Management.Automation.Language.Parser]::ParseFile((Join-Path $project 'sync-usb.ps1'), [ref]$null, [ref]$parseErrors)
if ($parseErrors.Count) { throw ($parseErrors | Out-String) }
$definition = $ast.Find({ param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Select-UsbProgramPath' }, $true)
. ([scriptblock]::Create($definition.Extent.Text))
function Assert-True($Value, $Message) { if (-not $Value) { throw $Message } }
function Get-UsbProgramPaths { param($LaunchRoot) $script:paths }
function Read-Host {
    param($Prompt)
    if ($script:answers.Count -eq 0) { throw 'Unexpected prompt' }
    $script:answers.Dequeue()
}
$script:answers = New-Object 'System.Collections.Generic.Queue[string]'
$script:paths = @('F:\COMP-INFO')
Assert-True ((Select-UsbProgramPath 'C:\Tools') -eq 'F:\COMP-INFO') 'Single USB path must be automatic'
$script:paths = @('G:\Inventory', 'F:\COMP-INFO')
$script:answers.Enqueue('9')
$script:answers.Enqueue('2')
Assert-True ((Select-UsbProgramPath 'C:\Tools') -eq 'G:\Inventory') 'Multiple drives must use user choice'
$script:paths = @()
$script:answers.Enqueue('H:relative')
$script:answers.Enqueue('"H:\Tools\Inventory"')
Assert-True ((Select-UsbProgramPath 'C:\Tools') -eq 'H:\Tools\Inventory') 'Manual nested path must work'
$script:answers.Enqueue('')
$cancelled = $false
try { $null = Select-UsbProgramPath 'C:\Tools' } catch { $cancelled = $true }
Assert-True $cancelled 'Empty answer must cancel without copying'
'USB path selection checks passed.'
