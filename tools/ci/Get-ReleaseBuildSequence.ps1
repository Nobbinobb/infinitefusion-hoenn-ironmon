<#
.SYNOPSIS
Allocates a new helper and manifest sequence above authenticated stable and test builds.
.DESCRIPTION
Reads only signed prior metadata. Test history supplies a sequence watermark,
never adoption inventories. Stable adoption history remains unchanged.
#>
param([Parameter(Mandatory)][ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version, [Parameter(Mandatory)][string]$Tool, [string]$HistoryDirectory)
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true
. (Join-Path $PSScriptRoot 'UpdateArtifacts.ps1')
$projectRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$trust = Join-Path $projectRoot 'resources/updater/trusted-keys.json'
# Sequence 9 was issued for the withdrawn 0.9.0 test release. Removing its
# public release must not allow that signed sequence or helper identity to recur.
$sequence = 10L
if ($HistoryDirectory -and (Test-Path -LiteralPath (Join-Path $HistoryDirectory 'update-manifest.json'))) {
  Invoke-UpdateCandidateTool $Tool @('verify', $HistoryDirectory, $trust, 'history') | Out-Null
  $previous = Get-Content -LiteralPath (Join-Path $HistoryDirectory 'update-manifest.json') -Raw | ConvertFrom-Json
  $sequence = [Math]::Max($sequence, ([long]$previous.releaseSequence + 1))
}
if ($env:GITHUB_ACTIONS -eq 'true') {
  $repository = 'Nobbinobb/infinitefusion-hoenn-ironmon'
  $releases = @(gh api "repos/$repository/releases?per_page=100" | ConvertFrom-Json)
  $test = $releases | Where-Object tag_name -CEQ "v$Version" | Select-Object -First 1
  if ($test) {
    if ($test.immutable -or -not $test.prerelease) { throw 'An immutable or stable release already owns this version.' }
    if (@($test.assets | Where-Object name -CEQ 'update-manifest.sig.json').Count) {
      $directory = Join-Path $projectRoot 'data/updater/previous-test-build'
      New-Item -ItemType Directory -Path $directory | Out-Null
      foreach ($name in 'update-manifest.json','update-manifest.sig.json','update-data.json') {
        $remote = @($test.assets | Where-Object name -CEQ $name)
        if ($remote.Count -ne 1 -or $remote[0].digest -cnotmatch '^sha256:[0-9a-f]{64}$') { throw 'Test history requires independent asset digests.' }
        gh release download "v$Version" --repo $repository --pattern $name --dir $directory
        $path = Join-Path $directory $name
        if ((Get-Item -LiteralPath $path).Length -ne $remote[0].size -or ('sha256:' + (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()) -cne $remote[0].digest) { throw 'Test history differs from GitHub evidence.' }
      }
      Invoke-UpdateCandidateTool $Tool @('verify', $directory, $trust, 'history') | Out-Null
      $previousTest = Get-Content -LiteralPath (Join-Path $directory 'update-manifest.json') -Raw | ConvertFrom-Json
      if ($previousTest.ironmonVersion -cne $Version) { throw 'Test history version differs from its tag.' }
      $sequence = [Math]::Max($sequence, ([long]$previousTest.releaseSequence + 1))
    } elseif (-not $test.draft -or @($test.assets | Where-Object name -CEQ 'update-manifest.json').Count) {
      throw 'Existing test metadata has no signature; resume its original publication job rather than resetting the sequence.'
    }
  }
}
if ($sequence -lt 1 -or $sequence -gt [ushort]::MaxValue) { throw 'The next release sequence cannot be represented in the recovery helper version.' }
return $sequence
