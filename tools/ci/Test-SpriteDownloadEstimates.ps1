<#
.SYNOPSIS
Validates size-feed publication against an in-memory GitHub double; no network or repository mutation is possible.
#>
$ErrorActionPreference = 'Stop'
$fixtureRoot = Join-Path ([IO.Path]::GetTempPath()) ('ironmon-sprite-estimate-tests-' + [guid]::NewGuid().ToString('N'))
$saved = @{}
foreach ($name in @('GITHUB_ACTIONS','GITHUB_REF','GITHUB_REPOSITORY','GH_TOKEN')) { $saved[$name] = [Environment]::GetEnvironmentVariable($name) }
$global:IronmonSpriteEstimateTestState = @{}
$global:IronmonSpriteEstimateTestState.exists = $true
$global:IronmonSpriteEstimateTestState.foreignTree = $false
$global:IronmonSpriteEstimateTestState.calls = [Collections.Generic.List[object]]::new()
function Invoke-RestMethod($Method, $Uri, $Headers, $TimeoutSec, $ContentType, $Body) {
    $route = ([uri]$Uri).AbsolutePath
    if (-not $Uri.StartsWith('https://api.github.com/repos/Nobbinobb/infinitefusion-hoenn-ironmon/', [StringComparison]::Ordinal)) { throw 'Unexpected test API host.' }
    $payload = if ($Body) { $Body | ConvertFrom-Json } else { $null }
    $global:IronmonSpriteEstimateTestState.calls.Add(@{method=$Method;route=$route;body=$payload})
    if ($Method -eq 'GET' -and $route.EndsWith('/git/ref/heads/sprite-size-estimates')) {
        if (-not $global:IronmonSpriteEstimateTestState.exists) { throw [Microsoft.PowerShell.Commands.HttpResponseException]::new('Not found', [Net.Http.HttpResponseMessage]::new([Net.HttpStatusCode]::NotFound)) }
        return @{object=@{sha=('a' * 40)}}
    }
    if ($Method -eq 'GET' -and $route.Contains('/git/commits/')) { return @{tree=@{sha=('b' * 40)}} }
    if ($Method -eq 'GET' -and $route.Contains('/git/trees/')) { return @{truncated=$false;tree=@(@{path=$(if ($global:IronmonSpriteEstimateTestState.foreignTree) {'source.cs'} else {'estimates.json'});type='blob'})} }
    if ($Method -in @('POST','PATCH')) { return @{sha=('c' * 40)} }
    throw 'Unexpected fake API request.'
}
function Assert-Rejected([scriptblock]$Action) {
    $rejected = $false
    try { & $Action | Out-Null } catch { $rejected = $true }
    if (-not $rejected) { throw 'An unsafe sprite publication was accepted.' }
}
try {
    New-Item -ItemType Directory -Path $fixtureRoot | Out-Null
    $path = Join-Path $fixtureRoot 'estimates.json'
    $feed = @{schemaVersion=1;measuredAt=[DateTimeOffset]::UtcNow.ToString('O');games=@(@{gameCommit=('a' * 40);bytes=1234;availableSheets=2;unavailableSheets=1})}
    $feed | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $path
    $env:GITHUB_ACTIONS='true'; $env:GITHUB_REF='refs/heads/main'; $env:GITHUB_REPOSITORY='Nobbinobb/infinitefusion-hoenn-ironmon'; $env:GH_TOKEN='isolated-fake-token'
    $publish = Join-Path $PSScriptRoot 'Publish-SpriteDownloadEstimates.ps1'
    & $publish -Path $path | Out-Null
    $last = $global:IronmonSpriteEstimateTestState.calls[-1]
    if ($last.method -ne 'PATCH' -or -not $last.route.EndsWith('/git/refs/heads/sprite-size-estimates') -or $last.body.force -ne $false) { throw 'Publication did not advance only the dedicated branch without force.' }
    $global:IronmonSpriteEstimateTestState.exists=$false; $global:IronmonSpriteEstimateTestState.calls.Clear()
    & $publish -Path $path | Out-Null
    $commit = $global:IronmonSpriteEstimateTestState.calls | Where-Object { $_.method -eq 'POST' -and $_.route.EndsWith('/git/commits') }
    if ($commit.body.parents.Count -ne 0 -or $global:IronmonSpriteEstimateTestState.calls[-1].body.ref -cne 'refs/heads/sprite-size-estimates') { throw 'Initial publication was not an isolated data branch.' }
    $global:IronmonSpriteEstimateTestState.exists=$true; $global:IronmonSpriteEstimateTestState.foreignTree=$true; $global:IronmonSpriteEstimateTestState.calls.Clear()
    Assert-Rejected { & $publish -Path $path }
    if (@($global:IronmonSpriteEstimateTestState.calls | Where-Object method -ne 'GET').Count) { throw 'Foreign branch content was overwritten.' }
    $global:IronmonSpriteEstimateTestState.foreignTree=$false; $global:IronmonSpriteEstimateTestState.calls.Clear()
    $feed.measuredAt = [DateTimeOffset]::UtcNow.AddDays(-2).ToString('O')
    $feed | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $path
    Assert-Rejected { & $publish -Path $path }
    if ($global:IronmonSpriteEstimateTestState.calls.Count) { throw 'Stale data reached publication.' }
    $env:GITHUB_REF='refs/heads/feature/unreviewed'
    Assert-Rejected { & $publish -Path $path }
    Write-Output 'Sprite estimate publication passed: isolated branch creation, non-forced updates, foreign-content protection, stale-data rejection, and main-only guard.'
} finally {
    Remove-Item Function:Invoke-RestMethod
    Remove-Variable IronmonSpriteEstimateTestState -Scope Global
    foreach ($name in $saved.Keys) { [Environment]::SetEnvironmentVariable($name, $saved[$name]) }
    $resolved = [IO.Path]::GetFullPath($fixtureRoot)
    $tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    if (-not $resolved.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($resolved) -notmatch '^ironmon-sprite-estimate-tests-[0-9a-f]{32}$') { throw 'Unsafe sprite fixture cleanup path.' }
    if (Test-Path -LiteralPath $resolved) { Remove-Item -LiteralPath $resolved -Recurse -Force }
}
