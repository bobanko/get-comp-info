# Builds a program release archive: the program files plus manifest.json with their SHA-256.
# Requires cloud-client.ps1 (Test-ProgramFileName). Loading this file builds nothing.

function Get-BytesSha256 {
    param([byte[]]$Bytes)
    $sha = [Security.Cryptography.SHA256]::Create()
    try { return ([BitConverter]::ToString($sha.ComputeHash($Bytes)) -replace '-', '').ToLowerInvariant() }
    finally { $sha.Dispose() }
}

function New-ReleasePackage {
    param(
        [Parameter(Mandatory)][string]$SourceRoot,
        [Parameter(Mandatory)][string[]]$Files,
        [Parameter(Mandatory)][string]$Version,
        [Parameter(Mandatory)][string]$Commit,
        [string[]]$Notes = @(),
        [Parameter(Mandatory)][string]$OutputPath
    )
    Add-Type -AssemblyName System.IO.Compression
    $entries = @(foreach ($name in ($Files | Sort-Object -Unique)) {
        if (-not (Test-ProgramFileName $name)) { throw "Файл не может входить в версию программы: $name" }
        $bytes = [IO.File]::ReadAllBytes((Join-Path $SourceRoot $name))
        [pscustomobject]@{ Path = $name; Bytes = $bytes; Sha256 = (Get-BytesSha256 $bytes) }
    })
    if ($entries.Count -eq 0) { throw 'Нет файлов для версии программы.' }

    $manifest = [ordered]@{
        version = $Version
        commit = $Commit
        publishedAt = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
        notes = @($Notes)
        files = @($entries | ForEach-Object { [ordered]@{ path = $_.Path; sha256 = $_.Sha256; size = $_.Bytes.Length } })
    }
    $manifestBytes = [Text.UTF8Encoding]::new($false).GetBytes((ConvertTo-Json -InputObject $manifest -Depth 5))

    $stream = [IO.File]::Open($OutputPath, [IO.FileMode]::CreateNew)
    try {
        $archive = [IO.Compression.ZipArchive]::new($stream, [IO.Compression.ZipArchiveMode]::Create)
        try {
            foreach ($item in @([pscustomobject]@{ Path = 'manifest.json'; Bytes = $manifestBytes }) + $entries) {
                $entry = $archive.CreateEntry($item.Path, [IO.Compression.CompressionLevel]::Optimal)
                $output = $entry.Open()
                try { $output.Write($item.Bytes, 0, $item.Bytes.Length) } finally { $output.Dispose() }
            }
        }
        finally { $archive.Dispose() }
    }
    finally { $stream.Dispose() }

    return [pscustomobject]@{
        Path = $OutputPath
        Version = $Version
        Sha256 = (Get-FileHash -LiteralPath $OutputPath -Algorithm SHA256).Hash.ToLowerInvariant()
        Size = (Get-Item -LiteralPath $OutputPath).Length
        Files = @($entries | ForEach-Object { $_.Path })
    }
}
