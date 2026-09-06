param([switch]$GenerateOnly)

$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent $PSScriptRoot
$gameRoot = Split-Path -Parent $projectRoot
$distribution = Join-Path $projectRoot "dist"
$runtimeRequiredDistribution = Join-Path $projectRoot "dist-runtime-required"
$releaseDirectory = Join-Path $projectRoot "release"
$trackerProject = Join-Path $projectRoot "tracker\src\Ironmon.Tracker.App\Ironmon.Tracker.App.csproj"
[xml]$trackerProjectDocument = Get-Content -LiteralPath $trackerProject
$releaseVersion = @($trackerProjectDocument.Project.PropertyGroup.ApplicationDisplayVersion) |
  Where-Object { ![string]::IsNullOrWhiteSpace($_) } |
  Select-Object -First 1
if ($null -eq $releaseVersion -or $releaseVersion -notmatch '^\d+\.\d+\.\d+(?:[-+][0-9A-Za-z.-]+)?$') {
  throw "The tracker project must define a valid ApplicationDisplayVersion."
}
$releaseRuntimeIdentifier = "win-x64"
$selfContainedArchiveName = "Ironmon-v$releaseVersion-$releaseRuntimeIdentifier.zip"
$runtimeRequiredArchiveName = "Ironmon-v$releaseVersion-$releaseRuntimeIdentifier-runtime-required.zip"
$releaseArtifactNames = @(
  $selfContainedArchiveName,
  ($selfContainedArchiveName -replace "\.zip$", ".sha256.txt"),
  $runtimeRequiredArchiveName,
  ($runtimeRequiredArchiveName -replace "\.zip$", ".sha256.txt")
)
$existingReleaseArtifacts = $releaseArtifactNames | Where-Object {
  Test-Path -LiteralPath (Join-Path $releaseDirectory $_)
}
if (-not $GenerateOnly -and $existingReleaseArtifacts) {
  throw "Release version $releaseVersion already has published artifacts: $($existingReleaseArtifacts -join ', '). Bump ApplicationDisplayVersion, ApplicationVersion, and Version before building another release. Existing release artifacts are immutable."
}
$areaCatalog = Join-Path $projectRoot "data\area_catalog.dat"
$fusionPredecessorIndex = Join-Path $projectRoot "data\fusion_predecessor_index.dat"
$playerFusionWorkerCatalog = Join-Path $projectRoot "data\player_fusion_worker_catalog.json"
$areaAudit = Join-Path $projectRoot "docs\audits\generated\AREA_CATALOG_GENERATED.csv"
$cosmeticAudit = Join-Path $projectRoot "docs\audits\generated\COSMETICS_GENERATED.json"
$generatedCosmeticAudit = "$cosmeticAudit.release.tmp"
$coverageDataset = Join-Path $projectRoot "data\type_coverage.json"
$coverageAudit = Join-Path $projectRoot "docs\audits\generated\TYPE_COVERAGE_GENERATED.csv"
$itemAudit = Join-Path $projectRoot "docs\audits\generated\ITEM_RANDOMIZATION_GENERATED.csv"
$obtainabilityAudit = Join-Path $projectRoot "docs\audits\generated\OBTAINABILITY_FOUNDATION_GENERATED.csv"
$obtainabilitySourceCatalog = Join-Path $projectRoot "data\obtainability_source_catalog.json"
$movePowerPresentationCatalog = Join-Path $projectRoot "data\move_power_presentation.json"
$defensePresentationCatalog = Join-Path $projectRoot "data\defense_presentation.json"
$defensePresentationAudit = Join-Path $projectRoot "docs\audits\generated\DEFENSE_PRESENTATION_GENERATED.csv"
$generatedDefensePresentationCatalog = "$defensePresentationCatalog.release.tmp"
$generatedDefensePresentationAudit = "$defensePresentationAudit.release.tmp"
$generatedAreaCatalog = "$areaCatalog.release.tmp"
$generatedFusionPredecessorIndex = "$fusionPredecessorIndex.release.tmp"
$generatedAreaAudit = "$areaAudit.release.tmp"
$generatedCoverageDataset = "$coverageDataset.release.tmp"
$generatedCoverageAudit = "$coverageAudit.release.tmp"
$generatedItemAudit = "$itemAudit.release.tmp"
$generatedObtainabilityAudit = "$obtainabilityAudit.release.tmp"
$generatedObtainabilitySourceCatalog = "$obtainabilitySourceCatalog.release.tmp"

