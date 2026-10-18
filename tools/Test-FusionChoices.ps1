param(
    [string]$GameRoot = (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)),
    [string]$OutputDirectory = (Join-Path (Split-Path -Parent $PSScriptRoot) 'data/fusion-choice-validation'),
    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$resolvedGameRoot = [IO.Path]::GetFullPath($GameRoot)
$resolvedOutput = [IO.Path]::GetFullPath($OutputDirectory)
. (Join-Path $PSScriptRoot 'generation/GameRuntime-Tooling.ps1')
if (-not $SkipBuild) {
    & (Join-Path $PSScriptRoot 'Build-Distribution.ps1') -GameRoot $resolvedGameRoot -ReuseGeneratedAssets
}
New-Item -ItemType Directory -Path $resolvedOutput -Force | Out-Null
$loader = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'generation/Script-Loader.rb'))
$fixture = (Join-Path $projectRoot 'tests/fixtures/fusion-stats-v2.json').Replace('\', '/')
$test = (Join-Path $projectRoot 'tests/runtime/Fusion-Choices.rb').Replace('\', '/')
$errorPath = Join-Path $resolvedOutput 'error.txt'
$resultPath = Join-Path $resolvedOutput 'result.txt'
Remove-Item -LiteralPath $errorPath, $resultPath -Force -ErrorAction SilentlyContinue
$ruby = @"
begin
Dir.chdir('$($resolvedGameRoot.Replace('\', '/'))')
$loader
IronmonScriptLoader.load_directory('Data/Scripts', [/\A(?:998|999)/])
GameData.load_all
Graphics.resize_screen(Settings::SCREEN_WIDTH, Settings::SCREEN_HEIGHT)
`$ironmon_fusion_fixture_path = '$fixture'
`$PokemonSystem = PokemonSystem.new
`$game_system = Game_System.new
`$game_switches = Game_Switches.new
`$game_variables = Game_Variables.new
`$game_temp = Game_Temp.new
`$PokemonGlobal = PokemonGlobalMetadata.new
load '$test'
`$PokemonGlobal.ironmon_mode = true
`$PokemonGlobal.ironmon_seed = 1279932942
Ironmon.prepare_base_stat_randomization
Ironmon.prepare_ability_randomization
first = Pokemon.new(75744, 30)
second = Pokemon.new(:GALLADE, 30)
first.personalID = 21
second.personalID = 42
first.original_body = Pokemon.new(:LAPRAS, 30)
first.nature = :ADAMANT
second.nature = :MODEST
first.moves = [:TACKLE, :GROWL, :WATERGUN, :CONFUSION].map { |id| Pokemon::Move.new(id) }
second.moves = [:SLASH, :LEER, :PSYBEAM, :SWORDSDANCE].map { |id| Pokemon::Move.new(id) }
Ironmon.apply_normal_fusion_instance_data(first, second, false)
def save_fusion_preview(scene, filename, row = 0, column = 0)
  scene.pbStartScene
  sprites = scene.instance_variable_get(:@sprites)
  sprites["option"].index = row
  sprites["option"][row] = column
  sprites["option"].instance_variable_set(:@selected_position, column)
  scene.updateDescription(row)
  Graphics.update
  bitmap = Graphics.snap_to_bitmap
  bitmap.save_to_png('$($resolvedOutput.Replace('\', '/'))/' + filename)
  bitmap.dispose
ensure
  scene.pbEndScene
end
save_fusion_preview(FusionMovesOptionsScene.new(first, second), 'fusion-current-stat.png')
save_fusion_preview(FusionMovesOptionsScene.new(first, second), 'fusion-current-move-details.png', 1)
rows = JSON.parse(File.binread('$fixture'))
row = rows.reverse.find do |entry|
  (entry["label"] || entry[:label]) == 4
end
`$PokemonGlobal.ironmon_seed = row["seed"] || row[:seed]
Ironmon.reset_base_stat_generator_cache
first.species = row["resultId"] || row[:resultId]
longest = []
GameData::Move.each { |move| longest << move }
longest = longest.sort_by { |move| -move.name.length }.uniq { |move| move.name }.first(8)
first.moves = longest.first(4).map { |move| Pokemon::Move.new(move.id) }
second.moves = longest.last(4).map { |move| Pokemon::Move.new(move.id) }
save_fusion_preview(FusionMovesOptionsScene.new(first, second), 'fusion-current-long-content.png')
File.binwrite('$($resultPath.Replace('\', '/'))', 'Fusion stat, clue, profile and native rendering checks passed.')
exit! 0
rescue Exception => error
File.binwrite('$($errorPath.Replace('\', '/'))', "#{error.class}: #{error.message}\n#{error.backtrace.join("\n")}")
exit! 1
end
"@
Invoke-IronmonGameRuntime -GameRoot $resolvedGameRoot -RubySource $ruby -TimeoutSeconds 90 -OperationName 'fusion stat and clue validation' -ErrorReportPath $errorPath
if (-not (Test-Path -LiteralPath $resultPath)) {
    throw 'The bundled runtime did not finish fusion validation.'
}
Get-Content -LiteralPath $resultPath
Write-Output "Native clue screenshot: $(Join-Path $resolvedOutput 'fusion-current-stat.png')"
