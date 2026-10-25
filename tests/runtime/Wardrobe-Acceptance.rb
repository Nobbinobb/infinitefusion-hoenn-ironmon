source_root = ENV.fetch("IRONMON_SMOKE_SOURCE_ROOT").tr("\\", "/")
output_root = ENV.fetch("IRONMON_SMOKE_OUTPUT_ROOT").tr("\\", "/")
$ironmon_wardrobe_output = output_root
begin
  module System
    def self.data_directory
      return File.join($ironmon_wardrobe_output, "userdata")
    end
  end
  Dir.mkdir(System.data_directory)
  load File.join(source_root, "tools/generation/Script-Loader.rb")
  IronmonScriptLoader.load_directory("Data/Scripts", [/\A(?:997|998|999)/])
  GameData.load_all
  IronmonScriptLoader.load_manifest(File.join(source_root, "src"), File.join(source_root, "src/load_order.json"), false)
  Ironmon.send(:remove_const, :TRACKER_PORT)
  Ironmon.const_set(:TRACKER_PORT, ENV.fetch("IRONMON_WARDROBE_TRACKER_PORT").to_i)
  $data_system = load_data("Data/System.rxdata")
  $PokemonTemp = PokemonTemp.new
  SaveData.initialize_bootup_values
  SaveData.load_new_game_values
  $PokemonGlobal.ironmon_mode = true
  $PokemonGlobal.ironmon_checkpoint_id = 345
  $PokemonGlobal.ironmon_run_ledger = Ironmon.default_run_ledger
  $PokemonGlobal.ironmon_run_ledger["current_attempt"] = { "result" => "active", "attempt_number" => 1, "seed" => 44, "active_seconds" => 0.0, "statistics" => { "starting_badges" => 0 } }
  $Trainer = Player.new("Wardrobe acceptance", :POKEMONTRAINER_Red)
  $Trainer.save_slot = "File A"
  $ironmon_cosmetic_test_root = output_root
  load File.join(source_root, "tests/runtime/Cosmetics.rb")
  IronmonCosmeticRuntimeTests.run
  collection_root = File.join(output_root, "collection")
  Dir.mkdir(collection_root)
  saved_globals = [$Trainer, $PokemonGlobal, $PokemonSystem, $PokemonTemp, $game_temp]
  load File.join(source_root, "tests/runtime/Collection-Wardrobe.rb")
  IronmonCollectionWardrobeRuntimeTests.run(collection_root)
  $Trainer, $PokemonGlobal, $PokemonSystem, $PokemonTemp, $game_temp = saved_globals
  c = Ironmon::Cosmetics
  # No generated battle world is needed to exercise cosmetics and the real transport.
  def Ironmon.tracker_current_state
    return { "ironmon_active" => true, "run_id" => ensure_tracker_run_id, "battle_id" => nil, "sequence" => 0 }
  end
  paused_seconds = {}
  Ironmon::TrackerConnection.prepend(Module.new do
    define_method(:disconnect) do |error = nil|
      File.open(File.join($ironmon_wardrobe_output, "disconnects.txt"), "a") { |file| file.puts("#{error.inspect}\n#{error ? error.backtrace.join("\n") : caller.first(3).join("\n")}") }
      super(error)
    end
    define_method(:send_event) do |event, payload, *arguments|
      marker = File.join($ironmon_wardrobe_output, "skip-reward-ack.request")
      if event == "wardrobe_achievement_rewards_acknowledged" && File.file?(marker)
        File.delete(marker)
        true
      else
        super(event, payload, *arguments)
      end
    end
    define_method(:update) do |*arguments|
      if Ironmon::Cosmetics.active_wardrobe
        id = Ironmon::Cosmetics.active_wardrobe.wardrobe_id
        seconds = Ironmon.current_run_attempt["active_seconds"]
        raise "Wardrobe advanced the attempt timer" if paused_seconds.has_key?(id) && paused_seconds[id] != seconds
        paused_seconds[id] = seconds
      end
      result = super(*arguments)
      poll = File.join($ironmon_wardrobe_output, "achievement-poll.request")
      if File.file?(poll)
        File.delete(poll)
        send_event("achievement_poll", {})
      end
      marker = File.join($ironmon_wardrobe_output, "disconnect.request")
      if File.file?(marker)
        File.delete(marker)
        disconnect
      end
      result
    end
  end)
  c.profile_directory = File.join(output_root, "shared")
  c.apply(IronmonCosmeticRuntimeTests.appearance(c.catalog))
  c.profile.award("acceptance", "trainer:funds", 3000)
  gameplay_before = Marshal.dump([$Trainer.party, $Trainer.money, $PokemonBag, $game_switches, $game_variables])
  raise "A wardrobe was opened without a game entry" if c.wardrobe_snapshot["open"]
  begin
    c.wardrobe_request({ "action" => "open" })
    raise "Tracker opened a wardrobe"
  rescue RuntimeError => error
    raise if !error.message.include?("Open the wardrobe in the game first")
  end
  raise "Free look did not confirm" if !c.open_wardrobe
  original = c.capture
  raise "Initial claim did not persist" if !c.profile.read["initial_claimed"]
  raise "Cancel returned confirmation" if c.open_wardrobe
  raise "Cancel equipped a preview" if c.capture != original
  raise "Apply did not confirm" if !c.open_wardrobe
  raise "Accepted outfit was not equipped" if c.capture != c.profile.read["appearances"]["slot:File A"]
  raise "Wardrobe altered non-cosmetic gameplay state" if Marshal.dump([$Trainer.party, $Trainer.money, $PokemonBag, $game_switches, $game_variables]) != gameplay_before
  original = c.capture
  screen = c::WardrobeScreen.new(c.catalog, c.profile, "slot:File A")
  # Exercise the physical native Back path, independent of tracker forwarded controls.
  input = Module.new do
    define_method(:trigger?) do |button|
      return true if button == Input::BACK && Ironmon::Cosmetics.active_wardrobe
      super(button)
    end
  end
  Input.singleton_class.prepend(input)
  raise "Physical Back did not cancel" if screen.run
  raise "Physical Back changed appearance" if c.capture != original
  broken = c::Profile.new(File.join(output_root, "broken"))
  broken_screen = c::WardrobeScreen.new(c.catalog, broken, "slot:File A")
  File.binwrite(File.join(broken.directory, "profile-a.json"), "damaged")
  begin
    broken_screen.run
    raise "A damaged profile was silently reset"
  rescue RuntimeError => error
    raise if !error.message.include?("JSON") && !error.message.include?("damaged")
  end
  raise "Failed wardrobe left an active session" if c.active_wardrobe
  raise "Failed wardrobe leaked its viewport" if !broken_screen.instance_variable_get(:@viewport).disposed?
  10.times { Graphics.update; Input.update }
  File.binwrite(File.join(output_root, "wardrobe-result.txt"), "Passed native wardrobe transactions, previews, tracker round trips and physical Back.\n")
  exit!(0)
rescue Exception => error
  File.binwrite(File.join(output_root, "wardrobe-result.txt"), "#{error.class}: #{error.message}\n#{error.backtrace.join("\n")}")
  exit!(1)
end
