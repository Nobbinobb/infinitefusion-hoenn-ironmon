function Assert-ReleaseCandidate([string]$Directory, [string]$SourceTree, [string]$InputFingerprint, [string]$Version) {
  $manifest = Get-Content -LiteralPath (Join-Path $Directory 'candidate.json') -Raw | ConvertFrom-Json
  if ($manifest.schema_version -ne 1 -or $manifest.source_tree -cne $SourceTree -or
      $manifest.inputs.fingerprint -cne $InputFingerprint -or $manifest.version -cne $Version -or
      $manifest.source_commit -cnotmatch '^[0-9a-f]{40}$' -or $manifest.source_tree -cnotmatch '^[0-9a-f]{40}$' -or
      $manifest.inputs.game_commit -cnotmatch '^[0-9a-f]{40}$' -or $manifest.run_id -notmatch '^\d+$') {
    throw 'Candidate source, version or upstream provenance differs from the requested release.'
  }
  $expected = @("Ironmon-v$Version-win-x64.zip", "Ironmon-v$Version-win-x64-runtime-required.zip", 'release-evidence.zip')
  if (@($manifest.assets).Count -ne 3 -or @($manifest.assets.name | Sort-Object -Unique).Count -ne 3) {
    throw 'Expected two player packages and one evidence archive.'
  }
  foreach ($asset in $manifest.assets) {
    if ($asset.name -cnotin $expected -or $asset.sha256 -cnotmatch '^[0-9a-f]{64}$') { throw 'Invalid candidate asset.' }
    $path = Join-Path $Directory $asset.name
    if ((Get-Item -LiteralPath $path).Length -ne $asset.bytes -or
        (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() -cne $asset.sha256) {
      throw "Candidate checksum mismatch: $($asset.name)."
    }
    $checksum = $path -replace '\.zip$', '.sha256.txt'
    if ((Get-Content -LiteralPath $checksum -Raw).Trim() -cne "$($asset.sha256)  $($asset.name)") {
      throw "Candidate sidecar mismatch: $($asset.name)."
    }
  }
  $allowed = @('candidate.json') + $expected + @($expected | ForEach-Object { $_ -replace '\.zip$', '.sha256.txt' })
  if (@(Get-ChildItem -LiteralPath $Directory -Force | Where-Object { $_.PSIsContainer -or $_.Name -cnotin $allowed }).Count) {
    throw 'Unexpected files in the release candidate.'
  }
  return $manifest
}

