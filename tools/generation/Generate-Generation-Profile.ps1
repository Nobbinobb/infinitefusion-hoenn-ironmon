param(
    [string]$GameRoot = (Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $PSScriptRoot))),
    [string]$OutputPath = (Join-Path (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)) "data\generation_profile.json"),
    [ValidateRange(60, 3600)][int]$TimeoutSeconds = 900,
    [switch]$SkipComponentGeneration
)

$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$dataRoot = Join-Path $projectRoot "data"
$resolvedGameRoot = [IO.Path]::GetFullPath($GameRoot)
$resolvedOutputPath = [IO.Path]::GetFullPath($OutputPath)
$baseCatalogPath = Join-Path $dataRoot "generation_base_catalog.json"
$baseDescriptorPath = Join-Path $dataRoot "generation_base_catalog.component.json"
$poolPath = Join-Path $dataRoot "generation_custom_fusion_pool.bin"
$poolDescriptorPath = Join-Path $dataRoot "generation_custom_fusion_pool.component.json"
$spritePath = Join-Path $dataRoot "generation_custom_sprites.json"
$spriteDescriptorPath = Join-Path $dataRoot "generation_custom_sprites.component.json"
$areaCatalogPath = Join-Path $dataRoot "area_catalog.dat"
$areaDescriptorPath = Join-Path $dataRoot "generation_area_catalog.component.json"
$obtainabilityPath = Join-Path $dataRoot "obtainability_source_catalog.json"
$obtainabilityDescriptorPath = Join-Path $dataRoot "generation_obtainability_sources.component.json"
$profileContractPath = Join-Path $projectRoot "src\compatibility\Generation_Profile.rb"
$exporterPath = Join-Path $PSScriptRoot "Export-GenerationProfile.rb"
$loaderPath = Join-Path $PSScriptRoot "Script-Loader.rb"
$errorPath = "$resolvedOutputPath.error"

. (Join-Path $PSScriptRoot "GameRuntime-Tooling.ps1")

function Write-ComponentDescriptor {
    param(
        [string]$Name,
        [int]$SchemaVersion,
        [string]$SourcePath,
        [string]$DescriptorPath
    )

    if (-not (Test-Path -LiteralPath $SourcePath) -or
        (Get-Item -LiteralPath $SourcePath).Length -eq 0) {
        throw "Generation profile source component '$SourcePath' is unavailable."
    }
    $descriptor = [ordered]@{
        name = $Name
        schema_version = $SchemaVersion
        sha256 = (Get-FileHash -LiteralPath $SourcePath -Algorithm SHA256).Hash.ToLowerInvariant()
        byte_length = (Get-Item -LiteralPath $SourcePath).Length
    }
    $json = $descriptor | ConvertTo-Json -Compress
    [IO.File]::WriteAllText($DescriptorPath, "$json`n", [Text.UTF8Encoding]::new($false))
}

