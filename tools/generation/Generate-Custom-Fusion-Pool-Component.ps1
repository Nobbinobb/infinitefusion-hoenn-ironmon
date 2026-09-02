param(
    [string]$GameRoot = (Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $PSScriptRoot))),
    [string]$OutputPath = (Join-Path (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)) "data\generation_custom_fusion_pool.bin"),
    [string]$DescriptorPath = (Join-Path (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)) "data\generation_custom_fusion_pool.component.json"),
    [string]$AuditPath = (Join-Path (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)) "data\generation_custom_fusion_pool.audit.json"),
    [ValidateRange(60, 3600)][int]$TimeoutSeconds = 900
)

$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$resolvedGameRoot = [IO.Path]::GetFullPath($GameRoot)
$resolvedOutputPath = [IO.Path]::GetFullPath($OutputPath)
$resolvedDescriptorPath = [IO.Path]::GetFullPath($DescriptorPath)
$resolvedAuditPath = [IO.Path]::GetFullPath($AuditPath)
$sourceRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot "src"))
$manifestPath = Join-Path $sourceRoot "load_order.json"
$loaderPath = Join-Path $PSScriptRoot "Script-Loader.rb"
$exporterPath = Join-Path $PSScriptRoot "Export-CustomFusionPoolComponent.rb"
$errorPath = "$resolvedOutputPath.error"

. (Join-Path $PSScriptRoot "GameRuntime-Tooling.ps1")

foreach ($path in $resolvedOutputPath, $resolvedDescriptorPath, $resolvedAuditPath) {
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $path) | Out-Null
}
Remove-Item -LiteralPath $resolvedOutputPath, $resolvedDescriptorPath, $resolvedAuditPath, $errorPath, "$resolvedOutputPath.tmp" -Force -ErrorAction SilentlyContinue

$loaderSource = [IO.File]::ReadAllText($loaderPath, [Text.Encoding]::UTF8)
$rubyOutput = $resolvedOutputPath.Replace('\', '/')
$rubyDescriptor = $resolvedDescriptorPath.Replace('\', '/')
$rubyAudit = $resolvedAuditPath.Replace('\', '/')
$rubyGameRoot = $resolvedGameRoot.Replace('\', '/')
$rubySourceRoot = $sourceRoot.Replace('\', '/')
$rubyManifest = $manifestPath.Replace('\', '/')
$rubyExporter = ([IO.Path]::GetFullPath($exporterPath)).Replace('\', '/')
$bootstrapSource = @(
    "begin"
    "`$ironmon_custom_fusion_component_output_path = `"$rubyOutput`""
    "`$ironmon_custom_fusion_component_descriptor_path = `"$rubyDescriptor`""
    "`$ironmon_custom_fusion_component_audit_path = `"$rubyAudit`""
    "Dir.chdir(`"$rubyGameRoot`")"
    $loaderSource
    "IronmonScriptLoader.load_directory(`"Data/Scripts`", [/\A(?:997|998|999)/])"
    "GameData.load_all"
    "`$game_temp = Game_Temp.new"
    "Game.load_sprites_list_caches"
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
        -OperationName "custom-fusion pool component export" `
        -ErrorReportPath $errorPath

    foreach ($path in $resolvedOutputPath, $resolvedDescriptorPath, $resolvedAuditPath) {
        if (-not (Test-Path -LiteralPath $path)) {
            throw "The bundled runtime did not produce '$path'."
        }
    }
    $descriptor = Get-Content -LiteralPath $resolvedDescriptorPath -Raw | ConvertFrom-Json
    $audit = Get-Content -LiteralPath $resolvedAuditPath -Raw | ConvertFrom-Json
    $bytes = [IO.File]::ReadAllBytes($resolvedOutputPath)
    if ($bytes.Length -lt 20 -or
        [Text.Encoding]::ASCII.GetString($bytes, 0, 8) -ne "IFCFPOOL") {
        throw "The custom-fusion component header is invalid."
    }
    $schemaVersion = [BitConverter]::ToUInt16($bytes, 8)
    $normalSpeciesCount = [BitConverter]::ToUInt16($bytes, 10)
    $eligibleCount = [BitConverter]::ToUInt32($bytes, 12)
    $bitsetLength = [BitConverter]::ToUInt32($bytes, 16)
    $expectedBitsetLength = [Math]::Ceiling(
        ($normalSpeciesCount * $normalSpeciesCount) / 8.0
    )
    if ($schemaVersion -ne 1 -or
        $bitsetLength -ne $expectedBitsetLength -or
        $bytes.Length -ne 20 + $bitsetLength -or
        $eligibleCount -ne $audit.eligible_count) {
        throw "The custom-fusion component header and audit are inconsistent."
    }
    $actualDigest = (Get-FileHash -LiteralPath $resolvedOutputPath -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($descriptor.name -ne "custom_fusion_pool" -or
        $descriptor.schema_version -ne $schemaVersion -or
        $descriptor.byte_length -ne $bytes.Length -or
        $descriptor.sha256 -ne $actualDigest) {
        throw "The custom-fusion component descriptor does not match the exported bytes."
    }
    if (@($audit.excluded_sprite_authors) -notcontains "japeal") {
        throw "The custom-fusion component audit does not record the autogenerated-sprite exclusion."
    }
    Write-Output "Custom-fusion component exported: $eligibleCount eligible fusions in $($bytes.Length) bytes."
}
finally {
    Remove-Item -LiteralPath $errorPath, "$resolvedOutputPath.tmp" -Force -ErrorAction SilentlyContinue
}
