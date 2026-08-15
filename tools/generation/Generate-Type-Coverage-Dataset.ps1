param(
    [string]$GameRoot = (Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $PSScriptRoot))),
    [string]$OutputPath = (Join-Path (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)) "data\type_coverage.json"),
    [string]$AuditPath = (Join-Path (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)) "docs\audits\generated\TYPE_COVERAGE_GENERATED.csv"),
    [ValidateRange(1, 600)]
    [int]$TimeoutSeconds = 90,
    [switch]$ShowGameWindow
)

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "GameRuntime-Tooling.ps1")

$projectRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$resolvedGameRoot = [IO.Path]::GetFullPath($GameRoot)
$resolvedProjectRoot = [IO.Path]::GetFullPath($projectRoot)
$resolvedOutputPath = [IO.Path]::GetFullPath($OutputPath)
$resolvedAuditPath = [IO.Path]::GetFullPath($AuditPath)
$resolvedSourcePath = [IO.Path]::GetFullPath((Join-Path $projectRoot "src"))
$loaderSourcePath = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot "Script-Loader.rb"))
$exporterSource = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot "Export-TypeCoverageDataset.rb"))
Assert-PathWithinDirectory -Path $resolvedOutputPath -Directory $resolvedProjectRoot
Assert-PathWithinDirectory -Path $resolvedAuditPath -Directory $resolvedProjectRoot
Assert-PathWithinDirectory -Path $resolvedSourcePath -Directory $resolvedGameRoot
Assert-PathWithinDirectory -Path $loaderSourcePath -Directory $resolvedGameRoot
Assert-PathWithinDirectory -Path $exporterSource -Directory $resolvedGameRoot
if (-not (Test-Path -LiteralPath $loaderSourcePath)) {
    throw "The shared game-script loader was not found at '$loaderSourcePath'."
}
if (-not (Test-Path -LiteralPath $exporterSource)) {
    throw "The type coverage exporter was not found at '$exporterSource'."
}

$rubyOutputPath = $resolvedOutputPath.Replace('\', '/')
$rubyAuditPath = $resolvedAuditPath.Replace('\', '/')
$rubyGameRoot = $resolvedGameRoot.Replace('\', '/')
$rubySourcePath = $resolvedSourcePath.Replace('\', '/')
$bootstrapMarker = "$rubyOutputPath.bootstrap"
$loaderCode = [IO.File]::ReadAllText($loaderSourcePath, [Text.Encoding]::UTF8)
$exporterCode = [IO.File]::ReadAllText($exporterSource, [Text.Encoding]::UTF8)
$bootstrapSource = "File.binwrite(`"$bootstrapMarker`", `"bootstrap loaded\n`")`n`$ironmon_type_coverage_output_path = `"$rubyOutputPath`"`n`$ironmon_type_coverage_audit_path = `"$rubyAuditPath`"`n`$ironmon_type_coverage_game_root = `"$rubyGameRoot`"`n`$ironmon_type_coverage_source_path = `"$rubySourcePath`"`n$loaderCode`n$exporterCode"

New-Item -ItemType Directory -Force -Path (Split-Path -Parent $resolvedOutputPath) | Out-Null
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $resolvedAuditPath) | Out-Null
foreach ($suffix in ".progress", ".error", ".bootstrap", ".summary", ".tmp") {
    Remove-Item -LiteralPath "$resolvedOutputPath$suffix" -Force -ErrorAction SilentlyContinue
}

Invoke-IronmonGameRuntime -GameRoot $resolvedGameRoot `
    -RubySource $bootstrapSource `
    -TimeoutSeconds $TimeoutSeconds `
    -OperationName "type coverage dataset extraction" `
    -ErrorReportPath "$resolvedOutputPath.error" `
    -ShowGameWindow:$ShowGameWindow

if (-not (Test-Path -LiteralPath $resolvedOutputPath) -or
    (Get-Item -LiteralPath $resolvedOutputPath).Length -eq 0) {
    throw "The game runtime did not produce a type coverage dataset."
}
if (-not (Test-Path -LiteralPath $resolvedAuditPath) -or
    (Get-Item -LiteralPath $resolvedAuditPath).Length -eq 0) {
    throw "The game runtime did not produce a type coverage audit."
}
if (-not (Test-Path -LiteralPath "$resolvedOutputPath.summary")) {
    throw "The game runtime did not produce a type coverage summary."
}

$summary = @{}
Get-Content -LiteralPath "$resolvedOutputPath.summary" | ForEach-Object {
    $parts = $_ -split '=', 2
    if ($parts.Count -eq 2) {
        $summary[$parts[0]] = [int]$parts[1]
    }
}
foreach ($key in "profiles", "normal_pool_size", "fusion_pool_size") {
    if (-not $summary.ContainsKey($key)) {
        throw "The game runtime type coverage summary is missing '$key'."
    }
}
$document = Get-Content -LiteralPath $resolvedOutputPath -Raw | ConvertFrom-Json
if ($document.schema_version -ne 1 -or
    $document.profiles.Count -ne $summary.profiles -or
    $document.normal_pool_size -ne $summary.normal_pool_size -or
    $document.fusion_pool_size -ne $summary.fusion_pool_size) {
    throw "The generated type coverage document does not match its runtime summary."
}

foreach ($suffix in ".progress", ".error", ".bootstrap", ".summary", ".tmp") {
    Remove-Item -LiteralPath "$resolvedOutputPath$suffix" -Force -ErrorAction SilentlyContinue
}
Write-Output "Type coverage dataset generated from '$resolvedGameRoot': $($summary.profiles) profiles, $($summary.normal_pool_size) normal Pokemon, $($summary.fusion_pool_size) custom fusions."
