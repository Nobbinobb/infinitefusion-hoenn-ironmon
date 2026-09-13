param([Parameter(Mandatory)][string]$SnapshotDirectory)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true
$projectRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
. (Join-Path $PSScriptRoot 'UpdateArtifacts.ps1')
Set-Location -LiteralPath $projectRoot
if (git status --porcelain --untracked-files=no) { throw 'Release build changed tracked source.' }
& (Join-Path $PSScriptRoot 'Test-ReleaseMetadata.ps1')
& (Join-Path $PSScriptRoot 'Test-ReleaseArchives.ps1')
[xml]$project = Get-Content tracker/src/Ironmon.Tracker.App/Ironmon.Tracker.App.csproj -Raw
$version = [string]$project.Project.PropertyGroup.ApplicationDisplayVersion
$snapshot = Get-Content (Join-Path $SnapshotDirectory 'upstream-inputs.json') -Raw | ConvertFrom-Json
$evidence = Join-Path $env:RUNNER_TEMP 'release-evidence'
New-Item -ItemType Directory -Path $evidence | Out-Null
# Explicit data/report types only; do not sweep the game directory or maintainer material.
Copy-Item docs/audits/generated/*_GENERATED.* -Destination $evidence
foreach ($pattern in 'data/*.audit.json', 'data/*.component.json', 'data/generation_profile.json', 'data/*.summary', 'data/*-test.json', 'data/*-probe.json') {
  Get-ChildItem $pattern -File -ErrorAction SilentlyContinue | Copy-Item -Destination $evidence
}
Copy-Item resources/sprites/validated_custom_sprites.json.gz -Destination $evidence
Copy-Item (Join-Path $SnapshotDirectory 'upstream-inputs.json') -Destination $evidence
Copy-Item (Join-Path $env:RUNNER_TEMP 'release-gate.log') -Destination $evidence
Copy-Item "docs/releases/RELEASE_NOTES_$version.md" -Destination $evidence
Compress-Archive -Path "$evidence/*" -DestinationPath release/release-evidence.zip
$roles = Get-UpdateArtifactRoles (Join-Path $projectRoot 'release') $version
$assets = @($roles.Keys | Sort-Object | ForEach-Object {
  $file = Get-Item -LiteralPath (Join-Path $projectRoot "release/$_")
  [ordered]@{ name = $_; role = $roles[$_]; bytes = $file.Length; sha256 = (Get-FileHash $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
})
foreach ($asset in $assets) {
  "$($asset.sha256)  $($asset.name)" | Set-Content (Join-Path 'release' (Get-ReleaseChecksumName $asset.name)) -Encoding utf8NoBOM
}
$manifest = [ordered]@{
  schema_version = 2; version = $version
  source_commit = (git rev-parse HEAD); source_tree = (git rev-parse 'HEAD^{tree}')
  repository = $env:GITHUB_REPOSITORY; run_id = $env:GITHUB_RUN_ID; run_attempt = $env:GITHUB_RUN_ATTEMPT
  inputs = $snapshot; assets = $assets
  generation_cache_sha256 = (Get-FileHash -LiteralPath (Join-Path $projectRoot 'data/ci/generated-cache/manifest.json') -Algorithm SHA256).Hash.ToLowerInvariant()
  profile = (Get-Content data/generation_profile.json -Raw | ConvertFrom-Json)
  fusion_pool = (Get-Content data/generation_custom_fusion_pool.audit.json -Raw | ConvertFrom-Json)
}
$manifest | ConvertTo-Json -Depth 20 | Set-Content release/candidate.json -Encoding utf8NoBOM
. (Join-Path $PSScriptRoot 'Candidate.ps1')
$null = Assert-ReleaseCandidate (Join-Path $projectRoot 'release') $manifest.source_tree $snapshot.fingerprint $version
"Version $version; eligible fusions: $($manifest.fusion_pool.eligible_count); game: $($snapshot.game_commit)" >> $env:GITHUB_STEP_SUMMARY
"Input fingerprint: $($snapshot.fingerprint). Packages, audits and provenance are in the release-candidate artifact." >> $env:GITHUB_STEP_SUMMARY