function Test-PlayerDistribution {
  param([string]$DistributionPath)

  $distributedDefensePresentationCatalog = Join-Path $DistributionPath "Data\Ironmon\defense_presentation.json"
  if (-not (Test-Path -LiteralPath $distributedDefensePresentationCatalog) -or
      (Get-FileHash -LiteralPath $distributedDefensePresentationCatalog -Algorithm SHA256).Hash -ne
        (Get-FileHash -LiteralPath $defensePresentationCatalog -Algorithm SHA256).Hash) {
    throw "The player distribution does not contain the freshly generated defense presentation catalog."
  }
  $distributedAreaCatalog = Join-Path $DistributionPath "Data\Ironmon\area_catalog.dat"
  if (-not (Test-Path -LiteralPath $distributedAreaCatalog) -or
      (Get-FileHash -LiteralPath $distributedAreaCatalog -Algorithm SHA256).Hash -ne
        (Get-FileHash -LiteralPath $areaCatalog -Algorithm SHA256).Hash) {
    throw "The player distribution does not contain the freshly generated area catalog."
  }
  $distributedObtainabilitySourceCatalog = Join-Path $DistributionPath "Data\Ironmon\obtainability_source_catalog.json"
  if (-not (Test-Path -LiteralPath $distributedObtainabilitySourceCatalog) -or
      (Get-FileHash -LiteralPath $distributedObtainabilitySourceCatalog -Algorithm SHA256).Hash -ne
        (Get-FileHash -LiteralPath $obtainabilitySourceCatalog -Algorithm SHA256).Hash) {
    throw "The player distribution does not contain the freshly generated obtainability source catalog."
  }
  $distributedMovePowerPresentationCatalog = Join-Path $DistributionPath "Data\Ironmon\move_power_presentation.json"
  if (-not (Test-Path -LiteralPath $distributedMovePowerPresentationCatalog) -or
      (Get-FileHash -LiteralPath $distributedMovePowerPresentationCatalog -Algorithm SHA256).Hash -ne
        (Get-FileHash -LiteralPath $movePowerPresentationCatalog -Algorithm SHA256).Hash) {
    throw "The player distribution does not contain the freshly generated move-power presentation catalog."
  }
  $distributedFusionPredecessorIndex = Join-Path $DistributionPath "Data\Ironmon\fusion_predecessor_index.dat"
  if (-not (Test-Path -LiteralPath $distributedFusionPredecessorIndex) -or
      (Get-FileHash -LiteralPath $distributedFusionPredecessorIndex -Algorithm SHA256).Hash -ne
        (Get-FileHash -LiteralPath $fusionPredecessorIndex -Algorithm SHA256).Hash) {
    throw "The player distribution does not contain the freshly generated fusion predecessor index."
  }

  $trackerConnectionAssemblyPath = Join-Path $DistributionPath "Ironmon Tracker\Ironmon.Tracker.Connection.dll"
  $trackerConnectionAssembly = [Reflection.Assembly]::LoadFile($trackerConnectionAssemblyPath)
  $workerResourceName = "Ironmon.Tracker.Connection.Resources.player_fusion_worker_catalog.json"
  $workerResource = $trackerConnectionAssembly.GetManifestResourceStream($workerResourceName)
  if ($null -eq $workerResource) {
    throw "The published tracker does not contain the player-fusion worker catalog."
  }
  $workerSha256 = [Security.Cryptography.SHA256]::Create()
  try {
    $workerResourceHash = [BitConverter]::ToString(
      $workerSha256.ComputeHash($workerResource)
    ).Replace("-", "")
  } finally {
    $workerSha256.Dispose()
    $workerResource.Dispose()
  }
  if ($workerResourceHash -ne
      (Get-FileHash -LiteralPath $playerFusionWorkerCatalog -Algorithm SHA256).Hash) {
    throw "The published tracker does not contain the freshly generated player-fusion worker catalog."
  }
  $sourceResourceName = "Ironmon.Tracker.Connection.Resources.obtainability_source_catalog.json"
  $sourceResource = $trackerConnectionAssembly.GetManifestResourceStream($sourceResourceName)
  if ($null -eq $sourceResource) {
    throw "The published tracker does not contain the obtainability source catalog."
  }
  $sourceSha256 = [Security.Cryptography.SHA256]::Create()
  try {
    $sourceResourceHash = [BitConverter]::ToString(
      $sourceSha256.ComputeHash($sourceResource)
    ).Replace("-", "")
  } finally {
    $sourceSha256.Dispose()
    $sourceResource.Dispose()
  }
  if ($sourceResourceHash -ne
      (Get-FileHash -LiteralPath $obtainabilitySourceCatalog -Algorithm SHA256).Hash) {
    throw "The published tracker does not contain the freshly generated obtainability source catalog."
  }

  $trackerAssemblyPath = Join-Path $DistributionPath "Ironmon Tracker\Ironmon Tracker.dll"
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

  $forbiddenFiles = Get-ChildItem -LiteralPath $DistributionPath -File -Recurse |
    Where-Object {
      $_.Name -match "AccessGenerator|private[-_ ]?key" -or
      $_.Name -match "Generate-(?:Area-Catalog|Type-Coverage-Dataset|Item-Randomization-Audit|Obtainability-Foundation-Audit|Player-Fusion-Worker-Catalog|Defense-Presentation|Cosmetic-Audit)" -or
      $_.Name -match "Export-(?:AreaCatalog|TypeCoverageDataset|ItemRandomizationAudit|ObtainabilityFoundationAudit|PlayerFusionWorkerCatalog|DefensePresentation|CosmeticAudit)" -or
      $_.Name -match "(?:AREA_CATALOG|TYPE_COVERAGE|ITEM_RANDOMIZATION|OBTAINABILITY_FOUNDATION|DEFENSE_PRESENTATION|COSMETICS)_GENERATED|DEFENSE_PRESENTATION_RULES|GameRuntime-Tooling|Script-Loader" -or
      $_.Name -match "Test-GameRuntime|(?:Area-Progress|Diagnostic-Access)\.rb" -or
      $_.Name -match "\.(?:bootstrap|progress|summary|tests|tmp)$" -or
      $_.Extension -in ".ironmon-access", ".key", ".p8", ".p12", ".pfx", ".pem" -or
      $_.FullName -match "998_Ironmon_Development|maintainer-dist"
    }
  if ($forbiddenFiles) {
    $relativeForbiddenFiles = $forbiddenFiles.FullName |
      ForEach-Object { $_.Substring($DistributionPath.Length + 1) }
    throw "Player distribution contains forbidden maintainer, token, key, or development files: $($relativeForbiddenFiles -join ', ')"
  }
}

