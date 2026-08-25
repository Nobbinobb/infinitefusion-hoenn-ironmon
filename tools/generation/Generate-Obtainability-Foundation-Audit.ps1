param(
    [string]$GameRoot = (Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $PSScriptRoot))),
    [string]$AuditPath = (Join-Path (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)) "docs\audits\generated\OBTAINABILITY_FOUNDATION_GENERATED.csv"),
    [string]$AreaCatalogPath = (Join-Path (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)) "data\area_catalog.dat"),
    [string]$SourceCatalogPath = (Join-Path (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)) "data\obtainability_source_catalog.json"),
    [ValidateRange(1, 600)]
    [int]$TimeoutSeconds = 120,
    [switch]$ShowGameWindow
)

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "GameRuntime-Tooling.ps1")

$projectRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$resolvedProjectRoot = [IO.Path]::GetFullPath($projectRoot)
$resolvedGameRoot = [IO.Path]::GetFullPath($GameRoot)
$resolvedAuditPath = [IO.Path]::GetFullPath($AuditPath)
$resolvedAreaCatalogPath = [IO.Path]::GetFullPath($AreaCatalogPath)
$resolvedSourceCatalogPath = [IO.Path]::GetFullPath($SourceCatalogPath)
$sourceRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot "src"))
$manifestPath = [IO.Path]::GetFullPath((Join-Path $sourceRoot "load_order.json"))
$loaderPath = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot "Script-Loader.rb"))
$exporterPath = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot "Export-ObtainabilityFoundationAudit.rb"))

Assert-PathWithinDirectory -Path $resolvedAuditPath -Directory $resolvedProjectRoot
Assert-PathWithinDirectory -Path $resolvedSourceCatalogPath -Directory $resolvedProjectRoot
foreach ($requiredPath in $manifestPath, $loaderPath, $exporterPath, $resolvedAreaCatalogPath) {
    if (-not (Test-Path -LiteralPath $requiredPath)) {
        throw "The obtainability foundation audit dependency was not found at '$requiredPath'."
    }
}

$rubyOutput = $resolvedAuditPath.Replace('\', '/')
$rubyGameRoot = $resolvedGameRoot.Replace('\', '/')
$rubySourceRoot = $sourceRoot.Replace('\', '/')
$rubyManifest = $manifestPath.Replace('\', '/')
$rubyAreaCatalog = $resolvedAreaCatalogPath.Replace('\', '/')
$rubySourceCatalog = $resolvedSourceCatalogPath.Replace('\', '/')
$loaderSource = [IO.File]::ReadAllText($loaderPath, [Text.Encoding]::UTF8)
$exporterSource = [IO.File]::ReadAllText($exporterPath, [Text.Encoding]::UTF8)
$bootstrapSource = @(
    "`$ironmon_obtainability_audit_output_path = `"$rubyOutput`""
    "`$ironmon_obtainability_audit_game_root = `"$rubyGameRoot`""
    "`$ironmon_obtainability_audit_source_root = `"$rubySourceRoot`""
    "`$ironmon_obtainability_audit_manifest_path = `"$rubyManifest`""
    "`$ironmon_obtainability_audit_area_catalog_path = `"$rubyAreaCatalog`""
    "`$ironmon_obtainability_source_catalog_output_path = `"$rubySourceCatalog`""
    $loaderSource
    $exporterSource
) -join "`n"

New-Item -ItemType Directory -Force -Path (Split-Path -Parent $resolvedAuditPath) | Out-Null
foreach ($suffix in ".progress", ".error", ".summary", ".tmp") {
    Remove-Item -LiteralPath "$resolvedAuditPath$suffix" -Force -ErrorAction SilentlyContinue
}
Remove-Item -LiteralPath "$resolvedSourceCatalogPath.tmp" -Force -ErrorAction SilentlyContinue

Invoke-IronmonGameRuntime -GameRoot $resolvedGameRoot `
    -RubySource $bootstrapSource `
    -TimeoutSeconds $TimeoutSeconds `
    -OperationName "obtainability foundation audit" `
    -ErrorReportPath "$resolvedAuditPath.error" `
    -ShowGameWindow:$ShowGameWindow

if (-not (Test-Path -LiteralPath $resolvedAuditPath) -or
    (Get-Item -LiteralPath $resolvedAuditPath).Length -eq 0) {
    throw "The game runtime did not produce the obtainability foundation audit."
}
if (-not (Test-Path -LiteralPath "$resolvedAuditPath.summary")) {
    throw "The game runtime did not produce an obtainability foundation audit summary."
}
if (-not (Test-Path -LiteralPath $resolvedSourceCatalogPath) -or
    (Get-Item -LiteralPath $resolvedSourceCatalogPath).Length -eq 0) {
    throw "The game runtime did not produce the semantic obtainability source catalog."
}

$summary = @{}
Get-Content -LiteralPath "$resolvedAuditPath.summary" | ForEach-Object {
    $parts = $_ -split '=', 2
    if ($parts.Count -eq 2) {
        $summary[$parts[0]] = [int]$parts[1]
    }
}
$requiredSummary = @(
    "schema_version",
    "authored_required_item_gift_calls",
    "runtime_hooks",
    "event_acquisition_calls",
    "event_resource_calls",
    "source_acquisition_calls",
    "source_resource_calls",
    "starter_slots",
    "encounter_slots",
    "randomizable_item_slots",
    "evolution_item_methods",
    "required_evolution_items",
    "evolution_item_branches"
    "semantic_source_entries"
    "semantic_resource_entries"
)
foreach ($key in $requiredSummary) {
    if (-not $summary.ContainsKey($key)) {
        throw "The obtainability foundation audit summary is missing '$key'."
    }
}
foreach ($key in "authored_required_item_gift_calls", "runtime_hooks", "event_acquisition_calls", "event_resource_calls", "source_acquisition_calls", "source_resource_calls", "starter_slots", "encounter_slots", "randomizable_item_slots", "evolution_item_methods", "required_evolution_items", "evolution_item_branches", "semantic_source_entries", "semantic_resource_entries") {
    if ($summary[$key] -lt 1) {
        throw "The obtainability foundation audit contains no '$key' records."
    }
}

foreach ($suffix in ".progress", ".error", ".summary", ".tmp") {
    Remove-Item -LiteralPath "$resolvedAuditPath$suffix" -Force -ErrorAction SilentlyContinue
}
Write-Output "Obtainability foundation audit generated: $($summary.encounter_slots) encounter slots, $($summary.randomizable_item_slots) randomizable item slots, $($summary.required_evolution_items) required evolution items, $($summary.semantic_source_entries) semantic sources, and $($summary.semantic_resource_entries) semantic resources."
