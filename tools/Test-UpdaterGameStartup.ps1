<#
.SYNOPSIS
Checks a synchronized acceptance game's full compatibility inventory and Ironmon script startup.

.DESCRIPTION
Requires an owned game copy below ignored data. Isolates the save directory before
loading upstream scripts, checks the production compatibility inventory, then
loads the installed Ironmon scripts and game catalogs in the bundled runtime.
The runtime harness temporarily substitutes the loader archive; the bootstrap
restores its original bytes before checking compatibility. This is a script-load
smoke check, not an interactive title-screen, save-load or update rehearsal.

.PARAMETER GameRoot
The owned game copy already synchronized with Build-Distribution.ps1.

.PARAMETER TimeoutSeconds
Maximum runtime duration before the harness stops its own process.
#>
param([Parameter(Mandatory)][string]$GameRoot, [int]$TimeoutSeconds = 120)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'generation/GameRuntime-Tooling.ps1')
$resolvedGameRoot = [IO.Path]::GetFullPath($GameRoot)
Assert-PathWithinDirectory -Path $resolvedGameRoot -Directory (Join-Path $projectRoot 'data')
$manifest = Get-Content -LiteralPath (Join-Path $projectRoot 'src/load_order.json') -Raw | ConvertFrom-Json
foreach ($entry in $manifest) {
    $directory = if ($entry.bootstrap) { 'Data/Scripts' } else { 'Data/Scripts/997_Ironmon' }
    $installed = Join-Path (Join-Path $resolvedGameRoot $directory) $entry.output
    $canonical = Join-Path (Join-Path $projectRoot 'src') $entry.source
    if ((Get-FileHash -LiteralPath $installed).Hash -ne (Get-FileHash -LiteralPath $canonical).Hash) {
        throw "Synchronize canonical scripts before startup acceptance: $($entry.output)."
    }
}

$evidenceRoot = Join-Path $projectRoot ('data/updater/acceptance/startup-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path (Join-Path $evidenceRoot 'userdata') -Force | Out-Null
$errorPath = Join-Path $evidenceRoot 'error.txt'
$resultPath = Join-Path $evidenceRoot 'result.txt'
$rubyEvidence = $evidenceRoot.Replace('\', '/').Replace("'", "\'")
$rubyProject = $projectRoot.Replace('\', '/').Replace("'", "\'")
$archivePath = Join-Path $resolvedGameRoot 'Data/Scripts.rxdata'
$originalHash = (Get-FileHash -LiteralPath $archivePath).Hash
$source = @"
begin
  `$ironmon_startup_evidence = '$rubyEvidence'
  module System
    def self.data_directory
      `$ironmon_startup_evidence + '/userdata'
    end
  end
  File.binwrite('Data/Scripts.rxdata', File.binread('Data/Scripts.rxdata.ironmon-runtime-backup'))
  load '$rubyProject/tools/generation/Script-Loader.rb'
  IronmonScriptLoader.load_directory('Data/Scripts', [/\A(?:997|998|999)/])
  raise 'Save directory escaped the acceptance fixture' unless File.expand_path(SaveData::SAVE_DIR) == File.expand_path(System.data_directory)
  raise 'Full production compatibility inventory rejected the game copy' unless IronmonBootGuard.check(Dir.pwd)
  IronmonScriptLoader.load_directory('Data/Scripts/997_Ironmon')
  GameData.load_all
  raise 'Ironmon gameplay scripts did not initialize' unless defined?(Ironmon::VERSION)
  File.write('$rubyEvidence/result.txt', 'passed')
  exit! 0
rescue Exception => error
  File.write('$rubyEvidence/error.txt', error.full_message)
  exit! 1
end
"@
Invoke-IronmonGameRuntime -GameRoot $resolvedGameRoot -RubySource $source -TimeoutSeconds $TimeoutSeconds -OperationName 'checking isolated Ironmon startup' -ErrorReportPath $errorPath
if ((Get-FileHash -LiteralPath $archivePath).Hash -ne $originalHash) { throw 'The runtime loader was not restored exactly.' }
if (-not (Test-Path -LiteralPath $resultPath) -or (Get-Content -LiteralPath $resultPath -Raw) -ne 'passed') { throw 'The isolated game startup did not complete.' }
Write-Output "Bundled-runtime startup passed: canonical scripts, full game inventory, isolated saves and game catalogs. Evidence: $evidenceRoot"
