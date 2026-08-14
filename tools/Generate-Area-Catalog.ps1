param(
    [string]$GameRoot = (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)),
    [string]$OutputPath = (Join-Path (Split-Path -Parent $PSScriptRoot) "data\area_catalog.dat"),
    [string]$AuditPath = (Join-Path (Split-Path -Parent $PSScriptRoot) "docs\audits\AREA_CATALOG_GENERATED.csv"),
    [ValidateRange(1, 600)]
    [int]$TimeoutSeconds = 90,
    [switch]$ShowGameWindow,
    [switch]$ValidateInstalledScripts,
    [switch]$RunAreaProgressTests
)

$ErrorActionPreference = "Stop"

function Assert-PathWithinDirectory {
    param(
        [Parameter(Mandatory)]
        [string]$Path,
        [Parameter(Mandatory)]
        [string]$Directory
    )

    $resolvedPath = [IO.Path]::GetFullPath($Path)
    $resolvedDirectory = [IO.Path]::GetFullPath($Directory).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    if (-not $resolvedPath.StartsWith($resolvedDirectory, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to use '$resolvedPath' outside '$resolvedDirectory'."
    }
}

function ConvertTo-RubyFixnumBytes {
    param(
        [Parameter(Mandatory)]
        [ValidateRange(0, [int]::MaxValue)]
        [int]$Value
    )

    if ($Value -le 122) {
        return [byte[]]@($Value + 5)
    }
    $remaining = $Value
    $valueBytes = [Collections.Generic.List[byte]]::new()
    while ($remaining -gt 0) {
        $valueBytes.Add([byte]($remaining -band 0xff))
        $remaining = $remaining -shr 8
    }
    return [byte[]](@($valueBytes.Count) + $valueBytes.ToArray())
}

function Compress-ZlibBytes {
    param(
        [Parameter(Mandatory)]
        [byte[]]$Bytes
    )

    $compressedStream = [IO.MemoryStream]::new()
    $deflateStream = [IO.Compression.DeflateStream]::new(
        $compressedStream,
        [IO.Compression.CompressionMode]::Compress,
        $true
    )
    try {
        $deflateStream.Write($Bytes, 0, $Bytes.Length)
    }
    finally {
        $deflateStream.Dispose()
    }
    $deflated = $compressedStream.ToArray()
    $compressedStream.Dispose()

    [uint32]$a = 1
    [uint32]$b = 0
    foreach ($value in $Bytes) {
        $a = ($a + $value) % 65521
        $b = ($b + $a) % 65521
    }
    [uint32]$checksum = ($b -shl 16) -bor $a
    return [byte[]](
        @(0x78, 0x9c) +
        $deflated +
        @(
            [byte](($checksum -shr 24) -band 0xff)
            [byte](($checksum -shr 16) -band 0xff)
            [byte](($checksum -shr 8) -band 0xff)
            [byte]($checksum -band 0xff)
        )
    )
}

function New-TemporaryScriptsArchive {
    param(
        [Parameter(Mandatory)]
        [byte[]]$OriginalBytes,
        [Parameter(Mandatory)]
        [string]$RubySource
    )

    $mainName = [Text.Encoding]::ASCII.GetBytes("Main")
    $mainOffset = -1
    for ($index = 0; $index -le $OriginalBytes.Length - $mainName.Length; $index++) {
        $matches = $true
        for ($nameIndex = 0; $nameIndex -lt $mainName.Length; $nameIndex++) {
            if ($OriginalBytes[$index + $nameIndex] -ne $mainName[$nameIndex]) {
                $matches = $false
                break
            }
        }
        if ($matches) {
            $mainOffset = $index
            break
        }
    }
    if ($mainOffset -lt 2 -or $OriginalBytes[$mainOffset - 2] -ne 0x22) {
        throw "The Infinite Fusion Scripts.rxdata Main entry was not recognized."
    }
    $dataMarkerOffset = $mainOffset + $mainName.Length
    if ($OriginalBytes[$dataMarkerOffset] -ne 0x22) {
        throw "The Infinite Fusion Scripts.rxdata Main payload was not recognized."
    }

    $sourceBytes = [Text.UTF8Encoding]::new($false).GetBytes($RubySource)
    $compressedSource = Compress-ZlibBytes -Bytes $sourceBytes
    $lengthBytes = ConvertTo-RubyFixnumBytes -Value $compressedSource.Length
    $prefixLength = $dataMarkerOffset + 1
    $result = [byte[]]::new($prefixLength + $lengthBytes.Length + $compressedSource.Length)
    [Array]::Copy($OriginalBytes, 0, $result, 0, $prefixLength)
    [Array]::Copy($lengthBytes, 0, $result, $prefixLength, $lengthBytes.Length)
    [Array]::Copy($compressedSource, 0, $result, $prefixLength + $lengthBytes.Length, $compressedSource.Length)
    return $result
}

$resolvedGameRoot = [IO.Path]::GetFullPath($GameRoot)
$gameExecutable = Join-Path $resolvedGameRoot "InfiniteFusion2.exe"
if (-not (Test-Path -LiteralPath $gameExecutable)) {
    $gameExecutable = Join-Path $resolvedGameRoot "InfiniteFusion2-performance.exe"
}
if (-not (Test-Path -LiteralPath $gameExecutable)) {
    throw "An Infinite Fusion executable was not found in '$resolvedGameRoot'."
}

$runningGame = Get-Process | Where-Object {
    try { [IO.Path]::GetFullPath($_.Path) -eq [IO.Path]::GetFullPath($gameExecutable) }
    catch { $false }
}
if ($runningGame) {
    throw "Close Infinite Fusion before generating the area catalog."
}
if ($RunAreaProgressTests -and -not $ValidateInstalledScripts) {
    throw "RunAreaProgressTests requires ValidateInstalledScripts."
}

$scriptsArchive = Join-Path $resolvedGameRoot "Data\Scripts.rxdata"
$exporterSource = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot "Export-AreaCatalog.rb"))
$resolvedOutputPath = [IO.Path]::GetFullPath($OutputPath)
$resolvedAuditPath = [IO.Path]::GetFullPath($AuditPath)
Assert-PathWithinDirectory -Path $scriptsArchive -Directory (Join-Path $resolvedGameRoot "Data")
Assert-PathWithinDirectory -Path $exporterSource -Directory $resolvedGameRoot
if (-not (Test-Path -LiteralPath $scriptsArchive)) {
    throw "The Infinite Fusion Scripts.rxdata file was not found."
}
if (-not (Test-Path -LiteralPath $exporterSource)) {
    throw "The area catalog exporter was not found at '$exporterSource'."
}

