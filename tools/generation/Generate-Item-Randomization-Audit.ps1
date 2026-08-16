param(
    [string]$GameRoot = (Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $PSScriptRoot))),
    [string]$AuditPath = (Join-Path (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)) "docs\audits\generated\ITEM_RANDOMIZATION_GENERATED.csv"),
    [ValidateRange(1, 600)]
    [int]$TimeoutSeconds = 90,
    [switch]$ShowGameWindow
)

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "GameRuntime-Tooling.ps1")

$projectRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$resolvedGameRoot = [IO.Path]::GetFullPath($GameRoot)
$resolvedAuditPath = [IO.Path]::GetFullPath($AuditPath)
$loaderPath = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot "Script-Loader.rb"))
$exporterPath = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot "Export-ItemRandomizationAudit.rb"))
$generatorPath = [IO.Path]::GetFullPath((Join-Path $projectRoot "src\003_Item_Randomization_Generation.rb"))
foreach ($requiredPath in $loaderPath, $exporterPath, $generatorPath) {
    if (-not (Test-Path -LiteralPath $requiredPath)) {
        throw "The item audit dependency was not found at '$requiredPath'."
    }
}

$rubyOutput = $resolvedAuditPath.Replace('\', '/')
$rubyGameRoot = $resolvedGameRoot.Replace('\', '/')
$rubyGenerator = $generatorPath.Replace('\', '/')
$loaderSource = [IO.File]::ReadAllText($loaderPath, [Text.Encoding]::UTF8)
$exporterSource = [IO.File]::ReadAllText($exporterPath, [Text.Encoding]::UTF8)
$bootstrapSource = "`$ironmon_item_audit_output_path = `"$rubyOutput`"`n`$ironmon_item_audit_game_root = `"$rubyGameRoot`"`n`$ironmon_item_generator_source_path = `"$rubyGenerator`"`n$loaderSource`n$exporterSource"
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $resolvedAuditPath) | Out-Null
Remove-Item -LiteralPath "$resolvedAuditPath.progress", "$resolvedAuditPath.error", "$resolvedAuditPath.summary" -Force -ErrorAction SilentlyContinue

Invoke-IronmonGameRuntime -GameRoot $resolvedGameRoot `
    -RubySource $bootstrapSource `
    -TimeoutSeconds $TimeoutSeconds `
    -OperationName "item randomization audit" `
    -ErrorReportPath "$resolvedAuditPath.error" `
    -ShowGameWindow:$ShowGameWindow

if (-not (Test-Path -LiteralPath $resolvedAuditPath) -or
    (Get-Item -LiteralPath $resolvedAuditPath).Length -eq 0) {
    throw "The game runtime did not produce the item randomization audit."
}
if (-not (Test-Path -LiteralPath "$resolvedAuditPath.summary")) {
    throw "The game runtime did not produce an item randomization audit summary."
}
$summary = @{}
Get-Content -LiteralPath "$resolvedAuditPath.summary" | ForEach-Object {
    $parts = $_ -split '=', 2
    if ($parts.Count -eq 2) {
        $summary[$parts[0]] = [int]$parts[1]
    }
}
foreach ($key in "ground_pool", "ground_weight", "tm_pool", "ground_slots", "tm_gifts", "marts") {
    if (-not $summary.ContainsKey($key)) {
        throw "The item randomization audit summary is missing '$key'."
    }
}
if ($summary.ground_pool -lt 1 -or $summary.ground_weight -lt 1 -or
    $summary.tm_pool -lt 1) {
    throw "The item randomization audit contains an empty result pool."
}
Remove-Item -LiteralPath "$resolvedAuditPath.progress", "$resolvedAuditPath.error", "$resolvedAuditPath.summary" -Force -ErrorAction SilentlyContinue
Write-Output "Item audit generated: $($summary.ground_pool) ground results with $($summary.ground_weight) total weight, $($summary.tm_pool) TM results, $($summary.ground_slots) ground slots, $($summary.tm_gifts) TM gifts, $($summary.marts) inline marts."
