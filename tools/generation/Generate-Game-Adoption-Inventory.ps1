<#
.SYNOPSIS
Generates the Hoenn ZIP-adoption inventory for the release's selected game revision.

.DESCRIPTION
Reads the complete selected tree and raw blobs without checking out or changing the
source repository. Every blob is checked against its Git object ID. The output
contains SHA-256 fingerprints, plus explicit CRLF alternatives for known UTF-8
text files. A companion manifest records the commit, file count and compressed
checksum; both outputs are embedded together. Build-TrackerRelease invokes this generator
as part of the catalog/release generation pipeline. Ordinary project builds only
consume the generated data. Requires PowerShell 7 and Git on the generation
machine; players use the private Git provider.

.PARAMETER GameRoot
The local official game checkout selected by the release workflow.

.PARAMETER GameCommit
The exact selected commit. Defaults to the game checkout's HEAD, which the hosted
workflow checks out from its resolved upstream inputs. Does not resolve upstream again.

.PARAMETER OutputPath
The destination gzip-compressed JSON inventory. Defaults to the repository's
ignored data/updater/baselines/hoenn.json.gz. Existing output is replaced.

.PARAMETER ManifestPath
The companion manifest destination. Defaults to hoenn.manifest.json beside the inventory.

.EXAMPLE
./tools/generation/Generate-Game-Adoption-Inventory.ps1 -GameRoot ..
#>
param(
    [string]$GameRoot = (Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $PSScriptRoot))),
    [string]$GameCommit,
    [string]$OutputPath = (Join-Path (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)) 'data/updater/baselines/hoenn.json.gz'),
    [string]$ManifestPath
)

