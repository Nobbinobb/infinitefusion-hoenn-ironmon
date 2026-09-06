$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$gameRoot = Split-Path -Parent $projectRoot
. (Join-Path $projectRoot 'tools/generation/GameRuntime-Tooling.ps1')
$marker = Join-Path $projectRoot 'data/hosted-runtime.txt'
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $marker) | Out-Null
$rubyMarker = $marker.Replace('\', '/') | ConvertTo-Json -Compress
$rubySourceRoot = (Join-Path $projectRoot 'src').Replace('\', '/') | ConvertTo-Json -Compress
$rubyManifest = (Join-Path $projectRoot 'src/load_order.json').Replace('\', '/') | ConvertTo-Json -Compress
$loader = [IO.File]::ReadAllText((Join-Path $projectRoot 'tools/generation/Script-Loader.rb'))
$source = @"
begin
$loader
IronmonScriptLoader.load_directory("Data/Scripts", [/\A(?:997|998|999)/])
GameData.load_all
IronmonScriptLoader.load_manifest($rubySourceRoot, $rubyManifest, false)
if File.exist?("Data/Ironmon/generation_profile.json")
  raise "Hosted bootstrap test requires a clean game without generated Ironmon data"
end
begin
  Ironmon.tracker_area_catalog_document
  raise "Runtime catalog validation unexpectedly accepted missing generated data"
rescue Ironmon::AreaCatalogError
end
File.binwrite($rubyMarker, "Hosted runtime loaded current source without generated data; missing catalog validation remains enforced.")
exit!(0)
rescue Exception => error
File.binwrite($rubyMarker, "#{error.class}: #{error.message}\n#{error.backtrace.join("\n")}")
exit!(1)
end
"@
Invoke-IronmonGameRuntime -GameRoot $gameRoot -RubySource $source -TimeoutSeconds 60 -OperationName 'hosted startup smoke test' -ErrorReportPath $marker
if (-not (Test-Path -LiteralPath $marker)) { throw 'Runtime did not write the startup marker.' }
Get-Content -LiteralPath $marker
