<#
.SYNOPSIS
Identifies and verifies reusable generated catalogs independently of tracker builds.
.DESCRIPTION
Only explicit generated data files enter the cache. Test results, distributions,
executables, signing material and release history are never restored from it.
#>

function Get-GenerationCacheFiles {
  @(
    'data/area_catalog.dat', 'data/fusion_predecessor_index.dat',
    'data/player_fusion_worker_catalog.json', 'data/type_coverage.json',
    'data/obtainability_source_catalog.json', 'data/defense_presentation.json',
    'data/move_power_presentation.json', 'data/graphics/Battle/cursor_fight.png',
    'data/graphics/Battle/cursor_fight_dark.png',
    'data/generation_profile.json', 'data/generation_base_catalog.json',
    'data/generation_base_catalog.component.json',
    'data/generation_custom_fusion_pool.bin', 'data/generation_custom_fusion_pool.component.json',
    'data/generation_custom_fusion_pool.audit.json', 'data/generation_custom_sprites.json',
    'data/generation_custom_sprites.component.json', 'data/generation_area_catalog.component.json',
    'data/generation_obtainability_sources.component.json',
    'data/updater/baselines/hoenn.json.gz', 'data/updater/baselines/hoenn.manifest.json',
    'docs/audits/generated/AREA_CATALOG_GENERATED.csv',
    'docs/audits/generated/COSMETICS_GENERATED.json',
    'docs/audits/generated/DEFENSE_PRESENTATION_GENERATED.csv',
    'docs/audits/generated/TYPE_COVERAGE_GENERATED.csv',
    'docs/audits/generated/ITEM_RANDOMIZATION_GENERATED.csv',
    'docs/audits/generated/OBTAINABILITY_FOUNDATION_GENERATED.csv'
  )
}

function Get-GenerationCacheKey([string]$ProjectRoot, [string]$Fingerprint) {
  if ($Fingerprint -cnotmatch '^[0-9a-f]{64}$') { throw 'Invalid upstream generation fingerprint.' }
  $paths = @(
    'src', 'tools/generation', 'tools/sprites', 'resources', 'docs/audits/DEFENSE_PRESENTATION_RULES.json',
    'tools/Build-Distribution.ps1', 'tools/Build-TrackerRelease.ps1',
    'tools/ci/GenerationCache.ps1', 'tools/ci/Initialize-HostedGame.ps1', '.gitattributes'
  )
  $files = @(git -C $ProjectRoot ls-files --cached --others --exclude-standard -- $paths | Sort-Object -Unique)
  if ($LASTEXITCODE -ne 0 -or $files.Count -eq 0) { throw 'Generation source inputs are unavailable.' }
  $records = @('generation-cache-v1', $Fingerprint, 'windows-x64')
  foreach ($relative in $files) {
    $path = Join-Path $ProjectRoot $relative
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { $records += "$relative deleted"; continue }
    $bytes = [IO.File]::ReadAllBytes($path)
    if ([IO.Path]::GetExtension($relative) -in '.rb','.ps1','.py','.json','.txt','.md','.yml' -or $relative -eq '.gitattributes') {
      $content = [Text.Encoding]::UTF8.GetString($bytes).Replace("`r`n", "`n")
      if ($relative -eq 'tools/Build-TrackerRelease.ps1') {
        $recipe = [regex]::Match($content, '(?ms)^# BEGIN GENERATED CATALOG RECIPE\n(?<recipe>.+?)^# END GENERATED CATALOG RECIPE$')
        if (-not $recipe.Success) { throw 'The release generation recipe is not explicitly bounded.' }
        $content = $recipe.Groups['recipe'].Value
      }
      # Catalog schemas use their own versions; the displayed mod version belongs
      # to freshly built packages, not to these generated gameplay components.
      if ($relative -eq 'src/foundation/Core.rb') {
        $content = [regex]::Replace($content, '(?m)^  VERSION = "[0-9]+\.[0-9]+\.[0-9]+"$', '  VERSION = "release-version"')
      }
      $bytes = [Text.Encoding]::UTF8.GetBytes($content)
    }
    $records += "$relative $([Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant())"
  }
  $digest = [Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes(($records -join "`n")))
  return 'generated-v1-' + [Convert]::ToHexString($digest).ToLowerInvariant()
}

function Assert-GenerationCachePath([string]$Root, [string]$Relative) {
  $rootPath = [IO.Path]::GetFullPath($Root).TrimEnd('\','/')
  $path = [IO.Path]::GetFullPath((Join-Path $rootPath $Relative))
  if (-not $path.StartsWith($rootPath + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Generation cache path escapes its root.' }
  $current = $path
  while ($current -and $current.Length -ge $rootPath.Length) {
    if (Test-Path -LiteralPath $current) {
      if ((Get-Item -LiteralPath $current -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Generation cache paths cannot contain links.' }
    }
    $current = Split-Path -Parent $current
  }
  return $path
}

function Restore-GenerationCache([string]$ProjectRoot, [string]$Directory, [string]$Key) {
  $manifestPath = Assert-GenerationCachePath $Directory 'manifest.json'
  if (-not (Test-Path -LiteralPath $manifestPath)) { return $false }
  $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
  if ($manifest.schema -ne 1 -or $manifest.key -cne $Key) { throw 'Generated cache input identity does not match this build.' }
  $expected = @(Get-GenerationCacheFiles | Sort-Object)
  $actual = @($manifest.files.path | Sort-Object)
  if ($actual.Count -ne $expected.Count -or ($actual -join "`n") -cne ($expected -join "`n")) { throw 'Generated cache has missing or unexpected files.' }
  foreach ($file in $manifest.files) {
    $path = Assert-GenerationCachePath $Directory $file.path
    $null = Assert-GenerationCachePath $ProjectRoot $file.path
    if (-not (Test-Path -LiteralPath $path -PathType Leaf) -or (Get-Item -LiteralPath $path).Length -ne $file.bytes -or
        (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() -cne $file.sha256) { throw "Generated cache content failed verification: $($file.path)." }
  }
  foreach ($file in $manifest.files) {
    $destination = Join-Path $ProjectRoot $file.path
    New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $Directory $file.path) -Destination $destination -Force
  }
  Write-Host "Reused verified generated catalogs: $Key."
  return $true
}

function Save-GenerationCache([string]$ProjectRoot, [string]$Directory, [string]$Key) {
  $files = @(foreach ($relative in Get-GenerationCacheFiles) {
    $source = Assert-GenerationCachePath $ProjectRoot $relative
    $destination = Assert-GenerationCachePath $Directory $relative
    if (-not (Test-Path -LiteralPath $source -PathType Leaf) -or (Get-Item -LiteralPath $source).Length -eq 0) { throw "Generated cache output is missing: $relative." }
    New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
    Copy-Item -LiteralPath $source -Destination $destination -Force
    [ordered]@{path=$relative;bytes=(Get-Item -LiteralPath $source).Length;sha256=(Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash.ToLowerInvariant()}
  })
  [ordered]@{schema=1;key=$Key;files=$files} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $Directory 'manifest.json') -Encoding utf8NoBOM
}