$ErrorActionPreference = 'Stop'
$sourceRoot = (Resolve-Path -LiteralPath $GameRoot).Path
$destination = [IO.Path]::GetFullPath($OutputPath)
if (-not $ManifestPath) { $ManifestPath = Join-Path ([IO.Path]::GetDirectoryName($destination)) 'hoenn.manifest.json' }
$manifestDestination = [IO.Path]::GetFullPath($ManifestPath)
if ($destination.Equals($manifestDestination, [StringComparison]::OrdinalIgnoreCase)) { throw 'Inventory and manifest destinations must be different.' }
$gitExecutable = (Get-Command git -CommandType Application | Select-Object -First 1).Source
if (-not $GameCommit) {
    $GameCommit = & $gitExecutable --no-replace-objects -C $sourceRoot rev-parse --verify 'HEAD^{commit}'
    if ($LASTEXITCODE -ne 0) { throw 'Cannot resolve the selected game checkout revision.' }
}
if ($GameCommit -cnotmatch '\A[0-9a-f]{40}\z') { throw 'An exact lowercase game commit is required.' }
$commit = $GameCommit
$objectType = & $gitExecutable --no-replace-objects -C $sourceRoot cat-file -t $commit
if ($LASTEXITCODE -ne 0 -or $objectType -ne 'commit') { throw 'The selected game revision must identify a commit object.' }
& $gitExecutable --no-replace-objects -C $sourceRoot fsck --strict --no-reflogs $commit | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'The selected game objects failed integrity verification.' }
$treeStart = [Diagnostics.ProcessStartInfo]::new($gitExecutable)
$treeStart.UseShellExecute = $false
$treeStart.CreateNoWindow = $true
$treeStart.RedirectStandardOutput = $true
$treeStart.RedirectStandardError = $true
$treeStart.StandardOutputEncoding = [Text.UTF8Encoding]::new($false, $true)
foreach ($argument in @('--no-replace-objects', '-c', 'core.quotepath=false', '-C', $sourceRoot, 'ls-tree', '-rl', $commit)) { $treeStart.ArgumentList.Add($argument) }
$treeProcess = [Diagnostics.Process]::Start($treeStart)
try {
    $treeOutput = $treeProcess.StandardOutput.ReadToEndAsync()
    $treeError = $treeProcess.StandardError.ReadToEndAsync()
    if (-not $treeProcess.WaitForExit(120000)) { throw 'Reading the selected game tree timed out.' }
    if ($treeProcess.ExitCode -ne 0) { throw "Cannot read the selected game tree: $($treeError.GetAwaiter().GetResult())" }
    $rows = @($treeOutput.GetAwaiter().GetResult() -split '\r?\n' | Where-Object { $_.Length -gt 0 })
}
finally {
    if (-not $treeProcess.HasExited) { $treeProcess.Kill($true); $treeProcess.WaitForExit() }
    $treeProcess.Dispose()
}
$textExtensions = @('.rb', '.txt', '.ini', '.json', '.csv', '.md', '.yml', '.yaml', '.xml', '.html', '.css', '.js', '.bat', '.ps1')
$utf8 = [Text.UTF8Encoding]::new($false, $true)
$files = [Collections.Generic.List[object]]::new()
$start = [Diagnostics.ProcessStartInfo]::new($gitExecutable)
$start.UseShellExecute = $false
$start.CreateNoWindow = $true
$start.RedirectStandardInput = $true
$start.RedirectStandardOutput = $true
$start.Environment['GIT_NO_LAZY_FETCH'] = '1'
foreach ($argument in @('--no-replace-objects', '-C', $sourceRoot, 'cat-file', '--batch')) { $start.ArgumentList.Add($argument) }
$process = [Diagnostics.Process]::Start($start)
try {
    foreach ($row in $rows) {
        if ($row -notmatch '^(100644|100755) blob ([0-9a-f]{40})\s+(\d+)\t([^"\t\r\n]+)$') { throw "Unsupported tree entry: $row" }
        $mode = $Matches[1]
        $blob = $Matches[2]
        $length = [int]$Matches[3]
        $relative = $Matches[4]
        if ($length -gt 268435456) { throw 'A blob exceeds the generation memory limit.' }
        $process.StandardInput.WriteLine($blob)
        $process.StandardInput.Flush()
        $header = [Text.StringBuilder]::new()
        do {
            $next = $process.StandardOutput.BaseStream.ReadByte()
            if ($next -lt 0) { throw 'Unexpected end of Git output.' }
            if ($next -ne 10) { [void]$header.Append([char]$next) }
            if ($header.Length -gt 100) { throw 'Invalid Git object header.' }
        } while ($next -ne 10)
        if ($header.ToString() -ne "$blob blob $length") { throw 'Git returned an unexpected blob.' }
        $bytes = [byte[]]::new($length)
        $process.StandardOutput.BaseStream.ReadExactly($bytes)
        if ($process.StandardOutput.BaseStream.ReadByte() -ne 10) { throw 'Invalid Git object delimiter.' }
        $objectHash = [Security.Cryptography.IncrementalHash]::CreateHash([Security.Cryptography.HashAlgorithmName]::SHA1)
        try {
            $objectHash.AppendData([Text.Encoding]::ASCII.GetBytes("blob $length`0"))
            $objectHash.AppendData($bytes)
            if ([Convert]::ToHexString($objectHash.GetHashAndReset()).ToLowerInvariant() -ne $blob) { throw 'Blob integrity verification failed.' }
        }
        finally { $objectHash.Dispose() }
        $canonical = [ordered]@{ Length = $length; Sha256 = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)) }
        $windowsText = $null
        if ($length -le 4194304 -and [IO.Path]::GetExtension($relative).ToLowerInvariant() -in $textExtensions) {
            try { $text = $utf8.GetString($bytes) } catch [Text.DecoderFallbackException] { $text = $null }
            if ($null -ne $text -and $text.Contains("`n") -and $text -notmatch '[\x00-\x08\x0B-\x0D\x0E-\x1F]') {
                $crlf = $utf8.GetBytes($text.Replace("`n", "`r`n"))
                $windowsText = [ordered]@{ Length = $crlf.Length; Sha256 = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($crlf)) }
            }
        }
        $files.Add([ordered]@{ Path = $relative; Mode = $mode; Blob = $blob; Canonical = $canonical; WindowsText = $windowsText })
    }
    $process.StandardInput.Close()
    if (-not $process.WaitForExit(10000) -or $process.ExitCode -ne 0) { throw 'Git object reading did not complete successfully.' }
}
finally {
    if (-not $process.HasExited) { $process.Kill($true); $process.WaitForExit() }
    $process.Dispose()
}

if ($files.Count -eq 0) { throw 'The selected game tree contains no files.' }
$json = ConvertTo-Json -InputObject ([ordered]@{ Commit = $commit; Files = @($files.ToArray()) }) -Depth 6 -Compress
$encoded = $utf8.GetBytes($json)
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($destination)) | Out-Null
$output = [IO.File]::Create($destination)
try {
    $gzip = [IO.Compression.GZipStream]::new($output, [IO.Compression.CompressionLevel]::SmallestSize, $true)
    try { $gzip.Write($encoded) } finally { $gzip.Dispose() }
}
finally { $output.Dispose() }
$compressedHash = (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash
$manifest = [ordered]@{ Commit = $commit; FileCount = $files.Count; Sha256 = $compressedHash }
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($manifestDestination)) | Out-Null
[IO.File]::WriteAllText($manifestDestination, (ConvertTo-Json -InputObject $manifest -Compress), $utf8)
[pscustomobject]@{
    Commit = $commit
    Files = $files.Count
    CanonicalBytes = ($files | ForEach-Object { $_.Canonical.Length } | Measure-Object -Sum).Sum
    WindowsTextVariants = @($files | Where-Object { $null -ne $_.WindowsText }).Count
    JsonSha256 = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($encoded))
    CompressedSha256 = $compressedHash
    OutputPath = $destination
    ManifestPath = $manifestDestination
}
