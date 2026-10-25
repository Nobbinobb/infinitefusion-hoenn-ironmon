<#
.SYNOPSIS
Builds encrypted, embedded achievement artwork from private PNG authoring files.
.PARAMETER DefaultIconPath
Private source PNG shared by achievements without a final icon assignment.
.PARAMETER MappingPath
Optional private JSON array of IconId, Path, and Achievements (stable achievement GUIDs).
PNG sources of at most 50x50 retain their exact bytes and pixels automatically.
Set PreserveSourcePixels to true to require exact-size input and reject larger sources.
.PARAMETER BasePackPath
Existing encrypted pack whose artwork and assignments are retained unless explicitly replaced.
.PARAMETER OutputPath
Encrypted pack destination. Original filenames and artwork are never copied to the distribution.
#>
param(
    [string]$DefaultIconPath,
    [string]$MappingPath,
    [string]$BasePackPath,
    [string]$OutputPath = (Join-Path $PSScriptRoot '../data/achievement-icons.pack')
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$displaySize = 50
$defaultId = 'b47a8a88-415c-4566-ac2e-d639701de372'
$rootKey = [Convert]::FromHexString('93E2B82EA8F9B85E1C1E98493D49BD586333D0A0737C5BD16B4D9E7C8628F239')
$known = [Collections.Generic.HashSet[Guid]]::new()
Get-Content -LiteralPath (Join-Path $PSScriptRoot '../data/achievements.pack') -Raw | ConvertFrom-Json | ForEach-Object { $known.Add([Guid]$_.Id) | Out-Null }
$sealedById = [ordered]@{}
$assignments = [ordered]@{}
if ($BasePackPath) {
    $basePack = Get-Content -LiteralPath $BasePackPath -Raw | ConvertFrom-Json -AsHashtable
    if ($basePack.Version -ne 1) { throw 'Unsupported base icon pack version.' }
    $defaultId = ([Guid]$basePack.DefaultId).ToString()
    foreach ($icon in $basePack.Icons) {
        $id = ([Guid]$icon.Id).ToString()
        if ($sealedById.Contains($id)) { throw 'Duplicate base icon identity.' }
        $sealedById[$id] = $icon
    }
    if (!$sealedById.Contains($defaultId)) { throw 'The base icon pack has no shared default image.' }
    foreach ($assignment in $basePack.Assignments.GetEnumerator()) {
        $achievementId = ([Guid]$assignment.Key).ToString()
        $iconId = ([Guid]$assignment.Value).ToString()
        if (!$known.Contains([Guid]$achievementId) -or !$sealedById.Contains($iconId)) { throw 'Invalid base achievement icon assignment.' }
        $assignments[$achievementId] = $iconId
    }
}
if (!$DefaultIconPath -and !$BasePackPath) { throw 'Supply a default source image or an existing encrypted base pack.' }
$sources = @()
if ($DefaultIconPath) {
    $sources += [pscustomobject]@{ IconId=$defaultId; Path=[IO.Path]::GetFullPath($DefaultIconPath); Achievements=@() }
}
if ($MappingPath) {
    $mappingRoot = Split-Path -Parent ([IO.Path]::GetFullPath($MappingPath))
    foreach ($entry in @(Get-Content -LiteralPath $MappingPath -Raw | ConvertFrom-Json)) {
        if ([Guid]$entry.IconId -eq [Guid]$defaultId) { throw 'Replace shared artwork using DefaultIconPath, not an achievement mapping.' }
        if ($null -ne $entry.PreserveSourcePixels -and $entry.PreserveSourcePixels -isnot [bool]) { throw 'PreserveSourcePixels must be a JSON Boolean.' }
        $sources += [pscustomobject]@{ IconId=([Guid]$entry.IconId).ToString(); Path=[IO.Path]::GetFullPath($entry.Path, $mappingRoot); Achievements=@($entry.Achievements); PreserveSourcePixels=($entry.PreserveSourcePixels -eq $true) }
    }
}
$identities = [Collections.Generic.HashSet[Guid]]::new()
$mappedAchievements = [Collections.Generic.HashSet[Guid]]::new()
foreach ($entry in $sources) {
    $id = [Guid]$entry.IconId
    if (!$identities.Add($id)) { throw 'Duplicate icon identity.' }
    foreach ($achievement in $entry.Achievements) {
        $achievementId = ([Guid]$achievement).ToString()
        if (!$known.Contains([Guid]$achievement) -or !$mappedAchievements.Add([Guid]$achievement)) { throw 'Unknown or duplicate achievement icon assignment.' }
        $assignments[$achievementId] = $id.ToString()
    }
    $source = [Drawing.Bitmap]::FromFile($entry.Path)
    $bitmap = $null
    $graphics = $null
    $png = [IO.MemoryStream]::new()
    $compressed = [IO.MemoryStream]::new()
    try {
        $fitsDisplay = $source.Width -le $displaySize -and $source.Height -le $displaySize
        if ($entry.PreserveSourcePixels -or $fitsDisplay) {
            if ($source.RawFormat.Guid -ne [Drawing.Imaging.ImageFormat]::Png.Guid -or -not $fitsDisplay) { throw 'Exact artwork must be a PNG no larger than 50x50.' }
            $bitmap = [Drawing.Bitmap]$source.Clone()
            $png.Write([IO.File]::ReadAllBytes($entry.Path))
        }
        else {
            $bitmap = [Drawing.Bitmap]::new($displaySize, $displaySize, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
            $graphics = [Drawing.Graphics]::FromImage($bitmap)
            $graphics.Clear([Drawing.Color]::Transparent)
            $graphics.CompositingMode = [Drawing.Drawing2D.CompositingMode]::SourceCopy
            $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $graphics.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::HighQuality
            $scale = [Math]::Min([double]$displaySize / $source.Width, [double]$displaySize / $source.Height)
            $width = [Math]::Max(1, [int][Math]::Round($source.Width * $scale))
            $height = [Math]::Max(1, [int][Math]::Round($source.Height * $scale))
            $graphics.DrawImage($source, [Drawing.Rectangle]::new([int](($displaySize-$width)/2), [int](($displaySize-$height)/2), $width, $height))
            $bitmap.Save($png, [Drawing.Imaging.ImageFormat]::Png)
        }
        $pixels = [byte[]]::new($bitmap.Width * $bitmap.Height * 4)
        for ($y=0; $y -lt $bitmap.Height; $y++) {
            for ($x=0; $x -lt $bitmap.Width; $x++) {
                $color = $bitmap.GetPixel($x, $y)
                $offset = ($y * $bitmap.Width + $x) * 4
                $pixels[$offset]=$color.R; $pixels[$offset+1]=$color.G; $pixels[$offset+2]=$color.B; $pixels[$offset+3]=$color.A
            }
        }
        $deflater = [IO.Compression.ZLibStream]::new($compressed, [IO.Compression.CompressionLevel]::Optimal, $true)
        try { $deflater.Write($pixels) } finally { $deflater.Dispose() }
        $asset = [ordered]@{ Width=$bitmap.Width; Height=$bitmap.Height; Png=[Convert]::ToBase64String($png.ToArray()); Pixels=[Convert]::ToBase64String($compressed.ToArray()) }
        $plaintext = [Text.Encoding]::UTF8.GetBytes(($asset | ConvertTo-Json -Compress))
        $context = [Text.Encoding]::UTF8.GetBytes('achievement-icons-v1:' + $id.ToString())
        $key = [Security.Cryptography.HMACSHA256]::HashData($rootKey, $context)
        $nonce = [Security.Cryptography.HMACSHA256]::HashData($key, $plaintext)[0..11]
        $tag = [byte[]]::new(16)
        $ciphertext = [byte[]]::new($plaintext.Length)
        $cipher = [Security.Cryptography.AesGcm]::new($key, 16)
        try { $cipher.Encrypt([byte[]]$nonce, $plaintext, $ciphertext, $tag, $context) }
        finally { $cipher.Dispose(); [Security.Cryptography.CryptographicOperations]::ZeroMemory($plaintext) }
        $sealedById[$id.ToString()] = [ordered]@{ Id=$id.ToString(); Nonce=[Convert]::ToBase64String([byte[]]$nonce); Tag=[Convert]::ToBase64String($tag); Ciphertext=[Convert]::ToBase64String($ciphertext) }
    }
    finally { if ($graphics) { $graphics.Dispose() }; if ($bitmap) { $bitmap.Dispose() }; $source.Dispose(); $png.Dispose(); $compressed.Dispose() }
}
$pack = [ordered]@{ Version=1; DefaultId=$defaultId; Assignments=$assignments; Icons=@($sealedById.Values) }
[IO.File]::WriteAllText([IO.Path]::GetFullPath($OutputPath), ($pack | ConvertTo-Json -Depth 16 -Compress), [Text.UTF8Encoding]::new($false))
Write-Host "Built $($sealedById.Count) encrypted icons and $($assignments.Count) explicit assignments; remaining achievements use the shared test icon."
