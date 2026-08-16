$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent $PSScriptRoot
$gameRoot = Split-Path -Parent $projectRoot
$distribution = Join-Path $projectRoot "dist"
$releaseDirectory = Join-Path $projectRoot "release"
$archiveName = "Ironmon-v0.7.6-battle-items.zip"
$archive = Join-Path $releaseDirectory $archiveName
$checksum = Join-Path $releaseDirectory "Ironmon-v0.7.6-battle-items.sha256.txt"
$areaCatalog = Join-Path $projectRoot "data\area_catalog.dat"
$areaAudit = Join-Path $projectRoot "docs\audits\generated\AREA_CATALOG_GENERATED.csv"
$coverageDataset = Join-Path $projectRoot "data\type_coverage.json"
$coverageAudit = Join-Path $projectRoot "docs\audits\generated\TYPE_COVERAGE_GENERATED.csv"
$generatedAreaCatalog = "$areaCatalog.release.tmp"
$generatedAreaAudit = "$areaAudit.release.tmp"
$generatedCoverageDataset = "$coverageDataset.release.tmp"
$generatedCoverageAudit = "$coverageAudit.release.tmp"

$generationStarted = [DateTime]::UtcNow.AddSeconds(-2)
try {
  & (Join-Path $PSScriptRoot "generation\Generate-Area-Catalog.ps1") `
    -GameRoot $gameRoot `
    -OutputPath $generatedAreaCatalog `
    -AuditPath $generatedAreaAudit
  & (Join-Path $PSScriptRoot "generation\Generate-Type-Coverage-Dataset.ps1") `
    -GameRoot $gameRoot `
    -OutputPath $generatedCoverageDataset `
    -AuditPath $generatedCoverageAudit
  foreach ($generatedPath in $generatedAreaCatalog, $generatedAreaAudit, $generatedCoverageDataset, $generatedCoverageAudit) {
    if (-not (Test-Path -LiteralPath $generatedPath) -or
        (Get-Item -LiteralPath $generatedPath).Length -eq 0) {
      throw "Release generation did not produce '$generatedPath'."
    }
    if ((Get-Item -LiteralPath $generatedPath).LastWriteTimeUtc -lt $generationStarted) {
      throw "Release generation left stale data at '$generatedPath'."
    }
  }

  foreach ($generatedFile in @(
    @{ Generated = $generatedAreaCatalog; Canonical = $areaCatalog },
    @{ Generated = $generatedAreaAudit; Canonical = $areaAudit },
    @{ Generated = $generatedCoverageDataset; Canonical = $coverageDataset },
    @{ Generated = $generatedCoverageAudit; Canonical = $coverageAudit }
  )) {
    $canonicalExists = Test-Path -LiteralPath $generatedFile.Canonical
    $contentChanged = -not $canonicalExists -or
      (Get-FileHash -LiteralPath $generatedFile.Generated -Algorithm SHA256).Hash -ne
        (Get-FileHash -LiteralPath $generatedFile.Canonical -Algorithm SHA256).Hash
    if ($contentChanged) {
      Copy-Item -LiteralPath $generatedFile.Generated -Destination $generatedFile.Canonical -Force
    }
  }
} finally {
  Remove-Item -LiteralPath $generatedAreaCatalog, $generatedAreaAudit, $generatedCoverageDataset, $generatedCoverageAudit `
    -Force `
    -ErrorAction SilentlyContinue
}

& (Join-Path $PSScriptRoot "Build-Distribution.ps1")
& (Join-Path $PSScriptRoot "Publish-Tracker.ps1")

$distributedAreaCatalog = Join-Path $distribution "Data\Ironmon\area_catalog.dat"
if (-not (Test-Path -LiteralPath $distributedAreaCatalog) -or
    (Get-FileHash -LiteralPath $distributedAreaCatalog -Algorithm SHA256).Hash -ne
      (Get-FileHash -LiteralPath $areaCatalog -Algorithm SHA256).Hash) {
  throw "The player distribution does not contain the freshly generated area catalog."
}

$trackerAssemblyPath = Join-Path $distribution "Ironmon Tracker\Ironmon Tracker.dll"
$trackerAssembly = [Reflection.Assembly]::LoadFile($trackerAssemblyPath)
$coverageResourceName = "Ironmon.Tracker.App.Resources.Coverage.type_coverage.json"
$coverageResource = $trackerAssembly.GetManifestResourceStream($coverageResourceName)
if ($null -eq $coverageResource) {
  throw "The published tracker does not contain the aggregate coverage resource."
}
$sha256 = [Security.Cryptography.SHA256]::Create()
try {
  $resourceHash = [BitConverter]::ToString(
    $sha256.ComputeHash($coverageResource)
  ).Replace("-", "")
} finally {
  $sha256.Dispose()
  $coverageResource.Dispose()
}
if ($resourceHash -ne
    (Get-FileHash -LiteralPath $coverageDataset -Algorithm SHA256).Hash) {
  throw "The published tracker does not contain the freshly generated coverage dataset."
}

$forbiddenFiles = Get-ChildItem -LiteralPath $distribution -File -Recurse |
  Where-Object {
    $_.Name -match "AccessGenerator|private[-_ ]?key" -or
    $_.Name -match "Generate-(?:Area-Catalog|Type-Coverage-Dataset)" -or
    $_.Name -match "Export-(?:AreaCatalog|TypeCoverageDataset)" -or
    $_.Name -match "(?:AREA_CATALOG|TYPE_COVERAGE)_GENERATED|GameRuntime-Tooling|Script-Loader" -or
    $_.Name -match "Test-GameRuntime|(?:Area-Progress|Diagnostic-Access)\.rb" -or
    $_.Name -match "\.(?:bootstrap|progress|summary|tests|tmp)$" -or
    $_.Extension -in ".ironmon-access", ".key", ".p8", ".p12", ".pfx", ".pem" -or
    $_.FullName -match "998_Ironmon_Development|maintainer-dist"
  }
if ($forbiddenFiles) {
  $relativeForbiddenFiles = $forbiddenFiles.FullName |
    ForEach-Object { $_.Substring($distribution.Length + 1) }
  throw "Player distribution contains forbidden maintainer, token, key, or development files: $($relativeForbiddenFiles -join ', ')"
}
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
