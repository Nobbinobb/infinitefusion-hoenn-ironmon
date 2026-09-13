<#
.SYNOPSIS
Validates the updater's early guard and save protection in the bundled game runtime.
.DESCRIPTION
Copies the bundled executable, runtime libraries and scripts into an owned fixture installation.
Only the copied loader is replaced, and System.data_directory is isolated before
base game scripts load. Uses the existing runtime harness. It never reads
player saves and never terminates a pre-existing game process. Build-Distribution
must synchronize the canonical scripts before this check.
#>
param(
    [string]$GameRoot = (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)),
    [int]$TimeoutSeconds = 90
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$installedGameRoot = [IO.Path]::GetFullPath($GameRoot)
. (Join-Path $PSScriptRoot 'generation/GameRuntime-Tooling.ps1')
$parent = Join-Path ([IO.Path]::GetTempPath()) 'ironmon-guard'
$fixture = Join-Path $parent ([Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixture -Force | Out-Null
$gameRoot = Join-Path $fixture 'game'
New-Item -ItemType Directory -Path (Join-Path $gameRoot 'Data') -Force | Out-Null
foreach ($name in 'InfiniteFusion2.exe', 'InfiniteFusion2-performance.exe', 'Game.ini') {
    Copy-Item -LiteralPath (Join-Path $installedGameRoot $name) -Destination (Join-Path $gameRoot $name)
}
Get-ChildItem -LiteralPath $installedGameRoot -Filter '*.dll' -File | Copy-Item -Destination $gameRoot
Copy-Item -LiteralPath (Join-Path $installedGameRoot 'Data/Scripts') -Destination (Join-Path $gameRoot 'Data/Scripts') -Recurse
Get-ChildItem -LiteralPath (Join-Path $installedGameRoot 'Data') -File | Where-Object { $_.Extension -in '.rxdata', '.dat', '.json' } | Copy-Item -Destination (Join-Path $gameRoot 'Data')
[IO.File]::WriteAllText((Join-Path $gameRoot 'mkxp.json'), (@{ gameFolder = '.'; vsync = $true; frameSkip = $false } | ConvertTo-Json), [Text.UTF8Encoding]::new($false))
Copy-Item -LiteralPath (Join-Path $installedGameRoot 'Fonts') -Destination (Join-Path $gameRoot 'Fonts') -Recurse
$result = Join-Path $fixture 'result.txt'
$errorPath = Join-Path $fixture 'error.txt'
$rubyRoot = $projectRoot.Replace('\', '/')
$rubyFixture = $fixture.Replace('\', '/')
$source = @"
begin
  File.write('$rubyFixture/progress.txt', 'entered bootstrap')
  `$ironmon_guard_fixture = '$rubyFixture'
  `$ironmon_loader_calls = []
  def load_scripts_from_folder(path)
    `$ironmon_loader_calls << path
  end
  module System
    def self.data_directory
      `$ironmon_guard_fixture + '/userdata'
    end
  end
  load '$rubyRoot/tools/generation/Script-Loader.rb'
  IronmonScriptLoader.load_directory('Data/Scripts', [/\A(?:997|998|999)/])
  `$ironmon_guard_fixture = '$rubyFixture'
  load '$rubyRoot/tests/runtime/Updater-Boot-Guard.rb'
  File.write('$rubyFixture/result.txt', 'passed')
  exit! 0
rescue Exception => error
  File.write('$rubyFixture/error.txt', error.full_message)
  exit! 1
end
"@
try {
    Invoke-IronmonGameRuntime -GameRoot $gameRoot -RubySource $source -TimeoutSeconds $TimeoutSeconds -OperationName 'checking the updater boot guard' -ErrorReportPath $errorPath
    if (!(Test-Path -LiteralPath $result) -or (Get-Content -LiteralPath $result -Raw) -ne 'passed') { throw 'The bundled runtime did not complete the updater guard checks.' }
    Write-Output 'Bundled-runtime updater guard checks passed: ZIP, Git, external changes, incomplete updates, byte-identical blocked saves and ordinary save reads.'
} catch {
    foreach ($log in 'error.txt', 'progress.txt') {
        $logPath = Join-Path $fixture $log
        if (Test-Path -LiteralPath $logPath) { Get-Content -LiteralPath $logPath | Write-Output }
    }
    throw
} finally {
    Assert-PathWithinDirectory -Path $fixture -Directory $parent
    Remove-Item -LiteralPath $fixture -Recurse -Force
}
