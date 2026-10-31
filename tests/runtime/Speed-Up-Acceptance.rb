# Runs the current canonical scripts in an owned bundled-game process.
source_root = ENV.fetch("IRONMON_SMOKE_SOURCE_ROOT").tr("\\", "/")
output_root = ENV.fetch("IRONMON_SMOKE_OUTPUT_ROOT").tr("\\", "/")
$ironmon_speed_up_acceptance_output = output_root
$ironmon_speed_up_output = output_root
$ironmon_move_learning_protection_output = output_root
$ironmon_controls_test_output_path = File.join(output_root, "controls.txt")
$ironmon_run_transition_test_output_path = File.join(output_root, "run-transitions.txt")
$stdout.sync = true
$stderr.sync = true
cases = []
started = Process.clock_gettime(Process::CLOCK_MONOTONIC)

begin
  module System
    def self.data_directory
      return File.join($ironmon_speed_up_acceptance_output, "userdata")
    end
  end
  Dir.mkdir(System.data_directory) if !Dir.exist?(System.data_directory)
  load File.join(source_root, "tools/generation/Script-Loader.rb")
  IronmonScriptLoader.load_directory("Data/Scripts", [/\A(?:997|998|999)/])
  GameData.load_all
  IronmonScriptLoader.load_manifest(File.join(source_root, "src"), File.join(source_root, "src/load_order.json"), false)

  Ironmon::TrackerConnection.class_eval do
    def update
      return
    end
  end
  module Ironmon
    def self.wait_for_tracker_session
      return
    end
  end
  module Game
    def self.save(*_args, **_kwargs)
      raise "Saving is disabled in speed-up acceptance"
    end
  end

  SaveData.initialize_bootup_values
  Game.initialize
  SaveData.load_new_game_values
  $MapFactory = PokemonMapFactory.new(10)
  Graphics.resize_screen(Settings::SCREEN_WIDTH, Settings::SCREEN_HEIGHT)
  groups = {
    "profiles" => "Speed-Up.rb",
    "protection" => "Move-Learning-Protection.rb",
    "input" => "Responsive-Input.rb",
    "controls" => "Controls.rb",
    "clock" => "Speed-Clock.rb",
    "interaction" => "Speed-Up-Interaction.rb",
    "pacing" => "Speed-Up-Pacing.rb",
    "stage_transitions" => "Speed-Stage-Transitions.rb",
    "calculation_pacing" => "Calculation-Pacing.rb",
    "player_fusion_pacing" => "Player-Fusion-Pacing.rb",
    "startup_preparation" => "Startup-Preparation.rb",
    "startup_transport" => "Startup-Transport.rb",
    "run_transitions" => "Run-Transitions.rb",
    "transitions" => "Speed-Transitions.rb",
    "sky_drop" => "Sky-Drop.rb",
    "switch_out_moves" => "Switch-Out-Moves.rb",
    "loss" => "Speed-Up-Loss.rb",
    "performance" => "Gameplay-Performance.rb",
    "json" => "Tracker-JSON-Performance.rb",
    "responsive" => "Responsive-Speed-Up.rb"
  }
  requested = ENV.fetch("IRONMON_SPEED_UP_GROUPS", groups.keys.join(",")).split(",").map(&:strip)
  raise "Unknown speed-up acceptance group" if requested.empty? || (requested - groups.keys).any?
  requested.each do |name|
    case_started = Process.clock_gettime(Process::CLOCK_MONOTONIC)
    puts "RUN: #{name}"
    load File.join(source_root, "tests/runtime", groups.fetch(name))
    cases << { "name" => name, "status" => "passed",
      "seconds" => Process.clock_gettime(Process::CLOCK_MONOTONIC) - case_started }
    puts "PASS: #{name}"
  end

  result = { "status" => "passed", "cases" => cases,
    "ruby_version" => RUBY_VERSION, "ruby_platform" => RUBY_PLATFORM,
    "seconds" => Process.clock_gettime(Process::CLOCK_MONOTONIC) - started }
  File.binwrite(File.join(output_root, "speed-up-acceptance.json"), JSON.generate(result))
  exit!(0)
rescue Exception => error
  failure = { "status" => "failed", "cases" => cases,
    "error" => "#{error.class}: #{error.message}", "backtrace" => error.backtrace }
  report = defined?(JSON) ? JSON.generate(failure) : failure.inspect
  File.binwrite(File.join(output_root, "speed-up-acceptance.json"), report)
  $stderr.puts report
  exit!(1)
end
