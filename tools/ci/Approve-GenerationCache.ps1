<#
.SYNOPSIS
Verifies the merged candidate's generation artifact before sharing its cache from main.
.DESCRIPTION
Binds the artifact manifest to the already verified release candidate, then checks
current generator inputs and every generated file before the cache-save step.
#>
param([Parameter(Mandatory)][string]$SnapshotDirectory)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'GenerationCache.ps1')
$projectRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$directory = Join-Path $projectRoot 'data/ci/generated-cache'
$candidate = Get-Content (Join-Path $projectRoot 'release/candidate.json') -Raw | ConvertFrom-Json
$manifest = Assert-GenerationCachePath $directory 'manifest.json'
if ($candidate.generation_cache_sha256 -cnotmatch '^[0-9a-f]{64}$' -or
    (Get-FileHash -LiteralPath $manifest -Algorithm SHA256).Hash.ToLowerInvariant() -cne $candidate.generation_cache_sha256) { throw 'Generated catalogs differ from the approved candidate.' }
$snapshot = Get-Content (Join-Path $SnapshotDirectory 'upstream-inputs.json') -Raw | ConvertFrom-Json
$key = Get-GenerationCacheKey $projectRoot $snapshot.fingerprint
if (-not (Restore-GenerationCache $projectRoot $directory $key)) { throw 'Approved generated catalogs are unavailable.' }
"key=$key" >> $env:GITHUB_OUTPUT
"directory=$directory" >> $env:GITHUB_OUTPUT
