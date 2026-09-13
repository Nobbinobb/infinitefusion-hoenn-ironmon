<#
.SYNOPSIS
Verifies release-driven inventory generation with two isolated local Git revisions.

.DESCRIPTION
Checks revision changes, frozen commit selection, Unicode paths, raw blob hashes,
manifest consistency and preservation of local changes. No network or game process
is used. Requires PowerShell 7 and Git on the development or CI machine.
#>
$ErrorActionPreference = 'Stop'
$generator = Join-Path $PSScriptRoot 'generation/Generate-Game-Adoption-Inventory.ps1'
$fixtureParent = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$fixtureRoot = Join-Path $fixtureParent ('ironmon-adoption-generation-' + [guid]::NewGuid().ToString('N'))
$repository = Join-Path $fixtureRoot 'game'
$gitExecutable = (Get-Command git -CommandType Application | Select-Object -First 1).Source
$utf8 = [Text.UTF8Encoding]::new($false)

function Invoke-FixtureGit {
    <#
    .SYNOPSIS
    Runs Git only in the disposable repository and rejects command failures.
    .PARAMETER Arguments
    Structured Git command arguments.
    #>
    param([string[]]$Arguments)
    $result = & $gitExecutable -C $repository -c core.hooksPath=disabled-hooks -c commit.gpgsign=false @Arguments
    if ($LASTEXITCODE -ne 0) { throw "Fixture Git failed: $($Arguments -join ' ')" }
    return $result
}

function Read-Inventory {
    <#
    .SYNOPSIS
    Reads generated compressed JSON for fixture assertions.
    .PARAMETER Path
    The compressed inventory path.
    #>
    param([string]$Path)
    $inputStream = [IO.File]::OpenRead($Path)
    try {
        $gzip = [IO.Compression.GZipStream]::new($inputStream, [IO.Compression.CompressionMode]::Decompress)
        try {
            $reader = [IO.StreamReader]::new($gzip, $utf8)
            try { return $reader.ReadToEnd() | ConvertFrom-Json } finally { $reader.Dispose() }
        } finally { $gzip.Dispose() }
    } finally { $inputStream.Dispose() }
}

function Assert-Inventory {
    <#
    .SYNOPSIS
    Checks that both generated resources describe the expected complete revision.
    .PARAMETER Result
    The generator result containing both output paths.
    .PARAMETER Commit
    The selected fixture commit.
    .PARAMETER Count
    The expected tracked-file count.
    #>
    param($Result, [string]$Commit, [int]$Count)
    $manifest = Get-Content -LiteralPath $Result.ManifestPath -Raw | ConvertFrom-Json
    $inventory = Read-Inventory $Result.OutputPath
    if ($inventory.Commit -cne $Commit -or $manifest.Commit -cne $Commit) { throw 'Generation used a different commit.' }
    if ($inventory.Files.Count -ne $Count -or $manifest.FileCount -ne $Count) { throw 'Generation omitted or added files.' }
    if ((Get-FileHash -LiteralPath $Result.OutputPath -Algorithm SHA256).Hash -cne $manifest.Sha256) { throw 'Manifest checksum does not match inventory.' }
    return $inventory
}

