<#
.SYNOPSIS
Measures a compressed pack of the exact game snapshot without downloading or changing the repository.
.DESCRIPTION
Packs only the selected commit, its trees and its blobs. History and later branch tips are excluded.
The byte count is a fresh-install estimate, not an exact promise for negotiated Git transfers.
.PARAMETER Repository
The local game checkout used by the release pipeline.
.PARAMETER Commit
The exact game commit already selected for this release.
#>
param([Parameter(Mandatory)][string]$Repository, [Parameter(Mandatory)][string]$Commit)
$ErrorActionPreference = 'Stop'
if ($Commit -cnotmatch '^[0-9a-f]{40}$') { throw 'A full game commit is required for download measurement.' }
$objects = @(& git --no-replace-objects -C $Repository ls-tree -r -t '--format=%(objectname)' $Commit)
if ($LASTEXITCODE -ne 0) { throw 'Cannot enumerate the selected game snapshot.' }
$rootTree = & git --no-replace-objects -C $Repository rev-parse "${Commit}^{tree}"
if ($LASTEXITCODE -ne 0) { throw 'Cannot resolve the selected game tree.' }
$objects = @(@($Commit, $rootTree) + $objects | Sort-Object -Unique)
if (@($objects | Where-Object { $_ -cnotmatch '^[0-9a-f]{40}$' }).Count) { throw 'The game snapshot contains an invalid object identity.' }
$gitExecutable = Get-Command git -CommandType Application | Select-Object -First 1
$start = [Diagnostics.ProcessStartInfo]::new($gitExecutable.Source)
$start.UseShellExecute = $false
$start.CreateNoWindow = $true
$start.RedirectStandardInput = $true
$start.RedirectStandardOutput = $true
$start.RedirectStandardError = $true
foreach ($argument in @('--no-replace-objects', '-C', $Repository, 'pack-objects', '--stdout', '--threads=1', '--delta-base-offset')) {
    $start.ArgumentList.Add($argument)
}
$process = [Diagnostics.Process]::Start($start)
try {
    $errors = $process.StandardError.ReadToEndAsync()
    foreach ($object in $objects) { $process.StandardInput.WriteLine($object) }
    $process.StandardInput.Close()
    $buffer = [byte[]]::new(81920)
    [long]$bytes = 0
    while (($read = $process.StandardOutput.BaseStream.Read($buffer, 0, $buffer.Length)) -gt 0) { $bytes += $read }
    $process.WaitForExit()
    if ($process.ExitCode -ne 0) { throw "Cannot measure the game snapshot: $($errors.GetAwaiter().GetResult())" }
    if ($bytes -le 0) { throw 'The compressed game snapshot is empty.' }
    return $bytes
}
finally {
    if (-not $process.HasExited) { $process.Kill($true); $process.WaitForExit() }
    $process.Dispose()
}
