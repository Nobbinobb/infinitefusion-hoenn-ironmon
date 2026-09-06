param(
  [Parameter(Mandatory)][ValidatePattern('^[0-9a-f]{40}$')][string]$SourceCommit,
  [ValidateRange(0, 3)][int]$RefreshAttempt = 0
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true
if ($env:GITHUB_ACTIONS -ne 'true' -or $env:GITHUB_REF -ne 'refs/heads/main') {
  throw 'Production publication runs only from main in GitHub Actions.'
}
. (Join-Path $PSScriptRoot 'Candidate.ps1')
$sourceRoot = Join-Path $env:GITHUB_WORKSPACE 'source'
$directory = Join-Path $env:GITHUB_WORKSPACE 'candidate'
$actualCommit = git -C $sourceRoot rev-parse HEAD
$tree = git -C $sourceRoot rev-parse 'HEAD^{tree}'
if ($actualCommit -ne $SourceCommit) { throw 'Approved source checkout mismatch.' }
[xml]$project = Get-Content (Join-Path $sourceRoot 'tracker/src/Ironmon.Tracker.App/Ironmon.Tracker.App.csproj') -Raw
$version = [string]$project.Project.PropertyGroup.ApplicationDisplayVersion
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'Invalid release version.' }
$manifest = Get-Content (Join-Path $directory 'candidate.json') -Raw | ConvertFrom-Json
$manifest = Assert-ReleaseCandidate $directory $tree $manifest.inputs.fingerprint $version
if ($manifest.repository -cne $env:GITHUB_REPOSITORY) { throw 'Candidate repository mismatch.' }

# Resolve immediately before publication, after all expensive tests. Never publish
# stale bytes or rebuild inside the write-token job. Dispatch a fresh read-only gate.
$latestPath = Join-Path $env:RUNNER_TEMP 'publication-inputs'
& (Join-Path $PSScriptRoot 'Get-UpstreamInputs.ps1') -OutputDirectory $latestPath
$latest = Get-Content (Join-Path $latestPath 'upstream-inputs.json') -Raw | ConvertFrom-Json
if ($latest.fingerprint -ne $manifest.inputs.fingerprint) {
  if ($RefreshAttempt -ge 3) { throw 'Upstream changed during three consecutive builds. No release was published; rerun after upstream settles.' }
  gh workflow run release.yml --repo $env:GITHUB_REPOSITORY --ref main -f "source_commit=$SourceCommit" -f "refresh_attempt=$($RefreshAttempt + 1)"
  "Upstream changed after validation. Publication deferred; automatically dispatched a full fresh gate for $SourceCommit." >> $env:GITHUB_STEP_SUMMARY
  return
}

$tag = "v$version"
$releases = @(gh release list --repo $env:GITHUB_REPOSITORY --limit 1000 --json tagName,isDraft | ConvertFrom-Json)
$existing = $releases | Where-Object tagName -eq $tag | Select-Object -First 1
if ($releases | Where-Object { -not $_.isDraft -and $_.tagName -match '^v(\d+\.\d+\.\d+)$' -and [version]$Matches[1] -gt [version]$version }) {
  throw 'A newer version is already published; refusing to make this older release latest.'
}
$notes = Join-Path $sourceRoot "docs/releases/RELEASE_NOTES_$version.md"
$releaseNotes = Join-Path $env:RUNNER_TEMP 'release-notes.md'
@(
  (Get-Content -LiteralPath $notes -Raw)
  "`n## Build provenance`n"
  "Approved source: $SourceCommit"
  "Infinite Fusion: $($manifest.inputs.game_commit)"
  "Input fingerprint: $($manifest.inputs.fingerprint)"
  "Verified build: https://github.com/$env:GITHUB_REPOSITORY/actions/runs/$($manifest.run_id)"
  "`nThe evidence archive contains generated audits, test reports and input provenance. SHA-256 sidecars accompany every ZIP."
) | Set-Content -LiteralPath $releaseNotes -Encoding utf8NoBOM
if (-not $existing) {
  # Detect an existing tag before creating a draft; never silently target the wrong source.
  $refs = gh api "repos/$env:GITHUB_REPOSITORY/git/matching-refs/tags/$tag" | ConvertFrom-Json
  if ($refs | Where-Object ref -eq "refs/tags/$tag") { throw 'Release tag already exists without a matching release; manual investigation is required.' }
  gh release create $tag --repo $env:GITHUB_REPOSITORY --target $SourceCommit --draft --title "Ironmon $version" --notes-file $releaseNotes
}
$release = gh api "repos/$env:GITHUB_REPOSITORY/releases/tags/$tag" | ConvertFrom-Json
if ($release.target_commitish -ne $SourceCommit) { throw 'Existing release targets different source.' }
$assets = @(Get-ChildItem -LiteralPath $directory -File)
foreach ($asset in $assets) {
  $remote = $release.assets | Where-Object name -eq $asset.Name | Select-Object -First 1
  $digest = 'sha256:' + (Get-FileHash -LiteralPath $asset.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
  if ($remote) {
    if ($remote.digest -cne $digest -or $remote.size -ne $asset.Length) { throw "Existing release asset differs: $($asset.Name)." }
  } elseif (-not $release.draft) {
    throw 'Published release is incomplete; immutable assets were not modified.'
  } else {
    gh release upload $tag $asset.FullName --repo $env:GITHUB_REPOSITORY
  }
}
$verified = gh api "repos/$env:GITHUB_REPOSITORY/releases/tags/$tag" | ConvertFrom-Json
if (@($verified.assets).Count -ne $assets.Count) { throw 'Unexpected release assets.' }
foreach ($asset in $assets) {
  $remote = $verified.assets | Where-Object name -eq $asset.Name | Select-Object -First 1
  if (-not $remote -or $remote.digest -cne ('sha256:' + (Get-FileHash $asset.FullName -Algorithm SHA256).Hash.ToLowerInvariant()) -or $remote.size -ne $asset.Length) {
    throw 'Uploaded release asset failed independent digest verification.'
  }
}
if ($verified.draft) { gh release edit $tag --repo $env:GITHUB_REPOSITORY --draft=false --latest }
"Published $tag from approved source $SourceCommit using the verified candidate bytes." >> $env:GITHUB_STEP_SUMMARY
