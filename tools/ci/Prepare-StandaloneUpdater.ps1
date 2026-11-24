param(
  [Parameter(Mandatory)][ValidatePattern('^[0-9a-f]{40}$')][string]$SourceCommit,
  [Parameter(Mandatory)][ValidatePattern('^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$')][string]$Version,
  [Parameter(Mandatory)][string]$UpdateTool
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true
if ($env:GITHUB_ACTIONS -ne 'true' -or $env:GITHUB_REF -ne 'refs/heads/main') {
  throw 'Production publication runs only from main in GitHub Actions.'
}

$directory = Join-Path $env:GITHUB_WORKSPACE 'candidate'
$trust = Join-Path $env:GITHUB_WORKSPACE 'resources/updater/trusted-keys.json'
$tag = "vupdater-$Version"
$names = @('Ironmon-Standalone-Updater-win-x64.exe', 'updater-bootstrap.json', 'updater-bootstrap.sig.json')
$manifest = Get-Content -LiteralPath (Join-Path $directory 'updater-bootstrap.json') -Raw | ConvertFrom-Json
if ($manifest.version -cne $Version -or $manifest.repository -cne $env:GITHUB_REPOSITORY) {
  throw 'Worker candidate identity differs from the release request.'
}

$existing = @(gh release list --repo $env:GITHUB_REPOSITORY --limit 1000 --json tagName,isDraft,isPrerelease | ConvertFrom-Json)
foreach ($release in $existing) {
  if ($release.tagName -cmatch '^vupdater-(\d+\.\d+\.\d+)$' -and -not $release.isDraft -and [version]$Matches[1] -ge [version]$Version) {
    throw 'The worker version must be greater than every published worker version.'
  }
}

$ref = @(gh api "repos/$env:GITHUB_REPOSITORY/git/matching-refs/tags/$tag" | ConvertFrom-Json) | Where-Object ref -CEQ "refs/tags/$tag" | Select-Object -First 1
if ($ref -and ($ref.object.type -cne 'commit' -or $ref.object.sha -cne $SourceCommit)) {
  throw 'The worker tag does not point to the approved source commit.'
}
if (-not ($existing | Where-Object tagName -CEQ $tag)) {
  if ($ref) { throw 'Worker tag exists without a matching release.' }
  gh release create $tag --repo $env:GITHUB_REPOSITORY --target $SourceCommit --draft --prerelease --latest=false --title "Ironmon standalone updater $Version" --notes "Signed Windows x64 updater for Ironmon trackers with standalone update support. Approved source: $SourceCommit."
}

$releaseId = gh release view $tag --repo $env:GITHUB_REPOSITORY --json databaseId --jq '.databaseId'
if ($releaseId -cnotmatch '^[1-9]\d*$') { throw 'Worker draft identity is unavailable.' }
$releasePath = "repos/$env:GITHUB_REPOSITORY/releases/$releaseId"
$release = gh api $releasePath | ConvertFrom-Json
if ($release.tag_name -cne $tag -or $release.target_commitish -cne $SourceCommit -or -not $release.draft -or -not $release.prerelease -or $release.immutable) {
  throw 'The existing worker is not a private draft at the approved source commit.'
}
if (@($release.assets | Where-Object { $_.name -cnotin $names }).Count) { throw 'Unexpected files in the worker draft.' }

# Keep the exact detached signature when retrying the same candidate.
$signature = $release.assets | Where-Object name -CEQ 'updater-bootstrap.sig.json' | Select-Object -First 1
if ($signature) {
  Invoke-WebRequest -Uri "https://api.github.com/repos/$env:GITHUB_REPOSITORY/releases/assets/$($signature.id)" -Headers @{ Authorization = "Bearer $env:GH_TOKEN"; Accept = 'application/octet-stream' } -OutFile (Join-Path $directory $signature.name)
  $downloaded = Get-FileHash -LiteralPath (Join-Path $directory $signature.name) -Algorithm SHA256
  if ($signature.digest -cne ('sha256:' + $downloaded.Hash.ToLowerInvariant())) { throw 'The saved worker signature differs from GitHub.' }
}
dotnet $UpdateTool worker-sign $directory $trust
dotnet $UpdateTool worker-verify $directory $trust

foreach ($name in $names) {
  $file = Get-Item -LiteralPath (Join-Path $directory $name)
  $expected = 'sha256:' + (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
  $asset = $release.assets | Where-Object name -CEQ $name | Select-Object -First 1
  if (-not $asset) {
    gh release upload $tag $file.FullName --repo $env:GITHUB_REPOSITORY
    $release = gh api $releasePath | ConvertFrom-Json
    $asset = $release.assets | Where-Object name -CEQ $name | Select-Object -First 1
  }
  if ($asset.digest -cne $expected -or $asset.size -ne $file.Length -or $asset.state -cne 'uploaded') {
    throw "Worker draft asset differs from the signed candidate: $name"
  }
}
if (-not $release.draft -or -not $release.prerelease -or $release.immutable -or @($release.assets).Count -ne $names.Count) {
  throw 'Worker draft flags or asset count changed during verification.'
}

"Prepared $tag as a signed draft with three verified assets at source $SourceCommit (release $releaseId)." >> $env:GITHUB_STEP_SUMMARY
