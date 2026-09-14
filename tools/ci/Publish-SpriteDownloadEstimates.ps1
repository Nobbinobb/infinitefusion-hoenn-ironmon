<#
.SYNOPSIS
Publishes a complete informational size feed to its dedicated data branch, without modifying source or releases.
#>
param([Parameter(Mandatory)][string]$Path)
$ErrorActionPreference = 'Stop'
if ($env:GITHUB_ACTIONS -cne 'true' -or $env:GITHUB_REF -cne 'refs/heads/main' -or $env:GITHUB_REPOSITORY -cne 'Nobbinobb/infinitefusion-hoenn-ironmon' -or -not $env:GH_TOKEN) { throw 'Sprite estimate publication requires the main-branch GitHub workflow.' }
if ((Get-Item -LiteralPath $Path).Length -gt 65536) { throw 'The estimate feed exceeds its size limit.' }
$bytes = [IO.File]::ReadAllBytes((Resolve-Path -LiteralPath $Path))
$document = [Text.Encoding]::UTF8.GetString($bytes) | ConvertFrom-Json
$now = [DateTimeOffset]::UtcNow
if ($document.schemaVersion -ne 1 -or $document.games.Count -lt 1 -or $document.games.Count -gt 16 -or
    [DateTimeOffset]$document.measuredAt -lt $now.AddHours(-1) -or [DateTimeOffset]$document.measuredAt -gt $now.AddMinutes(15)) { throw 'Only a fresh complete estimate can be published.' }
$seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
if ($document.spriteManifestCommit -and $document.spriteManifestCommit -cnotmatch '^[0-9a-f]{40}$') { throw 'The sprite manifest revision is invalid.' }
foreach ($game in $document.games) {
    foreach ($number in @($game.bytes, $game.availableSheets, $game.unavailableSheets)) {
        if ($number -isnot [int] -and $number -isnot [long]) { throw 'Sprite sizes and counts must be explicit integers.' }
    }
    if ($game.gameCommit -cnotmatch '^[0-9a-f]{40}$' -or -not $seen.Add($game.gameCommit) -or $game.bytes -le 0 -or $game.bytes -gt 107374182400 -or $game.availableSheets -le 0 -or $game.unavailableSheets -lt 0 -or ($game.availableSheets + $game.unavailableSheets) -gt 50000) { throw 'The estimate feed contains invalid or duplicate game measurements.' }
}
$headers = @{ Authorization="Bearer $env:GH_TOKEN"; Accept='application/vnd.github+json'; 'X-GitHub-Api-Version'='2022-11-28' }
$apiRoot = 'https://api.github.com/repos/Nobbinobb/infinitefusion-hoenn-ironmon'
function Invoke-EstimateApi([string]$Method, [string]$Route, $Body) {
    $arguments = @{ Method=$Method; Uri="$apiRoot/$Route"; Headers=$headers; TimeoutSec=30 }
    if ($null -ne $Body) { $arguments.ContentType = 'application/json'; $arguments.Body = $Body | ConvertTo-Json -Depth 8 -Compress }
    Invoke-RestMethod @arguments
}
$reference = $null
try { $reference = Invoke-EstimateApi GET 'git/ref/heads/sprite-size-estimates' } catch {
    if ([int]$_.Exception.Response.StatusCode -ne 404) { throw }
}
$parents = @()
if ($reference) {
    $parents = @($reference.object.sha)
    $previous = Invoke-EstimateApi GET "git/commits/$($parents[0])"
    $tree = Invoke-EstimateApi GET "git/trees/$($previous.tree.sha)"
    if ($tree.truncated -or $tree.tree.Count -ne 1 -or $tree.tree[0].path -cne 'estimates.json' -or $tree.tree[0].type -cne 'blob') { throw 'The estimate branch contains unexpected files; publication stopped.' }
}
$blob = Invoke-EstimateApi POST 'git/blobs' @{content=[Convert]::ToBase64String($bytes);encoding='base64'}
$tree = Invoke-EstimateApi POST 'git/trees' @{tree=@(@{path='estimates.json';mode='100644';type='blob';sha=$blob.sha})}
$commit = Invoke-EstimateApi POST 'git/commits' @{message='Refresh daily sprite download estimates';tree=$tree.sha;parents=$parents}
if ($reference) {
    $null = Invoke-EstimateApi PATCH 'git/refs/heads/sprite-size-estimates' @{sha=$commit.sha;force=$false}
} else {
    $null = Invoke-EstimateApi POST 'git/refs' @{ref='refs/heads/sprite-size-estimates';sha=$commit.sha}
}
Write-Output 'Published the complete daily sprite estimate on its dedicated data branch.'