function New-DeterministicReleaseArchive {
  param(
    [string]$DistributionPath,
    [string]$ArchiveName
  )

  $archive = Join-Path $releaseDirectory $ArchiveName
  $checksum = Join-Path $releaseDirectory ($ArchiveName -replace "\.zip$", ".sha256.txt")
  if ((Test-Path -LiteralPath $archive) -or
      (Test-Path -LiteralPath $checksum)) {
    throw "Release artifacts are immutable and '$ArchiveName' already exists."
  }

  $archiveStream = [System.IO.File]::Open($archive, [System.IO.FileMode]::CreateNew)
  $zip = New-Object System.IO.Compression.ZipArchive(
    $archiveStream,
    [System.IO.Compression.ZipArchiveMode]::Create,
    $false
  )
  $fixedTimestamp = New-Object System.DateTimeOffset(2000, 1, 1, 0, 0, 0,
                                                     [System.TimeSpan]::Zero)
  try {
    $files = Get-ChildItem -LiteralPath $DistributionPath -File -Recurse |
      Sort-Object FullName
    foreach ($file in $files) {
      $relativePath = $file.FullName.Substring($DistributionPath.Length + 1)
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
  "$hash  $ArchiveName" | Out-File -LiteralPath $checksum -Encoding utf8NoBOM -NoClobber
  Write-Output "Created $archive"
  Write-Output "SHA256 $hash"
}

$generatedPlayerFusionWorkerCatalog = "$playerFusionWorkerCatalog.release.tmp"
try {
  & (Join-Path $PSScriptRoot "generation\Generate-Player-Fusion-Worker-Catalog.ps1") `
    -GameRoot $gameRoot `
    -OutputPath $generatedPlayerFusionWorkerCatalog
  if (-not (Test-Path -LiteralPath $generatedPlayerFusionWorkerCatalog) -or
      (Get-Item -LiteralPath $generatedPlayerFusionWorkerCatalog).Length -eq 0) {
    throw "Release generation did not produce the player-fusion worker catalog."
  }
  if (-not (Test-Path -LiteralPath $playerFusionWorkerCatalog) -or
      (Get-FileHash -LiteralPath $generatedPlayerFusionWorkerCatalog -Algorithm SHA256).Hash -ne
        (Get-FileHash -LiteralPath $playerFusionWorkerCatalog -Algorithm SHA256).Hash) {
    Copy-Item -LiteralPath $generatedPlayerFusionWorkerCatalog -Destination $playerFusionWorkerCatalog -Force
  }
} finally {
  Remove-Item -LiteralPath $generatedPlayerFusionWorkerCatalog -Force -ErrorAction SilentlyContinue
}

$generationStarted = [DateTime]::UtcNow.AddSeconds(-2)
try {
  & (Join-Path $PSScriptRoot "generation\Generate-Cosmetic-Audit.ps1") `
    -GameRoot $gameRoot -OutputPath $generatedCosmeticAudit -PreviousPath $cosmeticAudit
  & (Join-Path $PSScriptRoot "generation\Generate-Defense-Presentation.ps1") `
    -GameRoot $gameRoot `
    -OutputPath $generatedDefensePresentationCatalog `
    -AuditPath $generatedDefensePresentationAudit
  & (Join-Path $PSScriptRoot "generation\Generate-Area-Catalog.ps1") `
    -GameRoot $gameRoot `
    -OutputPath $generatedAreaCatalog `
    -AuditPath $generatedAreaAudit
  & (Join-Path $PSScriptRoot "generation\Generate-Fusion-Predecessor-Index.ps1") `
    -GameRoot $gameRoot `
    -OutputPath $generatedFusionPredecessorIndex
  & (Join-Path $PSScriptRoot "generation\Generate-Item-Randomization-Audit.ps1") `
    -GameRoot $gameRoot `
    -AuditPath $generatedItemAudit
  & (Join-Path $PSScriptRoot "generation\Generate-Obtainability-Foundation-Audit.ps1") `
    -GameRoot $gameRoot `
    -AreaCatalogPath $generatedAreaCatalog `
    -SourceCatalogPath $generatedObtainabilitySourceCatalog `
    -AuditPath $generatedObtainabilityAudit
  foreach ($generatedPath in $generatedCosmeticAudit, $generatedAreaCatalog, $generatedAreaAudit, $generatedFusionPredecessorIndex, $generatedItemAudit, $generatedObtainabilityAudit, $generatedObtainabilitySourceCatalog, $generatedDefensePresentationCatalog, $generatedDefensePresentationAudit) {
    if (-not (Test-Path -LiteralPath $generatedPath) -or
        (Get-Item -LiteralPath $generatedPath).Length -eq 0) {
      throw "Release generation did not produce '$generatedPath'."
    }
    if ((Get-Item -LiteralPath $generatedPath).LastWriteTimeUtc -lt $generationStarted) {
      throw "Release generation left stale data at '$generatedPath'."
    }
  }

  foreach ($generatedFile in @(
    @{ Generated = $generatedCosmeticAudit; Canonical = $cosmeticAudit },
    @{ Generated = $generatedDefensePresentationCatalog; Canonical = $defensePresentationCatalog },
    @{ Generated = $generatedDefensePresentationAudit; Canonical = $defensePresentationAudit },
    @{ Generated = $generatedAreaCatalog; Canonical = $areaCatalog },
    @{ Generated = $generatedAreaAudit; Canonical = $areaAudit },
    @{ Generated = $generatedFusionPredecessorIndex; Canonical = $fusionPredecessorIndex },
    @{ Generated = $generatedItemAudit; Canonical = $itemAudit },
    @{ Generated = $generatedObtainabilityAudit; Canonical = $obtainabilityAudit },
    @{ Generated = $generatedObtainabilitySourceCatalog; Canonical = $obtainabilitySourceCatalog }
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
  Remove-Item -LiteralPath $generatedCosmeticAudit, $generatedAreaCatalog, $generatedAreaAudit, $generatedFusionPredecessorIndex, $generatedItemAudit, $generatedObtainabilityAudit, $generatedObtainabilitySourceCatalog, $generatedDefensePresentationCatalog, $generatedDefensePresentationAudit `
    -Force `
    -ErrorAction SilentlyContinue
}

& (Join-Path $PSScriptRoot "generation\Generate-Generation-Profile.ps1") `
  -GameRoot $gameRoot

& (Join-Path $PSScriptRoot "Build-Distribution.ps1")

$coverageGenerationStarted = [DateTime]::UtcNow.AddSeconds(-2)
try {
  & (Join-Path $PSScriptRoot "generation\Generate-Type-Coverage-Dataset.ps1") `
    -GameRoot $gameRoot `
    -OutputPath $generatedCoverageDataset `
    -AuditPath $generatedCoverageAudit
  foreach ($generatedPath in $generatedCoverageDataset, $generatedCoverageAudit) {
    if (-not (Test-Path -LiteralPath $generatedPath) -or
        (Get-Item -LiteralPath $generatedPath).Length -eq 0) {
      throw "Release generation did not produce '$generatedPath'."
    }
    if ((Get-Item -LiteralPath $generatedPath).LastWriteTimeUtc -lt $coverageGenerationStarted) {
      throw "Release generation left stale data at '$generatedPath'."
    }
  }

  foreach ($generatedFile in @(
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
  Remove-Item -LiteralPath $generatedCoverageDataset, $generatedCoverageAudit `
    -Force `
    -ErrorAction SilentlyContinue
}

if ($GenerateOnly) {
  Write-Output "Generated current-source release catalogs and distribution."
  return
}

& dotnet test (Join-Path $projectRoot "tracker\tests\Ironmon.Tracker.Tests\Ironmon.Tracker.Tests.csproj") `
  --configuration Release `
  --maxcpucount:1 `
  -- xUnit.ParallelizeTestCollections=false
if ($LASTEXITCODE -ne 0) {
  throw "Tracker tests failed."
}

& dotnet test (Join-Path $projectRoot "tracker\tests\Ironmon.Tracker.App.Tests\Ironmon.Tracker.App.Tests.csproj") `
  --configuration Release `
  --runtime win-x64 `
  --maxcpucount:1
if ($LASTEXITCODE -ne 0) {
  throw "Tracker app tests failed."
}

& (Join-Path $PSScriptRoot "Test-GenerationProfile.ps1") `
  -GameRoot $gameRoot `
  -TimeoutSeconds 300

& (Join-Path $PSScriptRoot "Test-GameRuntime.ps1") `
  -GameRoot $gameRoot `
  -TimeoutSeconds 300

& (Join-Path $PSScriptRoot "Test-DefenseOverview.ps1")
& (Join-Path $PSScriptRoot "Test-AreaEncounterLookup.ps1") -GameRoot $gameRoot

& (Join-Path $PSScriptRoot "Publish-Tracker.ps1") -DeploymentMode SelfContained

$resolvedRuntimeRequiredDistribution = [System.IO.Path]::GetFullPath($runtimeRequiredDistribution)
$resolvedProjectRoot = [System.IO.Path]::GetFullPath($projectRoot).TrimEnd([System.IO.Path]::DirectorySeparatorChar)
if (!$resolvedRuntimeRequiredDistribution.StartsWith($resolvedProjectRoot + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase)) {
  throw "Runtime-required distribution must remain inside the Ironmon project directory."
}
if (Test-Path -LiteralPath $resolvedRuntimeRequiredDistribution) {
  Remove-Item -LiteralPath $resolvedRuntimeRequiredDistribution -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $resolvedRuntimeRequiredDistribution | Out-Null
foreach ($distributionEntry in "Data", "README.md", "INSTALLATION.md", "RELEASE_NOTES.md", "LICENSE", "THIRD_PARTY_NOTICES.md", "OPEN-SANS-LICENSE.txt") {
  $sourceEntry = Join-Path $distribution $distributionEntry
  if (-not (Test-Path -LiteralPath $sourceEntry)) {
    throw "The base player distribution is missing '$distributionEntry'."
  }

  Copy-Item -LiteralPath $sourceEntry -Destination (Join-Path $resolvedRuntimeRequiredDistribution $distributionEntry) -Recurse -Force
}
& (Join-Path $PSScriptRoot "Publish-Tracker.ps1") `
  -DeploymentMode RuntimeRequired `
  -OutputDirectory (Join-Path $resolvedRuntimeRequiredDistribution "Ironmon Tracker")

Test-PlayerDistribution -DistributionPath $distribution
Test-PlayerDistribution -DistributionPath $resolvedRuntimeRequiredDistribution

New-Item -ItemType Directory -Force -Path $releaseDirectory | Out-Null
Add-Type -AssemblyName System.IO.Compression
New-DeterministicReleaseArchive -DistributionPath $distribution -ArchiveName $selfContainedArchiveName
New-DeterministicReleaseArchive -DistributionPath $resolvedRuntimeRequiredDistribution -ArchiveName $runtimeRequiredArchiveName
