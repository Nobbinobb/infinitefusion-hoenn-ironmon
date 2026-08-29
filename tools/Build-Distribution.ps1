$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent $PSScriptRoot
$gameRoot = Split-Path -Parent $projectRoot
. (Join-Path $PSScriptRoot "generation\GameRuntime-Tooling.ps1")
Restore-IronmonGameRuntimeArchive -GameRoot $gameRoot | Out-Null
$sourceRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot "src"))
$sourceManifest = Join-Path $sourceRoot "load_order.json"
$catalog = Join-Path $projectRoot "data\area_catalog.dat"
$obtainabilitySourceCatalog = Join-Path $projectRoot "data\obtainability_source_catalog.json"
$movePowerPresentationCatalog = Join-Path $projectRoot "data\move_power_presentation.json"
$fusionPredecessorIndex = Join-Path $projectRoot "data\fusion_predecessor_index.dat"
$battleMoveColorSource = Join-Path $projectRoot "data\graphics\Battle"
$distribution = Join-Path $projectRoot "dist\Data\Scripts\997_Ironmon"
$distributionData = Join-Path $projectRoot "dist\Data\Ironmon"
$distributionBattleGraphics = Join-Path $distributionData "graphics\Battle"
$distributionRoot = Join-Path $projectRoot "dist"
$distributionReadme = Join-Path $projectRoot "packaging\README.md"
$installationGuide = Join-Path $projectRoot "docs\guides\INSTALLATION.md"
$releaseNotes = Join-Path $projectRoot "docs\releases\RELEASE_NOTES_0.8.1.md"
$installation = Join-Path $gameRoot "Data\Scripts\997_Ironmon"
$installationData = Join-Path $gameRoot "Data\Ironmon"
$installationBattleGraphics = Join-Path $installationData "graphics\Battle"

