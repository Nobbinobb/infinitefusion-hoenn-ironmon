param(
  [string]$ArchivePath,
  [string]$Repository = $env:GITHUB_REPOSITORY
)

$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$dataRoot = Join-Path $projectRoot "data"
$systemTemporaryRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$temporaryRoot = [IO.Path]::GetFullPath(
  (Join-Path $systemTemporaryRoot "ironmon-ci-catalogs-$([Guid]::NewGuid().ToString('N'))")
)
if (-not $temporaryRoot.StartsWith($systemTemporaryRoot, [StringComparison]::OrdinalIgnoreCase)) {
  throw "The catalog extraction directory is outside the system temporary directory."
}

function Export-EmbeddedResource {
  param(
    [string]$AssemblyPath,
    [string]$ResourceName,
    [string]$DestinationPath
  )

  $assembly = [Reflection.Assembly]::Load([IO.File]::ReadAllBytes($AssemblyPath))
  $resource = $assembly.GetManifestResourceStream($ResourceName)
  if ($null -eq $resource) {
    throw "Release assembly '$AssemblyPath' does not contain '$ResourceName'."
  }

  $output = [IO.File]::Create($DestinationPath)
  try {
    $resource.CopyTo($output)
  } finally {
    $output.Dispose()
    $resource.Dispose()
  }
}

New-Item -ItemType Directory -Path $temporaryRoot | Out-Null
try {
  if ([string]::IsNullOrWhiteSpace($ArchivePath)) {
    if ([string]::IsNullOrWhiteSpace($Repository)) {
      throw "Repository is required when ArchivePath is not supplied."
    }

    $downloadRoot = Join-Path $temporaryRoot "download"
    New-Item -ItemType Directory -Path $downloadRoot | Out-Null
    & gh release download --repo $Repository `
      --pattern "Ironmon-v*-win-x64-runtime-required.zip" `
      --dir $downloadRoot
    if ($LASTEXITCODE -ne 0) {
      throw "The latest published Ironmon release could not be downloaded."
    }

    $archives = @(Get-ChildItem -LiteralPath $downloadRoot -Filter "*.zip" -File)
    if ($archives.Count -ne 1) {
      throw "Expected exactly one runtime-required release archive, found $($archives.Count)."
    }
    $ArchivePath = $archives[0].FullName
  }

  $resolvedArchive = [IO.Path]::GetFullPath($ArchivePath)
  if (-not (Test-Path -LiteralPath $resolvedArchive -PathType Leaf)) {
    throw "Release archive '$resolvedArchive' does not exist."
  }

  $extractionRoot = Join-Path $temporaryRoot "release"
  Expand-Archive -LiteralPath $resolvedArchive -DestinationPath $extractionRoot
  $releaseDataRoot = Join-Path $extractionRoot "Data\Ironmon"
  $trackerRoot = Join-Path $extractionRoot "Ironmon Tracker"
  New-Item -ItemType Directory -Path $dataRoot -Force | Out-Null

  foreach ($fileName in "generation_custom_fusion_pool.bin", "generation_profile.json", "obtainability_source_catalog.json") {
    $source = Join-Path $releaseDataRoot $fileName
    if (-not (Test-Path -LiteralPath $source -PathType Leaf)) {
      throw "The release archive does not contain Data/Ironmon/$fileName."
    }
    Copy-Item -LiteralPath $source -Destination (Join-Path $dataRoot $fileName)
  }

  Export-EmbeddedResource `
    -AssemblyPath (Join-Path $trackerRoot "Ironmon Tracker.dll") `
    -ResourceName "Ironmon.Tracker.App.Resources.Coverage.type_coverage.json" `
    -DestinationPath (Join-Path $dataRoot "type_coverage.json")
  Export-EmbeddedResource `
    -AssemblyPath (Join-Path $trackerRoot "Ironmon.Tracker.Connection.dll") `
    -ResourceName "Ironmon.Tracker.Connection.Resources.player_fusion_worker_catalog.json" `
    -DestinationPath (Join-Path $dataRoot "player_fusion_worker_catalog.json")

  Write-Output "Restored release-generated catalogs required by tracker CI."
} finally {
  if (Test-Path -LiteralPath $temporaryRoot) {
    Remove-Item -LiteralPath $temporaryRoot -Recurse -Force
  }
}
