source_root = ENV.fetch("IRONMON_SMOKE_SOURCE_ROOT").tr("\\", "/")
output_root = ENV.fetch("IRONMON_SMOKE_OUTPUT_ROOT").tr("\\", "/")
$ironmon_story_lookup_output = output_root
begin
  module System
    def self.data_directory
      return File.join($ironmon_story_lookup_output, "userdata")
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
  $data_common_events = load_data("Data/CommonEvents.rxdata")
  $PokemonTemp = PokemonTemp.new
  SaveData.initialize_bootup_values
  SaveData.load_new_game_values
  Game.load_custom_sprites_list_cache
  initialize_alt_sprite_substitutions
  Ironmon.activate_run_generation_profile(Ironmon.current_generation_profile_id)
  load File.join(source_root, "tests/runtime/Story-Trainers.rb")
  IronmonStoryTrainerRuntimeTests.test_live_fusion_lookup(output_root)
  $ironmon_area_progress_test_output_path = File.join(output_root, "area-progress.txt")
  load File.join(source_root, "tests/runtime/Area-Progress.rb")
  File.binwrite(File.join(output_root, "result.txt"), "Passed story trainer live/archive lookup and area-progress regressions.\n")
  exit!(0)
rescue Exception => error
  File.binwrite(File.join(output_root, "result.txt"), "#{error.class}: #{error.message}\n#{error.backtrace.join("\n")}")
  exit!(1)
end