$originalScriptsArchive = [IO.File]::ReadAllBytes($scriptsArchive)
$bootstrapMarker = "$($resolvedOutputPath.Replace('\', '/')).bootstrap"
$rubyOutputPath = $resolvedOutputPath.Replace('\', '/')
$rubyAuditPath = $resolvedAuditPath.Replace('\', '/')
$rubyGameRoot = $resolvedGameRoot.Replace('\', '/')
$rubyValidateInstalledScripts = if ($ValidateInstalledScripts) { "true" } else { "false" }
$runtimeTestPath = Join-Path $PSScriptRoot "Test-Area-Progress-Runtime.rb"
$runtimeTestSource = if ($RunAreaProgressTests) {
    [IO.File]::ReadAllText($runtimeTestPath, [Text.Encoding]::UTF8)
}
else {
    ""
}
$runtimeTestHex = [BitConverter]::ToString(
    [Text.UTF8Encoding]::new($false).GetBytes($runtimeTestSource)
).Replace("-", "")
$exporterCode = [IO.File]::ReadAllText($exporterSource, [Text.Encoding]::UTF8)
$bootstrapSource = "File.binwrite(`"$bootstrapMarker`", `"bootstrap loaded\n`")`n`$ironmon_area_catalog_output_path = `"$rubyOutputPath`"`n`$ironmon_area_catalog_audit_path = `"$rubyAuditPath`"`n`$ironmon_area_catalog_game_root = `"$rubyGameRoot`"`n`$ironmon_area_catalog_validate_installed_scripts = $rubyValidateInstalledScripts`n`$ironmon_area_catalog_runtime_test_source = [`"$runtimeTestHex`"].pack(`"H*`")`n$exporterCode"
$temporaryScriptsArchive = New-TemporaryScriptsArchive -OriginalBytes $originalScriptsArchive -RubySource $bootstrapSource

New-Item -ItemType Directory -Force -Path (Split-Path -Parent $resolvedOutputPath) | Out-Null
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $resolvedAuditPath) | Out-Null
Remove-Item -LiteralPath "$resolvedOutputPath.progress" -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath "$resolvedOutputPath.error" -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath "$resolvedOutputPath.bootstrap" -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath "$resolvedOutputPath.summary" -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath "$resolvedOutputPath.tests" -Force -ErrorAction SilentlyContinue

$gameProcess = $null
try {
    [IO.File]::WriteAllBytes($scriptsArchive, $temporaryScriptsArchive)
    $startArguments = @{
        FilePath = $gameExecutable
        WorkingDirectory = $resolvedGameRoot
        PassThru = $true
    }
    if (-not $ShowGameWindow) {
        $startArguments.WindowStyle = "Hidden"
    }
    $gameProcess = Start-Process @startArguments
    if (-not $gameProcess.WaitForExit($TimeoutSeconds * 1000)) {
        Stop-Process -Id $gameProcess.Id
        $gameProcess.WaitForExit()
        throw "The game runtime did not finish area catalog extraction within $TimeoutSeconds seconds."
    }
    if ($gameProcess.ExitCode -ne 0) {
        $detail = if (Test-Path -LiteralPath "$resolvedOutputPath.error") {
            Get-Content -LiteralPath "$resolvedOutputPath.error" -Raw
        }
        else {
            "No exporter error report was produced."
        }
        throw "The game runtime area catalog extractor exited with code $($gameProcess.ExitCode). $detail"
    }
}
finally {
    if ($gameProcess -and -not $gameProcess.HasExited) {
        Stop-Process -Id $gameProcess.Id
        $gameProcess.WaitForExit()
    }
    [IO.File]::WriteAllBytes($scriptsArchive, $originalScriptsArchive)
}

if (-not (Test-Path -LiteralPath $resolvedOutputPath) -or
    (Get-Item -LiteralPath $resolvedOutputPath).Length -eq 0) {
    throw "The game runtime did not produce an area catalog."
}
if (-not (Test-Path -LiteralPath $resolvedAuditPath) -or
    (Get-Item -LiteralPath $resolvedAuditPath).Length -eq 0) {
    throw "The game runtime did not produce the area catalog audit."
}
if (-not (Test-Path -LiteralPath "$resolvedOutputPath.summary")) {
    throw "The game runtime did not produce an area catalog summary."
}
if ($RunAreaProgressTests -and
    -not (Test-Path -LiteralPath "$resolvedOutputPath.tests")) {
    throw "The game runtime did not complete the area progress tests."
}
$summary = @{}
Get-Content -LiteralPath "$resolvedOutputPath.summary" | ForEach-Object {
    $parts = $_ -split '=', 2
    if ($parts.Count -eq 2) {
        $summary[$parts[0]] = [int]$parts[1]
    }
}
foreach ($key in "areas", "trainers", "items", "hidden_items") {
    if (-not $summary.ContainsKey($key)) {
        throw "The game runtime area catalog summary is missing '$key'."
    }
}

Remove-Item -LiteralPath "$resolvedOutputPath.progress" -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath "$resolvedOutputPath.error" -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath "$resolvedOutputPath.bootstrap" -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath "$resolvedOutputPath.summary" -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath "$resolvedOutputPath.tests" -Force -ErrorAction SilentlyContinue
Write-Output "Area catalog and audit generated directly from '$resolvedGameRoot': $($summary.areas) areas, $($summary.trainers) trainers, $($summary.items) items ($($summary.hidden_items) hidden)."
