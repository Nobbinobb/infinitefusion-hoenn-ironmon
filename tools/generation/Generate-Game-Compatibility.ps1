<#
.SYNOPSIS
Creates the early game guard inventory from the already generated release baseline.
.DESCRIPTION
Build-Distribution calls this packaging step. It never resolves upstream again,
and ordinary IDE builds do not generate it. Artwork and audio are excluded from
startup hashing; executable code, game data and configuration remain mandatory.
#>
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$baselineRoot = Join-Path $projectRoot 'data/updater/baselines'
$inputPath = Join-Path $baselineRoot 'hoenn.json.gz'
$manifest = Get-Content -LiteralPath (Join-Path $baselineRoot 'hoenn.manifest.json') -Raw | ConvertFrom-Json
if ((Get-FileHash -LiteralPath $inputPath).Hash -cne $manifest.Sha256) { throw 'The game baseline checksum differs.' }
$inputStream = [IO.File]::OpenRead($inputPath)
try {
    $gzip = [IO.Compression.GZipStream]::new($inputStream, [IO.Compression.CompressionMode]::Decompress)
    $reader = [IO.StreamReader]::new($gzip)
    try { $baseline = $reader.ReadToEnd() | ConvertFrom-Json } finally { $reader.Dispose() }
} finally { $inputStream.Dispose() }
if ($baseline.Commit -cne $manifest.Commit -or $baseline.Files.Count -ne $manifest.FileCount) { throw 'The game baseline identity differs.' }
$extensions = @('.rb', '.rxdata', '.dat', '.dll', '.exe', '.ini', '.so')
$files = @($baseline.Files | Where-Object { [IO.Path]::GetExtension($_.Path).ToLowerInvariant() -in $extensions -and $_.Path -cne 'Data/Scripts/DownloadedSettings.rb' } | ForEach-Object {
    [ordered]@{ Path = $_.Path; Canonical = $_.Canonical; WindowsText = $_.WindowsText }
})
if ($files.Count -eq 0) { throw 'The runtime compatibility inventory is empty.' }
$document = [ordered]@{ Commit = $baseline.Commit; Files = $files }
$outputPath = Join-Path $projectRoot 'data/updater/game-compatibility.json'
[IO.File]::WriteAllText($outputPath, ($document | ConvertTo-Json -Depth 8 -Compress), [Text.UTF8Encoding]::new($false))
Write-Output "Generated game startup checks for $($manifest.Commit) ($($files.Count) files)."
