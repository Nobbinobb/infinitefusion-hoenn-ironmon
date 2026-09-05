param(
    [string]$GameRoot = (Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $PSScriptRoot))),
    [string]$OutputPath = (Join-Path (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)) "tracker\src\Ironmon.Tracker.App\Resources\Catalogs\item-presentation.json")
)

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "GameRuntime-Tooling.ps1")
$projectRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$resolvedGameRoot = [IO.Path]::GetFullPath($GameRoot)
$resolvedOutputPath = [IO.Path]::GetFullPath($OutputPath)
Assert-PathWithinDirectory -Path $resolvedOutputPath -Directory $projectRoot
$loader = [IO.File]::ReadAllText((Join-Path $PSScriptRoot "Script-Loader.rb"))
$exporter = [IO.File]::ReadAllText((Join-Path $PSScriptRoot "Export-ItemPresentation.rb"))
$outputHex = [Convert]::ToHexString([Text.Encoding]::UTF8.GetBytes($resolvedOutputPath.Replace('\', '/')))
$sourceHex = [Convert]::ToHexString([Text.Encoding]::UTF8.GetBytes((Join-Path $projectRoot "src").Replace('\', '/')))
$bootstrap = "`$ironmon_item_output = [`"$outputHex`"].pack(`"H*`")`n`$ironmon_item_source = [`"$sourceHex`"].pack(`"H*`")`n$loader`n$exporter"
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $resolvedOutputPath) | Out-Null
Invoke-IronmonGameRuntime -GameRoot $resolvedGameRoot -RubySource $bootstrap -TimeoutSeconds 120 -OperationName "item presentation catalog extraction" -ErrorReportPath "$resolvedOutputPath.error"
if (-not (Test-Path -LiteralPath $resolvedOutputPath)) {
    throw "The runtime did not export the item presentation catalog."
}
Write-Output "Generated item presentation catalog: $resolvedOutputPath"