& (Join-Path $PSScriptRoot "generation\Generate-Move-Power-Presentation.ps1") `
    -OutputPath $movePowerPresentationCatalog
& (Join-Path $PSScriptRoot "generation\Generate-Battle-Move-Type-Colors.ps1") `
    -GameRoot $gameRoot `
    -OutputDirectory $battleMoveColorSource

$manifestDocument = Get-Content -LiteralPath $sourceManifest -Raw |
    ConvertFrom-Json
$manifest = @(
    foreach ($entry in $manifestDocument) {
        $entry
    }
)
if ($manifest.Count -eq 0) {
    throw "The Ironmon Ruby source manifest is empty."
}

$sourcePrefix = $sourceRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) +
    [IO.Path]::DirectorySeparatorChar
$seenSources = @{}
$seenOutputs = @{}
$scripts = foreach ($entry in $manifest) {
    if (!$entry.source -or !$entry.output) {
        throw "Every Ruby manifest entry requires source and output values."
    }
    $relativeSource = $entry.source.ToString().Replace(
        '/', [IO.Path]::DirectorySeparatorChar
    )
    $outputName = $entry.output.ToString()
    if ([IO.Path]::IsPathRooted($relativeSource)) {
        throw "Ruby source paths must be relative: '$relativeSource'."
    }
    $sourcePath = [IO.Path]::GetFullPath(
        (Join-Path $sourceRoot $relativeSource)
    )
    if (!$sourcePath.StartsWith(
        $sourcePrefix, [StringComparison]::OrdinalIgnoreCase
    )) {
        throw "Ruby source path escapes src: '$relativeSource'."
    }
    if (!(Test-Path -LiteralPath $sourcePath -PathType Leaf)) {
        throw "Manifest Ruby source does not exist: '$relativeSource'."
    }
    if ([IO.Path]::GetExtension($sourcePath) -ne ".rb") {
        throw "Manifest source is not Ruby: '$relativeSource'."
    }
    if ($outputName -notmatch '^\d{3}_[A-Za-z0-9_]+\.rb$' -or
        [IO.Path]::GetFileName($outputName) -ne $outputName) {
        throw "Invalid flat runtime Ruby name: '$outputName'."
    }
    if ($seenSources.ContainsKey($sourcePath)) {
        throw "Ruby source is listed more than once: '$relativeSource'."
    }
    if ($seenOutputs.ContainsKey($outputName)) {
        throw "Runtime Ruby name is listed more than once: '$outputName'."
    }
    $seenSources[$sourcePath] = $true
    $seenOutputs[$outputName] = $true
    [pscustomobject]@{
        Source = $sourcePath
        Output = $outputName
        Hash = (Get-FileHash -LiteralPath $sourcePath).Hash
    }
}

$discoveredSources = @(
    Get-ChildItem -LiteralPath $sourceRoot -Recurse -File -Filter "*.rb" |
        ForEach-Object { $_.FullName }
)
$unlistedSources = @($discoveredSources | Where-Object {
    !$seenSources.ContainsKey($_)
})
if ($unlistedSources.Count -gt 0 -or
    $discoveredSources.Count -ne $scripts.Count) {
    $unlisted = $unlistedSources -join "', '"
    throw "Every canonical Ruby source must appear once in the manifest. Unlisted: '$unlisted'."
}

$sortedOutputs = [string[]]($scripts.Output)
[Array]::Sort($sortedOutputs, [StringComparer]::Ordinal)
for ($index = 0; $index -lt $scripts.Count; $index++) {
    if ($scripts[$index].Output -cne $sortedOutputs[$index]) {
        throw "Ruby manifest entries must follow ordinal runtime load order."
    }
}

New-Item -ItemType Directory -Force -Path $distribution | Out-Null
New-Item -ItemType Directory -Force -Path $distributionData | Out-Null
New-Item -ItemType Directory -Force -Path $installation | Out-Null
New-Item -ItemType Directory -Force -Path $installationData | Out-Null
New-Item -ItemType Directory -Force -Path $distributionBattleGraphics | Out-Null
New-Item -ItemType Directory -Force -Path $installationBattleGraphics | Out-Null

Get-ChildItem -LiteralPath $distribution -Filter "*.rb" | Remove-Item -Force
Get-ChildItem -LiteralPath $installation -Filter "*.rb" | Remove-Item -Force
Remove-Item -LiteralPath (Join-Path $distributionData "area_catalog.json") -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath (Join-Path $installationData "area_catalog.json") -Force -ErrorAction SilentlyContinue
foreach ($script in $scripts) {
    Copy-Item -LiteralPath $script.Source -Destination (
        Join-Path $distribution $script.Output
    )
    Copy-Item -LiteralPath $script.Source -Destination (
        Join-Path $installation $script.Output
    )
}
foreach ($runtimeRoot in $distribution, $installation) {
    $nestedRuby = @(Get-ChildItem -LiteralPath $runtimeRoot -Recurse -File `
        -Filter "*.rb" | Where-Object {
            $_.DirectoryName -ne $runtimeRoot
        })
    $runtimeRuby = @(Get-ChildItem -LiteralPath $runtimeRoot -File `
        -Filter "*.rb")
    if ($nestedRuby.Count -gt 0 -or $runtimeRuby.Count -ne $scripts.Count) {
        throw "Generated Ironmon runtime scripts must be complete and flat."
    }
    foreach ($script in $scripts) {
        $runtimePath = Join-Path $runtimeRoot $script.Output
        if ($script.Hash -ne (Get-FileHash -LiteralPath $runtimePath).Hash) {
            throw "Generated runtime Ruby differs from '$($script.Source)'."
        }
    }
}
Copy-Item -LiteralPath $catalog -Destination (Join-Path $distributionData "area_catalog.dat")
Copy-Item -LiteralPath $catalog -Destination (Join-Path $installationData "area_catalog.dat")
Copy-Item -LiteralPath $obtainabilitySourceCatalog -Destination (Join-Path $distributionData "obtainability_source_catalog.json")
Copy-Item -LiteralPath $obtainabilitySourceCatalog -Destination (Join-Path $installationData "obtainability_source_catalog.json")
Copy-Item -LiteralPath $movePowerPresentationCatalog -Destination (Join-Path $distributionData "move_power_presentation.json")
Copy-Item -LiteralPath $movePowerPresentationCatalog -Destination (Join-Path $installationData "move_power_presentation.json")
Copy-Item -LiteralPath $fusionPredecessorIndex -Destination (Join-Path $distributionData "fusion_predecessor_index.dat")
Copy-Item -LiteralPath $fusionPredecessorIndex -Destination (Join-Path $installationData "fusion_predecessor_index.dat")
foreach ($battleMoveSheet in "cursor_fight.png", "cursor_fight_dark.png") {
    $sourceSheet = Join-Path $battleMoveColorSource $battleMoveSheet
    $distributionSheet = Join-Path $distributionBattleGraphics $battleMoveSheet
    $installationSheet = Join-Path $installationBattleGraphics $battleMoveSheet
    Copy-Item -LiteralPath $sourceSheet -Destination $distributionSheet
    Copy-Item -LiteralPath $sourceSheet -Destination $installationSheet
    $sourceHash = (Get-FileHash -LiteralPath $sourceSheet).Hash
    if ($sourceHash -ne (Get-FileHash -LiteralPath $distributionSheet).Hash -or
        $sourceHash -ne (Get-FileHash -LiteralPath $installationSheet).Hash) {
        throw "Generated battle move sheet '$battleMoveSheet' was not copied exactly."
    }
}
Copy-Item -LiteralPath $distributionReadme -Destination (Join-Path $distributionRoot "README.md")
Copy-Item -LiteralPath $installationGuide -Destination (Join-Path $distributionRoot "INSTALLATION.md")
Copy-Item -LiteralPath $releaseNotes -Destination (Join-Path $distributionRoot "RELEASE_NOTES.md")

Write-Output "Ironmon source and data copied to the distribution and local game."
