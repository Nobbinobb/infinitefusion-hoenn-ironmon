<#
.SYNOPSIS
Builds encrypted, embedded achievement artwork from private PNG authoring files.
.PARAMETER DefaultIconPath
Private source PNG shared by achievements without a final icon assignment.
.PARAMETER MappingPath
Optional private JSON array of IconId, Path, and Achievements (stable achievement GUIDs).
.PARAMETER OutputPath
Encrypted pack destination. Original filenames and artwork are never copied to the distribution.
#>
param(
    [Parameter(Mandatory)][string]$DefaultIconPath,
    [string]$MappingPath,
    [string]$OutputPath = (Join-Path $PSScriptRoot '../data/achievement-icons.pack')
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$defaultId = 'b47a8a88-415c-4566-ac2e-d639701de372'
$rootKey = [Convert]::FromHexString('93E2B82EA8F9B85E1C1E98493D49BD586333D0A0737C5BD16B4D9E7C8628F239')
$known = [Collections.Generic.HashSet[Guid]]::new()
Get-Content -LiteralPath (Join-Path $PSScriptRoot '../data/achievements.pack') -Raw | ConvertFrom-Json | ForEach-Object { $known.Add([Guid]$_.Id) | Out-Null }
$sources = @([pscustomobject]@{ IconId=$defaultId; Path=[IO.Path]::GetFullPath($DefaultIconPath); Achievements=@() })
if ($MappingPath) {
    $mappingRoot = Split-Path -Parent ([IO.Path]::GetFullPath($MappingPath))
    foreach ($entry in @(Get-Content -LiteralPath $MappingPath -Raw | ConvertFrom-Json)) {
        $sources += [pscustomobject]@{ IconId=([Guid]$entry.IconId).ToString(); Path=[IO.Path]::GetFullPath($entry.Path, $mappingRoot); Achievements=@($entry.Achievements) }
    }
}
$identities = [Collections.Generic.HashSet[Guid]]::new()
$assignments = [ordered]@{}
$sealed = foreach ($entry in $sources) {
    $id = [Guid]$entry.IconId
    if (!$identities.Add($id)) { throw 'Duplicate icon identity.' }
    foreach ($achievement in $entry.Achievements) {
        $achievementId = ([Guid]$achievement).ToString()
        if (!$known.Contains([Guid]$achievement) -or $assignments.Contains($achievementId)) { throw 'Unknown or duplicate achievement icon assignment.' }
        $assignments[$achievementId] = $id.ToString()
    }
    $source = [Drawing.Bitmap]::FromFile($entry.Path)
    $bitmap = [Drawing.Bitmap]::new(64, 64, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    $png = [IO.MemoryStream]::new()
    $compressed = [IO.MemoryStream]::new()
    try {
        $graphics.Clear([Drawing.Color]::Transparent)
        $graphics.CompositingMode = [Drawing.Drawing2D.CompositingMode]::SourceCopy
        $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $scale = [Math]::Min(64.0 / $source.Width, 64.0 / $source.Height)
        $width = [Math]::Max(1, [int][Math]::Round($source.Width * $scale))
        $height = [Math]::Max(1, [int][Math]::Round($source.Height * $scale))
        $graphics.DrawImage($source, [Drawing.Rectangle]::new([int]((64-$width)/2), [int]((64-$height)/2), $width, $height))
        $bitmap.Save($png, [Drawing.Imaging.ImageFormat]::Png)
        $pixels = [byte[]]::new(64 * 64 * 4)
        for ($y=0; $y -lt 64; $y++) {
            for ($x=0; $x -lt 64; $x++) {
                $color = $bitmap.GetPixel($x, $y)
                $offset = ($y * 64 + $x) * 4
                $pixels[$offset]=$color.R; $pixels[$offset+1]=$color.G; $pixels[$offset+2]=$color.B; $pixels[$offset+3]=$color.A
            }
        }
        $deflater = [IO.Compression.ZLibStream]::new($compressed, [IO.Compression.CompressionLevel]::Optimal, $true)
        try { $deflater.Write($pixels) } finally { $deflater.Dispose() }
        $asset = [ordered]@{ Width=64; Height=64; Png=[Convert]::ToBase64String($png.ToArray()); Pixels=[Convert]::ToBase64String($compressed.ToArray()) }
        $plaintext = [Text.Encoding]::UTF8.GetBytes(($asset | ConvertTo-Json -Compress))
        $context = [Text.Encoding]::UTF8.GetBytes('achievement-icons-v1:' + $id.ToString())
        $key = [Security.Cryptography.HMACSHA256]::HashData($rootKey, $context)
        $nonce = [Security.Cryptography.HMACSHA256]::HashData($key, $plaintext)[0..11]
        $tag = [byte[]]::new(16)
        $ciphertext = [byte[]]::new($plaintext.Length)
        $cipher = [Security.Cryptography.AesGcm]::new($key, 16)
        try { $cipher.Encrypt([byte[]]$nonce, $plaintext, $ciphertext, $tag, $context) }
        finally { $cipher.Dispose(); [Security.Cryptography.CryptographicOperations]::ZeroMemory($plaintext) }
        [ordered]@{ Id=$id.ToString(); Nonce=[Convert]::ToBase64String([byte[]]$nonce); Tag=[Convert]::ToBase64String($tag); Ciphertext=[Convert]::ToBase64String($ciphertext) }
    }
    finally { $graphics.Dispose(); $bitmap.Dispose(); $source.Dispose(); $png.Dispose(); $compressed.Dispose() }
}
$pack = [ordered]@{ Version=1; DefaultId=$defaultId; Assignments=$assignments; Icons=@($sealed) }
[IO.File]::WriteAllText([IO.Path]::GetFullPath($OutputPath), ($pack | ConvertTo-Json -Depth 16 -Compress), [Text.UTF8Encoding]::new($false))
Write-Host "Built $($sources.Count) encrypted icons and $($assignments.Count) explicit assignments; remaining achievements use the shared test icon."
