param(
    [string]$GameRoot = (Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $PSScriptRoot))),
    [string]$OutputPath = (Join-Path (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)) "data\area_catalog.dat"),
    [string]$AuditPath = (Join-Path (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)) "docs\audits\generated\AREA_CATALOG_GENERATED.csv"),
    [ValidateRange(1, 600)]
    [int]$TimeoutSeconds = 90,
    [switch]$ShowGameWindow,
    [switch]$ValidateInstalledScripts,
    [switch]$RunAreaProgressTests
)

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "GameRuntime-Tooling.ps1")

$projectRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$resolvedGameRoot = [IO.Path]::GetFullPath($GameRoot)
if ($RunAreaProgressTests -and -not $ValidateInstalledScripts) {
    throw "RunAreaProgressTests requires ValidateInstalledScripts."
}

$loaderSourcePath = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot "Script-Loader.rb"))
$exporterSource = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot "Export-AreaCatalog.rb"))
$resolvedOutputPath = [IO.Path]::GetFullPath($OutputPath)
$resolvedAuditPath = [IO.Path]::GetFullPath($AuditPath)
Assert-PathWithinDirectory -Path $loaderSourcePath -Directory $resolvedGameRoot
Assert-PathWithinDirectory -Path $exporterSource -Directory $resolvedGameRoot
if (-not (Test-Path -LiteralPath $loaderSourcePath)) {
    throw "The shared game-script loader was not found at '$loaderSourcePath'."
}
if (-not (Test-Path -LiteralPath $exporterSource)) {
    throw "The area catalog exporter was not found at '$exporterSource'."
}

$bootstrapMarker = "$($resolvedOutputPath.Replace('\', '/')).bootstrap"
$rubyOutputPath = $resolvedOutputPath.Replace('\', '/')
$rubyAuditPath = $resolvedAuditPath.Replace('\', '/')
$rubyGameRoot = $resolvedGameRoot.Replace('\', '/')
$rubyValidateInstalledScripts = if ($ValidateInstalledScripts) { "true" } else { "false" }
$runtimeTestPath = Join-Path $projectRoot "tests\runtime\Area-Progress.rb"
$runtimeTestSource = if ($RunAreaProgressTests) {
    [IO.File]::ReadAllText($runtimeTestPath, [Text.Encoding]::UTF8)
}
else {
    ""
}
$runtimeTestHex = [BitConverter]::ToString(
    [Text.UTF8Encoding]::new($false).GetBytes($runtimeTestSource)
).Replace("-", "")
$loaderCode = [IO.File]::ReadAllText($loaderSourcePath, [Text.Encoding]::UTF8)
$exporterCode = [IO.File]::ReadAllText($exporterSource, [Text.Encoding]::UTF8)
$bootstrapSource = "File.binwrite(`"$bootstrapMarker`", `"bootstrap loaded\n`")`n`$ironmon_area_catalog_output_path = `"$rubyOutputPath`"`n`$ironmon_area_catalog_audit_path = `"$rubyAuditPath`"`n`$ironmon_area_catalog_game_root = `"$rubyGameRoot`"`n`$ironmon_area_catalog_validate_installed_scripts = $rubyValidateInstalledScripts`n`$ironmon_area_catalog_runtime_test_source = [`"$runtimeTestHex`"].pack(`"H*`")`n$loaderCode`n$exporterCode"
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $resolvedOutputPath) | Out-Null
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $resolvedAuditPath) | Out-Null
Remove-Item -LiteralPath "$resolvedOutputPath.progress" -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath "$resolvedOutputPath.error" -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath "$resolvedOutputPath.bootstrap" -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath "$resolvedOutputPath.summary" -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath "$resolvedOutputPath.tests" -Force -ErrorAction SilentlyContinue

Invoke-IronmonGameRuntime -GameRoot $resolvedGameRoot `
    -RubySource $bootstrapSource `
    -TimeoutSeconds $TimeoutSeconds `
    -OperationName "area catalog extraction" `
    -ErrorReportPath "$resolvedOutputPath.error" `
    -ShowGameWindow:$ShowGameWindow

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
