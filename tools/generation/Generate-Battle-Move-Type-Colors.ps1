param(
    [Parameter(Mandatory = $true)]
    [string]$GameRoot,
    [Parameter(Mandatory = $true)]
    [string]$OutputDirectory,
    [string]$PalettePath = (Join-Path $PSScriptRoot "Battle-Move-Type-Palette.json")
)

$ErrorActionPreference = "Stop"

Add-Type -AssemblyName System.Drawing

$buttonWidth = 384
$buttonHeight = 46
$typeRowCount = 19
$baseColorX = 20
$baseColorY = 24
$sourceDirectory = Join-Path $GameRoot "Graphics\Pictures\Battle"
$sourceNames = @("cursor_fight.png", "cursor_fight_dark.png")
$palette = @(Get-Content -LiteralPath $PalettePath -Raw | ConvertFrom-Json)

if ($palette.Count -ne 18) {
    throw "The battle move palette must define exactly 18 types."
}

$paletteRows = @{}
foreach ($entry in $palette) {
    $row = [int]$entry.row
    if ($row -lt 0 -or $row -ge $typeRowCount -or $row -eq 9) {
        throw "Invalid battle move palette row '$row'."
    }
    if ($paletteRows.ContainsKey($row)) {
        throw "Battle move palette row '$row' is defined more than once."
    }
    if ($entry.color -notmatch '^#[0-9a-fA-F]{6}$') {
        throw "Invalid battle move palette color '$($entry.color)'."
    }
    $paletteRows[$row] = $entry
}

function Get-ColorKey {
    param([System.Drawing.Color]$Color)

    return "{0:X2}{1:X2}{2:X2}{3:X2}" -f `
        $Color.A, $Color.R, $Color.G, $Color.B
}

function ConvertTo-Hsl {
    param([System.Drawing.Color]$Color)

    $red = $Color.R / 255.0
    $green = $Color.G / 255.0
    $blue = $Color.B / 255.0
    $maximum = [Math]::Max($red, [Math]::Max($green, $blue))
    $minimum = [Math]::Min($red, [Math]::Min($green, $blue))
    $delta = $maximum - $minimum
    $lightness = ($maximum + $minimum) / 2.0
    $hue = 0.0
    $saturation = 0.0
    if ($delta -gt 0.0) {
        $saturation = if ($lightness -gt 0.5) {
            $delta / (2.0 - $maximum - $minimum)
        } else {
            $delta / ($maximum + $minimum)
        }
        if ($maximum -eq $red) {
            $hue = (($green - $blue) / $delta) + $(if ($green -lt $blue) { 6.0 } else { 0.0 })
        } elseif ($maximum -eq $green) {
            $hue = (($blue - $red) / $delta) + 2.0
        } else {
            $hue = (($red - $green) / $delta) + 4.0
        }
        $hue /= 6.0
    }
    return [pscustomobject]@{
        Hue = $hue
        Saturation = $saturation
        Lightness = $lightness
    }
}

function ConvertFrom-Hsl {
    param(
        [double]$Hue,
        [double]$Saturation,
        [double]$Lightness,
        [byte]$Alpha
    )

    function Convert-HueChannel {
        param(
            [double]$First,
            [double]$Second,
            [double]$Channel
        )

        if ($Channel -lt 0.0) { $Channel += 1.0 }
        if ($Channel -gt 1.0) { $Channel -= 1.0 }
        if ($Channel -lt (1.0 / 6.0)) {
            return $First + (($Second - $First) * 6.0 * $Channel)
        }
        if ($Channel -lt 0.5) { return $Second }
        if ($Channel -lt (2.0 / 3.0)) {
            return $First + (($Second - $First) * ((2.0 / 3.0) - $Channel) * 6.0)
        }
        return $First
    }

    if ($Saturation -le 0.0) {
        $red = $Lightness
        $green = $Lightness
        $blue = $Lightness
    } else {
        $second = if ($Lightness -lt 0.5) {
            $Lightness * (1.0 + $Saturation)
        } else {
            $Lightness + $Saturation - ($Lightness * $Saturation)
        }
        $first = (2.0 * $Lightness) - $second
        $red = Convert-HueChannel $first $second ($Hue + (1.0 / 3.0))
        $green = Convert-HueChannel $first $second $Hue
        $blue = Convert-HueChannel $first $second ($Hue - (1.0 / 3.0))
    }
    return [System.Drawing.Color]::FromArgb(
        $Alpha,
        [Math]::Round($red * 255.0),
        [Math]::Round($green * 255.0),
        [Math]::Round($blue * 255.0)
    )
}

function ConvertFrom-HexColor {
    param([string]$HexColor)

    return [System.Drawing.Color]::FromArgb(
        [Convert]::ToInt32($HexColor.Substring(1, 2), 16),
        [Convert]::ToInt32($HexColor.Substring(3, 2), 16),
        [Convert]::ToInt32($HexColor.Substring(5, 2), 16)
    )
}

function Get-CommonRowColors {
    param([System.Drawing.Bitmap]$Bitmap)

    $common = $null
    for ($row = 0; $row -lt $typeRowCount; $row++) {
        $rowColors = New-Object 'System.Collections.Generic.HashSet[string]'
        for ($y = $row * $buttonHeight; $y -lt ($row + 1) * $buttonHeight; $y++) {
            for ($x = 0; $x -lt $Bitmap.Width; $x++) {
                [void]$rowColors.Add((Get-ColorKey $Bitmap.GetPixel($x, $y)))
            }
        }
        if ($null -eq $common) {
            $common = New-Object 'System.Collections.Generic.HashSet[string]' ($rowColors)
        } else {
            $common.IntersectWith($rowColors)
        }
    }
    return $common
}

function New-TypeShadeMap {
    param(
        [System.Drawing.Bitmap]$Bitmap,
        [System.Collections.Generic.HashSet[string]]$CommonColors,
        [int]$TemplateRow,
        [System.Drawing.Color]$TargetColor
    )

    $templateBase = ConvertTo-Hsl $Bitmap.GetPixel(
        $baseColorX,
        ($TemplateRow * $buttonHeight) + $baseColorY
    )
    $target = ConvertTo-Hsl $TargetColor
    $map = @{}
    for ($y = $TemplateRow * $buttonHeight; $y -lt ($TemplateRow + 1) * $buttonHeight; $y++) {
        for ($x = 0; $x -lt $Bitmap.Width; $x++) {
            $sourceColor = $Bitmap.GetPixel($x, $y)
            $key = Get-ColorKey $sourceColor
            if ($CommonColors.Contains($key) -or $map.ContainsKey($key)) {
                continue
            }
            $source = ConvertTo-Hsl $sourceColor
            $saturationScale = if ($templateBase.Saturation -gt 0.0) {
                $source.Saturation / $templateBase.Saturation
            } else {
                1.0
            }
            $saturation = [Math]::Min(
                1.0,
                [Math]::Max(0.0, $target.Saturation * $saturationScale)
            )
            $lightness = [Math]::Min(
                1.0,
                [Math]::Max(
                    0.0,
                    $target.Lightness + ($source.Lightness - $templateBase.Lightness)
                )
            )
            $map[$key] = ConvertFrom-Hsl `
                $target.Hue $saturation $lightness $sourceColor.A
        }
    }
    return $map
}

