<#
.SYNOPSIS
Builds the encrypted achievement pack from an operator-owned private definition file.
.PARAMETER DefinitionPath
The private JSON authoring file outside the public repository.
.PARAMETER OutputPath
The encrypted pack destination; defaults to the canonical embedded asset.
#>
param(
    [Parameter(Mandatory = $true)][string]$DefinitionPath,
    [string]$OutputPath = (Join-Path $PSScriptRoot '../data/achievements.pack')
)
$ErrorActionPreference = 'Stop'
$definitions = @(Get-Content -LiteralPath $DefinitionPath -Raw | ConvertFrom-Json)
if (!$definitions.Count) { throw 'The achievement pack must contain at least one definition.' }
$identities = [Collections.Generic.HashSet[Guid]]::new()
$numbers = [Collections.Generic.HashSet[int]]::new()
foreach ($entry in $definitions) {
    if (!$identities.Add([Guid]::Parse($entry.Id))) { throw 'Duplicate stable achievement identity.' }
    if (!$numbers.Add([int]$entry.Rule.Number) -or $entry.Rule.Number -lt 1 -or $entry.Rule.Number -gt $definitions.Count) { throw 'Achievement numbers must be unique and contiguous from one through the pack count.' }
    if (!$entry.Rule.Name -or !$entry.Rule.Condition -or !$entry.Rule.Group -or !$entry.Rule.Operation -or !$entry.Rule.Measure) { throw 'Incomplete achievement definition.' }
}
$reachable = [Collections.Generic.HashSet[Guid]]::new()
do {
    $changed = $false
    foreach ($entry in $definitions) {
        $id = [Guid]::Parse($entry.Id)
        if ($reachable.Contains($id)) { continue }
        $eligible = $true
        foreach ($gate in $entry.Gates) {
            if (!$gate.Parents.Count) { throw 'An activation group has no parents.' }
            $satisfied = !$gate.Any
            foreach ($parentText in $gate.Parents) {
                $parent = [Guid]::Parse($parentText)
                if (!$identities.Contains($parent)) { throw 'A prerequisite identity is missing.' }
                if ($gate.Any) { $satisfied = $satisfied -or $reachable.Contains($parent) }
                else { $satisfied = $satisfied -and $reachable.Contains($parent) }
            }
            $eligible = $eligible -and $satisfied
        }
        if ($eligible) { $reachable.Add($id) | Out-Null; $changed = $true }
    }
} while ($changed)
if ($reachable.Count -ne $definitions.Count) { throw 'The activation graph has unreachable nodes.' }
$key = [Convert]::FromHexString('93E2B82EA8F9B85E1C1E98493D49BD586333D0A0737C5BD16B4D9E7C8628F239')
$sealed = foreach ($entry in $definitions) {
    $id = [Guid]::Parse($entry.Id)
    $nodeKey = [Security.Cryptography.HMACSHA256]::HashData($key, $id.ToByteArray())
    $plaintext = [Text.Encoding]::UTF8.GetBytes(($entry.Rule | ConvertTo-Json -Depth 64 -Compress))
    # Identical input is reproducible; changed plaintext obtains a different nonce.
    $nonce = [Security.Cryptography.HMACSHA256]::HashData($nodeKey, $plaintext)[0..11]
    $tag = [byte[]]::new(16)
    $ciphertext = [byte[]]::new($plaintext.Length)
    $cipher = [Security.Cryptography.AesGcm]::new($nodeKey, 16)
    try { $cipher.Encrypt([byte[]]$nonce, $plaintext, $ciphertext, $tag, $id.ToByteArray()) }
    finally { $cipher.Dispose(); [Security.Cryptography.CryptographicOperations]::ZeroMemory($plaintext) }
    [ordered]@{ Id=$entry.Id; Gates=@($entry.Gates); Nonce=[Convert]::ToBase64String([byte[]]$nonce); Tag=[Convert]::ToBase64String($tag); Ciphertext=[Convert]::ToBase64String($ciphertext) }
}
$output = $sealed | ConvertTo-Json -Depth 64 -Compress
[IO.File]::WriteAllText([IO.Path]::GetFullPath($OutputPath), $output, [Text.UTF8Encoding]::new($false))
Write-Host "Built $($definitions.Count) independently encrypted achievement definitions."
