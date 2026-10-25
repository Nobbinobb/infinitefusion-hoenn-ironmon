source_root = ENV.fetch("IRONMON_SMOKE_SOURCE_ROOT").tr("\\", "/")
output_root = ENV.fetch("IRONMON_SMOKE_OUTPUT_ROOT").tr("\\", "/")
$ironmon_starter_rival_output = output_root
begin
  module System
    def self.data_directory
      return File.join($ironmon_starter_rival_output, "userdata")
    end
  end
  Dir.mkdir(System.data_directory) if !Dir.exist?(System.data_directory)
  load File.join(source_root, "tools/generation/Script-Loader.rb")
  IronmonScriptLoader.load_directory("Data/Scripts", [/\A(?:997|998|999)/])
  GameData.load_all
  IronmonScriptLoader.load_manifest(File.join(source_root, "src"), File.join(source_root, "src/load_order.json"), false)
  Ironmon.send(:remove_const, :TRACKER_PORT)
  Ironmon.const_set(:TRACKER_PORT, 0)
  $data_system = load_data("Data/System.rxdata")
  $data_tilesets = load_data("Data/Tilesets.rxdata")
  $data_common_events = load_data("Data/CommonEvents.rxdata")
  transfers = [5, 18].flat_map do |map_id|
    load_data(sprintf("Data/Map%03d.rxdata", map_id)).events.values.flat_map do |event|
      event.pages.each_with_index.flat_map do |page, index|
        page.list.select { |command| command.code == 201 }.map { |command| [map_id, event.id, index, command.parameters] }
      end
    end
  end
  File.binwrite(File.join(output_root, "lab-entry.txt"), transfers.map(&:inspect).join("\n"))
  $PokemonTemp = PokemonTemp.new
  SaveData.initialize_bootup_values
  SaveData.load_new_game_values
  Game.load_custom_sprites_list_cache
  initialize_alt_sprite_substitutions
  $PokemonGlobal.autogen_sprites_cache = {}
  pbSet(VAR_TRAINER_GENDER, GENDER_MALE)
  $PokemonGlobal.ironmon_mode = true
  $PokemonGlobal.ironmon_seed = 123456
  $PokemonGlobal.ironmon_configuration = Ironmon::Configuration.new
  Ironmon.activate_run_generation_profile(Ironmon.current_generation_profile_id)
  original_tracker_ready = Ironmon.method(:tracker_ready?)
  def Ironmon.tracker_ready?; true; end
  load File.join(source_root, "tests/runtime/Starter-Rival.rb")
  regression_mode = ENV["IRONMON_RIVAL_REGRESSIONS"]
  if ["1", "management"].include?(regression_mode)
    $ironmon_early_game_test_output_path = File.join(output_root, "early-game-result.txt")
    $ironmon_trainer_battle_test_output_path = File.join(output_root, "trainer-battle-result.txt")
    $ironmon_seeded_run_import_test_output_path = File.join(output_root, "seed-import-result.txt")
    $ironmon_game_management_test_output_path = File.join(output_root, "game-management-result.txt")
    $ironmon_game_management_test_save_root = File.join(output_root, "management-saves-#{Process.pid}-#{Time.now.to_i}")
    files = regression_mode == "management" ? ["Game-Management.rb"] :
      ["Early-Game.rb", "Trainer-Battles.rb", "Seeded-Run-Import.rb", "Game-Management.rb"]
    files.each do |name|
      if name == "Game-Management.rb"
        Ironmon.define_singleton_method(:tracker_ready?, original_tracker_ready)
        pinned_pool = Ironmon::ProfileCustomFusionPool.new(Ironmon.generation_profile_component_path("custom_fusion_pool"))
        Ironmon.instance_variable_set(:@custom_fusion_pool_service, pinned_pool)
      end
      load File.join(source_root, "tests/runtime", name)
    end
    File.binwrite(File.join(output_root, "starter-rival-result.txt"), "Passed regression groups: #{files.join(", ")}.\n")
  else
    IronmonStarterRivalTests.run(output_root)
  end
  exit!(0)
rescue Exception => error
  File.binwrite(File.join(output_root, "starter-rival-result.txt"), "#{error.class}: #{error.message}\n#{error.backtrace.join("\n")}")
  exit!(1)
end
