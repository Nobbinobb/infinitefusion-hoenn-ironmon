<#
.SYNOPSIS
Exercises real seeded encounter lookup work in the bundled game without loading or saving through gameplay hooks.
#>
param(
    [string]$GameRoot = (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)),
    [string]$SavePath,
    [int]$Seed = 116872428,
    [switch]$Native,
    [ValidateSet('diagnostic', 'automatic', 'manual')]
    [string]$PreparationMode = 'diagnostic',
    [ValidateRange(1, 600)]
    [int]$TimeoutSeconds = 180
)

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "generation\GameRuntime-Tooling.ps1")
$projectRoot = Split-Path -Parent $PSScriptRoot
$reportPath = Join-Path $projectRoot "data\area-encounter-lookup-probe.json"
$saveHash = $null
if ($SavePath) {
    $SavePath = (Resolve-Path -LiteralPath $SavePath).Path
    $saveHash = (Get-FileHash -LiteralPath $SavePath -Algorithm SHA256).Hash
}
$options = @{
    seed = $Seed
    save_path = $SavePath
    report_path = $reportPath.Replace('\', '/')
    wild_test_path = (Join-Path $projectRoot 'tests\runtime\Wild-Encounter-Fusions.rb').Replace('\', '/')
    preparation_mode = $PreparationMode
}
$nativeProcess = $null
try {
if ($Native) {
    if (-not $SavePath) { throw 'Native preparation validation requires a real seeded save.' }
    $nativeRoot = Join-Path $projectRoot ('data\area-native-' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $nativeRoot | Out-Null
    $nativeProject = Join-Path $projectRoot 'tracker\tools\Ironmon.Tracker.EncounterProbe\Ironmon.Tracker.EncounterProbe.csproj'
    $nativeAssets = Join-Path (Split-Path -Parent $nativeProject) 'obj\project.assets.json'
    if (-not (Test-Path -LiteralPath $nativeAssets)) {
        & dotnet restore $nativeProject --disable-build-servers --disable-parallel -m:1 /nodeReuse:false
        if ($LASTEXITCODE -ne 0) { throw 'Native probe dependency restore failed.' }
    }
    & dotnet build $nativeProject --no-restore --disable-build-servers -m:1 /nodeReuse:false
    if ($LASTEXITCODE -ne 0) { throw 'Native probe build failed.' }
    $nativeAssembly = Join-Path (Split-Path -Parent $nativeProject) 'bin\Debug\net10.0\Ironmon.Tracker.EncounterProbe.dll'
    $nativeProcess = Start-Process dotnet -ArgumentList @(('"' + $nativeAssembly + '"'), ('"' + $nativeRoot + '"'), $PreparationMode) -WindowStyle Hidden -PassThru
    $nativeDeadline = [DateTime]::UtcNow.AddSeconds(15)
    while (-not (Test-Path -LiteralPath (Join-Path $nativeRoot 'port.txt'))) {
        if ($nativeProcess.HasExited -or [DateTime]::UtcNow -gt $nativeDeadline) { throw 'Native probe listener did not start.' }
        Start-Sleep -Milliseconds 50
    }
    $options.native_root = $nativeRoot.Replace('\', '/')
}
$optionsHex = [BitConverter]::ToString([Text.Encoding]::UTF8.GetBytes(($options | ConvertTo-Json -Compress))).Replace('-', '')
$loader = Get-Content (Join-Path $PSScriptRoot 'generation\Script-Loader.rb') -Raw
$testSource = Get-Content (Join-Path $projectRoot 'tests\runtime\Area-Encounter-Lookup.rb') -Raw
$bootstrap = @(
    'begin'
    $loader
    'IronmonScriptLoader.load_directory("Data/Scripts", [/\A(?:998|999)/])'
    'GameData.load_all'
    ('$ironmon_area_lookup_probe_options = Ironmon::TrackerConnection.new.send(:normalize_json, JSON.parse(["' + $optionsHex + '"].pack("H*")))')
    $testSource
    'exit! 0'
    'rescue Exception => error'
    ('File.binwrite("' + $reportPath.Replace('\', '/') + '.error", "#{error.class}: #{error.message}\n#{error.backtrace.join("\n")}")')
    'exit! 1'
    'end'
) -join "`n"
& (Join-Path $PSScriptRoot 'Build-Distribution.ps1')
Invoke-IronmonGameRuntime -GameRoot $GameRoot -RubySource $bootstrap -TimeoutSeconds $TimeoutSeconds -OperationName 'area encounter lookup probe' -ErrorReportPath "$reportPath.error"
}
finally {
    if ($nativeProcess -and -not $nativeProcess.HasExited) { Stop-Process -Id $nativeProcess.Id }
    if ($saveHash -and (Get-FileHash -LiteralPath $SavePath -Algorithm SHA256).Hash -ne $saveHash) {
        throw "The encounter lookup test changed the supplied save file."
    }
}
$report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
$report | Select-Object passed, seed, pool_size, preparation_milliseconds, maximum_slice_milliseconds, global_preparation_milliseconds, global_maximum_slice_milliseconds, global_maximum_slice_stage, mapping_errors | ConvertTo-Json -Compress
$report.requests | Where-Object phase -ne 'poll' | ConvertTo-Json -Compress
Write-Output "Full request timings: $reportPath"
if ($Native) {
    $nativeReportPath = Join-Path $nativeRoot 'native-report.json'
    $nativeReport = Get-Content -LiteralPath $nativeReportPath -Raw | ConvertFrom-Json
    $nativeReport | Select-Object Passed, Error, Timings | ConvertTo-Json -Depth 6
    if (-not $nativeReport.Passed) { throw "Native probe failed; see $nativeReportPath" }
}
