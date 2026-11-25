# Writes the FlashStashBee app icon as a 256 px PNG and a multi-size ICO.
param([string]$OutDir = (Join-Path (Split-Path -Parent $PSScriptRoot) 'app'))

$ErrorActionPreference = 'Stop'
$renderer = Join-Path $PSScriptRoot '..\app\wpf\FlashStashBee\TrayIcons.cs'
Add-Type -Path $renderer -ReferencedAssemblies 'System.Drawing.dll'

function Render-Png([int]$size) {
    $bitmap = [CompInfo.Wpf.TrayIcons]::RenderAppIcon($size)
    $stream = New-Object System.IO.MemoryStream
    try {
        $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
        return , $stream.ToArray()
    } finally {
        $stream.Dispose()
        $bitmap.Dispose()
    }
}

$entries = foreach ($size in 16, 20, 24, 32, 40, 48, 64, 128, 256) {
    @{ Size = $size; Bytes = (Render-Png $size) }
}
[System.IO.File]::WriteAllBytes((Join-Path $OutDir 'flashstashbee.png'), $entries[-1].Bytes)


$ms = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter($ms)
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$entries.Count)
$offset = 6 + 16 * $entries.Count
foreach ($e in $entries) {
    $dim = if ($e.Size -ge 256) { 0 } else { $e.Size }
    $bw.Write([byte]$dim); $bw.Write([byte]$dim)
    $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([uint16]1); $bw.Write([uint16]32)
    $bw.Write([uint32]$e.Bytes.Length); $bw.Write([uint32]$offset)
    $offset += $e.Bytes.Length
}
foreach ($e in $entries) { $bw.Write($e.Bytes) }
$bw.Flush()
[System.IO.File]::WriteAllBytes((Join-Path $OutDir 'flashstashbee.ico'), $ms.ToArray())
$bw.Dispose()
Write-Output ("Done: {0}\flashstashbee.png and flashstashbee.ico ({1} sizes)" -f $OutDir, $entries.Count)
