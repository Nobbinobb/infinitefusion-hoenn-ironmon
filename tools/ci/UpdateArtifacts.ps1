function Get-ReleaseChecksumName([string]$Name) {
  <#
  .SYNOPSIS
  Preserves existing ZIP sidecars and gives other release assets unambiguous checksums.
  #>
  if ($Name.EndsWith('.zip', [StringComparison]::Ordinal)) { return $Name.Substring(0, $Name.Length - 4) + '.sha256.txt' }
  return $Name + '.sha256.txt'
}

function Get-UpdateArtifactRoles([string]$Directory, [string]$Version) {
  <#
  .SYNOPSIS
  Reads explicit updater roles without selecting executables by enumeration order.
  #>
  $update = Get-Content -LiteralPath (Join-Path $Directory 'update-manifest.json') -Raw | ConvertFrom-Json
  if ($update.schemaVersion -notin @(1,2) -or $update.documentType -cne 'release' -or $update.ironmonVersion -cne $Version -or
      $update.trackerVersion -cne $Version -or $update.repository -cne 'Nobbinobb/infinitefusion-hoenn-ironmon' -or
      $update.channel -cne 'stable' -or $update.releaseSequence -le 0) { throw 'Invalid candidate update identity.' }
  $roles = [Collections.Generic.Dictionary[string,string]]::new([StringComparer]::Ordinal)
  foreach ($asset in $update.assets) {
    $downloadName = if ($asset.container) { $asset.container.name } else { $asset.name }
    if ($asset.name -cnotmatch '^[A-Za-z0-9][A-Za-z0-9._-]{0,150}$' -or
        $asset.role -cnotin @('tracker-self-contained','tracker-runtime-required','setup','updater','release-notes','ironmon-files','legacy-ironmon-files','game-files') -or
        $asset.url -cne "https://github.com/Nobbinobb/infinitefusion-hoenn-ironmon/releases/download/v$Version/$downloadName") { throw 'Invalid updater artifact role or URL.' }
    if ($asset.container -and ($update.schemaVersion -ne 2 -or $downloadName -cne 'update-data.json' -or $asset.role -cnotin @('ironmon-files','legacy-ironmon-files','game-files','release-notes') -or $asset.container.bytes -le 0 -or $asset.container.bytes -gt 48MB -or $asset.container.sha256 -cnotmatch '^[0-9a-f]{64}$')) { throw 'Invalid signed metadata container.' }
    $roles.Add($asset.name, $asset.role)
  }
  $requiredRoles = @('tracker-self-contained','tracker-runtime-required','setup','release-notes')
  if ($update.schemaVersion -eq 1) { $requiredRoles += 'updater' }
  foreach ($role in $requiredRoles) {
    if (@($roles.Values | Where-Object { $_ -ceq $role }).Count -ne 1) { throw "Missing or duplicate updater role: $role." }
  }
  if (@($roles.Values | Where-Object { $_ -ceq 'ironmon-files' }).Count -ne 2 -or @($roles.Values | Where-Object { $_ -ceq 'game-files' }).Count -lt 1) { throw 'Missing package or game inventories.' }
  if ($update.schemaVersion -eq 2) {
    $containers = @($update.assets | Where-Object container | ForEach-Object { $_.container | ConvertTo-Json -Compress } | Sort-Object -Unique)
    if ($containers.Count -ne 1 -or -not $update.helper -or $roles.Values -ccontains 'updater') { throw 'Compact releases require one metadata container and an embedded helper.' }
    $roles.Add('update-data.json', 'update-data')
  }
  $roles.Add('update-manifest.json', 'update-manifest')
  $roles.Add('update-trusted-keys.json', 'updater-trust')
  $roles.Add('release-evidence.zip', 'evidence')
  return ,$roles
}

function Get-PublishedReleaseFiles([string]$Directory, [string]$Version) {
  <#
  .SYNOPSIS
  Keeps the public release to three player downloads, three updater documents and one checksum list.
  #>
  $update = Get-Content -LiteralPath (Join-Path $Directory 'update-manifest.json') -Raw | ConvertFrom-Json
  if ($update.schemaVersion -ne 2) { throw 'Only compact release metadata may be published.' }
  $null = Get-UpdateArtifactRoles $Directory $Version
  $names = @($update.assets | Where-Object role -CIn @('tracker-self-contained','tracker-runtime-required','setup') | ForEach-Object name)
  $names += @('update-manifest.json','update-manifest.sig.json','update-data.json')
  if ($names.Count -ne 6 -or @($names | Sort-Object -Unique).Count -ne 6) { throw 'The public release file list is incomplete or ambiguous.' }
  return @($names | Sort-Object | ForEach-Object { Get-Item -LiteralPath (Join-Path $Directory $_) })
}

function Write-ReleaseChecksums([string]$Directory, [IO.FileInfo[]]$Files) {
  <#
  .SYNOPSIS
  Writes one deterministic checksum list for the exact public payloads and signed metadata.
  #>
  $path = Join-Path $Directory 'SHA256SUMS.txt'
  $lines = @($Files | Sort-Object Name | ForEach-Object { "$((Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant())  $($_.Name)" })
  [IO.File]::WriteAllText($path, ($lines -join "`n") + "`n", [Text.UTF8Encoding]::new($false))
  return Get-Item -LiteralPath $path
}

function Invoke-UpdateCandidateTool([string]$Tool, [string[]]$Arguments) {
  <#
  .SYNOPSIS
  Runs already-built trusted tools without rebuilding while signing secrets are available.
  #>
  & dotnet $Tool @Arguments
  if ($LASTEXITCODE -ne 0) { throw 'Update release metadata validation failed.' }
}

function Test-ReleaseHistoryCurrent([string]$Directory, [object[]]$PublishedReleases, [string]$Version) {
  <#
  .SYNOPSIS
  Invalidates a candidate when another stable release was published during its build.
  #>
  $latest = $PublishedReleases | Where-Object { -not $_.isDraft -and -not $_.isPrerelease -and $_.tagName -cmatch '^v\d+\.\d+\.\d+$' } | Sort-Object { [version]$_.tagName.Substring(1) } -Descending | Select-Object -First 1
  if ($latest -and $latest.tagName -ceq "v$Version") { return $true }
  $update = Get-Content -LiteralPath (Join-Path $Directory 'update-manifest.json') -Raw | ConvertFrom-Json
  $previous = @($update.adoptionBaselines.legacyPackages.version) | Where-Object { $_ } | Sort-Object { [version]$_ } -Descending | Select-Object -First 1
  if (-not $latest) { return -not $previous }
  return $latest.tagName -ceq "v$previous"
}
