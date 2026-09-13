<#
.SYNOPSIS
Exports the exact generated-data cache key and ignored output directory to Actions.
#>
param([Parameter(Mandatory)][string]$ProjectRoot, [Parameter(Mandatory)][string]$SnapshotDirectory)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'GenerationCache.ps1')
$ProjectRoot = (Resolve-Path -LiteralPath $ProjectRoot).Path
$snapshot = Get-Content (Join-Path $SnapshotDirectory 'upstream-inputs.json') -Raw | ConvertFrom-Json
$key = Get-GenerationCacheKey $ProjectRoot $snapshot.fingerprint
$directory = Join-Path $ProjectRoot 'data/ci/generated-cache'
"key=$key" >> $env:GITHUB_OUTPUT
"directory=$directory" >> $env:GITHUB_OUTPUT
