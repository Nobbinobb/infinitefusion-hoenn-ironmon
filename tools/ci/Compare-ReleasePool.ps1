$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true
. (Join-Path $PSScriptRoot 'FusionPoolComparison.ps1')
$projectRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$releases = gh release list --repo $env:GITHUB_REPOSITORY --limit 1000 --json tagName,isDraft,isPrerelease | ConvertFrom-Json
$previous = $releases | Where-Object { -not $_.isDraft -and -not $_.isPrerelease -and $_.tagName -match '^v\d+\.\d+\.\d+$' } |
  Sort-Object { [version]$_.tagName.Substring(1) } -Descending | Select-Object -First 1
$output = Join-Path $projectRoot 'data/release-pool-comparison.audit.json'
if (-not $previous) {
  @{ baseline = $null; reason = 'No previous stable release exists.' } | ConvertTo-Json | Set-Content $output
  return
}
$directory = Join-Path $env:RUNNER_TEMP 'previous-release-baseline'
$archiveName = "Ironmon-$($previous.tagName)-win-x64-runtime-required.zip"
gh release download $previous.tagName --repo $env:GITHUB_REPOSITORY --pattern $archiveName --dir $directory
$archive = [IO.Compression.ZipFile]::OpenRead((Join-Path $directory $archiveName))
try {
  $entries = @($archive.Entries | Where-Object FullName -eq 'Data/Ironmon/generation_custom_fusion_pool.bin')
  if ($entries.Count -ne 1 -or $entries[0].Length -gt 1MB) { throw 'Previous release has no unambiguous fusion-pool baseline.' }
  $stream = $entries[0].Open()
  $buffer = [IO.MemoryStream]::new()
  try { $stream.CopyTo($buffer); $previousBytes = $buffer.ToArray() } finally { $stream.Dispose(); $buffer.Dispose() }
} finally { $archive.Dispose() }
$currentBytes = [IO.File]::ReadAllBytes((Join-Path $projectRoot 'data/generation_custom_fusion_pool.bin'))
$comparison = Compare-FusionPools $previousBytes $currentBytes
$comparison.baseline = $previous.tagName
$comparison | ConvertTo-Json -Depth 5 | Set-Content $output -Encoding utf8NoBOM
"Fusion pool vs $($previous.tagName): $($comparison.current_count) eligible; +$($comparison.added_count) / -$($comparison.removed_count). Full membership changes are in release-evidence.zip." >> $env:GITHUB_STEP_SUMMARY
if ($comparison.removed_count -gt 0) {
  "Review the removed memberships and upstream provenance. Counts are not forced to increase; legitimate upstream removals remain visible." >> $env:GITHUB_STEP_SUMMARY
}
