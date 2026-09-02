param(
    [string]$GameRoot = (Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $PSScriptRoot))),
    [string]$OutputPath = (Join-Path (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)) "data\generation_base_catalog.json"),
    [string]$DescriptorPath = (Join-Path (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)) "data\generation_base_catalog.component.json"),
    [ValidateRange(60, 3600)][int]$TimeoutSeconds = 900
)

$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$resolvedGameRoot = [IO.Path]::GetFullPath($GameRoot)
$resolvedOutputPath = [IO.Path]::GetFullPath($OutputPath)
$resolvedDescriptorPath = [IO.Path]::GetFullPath($DescriptorPath)
$sourceRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot "src"))
$manifestPath = Join-Path $sourceRoot "load_order.json"
$loaderPath = Join-Path $PSScriptRoot "Script-Loader.rb"
$exporterPath = Join-Path $PSScriptRoot "Export-GenerationBaseCatalog.rb"
$errorPath = "$resolvedOutputPath.error"

. (Join-Path $PSScriptRoot "GameRuntime-Tooling.ps1")

New-Item -ItemType Directory -Force -Path (Split-Path -Parent $resolvedOutputPath) | Out-Null
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $resolvedDescriptorPath) | Out-Null
Remove-Item -LiteralPath $resolvedOutputPath, $resolvedDescriptorPath, $errorPath, "$resolvedOutputPath.tmp" -Force -ErrorAction SilentlyContinue

$loaderSource = [IO.File]::ReadAllText($loaderPath, [Text.Encoding]::UTF8)
$rubyOutput = $resolvedOutputPath.Replace('\', '/')
$rubyDescriptor = $resolvedDescriptorPath.Replace('\', '/')
$rubyGameRoot = $resolvedGameRoot.Replace('\', '/')
$rubySourceRoot = $sourceRoot.Replace('\', '/')
$rubyManifest = $manifestPath.Replace('\', '/')
$rubyExporter = ([IO.Path]::GetFullPath($exporterPath)).Replace('\', '/')
$bootstrapSource = @(
    "begin"
    "`$ironmon_generation_base_catalog_output_path = `"$rubyOutput`""
    "`$ironmon_generation_base_catalog_descriptor_path = `"$rubyDescriptor`""
    "Dir.chdir(`"$rubyGameRoot`")"
    $loaderSource
    "IronmonScriptLoader.load_directory(`"Data/Scripts`", [/\A(?:997|998|999)/])"
    "GameData.load_all"
    "IronmonScriptLoader.load_manifest(`"$rubySourceRoot`", `"$rubyManifest`")"
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
        -OperationName "generation base-catalog export" `
        -ErrorReportPath $errorPath

    if (-not (Test-Path -LiteralPath $resolvedOutputPath) -or
        -not (Test-Path -LiteralPath $resolvedDescriptorPath)) {
        throw "The bundled runtime did not produce the generation base catalog and descriptor."
    }
    $document = Get-Content -LiteralPath $resolvedOutputPath -Raw | ConvertFrom-Json
    $descriptor = Get-Content -LiteralPath $resolvedDescriptorPath -Raw | ConvertFrom-Json
    if ($document.schema_version -ne 1 -or $descriptor.schema_version -ne 1) {
        throw "The generation base catalog has an unsupported schema."
    }
    if (-not $document.audit.species_are_normal_only -or
        -not $document.audit.species_ids_are_contiguous -or
        $document.normal_species_count -ne $document.species.Count) {
        throw "The generation base catalog failed its normal-species audit."
    }
    $actualLength = (Get-Item -LiteralPath $resolvedOutputPath).Length
    $actualDigest = (Get-FileHash -LiteralPath $resolvedOutputPath -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($descriptor.name -ne "base_catalog" -or
        $descriptor.byte_length -ne $actualLength -or
        $descriptor.sha256 -ne $actualDigest) {
        throw "The generation base-catalog descriptor does not match the exported bytes."
    }
    Write-Output "Generation base catalog exported: $($document.species.Count) species, $($document.moves.Count) moves, $($document.abilities.Count) abilities, and $($document.items.Count) items."
}
finally {
    Remove-Item -LiteralPath $errorPath, "$resolvedOutputPath.tmp" -Force -ErrorAction SilentlyContinue
}
