<#
.SYNOPSIS
Builds fixed encrypted 1024px artwork for the tracker's earned-achievement viewer.
.PARAMETER MappingPath
Private JSON array of IconId, Path, and SourceSha256 for approved original PNG masters.
.PARAMETER BasePackRoot
Optional existing artwork directory whose unmapped encrypted images are retained.
.PARAMETER IconPackPath
Validated small-icon candidate supplying the assigned artwork identities; defaults to the canonical pack.
.PARAMETER OutputRoot
New output directory for index.pack and bounded part-NNN.pack files. Existing directories are never overwritten.
#>
param(
    [Parameter(Mandatory)][string]$MappingPath,
    [string]$BasePackRoot,
    [string]$IconPackPath = (Join-Path $PSScriptRoot '../data/achievement-icons.pack'),
    [string]$OutputRoot = (Join-Path $PSScriptRoot '../data/achievement-artwork')
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$resolution = 1024
$maximumPartBytes = 32 * 1024 * 1024
$rootKey = [Convert]::FromHexString('93E2B82EA8F9B85E1C1E98493D49BD586333D0A0737C5BD16B4D9E7C8628F239')
$small = Get-Content -LiteralPath $IconPackPath -Raw | ConvertFrom-Json -AsHashtable
$known = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
foreach ($id in $small.Assignments.Values) { $known.Add($id) | Out-Null }
$records = @{}
if ($BasePackRoot) {
    $inputStream = [IO.File]::OpenRead((Join-Path $BasePackRoot 'index.pack'))
    $reader = [IO.BinaryReader]::new($inputStream)
    try {
        if ([Text.Encoding]::ASCII.GetString($reader.ReadBytes(8)) -cne 'IRMART01' -or $reader.ReadInt32() -ne 1) { throw 'Unsupported artwork index.' }
        $count = $reader.ReadInt32()
        if ($count -lt 1 -or $count -gt 10000 -or $inputStream.Length -ne (16 + $count * 68)) { throw 'Invalid artwork index bounds.' }
        for ($i=0; $i -lt $count; $i++) {
            $id = ([Guid]::new($reader.ReadBytes(16))).ToString()
            if ($records.ContainsKey($id) -or -not $known.Contains($id)) { throw 'Invalid retained artwork identity.' }
            $records[$id] = @{Part=$reader.ReadInt32();Offset=$reader.ReadInt64();Length=$reader.ReadInt32();Width=$reader.ReadInt32();Height=$reader.ReadInt32();Nonce=$reader.ReadBytes(12);Tag=$reader.ReadBytes(16)}
        }
    }
    finally { $reader.Dispose(); $inputStream.Dispose() }
}
$mappingRoot = Split-Path -Parent ([IO.Path]::GetFullPath($MappingPath))
$sources = @{}
foreach ($entry in @(Get-Content -LiteralPath $MappingPath -Raw | ConvertFrom-Json)) {
    $id = ([Guid]$entry.IconId).ToString()
    if (-not $known.Contains($id) -or $sources.ContainsKey($id)) { throw 'Unknown or duplicate assigned artwork identity.' }
    $path = [IO.Path]::GetFullPath($entry.Path,$mappingRoot)
    if (-not $entry.SourceSha256 -or (Get-FileHash -LiteralPath $path).Hash -ne $entry.SourceSha256) { throw 'Approved master hash differs.' }
    $sources[$id] = @{Path=$path;SourceSha256=$entry.SourceSha256}
    $records[$id] = $null
}
if ($records.Count -eq 0) { throw 'No assigned artwork was supplied.' }
$output = [IO.Path]::GetFullPath($OutputRoot)
if (Test-Path -LiteralPath $output) { throw 'Use a new output directory and verify the candidate before publishing.' }
New-Item -ItemType Directory -Path $output -Force | Out-Null
$part = 0
$partStream = $null
$written = [ordered]@{}
try {
    foreach ($id in @($records.Keys | Sort-Object)) {
        if ($sources.ContainsKey($id)) {
            $source = $sources[$id]
            if ((Get-FileHash -LiteralPath $source.Path).Hash -ne $source.SourceSha256) { throw 'Approved master changed during the build.' }
            $original = [Drawing.Bitmap]::FromFile($source.Path)
            $bitmap = [Drawing.Bitmap]::new($resolution,$resolution,[Drawing.Imaging.PixelFormat]::Format32bppArgb)
            $graphics = [Drawing.Graphics]::FromImage($bitmap)
            $png = [IO.MemoryStream]::new()
            try {
                if ($original.RawFormat.Guid -ne [Drawing.Imaging.ImageFormat]::Png.Guid -or [Math]::Max($original.Width,$original.Height) -lt $resolution) { throw 'Supply the approved large PNG master, not its thumbnail.' }
                $graphics.Clear([Drawing.Color]::Transparent)
                $graphics.CompositingMode = [Drawing.Drawing2D.CompositingMode]::SourceCopy
                $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
                $graphics.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::HighQuality
                $scale = [Math]::Min([double]$resolution/$original.Width,[double]$resolution/$original.Height)
                $width = [Math]::Max(1,[int][Math]::Round($original.Width*$scale))
                $height = [Math]::Max(1,[int][Math]::Round($original.Height*$scale))
                $graphics.DrawImage($original,[Drawing.Rectangle]::new([int](($resolution-$width)/2),[int](($resolution-$height)/2),$width,$height))
                $bitmap.Save($png,[Drawing.Imaging.ImageFormat]::Png)
                $plaintext = $png.ToArray()
            }
            finally { $graphics.Dispose(); $bitmap.Dispose(); $original.Dispose(); $png.Dispose() }
            $context = [Text.Encoding]::UTF8.GetBytes('achievement-artwork-detail-v1:'+$id)
            $key = [Security.Cryptography.HMACSHA256]::HashData($rootKey,$context)
            $nonce = [byte[]]([Security.Cryptography.HMACSHA256]::HashData($key,$plaintext)[0..11])
            $tag = [byte[]]::new(16)
            $ciphertext = [byte[]]::new($plaintext.Length)
            $cipher = [Security.Cryptography.AesGcm]::new($key,16)
            try { $cipher.Encrypt($nonce,$plaintext,$ciphertext,$tag,$context) }
            finally { $cipher.Dispose(); [Security.Cryptography.CryptographicOperations]::ZeroMemory($plaintext); [Security.Cryptography.CryptographicOperations]::ZeroMemory($key) }
            $record = @{Width=$resolution;Height=$resolution;Nonce=$nonce;Tag=$tag}
        }
        else {
            $record = $records[$id]
            $input = [IO.File]::OpenRead((Join-Path $BasePackRoot ('part-{0:D3}.pack' -f $record.Part)))
            try {
                if ($record.Width -ne $resolution -or $record.Height -ne $resolution -or $record.Length -lt 24 -or $record.Length -gt 8*1024*1024 -or $record.Offset -lt 0 -or $record.Offset -gt $input.Length-$record.Length) { throw 'Invalid retained artwork bounds.' }
                $input.Position = $record.Offset
                $ciphertext = [byte[]]::new($record.Length)
                $input.ReadExactly($ciphertext)
            }
            finally { $input.Dispose() }
        }
        if ($null -eq $partStream -or $partStream.Position+$ciphertext.Length -gt $maximumPartBytes) {
            if ($partStream) { $partStream.Dispose(); $part++ }
            $partStream = [IO.File]::Open((Join-Path $output ('part-{0:D3}.pack' -f $part)),[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
        }
        $written[$id] = @{Part=$part;Offset=$partStream.Position;Length=$ciphertext.Length;Width=$record.Width;Height=$record.Height;Nonce=$record.Nonce;Tag=$record.Tag}
        $partStream.Write($ciphertext)
    }
}
finally { if ($partStream) { $partStream.Dispose() } }
$indexStream = [IO.File]::Open((Join-Path $output 'index.pack'),[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
$writer = [IO.BinaryWriter]::new($indexStream)
try {
    $writer.Write([Text.Encoding]::ASCII.GetBytes('IRMART01'))
    $writer.Write([int]1)
    $writer.Write([int]$written.Count)
    foreach ($id in $written.Keys) {
        $record = $written[$id]
        $writer.Write(([Guid]$id).ToByteArray())
        $writer.Write([int]$record.Part); $writer.Write([long]$record.Offset); $writer.Write([int]$record.Length)
        $writer.Write([int]$record.Width); $writer.Write([int]$record.Height)
        $writer.Write([byte[]]$record.Nonce); $writer.Write([byte[]]$record.Tag)
    }
}
finally { $writer.Dispose(); $indexStream.Dispose() }
Write-Host "Built $($written.Count) encrypted 1024x1024 detail images across $($part+1) bounded parts."