try {
    New-Item -ItemType Directory -Path $repository -Force | Out-Null
    Invoke-FixtureGit @('init', '--quiet', '--template=') | Out-Null
    Invoke-FixtureGit @('config', 'core.autocrlf', 'false') | Out-Null
    $unicodeFile = Join-Path $repository 'Trovão.txt'
    $firstBytes = $utf8.GetBytes("first revision`n")
    [IO.File]::WriteAllBytes($unicodeFile, $firstBytes)
    Invoke-FixtureGit @('add', '--all') | Out-Null
    Invoke-FixtureGit @('-c', 'user.name=Fixture', '-c', 'user.email=fixture@example.invalid', 'commit', '--quiet', '-m', 'first') | Out-Null
    $firstCommit = Invoke-FixtureGit @('rev-parse', 'HEAD')
    $first = & $generator -GameRoot $repository -OutputPath (Join-Path $fixtureRoot 'first/hoenn.json.gz')
    $inventory = Assert-Inventory $first $firstCommit 1
    $file = $inventory.Files[0]
    if ($file.Path -cne 'Trovão.txt' -or $file.Canonical.Sha256 -cne [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($firstBytes))) { throw 'Unicode path or raw blob content changed.' }
    $crlf = $utf8.GetBytes("first revision`r`n")
    if ($file.WindowsText.Sha256 -cne [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($crlf))) { throw 'Explicit CRLF alternative is incorrect.' }

    [IO.File]::WriteAllText($unicodeFile, "second revision`n", $utf8)
    [IO.File]::WriteAllText((Join-Path $repository 'added.txt'), "new file`n", $utf8)
    Invoke-FixtureGit @('add', '--all') | Out-Null
    Invoke-FixtureGit @('-c', 'user.name=Fixture', '-c', 'user.email=fixture@example.invalid', 'commit', '--quiet', '-m', 'second') | Out-Null
    $secondCommit = Invoke-FixtureGit @('rev-parse', 'HEAD')
    [IO.File]::WriteAllText($unicodeFile, 'uncommitted local change', $utf8)
    [IO.File]::WriteAllText((Join-Path $repository 'untracked.txt'), 'personal file', $utf8)
    $statusBefore = Invoke-FixtureGit @('status', '--porcelain') | Out-String
    $second = & $generator -GameRoot $repository -OutputPath $first.OutputPath
    $null = Assert-Inventory $second $secondCommit 2
    if ($first.CompressedSha256 -ceq $second.CompressedSha256) { throw 'A new revision did not update the generated inventory.' }

    $shallowRoot = Join-Path $fixtureRoot 'shallow-game'
    $sourceUri = ([uri]::new($repository + [IO.Path]::DirectorySeparatorChar)).AbsoluteUri
    & $gitExecutable clone --quiet --depth=1 --no-local $sourceUri $shallowRoot
    if ($LASTEXITCODE -ne 0) { throw 'Cannot create the shallow CI checkout fixture.' }
    $shallow = & $generator -GameRoot $shallowRoot -OutputPath (Join-Path $fixtureRoot 'shallow/hoenn.json.gz')
    $null = Assert-Inventory $shallow $secondCommit 2
    if ($shallow.CompressedSha256 -cne $second.CompressedSha256) { throw 'Shallow CI checkout generated different inventory bytes.' }

    $frozen = & $generator -GameRoot $repository -GameCommit $firstCommit -OutputPath (Join-Path $fixtureRoot 'frozen/hoenn.json.gz')
    $null = Assert-Inventory $frozen $firstCommit 1
    if ($frozen.CompressedSha256 -cne $first.CompressedSha256) { throw 'Explicit commit selection was affected by newer HEAD or local changes.' }
    $statusAfter = Invoke-FixtureGit @('status', '--porcelain') | Out-String
    if ($statusBefore -cne $statusAfter -or (Get-Content -LiteralPath $unicodeFile -Raw) -cne 'uncommitted local change') { throw 'Generation changed local files or repository state.' }
    Write-Output 'Game inventory generation passed: selected revisions, refreshed metadata, shallow checkout, frozen commits, Unicode, CRLF and local preservation.'
} finally {
    $resolvedFixture = [IO.Path]::GetFullPath($fixtureRoot)
    $parentPrefix = $fixtureParent.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    if (-not $resolvedFixture.StartsWith($parentPrefix, [StringComparison]::OrdinalIgnoreCase)) { throw 'Fixture cleanup escaped its temporary parent.' }
    if (Test-Path -LiteralPath $resolvedFixture) { Remove-Item -LiteralPath $resolvedFixture -Recurse -Force }
}
