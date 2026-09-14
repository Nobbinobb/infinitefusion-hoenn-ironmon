<#
.SYNOPSIS
Checks that game download estimates measure only the pinned snapshot in an isolated repository.
#>
$ErrorActionPreference = 'Stop'
$fixtureRoot = Join-Path ([IO.Path]::GetTempPath()) ('ironmon-download-estimate-' + [guid]::NewGuid().ToString('N'))
$gitExecutable = (Get-Command git -CommandType Application | Select-Object -First 1).Source
function Invoke-FixtureGit([string[]]$Arguments, [string]$InputText) {
    if ($PSBoundParameters.ContainsKey('InputText')) {
        $result = $InputText | & $gitExecutable -C $fixtureRoot @Arguments
    } else {
        $result = & $gitExecutable -C $fixtureRoot @Arguments
    }
    if ($LASTEXITCODE -ne 0) { throw 'Fixture Git operation failed.' }
    return $result
}
try {
    New-Item -ItemType Directory -Path $fixtureRoot | Out-Null
    $null = & $gitExecutable init --quiet $fixtureRoot
    if ($LASTEXITCODE -ne 0) { throw 'Cannot initialize the measurement fixture.' }
    $null = Invoke-FixtureGit @('config', 'user.name', 'Download estimate test')
    $null = Invoke-FixtureGit @('config', 'user.email', 'estimate@example.invalid')
    $largeFile = Join-Path $fixtureRoot 'payload.bin'
    $payload = [byte[]]::new(1048576)
    [Random]::new(731).NextBytes($payload)
    [IO.File]::WriteAllBytes($largeFile, $payload)
    $largeBlob = Invoke-FixtureGit @('hash-object', '-w', '--', $largeFile)
    $largeTree = Invoke-FixtureGit @('mktree') "100644 blob $largeBlob`tlarge.bin"
    $history = Invoke-FixtureGit @('commit-tree', $largeTree, '-m', 'Large historic snapshot')
    $smallBlob = Invoke-FixtureGit @('hash-object', '-w', '--stdin') 'Small current snapshot'
    $smallTree = Invoke-FixtureGit @('mktree') "100644 blob $smallBlob`tsmall.txt"
    $selected = Invoke-FixtureGit @('commit-tree', $smallTree, '-p', $history, '-m', 'Pinned small snapshot')
    $measure = Join-Path $PSScriptRoot 'Get-GameDownloadEstimate.ps1'
    $smallBytes = & $measure -Repository $fixtureRoot -Commit $selected
    $largeBytes = & $measure -Repository $fixtureRoot -Commit $history
    if ($smallBytes -le 0 -or $smallBytes -ge 8192 -or $largeBytes -lt 1000000) { throw 'The estimate included history or failed to measure the pinned content.' }
    $later = Invoke-FixtureGit @('commit-tree', $largeTree, '-p', $selected, '-m', 'Later branch snapshot')
    $null = Invoke-FixtureGit @('update-ref', 'refs/heads/fixture', $later)
    $repeatedBytes = & $measure -Repository $fixtureRoot -Commit $selected
    if ($repeatedBytes -ne $smallBytes) { throw 'Moving the branch changed the pinned snapshot measurement.' }
    $rejected = $false
    try { $null = & $measure -Repository $fixtureRoot -Commit ('f' * 40) 2>$null } catch { $rejected = $true }
    if (-not $rejected) { throw 'A nonexistent game commit was accepted.' }
    Write-Output 'Game download estimates passed: pinned content, excluded history, later branch tip, and missing commit.'
} finally {
    $resolved = [IO.Path]::GetFullPath($fixtureRoot)
    $tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    if (-not $resolved.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($resolved) -notmatch '^ironmon-download-estimate-[0-9a-f]{32}$') { throw 'Unsafe measurement fixture cleanup path.' }
    if (Test-Path -LiteralPath $resolved) { Remove-Item -LiteralPath $resolved -Recurse -Force }
}