New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null

foreach ($sourceName in $sourceNames) {
    $sourcePath = Join-Path $sourceDirectory $sourceName
    if (!(Test-Path -LiteralPath $sourcePath -PathType Leaf)) {
        throw "Missing Infinite Fusion battle move sheet '$sourcePath'."
    }
    $source = [System.Drawing.Bitmap]::FromFile($sourcePath)
    try {
        if ($source.Width -ne $buttonWidth -or
            $source.Height -ne ($buttonHeight * $typeRowCount)) {
            throw "Unexpected battle move sheet dimensions in '$sourcePath'."
        }
        $commonColors = Get-CommonRowColors $source
        $output = New-Object System.Drawing.Bitmap(
            $source.Width,
            $source.Height,
            [System.Drawing.Imaging.PixelFormat]::Format32bppArgb
        )
        try {
            for ($row = 0; $row -lt $typeRowCount; $row++) {
                if (!$paletteRows.ContainsKey($row)) {
                    for ($y = 0; $y -lt $buttonHeight; $y++) {
                        for ($x = 0; $x -lt $source.Width; $x++) {
                            $output.SetPixel(
                                $x,
                                ($row * $buttonHeight) + $y,
                                $source.GetPixel($x, ($row * $buttonHeight) + $y)
                            )
                        }
                    }
                    continue
                }
                $targetColor = ConvertFrom-HexColor $paletteRows[$row].color
                $shadeMap = New-TypeShadeMap `
                    $source $commonColors $row $targetColor
                for ($y = 0; $y -lt $buttonHeight; $y++) {
                    for ($x = 0; $x -lt $source.Width; $x++) {
                        $templateColor = $source.GetPixel(
                            $x,
                            ($row * $buttonHeight) + $y
                        )
                        $key = Get-ColorKey $templateColor
                        $color = if ($commonColors.Contains($key)) {
                            $templateColor
                        } else {
                            $shadeMap[$key]
                        }
                        $output.SetPixel(
                            $x,
                            ($row * $buttonHeight) + $y,
                            $color
                        )
                    }
                }
            }
            $outputPath = Join-Path $OutputDirectory $sourceName
            $output.Save($outputPath, [System.Drawing.Imaging.ImageFormat]::Png)
        } finally {
            $output.Dispose()
        }
    } finally {
        $source.Dispose()
    }
}

Write-Output "Generated Ironmon battle move type color sheets in '$OutputDirectory'."
