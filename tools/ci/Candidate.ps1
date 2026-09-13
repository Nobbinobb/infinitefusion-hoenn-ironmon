. (Join-Path $PSScriptRoot 'UpdateArtifacts.ps1')

function Assert-ReleaseCandidate([string]$Directory, [string]$SourceTree, [string]$InputFingerprint, [string]$Version) {
  <#
  .SYNOPSIS
  Verifies frozen source/input provenance and every role-bound asset before reuse or publication.
  #>
  $manifest = Get-Content -LiteralPath (Join-Path $Directory 'candidate.json') -Raw | ConvertFrom-Json
  if ($manifest.schema_version -ne 2 -or $manifest.source_tree -cne $SourceTree -or
      $manifest.inputs.fingerprint -cne $InputFingerprint -or $manifest.version -cne $Version -or
      $manifest.source_commit -cnotmatch '^[0-9a-f]{40}$' -or $manifest.source_tree -cnotmatch '^[0-9a-f]{40}$' -or
      $manifest.inputs.game_commit -cnotmatch '^[0-9a-f]{40}$' -or $manifest.run_id -notmatch '^\d+$') {
    throw 'Candidate source, version or upstream provenance differs from the requested release.'
  }
  $roles = Get-UpdateArtifactRoles $Directory $Version
  if (@($manifest.assets).Count -ne $roles.Count -or @($manifest.assets.name | Sort-Object -Unique).Count -ne $roles.Count) { throw 'Candidate assets do not match required release roles.' }
  $update = Get-Content -LiteralPath (Join-Path $Directory 'update-manifest.json') -Raw | ConvertFrom-Json
  if ($update.game.preferredCommit -cne $manifest.inputs.game_commit) { throw 'Update metadata refers to different game inputs.' }
  foreach ($asset in $manifest.assets) {
    if (-not $roles.ContainsKey($asset.name) -or $asset.role -cne $roles[$asset.name] -or $asset.sha256 -cnotmatch '^[0-9a-f]{64}$' -or $asset.bytes -le 0) { throw 'Invalid candidate asset.' }
    $path = Join-Path $Directory $asset.name
    if ((Get-Item -LiteralPath $path).Length -ne $asset.bytes -or (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() -cne $asset.sha256) { throw "Candidate checksum mismatch: $($asset.name)." }
    $sidecar = Join-Path $Directory (Get-ReleaseChecksumName $asset.name)
    if ((Get-Content -LiteralPath $sidecar -Raw).Trim() -cne "$($asset.sha256)  $($asset.name)") { throw "Candidate sidecar mismatch: $($asset.name)." }
    $binding = @($update.assets | Where-Object name -CEQ $asset.name)
    if ($binding.Count -eq 1 -and ($binding[0].sha256.ToLowerInvariant() -cne $asset.sha256 -or $binding[0].bytes -ne $asset.bytes)) { throw 'Candidate and updater fingerprints disagree.' }
    if ($asset.role -ceq 'update-data' -and @($update.assets | Where-Object { $_.container -and ($_.container.sha256 -cne $asset.sha256 -or $_.container.bytes -ne $asset.bytes) }).Count) { throw 'Candidate metadata differs from its signed container.' }
  }
  $allowed = @('candidate.json') + @($roles.Keys) + @($roles.Keys | ForEach-Object { Get-ReleaseChecksumName $_ })
  # The publication job authenticates detached signatures against independent public trust.
  if (Test-Path -LiteralPath (Join-Path $Directory 'update-manifest.sig.json')) {
    $allowed += @('update-manifest.sig.json', 'update-manifest.sig.json.sha256.txt', 'SHA256SUMS.txt')
  }
  if (@(Get-ChildItem -LiteralPath $Directory -Force | Where-Object { $_.PSIsContainer -or $_.Name -cnotin $allowed }).Count) { throw 'Unexpected files in the release candidate.' }
  return $manifest
}

