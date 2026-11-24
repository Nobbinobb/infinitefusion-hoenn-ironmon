# Runs only inside Infinite Fusion's native runtime through mkxp's customScript.
source_root = ENV.fetch("IRONMON_SMOKE_SOURCE_ROOT").tr("\\", "/")
output_root = ENV.fetch("IRONMON_SMOKE_OUTPUT_ROOT").tr("\\", "/")
$ironmon_smoke_output_root = output_root
$stdout.sync = true
$stderr.sync = true
cases = []
started = Process.clock_gettime(Process::CLOCK_MONOTONIC)

begin
  module System
    def self.data_directory
      return File.join($ironmon_smoke_output_root, "userdata")
    end
  end
  Dir.mkdir(System.data_directory) if !Dir.exist?(System.data_directory)
  load File.join(source_root, "tools/generation/Script-Loader.rb")
  IronmonScriptLoader.load_directory("Data/Scripts", [/\A(?:997|998|999)/])
  GameData.load_all
  IronmonScriptLoader.load_manifest(
    File.join(source_root, "src"),
    File.join(source_root, "src/load_order.json"),
    false
  )
  raise "The native graphics/input runtime is missing" if !defined?(Graphics) || !defined?(Input)
  raise "The selected Hoenn release is not supported" if !Ironmon.compatible?
  raise "Compiled species data is empty" if GameData::Species.keys.empty?
  raise "Hoenn boss trainer recognition failed" if !Ironmon.gym_leader_types.include?(:LEADER_Wattson) ||
    !Ironmon.boss_trainer_types.include?(:RIVAL2)
  land_encounters_supported = [:Land4, :Puddle].all? do |encounter_type|
    GameData::EncounterType.exists?(encounter_type) && Ironmon.overworld_encounter_environment?(encounter_type)
  end
  raise "Hoenn land encounter recognition failed" if !land_encounters_supported
  raise "Startup preparation cannot reach native input" if
    Ironmon::StartupPreparation.native_input_update.owner != Input.singleton_class
  raise "The current battle Run hook was not loaded" if !Ironmon.respond_to?(:battle_cancel_run_hotkey?)
  cases << { "name" => "source startup", "status" => "passed",
    "seconds" => Process.clock_gettime(Process::CLOCK_MONOTONIC) - started }

  $ironmon_deterministic_hashing_test_output_path = File.join(output_root, "hashing.txt")
  $ironmon_battle_run_hotkey_test_output_path = File.join(output_root, "battle-run.txt")
  $ironmon_feature_settings_result = File.join(output_root, "features.txt")
  [
    ["collection startup", "Collection-Startup.rb"],
    ["deterministic hashing", "Deterministic-Hashing.rb"],
    ["UTF-8 tracker text", "Tracker-JSON.rb"],
    ["battle Run shortcut", "Battle-Run-Hotkey.rb"],
    ["automatic battle party", "Battle-Party-Selection.rb"],
    ["feature settings", "Feature-Settings.rb"]
  ].each do |name, file|
    case_started = Process.clock_gettime(Process::CLOCK_MONOTONIC)
    load File.join(source_root, "tests/runtime", file)
    cases << { "name" => name, "status" => "passed",
      "seconds" => Process.clock_gettime(Process::CLOCK_MONOTONIC) - case_started }
    puts "PASS: #{name}"
  end

  result = { "status" => "passed", "cases" => cases,
    "ruby_version" => RUBY_VERSION, "ruby_platform" => RUBY_PLATFORM,
    "seconds" => Process.clock_gettime(Process::CLOCK_MONOTONIC) - started }
  File.binwrite(File.join(output_root, "result.json"), JSON.generate(result))
  exit!(0)
rescue Exception => error
  failure = { "status" => "failed", "cases" => cases,
    "error" => "#{error.class}: #{error.message}", "backtrace" => error.backtrace }
  text = defined?(JSON) ? JSON.generate(failure) : failure.inspect
  File.binwrite(File.join(output_root, "result.json"), text)
  $stderr.puts text
  exit!(1)
end
