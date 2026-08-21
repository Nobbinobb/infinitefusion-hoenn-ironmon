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

function Invoke-IronmonGameRuntime {
    param(
        [Parameter(Mandatory)]
        [string]$GameRoot,
        [Parameter(Mandatory)]
        [string]$RubySource,
        [Parameter(Mandatory)]
        [ValidateRange(1, 86400)]
        [int]$TimeoutSeconds,
        [Parameter(Mandatory)]
        [string]$OperationName,
        [string]$ErrorReportPath,
        [string]$ProgressPath,
        [string]$ProgressActivity = "Bundled-runtime operation",
        [switch]$ShowGameWindow
    )

    $resolvedGameRoot = [IO.Path]::GetFullPath($GameRoot)
    $candidateExecutables = @(
        (Join-Path $resolvedGameRoot "InfiniteFusion2.exe"),
        (Join-Path $resolvedGameRoot "InfiniteFusion2-performance.exe")
    ) | Where-Object { Test-Path -LiteralPath $_ }
    if ($candidateExecutables.Count -eq 0) {
        throw "An Infinite Fusion executable was not found in '$resolvedGameRoot'."
    }
    $runningGame = Get-Process | Where-Object {
        try {
            $processPath = [IO.Path]::GetFullPath($_.Path)
            $candidateExecutables | Where-Object {
                $processPath.Equals([IO.Path]::GetFullPath($_), [StringComparison]::OrdinalIgnoreCase)
            }
        }
        catch {
            $false
        }
    }
    if ($runningGame) {
        throw "Close Infinite Fusion before $OperationName."
    }

    $gameExecutable = $candidateExecutables[0]
    $scriptsArchive = Join-Path $resolvedGameRoot "Data\Scripts.rxdata"
    Assert-PathWithinDirectory -Path $scriptsArchive -Directory (Join-Path $resolvedGameRoot "Data")
    if (-not (Test-Path -LiteralPath $scriptsArchive)) {
        throw "The Infinite Fusion Scripts.rxdata file was not found."
    }

    $originalScriptsArchive = [IO.File]::ReadAllBytes($scriptsArchive)
    $temporaryScriptsArchive = New-TemporaryScriptsArchive -OriginalBytes $originalScriptsArchive -RubySource $RubySource
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
        $runtimeTimer = [Diagnostics.Stopwatch]::StartNew()
        $lastProgressText = $null
        while (-not $gameProcess.WaitForExit(250)) {
            if ($runtimeTimer.Elapsed.TotalSeconds -ge $TimeoutSeconds) {
                Stop-Process -Id $gameProcess.Id
                $gameProcess.WaitForExit()
                throw "The game runtime did not finish $OperationName within $TimeoutSeconds seconds."
            }
            if ($ProgressPath -and (Test-Path -LiteralPath $ProgressPath)) {
                try {
                    $progressValues = @{}
                    Get-Content -LiteralPath $ProgressPath | ForEach-Object {
                        $parts = $_ -split '=', 2
                        if ($parts.Count -eq 2) {
                            $progressValues[$parts[0]] = $parts[1]
                        }
                    }
                    $workCompleted = 0
                    $workTotal = 0
                    if ([int]::TryParse(
                        $progressValues.work_completed,
                        [ref]$workCompleted
                    ) -and [int]::TryParse(
                        $progressValues.work_total,
                        [ref]$workTotal
                    ) -and $workTotal -gt 0) {
                        $percent = [Math]::Min(
                            100,
                            [Math]::Floor(100 * $workCompleted / $workTotal)
                        )
                        $seedStatus = if ($progressValues.seed) {
                            "Seed $($progressValues.seed)"
                        }
                        else {
                            "Preparing"
                        }
                        $phaseStatus = if ($progressValues.phase) {
                            ": $($progressValues.phase)"
                        }
                        else {
                            ""
                        }
                        $progressText = "$seedStatus$phaseStatus ($percent%)"
                        if ($progressText -ne $lastProgressText) {
                            Write-Progress `
                                -Activity $ProgressActivity `
                                -Status $progressText `
                                -PercentComplete $percent
                            $lastProgressText = $progressText
                        }
                    }
                }
                catch {
                    # The runtime can replace the progress file between reads.
                }
            }
        }
        $gameProcess.WaitForExit()
        $runtimeTimer.Stop()
        if ($ProgressPath) {
            Write-Progress -Activity $ProgressActivity -Completed
        }
        if ($gameProcess.ExitCode -ne 0) {
            $detail = if ($ErrorReportPath -and (Test-Path -LiteralPath $ErrorReportPath)) {
                Get-Content -LiteralPath $ErrorReportPath -Raw
            }
            else {
                "No exporter error report was produced."
            }
            throw "The game runtime $OperationName exited with code $($gameProcess.ExitCode). $detail"
        }
    }
    finally {
        if ($ProgressPath) {
            Write-Progress -Activity $ProgressActivity -Completed
        }
        if ($gameProcess -and -not $gameProcess.HasExited) {
            Stop-Process -Id $gameProcess.Id
            $gameProcess.WaitForExit()
        }
        [IO.File]::WriteAllBytes($scriptsArchive, $originalScriptsArchive)
    }
}
