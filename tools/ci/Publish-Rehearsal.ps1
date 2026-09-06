$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true
if ($env:GITHUB_REF -ne 'refs/heads/release-test/integration-20260906') {
  throw 'Publication is restricted to the disposable integration branch.'
}
$candidateCommit = git rev-parse 'HEAD^2'
$mergedTree = git rev-parse 'HEAD^{tree}'
$runs = gh run list --repo $env:GITHUB_REPOSITORY --workflow release-rehearsal.yml --commit $candidateCommit --status success --json databaseId,headSha | ConvertFrom-Json
$candidateRun = $runs | Where-Object headSha -eq $candidateCommit | Select-Object -First 1
if ($null -eq $candidateRun) { throw 'No successful candidate build matches the merged commit.' }
gh run download $candidateRun.databaseId --repo $env:GITHUB_REPOSITORY --name release-candidate --dir candidate
$manifest = Get-Content candidate/candidate.json -Raw | ConvertFrom-Json
if (-not $manifest.rehearsal -or $manifest.source_commit -ne $candidateCommit -or $manifest.source_tree -ne $mergedTree -or $manifest.game_commit -ne $env:GAME_REVISION) {
  throw 'Merged source or game revision differs from the tested candidate.'
}
if (@($manifest.archives).Count -ne 2) { throw 'Expected two validated archives.' }
foreach ($archive in $manifest.archives) {
  if ($archive.name -ne [IO.Path]::GetFileName($archive.name)) { throw 'Invalid archive filename.' }
  $path = Join-Path candidate $archive.name
  if ((Get-FileHash $path -Algorithm SHA256).Hash.ToLowerInvariant() -ne $archive.sha256 -or (Get-Item $path).Length -ne $archive.bytes) {
    throw "Candidate checksum mismatch: $($archive.name)"
  }
}
$tag = "release-test-$($env:GITHUB_RUN_ID)"
@"
Disposable automated release rehearsal. DO NOT INSTALL.

This uses existing version metadata solely to test packaging and publication.
Source commit: $candidateCommit
Merged commit: $env:GITHUB_SHA
Game commit: $env:GAME_REVISION
Candidate run: $($candidateRun.databaseId)
The candidate ZIPs were verified after merge and were not rebuilt.
"@ | Set-Content candidate/REHEARSAL.md
$assets = @(Get-ChildItem candidate -File | Select-Object -ExpandProperty FullName)
gh release create $tag @assets --repo $env:GITHUB_REPOSITORY --target $env:GITHUB_SHA --prerelease --latest=false --title "DISPOSABLE TEST - release automation" --notes-file candidate/REHEARSAL.md
"Published disposable prerelease: $tag from validated run $($candidateRun.databaseId)." >> $env:GITHUB_STEP_SUMMARY
