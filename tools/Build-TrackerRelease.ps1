$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent $PSScriptRoot
$distribution = Join-Path $projectRoot "dist"
$releaseDirectory = Join-Path $projectRoot "release"
$archiveName = "Ironmon-v0.7.1-randomization-fixes.zip"
$archive = Join-Path $releaseDirectory $archiveName
$checksum = Join-Path $releaseDirectory "Ironmon-v0.7.1-randomization-fixes.sha256.txt"

& (Join-Path $PSScriptRoot "Build-Distribution.ps1")
& (Join-Path $PSScriptRoot "Publish-Tracker.ps1")
New-Item -ItemType Directory -Force -Path $releaseDirectory | Out-Null
if (Test-Path -LiteralPath $archive) {
  Remove-Item -LiteralPath $archive -Force
}
if (Test-Path -LiteralPath $checksum) {
  Remove-Item -LiteralPath $checksum -Force
}

Add-Type -AssemblyName System.IO.Compression
$archiveStream = [System.IO.File]::Open($archive, [System.IO.FileMode]::CreateNew)
$zip = New-Object System.IO.Compression.ZipArchive(
  $archiveStream,
  [System.IO.Compression.ZipArchiveMode]::Create,
  $false
)
$fixedTimestamp = New-Object System.DateTimeOffset(2000, 1, 1, 0, 0, 0,
                                                   [System.TimeSpan]::Zero)
try {
  $files = Get-ChildItem -LiteralPath $distribution -File -Recurse |
    Sort-Object FullName
  foreach ($file in $files) {
    $relativePath = $file.FullName.Substring($distribution.Length + 1)
    $entry = $zip.CreateEntry(
      $relativePath.Replace("\", "/"),
      [System.IO.Compression.CompressionLevel]::Optimal
    )
    $entry.LastWriteTime = $fixedTimestamp
    $inputStream = [System.IO.File]::OpenRead($file.FullName)
    $entryStream = $entry.Open()
    try {
      $inputStream.CopyTo($entryStream)
    } finally {
      $entryStream.Dispose()
      $inputStream.Dispose()
    }
  }
} finally {
  $zip.Dispose()
  $archiveStream.Dispose()
}

$hash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -LiteralPath $checksum -Value "$hash  $archiveName"
Write-Output "Created $archive"
Write-Output "SHA256 $hash"