function Assert-ComponentDescriptor {
    param(
        [string]$Name,
        [int]$SchemaVersion,
        [string]$SourcePath,
        [string]$DescriptorPath
    )

    if (-not (Test-Path -LiteralPath $SourcePath) -or
        -not (Test-Path -LiteralPath $DescriptorPath)) {
        throw "Generation profile component '$Name' is unavailable."
    }
    $descriptor = Get-Content -LiteralPath $DescriptorPath -Raw | ConvertFrom-Json
    $sourceLength = (Get-Item -LiteralPath $SourcePath).Length
    $sourceDigest = (Get-FileHash -LiteralPath $SourcePath -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($descriptor.name -ne $Name -or
        $descriptor.schema_version -ne $SchemaVersion -or
        $descriptor.byte_length -ne $sourceLength -or
        $descriptor.sha256 -ne $sourceDigest) {
        throw "Generation profile component descriptor '$Name' is stale or invalid."
    }
}

if (-not $SkipComponentGeneration) {
    & (Join-Path $PSScriptRoot "Generate-Generation-Base-Catalog.ps1") `
        -GameRoot $resolvedGameRoot `
        -OutputPath $baseCatalogPath `
        -DescriptorPath $baseDescriptorPath `
        -TimeoutSeconds $TimeoutSeconds
    & (Join-Path $PSScriptRoot "Generate-Custom-Fusion-Pool-Component.ps1") `
        -GameRoot $resolvedGameRoot `
        -OutputPath $poolPath `
        -DescriptorPath $poolDescriptorPath `
        -TimeoutSeconds $TimeoutSeconds
}

$obtainabilityDocument = Get-Content -LiteralPath $obtainabilityPath -Raw | ConvertFrom-Json
if ($obtainabilityDocument.schema_version -ne 1) {
    throw "The obtainability source catalog has an unsupported schema."
}
Write-ComponentDescriptor -Name "custom_sprites" -SchemaVersion 1 -SourcePath $spritePath -DescriptorPath $spriteDescriptorPath
Write-ComponentDescriptor -Name "area_catalog" -SchemaVersion 1 -SourcePath $areaCatalogPath -DescriptorPath $areaDescriptorPath
Write-ComponentDescriptor -Name "obtainability_sources" -SchemaVersion 1 -SourcePath $obtainabilityPath -DescriptorPath $obtainabilityDescriptorPath
Assert-ComponentDescriptor -Name "base_catalog" -SchemaVersion 1 -SourcePath $baseCatalogPath -DescriptorPath $baseDescriptorPath
Assert-ComponentDescriptor -Name "custom_fusion_pool" -SchemaVersion 1 -SourcePath $poolPath -DescriptorPath $poolDescriptorPath
Assert-ComponentDescriptor -Name "area_catalog" -SchemaVersion 1 -SourcePath $areaCatalogPath -DescriptorPath $areaDescriptorPath
Assert-ComponentDescriptor -Name "obtainability_sources" -SchemaVersion 1 -SourcePath $obtainabilityPath -DescriptorPath $obtainabilityDescriptorPath

New-Item -ItemType Directory -Force -Path (Split-Path -Parent $resolvedOutputPath) | Out-Null
Remove-Item -LiteralPath $resolvedOutputPath, $errorPath, "$resolvedOutputPath.tmp" -Force -ErrorAction SilentlyContinue
$loaderSource = [IO.File]::ReadAllText($loaderPath, [Text.Encoding]::UTF8)
$profileContractSource = [IO.File]::ReadAllText($profileContractPath, [Text.Encoding]::UTF8)
$rubyOutput = $resolvedOutputPath.Replace('\', '/')
$rubyGameRoot = $resolvedGameRoot.Replace('\', '/')
$rubyDescriptors = @(
    $areaDescriptorPath,
    $baseDescriptorPath,
    $poolDescriptorPath,
    $spriteDescriptorPath,
    $obtainabilityDescriptorPath
) | ForEach-Object { "`"$($_.Replace('\', '/'))`"" }
$rubyExporter = ([IO.Path]::GetFullPath($exporterPath)).Replace('\', '/')
$bootstrapSource = @(
    "begin"
    "`$ironmon_generation_profile_output_path = `"$rubyOutput`""
    "`$ironmon_generation_profile_descriptor_paths = [$($rubyDescriptors -join ',')]"
    "Dir.chdir(`"$rubyGameRoot`")"
    $loaderSource
    "IronmonScriptLoader.load_directory(`"Data/Scripts`", [/\A(?:997|998|999)/])"
    $profileContractSource
    "eval(File.binread(`"$rubyExporter`"), TOPLEVEL_BINDING, `"$rubyExporter`")"
    "exit! 0"
    "rescue Exception => error"
    "backtrace = error.backtrace ? error.backtrace.join(`"\n`") : `"`""
    "File.binwrite(`"$($errorPath.Replace('\', '/'))`", `"#{error.class}: #{error.message}\n#{backtrace}`")"
    "exit! 1"
    "end"
) -join [Environment]::NewLine

try {
    Invoke-IronmonGameRuntime `
        -GameRoot $resolvedGameRoot `
        -RubySource $bootstrapSource `
        -TimeoutSeconds $TimeoutSeconds `
        -OperationName "generation profile export" `
        -ErrorReportPath $errorPath

    if (-not (Test-Path -LiteralPath $resolvedOutputPath)) {
        throw "The bundled runtime did not produce the generation profile."
    }
    $profile = Get-Content -LiteralPath $resolvedOutputPath -Raw | ConvertFrom-Json
    if ($profile.profile_id -notmatch '^[0-9a-f]{64}$' -or
        $profile.manifest.schema_version -ne 1 -or
        $profile.manifest.algorithms.Count -ne 13 -or
        $profile.manifest.components.Count -ne 5) {
        throw "The generated profile does not satisfy the version 1 contract."
    }
    Write-Output "Generation profile exported: $($profile.profile_id)"
}
finally {
    Remove-Item -LiteralPath $errorPath, "$resolvedOutputPath.tmp" -Force -ErrorAction SilentlyContinue
}
